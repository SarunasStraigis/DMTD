using System.IO;
using System.Text.Json;

namespace PhaseLab.Shell.Services;

public sealed class ShellSettings
{
    public string ActiveModeId { get; set; } = "dmtd";
}

public static class ShellSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhaseLab");

    private static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    public static ShellSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new ShellSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<ShellSettings>(json, JsonOptions) ?? new ShellSettings();
        }
        catch
        {
            return new ShellSettings();
        }
    }

    public static void Save(ShellSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
