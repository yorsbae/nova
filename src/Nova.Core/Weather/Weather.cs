using System.Globalization;
using System.Text.Json;

namespace Nova.Core.Weather;

public sealed record WeatherReading(double TemperatureC, int Code);
public sealed record GeoResult(string Name, string? Country, double Latitude, double Longitude)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Country) ? Name : $"{Name}, {Country}";
}

/// <summary>Open-Meteo (https://open-meteo.com) request builders and parsers. No API key; used only when the user opts in.</summary>
public static class WeatherApi
{
    public static string ForecastUrl(double lat, double lon)
    {
        var c = CultureInfo.InvariantCulture;
        return $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString("0.####", c)}&longitude={lon.ToString("0.####", c)}&current=temperature_2m,weather_code";
    }

    public static string GeocodeUrl(string city)
        => $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language=en&format=json";

    public static bool TryParseCurrent(string? json, out WeatherReading? reading)
    {
        reading = null;
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("current", out var cur)) return false;
            if (!cur.TryGetProperty("temperature_2m", out var t) || t.ValueKind != JsonValueKind.Number) return false;
            if (!cur.TryGetProperty("weather_code", out var w) || w.ValueKind != JsonValueKind.Number) return false;
            reading = new WeatherReading(t.GetDouble(), w.GetInt32());
            return true;
        }
        catch { return false; }
    }

    public static bool TryParseGeocode(string? json, out GeoResult? result)
    {
        result = null;
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return false;
            var r = arr[0];
            if (!r.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) return false;
            if (!r.TryGetProperty("latitude", out var la) || !r.TryGetProperty("longitude", out var lo)) return false;
            var lat = la.GetDouble(); var lon = lo.GetDouble();
            if (lat is < -90 or > 90 || lon is < -180 or > 180) return false;
            var country = r.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            result = new GeoResult(n.GetString() ?? "", country, lat, lon);
            return true;
        }
        catch { return false; }
    }

    /// <summary>WMO weather interpretation codes as used by Open-Meteo.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown conditions"
    };

    public static string Format(WeatherReading r, bool fahrenheit, string place)
    {
        var temp = fahrenheit ? r.TemperatureC * 9 / 5 + 32 : r.TemperatureC;
        var unit = fahrenheit ? "°F" : "°C";
        var text = $"{Math.Round(temp).ToString("0", CultureInfo.InvariantCulture)}{unit}, {Describe(r.Code)}";
        return string.IsNullOrWhiteSpace(place) ? text : $"{place}: {text}";
    }
}
