using System.IO;
using System.Windows.Threading;
using Nova.Core.Logging;
using Nova.Core.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Reads and controls whatever app currently owns the Windows media session (browser, Spotify, etc.) through the
/// supported GlobalSystemMediaTransportControls API: track, album art and playback position. Event-driven — no polling.
/// </summary>
public sealed class MediaService : IDisposable
{
    const long MaxArtBytes = 4 * 1024 * 1024;

    readonly Dispatcher _ui;
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    string _artKey = "";
    bool _disposed;

    public event Action<MediaInfo?>? Changed;
    /// <summary>Album art (encoded image bytes) of the current track, or null if there is none.</summary>
    public event Action<byte[]?>? ArtChanged;
    public event Action<MediaTimeline?>? TimelineChanged;

    public MediaTimeline? Timeline { get; private set; }
    /// <summary>Windows app id of the player ("Spotify.exe", "chrome.exe"...), or null.</summary>
    public string? SourceApp { get; private set; }

    public MediaService(Dispatcher ui) => _ui = ui;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager.CurrentSessionChanged += OnSessionChanged;
            Attach();
        }
        catch (Exception ex)
        {
            FileLog.Write($"Media session unavailable: {ex.GetType().Name}");
            Publish(null, null);   // feature degrades; the rest of NOVA keeps working
        }
    }

    void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Attach();
    void OnProperties(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = RefreshAsync(true);
    void OnPlayback(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync(false);
    void OnTimeline(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshAsync(false);

    void Attach()
    {
        Detach();
        try
        {
            _session = _manager?.GetCurrentSession();
            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnProperties;
                _session.PlaybackInfoChanged += OnPlayback;
                _session.TimelinePropertiesChanged += OnTimeline;
            }
        }
        catch (Exception ex) { FileLog.Write($"Media attach failed: {ex.GetType().Name}"); _session = null; }
        _ = RefreshAsync(true);
    }

    void Detach()
    {
        if (_session is null) return;
        try
        {
            _session.MediaPropertiesChanged -= OnProperties;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
        catch { }
        _session = null;
    }

    async Task RefreshAsync(bool propertiesChanged)
    {
        try
        {
            var s = _session;
            if (s is null) { _artKey = ""; Publish(null, null); return; }
            var props = await s.TryGetMediaPropertiesAsync();
            SourceApp = s.SourceAppUserModelId;
            var pb = s.GetPlaybackInfo();
            var c = pb.Controls;
            bool playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var info = new MediaInfo(
                props.Title ?? "", props.Artist ?? "", playing,
                c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled,
                c.IsNextEnabled, c.IsPreviousEnabled);

            MediaTimeline? timeline = null;
            try
            {
                var t = s.GetTimelineProperties();
                var duration = t.EndTime - t.StartTime;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                timeline = new MediaTimeline(t.Position - t.StartTime, duration, t.LastUpdatedTime.UtcDateTime, playing);
            }
            catch { /* some apps publish no timeline: no progress bar, no synced lyrics */ }

            // Album art is read only when the track changes, not on every play/pause.
            byte[]? art = null;
            bool artRead = false;
            var key = info.Title + "\u0001" + info.Artist;
            if (propertiesChanged || key != _artKey)
            {
                _artKey = key;
                artRead = true;
                art = await ReadArtAsync(props.Thumbnail);
            }

            Publish(info, timeline, artRead, art);
        }
        catch (Exception ex) { FileLog.Write($"Media refresh failed: {ex.GetType().Name}"); Publish(null, null); }
    }

    static async Task<byte[]?> ReadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxArtBytes) return null;
            using var source = stream.AsStreamForRead();
            using var ms = new MemoryStream((int)stream.Size);
            await source.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    void Publish(MediaInfo? info, MediaTimeline? timeline, bool artRead = true, byte[]? art = null)
    {
        if (_disposed) return;
        _ui.InvokeAsync(() =>
        {
            if (_disposed) return;
            Timeline = timeline;
            Changed?.Invoke(info);
            TimelineChanged?.Invoke(timeline);
            if (artRead) ArtChanged?.Invoke(info is { HasTrack: true } ? art : null);
        });
    }

    public async void TogglePlayPause() { try { if (_session is { } s) await s.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_session is { } s) await s.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session is { } s) await s.TrySkipPreviousAsync(); } catch { } }

    public void Dispose()
    {
        _disposed = true;
        Detach();
        try { if (_manager is not null) _manager.CurrentSessionChanged -= OnSessionChanged; } catch { }
        _manager = null;
    }
}
