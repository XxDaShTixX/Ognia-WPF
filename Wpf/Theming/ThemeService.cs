using Ognia.Wpf.Configuration;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Ognia.Wpf.Theming;

public sealed class ThemeInfo : IEquatable<ThemeInfo>
{
    public string FileName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;

    public bool Equals(ThemeInfo? other)
        => other is not null
           && string.Equals(FileName, other.FileName, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ThemeInfo);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(FileName);
}

public sealed class ThemeFile
{
    public string Name { get; set; } = "Untitled";
    public string Author { get; set; } = string.Empty;
    public Dictionary<string, string> Colors { get; set; } = new();
}

public static class ThemeService
{
    private const string DefaultThemeFileName = "Default.json";

    private static readonly (string Resource, string FileName)[] BundledThemes =
    {
        ("Ognia.Wpf.Themes.Default.json", DefaultThemeFileName),
        ("Ognia.Wpf.Themes.OgniaDark.json", "OgniaDark.json"),
        ("Ognia.Wpf.Themes.OgniaLight.json", "OgniaLight.json")
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static ResourceDictionary? _activeThemeDictionary;

    public static string ThemesFolder =>
        Path.Combine(AppPaths.AppDataFolder, "Themes");

    /// <summary>
    /// Refreshes the three Ognia-managed bundled themes from embedded resources,
    /// preserving every other file in the Themes folder as user-owned. Then applies
    /// the theme named in settings, falling back to Default.json when it is missing
    /// or invalid. Bundled themes are refreshed automatically on every startup.
    /// Throws if no theme can be applied — the app cannot render without theme brushes.
    /// </summary>
    public static void Initialize()
    {
        Directory.CreateDirectory(ThemesFolder);
        RefreshBundledThemes();

        var active = AppSettings.Instance.ActiveTheme;
        if (string.IsNullOrWhiteSpace(active) ||
            !File.Exists(Path.Combine(ThemesFolder, active)))
        {
            active = DefaultThemeFileName;
        }

        var applied = Apply(active);

        if (!applied && !string.Equals(active, DefaultThemeFileName, StringComparison.OrdinalIgnoreCase))
            applied = Apply(DefaultThemeFileName);

        if (!applied)
            throw new InvalidOperationException(
                $"No usable theme found in '{ThemesFolder}'. " +
                "Ensure the embedded Default.json theme is available.");

        // Persist whatever was actually applied — never the requested name if it failed.
        AppSettings.Instance.ActiveTheme = ActiveThemeFileName ?? DefaultThemeFileName;
        AppSettings.Instance.Save();
    }

    /// <summary>
    /// Applies a theme file from the Themes folder, including either an Ognia-managed
    /// bundled theme or a user-owned theme.
    /// Never throws — returns false on any failure.
    /// </summary>
    public static bool Apply(string fileName)
    {
        try
        {
            var path = Path.Combine(ThemesFolder, fileName);
            if (!File.Exists(path)) return false;

            var json = File.ReadAllText(path);
            var theme = JsonSerializer.Deserialize<ThemeFile>(json, JsonOptions);
            if (theme == null || theme.Colors.Count == 0) return false;

            var dict = new ResourceDictionary();
            foreach (var (key, hex) in theme.Colors)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                dict[key] = new SolidColorBrush(color);
                dict[$"{key}.Color"] = color;   // parallel Color resource for GradientStop
            }

            var merged = Application.Current.Resources.MergedDictionaries;
            if (_activeThemeDictionary != null)
                merged.Remove(_activeThemeDictionary);

            _activeThemeDictionary = dict;
            merged.Insert(0, dict);

            ActiveThemeFileName = fileName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<ThemeInfo> EnumerateThemes()
    {
        Directory.CreateDirectory(ThemesFolder);

        var result = new List<ThemeInfo>();
        foreach (var path in Directory.EnumerateFiles(ThemesFolder, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var theme = JsonSerializer.Deserialize<ThemeFile>(json, JsonOptions);
                if (theme == null) continue;

                result.Add(new ThemeInfo
                {
                    FileName = Path.GetFileName(path),
                    Name = string.IsNullOrWhiteSpace(theme.Name) ? Path.GetFileNameWithoutExtension(path) : theme.Name,
                    Author = theme.Author
                });
            }
            catch { }
        }

        return result.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Filename of the theme dictionary currently inserted into Application.Resources,
    /// or null if none is active.
    /// </summary>
    public static string? ActiveThemeFileName { get; private set; }

    private static void RefreshBundledThemes()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var (resource, fileName) in BundledThemes)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null)
                throw new InvalidOperationException(
                    $"Bundled theme resource '{resource}' was not found.");

            var targetPath = Path.Combine(ThemesFolder, fileName);
            using var file = new FileStream(
                targetPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            stream.CopyTo(file);
        }
    }
}