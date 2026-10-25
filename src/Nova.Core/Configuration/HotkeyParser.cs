namespace Nova.Core.Configuration;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 } // matches Win32 MOD_*

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key);

public static class HotkeyParser
{
    /// <summary>Parses strings like "Ctrl+Alt+N". Requires at least one modifier and exactly one key.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) return false;
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Control; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "windows": mods |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null) return false;
                    key = raw;
                    break;
            }
        }
        if (key is null || mods == HotkeyModifiers.None) return false;
        gesture = new HotkeyGesture(mods, key);
        return true;
    }
}
