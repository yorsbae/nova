using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// What the dock's right-click menu does to other programs, the way the Windows taskbar does it: close one window, exit a program
/// (close all its windows politely, and offer to end it if it does not react), and the list of frequently used folders for Explorer.
/// Nothing here kills a program without asking.
/// </summary>
public static class AppControl
{
    const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    static readonly RunningAppsService Apps = new();

    /// <summary>Asks one window to close (like its X button). The program may still ask "save changes?".</summary>
    public static void CloseWindow(IntPtr hwnd)
    {
        try { if (hwnd != IntPtr.Zero) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
        catch (Exception ex) { FileLog.Write($"Close window failed: {ex.GetType().Name}"); }
    }

    /// <summary>All windows that belong to the same program as this one.</summary>
    public static IReadOnlyList<IntPtr> WindowsOfProgram(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return WindowsOfProcess(pid);
    }

    static List<IntPtr> WindowsOfProcess(uint pid)
    {
        var list = new List<IntPtr>();
        foreach (var h in Apps.AllWindows())
        {
            NativeMethods.GetWindowThreadProcessId(h, out var p);
            if (p == pid) list.Add(h);
        }
        return list;
    }

    /// <summary>
    /// "Exit program": closes every window of the program. If it is still open after a few seconds (it is stuck, or waiting on a dialog),
    /// <paramref name="confirmEnd"/> is asked (with the program's name) and only a "yes" ends the process.
    /// A program that is hosted in a shared frame (Store apps) is closed window by window and never ended, and neither is Explorer or NOVA.
    /// </summary>
    public static void ExitProgram(IntPtr hwnd, bool hosted, Func<string, bool> confirmEnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            var windows = hosted ? new List<IntPtr> { hwnd } : WindowsOfProcess(pid);
            if (windows.Count == 0) windows.Add(hwnd);
            foreach (var h in windows) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (hosted || pid == (uint)Environment.ProcessId) return;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (proc.HasExited || WindowsOfProcess(pid).Count == 0) return;   // it closed (a program that stays in the tray is fine)
                    var name = proc.ProcessName;
                    if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return;
                    if (confirmEnd(name)) proc.Kill();
                }
                catch (Exception ex) { FileLog.Write($"Exit program follow-up failed: {ex.GetType().Name}"); }
            };
            timer.Start();
        }
        catch (Exception ex) { FileLog.Write($"Exit program failed: {ex.GetType().Name}"); }
    }

    /// <summary>
    /// The folders you open most, for File Explorer's dock menu: first the ones Explorer's own Quick access shows (pinned, then frequent),
    /// then Windows' "Frequent places" list, and only if both are empty the usual personal folders.
    /// Call it when the menu is about to open, not while the dock is being built.
    /// </summary>
    public static IReadOnlyList<(string Name, string Path)> QuickAccessFolders()
    {
        const int Max = 10;
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? name, string? path)
        {
            if (result.Count >= Max || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !seen.Add(path)) return;
            result.Add((string.IsNullOrWhiteSpace(name) ? (Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path) : name, path));
        }

        // Quick access, then Frequent places: both are read through the Windows shell.
        foreach (var source in new[] { "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}", "shell:::{3936E9E4-D92C-4EEE-A85A-BC16D5EA0819}" })
        {
            if (result.Count >= Max) break;
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type is null) break;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic folder = shell.NameSpace(source);
                if (folder is null) continue;
                foreach (dynamic item in folder.Items())
                {
                    if (result.Count >= Max) break;
                    try { Add((string)item.Name, (string)item.Path); } catch { /* an entry that is not a folder */ }
                }
            }
            catch (Exception ex) { FileLog.Write($"Frequent folders read failed: {ex.GetType().Name}"); }
        }

        if (result.Count == 0)
        {
            Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            Add("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            Add("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            Add("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        }
        return result;
    }

    /// <summary>Opens a folder in File Explorer.</summary>
    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { FileLog.Write($"Open folder failed: {ex.GetType().Name}"); }
    }

    /// <summary>Opens Task Manager (right-click on an empty part of the dock, like the Windows taskbar).</summary>
    public static void OpenTaskManager()
    {
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
        catch (Exception ex) { FileLog.Write($"Task Manager failed: {ex.GetType().Name}"); }
    }
}
