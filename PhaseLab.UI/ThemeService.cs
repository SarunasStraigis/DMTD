using Microsoft.Win32;
using System.Windows;

namespace PhaseLab.UI;

public static class ThemeService
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightThemeUri = new(
        "pack://application:,,,/PhaseLab.UI;component/Themes/LightTheme.xaml", UriKind.Absolute);

    private static readonly Uri DarkThemeUri = new(
        "pack://application:,,,/PhaseLab.UI;component/Themes/DarkTheme.xaml", UriKind.Absolute);

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public static event Action<AppTheme>? ThemeChanged;

    public static void Initialize()
    {
        ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    public static AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intValue)
            {
                return intValue == 0 ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch
        {
            // Fall back to light theme if registry is unavailable.
        }

        return AppTheme.Light;
    }

    public static void ApplySystemTheme()
    {
        ApplyTheme(DetectSystemTheme());
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current.Resources.MergedDictionaries.Count == 0)
        {
            return;
        }

        var themeUri = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri;
        Application.Current.Resources.MergedDictionaries[0] =
            new ResourceDictionary { Source = themeUri };

        CurrentTheme = theme;
        ThemeChanged?.Invoke(theme);
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(ApplySystemTheme);
    }
}
