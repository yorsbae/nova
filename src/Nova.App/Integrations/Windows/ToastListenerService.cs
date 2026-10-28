using System.Windows.Threading;
using Nova.Core.Logging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Reads the toasts Windows shows for other apps (WhatsApp, Telegram...) so the Island can show sender and text like a phone does.
/// Needs the user's "Notifications" access: Windows asks once, and it can be switched off in Settings → Privacy → Notifications.
/// If Windows refuses (or this build has no listener support) the service reports it and the title/flash signals keep working.
/// </summary>
public sealed class ToastListenerService : IDisposable
{
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly HashSet<uint> _seen = new();
    UserNotificationListener? _listener;
    bool _primed, _polling;

    /// <summary>App display name, title line, body text, notification id.</summary>
    public event Action<string, string, string, uint>? Toast;
    /// <summary>Raised once with the reason when notification access is not available.</summary>
    public event Action<string>? Unavailable;

    public async void Start()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Unavailable?.Invoke("Akses notifikasi Windows belum diizinkan (Settings → Privacy → Notifications).");
                return;
            }
            _poll.Tick -= OnTick;
            _poll.Tick += OnTick;
            _poll.Start();
            OnTick(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Toast listener unavailable: {ex.GetType().Name}");
            Unavailable?.Invoke("Pembaca notifikasi Windows tidak tersedia di PC ini.");
        }
    }

    async void OnTick(object? sender, EventArgs e)
    {
        if (_polling || _listener is null) return;
        _polling = true;
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var current = new HashSet<uint>();
            foreach (var n in list)
            {
                current.Add(n.Id);
                if (!_seen.Add(n.Id) || !_primed) continue;   // the first pass only records what is already there
                try
                {
                    var app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                    var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new();
                    var title = texts.Count > 0 ? texts[0] : "";
                    var body = texts.Count > 1 ? string.Join(" ", texts.Skip(1)) : "";
                    Toast?.Invoke(app, title, body, n.Id);
                }
                catch (Exception ex) { FileLog.Write($"Toast read failed: {ex.GetType().Name}"); }
            }
            _seen.IntersectWith(current);   // forget dismissed ones so the set cannot grow forever
            _primed = true;
        }
        catch (Exception ex) { FileLog.Write($"Toast poll failed: {ex.GetType().Name}"); }
        finally { _polling = false; }
    }

    public void Dispose()
    {
        _poll.Stop();
        _seen.Clear();
        _primed = false;
    }
}
