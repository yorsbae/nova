using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nova.Core.Data;

public sealed class NoteItem
{
    public string Text { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PlanTask
{
    public string Title { get; set; } = "";
    /// <summary>Free text such as "07:00 - 16:15" or "19:00". Empty = no time.</summary>
    public string Time { get; set; } = "";
    /// <summary>0 = normal, 1 = high.</summary>
    public int Priority { get; set; }
    public bool Done { get; set; }
    /// <summary>The day this task is for (time of day ignored). Null = just "today's list", as before.</summary>
    public DateTime? Due { get; set; }
    /// <summary>true = the task also rings (alarm) at its time on its day. Task and alarm are one thing.</summary>
    public bool Alarm { get; set; }
    /// <summary>true once the alarm of this task has rung.</summary>
    public bool Rang { get; set; }

    /// <summary>The first "HH:mm" in <see cref="Time"/> ("07:00 - 16:15" gives 07:00).</summary>
    public bool TryStart(out TimeSpan t)
    {
        t = default;
        var first = (Time ?? "").Split(new[] { ' ', '-', '–' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return TimeSpan.TryParse(first, System.Globalization.CultureInfo.InvariantCulture, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);
    }
}

/// <summary>An alarm: a time of day, optionally repeating on some weekdays (no days = rings once, then switches itself off).</summary>
public sealed class AlarmItem
{
    /// <summary>"HH:mm".</summary>
    public string Time { get; set; } = "06:00";
    public string Label { get; set; } = "";
    /// <summary>Bit 0 = Sunday ... bit 6 = Saturday. 0 = once.</summary>
    public int Days { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>The day this alarm last rang, so it rings once per day.</summary>
    public DateTime? LastRang { get; set; }

    public bool TryTime(out TimeSpan t)
        => TimeSpan.TryParseExact(Time, "hh\\:mm", System.Globalization.CultureInfo.InvariantCulture, out t) && t < TimeSpan.FromDays(1);

    public bool RepeatsOn(DayOfWeek d) => (Days & (1 << (int)d)) != 0;

    /// <summary>"Sekali", "Setiap hari", "Sen-Jum" style summary for the list.</summary>
    public string DaysText()
    {
        if (Days == 0) return "Sekali";
        if (Days == 0x7F) return "Setiap hari";
        if (Days == 0x3E) return "Senin–Jumat";
        var names = new[] { "Min", "Sen", "Sel", "Rab", "Kam", "Jum", "Sab" };
        var order = new[] { 1, 2, 3, 4, 5, 6, 0 };
        return string.Join(" ", order.Where(i => (Days & (1 << i)) != 0).Select(i => names[i]));
    }
}

public sealed class CalendarEvent
{
    public string Title { get; set; } = "";
    public DateTime Start { get; set; } = DateTime.Today;
    /// <summary>Minutes before the start at which the reminder shows. 0 = no reminder.</summary>
    public int ReminderMinutes { get; set; } = 10;
    [JsonIgnore] public bool Reminded { get; set; }
}

/// <summary>Notes, plan and events. Local only: stored in one small JSON file, never sent anywhere.</summary>
public sealed class UserData
{
    public const int MaxNotes = 50, MaxTasks = 50, MaxEvents = 300, MaxTextLength = 300, MaxNoteLength = 1000;

    public List<NoteItem> Notes { get; set; } = new();
    public List<PlanTask> Tasks { get; set; } = new();
    public List<CalendarEvent> Events { get; set; } = new();
    public List<AlarmItem> Alarms { get; set; } = new();
    /// <summary>Glasses of water drunk per day, key "yyyy-MM-dd". Only the last 60 days are kept.</summary>
    public Dictionary<string, int> Water { get; set; } = new();

    public const int MaxAlarms = 20;

    public static string Clean(string? t, int max = MaxTextLength)
    {
        if (string.IsNullOrWhiteSpace(t)) return "";
        var s = new string(t.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        return s.Length > max ? s[..max] : s;
    }

    public bool AddNote(string? text)
    {
        var t = Clean(text, MaxNoteLength);
        if (t.Length == 0 || Notes.Count >= MaxNotes) return false;
        Notes.Insert(0, new NoteItem { Text = t });
        return true;
    }

    /// <summary>Replaces the text of a note (from the detail view). An empty result is refused; use <c>Notes.Remove</c> to delete.</summary>
    public bool UpdateNote(NoteItem? note, string? text)
    {
        var t = Clean(text, MaxNoteLength);
        if (note is null || t.Length == 0 || !Notes.Contains(note)) return false;
        note.Text = t;
        return true;
    }

    public bool AddTask(string? title, string? time, int priority = 0)
    {
        var t = Clean(title, 100);
        if (t.Length == 0 || Tasks.Count >= MaxTasks) return false;
        Tasks.Add(new PlanTask { Title = t, Time = Clean(time, 20), Priority = priority > 0 ? 1 : 0 });
        return true;
    }

    public bool AddEvent(string? title, DateTime start, int reminderMinutes = 10)
    {
        var t = Clean(title, 100);
        if (t.Length == 0 || Events.Count >= MaxEvents) return false;
        Events.Add(new CalendarEvent { Title = t, Start = start, ReminderMinutes = Math.Min(1440, Math.Max(0, reminderMinutes)) });
        Events.Sort((a, b) => a.Start.CompareTo(b.Start));
        return true;
    }

    public bool AddTask(string? title, string? time, DateTime due, int priority = 0, bool alarm = false)
    {
        if (!AddTask(title, time, priority)) return false;
        var t = Tasks[^1];
        t.Due = due.Date;
        t.Alarm = alarm && t.TryStart(out _);   // an alarm needs a time
        return true;
    }

    /// <summary>Tasks for one calendar day.</summary>
    public IEnumerable<PlanTask> TasksOn(DateTime day) => Tasks.Where(t => t.Due?.Date == day.Date);

    /// <summary>The "today" list: tasks without a date, tasks due today, and older tasks still not done.</summary>
    public IEnumerable<PlanTask> TodayTasks(DateTime today)
        => Tasks.Where(t => t.Due is null || t.Due.Value.Date == today.Date || (t.Due.Value.Date < today.Date && !t.Done));

    /// <summary>Done tasks / today's tasks, as 0..1 (0 when there are none).</summary>
    public double PlanProgress() => PlanProgress(DateTime.Today);

    public double PlanProgress(DateTime today)
    {
        var list = TodayTasks(today).ToList();
        return list.Count == 0 ? 0 : (double)list.Count(t => t.Done) / list.Count;
    }

    // ---- alarms ----

    public bool AddAlarm(string? time, string? label, int days)
    {
        if (Alarms.Count >= MaxAlarms) return false;
        if (!TimeSpan.TryParse((time ?? "").Trim(), System.Globalization.CultureInfo.InvariantCulture, out var t) || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1)) return false;
        var a = new AlarmItem { Time = $"{t.Hours:00}:{t.Minutes:00}", Label = Clean(label, 40), Days = days & 0x7F };
        Alarms.Add(a);
        Alarms.Sort((x, y) => string.CompareOrdinal(x.Time, y.Time));
        return true;
    }

    /// <summary>
    /// Alarms that should ring now: enabled, today is one of their days (or they are one-shot), their time has come (within the last
    /// 2 minutes), and they have not rung today. Marks them as rung; one-shot alarms switch themselves off.
    /// </summary>
    public List<AlarmItem> TakeDueAlarms(DateTime now)
    {
        var due = new List<AlarmItem>();
        foreach (var a in Alarms)
        {
            if (!a.Enabled || !a.TryTime(out var t)) continue;
            if (a.Days != 0 && !a.RepeatsOn(now.DayOfWeek)) continue;
            if (a.LastRang?.Date == now.Date) continue;
            var at = now.Date + t;
            if (now < at || now > at.AddMinutes(2)) continue;
            a.LastRang = now.Date;
            if (a.Days == 0) a.Enabled = false;
            due.Add(a);
        }
        return due;
    }

    /// <summary>True while at least one task still has an alarm waiting (so the timer knows it must run).</summary>
    public bool HasPendingTaskAlarm => Tasks.Any(t => t.Alarm && !t.Done && !t.Rang);

    /// <summary>Tasks whose alarm should ring now: alarm on, not done, not rung, due today and the time came (within the last 2 minutes). Marks them as rung.</summary>
    public List<PlanTask> TakeDueTaskAlarms(DateTime now)
    {
        var due = new List<PlanTask>();
        foreach (var t in Tasks)
        {
            if (!t.Alarm || t.Done || t.Rang || t.Due?.Date != now.Date || !t.TryStart(out var ts)) continue;
            var at = now.Date + ts;
            if (now < at) continue;
            t.Rang = true;                        // also marks missed ones (PC was off) so they never ring late
            if (now <= at.AddMinutes(2)) due.Add(t);
        }
        return due;
    }

    // ---- water ----

    static string DayKey(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public int WaterOn(DateTime day) => Water.TryGetValue(DayKey(day), out var n) ? n : 0;

    /// <summary>Adds (or, with a negative number, removes) glasses for a day; never below 0 or above 99. Returns the new count.</summary>
    public int AddWater(DateTime day, int glasses)
    {
        var n = Math.Clamp(WaterOn(day) + glasses, 0, 99);
        Water[DayKey(day)] = n;
        if (Water.Count > 60)
            foreach (var old in Water.Keys.OrderBy(k => k, StringComparer.Ordinal).Take(Water.Count - 60).ToList()) Water.Remove(old);
        return n;
    }

    public IEnumerable<CalendarEvent> EventsOn(DateTime day)
        => Events.Where(e => e.Start.Date == day.Date).OrderBy(e => e.Start);

    /// <summary>Events whose reminder time has arrived (and have not started more than 5 minutes ago). Marks them as reminded.</summary>
    public List<CalendarEvent> TakeDueReminders(DateTime now)
    {
        var due = new List<CalendarEvent>();
        foreach (var e in Events)
        {
            if (e.Reminded || e.ReminderMinutes <= 0) continue;
            if (now >= e.Start.AddMinutes(-e.ReminderMinutes) && now <= e.Start.AddMinutes(5))
            {
                e.Reminded = true;
                due.Add(e);
            }
        }
        return due;
    }

    /// <summary>Repairs data read from disk: drops empty items, trims text and enforces the limits.</summary>
    public UserData Normalize()
    {
        Notes = (Notes ?? new()).Where(n => n is not null && Clean(n.Text, MaxNoteLength).Length > 0)
            .Select(n => { n.Text = Clean(n.Text, MaxNoteLength); return n; }).Take(MaxNotes).ToList();
        Tasks = (Tasks ?? new()).Where(t => t is not null && Clean(t.Title, 100).Length > 0)
            .Select(t => { t.Title = Clean(t.Title, 100); t.Time = Clean(t.Time, 20); t.Priority = t.Priority > 0 ? 1 : 0; return t; })
            .Take(MaxTasks).ToList();
        Alarms = (Alarms ?? new()).Where(a => a is not null && a.TryTime(out _)).Select(a => { a.Label = Clean(a.Label, 40); a.Days &= 0x7F; return a; })
            .OrderBy(a => a.Time, StringComparer.Ordinal).Take(MaxAlarms).ToList();
        Water = (Water ?? new()).Where(kv => kv.Value >= 0).ToDictionary(kv => kv.Key, kv => Math.Min(99, kv.Value));
        Events = (Events ?? new()).Where(e => e is not null && Clean(e.Title, 100).Length > 0)
            .Select(e => { e.Title = Clean(e.Title, 100); e.ReminderMinutes = Math.Min(1440, Math.Max(0, e.ReminderMinutes)); return e; })
            .OrderBy(e => e.Start).Take(MaxEvents).ToList();
        return this;
    }
}

public sealed class UserDataStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    readonly string _path;

    public UserDataStore(string? path = null) => _path = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA", "data.json");

    /// <summary>Never throws: a missing or damaged file gives empty data.</summary>
    public UserData Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UserData();
            return (JsonSerializer.Deserialize<UserData>(File.ReadAllText(_path), Options) ?? new UserData()).Normalize();
        }
        catch { return new UserData(); }
    }

    public bool Save(UserData data)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch { return false; }
    }
}
