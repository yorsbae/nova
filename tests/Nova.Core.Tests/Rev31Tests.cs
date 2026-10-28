using Nova.Core.Notifications;
using Xunit;

namespace Nova.Core.Tests;

public class ToastTextTests
{
    [Theory]
    [InlineData("WhatsApp", "Budi", "Halo, jadi?", "WhatsApp · Budi: Halo, jadi?")]
    [InlineData("WhatsApp", "Budi", "", "WhatsApp · Budi")]
    [InlineData("", "", "", "Notifikasi · ada notifikasi baru")]
    public void Describes_toast(string app, string title, string body, string expected)
        => Assert.Equal(expected, AppAlerts.DescribeToast(app, title, body));

    [Fact]
    public void Long_text_is_cut_with_ellipsis()
    {
        var s = AppAlerts.DescribeToast("WhatsApp", "Budi", new string('x', 300), 40);
        Assert.Equal(40, s.Length);
        Assert.EndsWith("…", s);
    }
}
