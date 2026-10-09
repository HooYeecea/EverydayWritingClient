using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace EverydayWritingClient.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        var dark = theme == "dark" || (theme == "system" && !SystemUsesLightTheme());
        Set("BgBrush", dark ? "#121212" : "#FAFAFA");
        Set("SurfaceBrush", dark ? "#1A1A1A" : "#FFFFFF");
        Set("SubtleBrush", dark ? "#2A2A2A" : "#F5F5F5");
        Set("TextBrush", dark ? "#ECECEC" : "#37352F");
        Set("MutedBrush", dark ? "#9A9A9A" : "#737373");
        Set("BorderBrush", dark ? "#3F3F3F" : "#E5E5E5");
        Set("PrimaryBrush", dark ? "#3A3A3A" : "#171717");
        Set("PrimaryFgBrush", dark ? "#F3F3F3" : "#FFFFFF");
        Set("DangerBrush", "#B42318");
    }

    private static bool SystemUsesLightTheme()
    {
        var value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            1);
        return value is int number && number != 0;
    }

    private static void Set(string key, string hex)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(hex);
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
