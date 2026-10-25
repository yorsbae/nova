using System.Runtime.InteropServices;

namespace Nova.App.Integrations.Windows;

/// <summary>Master volume and mute of the default playback device through the supported Core Audio API.</summary>
public static class AudioService
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    static readonly Guid IidEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    /// <summary>Runs <paramref name="action"/> with the default speaker's volume interface. False if there is no audio device.</summary>
    static bool With(Func<IAudioEndpointVolume, bool> action)
    {
        object? enumeratorObj = null, volumeObj = null;
        IMMDevice? device = null;
        try
        {
            enumeratorObj = new MMDeviceEnumeratorComObject();
            var enumerator = (IMMDeviceEnumerator)enumeratorObj;
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device) != 0 || device is null) return false;
            var iid = IidEndpointVolume;
            if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out volumeObj) != 0 || volumeObj is null) return false;
            return action((IAudioEndpointVolume)volumeObj);
        }
        catch { return false; }
        finally
        {
            if (volumeObj is not null) Marshal.ReleaseComObject(volumeObj);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumeratorObj is not null) Marshal.ReleaseComObject(enumeratorObj);
        }
    }

    /// <summary>Volume 0..1 and mute state, or false when there is no output device.</summary>
    public static bool TryGet(out double volume, out bool muted)
    {
        double v = 0; bool m = false;
        var ok = With(ep =>
        {
            if (ep.GetMasterVolumeLevelScalar(out var level) != 0) return false;
            ep.GetMute(out m);
            v = level;
            return true;
        });
        volume = v; muted = m;
        return ok;
    }

    public static bool SetVolume(double volume)
    {
        var ctx = Guid.Empty;
        var level = (float)Math.Min(1, Math.Max(0, volume));
        return With(ep =>
        {
            var c = ctx;
            if (ep.SetMasterVolumeLevelScalar(level, ref c) != 0) return false;
            if (level > 0) { var c2 = ctx; ep.SetMute(false, ref c2); }   // turning the volume up un-mutes, like the Windows slider
            return true;
        });
    }

    public static bool ToggleMute()
    {
        var ctx = Guid.Empty;
        return With(ep =>
        {
            if (ep.GetMute(out var m) != 0) return false;
            var c = ctx;
            return ep.SetMute(!m, ref c) == 0;
        });
    }

    // ---- change notifications (hardware volume keys, other apps) ----

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [ClassInterface(ClassInterfaceType.None)]
    sealed class VolumeCallback : IAudioEndpointVolumeCallback
    {
        readonly Action<double, bool> _on;
        public VolumeCallback(Action<double, bool> on) => _on = on;

        public int OnNotify(IntPtr data)
        {
            try
            {
                // AUDIO_VOLUME_NOTIFICATION_DATA: GUID eventContext (16 bytes), BOOL muted, float masterVolume, ...
                bool muted = Marshal.ReadInt32(data, 16) != 0;
                float volume = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
                _on(volume, muted);
            }
            catch { /* never let an exception cross into the audio engine */ }
            return 0;
        }
    }

    /// <summary>Tells NOVA when the volume or mute changes, with no polling. The callback arrives on an audio thread.</summary>
    public sealed class Watcher : IDisposable
    {
        object? _enumeratorObj, _deviceObj, _volumeObj;
        IAudioEndpointVolume? _volume;
        VolumeCallback? _callback;
        IntPtr _callbackPtr;

        public bool Start(Action<double, bool> onChange)
        {
            try
            {
                _enumeratorObj = new MMDeviceEnumeratorComObject();
                var enumerator = (IMMDeviceEnumerator)_enumeratorObj;
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0 || device is null) return false;
                _deviceObj = device;
                var iid = IidEndpointVolume;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out _volumeObj) != 0 || _volumeObj is null) return false;
                _volume = (IAudioEndpointVolume)_volumeObj;
                _callback = new VolumeCallback(onChange);
                _callbackPtr = Marshal.GetComInterfaceForObject(_callback, typeof(IAudioEndpointVolumeCallback));
                if (_volume.RegisterControlChangeNotify(_callbackPtr) == 0) return true;
                Dispose();
                return false;
            }
            catch { Dispose(); return false; }
        }

        public void Dispose()
        {
            try { if (_volume is not null && _callbackPtr != IntPtr.Zero) _volume.UnregisterControlChangeNotify(_callbackPtr); } catch { }
            try { if (_callbackPtr != IntPtr.Zero) Marshal.Release(_callbackPtr); } catch { }
            _callbackPtr = IntPtr.Zero;
            _volume = null; _callback = null;
            foreach (var o in new[] { _volumeObj, _deviceObj, _enumeratorObj })
                try { if (o is not null) Marshal.ReleaseComObject(o); } catch { }
            _volumeObj = _deviceObj = _enumeratorObj = null;
        }
    }

    // ---- output level (for the music visualizer) ----

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    static readonly Guid IidMeter = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    /// <summary>Peak level (0..1) of what the default speakers are playing right now. Kept open only while the visualizer is visible.</summary>
    public sealed class Meter : IDisposable
    {
        object? _enumeratorObj, _deviceObj, _meterObj;
        IAudioMeterInformation? _meter;

        public bool Start()
        {
            try
            {
                _enumeratorObj = new MMDeviceEnumeratorComObject();
                if (((IMMDeviceEnumerator)_enumeratorObj).GetDefaultAudioEndpoint(0, 1, out var device) != 0 || device is null) return false;
                _deviceObj = device;
                var iid = IidMeter;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out _meterObj) != 0 || _meterObj is null) return false;
                _meter = (IAudioMeterInformation)_meterObj;
                return true;
            }
            catch { Dispose(); return false; }
        }

        public double Peak()
        {
            try { return _meter is not null && _meter.GetPeakValue(out var p) == 0 ? Math.Min(1, Math.Max(0, p)) : 0; }
            catch { return 0; }
        }

        public void Dispose()
        {
            _meter = null;
            foreach (var o in new[] { _meterObj, _deviceObj, _enumeratorObj })
                try { if (o is not null) Marshal.ReleaseComObject(o); } catch { }
            _meterObj = _deviceObj = _enumeratorObj = null;
        }
    }
}
