namespace Ognia.Wpf.Models.Track;

public class BulkAddTracksRequest
{
    public List<BulkAddTrackItem> Urls { get; set; } = new();
}

public class BulkAddTrackItem
{
    public string Url { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Duration { get; set; }   // "hh:mm:ss" or "d.hh:mm:ss"
    public string? ThumbnailUrl { get; set; }
}