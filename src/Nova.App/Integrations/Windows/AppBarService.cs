using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Docks a window to the top or bottom edge of a monitor as a real Windows "AppBar" (the same mechanism the taskbar uses).
/// Windows then shrinks the desktop work area, so maximized and snapped windows stop next to the bar instead of
/// being covered by it. The reservation is released when the bar is removed or the process ends.
/// </summary>
public sealed class AppBarService : IDisposable
{
    const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_ACTIVATE = 6, ABM_WINDOWPOSCHANGED = 9;
    const uint ABE_TOP = 1, ABE_BOTTOM = 3;
    const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
    const int WM_ACTIVATE = 0x0006, WM_WINDOWPOSCHANGED = 0x0047;

    readonly Window _window;
    readonly bool _bottom;
    IntPtr _hwnd;
    HwndSource? _source;
    int _callbackMessage;
    bool _registered, _positioning, _disposed;
    string? _monitor;
    double _sizeDip = 32;

    /// <summary>True when a full-screen app/game starts or stops (the bar then drops behind it).</summary>
    public event Action<bool>? FullscreenChanged;
    /// <summary>Raised after the bar was (re)positioned, so overlapping windows can restore their z-order.</summary>
    public event Action? Repositioned;

    public bool IsRegistered => _registered;
    /// <summary>Screen rows (physical pixels) the bar really occupies after Windows placed it. int.MinValue = not placed.</summary>
    public int PlacedTop { get; private set; } = int.MinValue;
    public int PlacedBottom { get; private set; } = int.MinValue;

    public AppBarService(Window window, bool bottom = false) { _window = window; _bottom = bottom; }

    NativeMethods.APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        hWnd = _hwnd
    };

    /// <summary>Registers (first call) and positions the bar. Safe to call again when the size or monitor changes.</summary>
    public bool Dock(string? monitor, double sizeDip)
    {
        if (_disposed) return false;
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();

        // Moving to another monitor: release the old edge first so the new one is measured cleanly.
        if (_registered && !string.Equals(_monitor ?? "", monitor ?? "", StringComparison.OrdinalIgnoreCase)) Unregister();

        _monitor = monitor;
        _sizeDip = sizeDip;

        if (!_registered)
        {
            _callbackMessage = (int)NativeMethods.RegisterWindowMessage("NOVA.AppBar.v1");
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(Hook);

            var abd = NewData();
            abd.uCallbackMessage = (uint)_callbackMessage;
            _registered = NativeMethods.SHAppBarMessage(ABM_NEW, ref abd) != UIntPtr.Zero;
            if (!_registered)
            {
                _source?.RemoveHook(Hook);
                _source = null;
                FileLog.Write(_bottom ? "The dock space could not be registered with Windows." : "The top bar could not be registered with Windows.");
                return false;
            }
        }

        Position();
        return true;
    }

    /// <summary>Recomputes the position (monitor plugged/unplugged, resolution or scale changed).</summary>
    public void Reposition() => Position();

    void Position()
    {
        if (!_registered || _positioning) return;
        _positioning = true;
        try
        {
            var bounds = MonitorService.Resolve(_monitor).Bounds;

            // Land on the target monitor first so the DPI we read is that monitor's.
            int probeTop = _bottom ? bounds.Bottom - 32 : bounds.Top;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, bounds.Left, probeTop, bounds.Width, 32,
                NativeMethods.SWP_NOACTIVATE);
            int sizePx = (int)Math.Ceiling(_sizeDip * VisualTreeHelper.GetDpi(_window).DpiScaleY);

            var abd = NewData();
            abd.uEdge = _bottom ? ABE_BOTTOM : ABE_TOP;
            abd.rc = _bottom
                ? new NativeMethods.RECT { Left = bounds.Left, Top = bounds.Bottom - sizePx, Right = bounds.Right, Bottom = bounds.Bottom }
                : new NativeMethods.RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Top + sizePx };
            NativeMethods.SHAppBarMessage(ABM_QUERYPOS, ref abd);
            // QUERYPOS may move the edge (another bar on it); the thickness stays ours.
            if (_bottom) abd.rc.Top = abd.rc.Bottom - sizePx; else abd.rc.Bottom = abd.rc.Top + sizePx;
            NativeMethods.SHAppBarMessage(ABM_SETPOS, ref abd);

            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
                abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top, NativeMethods.SWP_NOACTIVATE);
            PlacedTop = abd.rc.Top;
            PlacedBottom = abd.rc.Bottom;
        }
        finally { _positioning = false; }
        Repositioned?.Invoke();
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_registered) return IntPtr.Zero;

        if (msg == _callbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    Position();
                    break;
                case ABN_FULLSCREENAPP:
                {
                    bool fullscreen = lParam != IntPtr.Zero;
                    NativeMethods.SetWindowPos(_hwnd, fullscreen ? NativeMethods.HWND_BOTTOM : NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    FullscreenChanged?.Invoke(fullscreen);
                    break;
                }
            }
            handled = true;
        }
        else if (msg == WM_ACTIVATE)
        {
            var abd = NewData();
            NativeMethods.SHAppBarMessage(ABM_ACTIVATE, ref abd);
        }
        else if (msg == WM_WINDOWPOSCHANGED)
        {
            var abd = NewData();
            NativeMethods.SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref abd);
        }
        return IntPtr.Zero;
    }

    void Unregister()
    {
        if (_registered)
        {
            var abd = NewData();
            NativeMethods.SHAppBarMessage(ABM_REMOVE, ref abd);
        }
        _source?.RemoveHook(Hook);
        _source = null;
        _registered = false;
        PlacedTop = PlacedBottom = int.MinValue;
    }

    /// <summary>Gives the reserved space back but keeps the service usable: <see cref="Dock"/> can reserve it again later.</summary>
    public void Release() => Unregister();

    /// <summary>Releases the reserved screen space for good. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
