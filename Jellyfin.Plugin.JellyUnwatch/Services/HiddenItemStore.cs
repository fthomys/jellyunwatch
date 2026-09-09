using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyUnwatch.Services;

public class HiddenItemStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILogger<HiddenItemStore> _logger;
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, HiddenItem>> _entries = new();
    private readonly Lock _saveLock = new();
    private readonly Lock _loadLock = new();
    private bool _loaded;

    public HiddenItemStore(ILogger<HiddenItemStore> logger)
    {
        _logger = logger;
    }

    public bool IsEmpty
    {
        get
        {
            EnsureLoaded();
            return _entries.IsEmpty;
        }
    }

    public IReadOnlyList<HiddenItem> GetHidden(Guid userId)
    {
        EnsureLoaded();

        if (_entries.TryGetValue(userId, out var forUser))
        {
            return forUser.Values.OrderByDescending(x => x.HiddenAtUtc).ToArray();
        }

        return Array.Empty<HiddenItem>();
    }

    public IReadOnlyDictionary<Guid, IReadOnlyList<HiddenItem>> GetAll()
    {
        EnsureLoaded();

        return _entries.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<HiddenItem>)pair.Value.Values.OrderByDescending(x => x.HiddenAtUtc).ToArray());
    }

    public void Hide(Guid userId, Guid itemId, Guid? seriesId, string? name, HideScope scope, string reason)
    {
        if (userId.Equals(Guid.Empty) || itemId.Equals(Guid.Empty))
        {
            return;
        }

        EnsureLoaded();

        var forUser = _entries.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, HiddenItem>());

        forUser[itemId] = new HiddenItem
        {
            ItemId = itemId,
            SeriesId = seriesId,
            Name = name,
            Scope = scope,
            HiddenAtUtc = DateTime.UtcNow
        };

        _logger.LogInformation("Hid item {ItemId} for user {UserId} with scope {Scope}, source: {Reason}", itemId, userId, scope, reason);
        Save();
    }

    public bool Unhide(Guid userId, Guid itemId, string reason)
    {
        EnsureLoaded();

        if (!_entries.TryGetValue(userId, out var forUser))
        {
            return false;
        }

        if (!forUser.TryRemove(itemId, out _))
        {
            return false;
        }

        if (forUser.IsEmpty)
        {
            _entries.TryRemove(userId, out _);
        }

        _logger.LogInformation("Restored item {ItemId} for user {UserId}, source: {Reason}", itemId, userId, reason);
        Save();
        return true;
    }

    public int UnhideSeries(Guid userId, Guid seriesId, string reason)
    {
        EnsureLoaded();

        if (seriesId.Equals(Guid.Empty) || !_entries.TryGetValue(userId, out var forUser))
        {
            return 0;
        }

        var removed = 0;
        foreach (var entry in forUser.Values.Where(x => x.SeriesId.HasValue && x.SeriesId.Value.Equals(seriesId)))
        {
            if (forUser.TryRemove(entry.ItemId, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            if (forUser.IsEmpty)
            {
                _entries.TryRemove(userId, out _);
            }

            _logger.LogInformation("Restored {Count} entries of series {SeriesId} for user {UserId}, source: {Reason}", removed, seriesId, userId, reason);
            Save();
        }

        return removed;
    }

    public int Clear(Guid userId)
    {
        EnsureLoaded();

        if (!_entries.TryRemove(userId, out var forUser))
        {
            return 0;
        }

        Save();
        return forUser.Count;
    }

    public bool IsHiddenFromResume(Guid userId, Guid itemId, Guid? seriesId)
    {
        EnsureLoaded();

        if (!_entries.TryGetValue(userId, out var forUser))
        {
            return false;
        }

        if (forUser.ContainsKey(itemId))
        {
            return true;
        }

        if (!seriesId.HasValue || seriesId.Value.Equals(Guid.Empty))
        {
            return false;
        }

        return forUser.Values.Any(x => x.Scope == HideScope.Series
            && x.SeriesId.HasValue
            && x.SeriesId.Value.Equals(seriesId.Value));
    }

    public bool IsHiddenFromNextUp(Guid userId, Guid itemId, Guid? seriesId, bool hideWholeSeries)
    {
        EnsureLoaded();

        if (!_entries.TryGetValue(userId, out var forUser))
        {
            return false;
        }

        if (forUser.ContainsKey(itemId))
        {
            return true;
        }

        if (!hideWholeSeries || !seriesId.HasValue || seriesId.Value.Equals(Guid.Empty))
        {
            return false;
        }

        return forUser.Values.Any(x => x.SeriesId.HasValue && x.SeriesId.Value.Equals(seriesId.Value));
    }

    public bool HasSeries(Guid userId, Guid seriesId)
    {
        EnsureLoaded();

        return _entries.TryGetValue(userId, out var forUser)
            && forUser.Values.Any(x => x.SeriesId.HasValue && x.SeriesId.Value.Equals(seriesId));
    }

    private string? GetStorePath()
    {
        var dataFolder = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(dataFolder))
        {
            return null;
        }

        return Path.Combine(dataFolder, "hidden-items.json");
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_loadLock)
        {
            if (_loaded)
            {
                return;
            }

            Load();
            _loaded = Plugin.Instance is not null;
        }
    }

    private void Load()
    {
        var path = GetStorePath();
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var stored = JsonSerializer.Deserialize<Dictionary<Guid, List<HiddenItem>>>(json, SerializerOptions);
            if (stored is null)
            {
                return;
            }

            foreach (var pair in stored)
            {
                var forUser = new ConcurrentDictionary<Guid, HiddenItem>();
                foreach (var entry in pair.Value)
                {
                    forUser[entry.ItemId] = entry;
                }

                _entries.TryAdd(pair.Key, forUser);
            }

            _logger.LogInformation("Loaded hidden items for {Count} users", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load hidden items from {Path}", path);
        }
    }

    private void Save()
    {
        var path = GetStorePath();
        if (path is null)
        {
            return;
        }

        lock (_saveLock)
        {
            try
            {
                var snapshot = _entries.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Values.ToList());

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, SerializerOptions));
                File.Move(tempPath, path, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save hidden items to {Path}", path);
            }
        }
    }
}
