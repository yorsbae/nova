using System.IO;

namespace Nova.App.Integrations.Windows;

/// <summary>One open app (all its windows collapsed into the first one found), as the dock shows it.</summary>
public sealed record RunningApp(IntPtr Hwnd, string Title, string? ExePath, bool IsHostedApp);

/// <summary>Lists the apps that have a normal, visible window (the same ones Alt+Tab shows). Read on demand; no hooks.</summary>
public sealed class RunningAppsService
{
    static readonly HashSet<string> SkipClasses = new(StringComparer.OrdinalIgnoreCase)
    { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    public IReadOnlyList<RunningApp> Snapshot()
    {
        var apps = new List<RunningApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint self = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((h, _) =>
        {
            try
            {
                if (!IsAppWindow(h)) return true;
                var path = NativeMethods.ProcessPath(h, out var pid);
                if (pid == self) return true;
                bool hosted = path is not null && string.Equals(Path.GetFileName(path), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
                var key = path is null || hosted ? "hwnd:" + h : path;
                if (!seen.Add(key)) return true;
                apps.Add(new RunningApp(h, NativeMethods.WindowText(h), hosted ? null : path, hosted || path is null));
            }
            catch { /* a window vanishing mid-enumeration is normal */ }
            return true;
        }, IntPtr.Zero);

        return apps;
    }

    /// <summary>Every app window (not one per program), top of the Z-order first. Used to restyle windows, never to launch anything.</summary>
    public IReadOnlyList<IntPtr> AllWindows()
    {
        var list = new List<IntPtr>();
        uint self = (uint)Environment.ProcessId;
        NativeMethods.EnumWindows((h, _) =>
        {
            try
            {
                if (!IsAppWindow(h)) return true;
                NativeMethods.GetWindowThreadProcessId(h, out var pid);
                if (pid != self) list.Add(h);
            }
            catch { /* a window vanishing mid-enumeration is normal */ }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>True when this window is the one the user is working in right now.</summary>
    public static bool IsForeground(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.ForegroundRoot() == hwnd;

    public static bool IsMinimized(IntPtr hwnd) => NativeMethods.IsIconic(hwnd);

    static bool IsAppWindow(IntPtr h)
    {
        if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsCloaked(h)) return false;
        if (NativeMethods.GetWindowTextLength(h) == 0) return false;
        int ex = NativeMethods.GetExStyle(h);
        if ((ex & 0x00000080) != 0) return false;   // tool window
        bool owned = NativeMethods.GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero;
        if (owned && (ex & NativeMethods.WS_EX_APPWINDOW) == 0) return false;
        return !SkipClasses.Contains(NativeMethods.ClassName(h));
    }

    public static void Activate(IntPtr hwnd) => NativeMethods.Activate(hwnd);
}
