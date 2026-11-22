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