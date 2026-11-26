using Ognia.Wpf.Models.Local;
using Ognia.Wpf.Repos;

namespace Ognia.Wpf.Library;

/// <summary>
/// Reads the full library through IMusicRepo and produces a LocalLibraryFileModel
/// suitable for writing to disk. Server ids are discarded; playlists and tracks are
/// renumbered 1..N so the on-disk shape matches what LocalLibraryStore.ImportFrom expects.
/// Tracks are deduplicated by (SourceName, SourceId).
/// </summary>
public sealed class LibraryExporter
{
    private readonly IMusicRepo _repo;

    public LibraryExporter(IMusicRepo repo) => _repo = repo;

    public async Task<(bool Success, string? Error, LocalLibraryFileModel? Data)> BuildAsync(
        IProgress<string>? progress = null)
    {
        var playlistsResult = await _repo.GetPlaylistsAsync();
        if (!playlistsResult.Success || playlistsResult.Data == null)
            return (false, playlistsResult.Error ?? "Failed to load playlists.", null);

        var file = new LocalLibraryFileModel { SchemaVersion = 1 };

        var trackKeyToLocalId = new Dictionary<(string, string), int>();
        var nextTrackId = 1;

        foreach (var summary in playlistsResult.Data)
        {
            progress?.Report($"Exporting '{summary.Name}'…");

            var detailResult = await _repo.GetPlaylistDetailAsync(summary.Id);
            if (!detailResult.Success || detailResult.Data == null)
                return (false, $"Failed to load '{summary.Name}': {detailResult.Error}", null);

            var nextPlaylistId = file.Playlists.Count + 1;
            var localPlaylist = new LocalPlaylistModel
            {
                Id = nextPlaylistId,
                Name = detailResult.Data.Name,
                CreatedAt = detailResult.Data.CreatedAt == default
                    ? DateTime.UtcNow
                    : detailResult.Data.CreatedAt
            };

            var position = 0;
            foreach (var t in detailResult.Data.Tracks.OrderBy(x => x.Position))
            {
                var key = (t.SourceName, t.SourceId);
                if (!trackKeyToLocalId.TryGetValue(key, out var localTrackId))
                {
                    localTrackId = nextTrackId++;
                    trackKeyToLocalId[key] = localTrackId;
                    file.Tracks.Add(new LocalTrackModel
                    {
                        Id = localTrackId,
                        SourceName = t.SourceName,
                        SourceId = t.SourceId,
                        Name = t.Name,
                        ThumbnailUrl = t.ThumbnailUrl,
                        Duration = t.Duration
                    });
                }

                if (localPlaylist.Tracks.Any(pt => pt.TrackId == localTrackId))
                    continue;

                localPlaylist.Tracks.Add(new LocalPlaylistTrackModel
                {
                    TrackId = localTrackId,
                    Position = position++,
                    AddedAt = DateTime.UtcNow
                });
            }

            file.Playlists.Add(localPlaylist);
        }

        return (true, null, file);
    }
}