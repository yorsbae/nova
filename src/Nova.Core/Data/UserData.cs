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
    public const int MaxNotes = 50, MaxTasks = 50, MaxEvents = 300, MaxTextLength = 300;

    public List<NoteItem> Notes { get; set; } = new();
    public List<PlanTask> Tasks { get; set; } = new();
    public List<CalendarEvent> Events { get; set; } = new();

    public static string Clean(string? t, int max = MaxTextLength)
    {
        if (string.IsNullOrWhiteSpace(t)) return "";
        var s = new string(t.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        return s.Length > max ? s[..max] : s;
    }

    public bool AddNote(string? text)
    {
        var t = Clean(text);
        if (t.Length == 0 || Notes.Count >= MaxNotes) return false;
        Notes.Insert(0, new NoteItem { Text = t });
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

    /// <summary>Done tasks / all tasks, as 0..1 (0 when there are none).</summary>
    public double PlanProgress() => Tasks.Count == 0 ? 0 : (double)Tasks.Count(t => t.Done) / Tasks.Count;

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
        Notes = (Notes ?? new()).Where(n => n is not null && Clean(n.Text).Length > 0)
            .Select(n => { n.Text = Clean(n.Text); return n; }).Take(MaxNotes).ToList();
        Tasks = (Tasks ?? new()).Where(t => t is not null && Clean(t.Title, 100).Length > 0)
            .Select(t => { t.Title = Clean(t.Title, 100); t.Time = Clean(t.Time, 20); t.Priority = t.Priority > 0 ? 1 : 0; return t; })
            .Take(MaxTasks).ToList();
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
