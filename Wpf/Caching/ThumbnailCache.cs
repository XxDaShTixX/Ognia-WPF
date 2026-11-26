using Ognia.Wpf.Library;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ognia.Wpf.Caching;

/// <summary>
/// Per-playlist thumbnail cache. Each playlist owns its own folder of
/// downscaled JPEGs, keyed by the track's display name.
/// </summary>
public static class ThumbnailCache
{
    private const int MaxDimension = 300;
    private const int JpegQuality = 92;

    private static readonly HttpClient Http = new();

    /// <summary>
    /// Ensures the track's thumbnail exists in the playlist's thumbnail folder.
    /// Downloads and re-encodes it if missing. Returns the local path, or null on failure.
    /// </summary>
    public static async Task<string?> GetOrDownloadAsync(
        string url, string playlistName, string trackName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (string.IsNullOrWhiteSpace(playlistName) || string.IsNullOrWhiteSpace(trackName)) return null;

        var folder = LocalLibrary.ThumbnailFolder(playlistName);
        if (folder == null) return null;

        var path = Path.Combine(folder, LocalLibrary.BuildThumbnailFileName(trackName));
        if (File.Exists(path)) return path;

        try
        {
            Directory.CreateDirectory(folder);

            var bytes = await Http.GetByteArrayAsync(url, ct);
            if (bytes.Length == 0) return null;

            var resized = ResizeAndEncode(bytes);
            if (resized == null) return null;

            // Unique temp per attempt — prevents collisions when two rows for the same track
            // race to download the same thumbnail.
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, resized, ct);

            try
            {
                File.Move(tmp, path, overwrite: true);
            }
            catch (IOException)
            {
                // Another writer won the race; treat their file as valid.
                try { File.Delete(tmp); } catch { }
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }

            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes the source image, downscales to fit within MaxDimension (never upscales),
    /// and re-encodes as JPEG at JpegQuality. Returns null if the source can't be decoded.
    /// </summary>
    private static byte[]? ResizeAndEncode(byte[] source)
    {
        try
        {
            BitmapSource original;
            using (var ms = new MemoryStream(source))
            {
                var decoder = BitmapDecoder.Create(
                    ms,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                original = decoder.Frames[0];
            }

            var scale = Math.Min(
                1.0,
                (double)MaxDimension / Math.Max(original.PixelWidth, original.PixelHeight));

            BitmapSource target = original;
            if (scale < 1.0)
            {
                var transformed = new TransformedBitmap(original, new ScaleTransform(scale, scale));
                transformed.Freeze();
                target = transformed;
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(target));

            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads a thumbnail into a temp folder for one-shot use (e.g. collage building).
    /// Never writes into a playlist's .thumbnails/ folder. Returns the temp path, or null.
    /// </summary>
    public static async Task<string?> GetTempAsync(
        string url, string sourceName, string sourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var folder = Path.Combine(Path.GetTempPath(), "ognia-covers");
        var path = Path.Combine(folder,
            $"{LocalLibrary.SanitizeFileName(sourceName)}_{LocalLibrary.SanitizeFileName(sourceId)}.jpg");

        if (File.Exists(path)) return path;

        try
        {
            Directory.CreateDirectory(folder);

            var bytes = await Http.GetByteArrayAsync(url, ct);
            if (bytes.Length == 0) return null;

            var resized = ResizeAndEncode(bytes);
            if (resized == null) return null;

            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, resized, ct);

            try
            {
                File.Move(tmp, path, overwrite: true);
            }
            catch (IOException)
            {
                // Another writer won the race; their file is fine.
                try { File.Delete(tmp); } catch { }
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }

            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}