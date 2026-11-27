using Ognia.Wpf.Caching;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Library;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Models.Track;
using Ognia.Wpf.Repos;
using Ognia.Wpf.Services;
using Ognia.Wpf.ViewModels;
using Ognia.Wpf.ViewModels.RightPane;
using Ognia.Wpf.ViewModels.Shell;
using System.Collections.ObjectModel;

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
        PlayRequested += t => NotificationService.Instance.Info($"Playing: {t.Name}");
        DownloadRequested += t => NotificationService.Instance.Info($"Downloading: {t.Name}...");
        RemoveDownloadRequested += t => NotificationService.Instance.Info($"Removed download for: {t.Name}");
        RemoveRequested += t => NotificationService.Instance.Info($"Removed track: {t.Name}");
        RenameRequested += t => NotificationService.Instance.Info($"Renamed track to: {t.Name}");
    }

    public void AttachPlayer(PlaybackViewModel playback, DownloadViewModel download)
    {
        Playback = playback;
        Download = download;
        Playback.Tracks = SelectedPlaylistTracks;
        Download.Tracks = SelectedPlaylistTracks;
        Playback.TrackDownloadRequested = t =>
        {
            NotificationService.Instance.Info($"Preparing to play '{t.Name}'...");
            return Download.EnsureDownloadedAsync(t);
        };

        // Hook up ViewModel level errors to the status bar
        Playback.ErrorOccurred += msg => NotificationService.Instance.Info(msg);
        Download.ErrorOccurred += msg => NotificationService.Instance.Info(msg);
        Download.ProgressReported += msg => NotificationService.Instance.Info(msg);
    }

    public IMusicRepo Repo { get; }
    public ILibraryTransfer Transfer { get; set; } = null!;

    public TrackDetailsViewModel? Details { get; set; }

    public ObservableCollection<PlaylistResponse> Playlists { get; } = new();
    public ObservableCollection<TrackRowViewModel> SelectedPlaylistTracks { get; }

    /// <summary>Tracks currently checked across the open playlist.</summary>
    public ObservableCollection<TrackRowViewModel> SelectedTracks { get; } = new();

    public bool HasMultiSelection => SelectedTracks.Count > 1;

    public int SelectedTrackCount => SelectedTracks.Count;

    /// <summary>
    /// Fires on every user-driven selection change (mouse, checkbox, range)
    /// so hosts can refresh the right pane even when <see cref="SelectedTrack"/>
    /// did not itself transition. Not raised by programmatic reload.
    /// </summary>
    public event Action? SelectionChanged;

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(SelectedTrackCount));
        SelectionChanged?.Invoke();
    }

    private void OnRowSelectionChanged(TrackRowViewModel row, bool selected)
    {
        if (selected)
        {
            if (!SelectedTracks.Contains(row)) SelectedTracks.Add(row);
        }
        else
        {
            SelectedTracks.Remove(row);
        }

        if (SelectedTracks.Count == 1) SelectedTrack = SelectedTracks[0];
        else if (SelectedTracks.Count == 0 && SelectedTrack != null && !SelectedPlaylistTracks.Contains(SelectedTrack)) SelectedTrack = null;

        if (selected) SelectionAnchor = row;
        else if (ReferenceEquals(SelectionAnchor, row)) SelectionAnchor = null;

        RaiseSelectionChanged();
    }

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

    #region Playlist Loading

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
                NotificationService.Instance.Error(result.Error ?? "Failed to load playlists.");
                return;
            }

            Playlists.Clear();
            foreach (var p in ApplyPlaylistOrder(result.Data))
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

    /// <summary>
    /// Returns playlists ordered by the user's saved order, then by Id for any
    /// playlists not yet in the saved order (e.g. newly created).
    /// </summary>
    private static IEnumerable<PlaylistResponse> ApplyPlaylistOrder(IEnumerable<PlaylistResponse> source)
    {
        var saved = AppSettings.Instance.PlaylistOrder;
        var rank = new Dictionary<int, int>();
        for (var i = 0; i < saved.Count; i++)
            rank[saved[i]] = i;

        return source
            .OrderBy(p => rank.TryGetValue(p.Id, out var r) ? r : int.MaxValue)
            .ThenBy(p => p.Id);
    }

    /// <summary>
    /// Persists the current <see cref="Playlists"/> order to settings.
    /// </summary>
    public void PersistPlaylistOrder()
    {
        AppSettings.Instance.PlaylistOrder = Playlists.Select(p => p.Id).ToList();
        AppSettings.Instance.Save();
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
            track, playlist.Name, this,
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
        SelectedTracks.Clear();
        SelectionAnchor = null;

        if (Playback != null)
            Playback.Tracks = SelectedPlaylistTracks;

        if (Download != null)
            Download.Tracks = SelectedPlaylistTracks;

        if (SelectedPlaylist == null) return;

        if (SelectedPlaylist == null) return;

        var result = await Repo.GetPlaylistDetailAsync(SelectedPlaylist.Id);
        if (!result.Success || result.Data == null)
        {
            NotificationService.Instance.Error(result.Error ?? "Failed to load tracks.");
            return;
        }

        foreach (var t in result.Data.Tracks)
        {
            var row = new TrackRowViewModel(
                t, SelectedPlaylist.Name, this,
                OnRowPlayRequested,
                OnRowMoreRequested,
                OnRowRenameRequested,
                OnRowRemoveRequested,
                OnRowDownloadRequested,
                OnRowRemoveDownloadRequested);
            row.SelectionChanged += OnRowSelectionChanged;
            SelectedPlaylistTracks.Add(row);
        }

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

    #region Playlist Deletion

    /// <summary>
    /// Deletes a playlist: confirms, removes from repo, wipes local folder,
    /// clears selection if it was open, reloads playlists, persists order.
    /// Shared by LibraryView (tile trash) and PlaylistView (header trash).
    /// </summary>
    /// <returns>True if the playlist was deleted.</returns>
    public async Task<bool> DeletePlaylistAsync(PlaylistResponse playlist)
    {
        if (playlist == null) return false;

        var confirm = System.Windows.MessageBox.Show(
            $"Delete playlist \"{playlist.Name}\"?\n\n" +
            "This removes all tracks in the playlist and deletes their downloaded files.",
            "Delete playlist",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return false;

        var deleted = false;

        await RunBusyAsync(async () =>
        {
            var result = await Repo.DeletePlaylistAsync(playlist.Id);
            if (!result.Success)
            {
                NotificationService.Instance.Error(result.Error ?? "Failed to delete playlist.");
                return;
            }

            LocalLibrary.DeletePlaylistFolder(playlist.Name);

            if (SelectedPlaylist?.Id == playlist.Id)
            {
                SelectedPlaylist = null;
                SelectedPlaylistTracks.Clear();
                FilteredPlaylistTracks.Clear();
            }

            await LoadPlaylistsAsync();
            PersistPlaylistOrder();
            NotificationService.Instance.Info($"Deleted playlist '{playlist.Name}'.");
            deleted = true;
        });

        return deleted;
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

    #region Bulk Operations

    /// <summary>
    /// Removes every checked track from the current playlist. Wipes local files
    /// for those tracks. Clears selection on success.
    /// </summary>
    public async Task<BulkTrackResult> RemoveSelectedTracksAsync()
    {
        var playlist = SelectedPlaylist;
        if (playlist == null) return new BulkTrackResult(0, 0, Array.Empty<string>());

        var rows = SelectedTracks.ToList();
        if (rows.Count == 0) return new BulkTrackResult(0, 0, Array.Empty<string>());

        var ids = rows.Select(r => r.Id).ToList();
        var playlistName = playlist.Name;

        BulkTrackResult result = new(0, 0, Array.Empty<string>());

        await RunBusyAsync(async () =>
        {
            result = await Repo.RemoveTracksAsync(playlist.Id, ids);

            foreach (var row in rows)
            {
                LocalLibrary.DeleteLocalFile(playlistName, row.Name);
                LocalLibrary.DeleteLocalThumbnail(playlistName, row.Name);
            }

            await ReloadSelectedPlaylistTracksAsync();
            await LoadPlaylistsAsync();

            if (result.Errors.Count == 0)
                NotificationService.Instance.Info($"Removed {result.Processed} track(s) from '{playlistName}'.");
            else
                NotificationService.Instance.Error($"{result.Errors.Count} track(s) failed: {result.Errors[0]}");
        });

        return result;
    }

    /// <summary>
    /// Copies every checked track to <paramref name="target"/>. Tracks already in
    /// the target are skipped. Selection is preserved (copy is non-destructive).
    /// </summary>
    public async Task<BulkTrackResult> CopySelectedTracksAsync(PlaylistResponse target)
    {
        var source = SelectedPlaylist;
        if (source == null || target == null) return new BulkTrackResult(0, 0, Array.Empty<string>());

        var ids = SelectedTracks.Select(r => r.Id).ToList();
        if (ids.Count == 0) return new BulkTrackResult(0, 0, Array.Empty<string>());

        BulkTrackResult result = new(0, 0, Array.Empty<string>());

        await RunBusyAsync(async () =>
        {
            result = await Repo.CopyTracksAsync(source.Id, target.Id, ids);
            await LoadPlaylistsAsync();

            var msg = $"Copied {result.Processed} to '{target.Name}'.";
            if (result.Skipped > 0) msg += $" {result.Skipped} already present.";
            if (result.Errors.Count > 0) NotificationService.Instance.Error(msg + $" {result.Errors.Count} failed.");
            else NotificationService.Instance.Info(msg);
        });

        return result;
    }

    /// <summary>
    /// Moves every checked track from the current playlist to <paramref name="target"/>.
    /// On success the moved rows disappear from the current playlist and selection is cleared.
    /// </summary>
    public async Task<BulkTrackResult> MoveSelectedTracksAsync(PlaylistResponse target)
    {
        var source = SelectedPlaylist;
        if (source == null || target == null) return new BulkTrackResult(0, 0, Array.Empty<string>());

        var ids = SelectedTracks.Select(r => r.Id).ToList();
        if (ids.Count == 0) return new BulkTrackResult(0, 0, Array.Empty<string>());

        BulkTrackResult result = new(0, 0, Array.Empty<string>());

        await RunBusyAsync(async () =>
        {
            result = await Repo.MoveTracksAsync(source.Id, target.Id, ids);

            await ReloadSelectedPlaylistTracksAsync();
            await LoadPlaylistsAsync();

            var msg = $"Moved {result.Processed} to '{target.Name}'.";
            if (result.Skipped > 0) msg += $" {result.Skipped} already present (removed from source).";
            if (result.Errors.Count > 0) NotificationService.Instance.Error(msg + $" {result.Errors.Count} failed.");
            else NotificationService.Instance.Info(msg);
        });

        return result;
    }

    /// <summary>
    /// Downloads every checked, not-yet-downloaded track sequentially.
    /// </summary>
    public async Task DownloadSelectedTracksAsync()
    {
        var rows = SelectedTracks.Where(t => !t.IsDownloaded).ToList();
        if (rows.Count == 0) return;

        var completed = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            NotificationService.Instance.Info($"Downloading {completed + 1}/{rows.Count}: {row.Name}");
            if (await Download.EnsureDownloadedAsync(row)) completed++;
            else failed++;
        }

        if (failed > 0) NotificationService.Instance.Error($"{failed} of {rows.Count} download(s) failed.");
        else NotificationService.Instance.Info($"Downloaded {completed} track(s).");
    }

    /// <summary>Deletes local files for every checked, downloaded track.</summary>
    public void RemoveDownloadsForSelected()
    {
        var rows = SelectedTracks.Where(t => t.IsDownloaded).ToList();
        if (rows.Count == 0) return;

        var failed = 0;
        foreach (var row in rows)
        {
            if (LocalLibrary.DeleteLocalFile(row.PlaylistName, row.Name)) row.IsDownloaded = false;
            else failed++;
            LocalLibrary.DeleteLocalThumbnail(row.PlaylistName, row.Name);
        }

        if (failed > 0) NotificationService.Instance.Error($"{failed} file(s) could not be deleted.");
        else NotificationService.Instance.Info($"Removed downloads for {rows.Count} track(s).");
    }

    #endregion

    #region Row Selection

    /// <summary>
    /// Row whose selection the last plain or Ctrl click established.
    /// Shift+click uses this as the range start. Null when nothing is selected.
    /// </summary>
    public TrackRowViewModel? SelectionAnchor { get; private set; }

    /// <summary>
    /// Applies a plain click: clears every other row, selects <paramref name="row"/>.
    /// </summary>
    public void SelectSingle(TrackRowViewModel row)
    {
        if (row == null) return;

        foreach (var r in SelectedPlaylistTracks)
            if (!ReferenceEquals(r, row)) r.IsSelected = false;

        row.IsSelected = true;

        SelectionAnchor = row;
        SelectedTrack = row;

        RaiseSelectionChanged();
    }

    /// <summary>
    /// Applies a Ctrl+click: toggles <paramref name="row"/>.
    /// </summary>
    public void ToggleSelection(TrackRowViewModel row)
    {
        if (row == null) return;
        row.IsSelected = !row.IsSelected;
        SelectionAnchor = row;

        if (row.IsSelected && SelectedTracks.Count == 1) SelectedTrack = row;

        RaiseSelectionChanged();
    }

    /// <summary>
    /// Applies a Shift+click: selects the contiguous range from the anchor to
    /// <paramref name="row"/> in <see cref="SelectedPlaylistTracks"/> order.
    /// When <paramref name="additive"/> is true (Ctrl+Shift), rows outside the
    /// range are left untouched; otherwise the existing selection is cleared.
    /// If there is no anchor, behaves like <see cref="SelectSingle"/>.
    /// </summary>
    public void SelectRange(TrackRowViewModel row, bool additive)
    {
        if (row == null) return;

        if (SelectionAnchor == null || !SelectedPlaylistTracks.Contains(SelectionAnchor))
        {
            SelectSingle(row);
            return;
        }

        var anchor = SelectionAnchor;
        var list = SelectedPlaylistTracks;
        var a = list.IndexOf(anchor);
        var b = list.IndexOf(row);
        if (a < 0 || b < 0) { SelectSingle(row); return; }

        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);

        if (!additive)
            foreach (var r in list) r.IsSelected = false;

        for (var i = lo; i <= hi; i++)
            list[i].IsSelected = true;

        if (SelectedTracks.Count == 1) SelectedTrack = SelectedTracks[0];
        else if (SelectedTracks.Count != 1) SelectedTrack = null;

        SelectionAnchor = anchor;
        RaiseSelectionChanged();

        // Anchor stays at the original click so subsequent Shift+clicks extend from it.
    }

    #endregion
}