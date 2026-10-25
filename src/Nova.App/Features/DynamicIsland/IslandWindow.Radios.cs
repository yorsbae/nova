using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.App.Integrations.Windows;
using Nova.Core.Logging;
using System.Windows.Automation;

namespace Nova.App.Features.DynamicIsland;

/// <summary>Wi-Fi and Bluetooth inside the Island: an on/off switch plus a list to find and connect, instead of opening Windows Settings.</summary>
public partial class IslandWindow
{
    readonly WifiService _wifi = new();
    readonly BluetoothService _bluetooth = new();
    QuickRadio _radioKind;
    bool _radioOpen, _radioBusy;
    CancellationTokenSource? _radioCts;
    string? _pendingSsid;

    string RadioName => _radioKind == QuickRadio.WiFi ? "Wi-Fi" : "Bluetooth";

    /// <summary>Opens the Island on the Wi-Fi or Bluetooth panel (used by the top bar icons).</summary>
    public void ShowRadio(QuickRadio kind)
    {
        if (!IsVisible) { Reposition(); Show(); }
        if (!_expanded) Expand(focus: true);
        GoToPage(Quick, morph: false);
        OpenRadio(kind);
    }

    void OnTglWifi(object sender, RoutedEventArgs e) => OpenRadio(QuickRadio.WiFi);
    void OnTglBluetooth(object sender, RoutedEventArgs e) => OpenRadio(QuickRadio.Bluetooth);

    void OpenRadio(QuickRadio kind)
    {
        var from = CurrentSize();
        _radioKind = kind;
        _radioOpen = true;
        _pendingSsid = null;
        RadioPassBox.Visibility = Visibility.Collapsed;
        RadioTitle.Text = RadioName;
        RadioStatus.Text = "Memeriksa…";
        RadioListLabel.Text = "";
        RadioList.Children.Clear();
        QuickMain.Visibility = Visibility.Collapsed;
        RadioView.Visibility = Visibility.Visible;
        UpdateQuickTimer();
        if (_expanded) Morph(from);
        _ = RefreshRadioAsync(scan: true);
    }

    void CloseRadio(bool morph = true)
    {
        if (!_radioOpen) return;
        var from = CurrentSize();
        _radioCts?.Cancel();
        _radioOpen = false;
        RadioView.Visibility = Visibility.Collapsed;
        QuickMain.Visibility = Visibility.Visible;
        UpdateQuickTimer();
        if (morph && _expanded) Morph(from);
        RefreshQuick();
    }

    void OnRadioBack(object sender, RoutedEventArgs e) => CloseRadio();
    void OnRadioScan(object sender, RoutedEventArgs e) => _ = ScanRadioAsync();
    void OnRadioOpenSettings(object sender, RoutedEventArgs e) => OpenUri(_radioKind == QuickRadio.WiFi ? "ms-settings:network-wifi" : "ms-settings:bluetooth");

    async void OnRadioSwitch(object sender, RoutedEventArgs e)
    {
        bool want = RadioSwitch.IsChecked == true;
        var kind = _radioKind;
        RadioSwitch.IsEnabled = false;
        RadioStatus.Text = want ? "Menyalakan…" : "Mematikan…";
        var result = await RadioService.SetAsync(kind, want);
        if (!result.Ok && result.Message is not null) ShowNotice(result.Message, important: true, seconds: 8);
        await Task.Delay(want ? 1500 : 400);
        if (_radioOpen && _radioKind == kind) await RefreshRadioAsync(scan: want && result.Ok);
        RadioSwitch.IsEnabled = true;
    }

    async Task RefreshRadioAsync(bool scan)
    {
        var kind = _radioKind;
        var on = await RadioService.IsOnAsync(kind);
        if (!_radioOpen || _radioKind != kind) return;

        RadioSwitch.IsEnabled = on is not null;
        RadioSwitch.IsChecked = on == true;
        AutomationProperties.SetName(RadioSwitch, $"{RadioName}: {(on == true ? "menyala" : "mati")}");
        RadioScan.IsEnabled = on == true;

        if (on is null) { RadioStatus.Text = $"PC ini tidak punya {RadioName}"; RadioList.Children.Clear(); return; }
        if (on == false)
        {
            RadioStatus.Text = "Mati";
            RadioListLabel.Text = "";
            RadioList.Children.Clear();
            RadioList.Children.Add(Hint($"Nyalakan {RadioName} untuk mencari."));
            return;
        }
        RadioStatus.Text = kind == QuickRadio.WiFi ? (WifiService.CurrentSsid() is { } s ? "Terhubung ke " + s : "Menyala, belum terhubung") : "Menyala";
        if (scan) await ScanRadioAsync();
    }

    async Task ScanRadioAsync()
    {
        if (_radioBusy || !_radioOpen) return;
        _radioBusy = true;
        _radioCts?.Cancel();
        var cts = _radioCts = new CancellationTokenSource();
        var kind = _radioKind;
        RadioScan.Content = "Mencari…";
        RadioScan.IsEnabled = false;
        try
        {
            var from = CurrentSize();
            if (kind == QuickRadio.WiFi) await ScanWifiAsync();
            else await ScanBluetoothAsync(cts.Token);
            if (_expanded && _radioOpen) Morph(from);
        }
        catch (Exception ex) { FileLog.Write($"Radio scan failed: {ex.GetType().Name}"); }
        finally
        {
            _radioBusy = false;
            RadioScan.Content = "Cari";
            RadioScan.IsEnabled = _radioOpen;
        }
    }

    async Task ScanWifiAsync()
    {
        RadioListLabel.Text = "Jaringan tersedia";
        var (networks, error) = await _wifi.ScanAsync();
        if (!_radioOpen || _radioKind != QuickRadio.WiFi) return;
        RadioList.Children.Clear();
        if (error is not null) { RadioList.Children.Add(Hint(error)); return; }
        if (networks.Count == 0) { RadioList.Children.Add(Hint("Tidak ada jaringan yang terlihat.")); return; }
        foreach (var n in networks)
        {
            var net = n;
            RadioList.Children.Add(net.Connected
                ? BuildRow(net.Ssid, "Terhubung", onDelete: () => { _wifi.Disconnect(); RadioStatus.Text = "Terputus"; _ = ScanRadioAsync(); }, deleteName: "Putuskan", accent: true)
                : BuildRow(net.Ssid, (net.Secure ? "Terkunci · " : "Terbuka · ") + net.Signal + "%", onClick: () => _ = ConnectWifiAsync(net.Ssid, net.Secure, null)));
        }
    }

    async Task ConnectWifiAsync(string ssid, bool secure, string? password)
    {
        RadioStatus.Text = "Menyambung ke " + ssid + "…";
        var result = await _wifi.ConnectAsync(ssid, secure, password);
        if (!_radioOpen) return;
        if (result.NeedsPassword)
        {
            var from = CurrentSize();
            _pendingSsid = ssid;
            RadioPassLabel.Text = "Kata sandi untuk " + ssid;
            RadioPass.Clear();
            RadioPassBox.Visibility = Visibility.Visible;
            RadioStatus.Text = "Butuh kata sandi";
            if (_hoverOpened) PinOpen();   // typing needs keyboard focus
            RadioPass.Focus();
            Morph(from);
            return;
        }
        if (result.Ok)
        {
            RadioPassBox.Visibility = Visibility.Collapsed;
            _pendingSsid = null;
            await Task.Delay(2500);
            if (_radioOpen) await RefreshRadioAsync(scan: true);
        }
        else
        {
            RadioStatus.Text = result.Message ?? "Gagal menyambung";
            ShowNotice(result.Message ?? "Gagal menyambung.", important: true, seconds: 6);
        }
    }

    void OnRadioConnect(object sender, RoutedEventArgs e)
    {
        if (_pendingSsid is not { } ssid || RadioPass.Password.Length == 0) return;
        var pw = RadioPass.Password;
        RadioPass.Clear();
        _ = ConnectWifiAsync(ssid, true, pw);
    }

    void OnRadioPassKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnRadioConnect(sender, e); e.Handled = true; }
    }

    async Task ScanBluetoothAsync(CancellationToken ct)
    {
        RadioList.Children.Clear();
        RadioListLabel.Text = "Perangkat Anda";
        var paired = await _bluetooth.PairedAsync();
        if (!_radioOpen || _radioKind != QuickRadio.Bluetooth) return;
        if (paired.Count == 0) RadioList.Children.Add(Hint("Belum ada perangkat yang dipasangkan."));
        foreach (var d in paired)
        {
            var dev = d;
            RadioList.Children.Add(BuildRow(dev.Name, dev.Connected ? "Terhubung" : "Sambungkan", accent: dev.Connected,
                onClick: dev.Connected ? null : () => _ = ConnectBluetoothAsync(dev)));
        }

        RadioStatus.Text = "Mencari perangkat di sekitar…";
        var nearby = await _bluetooth.DiscoverAsync(8, ct);
        if (!_radioOpen || _radioKind != QuickRadio.Bluetooth || ct.IsCancellationRequested) return;
        RadioStatus.Text = paired.Any(p => p.Connected) ? $"Menyala · {paired.Count(p => p.Connected)} terhubung" : "Menyala";
        RadioListLabel.Text = nearby.Count > 0 ? "Perangkat Anda · di sekitar" : "Perangkat Anda";
        foreach (var d in nearby.Where(n => paired.All(p => p.Id != n.Id)))
        {
            var dev = d;
            RadioList.Children.Add(BuildRow(dev.Name, "Pasangkan", onClick: () => _ = PairBluetoothAsync(dev)));
        }
    }

    async Task ConnectBluetoothAsync(BluetoothDeviceInfo dev)
    {
        RadioStatus.Text = "Menyambung ke " + dev.Name + "…";
        var r = await _bluetooth.ConnectAsync(dev.Id);
        if (!r.Ok) ShowNotice(r.Message ?? "Gagal menyambung.", important: true, seconds: 8);
        if (_radioOpen) await RefreshRadioAsync(scan: true);
    }

    async Task PairBluetoothAsync(BluetoothDeviceInfo dev)
    {
        RadioStatus.Text = "Memasangkan " + dev.Name + "…";
        var r = await _bluetooth.PairAsync(dev.Id);
        if (!r.Ok) ShowNotice(r.Message ?? "Gagal memasangkan.", important: true, seconds: 8);
        if (_radioOpen) await RefreshRadioAsync(scan: true);
    }
}
