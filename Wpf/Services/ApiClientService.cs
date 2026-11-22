using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Auth;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ognia.Wpf.Services;

public class ApiClientService
{
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromSeconds(30);
    private readonly HttpClient _http;
    private readonly HttpClient _anonymousHttp;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public event Action? Unauthorized;

    public ApiClientService(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        // This client never receives an Authorization default header.
        _anonymousHttp = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };
    }

    #region Account Operations
    /// <summary>
    /// Login with user credentials.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error, LoginResponse? Data)> LoginAsync(LoginRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/login", request);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
                return (true, null, data);
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Login failed ({response.StatusCode}): {error}", null);
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Attempts to refresh the authorization token.
    /// </summary>
    /// <returns></returns>
    public async Task<bool> TryRefreshAsync()
    {
        await _refreshLock.WaitAsync();

        try
        {
            return await TryRefreshCoreAsync();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Register a new account.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> RegisterAsync(RegisterRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/register", request);
            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Registration failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get account information.
    /// </summary>
    /// <returns></returns>
    public async Task<(bool Success, string? Error, string? Email)> GetMeAsync()
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.GetAsync("api/auth/me"));


            if (!response.IsSuccessStatusCode)
                return (false, $"Request failed ({response.StatusCode})", null);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var email = doc.RootElement.GetProperty("email").GetString();
            return (true, null, email);
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Process forgot password request and send out a token.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/forgot-password", request);

            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Request failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Reset forgotten password using the token.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> ResetPasswordAsync(ResetPasswordRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/reset-password", request);

            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Reset failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Change a logged-in user's password using the current password.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> ChangePasswordAsync(ChangePasswordRequest request)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.PostAsJsonAsync("api/auth/change-password", request));

            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Change failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Revokes the current refresh token server-side, then lets the caller clear local state.
    /// </summary>
    public async Task LogoutAsync()
    {
        try
        {
            var refreshToken = TokenStore.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken)) return;

            // The logout endpoint accepts an anonymous request bearing the refresh token.
            var response = await _http.PostAsJsonAsync("api/auth/logout",
                new RefreshTokenRequest { RefreshToken = refreshToken });

            // Swallow 401 — if the token was already revoked/expired, logout still succeeds locally.
        }
        catch
        {
            // Non-fatal: the user is logging out regardless.
        }
    }
    #endregion Account Operations

    #region Playlist Operations
    /// <summary>
    /// Get list of playlists the logged-in user has.
    /// </summary>
    /// <returns></returns>
    public async Task<(bool Success, string? Error, List<PlaylistResponse>? Data)> GetPlaylistsAsync()
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.GetAsync("api/playlists"));

            if (!response.IsSuccessStatusCode)
                return (false, $"Request failed ({response.StatusCode})", null);

            var data = await response.Content.ReadFromJsonAsync<List<PlaylistResponse>>();
            return (true, null, data ?? new List<PlaylistResponse>());
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Creates a playlist.
    /// </summary>
    /// <param name="name">The name of the playlist to create.</param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error, PlaylistResponse? Data)> CreatePlaylistAsync(CreatePlaylistRequest request)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.PostAsJsonAsync("api/playlists", request));

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<PlaylistResponse>();
                return (true, null, data);
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Create failed ({response.StatusCode}): {error}", null);
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Renames an existing playlist.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> RenamePlaylistAsync(int id, RenamePlaylistRequest request)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.PutAsJsonAsync($"api/playlists/{id}", request));

            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Rename failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes an existing playlist.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> DeletePlaylistAsync(int id)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.DeleteAsync($"api/playlists/{id}"));

            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Delete failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get playlist details, which includes tracks associated with the playlist.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error, PlaylistDetailResponse? Data)> GetPlaylistDetailAsync(int id)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.GetAsync($"api/playlists/{id}"));

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<PlaylistDetailResponse>();
                return (true, null, data);
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Load failed ({response.StatusCode}): {error}", null);
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", null);
        }
    }
    #endregion Playlist Operations

    #region Track Operations
    /// <summary>
    /// Adds track to an existing playlist.
    /// </summary>
    /// <param name="playlistId"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error, int Added, int Skipped)> AddTracksAsync(
    int playlistId, AddTrackRequest request)
    {
        try
        {
            var response = await SendWithRefreshAsync(() =>
                _http.PostAsJsonAsync($"api/playlists/{playlistId}/tracks", request));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return (false, $"Add failed ({response.StatusCode}): {error}", 0, 0);
            }

            // Backend returns either { id, name, ... } (single track) or { added, skipped } (playlist).
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            if (root.TryGetProperty("added", out var added) && root.TryGetProperty("skipped", out var skipped))
                return (true, null, added.GetInt32(), skipped.GetInt32());

            return (true, null, 1, 0);   // single-track success
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", 0, 0);
        }
    }

    public async Task<(bool Success, string? Error, List<string> Added, List<string> Skipped, List<string> Invalid)>
    AddTracksBulkAsync(int playlistId, IReadOnlyList<BulkAddTrackItem> items)
    {
        if (items.Count == 0)
            return (true, null, new(), new(), new());

        try
        {
            var body = new BulkAddTracksRequest { Urls = items.ToList() };
            var response = await SendWithRefreshAsync(() =>
                _http.PostAsJsonAsync($"api/playlists/{playlistId}/tracks/bulk", body));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return (false, $"Bulk add failed ({response.StatusCode}): {error}",
                    new(), new(), new());
            }

            var data = await response.Content.ReadFromJsonAsync<BulkAddTracksResponse>();
            return (true, null,
                data?.Added ?? new(),
                data?.Skipped ?? new(),
                data?.Invalid ?? new());
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", new(), new(), new());
        }
    }

    /// <summary>
    /// Removes track from an existing playlist.
    /// </summary>
    /// <param name="playlistId"></param>
    /// <param name="trackId"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> RemoveTrackAsync(int playlistId, int trackId)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.DeleteAsync($"api/playlists/{playlistId}/tracks/{trackId}"));

            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Remove failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Reorders tracks within a playlist.
    /// </summary>
    /// <param name="playlistId"></param>
    /// <param name="orderedTrackIds"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> ReorderTracksAsync(int playlistId, List<int> orderedTrackIds)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.PutAsJsonAsync(
                $"api/playlists/{playlistId}/tracks/order",
                new ReorderTracksRequest { TrackIds = orderedTrackIds }));

            if (response.IsSuccessStatusCode) return (true, null);

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Reorder failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    /// <summary>
    /// Renames a track in the backend.
    /// </summary>
    /// <param name="trackId"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    public async Task<(bool Success, string? Error)> RenameTrackAsync(int trackId, RenameTrackRequest request)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.PutAsJsonAsync($"api/tracks/{trackId}", request));

            if (response.IsSuccessStatusCode) return (true, null);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                        return (false, msg.GetString());
                }
                catch { }
                return (false, "A track with that name already exists in this playlist.");
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"Rename failed ({response.StatusCode}): {error}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error, TrackUsageResponse? Data)> GetTrackUsageAsync(int trackId)
    {
        try
        {
            var response = await SendWithRefreshAsync(() => _http.GetAsync($"api/tracks/{trackId}/usage"));

            if (!response.IsSuccessStatusCode)
                return (false, $"Usage check failed ({response.StatusCode})", null);

            var data = await response.Content.ReadFromJsonAsync<TrackUsageResponse>();
            return (true, null, data);
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}", null);
        }
    }
    #endregion Track Operations

    #region Private Methods
    /// <summary>
    /// Determines if the access token is expired and requires refresh.
    /// </summary>
    /// <returns></returns>
    private static bool AccessTokenNeedsRefresh()
    {
        var expiresAt = TokenStore.AccessTokenExpiresAt;

        if (!expiresAt.HasValue)
            return true;

        var expirationUtc = expiresAt.Value.ToUniversalTime();

        return DateTime.UtcNow >= expirationUtc - TokenRefreshSkew;
    }

    /// <summary>
    /// Core logic for refreshing expired access token.
    /// </summary>
    /// <returns></returns>
    private async Task<bool> TryRefreshCoreAsync()
    {
        var refreshToken = TokenStore.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
            return false;

        try
        {
            var response = await _anonymousHttp.PostAsJsonAsync(
                "api/auth/refresh",
                new RefreshTokenRequest
                {
                    RefreshToken = refreshToken
                });

            if (!response.IsSuccessStatusCode)
                return false;

            var data = await response.Content.ReadFromJsonAsync<LoginResponse>();

            if (data is null ||
                string.IsNullOrWhiteSpace(data.Token) ||
                string.IsNullOrWhiteSpace(data.RefreshToken))
            {
                return false;
            }

            TokenStore.Set(
                data.Token,
                data.Email,
                data.AccessTokenExpiresAt,
                data.RefreshToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="send"></param>
    /// <returns></returns>
    private async Task<HttpResponseMessage> SendWithRefreshAsync(Func<Task<HttpResponseMessage>> send)
    {
        // Refresh before sending if the locally stored token is expired
        // or close to expiration.
        if (TokenStore.IsAuthenticated && AccessTokenNeedsRefresh())
        {
            var refreshed = await TryRefreshAsync();

            if (!refreshed)
            {
                Unauthorized?.Invoke();
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
        }

        _http.DefaultRequestHeaders.Authorization =
            TokenStore.IsAuthenticated
                ? new AuthenticationHeaderValue("Bearer", TokenStore.Token)
                : null;

        var response = await send();

        // Still handle expiration detected by the server, for example
        // because of clock differences or token revocation.
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        var refreshedAfterUnauthorized = await TryRefreshAsync();

        if (!refreshedAfterUnauthorized)
        {
            Unauthorized?.Invoke();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenStore.Token);

        response = await send();

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            Unauthorized?.Invoke();

        return response;
    }
    #endregion Private Methods
}