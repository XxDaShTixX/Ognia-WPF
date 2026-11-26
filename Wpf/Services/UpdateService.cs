using Ognia.Wpf.Configuration;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace Ognia.Wpf.Services;

public sealed class UpdateService
{
    private static UpdateService? _instance;
    public static UpdateService Instance => _instance ??= new UpdateService();

    private UpdateManager _manager;
    private UpdateInfo? _pendingUpdate;
    private bool _pendingDownloaded;

    private UpdateService()
    {
        _manager = CreateManager();
    }

    private static bool UsesPrereleases =>
        string.Equals(AppSettings.Instance.Update.Channel, "prerelease", StringComparison.OrdinalIgnoreCase);

    private static UpdateManager CreateManager()
        => new(new GithubSource(
            AppInfo.GitHubRepositoryUrl,
            accessToken: null,
            prerelease: UsesPrereleases));

    /// <summary>
    /// Rebuilds the underlying manager so a channel change takes effect on the next check.
    /// Drops any pending update — the caller is expected to re-check after switching.
    /// </summary>
    public void ReloadChannel()
    {
        _pendingUpdate = null;
        _manager = CreateManager();
        StateChanged?.Invoke();
    }

    public bool IsInstalled
    {
        get { try { return _manager.IsInstalled; } catch { return false; } }
    }

    public string CurrentVersion
    {
        get
        {
            try
            {
                if (_manager.IsInstalled && _manager.CurrentVersion != null)
                    return _manager.CurrentVersion.ToString();
            }
            catch { }

            var asm = typeof(UpdateService).Assembly.GetName().Version;
            return asm == null ? "0.0.0" : $"{asm.Major}.{asm.Minor}.{asm.Build}";
        }
    }

    public string DisplayVersion => $"Ognia v{CurrentVersion}";

    public string? PendingVersion => _pendingUpdate?.TargetFullRelease.Version.ToString();
    public bool PendingIsDownloaded => _pendingDownloaded;

    public event Action? StateChanged;

    public async Task<string?> CheckAsync(bool downloadIfFound)
    {
        try
        {
            if (!_manager.IsInstalled) return null;

            var info = await _manager.CheckForUpdatesAsync();
            if (info == null) return null;

            if (downloadIfFound)
            {
                await _manager.DownloadUpdatesAsync(info);
                _pendingDownloaded = true;
            }
            else
            {
                _pendingDownloaded = false;
            }

            _pendingUpdate = info;
            StateChanged?.Invoke();
            return info.TargetFullRelease.Version.ToString();
        }
        catch
        {
            return null;
        }
    }

    public void ApplyAndRestart()
    {
        if (_pendingUpdate == null || !_pendingDownloaded) return;
        try { _manager.ApplyUpdatesAndRestart(_pendingUpdate); }
        catch { }
    }

    public void ApplyOnExit()
    {
        if (_pendingUpdate == null || !_pendingDownloaded) return;
        try { _manager.WaitExitThenApplyUpdates(_pendingUpdate, silent: true, restart: false); }
        catch { }
    }
}