namespace Dmtd.Core;

public static class AppDataPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhaseLab");

    public static string ShellSettingsFile => Path.Combine(Root, "settings.json");

    public static string DmtdSettingsDirectory => Path.Combine(Root, "Dmtd");

    public static string DmtdSettingsFile => Path.Combine(DmtdSettingsDirectory, "settings.json");

    public static string DmtdHistoryFile => Path.Combine(DmtdSettingsDirectory, "history.db");

    public static string JitterSettingsDirectory => Path.Combine(Root, "JitterMeasurement");

    public static string JitterSettingsFile => Path.Combine(JitterSettingsDirectory, "settings.json");
}
