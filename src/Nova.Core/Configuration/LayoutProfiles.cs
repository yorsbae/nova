namespace Nova.Core.Configuration;

/// <summary>Named layout profiles ("Work", "Presentation"...). Profiles never carry hotkey, dock items or accessibility settings.</summary>
public static class LayoutProfiles
{
    /// <summary>Returns the cleaned name, or null if empty/too long/invalid.</summary>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim();
        if (n.Length > SettingsValidator.MaxProfileNameLength || n.Any(char.IsControl)) return null;
        return n;
    }

    public static LayoutProfile Capture(NovaSettings s, string name) => new()
    {
        Name = name, Theme = s.Theme, AccentColor = s.AccentColor, Opacity = s.Opacity, ClockFormat = s.ClockFormat,
        ClockFontSize = s.ClockFontSize, IslandBehavior = s.IslandBehavior, IslandTopOffset = s.IslandTopOffset,
        DockVisibility = s.DockVisibility, DockBottomOffset = s.DockBottomOffset, DockIconSize = s.DockIconSize,
        DockSpacing = s.DockSpacing, IslandMonitor = s.IslandMonitor, DockMonitor = s.DockMonitor
    };

    public static void ApplyTo(NovaSettings s, LayoutProfile p)
    {
        s.Theme = p.Theme; s.AccentColor = p.AccentColor; s.Opacity = p.Opacity; s.ClockFormat = p.ClockFormat;
        s.ClockFontSize = p.ClockFontSize; s.IslandBehavior = p.IslandBehavior; s.IslandTopOffset = p.IslandTopOffset;
        s.DockVisibility = p.DockVisibility; s.DockBottomOffset = p.DockBottomOffset; s.DockIconSize = p.DockIconSize;
        s.DockSpacing = p.DockSpacing; s.IslandMonitor = p.IslandMonitor; s.DockMonitor = p.DockMonitor;
        s.Preset = LayoutPreset.Custom;
    }

    /// <summary>Saves the current layout under a name (replacing a profile with the same name, case-insensitively).</summary>
    public static bool TrySave(NovaSettings s, string? name, out string? error)
    {
        error = null;
        var clean = CleanName(name);
        if (clean is null) { error = $"Profile names must be 1–{SettingsValidator.MaxProfileNameLength} characters."; return false; }
        var idx = s.Profiles.FindIndex(p => string.Equals(p.Name, clean, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 && s.Profiles.Count >= SettingsValidator.MaxProfiles)
        { error = $"You can keep at most {SettingsValidator.MaxProfiles} profiles. Delete one first."; return false; }
        var profile = Capture(s, clean);
        if (idx >= 0) s.Profiles[idx] = profile; else s.Profiles.Add(profile);
        return true;
    }

    public static bool TryApply(NovaSettings s, string? name)
    {
        var p = s.Profiles.FirstOrDefault(x => string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (p is null) return false;
        ApplyTo(s, p);
        return true;
    }

    public static bool Delete(NovaSettings s, string? name)
        => s.Profiles.RemoveAll(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
}
