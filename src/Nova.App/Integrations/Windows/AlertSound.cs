using System.IO;
using System.Media;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// The sound of an alarm or an adzan: the user's own file (wav, mp3...) if one is set and exists, otherwise a Windows system sound.
/// One sound at a time; <see cref="Stop"/> silences it. A looping sound stops by itself after <c>maxSeconds</c>.
/// </summary>
internal static class AlertSound
{
    static MediaPlayer? _player;
    static readonly DispatcherTimer StopTimer = new();

    public static void Play(string? path, bool loop, int maxSeconds)
    {
        Stop();
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var player = new MediaPlayer();
                if (loop) player.MediaEnded += (_, _) => { player.Position = TimeSpan.Zero; player.Play(); };
                player.Open(new Uri(path));
                player.Play();
                _player = player;
            }
            else
            {
                if (loop) SystemSounds.Exclamation.Play();
                else SystemSounds.Asterisk.Play();
            }
        }
        catch (Exception ex) { FileLog.Write($"Alert sound failed: {ex.GetType().Name}"); }

        StopTimer.Stop();
        StopTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(maxSeconds, 3, 600));
        StopTimer.Tick -= OnTimeUp;
        StopTimer.Tick += OnTimeUp;
        StopTimer.Start();
    }

    static void OnTimeUp(object? sender, EventArgs e) => Stop();

    public static void Stop()
    {
        StopTimer.Stop();
        try { _player?.Stop(); _player?.Close(); }
        catch { /* already gone */ }
        _player = null;
    }
}
