using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;
using Ognia.Wpf.Session;
using Ognia.Wpf.TrackSources;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Ognia.Wpf.ViewModels;

/// <summary>
/// A single search-result row. Owns its own per-row playlist dropdown
/// (bound to the session's playlists) and its add-to-playlist state.
/// Add-button enablement is resolved against the target playlist's
/// track list via the repo, cached per (row, playlistId).
/// Match is by (SourceName, SourceId) — <see cref="TrackResponse.Id"/> is
/// not stable across search results and playlists.
/// </summary>
public sealed class ResultRowViewModel : ViewModelBase
{
    private readonly AppSession _session;

    public TrackResponse Track { get; }
    public string Name => Track.Name;
    public string ThumbnailUrl => Track.ThumbnailUrl;
    public string SourceUrl => TrackSourceUrlResolver.BuildUrl(Track.SourceName, Track.SourceId) ?? string.Empty;
    public TimeSpan Duration => Track.Duration;
    public string DurationLabel => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}:{Duration.Minutes:00}:{Duration.Seconds:00}"
        : Duration.ToString(@"mm\:ss");

    public ObservableCollection<PlaylistResponse> Playlists { get; }

    private readonly Dictionary<int, bool> _containsCache = new();
    private PlaylistResponse? _selectedPlaylist;

    public ResultRowViewModel(TrackResponse track, AppSession session)
    {
        Track = track;
        _session = session;

        Playlists = session.Playlists;
        _selectedPlaylist = Playlists.FirstOrDefault();

        if (_selectedPlaylist != null)
            _ = RefreshAddButtonStateAsync(_selectedPlaylist);

        // Playlists load async on shell entry; re-target when they arrive.
        Playlists.CollectionChanged += OnPlaylistsChanged;
    }
   
    public PlaylistResponse? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (!SetProperty(ref _selectedPlaylist, value)) return;

            IsAddButtonEnabled = false;
            if (value != null) _ = RefreshAddButtonStateAsync(value);
        }
    }

    #region Bindable Properties

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }
    private bool _isPlaying;

    public bool IsAddButtonEnabled
    {
        get => _isAddButtonEnabled;
        private set => SetProperty(ref _isAddButtonEnabled, value);
    }
    private bool _isAddButtonEnabled;

    #endregion

    /// <summary>
    /// Picks the first playlist once the session's collection populates,
    /// then resolves the add-button state for it.
    /// </summary>
    private void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selectedPlaylist != null) return;
        if (Playlists.Count == 0) return;

        SelectedPlaylist = Playlists[0];
    }

    private async Task RefreshAddButtonStateAsync(PlaylistResponse playlist)
    {
        var enabled = await ResolveAddButtonStateAsync(playlist.Id);

        if (!ReferenceEquals(_selectedPlaylist, playlist)) return;
        IsAddButtonEnabled = enabled;
    }

    private async Task<bool> ResolveAddButtonStateAsync(int playlistId)
    {
        if (_containsCache.TryGetValue(playlistId, out var cached))
            return !cached;

        var result = await _session.Repo.GetPlaylistDetailAsync(playlistId);
        if (!result.Success || result.Data == null)
            return false;

        var present = result.Data.Tracks.Any(t =>
            string.Equals(t.SourceName, Track.SourceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.SourceId, Track.SourceId, StringComparison.OrdinalIgnoreCase));

        _containsCache[playlistId] = present;
        return !present;
    }

    /// <summary>
    /// Called by the owning view model after a successful add, so the button
    /// immediately disables for the playlist it was just added to.
    /// </summary>
    public void MarkAddedTo(int playlistId)
    {
        _containsCache[playlistId] = true;
        if (_selectedPlaylist?.Id == playlistId)
            IsAddButtonEnabled = false;
    }
}