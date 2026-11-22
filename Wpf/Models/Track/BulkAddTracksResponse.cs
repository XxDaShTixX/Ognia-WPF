namespace Ognia.Wpf.Models.Track;

public class BulkAddTracksResponse
{
    public List<string> Added { get; set; } = new();
    public List<string> Skipped { get; set; } = new();
    public List<string> Invalid { get; set; } = new();
}