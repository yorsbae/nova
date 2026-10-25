using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nova.App.Integrations.Windows;

/// <summary>Shell icon of a file, folder or app, as a frozen WPF image. Returns null instead of throwing.</summary>
internal static class IconLoader
{
    public static BitmapSource? Load(string path)
    {
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico is null) return null;
            var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        catch { return null; }
    }

    /// <summary>
    /// A sharper icon for the dock, whose icons grow when the pointer is near. Programs, libraries and .ico files give a real
    /// high-resolution icon; shortcuts and everything else fall back to the standard 32 px icon.
    /// </summary>
    public static BitmapSource? LoadSized(string path, int px)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(path);
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                var h = NativeMethods.ExtractIconSized(path, px);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        return bs;
                    }
                    finally { NativeMethods.DestroyIcon(h); }
                }
            }
        }
        catch { /* fall back below */ }
        return Load(path);
    }
}
