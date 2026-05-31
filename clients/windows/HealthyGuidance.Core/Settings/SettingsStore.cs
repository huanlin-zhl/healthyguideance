using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HealthyGuidance.Core.Settings;

public static class SettingsStore
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HealthyGuidance",
        "config");

    private static readonly string SecretsPath = Path.Combine(ConfigDir, "secrets.dat");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static AppSettings Load()
    {
        if (!File.Exists(SecretsPath))
            return new AppSettings();

        try
        {
            var encrypted = File.ReadAllBytes(SecretsPath);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SecretsPath, encrypted);
    }

    public static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return string.Empty;
        if (apiKey.Length <= 8) return new string('*', apiKey.Length);
        return $"{apiKey[..4]}{new string('*', apiKey.Length - 8)}{apiKey[^4..]}";
    }
}
