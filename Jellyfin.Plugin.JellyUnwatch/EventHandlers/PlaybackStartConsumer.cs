using Jellyfin.Plugin.JellyUnwatch.Services;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyUnwatch.EventHandlers;

public class PlaybackStartConsumer : IEventConsumer<PlaybackStartEventArgs>
{
    private readonly HiddenItemStore _store;

    public PlaybackStartConsumer(HiddenItemStore store)
    {
        _store = store;
    }

    public Task OnEvent(PlaybackStartEventArgs eventArgs)
    {
        if (!(Plugin.Instance?.Configuration.AutoUnhideOnPlayback ?? true))
        {
            return Task.CompletedTask;
        }

        var item = eventArgs.Item;
        if (item is null || _store.IsEmpty)
        {
            return Task.CompletedTask;
        }

        var seriesId = ItemSeries.GetSeriesId(item);
        var reason = $"playback start, client {eventArgs.ClientName}, device {eventArgs.DeviceName}, automated {eventArgs.IsAutomated}";

        foreach (var user in eventArgs.Users)
        {
            _store.Unhide(user.Id, item.Id, reason);

            if (seriesId.HasValue)
            {
                _store.UnhideSeries(user.Id, seriesId.Value, reason);
            }
        }

        return Task.CompletedTask;
    }
}
