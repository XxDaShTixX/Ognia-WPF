namespace Ognia.Wpf.TrackSources;

public class YouTubeSourceUrlProvider : ITrackSourceUrlProvider
{
    public string SourceName => "youtube";

    public string? BuildUrl(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return null;
        return $"https://www.youtube.com/watch?v={sourceId}";
    }
}