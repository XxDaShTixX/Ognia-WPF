using Ognia.Wpf.Models.Playlist;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ognia.Wpf.Helpers;

public static class PlaylistCoverCache
{
    private static string Folder => Path.Combine(AppPaths.AppDataFolder, "playlist-covers");

    // Per-playlist lock so overlapping LoadPlaylistsAsync calls don't race on the same file.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    #region Public
    /// <summary>
    /// Synchronous fast path: returns an already-cached cover for this playlist, or null.
    /// Use this to set CachedCoverPath immediately on launch.
    /// </summary>
    public static string? TryGetCached(int playlistId)
    {
        try
        {
            var folder = Folder;
            if (!Directory.Exists(folder)) return null;

            // Any file matching pl-<id>-*.png is a valid cache for this playlist.
            foreach (var file in Directory.EnumerateFiles(folder, $"pl-{playlistId}-*.png"))
                return file;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes all cached .png images for a given playlist cover.
    /// </summary>
    /// <param name="playlistId"></param>
    public static void Invalidate(int playlistId)
    {
        try
        {
            var folder = Folder;
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, $"pl-{playlistId}-*.png"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Returns a path to a cachced 2x2 collage PNG for a playlist.
    /// </summary>
    /// <param name="playlistId"></param>
    /// <param name="thumbnails"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static async Task<string?> GetOrBuildAsync(
        int playlistId,
        IEnumerable<CoverThumbnail> thumbnails,
        CancellationToken ct = default)
    {
        var list = thumbnails.Where(t => !string.IsNullOrWhiteSpace(t.Url)).Take(4).ToList();
        if (list.Count == 0) return null;

        var gate = Locks.GetOrAdd(playlistId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Folder);

            var version = Hash(string.Join("|", list.Select(t => $"{t.SourceName}:{t.SourceId}")));
            var path = Path.Combine(Folder, $"pl-{playlistId}-{version}.png");

            try
            {
                foreach (var stale in Directory.EnumerateFiles(Folder, $"pl-{playlistId}-*.png"))
                    if (!string.Equals(stale, path, StringComparison.OrdinalIgnoreCase))
                        File.Delete(stale);
            }
            catch { }

            if (File.Exists(path)) return path;

            var bitmaps = new List<BitmapSource>();
            foreach (var t in list)
            {
                var cachedPath = await ThumbnailCache.GetTempAsync(t.Url, t.SourceName, t.SourceId, ct);
                if (cachedPath == null) continue;

                try { bitmaps.Add(Decode(cachedPath)); }
                catch { }
            }

            if (bitmaps.Count == 0) return null;

            var img = Compose2x2(bitmaps);

            var tmp = path + ".tmp";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(img));
            using (var fs = File.Create(tmp)) encoder.Save(fs);
            File.Move(tmp, path, overwrite: true);

            return path;
        }
        finally
        {
            gate.Release();
        }
    }
    #endregion

    #region Private
    /// <summary>
    /// Loads an image file from disk into a frozen BitmapImage.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private static BitmapSource Decode(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.None;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Generage / render up-to four input BitmapSource images into a single 256x256 (2x2 grid of 128x128 tiles).
    /// Used for playlist covers.
    /// </summary>
    /// <param name="images"></param>
    /// <returns></returns>
    private static BitmapSource Compose2x2(IReadOnlyList<BitmapSource> images)
    {
        const int tile = 128;
        const int size = tile * 2;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new System.Windows.Rect(0, 0, size, size));
            for (int i = 0; i < images.Count && i < 4; i++)
            {
                var src = images[i];
                int col = i % 2, row = i / 2;
                dc.DrawImage(src, new System.Windows.Rect(col * tile, row * tile, tile, tile));
            }
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// Computes a 12-character uppercase hexadecimal prefix of the UTF-8 SHA-1 hash of a string. 
    /// Used to version playlist cover cache files.
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    private static string Hash(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes)[..12];
    }
    #endregion
}