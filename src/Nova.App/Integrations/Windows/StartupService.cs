using Microsoft.Win32;

namespace Nova.App.Integrations.Windows;

/// <summary>Per-user "start with Windows" via HKCU Run key. No admin rights, easy to reverse.</summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "NOVA";

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                k.SetValue(ValueName, $"\"{exe}\" --startup");   // --startup: wait for the Windows shell before touching the taskbar
            }
            else k.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
