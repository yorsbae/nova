using Nova.Core.Configuration;
using Nova.Core.Data;
using Nova.Core.Reminders;
using Nova.Core.Status;
using Xunit;

namespace Nova.Core.Tests;

public class PrayerTimeTests
{
    static readonly DateTime Day = new(2026, 10, 26);

    static TimeSpan At(IReadOnlyList<PrayerEntry> list, Prayer p) => list.Single(e => e.Prayer == p).Time.TimeOfDay;

    static void AssertNear(TimeSpan actual, int hour, int minute, int toleranceMinutes = 8)
        => Assert.InRange(Math.Abs((actual - new TimeSpan(hour, minute, 0)).TotalMinutes), 0, toleranceMinutes);

    [Fact]
    public void Jakarta_times_are_in_the_right_places_and_order()
    {
        var d = PrayerCalculator.Compute(Day, -6.2088, 106.8456, 7);
        Assert.Equal(6, d.Count);
        AssertNear(At(d, Prayer.Fajr), 4, 10);
        AssertNear(At(d, Prayer.Sunrise), 5, 25);
        AssertNear(At(d, Prayer.Dhuhr), 11, 38);
        AssertNear(At(d, Prayer.Asr), 14, 51);
        AssertNear(At(d, Prayer.Maghrib), 17, 47);
        AssertNear(At(d, Prayer.Isha), 18, 58);
        Assert.Equal(d.OrderBy(e => e.Time).Select(e => e.Prayer), d.Select(e => e.Prayer));
    }

    [Fact]
    public void Mecca_noon_is_just_after_twelve()
    {
        var d = PrayerCalculator.Compute(Day, 21.4225, 39.8262, 3);
        AssertNear(At(d, Prayer.Dhuhr), 12, 6, 6);
    }

    [Fact]
    public void A_place_further_east_has_earlier_clock_times_in_the_same_zone()
    {
        var jakarta = PrayerCalculator.Compute(Day, -6.2088, 106.8456, 7);
        var surabaya = PrayerCalculator.Compute(Day, -7.2575, 112.7521, 7);
        Assert.True(At(surabaya, Prayer.Dhuhr) < At(jakarta, Prayer.Dhuhr));
    }

    [Fact]
    public void Hanafi_asr_is_later_than_the_usual_asr()
    {
        var usual = PrayerCalculator.Compute(Day, -6.2088, 106.8456, 7);
        var hanafi = PrayerCalculator.Compute(Day, -6.2088, 106.8456, 7, hanafiAsr: true);
        Assert.True(At(hanafi, Prayer.Asr) > At(usual, Prayer.Asr));
    }

    [Fact]
    public void Only_sunrise_has_no_adzan()
    {
        var d = PrayerCalculator.Compute(Day, -6.2088, 106.8456, 7);
        Assert.Equal(new[] { Prayer.Sunrise }, d.Where(e => !e.HasAdzan).Select(e => e.Prayer));
        Assert.Equal("Dzuhur", d.Single(e => e.Prayer == Prayer.Dhuhr).Name);
    }

    [Fact]
    public void Next_is_the_first_one_still_to_come_else_tomorrows_fajr()
    {
        double Offset(DateTime _) => 7;
        var noon = PrayerCalculator.Next(Day.AddHours(12), -6.2088, 106.8456, Offset);
        Assert.Equal(Prayer.Asr, noon!.Prayer);

        var night = PrayerCalculator.Next(Day.AddHours(23), -6.2088, 106.8456, Offset);
        Assert.Equal(Prayer.Fajr, night!.Prayer);
        Assert.Equal(Day.AddDays(1).Date, night.Time.Date);
    }

    [Fact]
    public void Far_from_the_equator_a_missing_twilight_is_skipped_not_wrong()
    {
        var d = PrayerCalculator.Compute(new DateTime(2026, 6, 21), 64.1, -21.9, 0);   // Reykjavik, midsummer
        Assert.DoesNotContain(d, e => e.Prayer == Prayer.Isha);
    }

    [Fact]
    public void Every_listed_place_can_be_found_and_has_sane_coordinates()
    {
        Assert.NotEmpty(PrayerPlaces.All);
        foreach (var p in PrayerPlaces.All)
        {
            Assert.Same(p, PrayerPlaces.Find(p.Name.ToUpperInvariant()));
            Assert.InRange(p.Latitude, -11, 6);
            Assert.InRange(p.Longitude, 95, 141);
        }
        Assert.Null(PrayerPlaces.Find("Atlantis"));
    }
}

public class HydrationScheduleTests
{
    [Fact]
    public void The_next_reminder_is_one_interval_later_inside_the_window()
        => Assert.Equal(new DateTime(2026, 10, 26, 11, 0, 0), HydrationSchedule.NextAfter(new DateTime(2026, 10, 26, 10, 0, 0), 60, 7, 22));

    [Fact]
    public void After_the_window_closes_the_next_one_waits_for_tomorrow_morning()
        => Assert.Equal(new DateTime(2026, 10, 27, 7, 0, 0), HydrationSchedule.NextAfter(new DateTime(2026, 10, 26, 21, 30, 0), 60, 7, 22));

    [Fact]
    public void Before_the_window_opens_the_next_one_waits_for_the_opening_hour()
        => Assert.Equal(new DateTime(2026, 10, 26, 7, 0, 0), HydrationSchedule.NextAfter(new DateTime(2026, 10, 26, 3, 0, 0), 60, 7, 22));

    [Fact]
    public void A_window_may_cross_midnight_and_equal_hours_mean_all_day()
    {
        Assert.True(HydrationSchedule.InWindow(new DateTime(2026, 1, 1, 23, 0, 0), 22, 6));
        Assert.True(HydrationSchedule.InWindow(new DateTime(2026, 1, 1, 2, 0, 0), 22, 6));
        Assert.False(HydrationSchedule.InWindow(new DateTime(2026, 1, 1, 12, 0, 0), 22, 6));
        Assert.True(HydrationSchedule.InWindow(new DateTime(2026, 1, 1, 12, 0, 0), 0, 0));
    }

    [Fact]
    public void Silly_intervals_are_kept_in_range()
    {
        var t = new DateTime(2026, 10, 26, 10, 0, 0);
        Assert.Equal(t.AddMinutes(5), HydrationSchedule.NextAfter(t, 0, 0, 0));
        Assert.Equal(t.AddMinutes(720), HydrationSchedule.NextAfter(t, 99999, 0, 0));
    }
}

public class AlarmAndWaterDataTests
{
    static readonly DateTime Monday = new(2026, 10, 26, 6, 30, 0);   // a Monday

    [Fact]
    public void Alarm_time_is_normalised_and_bad_times_are_refused()
    {
        var d = new UserData();
        Assert.True(d.AddAlarm("6:30", "  wake up ", 0));
        Assert.Equal("06:30", d.Alarms[0].Time);
        Assert.Equal("wake up", d.Alarms[0].Label);
        Assert.False(d.AddAlarm("25:00", "", 0));
        Assert.False(d.AddAlarm("soon", "", 0));
        Assert.False(d.AddAlarm("24:00", "", 0));
    }

    [Fact]
    public void Alarms_are_kept_in_time_order_and_capped()
    {
        var d = new UserData();
        d.AddAlarm("09:00", "b", 0); d.AddAlarm("06:00", "a", 0);
        Assert.Equal(new[] { "06:00", "09:00" }, d.Alarms.Select(a => a.Time));
        for (int i = 0; i < 40; i++) d.AddAlarm("07:00", "", 0);
        Assert.Equal(UserData.MaxAlarms, d.Alarms.Count);
    }

    [Fact]
    public void A_one_shot_alarm_rings_once_then_switches_off()
    {
        var d = new UserData();
        d.AddAlarm("06:30", "once", 0);
        Assert.Single(d.TakeDueAlarms(Monday));
        Assert.False(d.Alarms[0].Enabled);
        Assert.Empty(d.TakeDueAlarms(Monday.AddSeconds(30)));
    }

    [Fact]
    public void A_repeating_alarm_rings_on_its_days_once_per_day()
    {
        var d = new UserData();
        int mondayAndWednesday = (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Wednesday);
        d.AddAlarm("06:30", "gym", mondayAndWednesday);

        Assert.Single(d.TakeDueAlarms(Monday));
        Assert.Empty(d.TakeDueAlarms(Monday.AddSeconds(45)));            // already rang today
        Assert.True(d.Alarms[0].Enabled);                                 // still on for next time
        Assert.Empty(d.TakeDueAlarms(Monday.AddDays(1)));                 // Tuesday: not its day
        Assert.Single(d.TakeDueAlarms(Monday.AddDays(2)));                // Wednesday
    }

    [Fact]
    public void An_alarm_that_is_too_late_or_too_early_or_off_does_not_ring()
    {
        var d = new UserData();
        d.AddAlarm("06:30", "x", 0);
        Assert.Empty(d.TakeDueAlarms(Monday.AddMinutes(-1)));
        Assert.Empty(d.TakeDueAlarms(Monday.AddMinutes(3)));
        d.Alarms[0].Enabled = false;
        Assert.Empty(d.TakeDueAlarms(Monday));
    }

    [Fact]
    public void Days_are_summarised_for_the_list()
    {
        Assert.Equal("Sekali", new AlarmItem { Days = 0 }.DaysText());
        Assert.Equal("Setiap hari", new AlarmItem { Days = 0x7F }.DaysText());
        Assert.Equal("Senin–Jumat", new AlarmItem { Days = 0x3E }.DaysText());
        Assert.Equal("Sen Rab Min", new AlarmItem { Days = (1 << 1) | (1 << 3) | 1 }.DaysText());
    }

    [Fact]
    public void Water_counts_per_day_never_goes_below_zero_and_old_days_are_dropped()
    {
        var d = new UserData();
        var today = new DateTime(2026, 10, 26);
        Assert.Equal(0, d.WaterOn(today));
        Assert.Equal(2, d.AddWater(today, 2));
        Assert.Equal(0, d.AddWater(today, -5));
        d.AddWater(today, 3);
        Assert.Equal(0, d.WaterOn(today.AddDays(-1)));
        for (int i = 1; i <= 80; i++) d.AddWater(today.AddDays(-i), 1);
        Assert.True(d.Water.Count <= 60);
        Assert.Equal(3, d.WaterOn(today));   // the newest days are the ones kept
    }

    [Fact]
    public void Alarms_and_water_survive_a_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), "nova-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UserDataStore(path);
            var d = new UserData();
            d.AddAlarm("07:15", "kerja", 0x3E);
            d.AddWater(new DateTime(2026, 10, 26), 4);
            Assert.True(store.Save(d));
            var back = store.Load();
            Assert.Equal("07:15", back.Alarms[0].Time);
            Assert.Equal(0x3E, back.Alarms[0].Days);
            Assert.Equal(4, back.WaterOn(new DateTime(2026, 10, 26)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public class CalendarTaskTests
{
    static readonly DateTime Today = new(2026, 10, 26);

    [Fact]
    public void A_task_can_be_added_to_a_calendar_day()
    {
        var d = new UserData();
        Assert.True(d.AddTask("Submit report", "17:00", Today.AddDays(3).AddHours(9), priority: 1));
        var t = d.TasksOn(Today.AddDays(3)).Single();
        Assert.Equal(Today.AddDays(3), t.Due);   // time of day is dropped
        Assert.Equal(1, t.Priority);
        Assert.Empty(d.TasksOn(Today));
    }

    [Fact]
    public void Todays_list_has_undated_tasks_todays_tasks_and_unfinished_old_ones_but_not_future_ones()
    {
        var d = new UserData();
        d.AddTask("undated", "", 0);
        d.AddTask("today", "", Today);
        d.AddTask("future", "", Today.AddDays(2));
        d.AddTask("old open", "", Today.AddDays(-2));
        d.AddTask("old done", "", Today.AddDays(-2));
        d.Tasks.Single(t => t.Title == "old done").Done = true;

        Assert.Equal(new[] { "undated", "today", "old open" }, d.TodayTasks(Today).Select(t => t.Title));
    }

    [Fact]
    public void Progress_counts_only_todays_list()
    {
        var d = new UserData();
        d.AddTask("a", "", Today);
        d.AddTask("b", "", Today);
        d.AddTask("later", "", Today.AddDays(5));
        d.Tasks[0].Done = true;
        Assert.Equal(0.5, d.PlanProgress(Today));
    }

    [Fact]
    public void Dated_tasks_survive_a_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), "nova-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UserDataStore(path);
            var d = new UserData();
            d.AddTask("dated", "", Today.AddDays(1));
            store.Save(d);
            Assert.Equal(Today.AddDays(1), store.Load().Tasks[0].Due);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public class UsageFormatTests
{
    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(double.NaN, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(2048, "2 KB/s")]
    [InlineData(1572864, "1.5 MB/s")]
    [InlineData(3.2 * 1024 * 1024 * 1024, "3.2 GB/s")]
    public void Speeds_are_short_and_readable(double bytesPerSecond, string expected)
        => Assert.Equal(expected, RateFormatter.Format(bytesPerSecond));

    [Fact]
    public void The_compact_line_names_each_value_and_skips_unknown_ones()
    {
        Assert.Equal("CPU 12% · RAM 48%", RateFormatter.Compact(12, 48));
        Assert.Equal("RAM 48%", RateFormatter.Compact(null, 48));
        Assert.Equal("", RateFormatter.Compact(null, null));
    }
}

public class FaceAndReminderSettingsTests
{
    [Fact]
    public void Defaults_show_the_clock_and_a_pet_face_taking_turns_and_everything_else_off()
    {
        var s = new NovaSettings();
        Assert.Equal(IslandFaceMode.Alternate, s.IslandFaceMode);
        Assert.Equal(IslandFaceStyle.Pet, s.IslandFaceStyle);
        Assert.False(s.IslandShowUsage);
        Assert.False(s.HydrationEnabled);
        Assert.False(s.PrayerNotify);
        Assert.Equal("Jakarta", s.PrayerCity);
    }

    [Fact]
    public void Silly_values_are_brought_back_into_range()
    {
        var s = new NovaSettings
        {
            IslandFaceClockSeconds = 0, IslandFaceShowSeconds = 9999, HydrationIntervalMinutes = 1, HydrationGoalGlasses = 500,
            HydrationFromHour = -3, HydrationToHour = 99, PrayerLatitude = 500, PrayerLongitude = double.NaN,
            IslandFaceMode = (IslandFaceMode)42, IslandFaceStyle = (IslandFaceStyle)42
        };
        SettingsValidator.Normalize(s);
        Assert.Equal(5, s.IslandFaceClockSeconds);
        Assert.Equal(120, s.IslandFaceShowSeconds);
        Assert.Equal(15, s.HydrationIntervalMinutes);
        Assert.Equal(20, s.HydrationGoalGlasses);
        Assert.Equal(0, s.HydrationFromHour);
        Assert.Equal(24, s.HydrationToHour);
        Assert.Equal(65, s.PrayerLatitude);
        Assert.Equal(new NovaSettings().PrayerLongitude, s.PrayerLongitude);
        Assert.Equal(IslandFaceMode.Alternate, s.IslandFaceMode);
        Assert.Equal(IslandFaceStyle.Pet, s.IslandFaceStyle);
    }

    [Theory]
    [InlineData("Sangatta", "Sangatta")]
    [InlineData("Custom", "Custom")]
    [InlineData("Atlantis", "Jakarta")]
    [InlineData("", "Jakarta")]
    public void An_unknown_prayer_city_falls_back_to_the_default(string given, string expected)
    {
        var s = new NovaSettings { PrayerCity = given };
        SettingsValidator.Normalize(s);
        Assert.Equal(expected, s.PrayerCity);
    }
}
