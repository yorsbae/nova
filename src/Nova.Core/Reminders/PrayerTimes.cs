namespace Nova.Core.Reminders;

public enum Prayer { Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha }

public sealed record PrayerEntry(Prayer Prayer, DateTime Time)
{
    /// <summary>Indonesian names, as used on prayer timetables.</summary>
    public string Name => Prayer switch
    {
        Prayer.Fajr => "Subuh", Prayer.Sunrise => "Terbit", Prayer.Dhuhr => "Dzuhur",
        Prayer.Asr => "Ashar", Prayer.Maghrib => "Maghrib", _ => "Isya"
    };

    /// <summary>Sunrise only marks the end of Subuh; it is shown but there is no adzan for it.</summary>
    public bool HasAdzan => Prayer != Prayer.Sunrise;
}

/// <summary>A named place with coordinates, for the prayer timetable.</summary>
public sealed record PrayerPlace(string Name, double Latitude, double Longitude);

public static class PrayerPlaces
{
    public const string Custom = "Custom";

    public static readonly IReadOnlyList<PrayerPlace> All = new PrayerPlace[]
    {
        new("Jakarta", -6.2088, 106.8456), new("Bandung", -6.9175, 107.6191), new("Semarang", -6.9667, 110.4167),
        new("Yogyakarta", -7.7956, 110.3695), new("Surabaya", -7.2575, 112.7521), new("Denpasar", -8.6500, 115.2167),
        new("Mataram", -8.5833, 116.1167), new("Kupang", -10.1772, 123.6070), new("Medan", 3.5952, 98.6722),
        new("Banda Aceh", 5.5483, 95.3238), new("Pekanbaru", 0.5071, 101.4478), new("Padang", -0.9471, 100.4172),
        new("Palembang", -2.9761, 104.7754), new("Batam", 1.0456, 104.0305), new("Pontianak", -0.0263, 109.3425),
        new("Banjarmasin", -3.3194, 114.5908), new("Balikpapan", -1.2379, 116.8529), new("Samarinda", -0.5022, 117.1536),
        new("Sangatta", 0.5000, 117.5333), new("Makassar", -5.1477, 119.4327), new("Palu", -0.9003, 119.8779),
        new("Kendari", -3.9985, 122.5129), new("Manado", 1.4748, 124.8421), new("Ambon", -3.6954, 128.1814),
        new("Jayapura", -2.5337, 140.7181),
    };

    public static PrayerPlace? Find(string? name)
        => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Prayer times worked out on this PC from the sun's position (no internet). Defaults follow the Indonesian Ministry of Religious Affairs
/// (Kemenag): Fajr 20°, Isha 18°, Asr by the usual (Shafi'i) shadow rule, and 2 minutes of precaution (ihtiyath) added.
/// The method is the widely used one from PrayTimes.org.
/// </summary>
public static class PrayerCalculator
{
    public const double SunriseAngle = 0.833;

    public static IReadOnlyList<PrayerEntry> Compute(DateTime date, double latitude, double longitude, double utcOffsetHours,
        double fajrAngle = 20, double ishaAngle = 18, bool hanafiAsr = false, int ihtiyathMinutes = 2)
    {
        var jd = Julian(date.Year, date.Month, date.Day) - longitude / 360.0;
        double adjust = utcOffsetHours - longitude / 15.0;

        double Mid(double t) => FixHour(12 - Sun(jd + t).Equation);
        double AtAngle(double angle, double t, bool before)
        {
            var decl = Sun(jd + t).Declination;
            var x = (-Sin(angle) - Sin(decl) * Sin(latitude)) / (Cos(decl) * Cos(latitude));
            if (x < -1 || x > 1) return double.NaN;   // the sun never reaches that angle here (far from the equator)
            var dt = ArcCos(x) / 15.0;
            return Mid(t) + (before ? -dt : dt);
        }
        double AsrTime(double factor, double t)
        {
            var decl = Sun(jd + t).Declination;
            var angle = -ArcCot(factor + Tan(Math.Abs(latitude - decl)));
            return AtAngle(angle, t, before: false);
        }

        var hours = new (Prayer, double, int)[]
        {
            (Prayer.Fajr, AtAngle(fajrAngle, 5 / 24.0, true), ihtiyathMinutes),
            (Prayer.Sunrise, AtAngle(SunriseAngle, 6 / 24.0, true), -ihtiyathMinutes),
            (Prayer.Dhuhr, Mid(12 / 24.0), ihtiyathMinutes),
            (Prayer.Asr, AsrTime(hanafiAsr ? 2 : 1, 13 / 24.0), ihtiyathMinutes),
            (Prayer.Maghrib, AtAngle(SunriseAngle, 18 / 24.0, false), ihtiyathMinutes),
            (Prayer.Isha, AtAngle(ishaAngle, 18 / 24.0, false), ihtiyathMinutes),
        };

        var list = new List<PrayerEntry>();
        foreach (var (prayer, h, extra) in hours)
        {
            if (double.IsNaN(h)) continue;
            var local = FixHour(h + adjust) + extra / 60.0;
            list.Add(new PrayerEntry(prayer, date.Date.AddMinutes(Math.Round(local * 60))));
        }
        return list;
    }

    /// <summary>The first prayer that has not started yet today, else tomorrow's Fajr (null if no times could be worked out).</summary>
    public static PrayerEntry? Next(DateTime now, double latitude, double longitude, Func<DateTime, double> utcOffsetHours)
    {
        var today = Compute(now.Date, latitude, longitude, utcOffsetHours(now.Date));
        var next = today.FirstOrDefault(p => p.Time > now);
        if (next is not null) return next;
        var tomorrow = Compute(now.Date.AddDays(1), latitude, longitude, utcOffsetHours(now.Date.AddDays(1)));
        return tomorrow.FirstOrDefault();
    }

    // ---- astronomy (angles in degrees) ----

    static double Julian(int year, int month, int day)
    {
        if (month <= 2) { year -= 1; month += 12; }
        double a = Math.Floor(year / 100.0), b = 2 - a + Math.Floor(a / 4);
        return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day + b - 1524.5;
    }

    static (double Declination, double Equation) Sun(double jd)
    {
        double d = jd - 2451545.0;
        double g = FixAngle(357.529 + 0.98560028 * d);
        double q = FixAngle(280.459 + 0.98564736 * d);
        double l = FixAngle(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g));
        double e = 23.439 - 0.00000036 * d;
        double ra = FixHour(ArcTan2(Cos(e) * Sin(l), Cos(l)) / 15.0);
        return (ArcSin(Sin(e) * Sin(l)), q / 15.0 - ra);
    }

    static double Rad(double d) => d * Math.PI / 180.0;
    static double Deg(double r) => r * 180.0 / Math.PI;
    static double Sin(double d) => Math.Sin(Rad(d));
    static double Cos(double d) => Math.Cos(Rad(d));
    static double Tan(double d) => Math.Tan(Rad(d));
    static double ArcSin(double x) => Deg(Math.Asin(x));
    static double ArcCos(double x) => Deg(Math.Acos(x));
    static double ArcTan2(double y, double x) => Deg(Math.Atan2(y, x));
    static double ArcCot(double x) => Deg(Math.Atan(1 / x));
    static double Fix(double a, double m) { a -= m * Math.Floor(a / m); return a < 0 ? a + m : a; }
    static double FixAngle(double a) => Fix(a, 360);
    static double FixHour(double a) => Fix(a, 24);
}
