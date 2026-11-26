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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static ResourceDictionary? _activeThemeDictionary;

    public static string ThemesFolder =>
        Path.Combine(AppPaths.AppDataFolder, "Themes");

    /// <summary>
    /// Idempotent. Seeds the local Themes folder from the embedded defaults if empty,
    /// then applies the theme named in settings (falls back to Default.json).
    /// Throws if no theme can be applied — the app cannot render without theme brushes.
    /// </summary>
    public static void Initialize()
    {
        Directory.CreateDirectory(ThemesFolder);
        SeedDefaultsIfMissing();

        var active = AppSettings.Instance.ActiveTheme;
        if (string.IsNullOrWhiteSpace(active) ||
            !File.Exists(Path.Combine(ThemesFolder, active)))
        {
            active = "Default.json";
        }

        var applied = Apply(active);

        if (!applied && !string.Equals(active, "Default.json", StringComparison.OrdinalIgnoreCase))
            applied = Apply("Default.json");

        if (!applied)
            throw new InvalidOperationException(
                $"No usable theme found in '{ThemesFolder}'. " +
                "Ensure at least Default.json, Dark.json, and Light.json exist.");

        // Persist whatever was actually applied — never the requested name if it failed.
        AppSettings.Instance.ActiveTheme = ActiveThemeFileName ?? "Default.json";
        AppSettings.Instance.Save();
    }

    /// <summary>
    /// Applies the theme file with the given filename (e.g. "Dark.json").
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

    private static void SeedDefaultsIfMissing()
    {
        if (Directory.EnumerateFiles(ThemesFolder, "*.json").Any()) return;

        var assembly = Assembly.GetExecutingAssembly();
        var resources = new (string Resource, string FileName)[]
        {
            ("Ognia.Wpf.Themes.Default.json", "Default.json"),
            ("Ognia.Wpf.Themes.Dark.json",    "Dark.json"),
            ("Ognia.Wpf.Themes.Light.json",   "Light.json"),
        };

        foreach (var (resource, fileName) in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) continue;

            var targetPath = Path.Combine(ThemesFolder, fileName);
            using var file = File.Create(targetPath);
            stream.CopyTo(file);
        }
    }
}