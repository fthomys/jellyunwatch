using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JellyUnwatch.Services;

public static class ItemSeries
{
    public static Guid? GetSeriesId(BaseItem? item)
    {
        return item switch
        {
            Episode episode when !episode.SeriesId.Equals(Guid.Empty) => episode.SeriesId,
            Season season when !season.SeriesId.Equals(Guid.Empty) => season.SeriesId,
            Series series => series.Id,
            _ => null
        };
    }
}
