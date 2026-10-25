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
}
