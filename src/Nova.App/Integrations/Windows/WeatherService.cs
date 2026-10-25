using System.Net.Http;
using System.Windows.Threading;
using Nova.Core.Configuration;
using Nova.Core.Logging;
using Nova.Core.Weather;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Opt-in weather from open-meteo.com. Does nothing (no timer, no network) unless the user enabled it.
/// Only the city name (once, to find coordinates) and the coordinates (every 30 min) leave the PC.
/// </summary>
public sealed class WeatherService : IDisposable
{
    static readonly HttpClient Http = CreateClient();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(30) };
    NovaSettings? _settings;
    bool _busy;

    public event Action<string?>? TextChanged;

    public WeatherService() => _timer.Tick += async (_, _) => await RefreshAsync();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 256 * 1024 };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NOVA/1.0");
        return c;
    }

    /// <summary>Applies current settings: starts or stops refreshing. Never touches the network when disabled.</summary>
    public void Configure(NovaSettings s)
    {
        _settings = s;
        if (!s.WeatherEnabled || double.IsNaN(s.WeatherLatitude) || double.IsNaN(s.WeatherLongitude))
        {
            _timer.Stop();
            TextChanged?.Invoke(null);
            return;
        }
        _timer.Start();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var s = _settings;
        if (_busy || s is null || !s.WeatherEnabled || double.IsNaN(s.WeatherLatitude) || double.IsNaN(s.WeatherLongitude)) return;
        _busy = true;
        try
        {
            var json = await Http.GetStringAsync(WeatherApi.ForecastUrl(s.WeatherLatitude, s.WeatherLongitude));
            TextChanged?.Invoke(WeatherApi.TryParseCurrent(json, out var r) && r is not null
                ? WeatherApi.Format(r, s.WeatherFahrenheit, s.WeatherPlace)
                : "Weather unavailable");
        }
        catch (Exception ex)
        {
            FileLog.Write($"Weather refresh failed: {ex.GetType().Name}");
            TextChanged?.Invoke("Weather unavailable");
        }
        finally { _busy = false; }
    }

    /// <summary>One-off city lookup. Returns null if nothing was found or the request failed.</summary>
    public static async Task<GeoResult?> GeocodeAsync(string city)
    {
        try
        {
            var json = await Http.GetStringAsync(WeatherApi.GeocodeUrl(city));
            return WeatherApi.TryParseGeocode(json, out var g) ? g : null;
        }
        catch (Exception ex) { FileLog.Write($"Geocoding failed: {ex.GetType().Name}"); return null; }
    }

    public void Dispose() => _timer.Stop();
}
