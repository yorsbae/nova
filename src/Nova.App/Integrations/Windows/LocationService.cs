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
