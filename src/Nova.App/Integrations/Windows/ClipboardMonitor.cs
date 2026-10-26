using System.IO;
using System.Windows;
using System.Windows.Interop;
using Nova.Core.Clipboard;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// Opt-in clipboard history (text, pictures and copied files) using the standard clipboard-format listener (no polling, no hooks into other apps).
/// Respects the Windows opt-out flags that password managers set. History lives in memory only and is cleared on stop.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    readonly Window _host;
    HwndSource? _source;
    IntPtr _handle;
    bool _running;
    int _pending;

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
        // Apps often announce a copy a moment before the data is ready, and one copy can announce itself several times:
        // wait a little, and let only the newest announcement do the reading.
        int ticket = Interlocked.Increment(ref _pending);
        await Task.Delay(60);
        if (!_running || ticket != Volatile.Read(ref _pending)) return;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var data = System.Windows.Clipboard.GetDataObject();
                if (data is null || IsExcluded(data)) return;

                // Copied files (Explorer) first, then text, then a picture (screenshot, "Copy image").
                if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                { History.AddFiles(files); return; }
                if (data.GetDataPresent(DataFormats.UnicodeText))
                { History.Add(data.GetData(DataFormats.UnicodeText) as string); return; }
                if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    var img = System.Windows.Clipboard.GetImage();
                    var png = ClipboardTools.Encode(img);
                    if (png is not null && img is not null) History.AddImage(png, img.PixelWidth, img.PixelHeight);
                    return;
                }
                return;
            }
            catch { await Task.Delay(50); }   // clipboard briefly locked by another app
        }
    }

    /// <summary>Honors the formats Windows and password managers use to say "don't record this". Throws if the clipboard can't be read (the caller retries).</summary>
    static bool IsExcluded(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return true;
        if (!data.GetDataPresent("CanIncludeInClipboardHistory")) return false;

        // A DWORD: 0 = keep this out of history, 1 = fine. A format that cannot be read counts as "fine": apps that set it to 1
        // (many do) must not be dropped just because .NET hands the value over in an unexpected shape.
        return data.GetData("CanIncludeInClipboardHistory") switch
        {
            MemoryStream ms => IsAllZero(ms.ToArray()),
            byte[] bytes => IsAllZero(bytes),
            int value => value == 0,
            _ => false
        };
    }

    static bool IsAllZero(byte[] bytes) => bytes.Length > 0 && bytes.All(b => b == 0);

    public void Dispose() => Stop();
}
