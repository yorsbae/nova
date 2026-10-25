using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

public enum QuickRadio { WiFi, Bluetooth }

/// <param name="NeedsPassword">The network is secured and Windows has no saved password for it.</param>
public sealed record RadioResult(bool Ok, string? Message = null, bool NeedsPassword = false);

public sealed record WifiNetworkInfo(string Ssid, int Signal, bool Secure, bool Connected);

public sealed record BluetoothDeviceInfo(string Id, string Name, bool Paired, bool Connected);

/// <summary>Switches the Wi-Fi and Bluetooth radios on and off with Windows' own radio API. Never throws.</summary>
public static class RadioService
{
    static RadioKind Map(QuickRadio r) => r == QuickRadio.WiFi ? RadioKind.WiFi : RadioKind.Bluetooth;

    /// <summary>true/false = on/off. null = this PC has no such radio (or Windows won't say).</summary>
    public static async Task<bool?> IsOnAsync(QuickRadio kind)
    {
        try
        {
            var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == Map(kind)).ToList();
            if (radios.Count == 0) return null;
            return radios.Any(r => r.State == RadioState.On);
        }
        catch (Exception ex) { FileLog.Write($"Radio state failed: {ex.GetType().Name}"); return null; }
    }

    public static async Task<RadioResult> SetAsync(QuickRadio kind, bool on)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed)
                return new(false, "Windows tidak mengizinkan NOVA mengatur radio. Cek Pengaturan › Privasi & keamanan › Radio.");
            bool any = false;
            foreach (var radio in (await Radio.GetRadiosAsync()).Where(r => r.Kind == Map(kind)))
            {
                any = true;
                var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
                if (result != RadioAccessStatus.Allowed) return new(false, "Windows menolak perubahan itu.");
            }
            return any ? new(true) : new(false, "Perangkat ini tidak punya radio tersebut.");
        }
        catch (Exception ex)
        {
            FileLog.Write($"Radio switch failed: {ex.GetType().Name}");
            return new(false, "Gagal mengubah radio.");
        }
    }
}

/// <summary>Finds and joins Wi-Fi networks through Windows' Wi-Fi API. Needs Windows' Location permission to scan (a Windows rule).</summary>
public sealed class WifiService
{
    WiFiAdapter? _adapter;
    readonly Dictionary<string, WiFiAvailableNetwork> _last = new(StringComparer.Ordinal);

    /// <summary>Name of the network we're on right now, or null.</summary>
    public static string? CurrentSsid()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p is { IsWlanConnectionProfile: true }) return p.WlanConnectionProfileDetails.GetConnectedSsid();
        }
        catch { /* no profile: not connected */ }
        return null;
    }

    async Task<WiFiAdapter?> AdapterAsync()
    {
        if (_adapter is not null) return _adapter;
        var access = await WiFiAdapter.RequestAccessAsync();
        if (access != WiFiAccessStatus.Allowed) return null;
        _adapter = (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault();
        return _adapter;
    }

    static int Percent(double rssi) => (int)Math.Clamp(2 * (rssi + 100), 0, 100);

    public async Task<(IReadOnlyList<WifiNetworkInfo> Networks, string? Error)> ScanAsync()
    {
        try
        {
            var ad = await AdapterAsync();
            if (ad is null)
                return (Array.Empty<WifiNetworkInfo>(), "Windows belum mengizinkan NOVA memindai Wi-Fi. Nyalakan Lokasi di Pengaturan › Privasi & keamanan › Lokasi.");
            await ad.ScanAsync();
            _last.Clear();
            foreach (var n in ad.NetworkReport.AvailableNetworks)
            {
                var ssid = n.Ssid;
                if (string.IsNullOrWhiteSpace(ssid)) continue;
                if (!_last.TryGetValue(ssid, out var best) || n.NetworkRssiInDecibelMilliwatts > best.NetworkRssiInDecibelMilliwatts) _last[ssid] = n;
            }
            var current = CurrentSsid();
            var list = _last.Values
                .Select(n => new WifiNetworkInfo(n.Ssid, Percent(n.NetworkRssiInDecibelMilliwatts),
                    n.SecuritySettings.NetworkAuthenticationType is not (NetworkAuthenticationType.None or NetworkAuthenticationType.Open80211),
                    string.Equals(n.Ssid, current, StringComparison.Ordinal)))
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal)
                .Take(12).ToList();
            return (list, null);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Wi-Fi scan failed: {ex.GetType().Name}");
            return (Array.Empty<WifiNetworkInfo>(), "Pemindaian Wi-Fi gagal.");
        }
    }

    /// <summary>Joins a network. A network Windows already knows connects without a password; otherwise NeedsPassword says to ask for one.</summary>
    public async Task<RadioResult> ConnectAsync(string ssid, bool secure, string? password)
    {
        try
        {
            if (password is null && await Task.Run(() => TryKnownProfile(ssid))) return new(true);
            if (password is null && secure) return new(false, null, NeedsPassword: true);

            var ad = await AdapterAsync();
            if (ad is null || !_last.TryGetValue(ssid, out var net)) return new(false, "Jaringan tidak ditemukan. Tekan Cari lagi.");
            var result = password is null
                ? await ad.ConnectAsync(net, WiFiReconnectionKind.Automatic)
                : await ad.ConnectAsync(net, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = password });
            return result.ConnectionStatus == WiFiConnectionStatus.Success
                ? new(true)
                : new(false, result.ConnectionStatus == WiFiConnectionStatus.InvalidCredential ? "Kata sandi salah." : "Tidak bisa tersambung ke jaringan itu.");
        }
        catch (Exception ex)
        {
            FileLog.Write($"Wi-Fi connect failed: {ex.GetType().Name}");
            return new(false, "Gagal menyambung.");
        }
    }

    public void Disconnect()
    {
        try { _adapter?.Disconnect(); } catch (Exception ex) { FileLog.Write($"Wi-Fi disconnect failed: {ex.GetType().Name}"); }
    }

    /// <summary>Asks Windows to connect with a profile it already saved (the profile is named like the network).</summary>
    static bool TryKnownProfile(string ssid)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            psi.ArgumentList.Add("wlan");
            psi.ArgumentList.Add("connect");
            psi.ArgumentList.Add("name=" + ssid.Replace("\"", ""));
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(6000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}

/// <summary>Lists, finds, pairs and connects Bluetooth devices with Windows' device API.</summary>
public sealed class BluetoothService
{
    // "Bluetooth classic" association endpoints: headphones, speakers, keyboards, phones...
    const string Aqs = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
    static readonly string[] Props = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPaired" };

    static BluetoothDeviceInfo Convert(DeviceInformation d)
    {
        bool connected = d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is bool b && b;
        return new BluetoothDeviceInfo(d.Id, d.Name, d.Pairing.IsPaired, connected);
    }

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> PairedAsync()
    {
        try
        {
            var found = await DeviceInformation.FindAllAsync(Aqs + " AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True",
                Props, DeviceInformationKind.AssociationEndpoint);
            return found.Where(d => !string.IsNullOrWhiteSpace(d.Name)).Select(Convert)
                        .OrderByDescending(d => d.Connected).ThenBy(d => d.Name).Take(10).ToList();
        }
        catch (Exception ex) { FileLog.Write($"Bluetooth list failed: {ex.GetType().Name}"); return Array.Empty<BluetoothDeviceInfo>(); }
    }

    /// <summary>Watches for devices in range for a few seconds. Devices that are already paired are left out.</summary>
    public async Task<IReadOnlyList<BluetoothDeviceInfo>> DiscoverAsync(int seconds, CancellationToken ct)
    {
        var found = new Dictionary<string, BluetoothDeviceInfo>();
        DeviceWatcher? watcher = null;
        try
        {
            watcher = DeviceInformation.CreateWatcher(Aqs, Props, DeviceInformationKind.AssociationEndpoint);
            watcher.Added += (_, d) =>
            {
                if (string.IsNullOrWhiteSpace(d.Name) || d.Pairing.IsPaired) return;
                lock (found) found[d.Id] = Convert(d);
            };
            watcher.Start();
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), ct); } catch (OperationCanceledException) { }
        }
        catch (Exception ex) { FileLog.Write($"Bluetooth discovery failed: {ex.GetType().Name}"); }
        finally
        {
            try
            {
                if (watcher is not null && watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
            }
            catch { }
        }
        lock (found) return found.Values.OrderBy(d => d.Name).Take(10).ToList();
    }

    public async Task<RadioResult> PairAsync(string id)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(id, Props, DeviceInformationKind.AssociationEndpoint);
            if (info.Pairing.IsPaired) return new(true);
            var result = await info.Pairing.PairAsync();
            return result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired
                ? new(true)
                : new(false, result.Status == DevicePairingResultStatus.RequiredHandlerNotRegistered
                    ? "Perangkat ini butuh kode pairing. Pasangkan lewat Pengaturan Windows."
                    : "Gagal memasangkan perangkat. Pastikan ia dalam mode pairing.");
        }
        catch (Exception ex)
        {
            FileLog.Write($"Bluetooth pair failed: {ex.GetType().Name}");
            return new(false, "Gagal memasangkan perangkat.");
        }
    }

    /// <summary>Asks Windows to connect to a paired device. Asking for its services makes Windows open the link; not every device accepts.</summary>
    public async Task<RadioResult> ConnectAsync(string id)
    {
        try
        {
            using var device = await BluetoothDevice.FromIdAsync(id);
            if (device is null) return new(false, "Perangkat tidak ditemukan.");
            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected) return new(true);
            await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
            await Task.Delay(2500);
            return device.ConnectionStatus == BluetoothConnectionStatus.Connected
                ? new(true)
                : new(false, "Belum tersambung. Pastikan perangkat menyala dan dekat; jika tetap gagal, sambungkan lewat Pengaturan Windows.");
        }
        catch (Exception ex)
        {
            FileLog.Write($"Bluetooth connect failed: {ex.GetType().Name}");
            return new(false, "Gagal menyambung ke perangkat.");
        }
    }
}
