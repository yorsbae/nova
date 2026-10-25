using System.Runtime.InteropServices;

namespace Nova.App.Integrations.Windows;

/// <summary>Reads and changes the Windows taskbar's "automatically hide" state through the supported AppBar API.</summary>
public static class TaskbarService
{
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    const uint ABS_AUTOHIDE = 0x1;

    static NativeMethods.APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        hWnd = NativeMethods.FindWindow("Shell_TrayWnd", null)
    };

    /// <summary>True/false for the current auto-hide state, or null if it can't be read.</summary>
    public static bool? GetAutoHide()
    {
        try
        {
            var abd = NewData();
            var state = (uint)NativeMethods.SHAppBarMessage(ABM_GETSTATE, ref abd).ToUInt64();
            return (state & ABS_AUTOHIDE) != 0;
        }
        catch { return null; }
    }

    /// <summary>Hides/shows the taskbar window(s) on every monitor. Start menu and the Win key keep working.</summary>
    public static void SetHidden(bool hidden)
    {
        try
        {
            int cmd = hidden ? 0 /* SW_HIDE */ : 8 /* SW_SHOWNA */;
            var main = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) NativeMethods.ShowWindow(main, cmd);
            var h = IntPtr.Zero;
            while ((h = NativeMethods.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                NativeMethods.ShowWindow(h, cmd);
        }
        catch { }
    }

    public static bool SetAutoHide(bool autoHide)
    {
        try
        {
            var abd = NewData();
            var state = (uint)NativeMethods.SHAppBarMessage(ABM_GETSTATE, ref abd).ToUInt64();
            state = autoHide ? state | ABS_AUTOHIDE : state & ~ABS_AUTOHIDE;
            abd.lParam = (IntPtr)(long)state;
            NativeMethods.SHAppBarMessage(ABM_SETSTATE, ref abd);
            return true;
        }
        catch { return false; }
    }
}
