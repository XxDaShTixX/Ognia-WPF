using System.IO;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Library;
using Ognia.Wpf.ViewModels;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Ognia.Wpf.YouTube;

public class YouTubeAudioDownloader
{
    private readonly YoutubeClient _youtube = new();

    /// <summary>
    /// Downloads a track's audio into the shared music root.
    /// Writes to a unique temp file first, then atomically moves into place,
    /// so concurrent callers never observe or open a partially-written file.
    /// Returns the saved file path.
    /// </summary>
    public async Task<string> DownloadAsync(
        TrackRowViewModel model,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var root = AppPaths.MusicFolder;
        var folder = Path.Combine(root, LocalLibrary.SanitizeFolderName(model.PlaylistName));
        Directory.CreateDirectory(folder);

        var targetPath = Path.Combine(folder, LocalLibrary.BuildFileName(model.Name));

        // Already on disk — nothing to do. This is the fast path for a second caller
        // that arrives after the first caller's Move has completed.
        if (File.Exists(targetPath))
            return targetPath;

        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(model.SourceId, ct);
            var audioStream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            if (progress != null)
            {
                await _youtube.Videos.Streams.DownloadAsync(
                    audioStream, tempPath,
                    new Progress<double>(p => progress.Report(p)), ct);
            }
            else
            {
                await _youtube.Videos.Streams.DownloadAsync(audioStream, tempPath, null, ct);
            }

            // Atomic replace. If another caller won the race while we were downloading,
            // their file is already at targetPath; discard ours.
            try
            {
                File.Move(tempPath, targetPath, overwrite: false);
            }
            catch (IOException)
            {
                // Someone else moved a file into place first. Use theirs.
                try { File.Delete(tempPath); } catch { }
            }

            return targetPath;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }
}