using Ognia.Wpf.Library;
using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace Ognia.Wpf.ViewModels.RightPane;

/// <summary>
/// Right-pane data context for a single selected track.
/// Self-refreshing: subscribes to <see cref="AppSession"/> so the view can be
/// constructed once and reused without the host having to call Refresh().
/// </summary>
public sealed class TrackDetailsViewModel : ViewModelBase, IDisposable
{
    private readonly AppSession _session;

    private TrackRowViewModel? _observedRow;

    private string _renameInput = string.Empty;
    private bool _isRenameOpen;

    private Models.Playlist.PlaylistResponse? _moveTarget;

    public TrackDetailsViewModel(AppSession session)
    {
        _session = session;

        RenameTrackCommand = new RelayCommand(
            _ => OpenRename(),
            _ => SelectedTrack != null);

        ConfirmRenameCommand = new RelayCommand(
            async _ => await ConfirmRenameAsync(),
            _ => _isRenameOpen
                 && !string.IsNullOrWhiteSpace(_renameInput)
                 && _renameInput.Trim() != (SelectedTrack?.Name ?? string.Empty));

        CancelRenameCommand = new RelayCommand(_ => CloseRename(), _ => _isRenameOpen);

        RemoveTrackCommand = new RelayCommand(
            async _ => await RemoveTrackAsync(),
            _ => SelectedTrack != null && SelectedPlaylist != null && !_session.IsBusy);

        DownloadCommand = new RelayCommand(
            _ => _session.Download.DownloadTrackCommand.Execute(null),
            _ => SelectedTrack != null && !SelectedTrack.IsDownloaded && !_session.Download.IsDownloading);

        RemoveDownloadCommand = new RelayCommand(
            _ => RemoveDownload(),
            _ => SelectedTrack != null
                 && LocalLibrary.FindLocalFile(SelectedTrack.PlaylistName, SelectedTrack.Name) != null);

        OpenLocationCommand = new RelayCommand(
            _ => OpenLocation(),
            _ => SelectedTrack != null
                 && LocalLibrary.FindLocalFile(SelectedTrack.PlaylistName, SelectedTrack.Name) != null);

        MoveCommand = new RelayCommand(
            async _ => await MoveToPlaylistAsync(),
            _ => _moveTarget != null && SelectedTrack != null && SelectedPlaylist != null && !_session.IsBusy);

        _session.PropertyChanged += OnSessionPropertyChanged;
        ObserveSelectedTrack(_session.SelectedTrack);
    }

    #region Observed state

    private TrackRowViewModel? SelectedTrack => _session.SelectedTrack;
    private Models.Playlist.PlaylistResponse? SelectedPlaylist => _session.SelectedPlaylist;

    public bool HasTrack => SelectedTrack != null;

    public string TrackName => SelectedTrack?.Name ?? string.Empty;
    public string? TrackThumbnailPath => SelectedTrack?.ThumbnailPath;

    public TimeSpan? TrackDuration => SelectedTrack?.Duration;
    public string TrackDurationLabel
    {
        get
        {
            var d = TrackDuration ?? TimeSpan.Zero;
            return d.TotalHours >= 1
                ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
                : d.ToString(@"mm\:ss");
        }
    }

    public string? TrackSourceUrl => SelectedTrack?.SourceUrl;

    public bool IsRenameOpen
    {
        get => _isRenameOpen;
        private set
        {
            if (SetProperty(ref _isRenameOpen, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public string RenameInput
    {
        get => _renameInput;
        set
        {
            if (SetProperty(ref _renameInput, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public IEnumerable<Models.Playlist.PlaylistResponse> MoveTargets =>
        _session.Playlists.Where(p => p.Id != SelectedPlaylist?.Id);

    public Models.Playlist.PlaylistResponse? MoveTarget
    {
        get => _moveTarget;
        set
        {
            if (!SetProperty(ref _moveTarget, value)) return;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    #endregion

    #region Commands

    public RelayCommand RenameTrackCommand { get; }
    public RelayCommand ConfirmRenameCommand { get; }
    public RelayCommand CancelRenameCommand { get; }
    public RelayCommand RemoveTrackCommand { get; }
    public RelayCommand DownloadCommand { get; }
    public RelayCommand RemoveDownloadCommand { get; }
    public RelayCommand OpenLocationCommand { get; }
    public RelayCommand MoveCommand { get; }

    #endregion

    #region Session wiring

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSession.SelectedTrack))
            ObserveSelectedTrack(_session.SelectedTrack);

        RaiseDependents();
    }

    private void ObserveSelectedTrack(TrackRowViewModel? row)
    {
        if (ReferenceEquals(_observedRow, row)) return;

        if (_observedRow != null) _observedRow.PropertyChanged -= OnRowPropertyChanged;
        _observedRow = row;
        if (_observedRow != null) _observedRow.PropertyChanged += OnRowPropertyChanged;

        // Selection change resets transient state.
        _moveTarget = null;
        _isRenameOpen = false;
        _renameInput = string.Empty;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only re-raise the ones the pane binds to.
        switch (e.PropertyName)
        {
            case nameof(TrackRowViewModel.ThumbnailPath):
                OnPropertyChanged(nameof(TrackThumbnailPath));
                break;
            case nameof(TrackRowViewModel.Name):
                OnPropertyChanged(nameof(TrackName));
                break;
            case nameof(TrackRowViewModel.IsDownloaded):
                OnPropertyChanged(nameof(HasTrack));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                break;
        }
    }

    private void RaiseDependents()
    {
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(TrackName));
        OnPropertyChanged(nameof(TrackThumbnailPath));
        OnPropertyChanged(nameof(TrackDuration));
        OnPropertyChanged(nameof(TrackDurationLabel));
        OnPropertyChanged(nameof(TrackSourceUrl));
        OnPropertyChanged(nameof(IsRenameOpen));
        OnPropertyChanged(nameof(RenameInput));
        OnPropertyChanged(nameof(MoveTarget));
        OnPropertyChanged(nameof(MoveTargets));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Kept for the host's convenience; the VM self-refreshes anyway.</summary>
    public void Refresh() => RaiseDependents();

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionPropertyChanged;
        if (_observedRow != null) _observedRow.PropertyChanged -= OnRowPropertyChanged;
        _observedRow = null;
    }

    #endregion

    #region Rename

    private void OpenRename()
    {
        RenameInput = SelectedTrack?.Name ?? string.Empty;
        IsRenameOpen = true;
    }

    private void CloseRename()
    {
        IsRenameOpen = false;
        RenameInput = string.Empty;
    }

    private async Task ConfirmRenameAsync()
    {
        if (SelectedTrack == null || SelectedPlaylist == null) return;

        var newName = _renameInput.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedTrack.Name)
        {
            CloseRename();
            return;
        }

        var clash = _session.SelectedPlaylistTracks
            .Any(t => t.Id != SelectedTrack.Id
                      && string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase));

        if (clash)
        {
            NotificationService.Instance.Error($"A track named \"{newName}\" already exists in this playlist.");
            return;
        }

        var trackId = SelectedTrack.Id;
        var oldName = SelectedTrack.Name;

        var result = await _session.Repo.RenameTrackAsync(
            trackId, new Models.Track.RenameTrackRequest { Name = newName });
        if (!result.Success)
        {
            NotificationService.Instance.Error(result.Error ?? "Failed to rename track.");
            return;
        }

        foreach (var playlist in _session.Playlists)
        {
            LocalLibrary.RenameLocalFile(playlist.Name, oldName, newName);
            LocalLibrary.RenameLocalThumbnail(playlist.Name, oldName, newName);
        }

        await _session.ReloadSelectedPlaylistTracksAsync();
        _session.SelectedTrack = _session.SelectedPlaylistTracks.FirstOrDefault(t => t.Id == trackId);

        CloseRename();
        NotificationService.Instance.Info($"Renamed track to '{newName}'");
    }

    #endregion

    #region Remove / move / cleanup

    private async Task RemoveTrackAsync()
    {
        if (SelectedPlaylist == null || SelectedTrack == null) return;

        var removed = SelectedTrack;
        var playlistName = SelectedPlaylist.Name;

        if (MessageBox.Show(
                $"Remove \"{removed.Name}\" from \"{playlistName}\"?",
                "Remove track",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await _session.RunBusyAsync(async () =>
        {
            var result = await _session.Repo.RemoveTrackAsync(SelectedPlaylist.Id, removed.Id);
            if (!result.Success)
            {
                NotificationService.Instance.Error(result.Error ?? "Failed to remove track.");
                return;
            }

            LocalLibrary.DeleteLocalFile(playlistName, removed.Name);
            LocalLibrary.DeleteLocalThumbnail(playlistName, removed.Name);

            await _session.ReloadSelectedPlaylistTracksAsync();
            _session.SelectedTrack = null;
            await _session.LoadPlaylistsAsync();

            NotificationService.Instance.Info($"Removed '{removed.Name}' from playlist");
        });
    }

    private async Task MoveToPlaylistAsync()
    {
        if (SelectedTrack == null || SelectedPlaylist == null || _moveTarget == null) return;

        var source = SelectedPlaylist;
        var target = _moveTarget;
        var trackId = SelectedTrack.Id;

        await _session.RunBusyAsync(async () =>
        {
            var result = await _session.Repo.MoveTracksAsync(source.Id, target.Id, new[] { trackId });

            await _session.ReloadSelectedPlaylistTracksAsync();
            await _session.LoadPlaylistsAsync();

            if (result.Errors.Count > 0)
                NotificationService.Instance.Error($"Move failed: {result.Errors[0]}");
            else if (result.Skipped > 0)
                NotificationService.Instance.Info($"Already present in '{target.Name}'; removed from source.");
            else
                NotificationService.Instance.Info($"Moved track to '{target.Name}'.");
        });
    }

    private void OpenLocation()
    {
        if (SelectedTrack == null) return;
        var path = LocalLibrary.FindLocalFile(SelectedTrack.PlaylistName, SelectedTrack.Name);
        if (path == null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void RemoveDownload()
    {
        if (SelectedTrack == null) return;

        if (MessageBox.Show(
                $"Delete the downloaded file for \"{SelectedTrack.Name}\"?",
                "Remove download",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _session.Download.RemoveDownloadCommand.Execute(null);
    }

    #endregion
}