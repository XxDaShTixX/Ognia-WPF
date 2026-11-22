namespace Ognia.Wpf.Services;

/// <summary>
/// Whole-library export/import. One implementation per backing store
/// (local json for guest, server for account). Selected once by mode.
/// </summary>
public interface ILibraryTransfer
{
    /// <summary>
    /// Writes the current library to <paramref name="path"/> as a LocalLibraryFileModel json.
    /// Returns (success, error, playlistCount).
    /// </summary>
    Task<(bool Success, string? Error, int PlaylistsExported)> ExportAsync(string path);

    /// <summary>
    /// Reads a LocalLibraryFileModel json from <paramref name="path"/> and merges it into the
    /// current library. Returns (success, error, playlistsImported).
    /// </summary>
    Task<(bool Success, string? Error, int PlaylistsImported)> ImportAsync(string path);
}