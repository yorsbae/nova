using System.Security.Cryptography;

namespace Nova.Core.Clipboard;

public enum ClipKind { Text, Image, Files }

/// <summary>One remembered clipboard item. Images are kept as compressed PNG bytes, never as raw bitmaps, to keep memory small.</summary>
public sealed class ClipEntry
{
    public ClipKind Kind { get; init; }
    /// <summary>The text (Text) or the file paths, one per line (Files). Empty for images.</summary>
    public string Text { get; init; } = "";
    public byte[]? Png { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public DateTime CapturedUtc { get; init; } = DateTime.UtcNow;
    /// <summary>Identity used to move an exact duplicate to the top instead of storing it twice.</summary>
    public string Key { get; init; } = "";

    public IReadOnlyList<string> Paths => Kind == ClipKind.Files
        ? Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        : Array.Empty<string>();
}

/// <summary>
/// In-memory clipboard history (text, images, copied files). Deliberately has no persistence: nothing here is ever written to disk or logged.
/// Newest first, exact duplicates move to the top, oversized entries are ignored rather than truncated.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxEntryLength = 5000;
    /// <summary>Largest compressed image kept (8 MB) and the most images held at once.</summary>
    public const int MaxImageBytes = 8 * 1024 * 1024, MaxImages = 10;
    public const int MaxFilesPerEntry = 50;

    readonly List<ClipEntry> _entries = new();
    int _max;

    public ClipboardHistory(int maxItems = 20) => _max = Math.Min(100, Math.Max(1, maxItems));

    public event Action? Changed;

    /// <summary>Everything, newest first.</summary>
    public IReadOnlyList<ClipEntry> Entries => _entries;

    /// <summary>Only the text items, newest first.</summary>
    public IReadOnlyList<string> Items => _entries.Where(e => e.Kind == ClipKind.Text).Select(e => e.Text).ToList();

    public int MaxItems
    {
        get => _max;
        set { _max = Math.Min(100, Math.Max(1, value)); if (Trim()) Changed?.Invoke(); }
    }

    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxEntryLength) return false;
        return Put(new ClipEntry { Kind = ClipKind.Text, Text = text, Key = "t:" + text });
    }

    /// <summary>Remembers an image given as PNG bytes. Ignored when empty or too big.</summary>
    public bool AddImage(byte[]? png, int width, int height)
    {
        if (png is null || png.Length == 0 || png.Length > MaxImageBytes || width <= 0 || height <= 0) return false;
        var key = "i:" + Convert.ToHexString(SHA256.HashData(png));
        return Put(new ClipEntry { Kind = ClipKind.Image, Png = png, Width = width, Height = height, Key = key });
    }

    /// <summary>Remembers copied files (their paths only, not their contents).</summary>
    public bool AddFiles(IEnumerable<string>? paths)
    {
        var list = (paths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxFilesPerEntry).ToList();
        if (list.Count == 0) return false;
        var text = string.Join('\n', list);
        if (text.Length > MaxEntryLength) return false;
        return Put(new ClipEntry { Kind = ClipKind.Files, Text = text, Key = "f:" + text });
    }

    public bool Remove(ClipEntry entry)
    {
        if (!_entries.Remove(entry)) return false;
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_entries.Count == 0) return;
        _entries.Clear();
        Changed?.Invoke();
    }

    bool Put(ClipEntry entry)
    {
        _entries.RemoveAll(e => e.Key == entry.Key);
        _entries.Insert(0, entry);
        // Images are the heavy ones: keep only the newest few, whatever the overall limit is.
        int images = 0;
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].Kind == ClipKind.Image && ++images > MaxImages) { _entries.RemoveAt(i); i--; }
        Trim();
        Changed?.Invoke();
        return true;
    }

    bool Trim()
    {
        if (_entries.Count <= _max) return false;
        _entries.RemoveRange(_max, _entries.Count - _max);
        return true;
    }

    /// <summary>Single-line preview for lists (whitespace collapsed, truncated with an ellipsis).</summary>
    public static string Preview(string text, int max = 60)
    {
        var one = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= max ? one : one[..(max - 1)] + "…";
    }

    /// <summary>One-line label for any entry: the text, "Gambar 800×600", or "3 file: a.txt, b.png…".</summary>
    public static string Describe(ClipEntry e, int max = 60) => e.Kind switch
    {
        ClipKind.Image => $"Gambar {e.Width}×{e.Height}",
        ClipKind.Files => Preview(FilesLabel(e), max),
        _ => Preview(e.Text, max)
    };

    static string FilesLabel(ClipEntry e)
    {
        // Split on both kinds of slash by hand, so the label is the same on any system.
        var names = e.Paths.Select(p =>
        {
            var trimmed = p.TrimEnd('\\', '/');
            int cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
            var name = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
            return name.Length > 0 ? name : p;
        }).ToList();
        return (names.Count == 1 ? "File: " : $"{names.Count} file: ") + string.Join(", ", names);
    }
}
