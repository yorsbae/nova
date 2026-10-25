using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Nova.App.Integrations.Windows;
using Nova.Core.Logging;
using WinForms = System.Windows.Forms;

namespace Nova.App.Features.FloatingDock;

/// <summary>
/// The "minimize into the dock / grow out of the dock" animation of the Mac. Windows animates minimize towards its own
/// (hidden) taskbar, so NOVA does it itself: it takes a picture of the window, shows the picture in a see-through overlay,
/// minimizes or restores the real window with Windows' own animation switched off, and flies the picture between the window
/// and its dock icon. If anything goes wrong the window is simply minimized or restored without the animation.
/// </summary>
internal sealed class WindowGenie
{
    const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    const uint PW_RENDERFULLCONTENT = 0x2;

    readonly Dictionary<IntPtr, BitmapSource> _shots = new();
    bool _busy;

    public bool Busy => _busy;

    // ---- the overlay that carries the picture ----

    sealed class GhostWindow : Window
    {
        public readonly Image Pic = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5) };
        public readonly ScaleTransform Zoom = new(1, 1);
        public readonly TranslateTransform Move = new(0, 0);

        public GhostWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Width = 1; Height = 1;
            var group = new TransformGroup();
            group.Children.Add(Zoom);
            group.Children.Add(Move);
            Pic.RenderTransform = group;
            RenderOptions.SetBitmapScalingMode(Pic, BitmapScalingMode.HighQuality);
            Content = new Canvas { IsHitTestVisible = false, Children = { Pic } };
            SourceInitialized += (_, _) => NativeMethods.SetClickThrough(this);
        }
    }

    // ---- public entry points ----

    /// <summary>Minimizes the window into the dock icon at <paramref name="iconCenterPx"/> (screen pixels).</summary>
    public void Minimize(IntPtr hwnd, Point iconCenterPx, double iconPx, bool animate)
    {
        if (_busy) return;
        if (!animate) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE); return; }
        _busy = true;
        _ = MinimizeAsync(hwnd, iconCenterPx, iconPx);
    }

    /// <summary>Brings a minimized window back out of its dock icon.</summary>
    public void Restore(IntPtr hwnd, Point iconCenterPx, double iconPx, bool animate)
    {
        if (_busy) return;
        if (!animate || !_shots.ContainsKey(hwnd)) { NativeMethods.Activate(hwnd); return; }
        _busy = true;
        _ = RestoreAsync(hwnd, iconCenterPx, iconPx);
    }

    /// <summary>Forget pictures of windows that no longer exist.</summary>
    public void Prune()
    {
        foreach (var h in _shots.Keys.ToList())
            if (!NativeMethods.IsWindow(h)) _shots.Remove(h);
    }

    // ---- minimize ----

    async Task MinimizeAsync(IntPtr hwnd, Point icon, double iconPx)
    {
        GhostWindow? ghost = null;
        try
        {
            var shot = Capture(hwnd, out var rect);
            if (shot is null) { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE); return; }
            _shots[hwnd] = shot;
            ghost = ShowGhost(shot, rect, out var dpi);

            await Task.Delay(45);   // let the picture paint once, so there is no gap when the real window goes away
            NativeMethods.TrySetDwm(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);

            var to = TargetPose(rect, icon, iconPx, dpi);
            await FlyAsync(ghost, Pose.Rest, to, 260, new CubicEase { EasingMode = EasingMode.EaseIn });
        }
        catch (Exception ex)
        {
            FileLog.Write($"Minimize animation failed: {ex.GetType().Name}");
            try { NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE); } catch { }
        }
        finally
        {
            try { ghost?.Close(); } catch { }
            NativeMethods.TrySetDwm(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 0);
            _busy = false;
        }
    }

    // ---- restore ----

    async Task RestoreAsync(IntPtr hwnd, Point icon, double iconPx)
    {
        GhostWindow? ghost = null;
        try
        {
            var shot = _shots[hwnd];
            var rect = RestoredRect(hwnd);
            ghost = ShowGhost(shot, rect, out var dpi);

            var from = TargetPose(rect, icon, iconPx, dpi);
            ghost.Zoom.ScaleX = from.ScaleX; ghost.Zoom.ScaleY = from.ScaleY;
            ghost.Move.X = from.X; ghost.Move.Y = from.Y;
            ghost.Pic.Opacity = from.Opacity;

            await FlyAsync(ghost, from, Pose.Rest, 280, new CubicEase { EasingMode = EasingMode.EaseOut });

            NativeMethods.TrySetDwm(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            NativeMethods.Activate(hwnd);   // restores and focuses; the picture still covers the spot, so there is no pop
            await Task.Delay(90);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Restore animation failed: {ex.GetType().Name}");
            try { NativeMethods.Activate(hwnd); } catch { }
        }
        finally
        {
            try { ghost?.Close(); } catch { }
            NativeMethods.TrySetDwm(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 0);
            _busy = false;
        }
    }

    // ---- poses and flying ----

    readonly record struct Pose(double ScaleX, double ScaleY, double X, double Y, double Opacity)
    {
        public static readonly Pose Rest = new(1, 1, 0, 0, 1);
    }

    /// <summary>The window shrunk onto the icon: scale to the icon's size, move its centre to the icon's centre, and fade a little.</summary>
    static Pose TargetPose(NativeMethods.RECT rect, Point iconPx, double iconSizePx, double dpi)
    {
        double w = Math.Max(1, rect.Right - rect.Left), h = Math.Max(1, rect.Bottom - rect.Top);
        double cx = rect.Left + w / 2, cy = rect.Top + h / 2;
        double size = Math.Max(8, iconSizePx);
        return new Pose(size / w, size / h, (iconPx.X - cx) / dpi, (iconPx.Y - cy) / dpi, 0.25);
    }

    static Task FlyAsync(GhostWindow g, Pose from, Pose to, int ms, IEasingFunction ease)
    {
        var done = new TaskCompletionSource<bool>();
        var d = TimeSpan.FromMilliseconds(ms);
        DoubleAnimation A(double a, double b) => new(a, b, d) { EasingFunction = ease };

        var sx = A(from.ScaleX, to.ScaleX);
        sx.Completed += (_, _) => done.TrySetResult(true);
        g.Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        g.Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, A(from.ScaleY, to.ScaleY));
        g.Move.BeginAnimation(TranslateTransform.XProperty, A(from.X, to.X));
        g.Move.BeginAnimation(TranslateTransform.YProperty, A(from.Y, to.Y));
        g.Pic.BeginAnimation(UIElement.OpacityProperty, A(from.Opacity, to.Opacity));
        return done.Task;
    }

    // ---- window pictures and overlay ----

    GhostWindow ShowGhost(BitmapSource shot, NativeMethods.RECT rect, out double dpi)
    {
        var mon = WinForms.Screen.FromRectangle(new System.Drawing.Rectangle(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top))).Bounds;
        var g = new GhostWindow { Left = mon.Left, Top = mon.Top };
        g.Show();
        var handle = new WindowInteropHelper(g).Handle;
        // Two passes: moving to a monitor with another scale makes WPF rescale the window.
        for (int pass = 0; pass < 2; pass++)
            NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, mon.Left, mon.Top, mon.Width, mon.Height, NativeMethods.SWP_NOACTIVATE);
        g.UpdateLayout();

        dpi = VisualTreeHelper.GetDpi(g).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        var pic = g.Pic;
        pic.Source = shot;
        pic.Width = (rect.Right - rect.Left) / dpi;
        pic.Height = (rect.Bottom - rect.Top) / dpi;
        Canvas.SetLeft(pic, (rect.Left - mon.Left) / dpi);
        Canvas.SetTop(pic, (rect.Top - mon.Top) / dpi);
        return g;
    }

    /// <summary>A picture of the window as it looks now. null if Windows can't draw it (some protected or GPU-only windows).</summary>
    static BitmapSource? Capture(IntPtr hwnd, out NativeMethods.RECT rect)
    {
        rect = default;
        if (!NativeMethods.GetWindowRect(hwnd, out rect)) return null;
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w < 48 || h < 48 || w > 8192 || h > 8192) return null;

        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            bool ok;
            try { ok = NativeMethods.PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
            finally { g.ReleaseHdc(hdc); }
            if (!ok) return null;
        }
        if (LooksEmpty(bmp)) return null;

        var hbmp = bmp.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { NativeMethods.DeleteObject(hbmp); }
    }

    /// <summary>Some windows come out completely black; a black flash would look worse than no animation.</summary>
    static bool LooksEmpty(System.Drawing.Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        for (int iy = 1; iy <= 3; iy++)
            for (int ix = 1; ix <= 3; ix++)
            {
                var c = bmp.GetPixel(w * ix / 4, h * iy / 4);
                if ((c.R | c.G | c.B) != 0) return false;
            }
        return true;
    }

    /// <summary>Where a minimized window will appear when restored (screen pixels).</summary>
    static NativeMethods.RECT RestoredRect(IntPtr hwnd)
    {
        var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref wp)) return new NativeMethods.RECT { Left = 100, Top = 100, Right = 900, Bottom = 700 };

        // The normal position is in "workspace" coordinates: relative to the top-left of the primary monitor's work area.
        var origin = (WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0]).WorkingArea;
        var r = wp.rcNormalPosition;
        r.Left += origin.Left; r.Right += origin.Left; r.Top += origin.Top; r.Bottom += origin.Top;

        if ((wp.flags & NativeMethods.WPF_RESTORETOMAXIMIZED) != 0)
        {
            var wa = WinForms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top))).WorkingArea;
            r = new NativeMethods.RECT { Left = wa.Left, Top = wa.Top, Right = wa.Right, Bottom = wa.Bottom };
        }
        return r;
    }
}
