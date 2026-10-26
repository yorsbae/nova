namespace Nova.Core.Reminders;

/// <summary>When to remind the user to drink: every N minutes, but only between two hours of the day.</summary>
public static class HydrationSchedule
{
    public static bool InWindow(DateTime t, int fromHour, int toHour)
    {
        if (fromHour == toHour) return true;   // no window: all day
        double h = t.TimeOfDay.TotalHours;
        return fromHour < toHour ? h >= fromHour && h < toHour : h >= fromHour || h < toHour;
    }

    /// <summary>The next reminder after <paramref name="from"/>: <paramref name="intervalMinutes"/> later, or the start of the next window if that falls outside it.</summary>
    public static DateTime NextAfter(DateTime from, int intervalMinutes, int fromHour, int toHour)
    {
        intervalMinutes = Math.Clamp(intervalMinutes, 5, 720);
        var t = from.AddMinutes(intervalMinutes);
        if (InWindow(t, fromHour, toHour)) return t;
        var start = t.Date.AddHours(fromHour);
        return start > t ? start : start.AddDays(1);
    }
}
