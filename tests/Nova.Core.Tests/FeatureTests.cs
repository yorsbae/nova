using Nova.Core.Configuration;
using Xunit;

namespace Nova.Core.Tests;

public class ListReorderTests
{
    [Fact]
    public void Moves_item_right_and_left()
    {
        var l = new List<string> { "a", "b", "c" };
        Assert.Equal(1, ListReorder.Move(l, 0, +1));
        Assert.Equal(new[] { "b", "a", "c" }, l);
        Assert.Equal(0, ListReorder.Move(l, 1, -1));
        Assert.Equal(new[] { "a", "b", "c" }, l);
    }

    [Fact]
    public void Out_of_range_moves_are_no_ops()
    {
        var l = new List<int> { 1, 2 };
        Assert.Equal(0, ListReorder.Move(l, 0, -1));
        Assert.Equal(1, ListReorder.Move(l, 1, +1));
        Assert.Equal(5, ListReorder.Move(l, 5, 1));
        Assert.Equal(new[] { 1, 2 }, l);
    }
}

public class PresetApplierTests
{
    [Fact]
    public void Minimal_changes_layout_but_preserves_user_choices()
    {
        var s = new NovaSettings { Theme = ThemeMode.Dark, Hotkey = "Ctrl+Shift+K", ReducedMotion = true, AccentColor = "#112233" };
        s.DockItems.Add(new DockItem { Name = "x", Path = @"C:\x.exe" });
        PresetApplier.Apply(s, LayoutPreset.Minimal);
        Assert.Equal(LayoutPreset.Minimal, s.Preset);
        Assert.Equal(24, s.DockIconSize);
        Assert.Equal(DockVisibility.RevealOnEdgeHover, s.DockVisibility);
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.Equal("Ctrl+Shift+K", s.Hotkey);
        Assert.True(s.ReducedMotion);
        Assert.Equal("#112233", s.AccentColor);
        Assert.Single(s.DockItems);
    }

    [Fact]
    public void Matches_detects_manual_drift_and_custom_always_matches()
    {
        var s = new NovaSettings();
        PresetApplier.Apply(s, LayoutPreset.Minimal);
        Assert.True(PresetApplier.Matches(s, LayoutPreset.Minimal));
        s.DockIconSize = 60;
        Assert.False(PresetApplier.Matches(s, LayoutPreset.Minimal));
        Assert.True(PresetApplier.Matches(s, LayoutPreset.Custom));
    }

    [Fact]
    public void Balanced_restores_default_layout()
    {
        var s = new NovaSettings { DockIconSize = 90, ClockFontSize = 60 };
        PresetApplier.Apply(s, LayoutPreset.Balanced);
        Assert.Equal(new NovaSettings().DockIconSize, s.DockIconSize);
        Assert.Equal(18, s.ClockFontSize);
    }
}

public class CloneTests
{
    [Fact]
    public void Clone_is_deep_for_dock_items()
    {
        var a = new NovaSettings();
        a.DockItems.Add(new DockItem { Name = "n", Path = @"C:\n.exe" });
        var b = a.Clone();
        b.DockItems[0].Name = "changed";
        b.DockItems.Add(new DockItem { Path = @"C:\z.exe" });
        Assert.Equal("n", a.DockItems[0].Name);
        Assert.Single(a.DockItems);
    }
}
