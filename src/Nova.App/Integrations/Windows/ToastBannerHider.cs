using System.Runtime.InteropServices;
using System.Text;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Keeps Windows' own toast banner off screen, so a notification shows once, on the Island.
/// Best effort: it moves the banner window out of view (it does not change any Windows setting) and puts nothing back to undo,
/// because the banner is recreated by Windows for every toast. Start it only while the Island is able to show those toasts.
/// The notification sound is Windows' own and is not touched.
/// </summary>
public sealed class ToastBannerHider : IDisposable
{
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out NativeMethods.RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    const uint EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0;
    const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    const int OffScreen = -32000;

    IntPtr _show, _move;
    WinEventProc? _proc;

    public void Start()
    {
        if (_show != IntPtr.Zero) return;
        try
        {
            // Out-of-context: the callback runs on this thread's message loop, never inside another program.
            _proc = OnEvent;
            _show = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            _move = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
        catch (Exception ex) { FileLog.Write($"Banner hider failed: {ex.GetType().Name}"); Stop(); }
    }

    void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero || !IsToastBanner(hwnd)) return;
        try
        {
            if (GetWindowRect(hwnd, out var r) && r.Left <= OffScreen / 2) return;   // already out of view
            SetWindowPos(hwnd, IntPtr.Zero, OffScreen, OffScreen, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { /* the banner closed meanwhile */ }
    }

    /// <summary>The shell's banner is a XAML island or CoreWindow whose title is "New notification".</summary>
    static bool IsToastBanner(IntPtr hwnd)
    {
        var cls = new StringBuilder(64);
        if (GetClassName(hwnd, cls, cls.Capacity) == 0) return false;
        var c = cls.ToString();
        if (c != "Windows.UI.Core.CoreWindow" && c != "XamlExplorerHostIslandWindow") return false;
        var title = new StringBuilder(64);
        GetWindowText(hwnd, title, title.Capacity);
        return title.ToString().Contains("notification", StringComparison.OrdinalIgnoreCase);
    }

    public void Stop()
    {
        if (_show != IntPtr.Zero) UnhookWinEvent(_show);
        if (_move != IntPtr.Zero) UnhookWinEvent(_move);
        _show = _move = IntPtr.Zero;
        _proc = null;
    }

    public void Dispose() => Stop();
}
