using Ognia.Wpf.Local;
using Ognia.Wpf.Models.Local;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;

namespace Ognia.Wpf.Repos;

/// <summary>
/// Serves music data from local JSON storage for guest mode.
/// Track operations are implemented in the next step; the playlist half is complete.
/// </summary>
public class LocalMusicRepo : IMusicRepo
{
    private readonly LocalLibraryStore _store = new();
    private readonly YouTubeUrlParser _youtube = new();

    #region Playlists

    public Task<(bool Success, string? Error, List<PlaylistResponse>? Data)> GetPlaylistsAsync()
    {
        var library = _store.Load();

        var playlists = library.Playlists
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PlaylistResponse
            {
                Id = p.Id,
                Name = p.Name,
                UserId = "local",
                CreatedAt = p.CreatedAt,
                PlaylistTracksCount = p.Tracks.Count,
                ThumbnailUrls = p.Tracks
                    .OrderBy(pt => pt.Position)
                    .Select(pt => library.Tracks.FirstOrDefault(t => t.Id == pt.TrackId)?.ThumbnailUrl ?? string.Empty)
                    .Take(4)
                    .ToList(),
                CoverThumbnails = p.Tracks
                    .OrderBy(pt => pt.Position)
                    .Select(pt => library.Tracks.FirstOrDefault(t => t.Id == pt.TrackId))
                    .Where(t => t != null)
                    .Select(t => new CoverThumbnail
                    {
                        Url = t!.ThumbnailUrl,
                        SourceName = t.SourceName,
                        SourceId = t.SourceId
                    })
                    .Take(4)
                    .ToList()
            })
            .ToList();

        return Task.FromResult<(bool, string?, List<PlaylistResponse>?)>((true, null, playlists));
    }

    public Task<(bool Success, string? Error, PlaylistResponse? Data)> CreatePlaylistAsync(CreatePlaylistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Task.FromResult<(bool, string?, PlaylistResponse?)>((false, "Name is required.", null));

        var library = _store.Load();

        var nextId = library.Playlists.Count == 0 ? 1 : library.Playlists.Max(p => p.Id) + 1;

        var playlist = new LocalPlaylistModel
        {
            Id = nextId,
            Name = request.Name.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        library.Playlists.Add(playlist);
        _store.Save(library);

        var response = new PlaylistResponse
        {
            Id = playlist.Id,
            Name = playlist.Name,
            UserId = "local",
            CreatedAt = playlist.CreatedAt,
            PlaylistTracksCount = 0,
            ThumbnailUrls = new(),
            CoverThumbnails = new()
        };

        return Task.FromResult<(bool, string?, PlaylistResponse?)>((true, null, response));
    }

    public Task<(bool Success, string? Error)> RenamePlaylistAsync(int id, RenamePlaylistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Task.FromResult<(bool, string?)>((false, "Name is required."));

        var library = _store.Load();
        var playlist = library.Playlists.FirstOrDefault(p => p.Id == id);

        if (playlist == null)
            return Task.FromResult<(bool, string?)>((false, "Playlist not found."));

        playlist.Name = request.Name.Trim();
        _store.Save(library);

        return Task.FromResult<(bool, string?)>((true, null));
    }

    public Task<(bool Success, string? Error)> DeletePlaylistAsync(int id)
    {
        var library = _store.Load();
        var playlist = library.Playlists.FirstOrDefault(p => p.Id == id);

        if (playlist == null)
            return Task.FromResult<(bool, string?)>((false, "Playlist not found."));

        // Collect track IDs before removing the playlist so we can prune orphans.
        var affectedTrackIds = playlist.Tracks.Select(t => t.TrackId).ToHashSet();

        library.Playlists.Remove(playlist);
        PruneOrphanTracks(library, affectedTrackIds);
        _store.Save(library);

        return Task.FromResult<(bool, string?)>((true, null));
    }

    public Task<(bool Success, string? Error, PlaylistDetailResponse? Data)> GetPlaylistDetailAsync(int id)
    {
        var library = _store.Load();
        var playlist = library.Playlists.FirstOrDefault(p => p.Id == id);

        if (playlist == null)
            return Task.FromResult<(bool, string?, PlaylistDetailResponse?)>((false, "Playlist not found.", null));

        var tracks = playlist.Tracks
            .OrderBy(pt => pt.Position)
            .Select(pt =>
            {
                var track = library.Tracks.FirstOrDefault(t => t.Id == pt.TrackId);
                if (track == null) return null;

                return new TrackResponse
                {
                    Id = track.Id,
                    SourceName = track.SourceName,
                    SourceId = track.SourceId,
                    Name = track.Name,
                    Duration = track.Duration,
                    ThumbnailUrl = track.ThumbnailUrl,
                    Position = pt.Position
                };
            })
            .Where(t => t != null)
            .Cast<TrackResponse>()
            .ToList();

        var response = new PlaylistDetailResponse
        {
            Id = playlist.Id,
            Name = playlist.Name,
            CreatedAt = playlist.CreatedAt,
            Tracks = tracks
        };

        return Task.FromResult<(bool, string?, PlaylistDetailResponse?)>((true, null, response));
    }

    #endregion

    #region Tracks

    public async Task<(bool Success, string? Error, int Added, int Skipped)> AddTracksAsync(
    int playlistId, string url, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "URL is required.", 0, 0);

        if (!_youtube.CanHandle(url))
            return (false, "Unsupported URL for any configured source.", 0, 0);

        List<string> urls;
        if (_youtube.IsPlaylistUrl(url))
        {
            try
            {
                var ids = await _youtube.FetchPlaylistVideoIdsAsync(url);
                if (ids.Count == 0)
                    return (false, "Playlist is empty or could not be read.", 0, 0);

                urls = ids.Select(id => $"https://www.youtube.com/watch?v={id}").ToList();
            }
            catch (Exception ex)
            {
                return (false, $"Failed to read playlist: {ex.Message}", 0, 0);
            }
        }
        else
        {
            urls = new List<string> { url };
        }

        var added = 0;
        var skipped = 0;
        for (var i = 0; i < urls.Count; i++)
        {
            if (urls.Count > 1)
                progress?.Report($"Adding {i + 1}/{urls.Count}…");

            var single = await AddSingleTrackAsync(playlistId, urls[i]);
            if (single.Success) added++; else skipped++;
        }

        return (true, null, added, skipped);
    }

    private async Task<(bool Success, string? Error, TrackResponse? Data)> AddSingleTrackAsync(
    int playlistId, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (false, "URL is required.", null);

        var library = _store.Load();
        var playlist = library.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist == null)
            return (false, "Playlist not found.", null);

        if (!_youtube.CanHandle(url))
            return (false, "Unsupported URL for any configured source.", null);

        var sourceId = _youtube.ExtractId(url);
        if (string.IsNullOrWhiteSpace(sourceId))
            return (false, "Could not extract a valid identifier from the URL.", null);

        var track = library.Tracks.FirstOrDefault(t =>
            t.SourceName == _youtube.SourceName && t.SourceId == sourceId);

        if (track == null)
        {
            TrackMetadataModel metadata;
            try
            {
                metadata = await _youtube.FetchMetadataAsync(sourceId);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to fetch track metadata: {ex.Message}", null);
            }

            var nextTrackId = library.Tracks.Count == 0
                ? 1
                : library.Tracks.Max(t => t.Id) + 1;

            track = new LocalTrackModel
            {
                Id = nextTrackId,
                SourceName = _youtube.SourceName,
                SourceId = sourceId,
                Name = metadata.Name,
                Duration = metadata.Duration,
                ThumbnailUrl = metadata.ThumbnailUrl
            };

            library.Tracks.Add(track);
        }

        if (playlist.Tracks.Any(pt => pt.TrackId == track.Id))
            return (false, "Track already in this playlist.", null);

        // Deduplicate the display name within this playlist.
        track.Name = GetUniqueTrackName(library, playlist, track.Name);

        var nextPosition = playlist.Tracks.Count == 0
            ? 0
            : playlist.Tracks.Max(pt => pt.Position) + 1;

        playlist.Tracks.Add(new LocalPlaylistTrackModel
        {
            TrackId = track.Id,
            Position = nextPosition,
            AddedAt = DateTime.UtcNow
        });

        _store.Save(library);

        return (true, null, new TrackResponse
        {
            Id = track.Id,
            SourceName = track.SourceName,
            SourceId = track.SourceId,
            Name = track.Name,
            Duration = track.Duration,
            ThumbnailUrl = track.ThumbnailUrl,
            Position = nextPosition
        });
    }

    public Task<(bool Success, string? Error)> RemoveTrackAsync(int playlistId, int trackId)
    {
        var library = _store.Load();
        var playlist = library.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist == null)
            return Task.FromResult<(bool, string?)>((false, "Playlist not found."));

        var link = playlist.Tracks.FirstOrDefault(pt => pt.TrackId == trackId);
        if (link == null)
            return Task.FromResult<(bool, string?)>((false, "Track not in playlist."));

        playlist.Tracks.Remove(link);
        PruneOrphanTracks(library, new HashSet<int> { trackId });
        _store.Save(library);

        return Task.FromResult<(bool, string?)>((true, null));
    }

    public Task<(bool Success, string? Error)> ReorderTracksAsync(int playlistId, List<int> orderedTrackIds)
    {
        var library = _store.Load();
        var playlist = library.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist == null)
            return Task.FromResult<(bool, string?)>((false, "Playlist not found."));

        if (playlist.Tracks.Count != orderedTrackIds.Count)
            return Task.FromResult<(bool, string?)>((false, "Track list does not match playlist contents."));

        var byTrackId = playlist.Tracks.ToDictionary(t => t.TrackId);
        for (var i = 0; i < orderedTrackIds.Count; i++)
        {
            if (!byTrackId.TryGetValue(orderedTrackIds[i], out var link))
                return Task.FromResult<(bool, string?)>((false, $"Track {orderedTrackIds[i]} is not in this playlist."));
            link.Position = i;
        }

        _store.Save(library);
        return Task.FromResult<(bool, string?)>((true, null));
    }

    public Task<(bool Success, string? Error)> RenameTrackAsync(int trackId, RenameTrackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Task.FromResult<(bool, string?)>((false, "Name is required."));

        var library = _store.Load();
        var track = library.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track == null)
            return Task.FromResult<(bool, string?)>((false, "Track not found."));

        var trimmed = request.Name.Trim();

        // Which playlists currently contain this track?
        var playlistIds = library.Playlists
            .Where(p => p.Tracks.Any(t => t.TrackId == trackId))
            .Select(p => p.Id)
            .ToHashSet();

        // Any *other* track in any of those playlists already using this name?
        var clash = library.Playlists
            .Where(p => playlistIds.Contains(p.Id))
            .SelectMany(p => p.Tracks)
            .Where(pt => pt.TrackId != trackId)
            .Select(pt => library.Tracks.FirstOrDefault(t => t.Id == pt.TrackId))
            .Any(t => t != null && string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));

        if (clash)
            return Task.FromResult<(bool, string?)>((false,
                $"A track named \"{trimmed}\" already exists in this playlist."));

        track.Name = trimmed;
        _store.Save(library);

        return Task.FromResult<(bool, string?)>((true, null));
    }

    public Task<(bool Success, string? Error, TrackUsageResponse? Data)> GetTrackUsageAsync(int trackId)
    {
        var library = _store.Load();
        var count = library.Playlists.Count(p => p.Tracks.Any(t => t.TrackId == trackId));
        return Task.FromResult<(bool, string?, TrackUsageResponse?)>((true, null, new TrackUsageResponse { PlaylistCount = count }));
    }

    public Task<BulkTrackResult> RemoveTracksAsync(int playlistId, IReadOnlyList<int> trackIds)
    {
        var library = _store.Load();
        var playlist = library.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist == null)
            return Task.FromResult(new BulkTrackResult(0, 0, new[] { "Playlist not found." }));

        var idSet = trackIds.ToHashSet();
        var removed = playlist.Tracks.RemoveAll(t => idSet.Contains(t.TrackId));
        PruneOrphanTracks(library, idSet);
        _store.Save(library);

        return Task.FromResult(new BulkTrackResult(removed, 0, Array.Empty<string>()));
    }

    public Task<BulkTrackResult> CopyTracksAsync(
        int sourcePlaylistId, int targetPlaylistId, IReadOnlyList<int> trackIds)
    {
        var library = _store.Load();
        var source = library.Playlists.FirstOrDefault(p => p.Id == sourcePlaylistId);
        var target = library.Playlists.FirstOrDefault(p => p.Id == targetPlaylistId);
        if (source == null || target == null)
            return Task.FromResult(new BulkTrackResult(0, 0, new[] { "Playlist not found." }));

        var alreadyInTarget = target.Tracks.Select(t => t.TrackId).ToHashSet();
        var sourceTrackIds = source.Tracks.Select(t => t.TrackId).ToHashSet();

        var processed = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var id in trackIds)
        {
            if (!sourceTrackIds.Contains(id)) { errors.Add($"Track {id} not found in source."); continue; }
            if (alreadyInTarget.Contains(id)) { skipped++; continue; }

            var nextPos = target.Tracks.Count == 0 ? 0 : target.Tracks.Max(t => t.Position) + 1;
            target.Tracks.Add(new LocalPlaylistTrackModel
            {
                TrackId = id,
                Position = nextPos,
                AddedAt = DateTime.UtcNow
            });
            alreadyInTarget.Add(id);
            processed++;
        }

        _store.Save(library);
        return Task.FromResult(new BulkTrackResult(processed, skipped, errors));
    }

    public Task<BulkTrackResult> MoveTracksAsync(
        int sourcePlaylistId, int targetPlaylistId, IReadOnlyList<int> trackIds)
    {
        var library = _store.Load();
        var source = library.Playlists.FirstOrDefault(p => p.Id == sourcePlaylistId);
        var target = library.Playlists.FirstOrDefault(p => p.Id == targetPlaylistId);
        if (source == null || target == null)
            return Task.FromResult(new BulkTrackResult(0, 0, new[] { "Playlist not found." }));

        var alreadyInTarget = target.Tracks.Select(t => t.TrackId).ToHashSet();
        var sourceTrackIds = source.Tracks.Select(t => t.TrackId).ToHashSet();

        var skipped = 0;
        var moved = 0;
        var errors = new List<string>();
        var removedIds = new HashSet<int>();

        foreach (var id in trackIds)
        {
            if (!sourceTrackIds.Contains(id)) { errors.Add($"Track {id} not found in source."); continue; }

            if (!alreadyInTarget.Contains(id))
            {
                var nextPos = target.Tracks.Count == 0 ? 0 : target.Tracks.Max(t => t.Position) + 1;
                target.Tracks.Add(new LocalPlaylistTrackModel
                {
                    TrackId = id,
                    Position = nextPos,
                    AddedAt = DateTime.UtcNow
                });
                alreadyInTarget.Add(id);
            }
            else
            {
                skipped++;
            }

            removedIds.Add(id);
            moved++;
        }

        if (removedIds.Count > 0)
        {
            source.Tracks.RemoveAll(t => removedIds.Contains(t.TrackId));
            PruneOrphanTracks(library, removedIds);
        }

        _store.Save(library);
        return Task.FromResult(new BulkTrackResult(moved, skipped, errors));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Removes tracks from the library that are no longer referenced by any playlist.
    /// </summary>
    private static void PruneOrphanTracks(LocalLibraryFileModel library, HashSet<int> candidateIds)
    {
        foreach (var trackId in candidateIds)
        {
            var stillReferenced = library.Playlists.Any(p => p.Tracks.Any(t => t.TrackId == trackId));
            if (!stillReferenced)
                library.Tracks.RemoveAll(t => t.Id == trackId);
        }
    }

    /// <summary>
    /// Returns a display name that does not collide (case-insensitively) with any other
    /// track already in the given playlist. Appends " (2)", " (3)", … when needed.
    /// </summary>
    private static string GetUniqueTrackName(
        LocalLibraryFileModel library, LocalPlaylistModel playlist, string baseName)
    {
        var used = playlist.Tracks
            .Select(pt => library.Tracks.FirstOrDefault(t => t.Id == pt.TrackId)?.Name)
            .Where(n => n != null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(baseName)) return baseName;

        for (var n = 2; n < int.MaxValue; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!used.Contains(candidate)) return candidate;
        }

        return $"{baseName} ({Guid.NewGuid():N})";
    }

    #endregion
}