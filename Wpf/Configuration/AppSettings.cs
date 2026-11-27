using Ognia.Wpf.Enums;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace Ognia.Wpf.Configuration;

public class AppSettings
{
    /// <summary>
    /// Singleton instance of the current settings.
    /// </summary>
    public static AppSettings Instance => _instance ??= Load();

    /// <summary>
    /// The singleton instance.
    /// </summary>
    private static AppSettings? _instance;

    #region Settings File Structure
    /// <summary>The account mode currently in use.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppMode? Mode { get; set; }

    /// <summary>Currently active theme filename. Defaults to the bundled Default.json theme.</summary>
    public string ActiveTheme { get; set; } = "Default.json";

    public UpdateSettings Update { get; set; } = new();
    public ApiSettings Api { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public List<int> PlaylistOrder { get; set; } = new();
    #endregion

    /// <summary>
    /// Load settings from file into instance.
    /// </summary>
    /// <returns></returns>
    private static AppSettings Load()
    {
        string SettingsPath = AppPaths.SettingsFilePath;

        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    /// <summary>
    /// Save settings back into file.
    /// </summary>
    public void Save()
    {
        string SettingsPath = AppPaths.SettingsFilePath;
        Directory.CreateDirectory(AppPaths.AppDataFolder);
        var json = JsonSerializer.Serialize(Instance, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}

/// <summary>
/// Holds update settings and preferences.
/// </summary>
public class UpdateSettings
{
    /// <summary>
    /// Determines if the auto-update featuer should be enabled or disabled.
    /// Enabled by default. Can be set via the Settings view.
    /// </summary>
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>
    /// "stable" or "prerelease". Controls whether pre-releases are considered when updating.
    /// "stable" by defauly unless changed in Settings view.
    /// </summary>
    public string Channel { get; set; } = "stable";
}

/// <summary>
/// Hold API settings and preferences.
/// </summary>
public class ApiSettings
{
    public string BaseUrl { get; set; } = "https://localhost:7235/";
}

/// <summary>
/// Holds Playback settings and preferences.
/// </summary>
public class PlaybackSettings
{
    /// <summary>
    /// Current volume.
    /// </summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>
    /// Current repeat mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

    /// <summary>
    /// Currnet shuffle toggle.
    /// </summary>
    public bool Shuffle { get; set; } = false;
}

/// <summary>
/// Hold Window settings and preferences.
/// </summary>
public class WindowSettings
{
    /// <summary>
    /// Application window width.
    /// </summary>
    public double Width { get; set; } = 1200;

    /// <summary>
    /// Application window height.
    /// </summary>
    public double Height { get; set; } = 800;

    /// <summary>
    /// Main window opacity.
    /// </summary>
    public double MainWindowOpacity { get; set; } = 1.0;

    /// <summary>
    /// Mini player resting opacity.
    /// </summary>
    public double MiniRestingOpacity { get; set; } = 0.2;

    /// <summary>
    /// Current window state (Normal, Minimized, Maximized). Defaults to Normal.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowState State { get; set; } = WindowState.Normal;
}