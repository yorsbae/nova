using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Windows.Devices.Geolocation;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// The laptop's own position from Windows' location service (GPS, or Wi-Fi positioning when there is no GPS chip).
/// Needs Windows Settings › Privacy &amp; security › Location switched on (also "Let desktop apps access your location").
/// The position stays on this PC; only the weather request sends the coordinates (see Settings). Never throws.
/// </summary>
public static class LocationService
{
    public enum Status { Ok, Denied, Unavailable }

    public sealed record Fix(Status Status, double Latitude = double.NaN, double Longitude = double.NaN);

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 64 * 1024 };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NOVA/1.0 (desktop companion)");   // required by the OpenStreetMap usage policy
        return c;
    }

    /// <summary>
    /// A readable place for a position: "Sambong, Batang, Jawa Tengah" (village, regency, province); when the village is unknown
    /// "Batang, Jawa Tengah". Uses OpenStreetMap Nominatim; only the coordinates are sent, and only when the position changed.
    /// Returns null when offline or unknown (the caller keeps showing "Lokasi saat ini").
    /// </summary>
    public static async Task<string?> PlaceNameAsync(double lat, double lon)
    {
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=14&addressdetails=1&accept-language=id&lat={lat:0.#####}&lon={lon:0.#####}");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("address", out var a)) return null;
            string Pick(params string[] keys)
            {
                foreach (var k in keys)
                    if (a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                        return v.GetString()!.Trim();
                return "";
            }
            var local = Pick("village", "suburb", "neighbourhood", "hamlet", "quarter", "city_district", "town");
            var region = Pick("county", "city", "municipality", "regency", "state_district");
            var state = Pick("state", "province");
            var parts = new[] { local, region, state }.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return parts.Length == 0 ? null : string.Join(", ", parts);
        }
        catch (Exception ex) { FileLog.Write($"Place name unavailable: {ex.GetType().Name}"); return null; }
    }

    public static async Task<Fix> GetAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed) return new Fix(Status.Denied);
            // City-level accuracy is enough for weather and prayer times; a cached position up to 30 min old is fine.
            var locator = new Geolocator { DesiredAccuracyInMeters = 5000 };
            var pos = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(20));
            var p = pos.Coordinate.Point.Position;
            if (double.IsNaN(p.Latitude) || double.IsNaN(p.Longitude)) return new Fix(Status.Unavailable);
            return new Fix(Status.Ok, p.Latitude, p.Longitude);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Location unavailable: {ex.GetType().Name}");
            return new Fix(Status.Unavailable);
        }
    }
}
