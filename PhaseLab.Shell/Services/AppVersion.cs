using System.Reflection;

namespace PhaseLab.Shell.Services;

public static class AppVersion
{
    public static string Current { get; } = GetCurrent();

    private static string GetCurrent()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
