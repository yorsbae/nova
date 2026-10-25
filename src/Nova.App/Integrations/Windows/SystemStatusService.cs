using System.Runtime.InteropServices;
using Nova.Core.Status;

namespace Nova.App.Integrations.Windows;

/// <summary>Local CPU / memory / battery snapshot. Sampled on demand (only while the Island is expanded) — no background polling.</summary>
public sealed class SystemStatusService
{
    readonly record struct Snapshot(ulong Idle, ulong Kernel, ulong User, DateTime At);
    Snapshot? _last;

    public string Sample()
    {
        double? cpu = null, mem = null;
        BatteryInfo? battery = null;

        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var now = DateTime.UtcNow;
            // A stale baseline would average over a long collapsed period, which would be misleading.
            if (_last is { } l && (now - l.At).TotalSeconds < 10)
                cpu = CpuUsage.Compute(l.Idle, l.Kernel, l.User, idle, kernel, user);
            _last = new Snapshot(idle, kernel, user, now);
        }

        var m = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref m)) mem = MemoryUsage.UsedPercent(m.ullTotalPhys, m.ullAvailPhys);

        if (NativeMethods.GetSystemPowerStatus(out var p)) battery = BatteryInfo.From(p.ACLineStatus, p.BatteryFlag, p.BatteryLifePercent);

        return StatusFormatter.Format(cpu, mem, battery);
    }
}
