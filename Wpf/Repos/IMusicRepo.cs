using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;

namespace Ognia.Wpf.Repos
{
    /// <summary>
    /// The data operations the UI needs, independent of whether they are
    /// served by the backend API (account mode) or local storage (guest mode).
    /// Account-only concerns (login, token refresh) are not part of this interface.
    /// </summary>
    public interface IMusicRepo
    {
        #region Playlists
        Task<(bool Success, string? Error, List<PlaylistResponse>? Data)> GetPlaylistsAsync();
        Task<(bool Success, string? Error, PlaylistResponse? Data)> CreatePlaylistAsync(CreatePlaylistRequest request);
        Task<(bool Success, string? Error)> RenamePlaylistAsync(int id, RenamePlaylistRequest request);
        Task<(bool Success, string? Error)> DeletePlaylistAsync(int id);
        Task<(bool Success, string? Error, PlaylistDetailResponse? Data)> GetPlaylistDetailAsync(int id);
        #endregion

        #region Tracks
        Task<(bool Success, string? Error, int Added, int Skipped)> AddTracksAsync(
            int playlistId, string url, IProgress<string>? progress = null);
        Task<(bool Success, string? Error)> RemoveTrackAsync(int playlistId, int trackId);
        Task<(bool Success, string? Error)> ReorderTracksAsync(int playlistId, List<int> orderedTrackIds);
        Task<(bool Success, string? Error)> RenameTrackAsync(int trackId, RenameTrackRequest request);
        Task<(bool Success, string? Error, TrackUsageResponse? Data)> GetTrackUsageAsync(int trackId);
        #endregion

        #region Bulk track Operations
        /// <summary>
        /// Removes several tracks from a playlist. Default fans out to
        /// <see cref="RemoveTrackAsync"/>. Native overrides should do this in one pass.
        /// </summary>
        async Task<BulkTrackResult> RemoveTracksAsync(int playlistId, IReadOnlyList<int> trackIds)
        {
            var errors = new List<string>();
            var processed = 0;

            foreach (var id in trackIds)
            {
                var result = await RemoveTrackAsync(playlistId, id);
                if (result.Success) processed++;
                else errors.Add(result.Error ?? $"Failed to remove track {id}.");
            }

            return new BulkTrackResult(processed, 0, errors);
        }

        /// <summary>
        /// Copies tracks (by id) from <paramref name="sourcePlaylistId"/> to
        /// <paramref name="targetPlaylistId"/>. Tracks already in the target are
        /// reported as skipped. Default fans out via <see cref="AddTracksAsync"/>.
        /// </summary>
        async Task<BulkTrackResult> CopyTracksAsync(
            int sourcePlaylistId, int targetPlaylistId, IReadOnlyList<int> trackIds)
        {
            var source = await GetPlaylistDetailAsync(sourcePlaylistId);
            if (!source.Success || source.Data == null)
                return new BulkTrackResult(0, 0, new[] { source.Error ?? "Failed to load source playlist." });

            var target = await GetPlaylistDetailAsync(targetPlaylistId);
            if (!target.Success || target.Data == null)
                return new BulkTrackResult(0, 0, new[] { target.Error ?? "Failed to load target playlist." });

            var alreadyInTarget = target.Data.Tracks.Select(t => t.Id).ToHashSet();
            var byId = source.Data.Tracks.ToDictionary(t => t.Id);

            var processed = 0;
            var skipped = 0;
            var errors = new List<string>();

            foreach (var id in trackIds)
            {
                if (!byId.TryGetValue(id, out var track)) { errors.Add($"Track {id} not found in source."); continue; }
                if (alreadyInTarget.Contains(id)) { skipped++; continue; }

                var url = Helpers.TrackSourceUrlResolver.BuildUrl(track.SourceName, track.SourceId);
                if (string.IsNullOrWhiteSpace(url)) { errors.Add($"Could not build URL for '{track.Name}'."); continue; }

                var result = await AddTracksAsync(targetPlaylistId, url);
                if (result.Success && result.Added > 0) processed++;
                else if (result.Success) skipped++;
                else errors.Add(result.Error ?? $"Failed to copy '{track.Name}'.");
            }

            return new BulkTrackResult(processed, skipped, errors);
        }

        /// <summary>
        /// Moves tracks from <paramref name="sourcePlaylistId"/> to
        /// <paramref name="targetPlaylistId"/>: copy then remove. Tracks already
        /// in the target are still removed from the source (that is the "move"
        /// semantic). Errors on individual tracks are collected, not thrown.
        /// </summary>
        async Task<BulkTrackResult> MoveTracksAsync(
            int sourcePlaylistId, int targetPlaylistId, IReadOnlyList<int> trackIds)
        {
            var copy = await CopyTracksAsync(sourcePlaylistId, targetPlaylistId, trackIds);

            var errors = new List<string>(copy.Errors);
            var removed = 0;

            foreach (var id in trackIds)
            {
                // Skip removal only if the copy hard-failed for this id.
                if (copy.Errors.Any(e => e.Contains(id.ToString()))) continue;

                var r = await RemoveTrackAsync(sourcePlaylistId, id);
                if (r.Success) removed++;
                else errors.Add(r.Error ?? $"Failed to remove track {id} from source.");
            }

            return new BulkTrackResult(removed, copy.Skipped, errors);
        }

        #endregion

        /// <summary>
        /// Bulk-adds URLs to a playlist. Backends with a native bulk endpoint override this.
        /// The default fans out to single-URL <see cref="AddTracksAsync"/> sequentially so
        /// every implementation (e.g. local repo) keeps working without changes.
        /// Returns (success, error, addedUrls, skippedUrls, invalidUrls).
        /// </summary>
        async Task<(bool Success, string? Error, List<string> Added, List<string> Skipped, List<string> Invalid)>
        AddTracksBulkAsync(int playlistId, IReadOnlyList<BulkAddTrackItem> items, IProgress<string>? progress = null)
        {
            var added = new List<string>();
            var skipped = new List<string>();
            var invalid = new List<string>();

            for (var i = 0; i < items.Count; i++)
            {
                progress?.Report($"{i + 1}/{items.Count}");
                var result = await AddTracksAsync(playlistId, items[i].Url);
                if (result.Success && result.Added > 0) added.Add(items[i].Url);
                else if (result.Success) skipped.Add(items[i].Url);
                else invalid.Add(items[i].Url);
            }

            return (true, null, added, skipped, invalid);
        }
    }
}