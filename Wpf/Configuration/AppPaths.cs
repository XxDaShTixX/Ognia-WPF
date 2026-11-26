using System.IO;

namespace Ognia.Wpf.Configuration;

public static class AppPaths
{
    /// <summary>
    /// Root music folder.
    /// C:\Users\<Username>\Music\Ognia
    /// </summary>
    public static string MusicFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Ognia");

    /// <summary>
    /// Folder where Ognia stores its settings and other app data.
    /// C:\Users\<Username>\AppData\Roaming\Ognia
    /// </summary>
    public static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ognia");

    /// <summary>
    /// Folder where Ognia stores themes data.
    /// </summary>
    public static string ThemesFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ognia", "Themes");

    /// <summary>
    /// The file path for the settings file.
    /// C:\Users\<Username>\AppData\Roaming\Ognia\settings.json
    /// </summary>
    public static string SettingsFilePath => Path.Combine(AppDataFolder, "settings.json");
     
    /// <summary>
    /// Initialize app data folder
    /// </summary>
    public static void Initialize()
    {
        // Create music root folder.
        Directory.CreateDirectory(MusicFolder);

        // Create app data folder.
        Directory.CreateDirectory(AppDataFolder);

        // Save default settings if not exists.
        AppSettings.Instance.Save();
    }
}