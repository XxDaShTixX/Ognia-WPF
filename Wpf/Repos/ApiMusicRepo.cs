using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;
using Ognia.Wpf.Services;

namespace Ognia.Wpf.Repos;

    /// <summary>
    /// Serves music data from the backend API. Thin delegation to ApiClientService —
    /// no logic of its own.
    /// </summary>
public class ApiMusicRepo : IMusicRepo
{
    private readonly ApiClientService _api;

    public ApiMusicRepo(ApiClientService api)
    {
        _api = api;
    }

    public Task<(bool Success, string? Error, List<PlaylistResponse>? Data)> GetPlaylistsAsync()
        => _api.GetPlaylistsAsync();

    public Task<(bool Success, string? Error, PlaylistResponse? Data)> CreatePlaylistAsync(CreatePlaylistRequest request)
        => _api.CreatePlaylistAsync(request);

    public Task<(bool Success, string? Error)> RenamePlaylistAsync(int id, RenamePlaylistRequest request)
        => _api.RenamePlaylistAsync(id, request);

    public Task<(bool Success, string? Error)> DeletePlaylistAsync(int id)
        => _api.DeletePlaylistAsync(id);

    public Task<(bool Success, string? Error, PlaylistDetailResponse? Data)> GetPlaylistDetailAsync(int id)
    => _api.GetPlaylistDetailAsync(id);

    public async Task<(bool Success, string? Error, int Added, int Skipped)> AddTracksAsync(
    int playlistId, string url, IProgress<string>? progress = null)
    {
        var result = await _api.AddTracksAsync(playlistId, new AddTrackRequest { Url = url });
        if (!result.Success)
            return (false, result.Error, 0, 0);

        return (true, null, result.Added, result.Skipped);
    }

    public Task<(bool Success, string? Error, List<string> Added, List<string> Skipped, List<string> Invalid)>
    AddTracksBulkAsync(int playlistId, IReadOnlyList<BulkAddTrackItem> items, IProgress<string>? progress = null)
    => _api.AddTracksBulkAsync(playlistId, items);

    public Task<(bool Success, string? Error)> RemoveTrackAsync(int playlistId, int trackId)
        => _api.RemoveTrackAsync(playlistId, trackId);

    public Task<(bool Success, string? Error)> ReorderTracksAsync(int playlistId, List<int> orderedTrackIds)
    => _api.ReorderTracksAsync(playlistId, orderedTrackIds);

    public Task<(bool Success, string? Error)> RenameTrackAsync(int trackId, RenameTrackRequest request)
        => _api.RenameTrackAsync(trackId, request);

    public Task<(bool Success, string? Error, TrackUsageResponse? Data)> GetTrackUsageAsync(int trackId)
        => _api.GetTrackUsageAsync(trackId);
}