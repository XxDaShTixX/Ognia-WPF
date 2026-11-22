using Ognia.Wpf.Repos;

namespace Ognia.Wpf.Services;

/// <summary>
/// Guest-mode transfer: reads/writes the local library.json via LocalLibraryStore.
/// </summary>
public sealed class LocalLibraryTransfer : ILibraryTransfer
{
    private readonly LocalLibraryStore _store;

    public LocalLibraryTransfer(LocalLibraryStore? store = null)
        => _store = store ?? new LocalLibraryStore();

    public Task<(bool Success, string? Error, int PlaylistsExported)> ExportAsync(string path)
    {
        var library = _store.Load();
        var count = library.Playlists.Count;
        var ok = _store.ExportTo(path);
        return Task.FromResult<(bool, string?, int)>(
            ok ? (true, null, count) : (false, "Failed to export the library.", 0));
    }

    public Task<(bool Success, string? Error, int PlaylistsImported)> ImportAsync(string path)
    {
        var result = _store.ImportFrom(path);
        return Task.FromResult(result);
    }
}