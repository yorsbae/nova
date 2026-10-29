using System.Runtime.InteropServices;
using Nova.Core.Status;

namespace Nova.App.Integrations.Windows;

/// <summary>Local CPU / memory / battery snapshot. Sampled on demand (only while the Island is expanded) — no background polling.</summary>
public sealed class SystemStatusService
{
    readonly record struct Snapshot(ulong Idle, ulong Kernel, ulong User, DateTime At);
    Snapshot? _last;

    // Network speed is the change in the adapters' byte counters between two readings.
    (long Down, long Up, DateTime At)? _lastNet;
    // Disk = how busy the disks are (what Task Manager's "Disk" column shows), not how full the drive is.
    IntPtr _diskQuery, _diskCounter; bool _diskTried;
    double? _disk;

    void EnsureDiskCounter()
    {
        if (_diskTried) return;
        _diskTried = true;
        try
        {
            if (NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _diskQuery) != 0) { _diskQuery = IntPtr.Zero; return; }
            if (NativeMethods.PdhAddEnglishCounterW(_diskQuery, @"\PhysicalDisk(_Total)\% Idle Time", IntPtr.Zero, out _diskCounter) != 0)
            { NativeMethods.PdhCloseQuery(_diskQuery); _diskQuery = IntPtr.Zero; return; }
            NativeMethods.PdhCollectQueryData(_diskQuery);   // first sample is only the baseline
        }
        catch { _diskQuery = IntPtr.Zero; }
    }

    double? ReadDiskActivity()
    {
        EnsureDiskCounter();
        if (_diskQuery == IntPtr.Zero) return null;
        try
        {
            if (NativeMethods.PdhCollectQueryData(_diskQuery) != 0) return null;
            if (NativeMethods.PdhGetFormattedCounterValue(_diskCounter, NativeMethods.PDH_FMT_DOUBLE, out _, out var v) != 0 || v.CStatus != 0) return null;
            return Math.Round(Math.Clamp(100.0 - v.DoubleValue, 0, 100), 0);
        }
        catch { return null; }
    }

    static bool IsVirtualNic(System.Net.NetworkInformation.NetworkInterface nic)
    {
        var d = (nic.Description + " " + nic.Name).ToLowerInvariant();
        return d.Contains("virtual") || d.Contains("vethernet") || d.Contains("vmware") || d.Contains("vmnet") || d.Contains("hyper-v")
            || d.Contains("loopback") || d.Contains("pseudo") || d.Contains("wintun") || d.Contains("tap-") || d.Contains("wsl");
    }

    /// <summary>CPU, memory, system drive, battery and network speed in one go. Cheap; call it every few seconds while it is being shown.</summary>
    public SystemMetrics SampleMetrics()
    {
        double? cpu = null, mem = null;
        BatteryInfo? battery = null;
        var nowUtc = DateTime.UtcNow;

        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (_last is { } l && (nowUtc - l.At).TotalSeconds < 10)
                cpu = CpuUsage.Compute(l.Idle, l.Kernel, l.User, idle, kernel, user);
            _last = new Snapshot(idle, kernel, user, nowUtc);
        }

        var m = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref m)) mem = MemoryUsage.UsedPercent(m.ullTotalPhys, m.ullAvailPhys);
        if (NativeMethods.GetSystemPowerStatus(out var p)) battery = BatteryInfo.From(p.ACLineStatus, p.BatteryFlag, p.BatteryLifePercent);

        _disk = ReadDiskActivity();

        double down = 0, up = 0;
        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel ||
                    IsVirtualNic(nic)) continue;
                var s = nic.GetIPStatistics();   // IPv4 + IPv6, like Task Manager
                rx += s.BytesReceived; tx += s.BytesSent;
            }
            if (_lastNet is { } n && (nowUtc - n.At).TotalSeconds is > 0 and < 15 && rx >= n.Down && tx >= n.Up)
            {
                var secs = (nowUtc - n.At).TotalSeconds;
                down = (rx - n.Down) / secs; up = (tx - n.Up) / secs;
            }
            _lastNet = (rx, tx, nowUtc);
        }
        catch { _lastNet = null; }

        return new SystemMetrics(cpu, mem, _disk, battery, down, up);
    }

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
