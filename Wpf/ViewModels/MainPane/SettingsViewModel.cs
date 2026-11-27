using Ognia.Wpf.Authentication;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Enums;
using Ognia.Wpf.Models.Auth;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Repos;
using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using Ognia.Wpf.Theming;
using System.Windows;

namespace Ognia.Wpf.ViewModels.MainPane;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ApiClientService _api;
    private readonly IAppNavigator _nav;
    private readonly UpdateService _updates = UpdateService.Instance;

    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;

    private string? _availableVersion;
    private string? _updateStatus;
    private bool _isChecking;

    public SettingsViewModel(ApiClientService api, IAppNavigator nav, AppSession session)
    {
        _api = api;
        _nav = nav;
        Session = session;

        ChangePasswordCommand = new RelayCommand(
            async _ => await ChangePasswordAsync(),
            _ => !string.IsNullOrWhiteSpace(CurrentPassword)
                 && !string.IsNullOrWhiteSpace(NewPassword)
                 && NewPassword == ConfirmPassword);

        LogoutCommand = new RelayCommand(async _ => await LogoutAsync());

        CheckForUpdatesCommand = new RelayCommand(
            async _ => await CheckForUpdatesAsync(),
            _ => CanCheckForUpdates);

        ApplyUpdateCommand = new RelayCommand(
            _ => ApplyUpdate(),
            _ => HasUpdate);

        OpenFolderCommand = new RelayCommand(
            p => OpenFolder(p as string),
            p => p is string s && !string.IsNullOrWhiteSpace(s));

        AvailableThemes = ThemeService.EnumerateThemes();

        var applied = ThemeService.ActiveThemeFileName
                      ?? AppSettings.Instance.ActiveTheme
                      ?? "Default.json";

        _selectedTheme = AvailableThemes
            .FirstOrDefault(t => string.Equals(t.FileName, applied, StringComparison.OrdinalIgnoreCase))
            ?? AvailableThemes.FirstOrDefault();

        _updates.StateChanged += OnUpdateStateChanged;

        if (_updates.IsInstalled)
            _availableVersion = _updates.PendingVersion;
    }

    public AppSession Session { get; }

    public bool IsGuestMode => AppSettings.Instance.Mode == AppMode.Guest;
    public bool IsAccountMode => !IsGuestMode;

    public IReadOnlyList<ThemeInfo> AvailableThemes { get; }

    private ThemeInfo? _selectedTheme;
    public ThemeInfo? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            if (value == null) return;

            if (ThemeService.Apply(value.FileName))
            {
                AppSettings.Instance.ActiveTheme = value.FileName;
                AppSettings.Instance.Save();
                NotificationService.Instance.Info($"Applied theme '{value.Name}'.");
            }
            else
            {
                NotificationService.Instance.Error("Failed to apply theme.");
            }
        }
    }

    public string CurrentPassword { get => _currentPassword; set { if (SetProperty(ref _currentPassword, value)) Requery(); } }
    public string NewPassword { get => _newPassword; set { if (SetProperty(ref _newPassword, value)) Requery(); } }
    public string ConfirmPassword { get => _confirmPassword; set { if (SetProperty(ref _confirmPassword, value)) Requery(); } }

    public string Email => IsGuestMode ? "(Guest)" : TokenStore.Email ?? "Unknown";

    #region Appearance

    public double MainWindowOpacityPercent
    {
        get => Math.Round(AppSettings.Instance.Window.MainWindowOpacity * 100);
        set
        {
            var clamped = Math.Clamp(value, 20, 100);
            var normalized = clamped / 100.0;
            if (Math.Abs(AppSettings.Instance.Window.MainWindowOpacity - normalized) < 0.001) return;

            AppSettings.Instance.Window.MainWindowOpacity = normalized;
            AppSettings.Instance.Save();
            OnPropertyChanged();

            if (System.Windows.Application.Current.MainWindow is { } win)
                win.Opacity = normalized;
        }
    }

    public double MiniWindowOpacityPercent
    {
        get => Math.Round(AppSettings.Instance.Window.MiniRestingOpacity * 100);
        set
        {
            var clamped = Math.Clamp(value, 1, 100);
            var normalized = clamped / 100.0;
            if (Math.Abs(AppSettings.Instance.Window.MiniRestingOpacity - normalized) < 0.001) return;

            AppSettings.Instance.Window.MiniRestingOpacity = normalized;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    #endregion

    #region Folders

    public string MusicFolderPath => AppPaths.MusicFolder;
    public string AppDataFolderPath => AppPaths.AppDataFolder;
    public string ThemesFolderPath => AppPaths.ThemesFolder;

    public RelayCommand OpenFolderCommand { get; }

    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            System.IO.Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            NotificationService.Instance.Error($"Could not open folder: {ex.Message}");
        }
    }

    #endregion

    #region Updates

    public string AppVersion => _updates.DisplayVersion;
    public bool IsUpdateInstalled => _updates.IsInstalled;

    public bool AutoUpdateEnabled
    {
        get => AppSettings.Instance.Update.AutoUpdateEnabled;
        set
        {
            if (AppSettings.Instance.Update.AutoUpdateEnabled == value) return;
            AppSettings.Instance.Update.AutoUpdateEnabled = value;
            AppSettings.Instance.Save();
            OnPropertyChanged();
        }
    }

    public string? AvailableVersion
    {
        get => _availableVersion;
        private set
        {
            if (!SetProperty(ref _availableVersion, value)) return;
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(AvailableVersionText));
        }
    }

    public bool HasUpdate => _availableVersion != null;

    public string AvailableVersionText => _availableVersion == null ? string.Empty : $"Update v{_availableVersion} available!";

    public string? UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (!SetProperty(ref _isChecking, value)) return;
            OnPropertyChanged(nameof(CanCheckForUpdates));
            Requery();
        }
    }

    public bool UsePrereleaseChannel
    {
        get => string.Equals(
            AppSettings.Instance.Update.Channel,
            "prerelease",
            StringComparison.OrdinalIgnoreCase);
        set
        {
            var channel = value ? "prerelease" : "stable";
            var changed = !string.Equals(
                AppSettings.Instance.Update.Channel,
                channel,
                StringComparison.OrdinalIgnoreCase);

            if (changed)
            {
                AppSettings.Instance.Update.Channel = channel;
                AppSettings.Instance.Save();
                _updates.ReloadChannel();
                AvailableVersion = null;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(UseStableChannel));

            if (changed)
            {
                UpdateStatus = value
                    ? "Switched to pre-release channel. Check for updates."
                    : "Switched to stable channel. Check for updates.";
            }
        }
    }

    public bool UseStableChannel
    {
        get => !UsePrereleaseChannel;
        set
        {
            if (value)
                UsePrereleaseChannel = false;
            else
                OnPropertyChanged();
        }
    }

    public bool CanCheckForUpdates => IsUpdateInstalled && !IsChecking;

    public RelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand ApplyUpdateCommand { get; }

    private void OnUpdateStateChanged()
    {
        if (!_updates.IsInstalled) return;
        AvailableVersion = _updates.PendingVersion;
    }

    private async Task CheckForUpdatesAsync()
    {
        IsChecking = true;
        UpdateStatus = "Checking for updates…";
        try
        {
            var version = await _updates.CheckAsync(downloadIfFound: true);
            if (version == null)
            {
                AvailableVersion = null;
                UpdateStatus = "You're up to date.";
            }
            else
            {
                AvailableVersion = version;
                UpdateStatus = AutoUpdateEnabled
                    ? $"v{version} downloaded. Restart to apply."
                    : $"v{version} available.";
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void ApplyUpdate()
    {
        if (!HasUpdate) return;

        var result = MessageBox.Show(
            $"Update to v{AvailableVersion}? The app will restart.",
            "Update Ognia",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _ = ApplyUpdateAsync();
    }

    private async Task ApplyUpdateAsync()
    {
        if (!_updates.PendingIsDownloaded)
        {
            UpdateStatus = "Downloading update…";
            var version = await _updates.CheckAsync(downloadIfFound: true);
            if (version == null)
            {
                UpdateStatus = "Update failed.";
                return;
            }
        }

        UpdateStatus = "Applying update…";
        _updates.ApplyAndRestart();
    }

    #endregion

    public RelayCommand ChangePasswordCommand { get; }
    public RelayCommand LogoutCommand { get; }

    private static void Requery() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    private async Task ChangePasswordAsync()
    {
        NotificationService.Instance.Info("Changing password…");

        var result = await _api.ChangePasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = CurrentPassword,
            NewPassword = NewPassword
        });

        if (!result.Success)
        {
            NotificationService.Instance.Error(result.Error ?? "Change failed.");
            return;
        }

        NotificationService.Instance.Info("Password changed.");
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }

    public async Task<(bool Success, string? Error, int Count)> ExportLibraryAsync(string destinationPath)
    {
        var (ok, error, count) = await Session.Transfer.ExportAsync(destinationPath);
        return (ok, error, count);
    }

    public async Task<(bool Success, string? Error, int Count)> ImportLibraryAsync(string sourcePath)
    {
        var (ok, error, count) = await Session.Transfer.ImportAsync(sourcePath);
        return (ok, error, count);
    }

    public Task<(bool Success, string? Error, string? BackupPath)> PrepareUpgradeAsync()
        => _nav.PrepareUpgradeAsync();

    public void BeginUpgrade(string backupPath) => _nav.BeginUpgrade(backupPath);

    private async Task LogoutAsync()
    {
        await _api.LogoutAsync();
        _nav.Logout();
        NotificationService.Instance.Info("Logged out.");
    }
}