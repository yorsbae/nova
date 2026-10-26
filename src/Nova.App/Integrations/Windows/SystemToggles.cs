using Microsoft.Win32;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// One-click switches for Night light and Do not disturb, so the Island button flips the setting at once instead of opening Settings.
/// Windows has no public API for either, so both use the per-user registry values Windows' own switches use (no admin rights).
/// Every method reports failure instead of throwing: the caller falls back to opening the Windows Settings page.
/// </summary>
public static class SystemToggles
{
    // ---- Night light ----

    const string NightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    /// <summary>The state blob is 41 bytes when off and 43 when on; byte 18 is 0x13 / 0x15. Anything else = an unknown layout, so we don't touch it.</summary>
    static bool TryRead(out byte[] data, out bool on)
    {
        data = Array.Empty<byte>(); on = false;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(NightKey, false);
            if (k?.GetValue("Data") is not byte[] d || d.Length < 25) return false;
            if (d[0] != 0x43 || d[1] != 0x42 || d[2] != 0x01 || d[3] != 0x00) return false;
            bool isOn = d.Length == 43 && d[18] == 0x15, isOff = d.Length == 41 && d[18] == 0x13;
            if (!isOn && !isOff) return false;
            data = d; on = isOn; return true;
        }
        catch { return false; }
    }

    /// <summary>true/false = on/off; null = can't be read on this PC.</summary>
    public static bool? NightLightOn() => TryRead(out _, out var on) ? on : null;

    /// <summary>Flips Night light. Returns the new state, or null when it could not be changed.</summary>
    public static bool? ToggleNightLight()
    {
        try
        {
            if (!TryRead(out var d, out var on)) return null;
            byte[] n;
            if (on)   // turn off: drop the two "enabled" bytes
            {
                n = d.Take(23).Concat(d.Skip(25)).ToArray();
                n[18] = 0x13;
            }
            else      // turn on: insert them
            {
                n = d.Take(23).Concat(new byte[] { 0x10, 0x00 }).Concat(d.Skip(23)).ToArray();
                n[18] = 0x15;
            }
            using var k = Registry.CurrentUser.OpenSubKey(NightKey, true);
            if (k is null) return null;
            k.SetValue("Data", n, RegistryValueKind.Binary);
            return !on;
        }
        catch (Exception ex) { FileLog.Write($"Night light toggle failed: {ex.GetType().Name}"); return null; }
    }

    // ---- Do not disturb ----

    const string ToastKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    const string ToastValue = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

    /// <summary>true = Do not disturb is on (notification banners are off).</summary>
    public static bool DndOn()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(ToastKey, false);
            return k?.GetValue(ToastValue) is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Flips Do not disturb. Returns the new state, or null on failure.</summary>
    public static bool? ToggleDnd()
    {
        try
        {
            bool want = !DndOn();
            using var k = Registry.CurrentUser.CreateSubKey(ToastKey, true);
            k.SetValue(ToastValue, want ? 0 : 1, RegistryValueKind.DWord);
            return want;
        }
        catch (Exception ex) { FileLog.Write($"Do-not-disturb toggle failed: {ex.GetType().Name}"); return null; }
    }
}
