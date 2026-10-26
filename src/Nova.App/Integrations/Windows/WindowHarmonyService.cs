using System.Windows.Threading;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Gives other programs' windows one look, like a Mac theme: the same light/dark mode, title-bar colour, title text colour,
/// round corners and no coloured border. It uses Windows' own per-window "DWM" settings (Windows 11 for colours and corners,
/// Windows 10 2004+ for dark mode), is best-effort, and puts every window back exactly as it was when switched off or on exit.
/// Programs that draw their own title bar (browsers, some editors) keep their own tab strip: set the program's own theme for that.
/// </summary>
public sealed class WindowHarmonyService : IDisposable
{
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34,
              DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
    const int ColorNone = unchecked((int)0xFFFFFFFE);
    const int ColorDefault = unchecked((int)0xFFFFFFFF);
    const int CornerRound = 2;

    sealed class Original
    {
        public bool HasDark, HasCaption, HasBorder, HasText, HasCorner;
        public int Dark, Caption, Border, Text, Corner;
    }

    readonly RunningAppsService _apps = new();
    readonly Dictionary<IntPtr, Original> _styled = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(6) };
    bool _on, _dark;

    public WindowHarmonyService() => _timer.Tick += (_, _) => Sweep();

    /// <summary>Switches the look on (with the given light/dark mode) or off.</summary>
    public void Apply(bool on, bool dark)
    {
        bool changed = on && _on && dark != _dark;
        _on = on; _dark = dark;
        if (!on) { _timer.Stop(); RestoreAll(); return; }
        if (changed) foreach (var h in _styled.Keys.ToList()) Style(h, _styled[h], dark);
        Sweep();
        _timer.Start();
    }

    static int Rgb(int r, int g, int b) => r | (g << 8) | (b << 16);   // COLORREF

    void Sweep()
    {
        try
        {
            foreach (var h in _styled.Keys.ToList())
                if (!NativeMethods.IsWindow(h)) _styled.Remove(h);

            foreach (var h in _apps.AllWindows())
            {
                if (_styled.ContainsKey(h) || IsFullscreenSized(h)) continue;
                var o = new Original();
                o.HasDark = NativeMethods.TryGetDwm(h, DWMWA_USE_IMMERSIVE_DARK_MODE, out o.Dark);
                o.HasCaption = NativeMethods.TryGetDwm(h, DWMWA_CAPTION_COLOR, out o.Caption);
                o.HasBorder = NativeMethods.TryGetDwm(h, DWMWA_BORDER_COLOR, out o.Border);
                o.HasText = NativeMethods.TryGetDwm(h, DWMWA_TEXT_COLOR, out o.Text);
                o.HasCorner = NativeMethods.TryGetDwm(h, DWMWA_WINDOW_CORNER_PREFERENCE, out o.Corner);
                _styled[h] = o;   // remembered even if Windows refuses, so a refused window isn't retried every sweep
                Style(h, o, _dark);
            }
        }
        catch (Exception ex) { FileLog.Write($"Window harmony sweep failed: {ex.GetType().Name}"); }
    }

    static bool IsFullscreenSized(IntPtr h)
    {
        try
        {
            if (!NativeMethods.GetWindowRect(h, out var r)) return false;
            var b = System.Windows.Forms.Screen.FromHandle(h).Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom && !NativeMethods.IsZoomed(h);
        }
        catch { return false; }
    }

    static void Style(IntPtr h, Original o, bool dark)
    {
        NativeMethods.TrySetDwm(h, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        NativeMethods.TrySetDwm(h, DWMWA_CAPTION_COLOR, dark ? Rgb(0x1E, 0x1E, 0x22) : Rgb(0xEC, 0xEC, 0xEE));
        NativeMethods.TrySetDwm(h, DWMWA_TEXT_COLOR, dark ? Rgb(0xEB, 0xEB, 0xF0) : Rgb(0x1E, 0x1E, 0x22));
        NativeMethods.TrySetDwm(h, DWMWA_BORDER_COLOR, ColorNone);
        NativeMethods.TrySetDwm(h, DWMWA_WINDOW_CORNER_PREFERENCE, CornerRound);
    }

    /// <summary>Puts every window back the way NOVA found it.</summary>
    public void RestoreAll()
    {
        foreach (var (h, o) in _styled)
        {
            try
            {
                if (!NativeMethods.IsWindow(h)) continue;
                if (o.HasDark) NativeMethods.TrySetDwm(h, DWMWA_USE_IMMERSIVE_DARK_MODE, o.Dark);
                NativeMethods.TrySetDwm(h, DWMWA_CAPTION_COLOR, o.HasCaption ? o.Caption : ColorDefault);
                NativeMethods.TrySetDwm(h, DWMWA_TEXT_COLOR, o.HasText ? o.Text : ColorDefault);
                NativeMethods.TrySetDwm(h, DWMWA_BORDER_COLOR, o.HasBorder ? o.Border : ColorDefault);
                if (o.HasCorner) NativeMethods.TrySetDwm(h, DWMWA_WINDOW_CORNER_PREFERENCE, o.Corner);
            }
            catch { }
        }
        _styled.Clear();
    }

    public void Dispose() { _timer.Stop(); RestoreAll(); }
}
