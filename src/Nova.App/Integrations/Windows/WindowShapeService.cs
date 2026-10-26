using System.Runtime.InteropServices;
using System.Windows.Threading;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Gives other programs' windows large rounded corners (a "rounded square"). Windows 11 only offers one small corner size, so NOVA
/// clips each window to a rounded shape with the window region that Windows provides for this (SetWindowRgn). Best-effort and reversible:
/// every region NOVA set is removed again when this is switched off or NOVA exits.
///
/// Honest limits: the clipped edge is not anti-aliased (it can look slightly jagged), the window's own shadow is clipped, windows that
/// already have a shape of their own, maximized windows and full-screen windows are left alone, and an elevated (administrator)
/// program cannot be changed by a normal program.
/// </summary>
public sealed class WindowShapeService : IDisposable
{
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hWnd, IntPtr hRgn);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dest, IntPtr a, IntPtr b, int mode);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out NativeMethods.RECT rect, int size);
    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const int RGN_OR = 2, RGN_DIFF = 4;
    const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0;

    sealed class Shaped { public int W, H; public double Radius; }

    readonly RunningAppsService _apps = new();
    readonly Dictionary<IntPtr, Shaped> _shaped = new();
    readonly HashSet<IntPtr> _skipped = new();      // windows NOVA decided to leave alone (own shape, refused...), not retried every sweep
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly HashSet<IntPtr> _pending = new();
    WinEventProc? _proc;      // kept in a field so the garbage collector never frees the callback
    IntPtr _hook;
    bool _on;
    double _radiusDip = 20;

    public WindowShapeService() => _timer.Tick += (_, _) => Sweep();

    /// <summary>Switches the rounded windows on (with a radius in device-independent pixels) or off.</summary>
    public void Apply(bool on, double radiusDip)
    {
        bool changed = on && _on && Math.Abs(radiusDip - _radiusDip) > 0.5;
        _on = on; _radiusDip = Math.Min(40, Math.Max(6, radiusDip));
        if (!on) { Stop(); return; }
        if (changed) foreach (var h in _shaped.Keys.ToList()) Shape(h, force: true);
        Start();
        Sweep();
    }

    void Start()
    {
        if (_hook == IntPtr.Zero)
        {
            _proc = OnLocationChange;
            // Out-of-context: the callback runs on this thread's message loop, never inside another program.
            _hook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
        _timer.Start();
    }

    void Stop()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        _proc = null;
        RestoreAll();
        _skipped.Clear();
    }

    // A shaped window was moved or resized: its clip must follow the new size, right away (batched on the next idle moment).
    void OnLocationChange(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || !_on || !_shaped.ContainsKey(hwnd)) return;
        if (!_pending.Add(hwnd)) return;
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _pending.Remove(hwnd);
            if (_on && _shaped.ContainsKey(hwnd)) Shape(hwnd, force: false);
        }, DispatcherPriority.Input);
    }

    void Sweep()
    {
        try
        {
            foreach (var h in _shaped.Keys.ToList())
                if (!NativeMethods.IsWindow(h)) _shaped.Remove(h);
            foreach (var h in _skipped.ToList())
                if (!NativeMethods.IsWindow(h)) _skipped.Remove(h);

            foreach (var h in _apps.AllWindows())
            {
                if (_shaped.ContainsKey(h) || _skipped.Contains(h)) continue;
                Shape(h, force: true);
            }
        }
        catch (Exception ex) { FileLog.Write($"Window shape sweep failed: {ex.GetType().Name}"); }
    }

    static bool HasOwnRegion(IntPtr h)
    {
        var probe = CreateRectRgn(0, 0, 0, 0);
        try { return GetWindowRgn(h, probe) != 0; }   // 0 = ERROR = the window has no region
        finally { DeleteObject(probe); }
    }

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    static bool IsFullscreenSized(IntPtr h, NativeMethods.RECT r)
    {
        try
        {
            var b = System.Windows.Forms.Screen.FromHandle(h).Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }
        catch { return false; }
    }

    /// <summary>Clips one window to a rounded rectangle (or removes the clip when it is maximized, minimized or full-screen).</summary>
    void Shape(IntPtr h, bool force)
    {
        try
        {
            if (!NativeMethods.IsWindow(h)) { _shaped.Remove(h); return; }
            bool known = _shaped.TryGetValue(h, out var state);

            if (NativeMethods.IsIconic(h) || NativeMethods.IsZoomed(h)) { if (known) Unshape(h); return; }
            if (!NativeMethods.GetWindowRect(h, out var win)) return;
            if (IsFullscreenSized(h, win)) { if (known) Unshape(h); return; }

            int w = win.Right - win.Left, hgt = win.Bottom - win.Top;
            if (w < 80 || hgt < 60) return;
            if (known && !force && state!.W == w && state.H == hgt) return;   // nothing changed

            if (!known && HasOwnRegion(h)) { _skipped.Add(h); return; }       // the program shaped itself: not ours to change

            // The visible frame is smaller than the window rectangle (Windows adds an invisible resize border). Round the visible frame.
            NativeMethods.RECT vis = win;
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var ext, Marshal.SizeOf<NativeMethods.RECT>()) == 0
                && ext.Right > ext.Left && ext.Bottom > ext.Top) vis = ext;

            double scale = GetDpiForWindow(h) / 96.0;
            if (scale <= 0) scale = 1;
            int r = (int)Math.Round(_radiusDip * scale);
            int dx = vis.Left - win.Left, dy = vis.Top - win.Top;
            int vl = dx, vt = dy, vr = dx + (vis.Right - vis.Left), vb = dy + (vis.Bottom - vis.Top);

            // Region = (the whole window rectangle without its four corner squares, so the invisible resize border keeps working)
            //          + (the visible frame as a rounded rectangle).
            var full = CreateRectRgn(0, 0, w, hgt);
            int cs = r + Math.Max(0, Math.Max(dx, Math.Max(dy, Math.Max(w - vr, hgt - vb)))) + 1;   // corner square size
            foreach (var (cl, ct) in new[] { (0, 0), (w - cs, 0), (0, hgt - cs), (w - cs, hgt - cs) })
            {
                var corner = CreateRectRgn(cl, ct, cl + cs, ct + cs);
                CombineRgn(full, full, corner, RGN_DIFF);
                DeleteObject(corner);
            }
            var rounded = CreateRoundRectRgn(vl, vt, vr + 1, vb + 1, r * 2, r * 2);
            CombineRgn(full, full, rounded, RGN_OR);
            DeleteObject(rounded);

            // Windows owns the region after a successful SetWindowRgn; delete it ourselves only when the call failed.
            if (SetWindowRgn(h, full, true) == 0) { DeleteObject(full); _skipped.Add(h); _shaped.Remove(h); return; }
            _shaped[h] = new Shaped { W = w, H = hgt, Radius = _radiusDip };
        }
        catch (Exception ex) { FileLog.Write($"Window shape failed: {ex.GetType().Name}"); _skipped.Add(h); }
    }

    void Unshape(IntPtr h)
    {
        try { if (NativeMethods.IsWindow(h)) SetWindowRgn(h, IntPtr.Zero, true); }
        catch { /* best effort */ }
        _shaped.Remove(h);
    }

    /// <summary>Removes every shape NOVA set.</summary>
    public void RestoreAll()
    {
        foreach (var h in _shaped.Keys.ToList()) Unshape(h);
        _shaped.Clear();
    }

    public void Dispose() { Stop(); }
}
