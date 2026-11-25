using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Repos;
using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using Ognia.Wpf.ViewModels;
using Ognia.Wpf.Views.Auth;
using Ognia.Wpf.Views.MainPane;
using Ognia.Wpf.Views.MainPane.Settings;
using Ognia.Wpf.Views.RightPane;
using Ognia.Wpf.Views.Shell;
using System.Windows;

namespace Ognia.Wpf.Navigation;

public sealed class AppNavigator : IAppNavigator
{
    private readonly ApiClientService _api;
    private readonly Action<object> _setScreen;
    private AppSession? _session;
    private PlaybackViewModel? _playback;
    private DownloadViewModel? _download;
    private TrackDetailsViewModel? _rightVm;
    private MultiTrackSelectionViewModel? _multiVm;
    private ShellView? _shell;

    public AppNavigator(ApiClientService api, Action<object> setScreen)
    {
        _api = api;
        _setScreen = setScreen;
    }

    public event Action<object>? MainContentChanged;
    public event Action<object?>? RightContentChanged;

    public PlaybackViewModel? Playback => _playback;

    #region IShellNavigator

    public void ShowLibrary()
    {
        // Leave SelectedPlaylist alone; the Library view doesn't care, and it
        // preserves the id used to restore the playback bar on next launch.
        _session!.SelectedTrack = null;
        MainContentChanged?.Invoke(new LibraryView
        {
            DataContext = new LibraryViewModel(_session, this)
        });
    }

    public void ShowPlaylist(PlaylistResponse playlist)
    {
        _session!.SelectedPlaylist = playlist;

        PlaybackSessionState.Instance.LastOpenedPlaylistId = playlist.Id;
        AppSettings.Instance.Save();

        MainContentChanged?.Invoke(new PlaylistView
        {
            DataContext = new PlaylistViewModel(_session, this)
        });
    }

    public void ShowSettings()
    {
        MainContentChanged?.Invoke(new SettingsView(_api, this, _session!));
    }

    public void ShowSearch()
    {
        _session!.SelectedTrack = null;
        MainContentChanged?.Invoke(new SearchView
        {
            DataContext = new SearchViewModel(_session)
        });
    }

    public void ShowTrackDetails(object? _) => RefreshRightPane();

    public void ClearRightPane()
    {
        if (_session == null) return;
        _session.SelectedTrack = null;
        RightContentChanged?.Invoke(null);
    }

    #endregion

    #region IAppNavigator

    public bool HasPendingUpgrade => _pendingUpgradePath != null;
    private string? _pendingUpgradePath;

    public void EnterGuestMode()
    {
        NotificationService.Instance.Info("Logged in as Guest.");
        AppSettings.Instance.Mode = Enums.AppMode.Guest;
        AppSettings.Instance.Save();
        ShowShell(new LocalMusicRepo());
    }

    public void ShowLanding() => SetScreen(new LandingView(_api, this));

    public void ShowLogin() => SetScreen(new LoginView(_api, this));

    public void ShowResetPassword() => SetScreen(new ResetPasswordView(_api, this));

    public void RestoreShellContent()
    {
        if (_shell == null) return;
        SetScreen(_shell);
    }

    public void Logout()
    {
        DisposeShell();

        if (AppSettings.Instance.Mode == Enums.AppMode.Guest)
        {
            AppSettings.Instance.Mode = null;
            AppSettings.Instance.Save();
        }

        TokenStore.Clear();
        ShowLanding();
    }

    public async Task<(bool Success, string? Error, string? BackupPath)> PrepareUpgradeAsync()
    {
        var store = new LocalLibraryStore();
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"ognia-upgrade-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        if (!store.ExportTo(tempPath))
            return (false, "Could not back up the local library.", null);

        return await Task.FromResult<(bool, string?, string?)>((true, null, tempPath));
    }

    public void BeginUpgrade(string backupPath)
    {
        _pendingUpgradePath = backupPath;
        AppSettings.Instance.Mode = null;
        AppSettings.Instance.Save();
        ShowLogin();
    }

    public async Task CompleteUpgradeAsync()
    {
        if (_pendingUpgradePath == null) return;

        try
        {
            var store = new LocalLibraryStore();
            var library = store.LoadFrom(_pendingUpgradePath);

            var uploader = new LibraryUploader(new ApiMusicRepo(_api));
            var (playlists, added, skipped, invalid, error) = await uploader.UploadAsync(library);

            if (error != null)
            {
                MessageBox.Show($"Upgrade failed: {error}", "Upgrade",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                var msg = $"Uploaded {playlists} playlist(s), {added} URL(s). " +
                          $"{skipped} already present.";
                if (invalid.Count > 0)
                    msg += $" {invalid.Count} URL(s) could not be imported.";

                MessageBox.Show(msg, "Upgrade complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            _pendingUpgradePath = null;
        }

        ShowShell(new ApiMusicRepo(_api));
    }

    #endregion

    #region Shell lifecycle

    public void ShowShell(IMusicRepo repo)
    {
        DisposeShell();

        _session = CreateSession(repo);
        InitializePlayback(_session);
        InitializeRightPane(_session);

        _shell = CreateShell(_session);

        SubscribeEvents();

        ShowLibrary();

        ConfigurePlaybackBar();

        RefreshRightPane();

        SetScreen(_shell);
    }

    private void RefreshRightPane()
    {
        if (_session == null) { RightContentChanged?.Invoke(null); return; }

        if (_session.HasMultiSelection && _multiVm != null)
        {
            _multiVm.OnPropertyChangedPublic();
            RightContentChanged?.Invoke(new MultiTrackActionsView { DataContext = _multiVm });
            return;
        }

        if (_rightVm == null || _session.SelectedTrack == null)
        {
            RightContentChanged?.Invoke(null);
            return;
        }

        // Fresh view per selection — kills stale-binding and stale-CanExecute issues.
        // The VM instance is preserved, so its subscriptions stay alive.
        _rightVm.Refresh();
        RightContentChanged?.Invoke(new TrackDetailsView { DataContext = _rightVm });
    }

    /// <summary>
    /// Cleans up the active shell, unsubscribes event handlers,
    /// disposes owned view models, and clears references so the
    /// shell can be safely recreated.
    /// </summary>
    public void DisposeShell()
    {
        UnsubscribeEvents();

        if (_session != null)
        {
            _session.Details = null;
        }

        _rightVm?.Dispose();
        _rightVm = null;
        _multiVm = null;

        _playback?.Dispose();
        _playback = null;
        _download = null;
        _session = null;

        _shell = null;
    }

    #endregion

    private void SetScreen(object screen) => _setScreen(screen);

    private AppSession CreateSession(IMusicRepo repo)
    {
        var session = new AppSession(repo);
        session.Transfer = LibraryTransferFactory.Create(repo, isGuest: repo is LocalMusicRepo);
        return session;
    }

    private void InitializeRightPane(AppSession session)
    {
        _rightVm = new TrackDetailsViewModel(session);
        _multiVm = new MultiTrackSelectionViewModel(session);

        session.Details = _rightVm;
        session.SelectionChanged += RefreshRightPane;
    }

    private void InitializePlayback(AppSession session)
    {
        _playback = new PlaybackViewModel();
        _download = new DownloadViewModel();

        session.AttachPlayer(_playback, _download);
    }

    private ShellView CreateShell(AppSession session)
    {
        return new ShellView
        {
            DataContext = new ShellViewModel(session, this)
        };
    }

    private void ConfigurePlaybackBar()
    {
        ArgumentNullException.ThrowIfNull(_shell);
        ArgumentNullException.ThrowIfNull(_session);
        ArgumentNullException.ThrowIfNull(_playback);
        ArgumentNullException.ThrowIfNull(_download);

        _shell.SetPlaybackContent(new PlaybackBarView
        {
            DataContext = new PlaybackBarViewModel(_session, _playback, _download)
        });
    }

    private void SubscribeEvents()
    {
        ArgumentNullException.ThrowIfNull(_shell);

        // Subscribe
        MainContentChanged += _shell.SetMainContent;
        RightContentChanged += _shell.SetRightContent;
    }

    private void UnsubscribeEvents()
    {
        if (_shell != null)
        {
            MainContentChanged -= _shell.SetMainContent;
            RightContentChanged -= _shell.SetRightContent;
        }

        if (_session != null)
        {
            _session.SelectionChanged -= RefreshRightPane;
        }
    }
}