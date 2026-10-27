using System.IO;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Finds the program a .lnk shortcut points to. A pinned dock item is very often a shortcut ("Google Chrome.lnk"), while the running
/// window belongs to "chrome.exe"; without the target the dock cannot tell that the pinned app is already open, so it would start a second
/// copy on click, and show the open one as an extra tile. The answer is cached (a shortcut rarely changes while NOVA runs).
/// </summary>
public static class ShortcutResolver
{
    static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Gate = new();

    /// <summary>The .exe behind <paramref name="path"/>: the path itself for an .exe, the shortcut's target for a .lnk, otherwise null.</summary>
    public static string? ExeOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var ext = Path.GetExtension(path);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return path;
        if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        lock (Gate) { if (Cache.TryGetValue(path, out var hit)) return hit; }

        string? target = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is not null)
            {
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic link = shell.CreateShortcut(path);
                string t = link.TargetPath;
                if (!string.IsNullOrWhiteSpace(t) && t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) target = t;
            }
        }
        catch { /* an unreadable shortcut just falls back to name matching */ }

        lock (Gate) Cache[path] = target;
        return target;
    }
}
