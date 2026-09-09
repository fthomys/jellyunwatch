namespace Jellyfin.Plugin.JellyUnwatch.Services;

public enum HideScope
{
    Item = 0,
    Series = 1
}

public class HiddenItem
{
    public Guid ItemId { get; set; }

    public Guid? SeriesId { get; set; }

    public string? Name { get; set; }

    public HideScope Scope { get; set; }

    public DateTime HiddenAtUtc { get; set; }
}
