using System.IO;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Models.Track;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Ognia.Wpf.YouTube;

public class PreviewDownloader
{
    private readonly YoutubeClient _youtube = new();

    public static string PreviewFolder =>
        Path.Combine(AppPaths.AppDataFolder, "previews");

    public async Task<string> DownloadAsync(
    TrackResponse track,
    IProgress<double>? progress = null,
    CancellationToken ct = default)
    {
        Directory.CreateDirectory(PreviewFolder);

        var targetPath = Path.Combine(PreviewFolder, $"{track.SourceId}.m4a");
        if (File.Exists(targetPath)) return targetPath;

        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(track.SourceId, ct);

            IStreamInfo? streamInfo = manifest.GetAudioOnlyStreams().TryGetWithHighestBitrate();

            streamInfo ??= manifest.GetMuxedStreams().TryGetWithHighestBitrate();

            streamInfo ??= manifest.Streams
                .OfType<IAudioStreamInfo>()
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();

            if (streamInfo is null)
                throw new InvalidOperationException($"No audio stream available for '{track.SourceId}'.");

            if (progress != null)
                await _youtube.Videos.Streams.DownloadAsync(
                    streamInfo, tempPath, progress, ct);
            else
                await _youtube.Videos.Streams.DownloadAsync(streamInfo, tempPath, null, ct);

            try { File.Move(tempPath, targetPath, overwrite: false); }
            catch (IOException) { try { File.Delete(tempPath); } catch { } }

            return targetPath;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public static void ClearAll()
    {
        try
        {
            if (!Directory.Exists(PreviewFolder)) return;
            foreach (var file in Directory.GetFiles(PreviewFolder, "*"))
                try { File.Delete(file); } catch { }
        }
        catch { }
    }
}