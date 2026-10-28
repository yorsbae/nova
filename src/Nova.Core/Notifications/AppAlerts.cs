using Nova.Core.Localization;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nova.Core.Notifications;

/// <summary>A chat app NOVA can announce on the Island. <see cref="Accent"/> is the colour of its round badge.</summary>
public sealed record MessengerApp(string Name, string Accent, string[] ProcessHints, string[] TitleKeywords);

/// <summary>
/// Recognises chat apps from a process name or a window title, and reads the unread counter that chat apps and
/// their web versions put in front of the title, e.g. "(3) WhatsApp". Windows does not let a normal desktop app read the
/// text of other apps' notifications, so this is the honest, local-only signal: "WhatsApp has something new".
/// </summary>
public static class AppAlerts
{
    public static readonly IReadOnlyList<MessengerApp> Messengers = new[]
    {
        new MessengerApp("WhatsApp", "#25D366", new[] { "whatsapp" }, new[] { "WhatsApp" }),
        new MessengerApp("Telegram", "#229ED9", new[] { "telegram" }, new[] { "Telegram" }),
        new MessengerApp("Discord", "#5865F2", new[] { "discord" }, new[] { "Discord" }),
        new MessengerApp("Slack", "#4A154B", new[] { "slack" }, new[] { "Slack" }),
        new MessengerApp("Teams", "#6264A7", new[] { "ms-teams", "teams" }, new[] { "Microsoft Teams" }),
        new MessengerApp("Signal", "#3A76F0", new[] { "signal" }, new[] { "Signal" }),
        new MessengerApp("Messenger", "#0A7CFF", new[] { "messenger" }, new[] { "Messenger" }),
        new MessengerApp("LINE", "#06C755", new[] { "line" }, new[] { "LINE" }),
    };

    static readonly Regex CountPrefix = new(@"^\s*[\(\[]\s*(\d{1,4})\s*\+?\s*[\)\]]\s*", RegexOptions.Compiled);

    /// <summary>Unread number from a title such as "(3) WhatsApp - Brave". null when the title carries no counter.</summary>
    public static int? UnreadFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return null;
        var m = CountPrefix.Match(title);
        if (!m.Success) return null;
        return int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    /// <summary>The title without its "(3) " counter.</summary>
    public static string StripCount(string? title) => CountPrefix.Replace(title ?? "", "").Trim();

    /// <summary>Which chat app a window belongs to, judged by its program name first (desktop apps), then by how its title starts (web versions).</summary>
    public static MessengerApp? Identify(string? processName, string? title)
    {
        var proc = (processName ?? "").Trim();
        if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) proc = proc[..^4];
        if (proc.Length > 0)
        {
            foreach (var app in Messengers)
                foreach (var hint in app.ProcessHints)
                    if (hint.Length >= 5 ? proc.Contains(hint, StringComparison.OrdinalIgnoreCase)
                                          : string.Equals(proc, hint, StringComparison.OrdinalIgnoreCase))
                        return app;
        }
        var t = StripCount(title);
        if (t.Length == 0) return null;
        foreach (var app in Messengers)
            foreach (var key in app.TitleKeywords)
                if (t.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return app;
        return null;
    }

    /// <summary>Short text for the Island, e.g. "WhatsApp · 3 pesan belum dibaca".</summary>
    public static string Describe(MessengerApp app, int? unread)
        => unread is int n && n > 0 ? $"{app.Name} · {n} belum dibaca" : $"{app.Name} · ada pesan baru";

    /// <summary>Island text for a real toast: "WhatsApp · Sender: message", cut so it fits one notch line.</summary>
    public static string DescribeToast(string? appName, string? title, string? body, int max = 90)
    {
        var name = string.IsNullOrWhiteSpace(appName) ? Loc.T("Notifikasi") : appName.Trim();
        var t = (title ?? "").Trim();
        var b = (body ?? "").Trim().Replace('\n', ' ').Replace('\r', ' ');
        var line = t.Length > 0 && b.Length > 0 ? $"{t}: {b}" : t.Length > 0 ? t : b;
        var text = line.Length > 0 ? $"{name} · {line}" : $"{name} · {Loc.T("ada notifikasi baru")}";
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }
}
