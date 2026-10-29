using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nova.App.Integrations.Windows;

internal static class NativeMethods
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    internal const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    internal static readonly IntPtr HWND_TOPMOST = new(-1), HWND_BOTTOM = new(1);

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // PDH: performance counters addressed by their English names, so they work on any Windows display language.
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] internal static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] internal static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll")] internal static extern uint PdhCloseQuery(IntPtr query);
    internal const uint PDH_FMT_DOUBLE = 0x200;
    [StructLayout(LayoutKind.Explicit)] internal struct PDH_FMT_COUNTERVALUE { [FieldOffset(0)] public uint CStatus; [FieldOffset(8)] public double DoubleValue; }

    [DllImport("kernel32.dll")] internal static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // ---- window enumeration, activation, Start key ----
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] internal static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    static extern IntPtr SendMessageTimeoutPtr(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    // ---- v2.3: foreground / minimize, window rectangles, PrintWindow, DWM attributes, shell hook ----
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", SetLastError = true)] static extern bool LockWorkStation();

    /// <summary>Locks the PC (the same as Win+L). False if Windows refused.</summary>
    internal static bool Lock()
    {
        try { return LockWorkStation(); }
        catch { return false; }
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] internal static extern bool RegisterShellHookWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool DeregisterShellHookWindow(IntPtr hWnd);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    internal const int SW_MINIMIZE = 6, SW_RESTORE = 9;
    internal const uint GA_ROOT = 2;
    internal const int WPF_RESTORETOMAXIMIZED = 0x2;

    /// <summary>The window as a whole (not just a tab): the foreground window's top-level ancestor.</summary>
    internal static IntPtr ForegroundRoot()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        var root = GetAncestor(fg, GA_ROOT);
        return root != IntPtr.Zero ? root : fg;
    }

    /// <summary>Reads a DWM attribute of any window. Returns false if Windows doesn't know it (older Windows) or refuses.</summary>
    internal static bool TryGetDwm(IntPtr hwnd, int attribute, out int value)
    {
        try { return DwmGetWindowAttribute(hwnd, attribute, out value, sizeof(int)) == 0; }
        catch { value = 0; return false; }
    }

    /// <summary>Sets a DWM attribute on any window. Returns false if Windows doesn't know it or refuses (e.g. an elevated app).</summary>
    internal static bool TrySetDwm(IntPtr hwnd, int attribute, int value)
    {
        try { return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0; }
        catch { return false; }
    }

    /// <summary>Icon at (about) the requested pixel size straight from an .exe/.dll/.ico, so a zoomed dock icon stays sharp. null when not possible.</summary>
    internal static IntPtr ExtractIconSized(string path, int px)
    {
        try
        {
            var icons = new IntPtr[1];
            var ids = new uint[1];
            uint n = PrivateExtractIcons(path, 0, px, px, icons, ids, 1, 0);
            return n >= 1 && n != 0xFFFFFFFF ? icons[0] : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    internal const int WS_EX_APPWINDOW = 0x00040000;
    internal static int GetExStyle(IntPtr h) => GetWindowLong(h, GWL_EXSTYLE);
    internal static bool IsToolWindow(IntPtr h) => (GetExStyle(h) & WS_EX_TOOLWINDOW) != 0;
    internal static bool IsCloaked(IntPtr h) => DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int c, sizeof(int)) == 0 && c != 0;

    internal static string WindowText(IntPtr h)
    {
        int n = GetWindowTextLength(h);
        if (n <= 0) return "";
        var sb = new StringBuilder(n + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    internal static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    internal static string? ProcessPath(IntPtr hwnd, out uint pid)
    {
        GetWindowThreadProcessId(hwnd, out pid);
        var proc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (proc == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(proc, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(proc); }
    }

    /// <summary>The icon a window uses itself (needed for Store apps, whose process icon is the generic frame host).</summary>
    internal static IntPtr WindowIcon(IntPtr h)
    {
        if (SendMessageTimeoutPtr(h, 0x007F /* WM_GETICON */, new IntPtr(1), IntPtr.Zero, 0x2, 100, out var r) != IntPtr.Zero && r != IntPtr.Zero) return r;
        return GetClassLongPtr(h, -14 /* GCL_HICON */);
    }

    /// <summary>The pointer in physical screen pixels.</summary>
    internal static bool TryGetCursor(out int x, out int y)
    {
        if (GetCursorPos(out var p)) { x = p.X; y = p.Y; return true; }
        x = y = 0;
        return false;
    }

    /// <summary>True while the left mouse button is held (anywhere on screen).</summary>
    internal static bool LeftButtonDown() => (GetAsyncKeyState(0x01 /* VK_LBUTTON */) & 0x8000) != 0;

    /// <summary>
    /// Brings another program's window to the front. The dock never takes focus itself (so it never steals typing), which means Windows
    /// does not let NOVA call SetForegroundWindow directly: the call would only flash a taskbar button, and the taskbar is hidden.
    /// So the input queue of the current foreground window is shared for a moment, which is the supported way around that rule.
    /// </summary>
    internal static void Activate(IntPtr h)
    {
        if (h == IntPtr.Zero || !IsWindow(h)) return;
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);

        uint me = GetCurrentThreadId();
        var fg = GetForegroundWindow();
        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint target = GetWindowThreadProcessId(h, out _);
        bool a1 = false, a2 = false;
        try
        {
            if (fgThread != 0 && fgThread != me) a1 = AttachThreadInput(me, fgThread, true);
            if (target != 0 && target != me && target != fgThread) a2 = AttachThreadInput(me, target, true);
            BringWindowToTop(h);
            if (!SetForegroundWindow(h)) SwitchToThisWindow(h, true);
        }
        finally
        {
            if (a1) AttachThreadInput(me, fgThread, false);
            if (a2) AttachThreadInput(me, target, false);
        }

        // Still not in front (some apps refuse): a brief "topmost" flip raises it without changing its normal state.
        if (GetForegroundWindow() != h && (GetExStyle(h) & 0x00000008 /* WS_EX_TOPMOST */) == 0)
        {
            const uint flags = SWP_NOMOVE | SWP_NOSIZE | 0x0040 /* SWP_SHOWWINDOW */;
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, flags);
            SetWindowPos(h, new IntPtr(-2) /* HWND_NOTOPMOST */, 0, 0, 0, 0, flags);
            SetForegroundWindow(h);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    /// <summary>Asks Windows to treat NOVA as a background "efficiency mode" process (low-power cores, lower clock). Windows 10 1709+; silently ignored if unsupported.</summary>
    internal static void EnableEcoQoS(bool on)
    {
        try
        {
            var s = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1 /* EXECUTION_SPEED */, StateMask = on ? 1u : 0u };
            SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref s, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch { }
    }

    /// <summary>Hands unused memory pages back to Windows (they are paged in again if needed).</summary>
    internal static void TrimWorkingSet()
    {
        try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
    }

    /// <summary>True once the Windows taskbar window exists, i.e. Explorer has finished starting.</summary>
    internal static bool ShellIsReady() => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    /// <summary>Win+Shift+S: the built-in screenshot / snipping overlay.</summary>
    internal static void PressWinShiftS()
    {
        const uint UP = 0x2;
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);   // Win
        keybd_event(0x10, 0, 0, UIntPtr.Zero);   // Shift
        keybd_event(0x53, 0, 0, UIntPtr.Zero);   // S
        keybd_event(0x53, 0, UP, UIntPtr.Zero);
        keybd_event(0x10, 0, UP, UIntPtr.Zero);
        keybd_event(0x5B, 0, UP, UIntPtr.Zero);
    }

    /// <summary>Presses and releases the Windows key, which opens the Start menu.</summary>
    internal static void PressWinKey()
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x5B, 0, 0x2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
    }

    /// <summary>Win+X: Windows' own quick-link menu (what a right-click on the Start button opens).</summary>
    internal static void PressWinX()
    {
        const uint UP = 0x2;
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);   // Win
        keybd_event(0x58, 0, 0, UIntPtr.Zero);   // X
        keybd_event(0x58, 0, UP, UIntPtr.Zero);
        keybd_event(0x5B, 0, UP, UIntPtr.Zero);
    }

    // ---- AppBar (docking a bar to a screen edge), taskbar state, theme broadcast ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")] internal static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string lpString);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeoutMs, out UIntPtr result);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Windows 11 rounds the corners of top-level windows; a bar docked to the screen edge must stay square.</summary>
    internal static void DisableRoundedCorners(Window w)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        int doNotRound = 1;   // DWMWCP_DONOTROUND
        try { DwmSetWindowAttribute(h, 33, ref doNotRound, sizeof(int)); } catch { /* Windows 10: attribute unknown, nothing to do */ }
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hWnd, IntPtr region, bool redraw);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    /// <summary>
    /// For the solid-window fallback: clips the window to a rounded rectangle, so no square corners show.
    /// <paramref name="radiusDip"/> 0 removes the clip. Windows owns the region after this call.
    /// </summary>
    internal static void ApplyRoundShape(Window w, double radiusDip)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero || w.ActualWidth < 2 || w.ActualHeight < 2) return;
        var dpi = VisualTreeHelper.GetDpi(w);
        int width = (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY);
        if (radiusDip <= 0) { SetWindowRgn(h, IntPtr.Zero, true); return; }
        int d = (int)Math.Round(radiusDip * 2 * dpi.DpiScaleX);
        SetWindowRgn(h, CreateRoundRectRgn(0, 0, width + 1, height + 1, d, d), true);
    }

    /// <summary>Makes the whole window see-through by a fixed amount (1 = solid). Works for the solid-window fallback too.</summary>
    internal static void SetWindowAlpha(Window w, double alpha)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | 0x00080000 /* WS_EX_LAYERED */);
        SetLayeredWindowAttributes(h, 0, (byte)Math.Round(255 * Math.Clamp(alpha, 0.2, 1.0)), 0x2 /* LWA_ALPHA */);
    }

    /// <summary>Re-asserts topmost order without moving, resizing or activating.</summary>
    internal static void BringToTop(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h != IntPtr.Zero) SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    /// <summary>For the invisible strip that reserves the dock's space: no focus, hidden from Alt+Tab, mouse clicks fall through to what is below.</summary>
    internal static void SetClickThrough(Window w)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        var ex = GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | 0x00000020 /* WS_EX_TRANSPARENT */;
        SetWindowLong(h, GWL_EXSTYLE, ex);
    }

    /// <summary>Hides from Alt+Tab and (optionally) prevents the window from taking activation on click.</summary>
    internal static void SetOverlayStyle(Window w, bool noActivate)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        var ex = GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        ex = noActivate ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
        SetWindowLong(h, GWL_EXSTYLE, ex);
    }
}
