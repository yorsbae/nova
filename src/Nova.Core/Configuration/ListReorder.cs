namespace Nova.Core.Configuration;

public static class ListReorder
{
    /// <summary>Moves the item at <paramref name="index"/> by <paramref name="delta"/>; returns its new index (unchanged if out of range).</summary>
    public static int Move<T>(IList<T> list, int index, int delta)
    {
        if (index < 0 || index >= list.Count) return index;
        var target = index + delta;
        if (target < 0 || target >= list.Count) return index;
        var item = list[index];
        list.RemoveAt(index);
        list.Insert(target, item);
        return target;
    }
}
