using System.Globalization;

namespace Nova.Core.Status;

public static class CpuUsage
{
    /// <summary>
    /// From two GetSystemTimes samples (100 ns ticks). Windows' kernel time INCLUDES idle time.
    /// Returns 0–100, or null if no time elapsed / counters went backwards.
    /// </summary>
    public static double? Compute(ulong idle0, ulong kernel0, ulong user0, ulong idle1, ulong kernel1, ulong user1)
    {
        if (idle1 < idle0 || kernel1 < kernel0 || user1 < user0) return null;
        var total = (kernel1 - kernel0) + (user1 - user0);
        if (total == 0) return null;
        var idle = idle1 - idle0;
        var busy = total > idle ? total - idle : 0;
        return Math.Round(100.0 * busy / total, 0);
    }
}

public static class MemoryUsage
{
    public static double? UsedPercent(ulong totalBytes, ulong availableBytes)
        => totalBytes == 0 || availableBytes > totalBytes ? null : Math.Round(100.0 * (totalBytes - availableBytes) / totalBytes, 0);
}

public sealed record BatteryInfo(int Percent, bool OnAc, bool Charging)
{
    /// <summary>From SYSTEM_POWER_STATUS fields. Returns null on desktops (flag 128) or when unknown (255).</summary>
    public static BatteryInfo? From(byte acLineStatus, byte batteryFlag, byte percent)
    {
        if (batteryFlag == 255 || (batteryFlag & 128) != 0 || percent > 100) return null;
        return new BatteryInfo(percent, acLineStatus == 1, (batteryFlag & 8) != 0);
    }
}

public static class StatusFormatter
{
    /// <summary>Plain-text status line; every value is labelled so nothing relies on color or icons.</summary>
    public static string Format(double? cpu, double? memory, BatteryInfo? battery)
    {
        var parts = new List<string>();
        var c = CultureInfo.InvariantCulture;
        if (cpu is not null) parts.Add($"CPU {cpu.Value.ToString("0", c)}%");
        if (memory is not null) parts.Add($"Memory {memory.Value.ToString("0", c)}%");
        if (battery is not null)
            parts.Add($"Battery {battery.Percent}%" + (battery.Charging ? " (charging)" : battery.OnAc ? " (plugged in)" : ""));
        return string.Join("  ·  ", parts);
    }
}

/// <summary>One reading of the machine's load, for the indicators. Null = unknown (for example CPU on the very first reading).</summary>
public sealed record SystemMetrics(double? Cpu, double? Memory, double? Disk, BatteryInfo? Battery, double DownBytesPerSecond, double UpBytesPerSecond);

public static class RateFormatter
{
    /// <summary>"0 B/s", "850 KB/s", "1.2 MB/s": short enough for a small label.</summary>
    public static string Format(double bytesPerSecond)
    {
        var c = CultureInfo.InvariantCulture;
        if (double.IsNaN(bytesPerSecond) || bytesPerSecond < 1) return "0 B/s";
        if (bytesPerSecond < 1024) return $"{bytesPerSecond.ToString("0", c)} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{(bytesPerSecond / 1024).ToString("0", c)} KB/s";
        if (bytesPerSecond < 1024.0 * 1024 * 1024) return $"{(bytesPerSecond / 1024 / 1024).ToString("0.0", c)} MB/s";
        return $"{(bytesPerSecond / 1024 / 1024 / 1024).ToString("0.0", c)} GB/s";
    }

    /// <summary>Compact one-line reading for the Island: "CPU 12% · RAM 48%".</summary>
    public static string Compact(double? cpu, double? memory)
    {
        var c = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        if (cpu is not null) parts.Add($"CPU {cpu.Value.ToString("0", c)}%");
        if (memory is not null) parts.Add($"RAM {memory.Value.ToString("0", c)}%");
        return string.Join(" · ", parts);
    }
}
