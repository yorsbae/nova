using Nova.Core.Configuration;
using Nova.Core.Localization;
using Xunit;

namespace Nova.Core.Tests;

public class LocTests
{
    [Fact]
    public void Indonesian_is_the_source_text()
    {
        Loc.Set("id");
        Assert.Equal("Tutup", Loc.T("Tutup"));
    }

    [Fact]
    public void English_translates_and_unknown_text_falls_back()
    {
        try
        {
            Loc.Set("en");
            Assert.Equal("Close", Loc.T("Tutup"));
            Assert.Equal("belum ada terjemahan", Loc.T("belum ada terjemahan"));
            Assert.Equal("Profile “Work” saved. Press Apply to keep it permanently.", Loc.F("Profil “{0}” tersimpan. Tekan Terapkan agar permanen.", "Work"));
        }
        finally { Loc.Set("id"); }
    }

    [Fact]
    public void Unknown_language_means_Indonesian()
    {
        Loc.Set("fr");
        Assert.Equal("id", Loc.Current);
    }

    [Fact]
    public void Validator_resets_an_unknown_language()
    {
        var s = SettingsValidator.Normalize(new NovaSettings { Language = "xx" });
        Assert.Equal("id", s.Language);
    }
}
