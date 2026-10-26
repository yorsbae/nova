using Microsoft.Win32;

namespace Nova.App.Integrations.Windows;

/// <summary>Cached "running on battery" flag, refreshed only when Windows reports a power change (no polling).</summary>
public static class PowerState
{
    public static bool OnBattery { get; private set; } = Read();

    static PowerState() => SystemEvents.PowerModeChanged += (_, _) => OnBattery = Read();

    static bool Read()
    {
        try { return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline; }
        catch { return false; }
    }
}
