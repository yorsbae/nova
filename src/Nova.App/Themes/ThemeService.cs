using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Nova.Core.Configuration;

namespace Nova.App.Themes;

/// <summary>Publishes NovaSurface/NovaSurfaceAlt/NovaWindow/NovaOnSurface/NovaBorder/NovaAccent brushes as app resources.</summary>
public static class ThemeService
{
    public static void Apply(Application app, NovaSettings s)
    {
        if (SystemParameters.HighContrast || s.HighContrastOverride)
        {
            Set(app, "NovaSurface", SystemColors.WindowColor);
            Set(app, "NovaWindow", SystemColors.WindowColor);
            Set(app, "NovaSurfaceAlt", SystemColors.ControlColor);
            Set(app, "NovaOnSurface", SystemColors.WindowTextColor);
            Set(app, "NovaBorder", SystemColors.WindowTextColor);
            Set(app, "NovaAccent", SystemColors.HighlightColor);
            Set(app, "NovaOnAccent", SystemColors.HighlightTextColor);
            return;
        }

        bool dark = s.Theme switch { ThemeMode.Dark => true, ThemeMode.Light => false, _ => !SystemUsesLightApps() };
        var window = dark ? Color.FromRgb(0x0B, 0x14, 0x26) : Color.FromRgb(0xF6, 0xF6, 0xF8);
        var surface = window;
        surface.A = (byte)Math.Round(255 * s.Opacity);
        Set(app, "NovaSurface", surface);
        Set(app, "NovaWindow", window);
        Set(app, "NovaSurfaceAlt", dark ? Color.FromRgb(0x15, 0x24, 0x40) : Color.FromRgb(0xE4, 0xE4, 0xEA));
        Set(app, "NovaOnSurface", dark ? Colors.White : Color.FromRgb(0x14, 0x14, 0x18));
        Set(app, "NovaBorder", dark ? Color.FromRgb(0x2F, 0x4F, 0x86) : Color.FromRgb(0x60, 0x60, 0x6A));
        var accent = ParseAccent(s.AccentColor);
        Set(app, "NovaAccent", accent);
        Set(app, "NovaOnAccent", OnAccent(accent));
    }

    /// <summary>Black or white, whichever reads better on the accent (WCAG relative luminance).</summary>
    static Color OnAccent(Color c)
    {
        static double Lin(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        var lum = 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        return lum > 0.18 ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White;
    }

    static Color ParseAccent(string text)
    {
        try { return (Color)ColorConverter.ConvertFromString(text); }
        catch { return Color.FromRgb(0x4C, 0x8D, 0xFF); }
    }

    static void Set(Application app, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        app.Resources[key] = b;
    }

    static bool SystemUsesLightApps()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return true; }
    }
}
