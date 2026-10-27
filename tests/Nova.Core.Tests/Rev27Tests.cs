using Nova.Core.Configuration;
using Xunit;

namespace Nova.Core.Tests;

public class ToggleHotkeyTests
{
    [Fact]
    public void Default_is_ctrl_alt_h()
        => Assert.Equal("Ctrl+Alt+H", new NovaSettings().ToggleHotkey);

    [Fact]
    public void Valid_shortcut_is_kept()
    {
        var s = new NovaSettings { ToggleHotkey = "Ctrl+Shift+J" };
        SettingsValidator.Normalize(s);
        Assert.Equal("Ctrl+Shift+J", s.ToggleHotkey);
    }

    [Fact]
    public void Empty_means_off_and_stays_empty()
    {
        var s = new NovaSettings { ToggleHotkey = "   " };
        SettingsValidator.Normalize(s);
        Assert.Equal("", s.ToggleHotkey);
    }

    [Fact]
    public void Invalid_shortcut_falls_back_to_default()
    {
        var s = new NovaSettings { ToggleHotkey = "banana" };
        SettingsValidator.Normalize(s);
        Assert.Equal("Ctrl+Alt+H", s.ToggleHotkey);
    }

    [Fact]
    public void Same_as_main_shortcut_falls_back_to_default()
    {
        var s = new NovaSettings { Hotkey = "Ctrl+Alt+K", ToggleHotkey = "ctrl+alt+k" };
        SettingsValidator.Normalize(s);
        Assert.Equal("Ctrl+Alt+H", s.ToggleHotkey);
    }

    [Fact]
    public void Default_taken_by_main_shortcut_switches_toggle_off()
    {
        var s = new NovaSettings { Hotkey = "Ctrl+Alt+H", ToggleHotkey = "Ctrl+Alt+H" };
        SettingsValidator.Normalize(s);
        Assert.Equal("", s.ToggleHotkey);
    }

    [Fact]
    public void New_eye_styles_exist_and_survive_validation()
    {
        foreach (var style in new[] { IslandFaceStyle.AnimeAura, IslandFaceStyle.Yandere, IslandFaceStyle.Demon, IslandFaceStyle.Hollow })
        {
            var s = new NovaSettings { IslandFaceStyle = style };
            SettingsValidator.Normalize(s);
            Assert.Equal(style, s.IslandFaceStyle);
        }
    }
}
