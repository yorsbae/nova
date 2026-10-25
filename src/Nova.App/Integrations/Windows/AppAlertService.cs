using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using Nova.Core.Logging;
using Nova.Core.Notifications;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Tells NOVA when a chat app (WhatsApp, Telegram, Discord...) has something new. Windows does not let a normal desktop app read the
/// text of other apps' notifications (that needs an MSIX-packaged app), so NOVA uses two local signals instead:
/// 1) the Windows "taskbar button flashes" message that chat apps send for a new message, and
/// 2) the unread counter that chat apps and their web versions put in the window title, e.g. "(3) WhatsApp".
/// Nothing leaves this PC and no message text is read.
/// </summary>
public sealed class AppAlertService : IDisposable
{
    const int HSHELL_FLASH = 0x8006;

    readonly DispatcherTimer _titles = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly Dictionary<string, int> _unread = new();
    readonly Dictionary<string, DateTime> _lastAlert = new();
    HwndSource? _source;
    uint _shellMessage;
    bool _hooked, _primed;

    /// <summary>A chat app has something new: app, unread count (null = unknown), window to bring forward.</summary>
    public event Action<MessengerApp, int?, IntPtr>? Alert;
    /// <summary>Any window asked for attention (its taskbar button would flash): window and program path.</summary>
    public event Action<IntPtr, string?>? Flashed;

    public void Start()
    {
        if (_hooked) return;
        try
        {
            var p = new HwndSourceParameters("NOVA.ShellHook") { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80, Width = 0, Height = 0 };
            _source = new HwndSource(p);
            _source.AddHook(Hook);
            _shellMessage = NativeMethods.RegisterWindowMessage("SHELLHOOK");
            _hooked = NativeMethods.RegisterShellHookWindow(_source.Handle);
            if (!_hooked) FileLog.Write("Shell hook could not be registered; chat alerts rely on window titles only.");
        }
        catch (Exception ex) { FileLog.Write($"Alert hook failed: {ex.GetType().Name}"); }

        _titles.Tick -= OnTitleTick;
        _titles.Tick += OnTitleTick;
        _titles.Start();
    }

    public void Stop()
    {
        _titles.Stop();
        _unread.Clear();
        _primed = false;
        if (_source is not null)
        {
            try { if (_hooked) NativeMethods.DeregisterShellHookWindow(_source.Handle); } catch { }
            _source.RemoveHook(Hook);
            _source.Dispose();
            _source = null;
        }
        _hooked = false;
    }

    public void Dispose() => Stop();

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_shellMessage != 0 && (uint)msg == _shellMessage && wParam.ToInt32() == HSHELL_FLASH)
        {
            try { OnFlash(lParam); } catch (Exception ex) { FileLog.Write($"Flash handling failed: {ex.GetType().Name}"); }
        }
        return IntPtr.Zero;
    }

    void OnFlash(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window)) return;
        var path = NativeMethods.ProcessPath(window, out _);
        Flashed?.Invoke(window, path);
        if (NativeMethods.ForegroundRoot() == window) return;   // the user is looking at it already

        var title = NativeMethods.WindowText(window);
        var app = AppAlerts.Identify(path is null ? null : Path.GetFileNameWithoutExtension(path), title);
        if (app is not null) Raise(app, AppAlerts.UnreadFromTitle(title), window);
    }

    void OnTitleTick(object? sender, EventArgs e)
    {
        try
        {
            var counts = new Dictionary<string, (int Count, IntPtr Hwnd, MessengerApp App)>();
            var fg = NativeMethods.ForegroundRoot();
            NativeMethods.EnumWindows((h, _) =>
            {
                try
                {
                    if (!NativeMethods.IsWindowVisible(h) || NativeMethods.GetWindowTextLength(h) == 0) return true;
                    var title = NativeMethods.WindowText(h);
                    if (AppAlerts.UnreadFromTitle(title) is not int n) return true;
                    var app = AppAlerts.Identify(null, title);
                    if (app is null) return true;
                    if (!counts.TryGetValue(app.Name, out var cur) || n > cur.Count) counts[app.Name] = (n, h, app);
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            foreach (var key in _unread.Keys.ToList())
                if (!counts.ContainsKey(key)) _unread[key] = 0;
            foreach (var (key, c) in counts)
            {
                _unread.TryGetValue(key, out var before);
                _unread[key] = c.Count;
                if (_primed && c.Count > before && c.Hwnd != fg) Raise(c.App, c.Count, c.Hwnd);
            }
            _primed = true;   // the first look only records what was already unread
        }
        catch (Exception ex) { FileLog.Write($"Title check failed: {ex.GetType().Name}"); }
    }

    void Raise(MessengerApp app, int? unread, IntPtr window)
    {
        if (_lastAlert.TryGetValue(app.Name, out var at) && (DateTime.UtcNow - at).TotalSeconds < 4) return;   // one alert per burst
        _lastAlert[app.Name] = DateTime.UtcNow;
        Alert?.Invoke(app, unread, window);
    }
}
