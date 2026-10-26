using Nova.Core.Data;
using Xunit;

namespace Nova.Core.Tests;

public class TaskAlarmTests
{
    static readonly DateTime Day = new(2026, 10, 27);

    [Fact]
    public void Task_with_time_and_alarm_rings_once_at_its_time()
    {
        var d = new UserData();
        Assert.True(d.AddTask("Rapat", "14:30", Day, 0, alarm: true));
        Assert.Empty(d.TakeDueTaskAlarms(Day.AddHours(14)));
        Assert.Single(d.TakeDueTaskAlarms(Day.AddHours(14).AddMinutes(30)));
        Assert.Empty(d.TakeDueTaskAlarms(Day.AddHours(14).AddMinutes(31)));
    }

    [Fact]
    public void Alarm_needs_a_time_and_missed_ones_do_not_ring_late()
    {
        var d = new UserData();
        d.AddTask("Tanpa jam", "", Day, 0, alarm: true);
        Assert.False(d.Tasks[0].Alarm);
        d.AddTask("Lewat", "08:00", Day, 0, alarm: true);
        Assert.Empty(d.TakeDueTaskAlarms(Day.AddHours(12)));
        Assert.False(d.HasPendingTaskAlarm);
    }

    [Fact]
    public void Range_time_uses_its_start()
    {
        var t = new PlanTask { Time = "07:00 - 16:15" };
        Assert.True(t.TryStart(out var ts));
        Assert.Equal(TimeSpan.FromHours(7), ts);
    }
}
