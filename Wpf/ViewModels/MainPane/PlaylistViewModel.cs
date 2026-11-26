using Ognia.Wpf.Caching;
using Ognia.Wpf.Library;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using Ognia.Wpf.ViewModels.Shell;
using System.Windows;

namespace Ognia.Wpf.ViewModels.MainPane;

public sealed class PlaylistViewModel : ViewModelBase
{
    public AppSession Session { get; }
    private readonly IShellNavigator _nav;

    public PlaylistViewModel(AppSession session, IShellNavigator nav)
    {
        Session = session;
        _nav = nav;
        _playback = session.Playback;

        PlayPlaylistCommand = new RelayCommand(
            async _ => await PlayPlaylistAsync(),
            _ => Session.SelectedPlaylist != null 
                && Session.SelectedPlaylistTracks.Count > 0
                && !Session.IsBusy);

        ToggleShuffleCommand = new RelayCommand(
            _ => _playback.IsShuffleOn = !_playback.IsShuffleOn);

        ToggleFilterCommand = new RelayCommand(_ =>
        {
            Session.PlaylistFilterOpen = !Session.PlaylistFilterOpen;
            if (!Session.PlaylistFilterOpen)
                Session.ClearPlaylistFilter();
        });

        ClearFilterCommand = new RelayCommand(_ => Session.ClearPlaylistFilter());

        OpenFolderCommand = new RelayCommand(
            _ => OpenFolder(),
            _ => Session.SelectedPlaylist != null
                 && LocalLibrary.FindPlaylistFolder(Session.SelectedPlaylist.Name) != null);

        RenamePlaylistCommand = new RelayCommand(
            _ => OpenRename(),
            _ => Session.SelectedPlaylist != null
                && !Session.IsBusy);

        ConfirmRenameCommand = new RelayCommand(
            async _ => await ConfirmRenameAsync(),
            _ => _isRenameOpen
                 && !string.IsNullOrWhiteSpace(_renameInput)
                 && _renameInput.Trim() != (Session.SelectedPlaylist?.Name ?? string.Empty)
                 && !Session.IsBusy);

        CancelRenameCommand = new RelayCommand(_ => CloseRename(), _ => _isRenameOpen);

        AddTrackCommand = new RelayCommand(
            _ => OpenAddTrack(),
            _ => Session.SelectedPlaylist != null
                && !Session.IsBusy);

        ConfirmAddTrackCommand = new RelayCommand(
            async _ => await ConfirmAddTrackAsync(),
            _ => _isAddTrackOpen && !string.IsNullOrWhiteSpace(_addTrackUrl)
                && !Session.IsBusy);

        CancelAddTrackCommand = new RelayCommand(_ => CloseAddTrack(), _ => _isAddTrackOpen);

        DeletePlaylistCommand = new RelayCommand(async _ =>
        {
            if (Session.SelectedPlaylist == null) return;
            await Session.DeletePlaylistAsync(Session.SelectedPlaylist);
            _nav.ShowLibrary();
        },
        _ => Session.SelectedPlaylist != null
            && !Session.IsBusy);


        Session.PlayRequested += t => _ = _playback.PlayTrackAsync(t);

        _playback.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaybackViewModel.IsShuffleOn))
                OnPropertyChanged(nameof(ShuffleLabel));
        };
    }

    private readonly PlaybackViewModel _playback;


    #region Commands

    public RelayCommand PlayPlaylistCommand { get; }
    public RelayCommand ToggleShuffleCommand { get; }
    public RelayCommand ToggleFilterCommand { get; }
    public RelayCommand ClearFilterCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand RenamePlaylistCommand { get; }
    public RelayCommand ConfirmRenameCommand { get; }
    public RelayCommand CancelRenameCommand { get; }
    public RelayCommand AddTrackCommand { get; }
    public RelayCommand ConfirmAddTrackCommand { get; }
    public RelayCommand CancelAddTrackCommand { get; }
    public RelayCommand DeletePlaylistCommand { get; }

    #endregion

    #region Bindable Properties

    public string ShuffleLabel => _playback.IsShuffleOn ? "⇄" : "⊘";

    public bool IsRenameOpen
    {
        get => _isRenameOpen;
        private set
        {
            if (SetProperty(ref _isRenameOpen, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    private bool _isRenameOpen;

    public string RenameInput
    {
        get => _renameInput;
        set
        {
            if (SetProperty(ref _renameInput, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    private string _renameInput = string.Empty;

    public bool IsAddTrackOpen
    {
        get => _isAddTrackOpen;
        private set
        {
            if (SetProperty(ref _isAddTrackOpen, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    private bool _isAddTrackOpen;

    public string AddTrackUrl
    {
        get => _addTrackUrl;
        set
        {
            if (SetProperty(ref _addTrackUrl, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    private string _addTrackUrl = string.Empty;

    #endregion

    #region Play

    private async Task PlayPlaylistAsync()
    {
        if (Session.SelectedPlaylist == null) return;
        if (Session.SelectedPlaylistTracks.Count == 0) return;

        if (!Session.SelectedPlaylistTracks.Any(t => t.Id == _playback.CurrentTrack?.Id))
            await _playback.PlayTrackAsync(Session.SelectedPlaylistTracks[0]);
        else
            _playback.PlayPause();
    }

    private void OpenFolder()
    {
        if (Session.SelectedPlaylist == null) return;
        var folder = LocalLibrary.FindPlaylistFolder(Session.SelectedPlaylist.Name);
        if (folder == null) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    #endregion

    #region Rename

    private void OpenRename()
    {
        IsAddTrackOpen = false;
        RenameInput = Session.SelectedPlaylist?.Name ?? string.Empty;
        IsRenameOpen = true;
    }

    private void CloseRename()
    {
        IsRenameOpen = false;
        RenameInput = string.Empty;
    }

    private async Task ConfirmRenameAsync()
    {
        if (Session.SelectedPlaylist == null) return;

        var newName = _renameInput.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == Session.SelectedPlaylist.Name)
        {
            CloseRename();
            return;
        }

        var playlistId = Session.SelectedPlaylist.Id;
        var oldName = Session.SelectedPlaylist.Name;

        CloseRename();

        await Session.RunBusyAsync(async () =>
        {
            var result = await Session.Repo.RenamePlaylistAsync(
                playlistId, new RenamePlaylistRequest { Name = newName });
            if (!result.Success)
            {
                NotificationService.Instance.Error(result.Error ?? "Failed to rename playlist.");
                return;
            }

            // If the currently playing track belongs to this playlist, stop playback so
            // NAudio releases its file handle — otherwise Directory.Move fails on Windows
            // and the old folder is left orphaned.
            var wasPlayingThisPlaylist =
                _playback.CurrentTrack != null
                && string.Equals(_playback.CurrentTrack.PlaylistName, oldName, StringComparison.OrdinalIgnoreCase)
                && _playback.PlaybackState != NAudio.Wave.PlaybackState.Stopped;

            if (wasPlayingThisPlaylist)
                _playback.Stop();

            LocalLibrary.RenamePlaylistFolder(oldName, newName);

            foreach (var row in Session.SelectedPlaylistTracks)
                row.UpdatePlaylistName(newName);

            await Session.LoadPlaylistsAsync();
            Session.SelectedPlaylist = Session.Playlists.FirstOrDefault(p => p.Id == playlistId);
            await Session.ReloadSelectedPlaylistTracksAsync();

            if (wasPlayingThisPlaylist)
            {
                var resumed = Session.SelectedPlaylistTracks
                    .FirstOrDefault(t => t.Id == _playback.CurrentTrack?.Id);
                if (resumed != null)
                    await _playback.PlayTrackAsync(resumed);
            }

            NotificationService.Instance.Info($"Renamed playlist to '{newName}'");
        });
    }

    #endregion

    #region Add track

    private void OpenAddTrack()
    {
        IsRenameOpen = false;
        AddTrackUrl = string.Empty;
        IsAddTrackOpen = true;
    }

    private void CloseAddTrack()
    {
        IsAddTrackOpen = false;
        AddTrackUrl = string.Empty;
    }

    private async Task ConfirmAddTrackAsync()
    {
        if (Session.SelectedPlaylist == null) return;

        var url = _addTrackUrl.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            CloseAddTrack();
            return;
        }

        var playlistId = Session.SelectedPlaylist.Id;

        CloseAddTrack();

        await Session.RunBusyAsync(async () =>
        {
            NotificationService.Instance.Info("Adding track(s)… this may take a moment for large playlists.");
            await Task.Yield();

            var progress = new Progress<string>(msg => NotificationService.Instance.Info(msg));
            var result = await Session.Repo.AddTracksAsync(playlistId, url, progress);

            if (!result.Success)
            {
                NotificationService.Instance.Error(result.Error ?? "Failed to add track(s).");
                return;
            }

            PlaylistCoverCache.Invalidate(playlistId);
            await Session.LoadPlaylistsAsync();
            await Session.ReloadSelectedPlaylistTracksAsync();

            if (result.Added == 0)
            {
                NotificationService.Instance.Error("Track already in this playlist.");
                return;
            }

            NotificationService.Instance.Info(result.Skipped > 0
                ? $"Added {result.Added}, {result.Skipped} skipped."
                : $"Added {result.Added} track(s).");
        });
    }

    #endregion

    #region Delete

    private async Task DeletePlaylistAsync()
    {
        if (Session.SelectedPlaylist == null) return;

        var deleted = Session.SelectedPlaylist;

        if (MessageBox.Show(
                $"Delete playlist \"{deleted.Name}\"?\n\nThis will also remove its downloaded files.",
                "Delete playlist",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await Session.RunBusyAsync(async () =>
        {
            var result = await Session.Repo.DeletePlaylistAsync(deleted.Id);
            if (!result.Success)
            {
                NotificationService.Instance.Error(result.Error ?? "Failed to delete playlist.");
                return;
            }

            LocalLibrary.DeletePlaylistFolder(deleted.Name);
            Session.SelectedPlaylist = null;
            Session.SelectedTrack = null;
            await Session.LoadPlaylistsAsync();

            _nav.ShowLibrary();
            NotificationService.Instance.Info($"Deleted playlist '{deleted.Name}'");
        });
    }

    #endregion
}