using JitterMeasurement.Core.Models;
using System.IO;
using System.Text.Json;

namespace JitterMeasurement.Module.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhaseLab", "JitterMeasurement");

    private static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    private static string LegacySettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JitterMeasurement", "settings.json");

    public static SavedSettings Load()
    {
        MigrateLegacySettingsIfNeeded();

        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new SavedSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<SavedSettings>(json, JsonOptions) ?? new SavedSettings();
        }
        catch
        {
            return new SavedSettings();
        }
    }

    public static void Save(SavedSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    private static void MigrateLegacySettingsIfNeeded()
    {
        if (File.Exists(SettingsFilePath) || !File.Exists(LegacySettingsFilePath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.Copy(LegacySettingsFilePath, SettingsFilePath);
        }
        catch
        {
            // Best-effort migration.
        }
    }
}
