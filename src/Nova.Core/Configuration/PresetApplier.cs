namespace Nova.Core.Configuration;

/// <summary>
/// Presets only touch layout/size fields. They never change the user's theme, accent, hotkey,
/// dock shortcuts, or accessibility settings (reduced motion, contrast, notices).
/// </summary>
public static class PresetApplier
{
    static NovaSettings Template(LayoutPreset p)
    {
        var t = new NovaSettings();
        if (p == LayoutPreset.Minimal)
        {
            t.ClockFontSize = 14;
            t.DockIconSize = 32;
            t.DockSpacing = 4;
            t.Opacity = 0.9;
            t.DockVisibility = DockVisibility.RevealOnEdgeHover;
            t.IslandTopOffset = 4;
            t.DockBottomOffset = 4;
            t.IslandBehavior = IslandBehavior.AlwaysCompact;
        }
        return t;
    }

    public static void Apply(NovaSettings s, LayoutPreset preset)
    {
        s.Preset = preset;
        if (preset == LayoutPreset.Custom) return;
        var t = Template(preset);
        Copy(t, s);
    }

    /// <summary>True when the layout fields still equal the preset (always true for Custom).</summary>
    public static bool Matches(NovaSettings s, LayoutPreset preset)
    {
        if (preset == LayoutPreset.Custom) return true;
        var t = Template(preset);
        return s.ClockFontSize == t.ClockFontSize && s.DockIconSize == t.DockIconSize &&
               s.DockSpacing == t.DockSpacing && Math.Abs(s.Opacity - t.Opacity) < 1e-9 &&
               s.DockVisibility == t.DockVisibility && s.IslandTopOffset == t.IslandTopOffset &&
               s.DockBottomOffset == t.DockBottomOffset && s.IslandBehavior == t.IslandBehavior;
    }

    static void Copy(NovaSettings from, NovaSettings to)
    {
        to.ClockFontSize = from.ClockFontSize;
        to.DockIconSize = from.DockIconSize;
        to.DockSpacing = from.DockSpacing;
        to.Opacity = from.Opacity;
        to.DockVisibility = from.DockVisibility;
        to.IslandTopOffset = from.IslandTopOffset;
        to.DockBottomOffset = from.DockBottomOffset;
        to.IslandBehavior = from.IslandBehavior;
    }
}
