using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.JellyUnwatch.Configuration;
using Jellyfin.Plugin.JellyUnwatch.Web;

namespace Jellyfin.Plugin.JellyUnwatch;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "JellyUnwatch";

    public override Guid Id => Guid.Parse("0340db8a-f7ee-4446-9661-051a80b16b67");

    public override string Description =>
        "Remove items from Continue Watching without losing playback progress.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var prefix = GetType().Namespace;

        return new[]
        {
            new PluginPageInfo
            {
                Name = "jellyunwatch",
                DisplayName = Name,
                EmbeddedResourcePath = $"{prefix}.Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "jellyunwatch.js",
                EmbeddedResourcePath = $"{prefix}.Configuration.configPage.js"
            }
        };
    }

    public override void OnUninstalling()
    {
        FileTransformationRegistrar.Unregister();
        base.OnUninstalling();
    }
}
