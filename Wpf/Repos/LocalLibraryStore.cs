using Ognia.Wpf.Helpers;
using Ognia.Wpf.Models.Local;
using System.IO;
using System.Text.Json;

namespace Ognia.Wpf.Repos;

/// <summary>
/// Reads and writes the guest library JSON file. All writes are atomic
/// (temp file + replace) so a crash mid-write cannot corrupt the library.
/// </summary>
public class LocalLibraryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public LocalLibraryStore()
    {
        _path = Path.Combine(AppPaths.AppDataFolder, "library.json");
    }

    public LocalLibraryFileModel Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new LocalLibraryFileModel();

            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<LocalLibraryFileModel>(json, Options);
            return file ?? new LocalLibraryFileModel();
        }
        catch
        {
            // Corrupt or unreadable file — start fresh rather than crash.
            return new LocalLibraryFileModel();
        }
    }

    public void Save(LocalLibraryFileModel library)
    {
        Directory.CreateDirectory(AppPaths.AppDataFolder);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(library, Options));
        File.Move(temp, _path, overwrite: true);
    }

    public bool ExportTo(string destinationPath)
    {
        try
        {
            var library = Load();
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            var json = JsonSerializer.Serialize(library, Options);
            File.WriteAllText(destinationPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a library json from an arbitrary path (used by import paths that
    /// stage a file before merging). Returns an empty model on any failure.
    /// </summary>
    public LocalLibraryFileModel LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new LocalLibraryFileModel();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LocalLibraryFileModel>(json, Options)
                   ?? new LocalLibraryFileModel();
        }
        catch
        {
            return new LocalLibraryFileModel();
        }
    }

    public (bool Success, string? Error, int PlaylistsImported) ImportFrom(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return (false, "File not found.", 0);

            var json = File.ReadAllText(sourcePath);
            var incoming = JsonSerializer.Deserialize<LocalLibraryFileModel>(json, Options);

            if (incoming == null)
                return (false, "File could not be read.", 0);

            if (incoming.SchemaVersion != 1)
                return (false, $"Unsupported schema version {incoming.SchemaVersion}.", 0);

            var current = Load();

            // Merge: dedupe tracks by (SourceName, SourceId). Playlists are matched by name;
            // an existing same-named playlist receives any new tracks from the incoming one.
            // Tracks[] in the incoming file are only added if some incoming link references them.
            var importedPlaylists = 0;

            foreach (var incomingPlaylist in incoming.Playlists)
            {
                var target = current.Playlists.FirstOrDefault(p =>
                    string.Equals(p.Name, incomingPlaylist.Name, StringComparison.OrdinalIgnoreCase));

                var isNewPlaylist = target == null;

                if (isNewPlaylist)
                {
                    var nextPlaylistId = current.Playlists.Count == 0
                        ? 1
                        : current.Playlists.Max(p => p.Id) + 1;

                    target = new LocalPlaylistModel
                    {
                        Id = nextPlaylistId,
                        Name = incomingPlaylist.Name,
                        CreatedAt = incomingPlaylist.CreatedAt
                    };
                    current.Playlists.Add(target);
                }

                // Source identities already linked to the target playlist.
                var linkedKeys = target!.Tracks
                    .Select(pt => current.Tracks.FirstOrDefault(t => t.Id == pt.TrackId))
                    .Where(t => t != null)
                    .Select(t => (t!.SourceName, t.SourceId))
                    .ToHashSet();

                var addedAny = false;

                foreach (var incomingLink in incomingPlaylist.Tracks.OrderBy(t => t.Position))
                {
                    var incomingTrack = incoming.Tracks.FirstOrDefault(t => t.Id == incomingLink.TrackId);
                    if (incomingTrack == null) continue;

                    var key = (incomingTrack.SourceName, incomingTrack.SourceId);
                    if (linkedKeys.Contains(key)) continue;

                    var existingTrack = current.Tracks.FirstOrDefault(t =>
                        t.SourceName == incomingTrack.SourceName && t.SourceId == incomingTrack.SourceId);

                    if (existingTrack == null)
                    {
                        var nextTrackId = current.Tracks.Count == 0
                            ? 1
                            : current.Tracks.Max(t => t.Id) + 1;

                        existingTrack = new LocalTrackModel
                        {
                            Id = nextTrackId,
                            SourceName = incomingTrack.SourceName,
                            SourceId = incomingTrack.SourceId,
                            Name = incomingTrack.Name,
                            Duration = incomingTrack.Duration,
                            ThumbnailUrl = incomingTrack.ThumbnailUrl
                        };

                        current.Tracks.Add(existingTrack);
                    }

                    var nextPosition = target.Tracks.Count == 0
                        ? 0
                        : target.Tracks.Max(pt => pt.Position) + 1;

                    target.Tracks.Add(new LocalPlaylistTrackModel
                    {
                        TrackId = existingTrack.Id,
                        Position = nextPosition,
                        AddedAt = DateTime.UtcNow
                    });

                    linkedKeys.Add(key);
                    addedAny = true;
                }

                if (isNewPlaylist || addedAny)
                    importedPlaylists++;
            }

            Save(current);
            return (true, null, importedPlaylists);
        }
        catch (Exception ex)
        {
            return (false, $"Import failed: {ex.Message}", 0);
        }
    }
}