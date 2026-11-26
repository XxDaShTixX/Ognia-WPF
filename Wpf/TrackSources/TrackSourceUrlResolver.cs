namespace Ognia.Wpf.TrackSources;

public static class TrackSourceUrlResolver
{
    private static readonly ITrackSourceUrlProvider[] Providers =
    {
        new YouTubeSourceUrlProvider(),
    };

    private static readonly Dictionary<string, ITrackSourceUrlProvider> ByName =
        Providers.ToDictionary(p => p.SourceName, StringComparer.OrdinalIgnoreCase);

    public static string? BuildUrl(string sourceName, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(sourceId))
            return null;

        return ByName.TryGetValue(sourceName, out var provider)
            ? provider.BuildUrl(sourceId)
            : null;
    }
}