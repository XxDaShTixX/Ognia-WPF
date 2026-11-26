using Ognia.Wpf.Authentication;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Library;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.Repos;
using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using Ognia.Wpf.ViewModels;
using Ognia.Wpf.ViewModels.Shell;
using Ognia.Wpf.Views.Auth;
using Ognia.Wpf.Views.RightPane;
using Ognia.Wpf.Views.Shell;
using System.Windows;

namespace Ognia.Wpf.Navigation;

public sealed class AppNavigator : IAppNavigator
{
    private readonly ApiClientService _api;
    private readonly Action<object> _setScreen;
    private readonly IViewFactory _viewFactory;

    private ShellContext? _context;
    private ShellView? _shell;

    public AppNavigator(ApiClientService api, Action<object> setScreen)
    {
        // Reference
        _api = api;
        _setScreen = setScreen;

        // Instantiate
        _viewFactory = new ViewFactory();
    }

    public PlaybackViewModel? Playback => _context?.Playback;

    #region Events

    public event Action<object>? MainContentChanged;
    public event Action<object?>? RightContentChanged;

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

    #region IShellNavigator

    public void ShowLibrary()
    {
        // Leave SelectedPlaylist alone; the Library view doesn't care, and it
        // preserves the id used to restore the playback bar on next launch.
        _context!.Session.SelectedTrack = null;

        MainContentChanged?.Invoke(
            _viewFactory.CreateLibraryView(_context!.Session, this));
    }

    public void ShowPlaylist(PlaylistResponse playlist)
    {
        _context!.Session.SelectedPlaylist = playlist;

        PlaybackSessionState.Instance.LastOpenedPlaylistId = playlist.Id;
        AppSettings.Instance.Save();

        MainContentChanged?.Invoke(
            _viewFactory.CreatePlaylistView(_context!.Session, this));
    }

    public void ShowSettings()
    {
        MainContentChanged?.Invoke(
            _viewFactory.CreateSettingsView(
                _api,
                this,
                _context!.Session));
    }

    public void ShowSearch()
    {
        _context!.Session.SelectedTrack = null;

        MainContentChanged?.Invoke(
            _viewFactory.CreateSearchView(_context!.Session));
    }

    public void ShowTrackDetails(object? _) => RefreshRightPane();

    public void ClearRightPane()
    {
        if (_context!.Session == null) return;
        _context!.Session.SelectedTrack = null;
        RightContentChanged?.Invoke(null);
    }

    #endregion



    #region Shell lifecycle

    public void ShowShell(IMusicRepo repo)
    {
        DisposeShell();

        var session = CreateSession(repo);

        _context = new ShellContext(session);

        InitializeRightPane(session);

        _shell = CreateShell(session);

        InitializeShell();

        SetScreen(_shell);
    }

    private void RefreshRightPane()
    {
        if (_context == null)
        {
            RightContentChanged?.Invoke(null);
            return;
        }

        if (_context.Session.HasMultiSelection)
        {
            _context.MultiSelection.OnPropertyChangedPublic();

            RightContentChanged?.Invoke(
                new MultiTrackActionsView
                {
                    DataContext = _context.MultiSelection
                });

            return;
        }

        if (_context.Session.SelectedTrack == null)
        {
            RightContentChanged?.Invoke(null);
            return;
        }

        // Fresh view per selection — kills stale-binding and stale-CanExecute issues.
        _context.Details.Refresh();

        RightContentChanged?.Invoke(
            new TrackDetailsView
            {
                DataContext = _context.Details
            });
    }

    private void InitializeShell()
    {
        SubscribeEvents();

        ShowLibrary();

        ConfigurePlaybackBar();

        RefreshRightPane();
    }

    /// <summary>
    /// Cleans up the active shell, unsubscribes event handlers,
    /// disposes owned view models, and clears references so the
    /// shell can be safely recreated.
    /// </summary>
    public void DisposeShell()
    {
        UnsubscribeEvents();

        _context?.Dispose();
        _context = null;

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
        session.SelectionChanged += RefreshRightPane;
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
        ArgumentNullException.ThrowIfNull(_context);

        _shell.SetPlaybackContent(new PlaybackBarView
        {
            DataContext = new PlaybackBarViewModel(_context.Session, _context.Playback, _context.Download)
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

        if (_context != null)
        {
            _context.Session.SelectionChanged -= RefreshRightPane;
        }
    }
}