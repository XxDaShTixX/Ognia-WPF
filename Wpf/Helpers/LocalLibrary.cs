using System.IO;

namespace Ognia.Wpf.Helpers;

public static class LocalLibrary
{
    private static string Root => AppPaths.MusicFolder;

    #region Naming
    /// <summary>
    /// Strips characters Windows disallows in filenames.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Unnamed" : cleaned;
    }

    /// <summary>
    /// Builds the canonical filename for a track: "{name}.m4a".
    /// </summary>
    public static string BuildFileName(string trackName) => $"{SanitizeFileName(trackName)}.m4a";

    /// <summary>
    /// Santizes a folder name by stripping characters Windows disallows in folder names.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static string SanitizeFolderName(string name) => SanitizeFileName(name);

    /// <summary>
    /// Builds the canonical thumbnail filename for a track: "{name}.jpg".
    /// </summary>
    public static string BuildThumbnailFileName(string trackName) => $"{SanitizeFileName(trackName)}.jpg";
    #endregion

    #region Paths
    /// <summary>
    /// Get the folder path of a given playlist.
    /// </summary>
    /// <param name="playlistName"></param>
    /// <returns></returns>
    private static string? PlaylistFolder(string playlistName)
    {
        if (string.IsNullOrWhiteSpace(Root)) return null;
        return Path.Combine(Root, SanitizeFolderName(playlistName));
    }

    /// <summary>
    /// Returns the playlist's folder path if it exists, otherwise null.
    /// </summary>
    public static string? FindPlaylistFolder(string playlistName)
    {
        var folder = PlaylistFolder(playlistName);
        if (folder == null || !Directory.Exists(folder)) return null;
        return folder;
    }

    /// <summary>
    /// Returns the playlist's thumbnail folder path (may not exist).
    /// </summary>
    public static string? ThumbnailFolder(string playlistName)
    {
        if (string.IsNullOrWhiteSpace(Root)) return null;
        return Path.Combine(Root, SanitizeFolderName(playlistName), ".thumbnails");
    }
    #endregion

    #region Lookup
    /// <summary>
    /// Returns the full path to a track's file in a playlist folder, or null if not downloaded.
    /// </summary>
    /// <returns></returns>
    public static string? FindLocalFile(string playlistName, string trackName)
    {
        var folder = PlaylistFolder(playlistName);
        if (folder == null || !Directory.Exists(folder)) return null;

        var path = Path.Combine(folder, BuildFileName(trackName));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns the set of downloaded track names in a playlist folder.
    /// </summary>
    public static HashSet<string> GetDownloadedTrackNames(string playlistName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folder = PlaylistFolder(playlistName);
        if (folder == null || !Directory.Exists(folder)) return result;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.m4a"))
                result.Add(Path.GetFileNameWithoutExtension(file));
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Returns the full path to a track's thumbnail in a playlist folder, or null if missing.
    /// </summary>
    public static string? FindLocalThumbnail(string playlistName, string trackName)
    {
        var folder = ThumbnailFolder(playlistName);
        if (folder == null || !Directory.Exists(folder)) return null;

        var path = Path.Combine(folder, BuildThumbnailFileName(trackName));
        return File.Exists(path) ? path : null;
    }
    #endregion

    #region Operations
    /// <summary>
    /// Deletes a track's file from a playlist folder. Returns true if a file was deleted.
    /// </summary>
    public static bool DeleteLocalFile(string playlistName, string trackName)
    {
        var path = FindLocalFile(playlistName, trackName);
        if (path == null) return false;

        try { File.Delete(path); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Renames a track's file inside a playlist folder. Returns true if renamed.
    /// </summary>
    public static bool RenameLocalFile(string playlistName, string oldName, string newName)
    {
        var folder = PlaylistFolder(playlistName);
        if (folder == null) return false;

        var oldPath = Path.Combine(folder, BuildFileName(oldName));
        var newPath = Path.Combine(folder, BuildFileName(newName));

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!File.Exists(oldPath))
            return false;

        try
        {
            if (File.Exists(newPath)) File.Delete(newPath);
            File.Move(oldPath, newPath);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Renames a playlist's folder. No-op if the old folder does not exist.
    /// </summary>
    public static bool RenamePlaylistFolder(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(Root)) return false;

        var oldFolder = Path.Combine(Root, SanitizeFolderName(oldName));
        var newFolder = Path.Combine(Root, SanitizeFolderName(newName));

        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Directory.Exists(oldFolder))
            return false;

        try
        {
            if (Directory.Exists(newFolder)) Directory.Delete(newFolder, recursive: true);
            Directory.Move(oldFolder, newFolder);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Renames a track's thumbnail inside a playlist folder. Returns true if renamed.
    /// </summary>
    public static bool RenameLocalThumbnail(string playlistName, string oldName, string newName)
    {
        var folder = ThumbnailFolder(playlistName);
        if (folder == null) return false;

        var oldPath = Path.Combine(folder, BuildThumbnailFileName(oldName));
        var newPath = Path.Combine(folder, BuildThumbnailFileName(newName));

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!File.Exists(oldPath))
            return false;

        try
        {
            if (File.Exists(newPath)) File.Delete(newPath);
            File.Move(oldPath, newPath);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Deletes a playlist's folder and everything inside it.
    /// </summary>
    public static bool DeletePlaylistFolder(string playlistName)
    {
        var folder = PlaylistFolder(playlistName);
        if (folder == null || !Directory.Exists(folder)) return false;

        try { Directory.Delete(folder, recursive: true); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Deletes a track's thumbnail from a playlist folder. Returns true if a file was deleted.
    /// </summary>
    public static bool DeleteLocalThumbnail(string playlistName, string trackName)
    {
        var path = FindLocalThumbnail(playlistName, trackName);
        if (path == null) return false;

        try { File.Delete(path); return true; }
        catch { return false; }
    }
    #endregion
}