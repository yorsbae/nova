using System.Runtime.InteropServices;
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

    /// <summary>
    /// Reads the Night light state blob. Byte 18 is 0x15 when on and 0x13 when off; when on, bytes 23-24 are 0x10 0x00.
    /// The blob is longer when a schedule is set, so only the header and these bytes are checked (not the exact length).
    /// </summary>
    static bool TryRead(out byte[] data, out bool on)
    {
        data = Array.Empty<byte>(); on = false;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(NightKey, false);
            if (k is null) { FileLog.Write("Night light: registry state not found (never switched on in Windows?)"); return false; }
            if (k.GetValue("Data") is not byte[] d || d.Length < 25) return false;
            if (d[0] != 0x43 || d[1] != 0x42 || d[2] != 0x01 || d[3] != 0x00) { FileLog.Write("Night light: unknown header"); return false; }
            bool isOn = d[18] == 0x15 && d[23] == 0x10 && d[24] == 0x00, isOff = d[18] == 0x13;
            if (!isOn && !isOff) { FileLog.Write($"Night light: unknown state byte {d[18]:X2}"); return false; }
            data = d; on = isOn; return true;
        }
        catch (Exception ex) { FileLog.Write($"Night light read failed: {ex.GetType().Name}"); return false; }
    }

    /// <summary>true/false = on/off. Falls back to the warm-tint state when Windows' own setting can't be read.</summary>
    public static bool? NightLightOn() => TryRead(out _, out var on) ? on : _tintOn;

    /// <summary>
    /// Flips Night light in ONE click. First tries Windows' own Night light (registry); when this Windows build keeps it in a layout
    /// NOVA does not know, it applies a warm screen tint itself instead, so the button never needs to open Settings.
    /// Returns the new state, or null only when neither way works.
    /// </summary>
    public static bool? ToggleNightLight()
    {
        if (TryRead(out var d, out var on))
        {
            try
            {
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
                if (k is not null)
                {
                    k.SetValue("Data", n, RegistryValueKind.Binary);
                    return !on;
                }
            }
            catch (Exception ex) { FileLog.Write($"Night light toggle failed: {ex.GetType().Name}"); }
        }
        return ToggleTint();
    }

    // ---- warm tint fallback (gamma ramp of the main screen; undone by RestoreTint on exit) ----

    static bool _tintOn;

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr hDc, [In] ushort[] ramp);

    static bool SetTint(bool on)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try
        {
            var ramp = new ushort[3 * 256];
            double r = 1.0, g = on ? 0.80 : 1.0, b = on ? 0.58 : 1.0;   // about 3500 K: warm, easy on the eyes
            for (int i = 0; i < 256; i++)
            {
                double v = i * 257.0;
                ramp[i] = (ushort)Math.Min(65535, v * r);
                ramp[256 + i] = (ushort)Math.Min(65535, v * g);
                ramp[512 + i] = (ushort)Math.Min(65535, v * b);
            }
            return SetDeviceGammaRamp(dc, ramp);
        }
        catch (Exception ex) { FileLog.Write($"Warm tint failed: {ex.GetType().Name}"); return false; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    static bool? ToggleTint()
    {
        bool want = !_tintOn;
        if (!SetTint(want)) return null;
        _tintOn = want;
        return want;
    }

    /// <summary>Puts the screen colours back to normal if NOVA's own warm tint is on. Call when NOVA exits.</summary>
    public static void RestoreTint()
    {
        if (_tintOn && SetTint(false)) _tintOn = false;
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
