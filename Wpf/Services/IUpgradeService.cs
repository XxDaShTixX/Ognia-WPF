namespace Ognia.Wpf.Services;

public interface IUpgradeService
{
    Task<(bool Success, string? Error, string? BackupPath)>
        PrepareAsync();

    Task CompleteAsync(string backupPath);

    void Begin(string backupPath);
}