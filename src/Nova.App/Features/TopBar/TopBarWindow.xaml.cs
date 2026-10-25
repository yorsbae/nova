using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using Nova.App.Integrations.Windows;
using Nova.Core.Configuration;
using Nova.Core.Logging;
using Nova.Core.Status;

namespace Nova.App.Features.TopBar;

/// <summary>
/// The strip along the top edge of the screen (the "menu bar" of the Mac look). It is a real AppBar: Windows reserves
/// its space, so other windows maximize below it instead of underneath it. The Island sits in its centre as the notch.
/// The right side shows Wi-Fi, Bluetooth, sound and battery icons; each one is clickable.
/// </summary>
public partial class TopBarWindow : Window
{
    readonly DispatcherTimer _tick = new();
    readonly AppBarService _bar;
    NovaSettings _s;
    string? _weather;
    bool _busy;

    public event Action<bool>? FullscreenChanged;
    public event Action? Repositioned;
    /// <summary>Raised after every status reading, so NOVA can announce changes (Bluetooth connected, Wi-Fi lost...).</summary>
    public event Action<QuickStatus>? StatusRead;

    public TopBarWindow(NovaSettings settings)
    {
        _s = settings;
        InitializeComponent();
        _bar = new AppBarService(this);
        _bar.FullscreenChanged += f => FullscreenChanged?.Invoke(f);
        _bar.Repositioned += () => Repositioned?.Invoke();
        _tick.Tick += (_, _) => Refresh();
        Closed += (_, _) => { _tick.Stop(); _bar.Dispose(); };
    }

    /// <summary>Applies settings and (re)docks the bar. Call after Show().</summary>
    public void ApplySettings(NovaSettings s)
    {
        _s = s;
        Tray.Visibility = s.ShowSystemStatus ? Visibility.Visible : Visibility.Collapsed;
        _tick.Interval = TimeSpan.FromSeconds(s.ShowSystemStatus ? 5 : 60);
        Refresh();
        _tick.Start();
        _bar.Dock(s.IslandMonitor, NotchLayout.BarHeightDip(s.ClockFontSize));
    }

    /// <summary>Re-docks after a monitor, resolution or scale change.</summary>
    public void Relocate() => _bar.Reposition();

    public void SetWeather(string? text) { _weather = text; Refresh(); }

    /// <summary>Gives the reserved screen space back to Windows right away.</summary>
    public void Release() => _bar.Dispose();

    void Refresh()
    {
        LeftText.Text = DateTime.Now.ToString("ddd, d MMM", CultureInfo.CurrentCulture);
        RightText.Text = _s.WeatherEnabled && !string.IsNullOrWhiteSpace(_weather) ? _weather! : "";
        if (_s.ShowSystemStatus) _ = RefreshStatusAsync();
    }

    async Task RefreshStatusAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var q = await Task.Run(QuickStatusService.ReadAsync);   // off the UI thread: it may start a process and talks to Bluetooth
            ApplyStatus(q);
            StatusRead?.Invoke(q);
        }
        catch (Exception ex) { FileLog.Write($"Top bar status failed: {ex.GetType().Name}"); }
        finally { _busy = false; }
    }

    void ApplyStatus(QuickStatus q)
    {
        // Network
        WifiGlyph.Text = q.Net == NetKind.Wired ? StatusGlyphs.Ethernet : StatusGlyphs.Wifi;
        WifiGlyph.Opacity = q.Net == NetKind.None ? 0.3 : q.Net == NetKind.Wifi ? StatusGlyphs.WifiOpacity(q.WifiSignal) : 1;
        var net = q.Net switch
        {
            NetKind.Wifi => q.WifiSignal >= 0 ? $"Wi-Fi connected, signal {q.WifiSignal}%" : "Wi-Fi connected",
            NetKind.Wired => "Wired network connected",
            _ => "Not connected to a network"
        };
        WifiButton.ToolTip = net;
        AutomationProperties.SetName(WifiButton, net);

        // Bluetooth (hidden on PCs without a Bluetooth radio)
        BluetoothButton.Visibility = q.BluetoothPresent ? Visibility.Visible : Visibility.Collapsed;
        BluetoothGlyph.Opacity = q.BluetoothOn ? 1 : 0.3;
        var bt = !q.BluetoothOn ? "Bluetooth off"
               : q.BluetoothConnected > 0 ? $"Bluetooth on, {q.BluetoothConnected} connected"
               : "Bluetooth on";
        BluetoothButton.ToolTip = bt;
        AutomationProperties.SetName(BluetoothButton, bt);

        // Sound
        ShowVolume(q.Volume, q.Muted);

        // Battery (hidden on desktops)
        if (q.BatteryPercent is int pct)
        {
            BatteryButton.Visibility = Visibility.Visible;
            BatteryText.Text = pct + "%";
            BatteryGlyph.Text = StatusGlyphs.Battery(pct, q.Charging);
            var b = $"Battery {pct}%" + (q.Charging ? ", charging" : "");
            BatteryButton.ToolTip = b;
            AutomationProperties.SetName(BatteryButton, b);
        }
        else BatteryButton.Visibility = Visibility.Collapsed;
    }

    void ShowVolume(double? volume, bool muted)
    {
        if (volume is not double v) { VolumeButton.Visibility = Visibility.Collapsed; return; }
        VolumeButton.Visibility = Visibility.Visible;
        VolumeGlyph.Text = StatusGlyphs.Volume(v, muted);
        var text = muted ? "Sound muted" : $"Volume {Math.Round(v * 100)}%";
        VolumeButton.ToolTip = text + " (click to mute, scroll to change)";
        AutomationProperties.SetName(VolumeButton, text);
    }

    // ---- clicks ----

    static void OpenSettingsPage(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { FileLog.Write($"Couldn't open {uri}: {ex.GetType().Name}"); }
    }

    void OnWifiClick(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:network-wifi");
    void OnBluetoothClick(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:bluetooth");
    void OnBatteryClick(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:batterysaver");

    void OnVolumeClick(object sender, RoutedEventArgs e)
    {
        AudioService.ToggleMute();
        if (AudioService.TryGet(out var v, out var m)) ShowVolume(v, m);
    }

    void OnVolumeWheel(object sender, MouseWheelEventArgs e)
    {
        if (!AudioService.TryGet(out var v, out _)) return;
        AudioService.SetVolume(v + (e.Delta > 0 ? 0.05 : -0.05));
        if (AudioService.TryGet(out var nv, out var nm)) ShowVolume(nv, nm);
        e.Handled = true;
    }

    // ---- narrow screens: drop the least important text first ----
    void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => RightText.Visibility = ActualWidth < 1000 ? Visibility.Collapsed : Visibility.Visible;

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        NativeMethods.SetOverlayStyle(this, noActivate: true);   // never takes focus, hidden from Alt+Tab
        NativeMethods.DisableRoundedCorners(this);
    }
}
