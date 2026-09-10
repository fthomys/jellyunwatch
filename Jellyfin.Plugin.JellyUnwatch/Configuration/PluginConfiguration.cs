using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyUnwatch.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool ClearResumePositionOnHide { get; set; }

    public bool HideSeriesFromNextUp { get; set; } = true;

    public bool AutoUnhideOnPlayback { get; set; } = true;

    public bool EnableWebButton { get; set; } = true;

    public bool ConfirmBeforeHiding { get; set; } = true;

    public bool AllowResetPlaybackProgress { get; set; } = true;
}
