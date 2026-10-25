using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace Nova.App.Integrations.Windows;

public sealed record MonitorChoice(string DeviceName, string Label);

public static class MonitorService
{
    public static IReadOnlyList<MonitorChoice> List()
    {
        var list = new List<MonitorChoice> { new("", "Primary monitor (default)") };
        foreach (var s in WinForms.Screen.AllScreens)
        {
            var digits = new string(s.DeviceName.Where(char.IsDigit).ToArray());
            var label = $"Display {(digits.Length > 0 ? digits : "?")}: {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? ", primary" : "")}";
            list.Add(new MonitorChoice(s.DeviceName, label));
        }
        return list;
    }

    /// <summary>The named monitor, or the primary one if it's empty or no longer connected.</summary>
    public static WinForms.Screen Resolve(string? deviceName)
    {
        var all = WinForms.Screen.AllScreens;
        if (!string.IsNullOrEmpty(deviceName))
        {
            var match = all.FirstOrDefault(s => string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return WinForms.Screen.PrimaryScreen ?? all[0];
    }
}

/// <summary>Top = below the work area top (plus offset). TopFlush = the very top of the monitor, ignoring any docked bar. Bottom = bottom of the work area.</summary>
public enum PlacementEdge { Top, TopFlush, Bottom, BottomFlush }

/// <summary>
/// Positions an overlay centred on a monitor's work area in physical pixels. Two passes, because moving a
/// window to a monitor with a different scale makes WPF rescale it, which changes its pixel size.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Centres the window horizontally on the monitor with its bottom edge at an exact screen row (physical pixels).</summary>
    public static void PlaceCenteredAtBottom(Window w, string? monitor, int bottomPx)
    {
        var handle = new WindowInteropHelper(w).EnsureHandle();
        var bounds = MonitorService.Resolve(monitor).Bounds;

        double wDip = w.ActualWidth, hDip = w.ActualHeight;
        if (wDip <= 0 || hDip <= 0)
        {
            w.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            wDip = w.DesiredSize.Width; hDip = w.DesiredSize.Height;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            var dpi = VisualTreeHelper.GetDpi(w);
            int wPx = (int)Math.Round(wDip * dpi.DpiScaleX), hPx = (int)Math.Round(hDip * dpi.DpiScaleY);
            int x = bounds.Left + (bounds.Width - wPx) / 2;
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, bottomPx - hPx, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }

    public static void PlaceCentered(Window w, string? monitor, PlacementEdge edge, double offsetDip)
    {
        var handle = new WindowInteropHelper(w).EnsureHandle();
        var screen = MonitorService.Resolve(monitor);
        var wa = edge is PlacementEdge.TopFlush or PlacementEdge.BottomFlush ? screen.Bounds : screen.WorkingArea;

        double wDip = w.ActualWidth, hDip = w.ActualHeight;
        if (wDip <= 0 || hDip <= 0)
        {
            w.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            wDip = w.DesiredSize.Width; hDip = w.DesiredSize.Height;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            var dpi = VisualTreeHelper.GetDpi(w);
            int wPx = (int)Math.Round(wDip * dpi.DpiScaleX), hPx = (int)Math.Round(hDip * dpi.DpiScaleY);
            int x = wa.Left + (wa.Width - wPx) / 2;
            int y = edge is PlacementEdge.Bottom or PlacementEdge.BottomFlush ? wa.Bottom - hPx : wa.Top + (int)Math.Round(offsetDip * dpi.DpiScaleY);
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }
}

/// <summary>Opaque, rectangular fallback for GPUs/drivers that mishandle transparent windows. Must run before the window is shown.</summary>
internal static class OverlayChrome
{
    public static void Configure(Window w, bool opaque)
    {
        if (!opaque) return;
        w.AllowsTransparency = false;
        w.Background = Application.Current.Resources["NovaWindow"] as Brush ?? SystemColors.WindowBrush;
    }
}
