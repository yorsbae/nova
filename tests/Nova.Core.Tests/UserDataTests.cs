using Nova.Core.Data;
using Xunit;

namespace Nova.Core.Tests;

public class UserDataTests
{
    [Fact]
    public void Adds_trim_and_reject_blank_and_respect_limits()
    {
        var d = new UserData();
        Assert.False(d.AddNote("   "));
        Assert.True(d.AddNote("  buy milk "));
        Assert.Equal("buy milk", d.Notes[0].Text);
        for (int i = 0; i < 100; i++) d.AddNote("n" + i);
        Assert.Equal(UserData.MaxNotes, d.Notes.Count);
        Assert.False(d.AddTask("", ""));
        Assert.True(d.AddTask("Gym", "16:30 - 17:30", priority: 5));
        Assert.Equal(1, d.Tasks[0].Priority);
    }

    [Fact]
    public void Plan_progress_counts_done_tasks()
    {
        var d = new UserData();
        Assert.Equal(0, d.PlanProgress());
        d.AddTask("a", ""); d.AddTask("b", "");
        d.Tasks[0].Done = true;
        Assert.Equal(0.5, d.PlanProgress());
    }

    [Fact]
    public void Events_are_sorted_and_filtered_by_day()
    {
        var d = new UserData();
        var day = new DateTime(2026, 10, 24);
        d.AddEvent("late", day.AddHours(19));
        d.AddEvent("early", day.AddHours(9));
        d.AddEvent("other day", day.AddDays(1));
        var today = d.EventsOn(day).Select(e => e.Title).ToList();
        Assert.Equal(new[] { "early", "late" }, today);
    }

    [Fact]
    public void Reminder_fires_once_inside_its_window()
    {
        var d = new UserData();
        var start = new DateTime(2026, 10, 24, 10, 0, 0);
        d.AddEvent("Meeting", start, reminderMinutes: 10);
        Assert.Empty(d.TakeDueReminders(start.AddMinutes(-11)));
        Assert.Single(d.TakeDueReminders(start.AddMinutes(-9)));
        Assert.Empty(d.TakeDueReminders(start.AddMinutes(-8)));        // only once
        d.AddEvent("Old", start.AddHours(-3), 10);
        Assert.Empty(d.TakeDueReminders(start));                       // long past: no late reminder
    }

    [Fact]
    public void Store_round_trips_and_survives_a_damaged_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nova-data-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserDataStore(path);
            var d = new UserData();
            d.AddNote("hello"); d.AddTask("t", "09:00"); d.AddEvent("e", new DateTime(2026, 10, 24, 8, 0, 0));
            Assert.True(store.Save(d));
            var back = store.Load();
            Assert.Single(back.Notes); Assert.Single(back.Tasks); Assert.Single(back.Events);

            File.WriteAllText(path, "{ not json");
            Assert.Empty(store.Load().Notes);
        }
        finally { File.Delete(path); }
    }
}

public class PomodoroTests
{
    static readonly DateTime T0 = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Starts_stopped_with_a_full_focus_session()
    {
        var p = new PomodoroTimer();
        Assert.False(p.Running);
        Assert.Equal(TimeSpan.FromMinutes(25), p.Remaining(T0));
    }

    [Fact]
    public void Counts_down_and_pause_keeps_the_remaining_time()
    {
        var p = new PomodoroTimer();
        p.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(20), p.Remaining(T0.AddMinutes(5)));
        p.Pause(T0.AddMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(20), p.Remaining(T0.AddHours(3)));
        p.Start(T0.AddHours(3));
        Assert.Equal(TimeSpan.FromMinutes(15), p.Remaining(T0.AddHours(3).AddMinutes(5)));
    }

    [Fact]
    public void Finishing_focus_moves_to_a_stopped_break_and_counts_the_session()
    {
        var p = new PomodoroTimer();
        p.Start(T0);
        Assert.False(p.Tick(T0.AddMinutes(24)));
        Assert.True(p.Tick(T0.AddMinutes(25)));
        Assert.Equal(PomodoroPhase.ShortBreak, p.Phase);
        Assert.False(p.Running);
        Assert.Equal(1, p.CompletedFocus);
        Assert.Equal(TimeSpan.FromMinutes(5), p.Remaining(T0.AddMinutes(25)));
    }

    [Fact]
    public void Every_fourth_focus_session_earns_a_long_break()
    {
        var p = new PomodoroTimer();
        var t = T0;
        for (int i = 1; i <= 4; i++)
        {
            p.Start(t); t = t.AddMinutes(25); Assert.True(p.Tick(t));
            if (i < 4) { Assert.Equal(PomodoroPhase.ShortBreak, p.Phase); p.Start(t); t = t.AddMinutes(5); Assert.True(p.Tick(t)); }
        }
        Assert.Equal(PomodoroPhase.LongBreak, p.Phase);
    }

    [Fact]
    public void Reset_and_skip_behave()
    {
        var p = new PomodoroTimer();
        p.Start(T0); p.Skip();
        Assert.Equal(PomodoroPhase.ShortBreak, p.Phase);
        Assert.Equal(0, p.CompletedFocus);   // skipping is not finishing
        p.Reset();
        Assert.Equal(PomodoroPhase.Focus, p.Phase);
        Assert.False(p.Running);
    }
}
