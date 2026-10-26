using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Nova.Core.Configuration;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

public sealed class HotkeyService : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const int Id = 0x4E4F;           // "NO"
    const uint MOD_NOREPEAT = 0x4000;

    readonly Window _host;
    readonly Action _onPressed;
    readonly int _id;
    HwndSource? _source;
    bool _registered;

    /// <param name="id">Each shortcut of the app needs its own id.</param>
    public HotkeyService(Window host, Action onPressed, int id = Id) { _host = host; _onPressed = onPressed; _id = id; }

    /// <summary>Maps text such as "N", "F12" or "5" to a WPF Key. Rejects raw numbers and unknown names.</summary>
    public static bool TryResolveKey(string text, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var name = text.Length == 1 && char.IsDigit(text[0]) ? "D" + text : text;
        if (name.All(char.IsDigit)) return false;
        return Enum.TryParse(name, true, out key) && Enum.IsDefined(key) && key != Key.None;
    }

    /// <summary>Returns false (and logs) if the gesture is invalid or already taken by another app.</summary>
    public bool Register(string gestureText)
    {
        Unregister();
        if (!HotkeyParser.TryParse(gestureText, out var g)) return false;
        if (!TryResolveKey(g.Key, out var key)) { FileLog.Write($"Hotkey key '{g.Key}' not recognised."); return false; }

        var handle = new WindowInteropHelper(_host).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(Hook);
        _registered = NativeMethods.RegisterHotKey(handle, _id, (uint)g.Modifiers | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!_registered) FileLog.Write($"Hotkey '{gestureText}' could not be registered (likely in use).");
        return _registered;
    }

    void Unregister()
    {
        var handle = new WindowInteropHelper(_host).Handle;
        if (_registered && handle != IntPtr.Zero) NativeMethods.UnregisterHotKey(handle, _id);
        _source?.RemoveHook(Hook);
        _source = null; _registered = false;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id) { _onPressed(); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
