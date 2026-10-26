using System.Collections.Specialized;
using System.IO;
using System.Windows.Media.Imaging;
using Nova.Core.Clipboard;

namespace Nova.App.Integrations.Windows;

/// <summary>Turns clipboard pictures into small PNG bytes and back, and puts a remembered item back on the clipboard.</summary>
internal static class ClipboardTools
{
    /// <summary>Most pixels kept for one picture (about a 6000 × 6000 screenshot). Bigger ones are skipped, not shrunk.</summary>
    public const long MaxPixels = 36_000_000;

    public static byte[]? Encode(BitmapSource? source)
    {
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0 || (long)source.PixelWidth * source.PixelHeight > MaxPixels) return null;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Reads PNG bytes. With <paramref name="decodeWidth"/> the picture is decoded small (for thumbnails), which saves memory.</summary>
    public static BitmapImage? Decode(byte[]? png, int decodeWidth = 0)
    {
        if (png is null || png.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(png);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // read everything now, so the stream can be closed
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    /// <summary>Puts a remembered item back on the Windows clipboard. False if it could not be (clipboard busy, files gone).</summary>
    public static bool Restore(ClipEntry entry)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                switch (entry.Kind)
                {
                    case ClipKind.Image:
                        var image = Decode(entry.Png);
                        if (image is null) return false;
                        System.Windows.Clipboard.SetImage(image);
                        return true;
                    case ClipKind.Files:
                        var list = new StringCollection();
                        foreach (var path in entry.Paths)
                            if (File.Exists(path) || Directory.Exists(path)) list.Add(path);
                        if (list.Count == 0) return false;
                        System.Windows.Clipboard.SetFileDropList(list);
                        return true;
                    default:
                        System.Windows.Clipboard.SetText(entry.Text);
                        return true;
                }
            }
            catch { Thread.Sleep(40); }   // clipboard briefly locked by another app
        }
        return false;
    }
}
