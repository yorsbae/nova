using System.IO;
using System.Windows;
using System.Windows.Interop;
using Nova.Core.Clipboard;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Opt-in, text-only clipboard history using the standard clipboard-format listener (no polling, no hooks into other apps).
/// Respects the Windows opt-out flags that password managers set. History lives in memory only and is cleared on stop.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    readonly Window _host;
    HwndSource? _source;
    IntPtr _handle;
    bool _running;

    public ClipboardHistory History { get; }

    public ClipboardMonitor(Window host, int maxItems) { _host = host; History = new ClipboardHistory(maxItems); }

    public bool Start()
    {
        if (_running) return true;
        _handle = new WindowInteropHelper(_host).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(Hook);
        _running = NativeMethods.AddClipboardFormatListener(_handle);
        return _running;
    }

    public void Stop()
    {
        if (_running) NativeMethods.RemoveClipboardFormatListener(_handle);
        _source?.RemoveHook(Hook);
        _source = null; _running = false;
        History.Clear();   // privacy: opting out wipes everything
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) _ = CaptureAsync();   // not marked handled: other listeners still get it
        return IntPtr.Zero;
    }

    async Task CaptureAsync()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var data = System.Windows.Clipboard.GetDataObject();
                if (data is null || IsExcluded(data) || !data.GetDataPresent(DataFormats.UnicodeText)) return;
                History.Add(data.GetData(DataFormats.UnicodeText) as string);
                return;
            }
            catch { await Task.Delay(40); }   // clipboard briefly locked by another app
        }
    }

    /// <summary>Honors the formats Windows and password managers use to say "don't record this".</summary>
    static bool IsExcluded(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return true;
            if (data.GetDataPresent("CanIncludeInClipboardHistory"))
                return data.GetData("CanIncludeInClipboardHistory") is not MemoryStream ms || ms.ToArray().All(b => b == 0);
            return false;
        }
        catch { return true; }   // can't tell → don't record
    }

    public void Dispose() => Stop();
}
