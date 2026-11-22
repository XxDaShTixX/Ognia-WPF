using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Local;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;
using Ognia.Wpf.Repos;

namespace Ognia.Wpf.Services;

public class LibraryUploader
{
    private readonly IMusicRepo _repo;

    public LibraryUploader(IMusicRepo repo) => _repo = repo;

    /// <summary>
    /// Returns (playlistsUploaded, urlsAdded, urlsSkipped, invalidUrls, error).
    /// Playlists are matched by name (case-insensitive) against existing server playlists;
    /// matching ones are reused, not duplicated. Counts are per input-URL — matches the
    /// server's per-URL bucketing. Metadata (name/duration/thumbnail) is sent per item so
    /// the server can skip the per-track YouTube metadata fetch on import.
    /// </summary>
    public async Task<(int PlaylistsUploaded, int UrlsAdded, int UrlsSkipped, List<string> InvalidUrls, string? Error)>
        UploadAsync(LocalLibraryFileModel library, IProgress<string>? progress = null)
    {
        var playlistsUploaded = 0;
        var urlsAdded = 0;
        var urlsSkipped = 0;
        var invalidUrls = new List<string>();

        // Snapshot existing playlists so re-imports reuse them instead of duplicating.
        var existingResult = await _repo.GetPlaylistsAsync();
        if (!existingResult.Success || existingResult.Data == null)
            return (0, 0, 0, invalidUrls,
                $"Failed to load existing playlists: {existingResult.Error}");

        var existingByName = existingResult.Data
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        foreach (var localPlaylist in library.Playlists)
        {
            int serverPlaylistId;

            if (existingByName.TryGetValue(localPlaylist.Name, out var existingId))
            {
                progress?.Report($"Reusing existing playlist '{localPlaylist.Name}'…");
                serverPlaylistId = existingId;
            }
            else
            {
                progress?.Report($"Creating playlist '{localPlaylist.Name}'…");

                var create = await _repo.CreatePlaylistAsync(
                    new CreatePlaylistRequest { Name = localPlaylist.Name });

                if (!create.Success || create.Data == null)
                    return (playlistsUploaded, urlsAdded, urlsSkipped, invalidUrls,
                        $"Failed to create playlist '{localPlaylist.Name}': {create.Error}");

                playlistsUploaded++;
                serverPlaylistId = create.Data.Id;
                existingByName[localPlaylist.Name] = serverPlaylistId;
            }

            var items = new List<BulkAddTrackItem>();
            foreach (var link in localPlaylist.Tracks.OrderBy(t => t.Position))
            {
                var track = library.Tracks.FirstOrDefault(t => t.Id == link.TrackId);
                if (track == null) continue;

                var url = TrackSourceUrlResolver.BuildUrl(track.SourceName, track.SourceId);
                if (url == null) { urlsSkipped++; continue; }

                items.Add(new BulkAddTrackItem
                {
                    Url = url,
                    Name = string.IsNullOrWhiteSpace(track.Name) ? null : track.Name,
                    Duration = track.Duration > TimeSpan.Zero ? FormatDuration(track.Duration) : null,
                    ThumbnailUrl = string.IsNullOrWhiteSpace(track.ThumbnailUrl) ? null : track.ThumbnailUrl
                });
            }

            if (items.Count == 0) continue;

            progress?.Report($"Adding {items.Count} track(s) to '{localPlaylist.Name}'…");

            var bulk = await _repo.AddTracksBulkAsync(serverPlaylistId, items);
            if (!bulk.Success)
                return (playlistsUploaded, urlsAdded, urlsSkipped, invalidUrls,
                    $"Failed to add tracks to '{localPlaylist.Name}': {bulk.Error}");

            urlsAdded += bulk.Added.Count;
            urlsSkipped += bulk.Skipped.Count;
            invalidUrls.AddRange(bulk.Invalid);
        }

        return (playlistsUploaded, urlsAdded, urlsSkipped, invalidUrls, null);
    }

    /// <summary>
    /// Formats as "hh:mm:ss" or "d.hh:mm:ss" for >=24h — matches what
    /// System.Text.Json expects for TimeSpan on the server.
    /// </summary>
    private static string FormatDuration(TimeSpan d)
        => d.TotalHours >= 24
            ? $"{(int)d.TotalDays}.{d.Hours:00}:{d.Minutes:00}:{d.Seconds:00}"
            : $"{(int)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}";
}