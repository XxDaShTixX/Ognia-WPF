using Ognia.Wpf.Configuration;
using System.IO;
using System.Text.Json;

namespace Ognia.Wpf.Authentication;

public static class TokenStore
{
    private static readonly string TokenPath =
        Path.Combine(AppPaths.AppDataFolder, "token.json");

    private static PersistedToken? _instance;

    private static PersistedToken? Instance
    {
        get
        {
            if (_instance != null) return _instance;
            _instance = Load();
            return _instance;
        }
    }

    public static string? Token => Instance?.Token;
    public static string? Email => Instance?.Email;
    public static string? RefreshToken => Instance?.RefreshToken;
    public static DateTime? AccessTokenExpiresAt => Instance?.AccessTokenExpiresAt;

    public static bool IsAuthenticated => Instance != null
        && !string.IsNullOrEmpty(Instance.Token)
        && !string.IsNullOrEmpty(Instance.RefreshToken);

    public static void Set(string token, string email, DateTime accessTokenExpiresAt, string refreshToken)
    {
        _instance = new PersistedToken
        {
            Token = token,
            Email = email,
            AccessTokenExpiresAt = DateTime.SpecifyKind(accessTokenExpiresAt, DateTimeKind.Utc),
            RefreshToken = refreshToken
        };
        Save(_instance);
    }

    public static void Clear()
    {
        _instance = null;
        try { if (File.Exists(TokenPath)) File.Delete(TokenPath); } catch { }
    }

    private static PersistedToken? Load()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            var json = File.ReadAllText(TokenPath);
            return JsonSerializer.Deserialize<PersistedToken>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void Save(PersistedToken token)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataFolder);
            File.WriteAllText(TokenPath,
                JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Non-fatal — the user just won't stay logged in across restarts.
        }
    }

    private class PersistedToken
    {
        public string Token { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiresAt { get; set; }
    }
}