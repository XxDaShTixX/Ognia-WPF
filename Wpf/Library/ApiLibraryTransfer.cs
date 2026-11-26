using Ognia.Wpf.Repos;
using System.IO;
using System.Text.Json;

namespace Ognia.Wpf.Library;

/// <summary>
/// Account-mode transfer: export pulls the whole library from the server via IMusicRepo;
/// import pushes a LocalLibraryFileModel through LibraryUploader.
/// </summary>
public sealed class ApiLibraryTransfer : ILibraryTransfer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IMusicRepo _repo;
    private readonly LibraryExporter _exporter;
    private readonly LibraryUploader _uploader;

    public ApiLibraryTransfer(IMusicRepo repo)
    {
        _repo = repo;
        _exporter = new LibraryExporter(repo);
        _uploader = new LibraryUploader(repo);
    }

    public async Task<(bool Success, string? Error, int PlaylistsExported)> ExportAsync(string path)
    {
        var (ok, error, model) = await _exporter.BuildAsync();
        if (!ok || model == null)
            return (false, error ?? "Export failed.", 0);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(model, Options));
            return (true, null, model.Playlists.Count);
        }
        catch (Exception ex)
        {
            return (false, $"Failed to write file: {ex.Message}", 0);
        }
    }

    public async Task<(bool Success, string? Error, int PlaylistsImported)> ImportAsync(string path)
    {
        var store = new LocalLibraryStore();
        var library = store.LoadFrom(path);

        if (library.SchemaVersion != 1)
            return (false, $"Unsupported schema version {library.SchemaVersion}.", 0);

        if (library.Playlists.Count == 0)
            return (false, "File contains no playlists.", 0);

        var (playlists, _, _, invalid, error) = await _uploader.UploadAsync(library);
        if (error != null)
            return (false, error, playlists);

        if (invalid.Count > 0)
            return (true,
                $"{invalid.Count} URL(s) could not be imported (unavailable or unsupported).",
                playlists);

        return (true, null, playlists);
    }
}