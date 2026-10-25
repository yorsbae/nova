namespace Nova.Core.Configuration;

/// <summary>The Island's temporary shelf: a short list of file paths kept in memory only (never copied, never saved).</summary>
public sealed class FileShelf
{
    public const int MaxItems = 12;
    readonly List<string> _items = new();

    public IReadOnlyList<string> Items => _items;
    public int Count => _items.Count;

    /// <summary>Adds a path unless it is empty, a duplicate, or the shelf is full. Returns true if it was added.</summary>
    public bool Add(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _items.Count >= MaxItems) return false;
        if (_items.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return false;
        _items.Add(path);
        return true;
    }

    public bool Remove(string path)
    {
        var i = _items.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        _items.RemoveAt(i);
        return true;
    }

    public void Clear() => _items.Clear();
}
