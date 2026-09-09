using System.Reflection;

namespace Jellyfin.Plugin.JellyUnwatch.Web;

public static class IndexHtmlTransformation
{
    public const string ScriptPath = "../JellyUnwatch/ClientScript.js";

    private static readonly string CacheKey = BuildCacheKey();

    public static string Transform(TransformationPayload payload)
    {
        var contents = payload.Contents ?? string.Empty;

        if (contents.Length == 0
            || contents.Contains(ScriptPath, StringComparison.Ordinal)
            || contents.IndexOf("<html", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return contents;
        }

        var tag = $"<script defer src=\"{ScriptPath}?v={CacheKey}\"></script>";

        var index = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

        return index < 0 ? contents : contents.Insert(index, tag);
    }

    private static string BuildCacheKey()
    {
        var version = Plugin.Instance?.Version.ToString() ?? "0";
        var location = Assembly.GetExecutingAssembly().Location;

        if (string.IsNullOrEmpty(location) || !File.Exists(location))
        {
            return version;
        }

        return $"{version}-{File.GetLastWriteTimeUtc(location).Ticks}";
    }
}
