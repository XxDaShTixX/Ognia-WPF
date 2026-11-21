using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Repos;
using Ognia.Wpf.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace Ognia.Wpf.Session;

public sealed class AppSession : ViewModelBase
{
    private bool _suppressAutoSwap;

    public AppSession(IMusicRepo repo)
    {
        Repo = repo;
        SelectedPlaylistTracks = new ObservableCollection<TrackRowViewModel>();
        SelectedPlaylistTracks.CollectionChanged += (_, _) =>
        {
            if (!IsFilterActive) RefreshFilteredTracks();
        };

        // Route all row-level operations through the session's status bar
        PlayRequested += t => SetStatus($"Playing: {t.Name}");
        DownloadRequested += t => SetStatus($"Downloading: {t.Name}...");
        RemoveDownloadRequested += t => SetStatus($"Removed download for: {t.Name}");
        RemoveRequested += t => SetStatus($"Removed track: {t.Name}");
        RenameRequested += t => SetStatus($"Renamed track to: {t.Name}");
    }

    public void AttachPlayer(PlaybackViewModel playback, DownloadViewModel download)
    {
        Playback = playback;
        Download = download;
        Playback.Tracks = SelectedPlaylistTracks;
        Download.Tracks = SelectedPlaylistTracks;
        Playback.TrackDownloadRequested = t =>
        {
            SetStatus($"Preparing to play '{t.Name}'...");
            return Download.EnsureDownloadedAsync(t);
        };

        // Hook up ViewModel level errors to the status bar
        Playback.ErrorOccurred += msg => SetError(msg);
        Download.ErrorOccurred += msg => SetError(msg);
        Download.ProgressReported += msg => SetStatus(msg);
    }

    public IMusicRepo Repo { get; }
    public TrackDetailsViewModel? Details { get; set; }

    public ObservableCollection<PlaylistResponse> Playlists { get; } = new();
    public ObservableCollection<TrackRowViewModel> SelectedPlaylistTracks { get; }

    /// <summary>
    /// Filtered view of <see cref="SelectedPlaylistTracks"/> bound by PlaylistView.
    /// Empty filter => contains every row (same instances).
    /// </summary>
    public ObservableCollection<TrackRowViewModel> FilteredPlaylistTracks { get; } = new();

    public PlaybackViewModel Playback { get; private set; } = null!;
    public DownloadViewModel Download { get; private set; } = null!;

    public bool SuppressAutoSwap => _suppressAutoSwap;

    #region Observable state

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (!SetProperty(ref _statusMessage, value)) return;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    private bool _isStatusError;
    public bool IsStatusError
    {
        get => _isStatusError;
        set => SetProperty(ref _isStatusError, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Runs an async mutation while holding the busy gate. Re-entrant calls are rejected.
    /// </summary>
    public async Task<bool> RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return false;
        IsBusy = true;
        try { await action(); return true; }
        finally { IsBusy = false; }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    private DispatcherTimer? _statusTimer;

    /// <summary>How long non-error status messages stay visible before auto-clearing.</summary>
    public static TimeSpan StatusAutoDismissDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Sets a non-error status message that auto-clears after <see cref="StatusAutoDismissDelay"/>.</summary>
    public void SetStatus(string message)
    {
        IsStatusError = false;
        StatusMessage = message;
        StartStatusTimer();
    }

    /// <summary>Sets an error status message that persists until replaced or cleared.</summary>
    public void SetError(string message)
    {
        StopStatusTimer();
        IsStatusError = true;
        StatusMessage = message;
    }

    public void ClearStatus()
    {
        StopStatusTimer();
        IsStatusError = false;
        StatusMessage = null;
    }

    private void StartStatusTimer()
    {
        StopStatusTimer();
        if (StatusAutoDismissDelay <= TimeSpan.Zero) return;

        _statusTimer = new DispatcherTimer { Interval = StatusAutoDismissDelay };
        _statusTimer.Tick += (_, _) => ClearStatus();
        _statusTimer.Start();
    }

    private void StopStatusTimer()
    {
        if (_statusTimer == null) return;
        _statusTimer.Stop();
        _statusTimer = null;
    }

    private PlaylistResponse? _selectedPlaylist;
    public PlaylistResponse? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (!SetProperty(ref _selectedPlaylist, value)) return;
            SelectedTrack = null;
            ClearPlaylistFilter();
            _ = ReloadSelectedPlaylistTracksAsync();
        }
    }

    private TrackRowViewModel? _selectedTrack;
    public TrackRowViewModel? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (!SetProperty(ref _selectedTrack, value)) return;
            if (Download != null) Download.SelectedTrack = value;
        }
    }

    private bool _isCreatingPlaylist;
    public bool IsCreatingPlaylist
    {
        get => _isCreatingPlaylist;
        set => SetProperty(ref _isCreatingPlaylist, value);
    }

    private string _newPlaylistName = string.Empty;
    public string NewPlaylistName
    {
        get => _newPlaylistName;
        set => SetProperty(ref _newPlaylistName, value);
    }

    #endregion

    #region Playlist loading

    public async Task LoadPlaylistsAsync()
    {
        IsLoading = true;

        var previousId = PlaybackSessionState.Instance.LastOpenedPlaylistId;

        _suppressAutoSwap = true;
        try
        {
            var result = await Repo.GetPlaylistsAsync();
            IsLoading = false;

            if (!result.Success || result.Data == null)
            {
                SetError(result.Error ?? "Failed to load playlists.");
                return;
            }

            Playlists.Clear();
            foreach (var p in result.Data)
                Playlists.Add(p);

            foreach (var p in Playlists)
            {
                var cached = PlaylistCoverCache.TryGetCached(p.Id);
                if (cached != null)
                    p.CachedCoverPath = cached;
            }

            foreach (var p in Playlists)
                _ = PopulateCoverAsync(p);

            if (previousId.HasValue)
            {
                var match = Playlists.FirstOrDefault(p => p.Id == previousId.Value);
                if (match != null)
                    await RestorePlaybackAsync(match);
            }
        }
        finally
        {
            _suppressAutoSwap = false;
        }
    }

    private async Task RestorePlaybackAsync(PlaylistResponse playlist)
    {
        var lastTrackId = PlaybackSessionState.Instance.LastTrackId;
        if (lastTrackId == null) return;

        // Don't clobber an already-playing track (e.g. re-entrant LoadPlaylistsAsync).
        if (Playback.CurrentTrack != null) return;

        // Load this playlist's tracks without navigating or selecting the playlist,
        // so the main pane stays on Library but the playback bar can be populated.
        var result = await Repo.GetPlaylistDetailAsync(playlist.Id);
        if (!result.Success || result.Data == null) return;

        var track = result.Data.Tracks.FirstOrDefault(t => t.Id == lastTrackId.Value);
        if (track == null) return;

        var row = new TrackRowViewModel(
            track, playlist.Name,
            OnRowPlayRequested,
            OnRowMoreRequested,
            OnRowRenameRequested,
            OnRowRemoveRequested,
            OnRowDownloadRequested,
            OnRowRemoveDownloadRequested);

        // Loaded row, not part of SelectedPlaylistTracks — playback bar only.
        Playback.RestoreTrack(row);
    }

    private static async Task PopulateCoverAsync(PlaylistResponse p)
    {
        var path = await PlaylistCoverCache.GetOrBuildAsync(p.Id, p.CoverThumbnails);
        p.CachedCoverPath = path;
    }

    public async Task ReloadSelectedPlaylistTracksAsync()
    {
        SelectedPlaylistTracks.Clear();
        FilteredPlaylistTracks.Clear();
        SelectedTrack = null;

        if (Playback != null)
            Playback.Tracks = SelectedPlaylistTracks;

        if (Download != null)
            Download.Tracks = SelectedPlaylistTracks;

        if (SelectedPlaylist == null) return;

        if (SelectedPlaylist == null) return;

        var result = await Repo.GetPlaylistDetailAsync(SelectedPlaylist.Id);
        if (!result.Success || result.Data == null)
        {
            SetError(result.Error ?? "Failed to load tracks.");
            return;
        }

        foreach (var t in result.Data.Tracks)
            SelectedPlaylistTracks.Add(new TrackRowViewModel(
                t, SelectedPlaylist.Name,
                OnRowPlayRequested,
                OnRowMoreRequested,
                OnRowRenameRequested,
                OnRowRemoveRequested,
                OnRowDownloadRequested,
                OnRowRemoveDownloadRequested));

        ReindexTracks();

        Playback?.RebindCurrentTrack();
    }

    /// <summary>
    /// Reassigns 1-based DisplayOrder to match the current collection order.
    /// </summary>
    public void ReindexTracks()
    {
        for (var i = 0; i < SelectedPlaylistTracks.Count; i++)
            SelectedPlaylistTracks[i].DisplayOrder = i + 1;
    }

    public void BeginCreatePlaylist()
    {
        NewPlaylistName = string.Empty;
        IsCreatingPlaylist = true;
    }

    public void EndCreatePlaylist()
    {
        IsCreatingPlaylist = false;
        NewPlaylistName = string.Empty;
    }

    #endregion

    #region Playlist Filtering
    private bool _playlistFilterOpen;
    public bool PlaylistFilterOpen
    {
        get => _playlistFilterOpen;
        set => SetProperty(ref _playlistFilterOpen, value);
    }

    private string _playlistFilter = string.Empty;

    public string PlaylistFilter
    {
        get => _playlistFilter;
        set
        {
            if (!SetProperty(ref _playlistFilter, value)) return;
            OnPropertyChanged(nameof(IsFilterActive));
            RefreshFilteredTracks();
        }
    }

    public bool IsFilterActive => !string.IsNullOrWhiteSpace(_playlistFilter);

    /// <summary>
    /// Rebuilds <see cref="FilteredPlaylistTracks"/> from <see cref="SelectedPlaylistTracks"/>
    /// using a case-insensitive "contains" match on track name.
    /// </summary>
    public void RefreshFilteredTracks()
    {
        var term = _playlistFilter?.Trim() ?? string.Empty;

        FilteredPlaylistTracks.Clear();

        if (term.Length == 0)
        {
            foreach (var t in SelectedPlaylistTracks)
                FilteredPlaylistTracks.Add(t);
            return;
        }

        foreach (var t in SelectedPlaylistTracks)
        {
            if (t.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                FilteredPlaylistTracks.Add(t);
        }
    }

    public void ClearPlaylistFilter()
    {
        if (_playlistFilter.Length == 0) return;
        PlaylistFilter = string.Empty;
    }
    #endregion

    #region Row callbacks

    /// <summary>Raised when a row requests playback of itself.</summary>
    public event Action<TrackRowViewModel>? PlayRequested;
    public event Action<TrackRowViewModel>? MoreRequested;
    public event Action<TrackRowViewModel>? RenameRequested;
    public event Action<TrackRowViewModel>? RemoveRequested;
    public event Action<TrackRowViewModel>? DownloadRequested;
    public event Action<TrackRowViewModel>? RemoveDownloadRequested;

    private void OnRowPlayRequested(TrackRowViewModel t) { SelectedTrack = t; PlayRequested?.Invoke(t); }
    private void OnRowMoreRequested(TrackRowViewModel t) { SelectedTrack = t; MoreRequested?.Invoke(t); }
    private void OnRowRenameRequested(TrackRowViewModel t) { SelectedTrack = t; RenameRequested?.Invoke(t); }
    private void OnRowRemoveRequested(TrackRowViewModel t) { SelectedTrack = t; RemoveRequested?.Invoke(t); }
    private void OnRowDownloadRequested(TrackRowViewModel t) { DownloadRequested?.Invoke(t); }
    private void OnRowRemoveDownloadRequested(TrackRowViewModel t) { RemoveDownloadRequested?.Invoke(t); }

    // Public adapters so view models can construct TrackRowViewModel instances
    // with the same callbacks the session uses internally.
    public Action<TrackRowViewModel> OnRowPlayRequestedPublic => OnRowPlayRequested;
    public Action<TrackRowViewModel> OnRowMoreRequestedPublic => OnRowMoreRequested;
    public Action<TrackRowViewModel> OnRowRenameRequestedPublic => OnRowRenameRequested;
    public Action<TrackRowViewModel> OnRowRemoveRequestedPublic => OnRowRemoveRequested;
    public Action<TrackRowViewModel> OnRowDownloadRequestedPublic => OnRowDownloadRequested;
    public Action<TrackRowViewModel> OnRowRemoveDownloadRequestedPublic => OnRowRemoveDownloadRequested;

    #endregion
}