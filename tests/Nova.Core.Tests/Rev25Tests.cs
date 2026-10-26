using Nova.Core.Clipboard;
using Nova.Core.Configuration;
using Nova.Core.Data;
using Xunit;

namespace Nova.Core.Tests;

public class ClipboardEntryTests
{
    static byte[] Png(byte seed) => new byte[] { 0x89, 0x50, 0x4E, 0x47, seed };

    [Fact]
    public void Images_are_kept_with_their_size_and_a_repeat_moves_to_the_top()
    {
        var h = new ClipboardHistory(10);
        Assert.True(h.AddImage(Png(1), 800, 600));
        Assert.True(h.AddImage(Png(2), 10, 10));
        Assert.True(h.AddImage(Png(1), 800, 600));

        Assert.Equal(2, h.Entries.Count);
        Assert.Equal(ClipKind.Image, h.Entries[0].Kind);
        Assert.Equal((800, 600), (h.Entries[0].Width, h.Entries[0].Height));
        Assert.Empty(h.Items);   // the text list stays text only
    }

    [Fact]
    public void Empty_or_oversized_images_are_ignored()
    {
        var h = new ClipboardHistory();
        Assert.False(h.AddImage(null, 10, 10));
        Assert.False(h.AddImage(Array.Empty<byte>(), 10, 10));
        Assert.False(h.AddImage(Png(1), 0, 10));
        Assert.False(h.AddImage(new byte[ClipboardHistory.MaxImageBytes + 1], 10, 10));
        Assert.Empty(h.Entries);
    }

    [Fact]
    public void Only_the_newest_few_images_are_kept()
    {
        var h = new ClipboardHistory(100);
        for (byte i = 0; i < ClipboardHistory.MaxImages + 3; i++) h.AddImage(Png(i), 10, 10);
        h.Add("some text");

        Assert.Equal(ClipboardHistory.MaxImages, h.Entries.Count(e => e.Kind == ClipKind.Image));
        Assert.Equal(new[] { "some text" }, h.Items);
        Assert.Equal(Png((byte)(ClipboardHistory.MaxImages + 2)), h.Entries[1].Png);   // newest picture first
    }

    [Fact]
    public void Copied_files_are_kept_as_a_list_of_paths()
    {
        var h = new ClipboardHistory();
        Assert.True(h.AddFiles(new[] { @"C:\work\one.txt", @"C:\work\two.png" }));
        var e = h.Entries[0];

        Assert.Equal(ClipKind.Files, e.Kind);
        Assert.Equal(new[] { @"C:\work\one.txt", @"C:\work\two.png" }, e.Paths);
        Assert.Equal("2 file: one.txt, two.png", ClipboardHistory.Describe(e));
    }

    [Fact]
    public void A_single_file_is_labelled_with_its_name_and_blank_lists_are_ignored()
    {
        var h = new ClipboardHistory();
        Assert.False(h.AddFiles(null));
        Assert.False(h.AddFiles(new[] { "", "  " }));
        h.AddFiles(new[] { @"C:\work\notes\" });
        Assert.Equal("File: notes", ClipboardHistory.Describe(h.Entries[0]));
    }

    [Fact]
    public void Describe_names_pictures_and_previews_text()
    {
        var h = new ClipboardHistory();
        h.AddImage(Png(1), 640, 480);
        h.Add("a\n b");
        Assert.Equal("a b", ClipboardHistory.Describe(h.Entries[0]));
        Assert.Equal("Gambar 640×480", ClipboardHistory.Describe(h.Entries[1]));
    }

    [Fact]
    public void Remove_deletes_one_entry_and_says_so_once()
    {
        var h = new ClipboardHistory();
        h.Add("a"); h.Add("b");
        int changed = 0; h.Changed += () => changed++;
        var b = h.Entries[0];

        Assert.True(h.Remove(b));
        Assert.False(h.Remove(b));
        Assert.Equal(new[] { "a" }, h.Items);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void The_overall_limit_counts_every_kind_of_entry()
    {
        var h = new ClipboardHistory(2);
        h.Add("old"); h.AddImage(Png(1), 5, 5); h.AddFiles(new[] { @"C:\a.txt" });
        Assert.Equal(2, h.Entries.Count);
        Assert.Equal(new[] { ClipKind.Files, ClipKind.Image }, h.Entries.Select(e => e.Kind));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void Clear_wipes_pictures_and_files_too()
    {
        var h = new ClipboardHistory();
        h.Add("t"); h.AddImage(Png(1), 5, 5); h.AddFiles(new[] { @"C:\a.txt" });
        h.Clear();
        Assert.Empty(h.Entries);
    }
}

public class NoteDetailTests
{
    [Fact]
    public void A_note_can_be_much_longer_than_a_task_title()
    {
        var d = new UserData();
        Assert.True(d.AddNote(new string('x', 900)));
        Assert.Equal(900, d.Notes[0].Text.Length);
        Assert.True(d.AddNote(new string('y', UserData.MaxNoteLength + 50)));
        Assert.Equal(UserData.MaxNoteLength, d.Notes[0].Text.Length);
    }

    [Fact]
    public void Editing_a_note_replaces_its_text_and_keeps_its_date()
    {
        var d = new UserData();
        d.AddNote("first");
        var note = d.Notes[0];
        var created = note.CreatedUtc;

        Assert.True(d.UpdateNote(note, "  first, but longer\nwith a second line "));
        Assert.Equal("first, but longer\nwith a second line", note.Text);
        Assert.Equal(created, note.CreatedUtc);
    }

    [Fact]
    public void Editing_refuses_empty_text_and_notes_that_are_not_in_the_list()
    {
        var d = new UserData();
        d.AddNote("keep me");
        var note = d.Notes[0];

        Assert.False(d.UpdateNote(note, "   "));
        Assert.False(d.UpdateNote(null, "text"));
        Assert.False(d.UpdateNote(new NoteItem { Text = "stranger" }, "text"));
        Assert.Equal("keep me", note.Text);
    }

    [Fact]
    public void Long_notes_survive_loading_from_disk()
    {
        var d = new UserData();
        d.Notes.Add(new NoteItem { Text = new string('z', 700) });
        Assert.Equal(700, d.Normalize().Notes[0].Text.Length);
    }
}

public class IslandPlacementSettingsTests
{
    [Fact]
    public void The_Island_starts_solid_and_centred()
    {
        var s = new NovaSettings();
        Assert.Equal(1.0, s.IslandOpacity);
        Assert.Equal(0, s.IslandOffsetX);
    }

    [Theory]
    [InlineData(0.01, 0.2)]
    [InlineData(0.5, 0.5)]
    [InlineData(7, 1.0)]
    [InlineData(double.NaN, 1.0)]
    public void Island_transparency_is_kept_in_range(double given, double expected)
    {
        var s = new NovaSettings { IslandOpacity = given };
        SettingsValidator.Normalize(s);
        Assert.Equal(expected, s.IslandOpacity, 6);
    }

    [Fact]
    public void A_dragged_position_is_kept_but_never_absurd()
    {
        var s = new NovaSettings { IslandOffsetX = -350, IslandTopOffset = 640 };
        SettingsValidator.Normalize(s);
        Assert.Equal(-350, s.IslandOffsetX);
        Assert.Equal(640, s.IslandTopOffset);

        var wild = new NovaSettings { IslandOffsetX = 1e9, IslandTopOffset = -5 };
        SettingsValidator.Normalize(wild);
        Assert.Equal(4000, wild.IslandOffsetX);
        Assert.Equal(0, wild.IslandTopOffset);
    }
}
