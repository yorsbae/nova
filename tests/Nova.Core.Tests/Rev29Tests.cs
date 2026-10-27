using Nova.Core.Configuration;
using Xunit;

namespace Nova.Core.Tests;

public class EyeStyleMigrationTests
{
    static NovaSettings LoadJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "nova-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            return new SettingsStore(path).Load().Settings;
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Theory]
    [InlineData("AnimeA")]
    [InlineData("AnimeB")]
    [InlineData("AnimeC")]
    public void Removed_anime_styles_load_as_AnimeAura(string old)
        => Assert.Equal(IslandFaceStyle.AnimeAura, LoadJson($"{{\"IslandFaceStyle\":\"{old}\"}}").IslandFaceStyle);

    [Fact]
    public void Old_style_does_not_reset_the_other_settings()
    {
        var s = LoadJson("{\"IslandFaceStyle\":\"AnimeB\",\"ClockFontSize\":22}");
        Assert.Equal(22, s.ClockFontSize);
    }

    [Fact]
    public void Unknown_style_falls_back_to_Pet()
        => Assert.Equal(IslandFaceStyle.Pet, LoadJson("{\"IslandFaceStyle\":\"Nope\"}").IslandFaceStyle);

    [Fact]
    public void Current_styles_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), "nova-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new NovaSettings { IslandFaceStyle = IslandFaceStyle.Sharingan });
            Assert.Equal(IslandFaceStyle.Sharingan, store.Load().Settings.IslandFaceStyle);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
