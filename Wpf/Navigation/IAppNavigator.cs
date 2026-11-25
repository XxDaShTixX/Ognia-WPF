using Ognia.Wpf.Repos;
using Ognia.Wpf.ViewModels;

namespace Ognia.Wpf.Navigation;

public interface IAppNavigator : IShellNavigator
{
    void ShowShell(IMusicRepo repo);
    void DisposeShell();

    void EnterGuestMode();
    void ShowLanding();
    void ShowLogin();
    void ShowResetPassword();
    void RestoreShellContent();
    void Logout();

    bool HasPendingUpgrade { get; }
    Task CompleteUpgradeAsync();

    Task<(bool Success, string? Error, string? BackupPath)> PrepareUpgradeAsync();
    void BeginUpgrade(string backupPath);

    /// <summary>The active playback view model, or null when no shell is shown.</summary>
    PlaybackViewModel? Playback { get; }
}