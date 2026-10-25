using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

public enum NetKind { None, Wifi, Wired }

/// <param name="WifiSignal">0-100, or -1 when unknown.</param>
/// <param name="Volume">0..1, or null when the PC has no output device.</param>
public sealed record QuickStatus(
    NetKind Net, int WifiSignal,
    bool BluetoothPresent, bool BluetoothOn, int BluetoothConnected,
    double? Volume, bool Muted,
    int? BatteryPercent, bool Charging,
    string? WifiName = null, IReadOnlyList<string>? BluetoothNames = null);

/// <summary>
/// Everything the top bar's little icons show: network, Bluetooth, sound, battery. Read from a background thread every few seconds;
/// uses only supported APIs and stays on this PC.
/// </summary>
public static class QuickStatusService
{
    static readonly Regex SignalLine = new(@"^\s*[^\r\n:]*?(?:signal|sinyal)[^\r\n:]*:\s*(\d{1,3})\s*%", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex SsidLine = new(@"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    static int _signal = -1;
    static string? _ssid;
    static DateTime _signalAt = DateTime.MinValue;

    public static async Task<QuickStatus> ReadAsync()
    {
        var (net, signal) = ReadNetwork();
        var (btPresent, btOn, btNames) = await ReadBluetoothAsync();

        double? volume = null; bool muted = false;
        if (AudioService.TryGet(out var v, out var m)) { volume = v; muted = m; }

        int? battery = null; bool charging = false;
        if (NativeMethods.GetSystemPowerStatus(out var p))
        {
            var info = Nova.Core.Status.BatteryInfo.From(p.ACLineStatus, p.BatteryFlag, p.BatteryLifePercent);
            if (info is not null) { battery = info.Percent; charging = info.Charging; }
        }
        return new QuickStatus(net, signal, btPresent, btOn, btNames.Count, volume, muted, battery, charging,
            net == NetKind.Wifi ? _ssid : null, btNames);
    }

    // ---- network ----

    static (NetKind, int) ReadNetwork()
    {
        try
        {
            bool wifi = false, wired = false;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                // A real connection has a gateway; this skips VPN / virtual-machine adapters that are "up" but go nowhere.
                var hasGateway = ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.GetAddressBytes().Any(b => b != 0));
                if (!hasGateway) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) wifi = true;
                else if (ni.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet) wired = true;
            }
            if (wifi) return (NetKind.Wifi, WifiSignal());
            if (wired) return (NetKind.Wired, -1);
        }
        catch (Exception ex) { FileLog.Write($"Network status failed: {ex.GetType().Name}"); }
        return (NetKind.None, -1);
    }

    /// <summary>Signal strength from the built-in "netsh wlan" tool (no admin needed). Cached for 60 s because it starts a process.</summary>
    static int WifiSignal()
    {
        if ((DateTime.UtcNow - _signalAt).TotalSeconds < 60) return _signal;
        _signalAt = DateTime.UtcNow;
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return _signal = -1;
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(2000)) { try { proc.Kill(); } catch { } return _signal = -1; }
            var ssid = SsidLine.Match(output);
            _ssid = ssid.Success ? ssid.Groups[1].Value : null;
            var m = SignalLine.Match(output);
            _signal = m.Success && int.TryParse(m.Groups[1].Value, out var pct) ? Math.Min(100, pct) : -1;
        }
        catch { _signal = -1; }
        return _signal;
    }

    // ---- bluetooth ----

    static async Task<(bool present, bool on, IReadOnlyList<string> names)> ReadBluetoothAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var bt = radios.Where(r => r.Kind == RadioKind.Bluetooth).ToList();
            var none = (IReadOnlyList<string>)Array.Empty<string>();
            if (bt.Count == 0) return (false, false, none);
            bool on = bt.Any(r => r.State == RadioState.On);
            IReadOnlyList<string> names = none;
            if (on)
            {
                try
                {
                    var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
                    names = (await DeviceInformation.FindAllAsync(selector)).Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                }
                catch { /* the on/off state is still useful */ }
            }
            return (true, on, names);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Bluetooth status unavailable: {ex.GetType().Name}");
            return (false, false, Array.Empty<string>());
        }
    }
}
