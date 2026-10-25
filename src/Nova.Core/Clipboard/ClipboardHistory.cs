namespace Nova.Core.Clipboard;

/// <summary>
/// In-memory, text-only clipboard history. Deliberately has no persistence: nothing here is ever written to disk or logged.
/// Newest first, exact duplicates move to the top, oversized entries are ignored rather than truncated.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxEntryLength = 5000;
    readonly List<string> _items = new();
    int _max;

    public ClipboardHistory(int maxItems = 20) => _max = Math.Min(100, Math.Max(1, maxItems));

    public event Action? Changed;
    public IReadOnlyList<string> Items => _items;

    public int MaxItems
    {
        get => _max;
        set { _max = Math.Min(100, Math.Max(1, value)); if (Trim()) Changed?.Invoke(); }
    }

    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxEntryLength) return false;
        _items.Remove(text);
        _items.Insert(0, text);
        Trim();
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }

    bool Trim()
    {
        if (_items.Count <= _max) return false;
        _items.RemoveRange(_max, _items.Count - _max);
        return true;
    }

    /// <summary>Single-line preview for lists (whitespace collapsed, truncated with an ellipsis).</summary>
    public static string Preview(string text, int max = 60)
    {
        var one = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= max ? one : one[..(max - 1)] + "…";
    }
}
