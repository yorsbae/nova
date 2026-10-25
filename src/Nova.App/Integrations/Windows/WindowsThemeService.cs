using Microsoft.Win32;

namespace Nova.App.Integrations.Windows;

/// <summary>Switches the per-user Windows light/dark setting (apps and system UI) and tells running apps about it.</summary>
public static class WindowsThemeService
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string AppsValue = "AppsUseLightTheme", SystemValue = "SystemUsesLightTheme";

    public static (int? Apps, int? SystemUi) Read()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return (k?.GetValue(AppsValue) as int?, k?.GetValue(SystemValue) as int?);
        }
        catch { return (null, null); }
    }

    public static void SetDark(bool dark) => Write(dark ? 0 : 1, dark ? 0 : 1);

    /// <summary>Puts back the values that were there before (null = the value did not exist).</summary>
    public static void Restore(int? apps, int? system) => Write(apps, system);

    static void Write(int? apps, int? system)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            Set(k, AppsValue, apps);
            Set(k, SystemValue, system);
        }
        catch { return; }
        Broadcast();
    }

    static void Set(RegistryKey k, string name, int? value)
    {
        if (value is int v) k.SetValue(name, v, RegistryValueKind.DWord);
        else k.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>The same notification Windows Settings sends, so Explorer and open apps repaint.</summary>
    static void Broadcast()
    {
        try
        {
            NativeMethods.SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, UIntPtr.Zero,
                "ImmersiveColorSet", 0x0002 /* SMTO_ABORTIFHUNG */, 2000, out _);
        }
        catch { }
    }
}
