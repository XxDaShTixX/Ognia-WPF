using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Track;
using YoutubeExplode;
using YoutubeExplode.Search;

namespace Ognia.Wpf.Local;

/// <summary>
/// Client-side YouTube search. Fetches a bounded window of results and
/// returns them as raw TrackResponse items — pagination is the caller's concern.
/// </summary>
public class YouTubeSearch
{
    private readonly YoutubeClient _client = new();

    public string SourceName => "youtube";

    /// <summary>
    /// Returns up to <paramref name="limit"/> results starting at <paramref name="offset"/>.
    /// Uses YouTubeExplode's cursor-based enumeration so repeated calls with growing
    /// offsets resume rather than refetch from scratch.
    /// </summary>
    public async Task<IReadOnlyList<TrackResponse>> SearchAsync(
        string query,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<TrackResponse>();
        if (limit <= 0) return Array.Empty<TrackResponse>();

        var results = new List<TrackResponse>(limit);
        var index = 0;

        await foreach (var result in _client.Search.GetResultsAsync(query, ct))
        {
            if (result is not VideoSearchResult v) continue;
            if (string.IsNullOrWhiteSpace(v.Id.Value)) continue;

            if (index++ < offset) continue;

            results.Add(new TrackResponse
            {
                Id = 0,
                SourceName = SourceName,
                SourceId = v.Id.Value,
                Name = v.Title,
                ThumbnailUrl = v.Thumbnails
                    .OrderByDescending(t => t.Resolution.Width)
                    .Select(t => t.Url)
                    .FirstOrDefault() ?? string.Empty,
                Duration = v.Duration ?? TimeSpan.Zero,
                Position = 0
            });

            if (results.Count >= limit) break;
        }

        // Download thumbnails in parallel for this page only.
        var downloads = results.Select(async track =>
        {
            if (string.IsNullOrWhiteSpace(track.ThumbnailUrl)) return;
            var localPath = await ThumbnailCache.GetTempAsync(
                track.ThumbnailUrl, track.SourceName, track.SourceId, ct);
            if (localPath != null)
                track.ThumbnailUrl = localPath;
        });

        await Task.WhenAll(downloads);

        return results;
    }
}