using Ognia.Wpf.Configuration;
using System.IO;
using System.Text.Json;

namespace Ognia.Wpf.Session;

public sealed class PlaybackSessionState
{
    /// <summary>
    /// Singleton instance of the current playback session state.
    /// </summary>
    public static PlaybackSessionState Instance => _instance ??= Load();

    /// <summary>
    /// The singleton instance.
    /// </summary>
    private static PlaybackSessionState? _instance;

    /// <summary>
    /// Json serializer options used when saving file.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    #region States
    public int? LastTrackId { get; set; }
    public double LastTrackPosition { get; set; }
    public int? LastOpenedPlaylistId { get; set; }
    #endregion

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }

    private static string FilePath =>
        Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFilePath)!, "session.json");

    private static PlaybackSessionState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<PlaybackSessionState>(json) ?? new();
            }
        }
        catch { }

        return new();
    }
}