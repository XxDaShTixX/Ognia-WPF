using Ognia.Wpf.Services;

namespace Ognia.Wpf.Configuration;

public static class AppInfo
{
    /// <summary>
    /// Main reference to the project repo.
    /// </summary>
    public const string GitHubRepositoryUrl = "https://github.com/XxDaShTixX/Ognia-WPF";

    /// <summary>
    /// Current version of the application retrieved from Velopack/Assembly.
    /// </summary>
    public static string Version => UpdateService.Instance.CurrentVersion;

    /// <summary>
    /// The formatted version (e.g. Ognia v1.0.0).
    /// </summary>
    public static string DisplayVersion => UpdateService.Instance.DisplayVersion;
}