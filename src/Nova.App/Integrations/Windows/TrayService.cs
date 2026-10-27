using WinForms = System.Windows.Forms;

namespace Nova.App.Integrations.Windows;

/// <summary>Notification-area icon: an always-reachable way to open, configure, reset or exit NOVA.</summary>
public sealed class TrayService : IDisposable
{
    readonly WinForms.NotifyIcon _icon;

    public TrayService(Action toggleIsland, Action openDock, Action openClipboard, Action openSettings, Action reset, Action exit, Action? hideShowIsland = null)
    {
        var menu = new WinForms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkMenuColors()),
            BackColor = System.Drawing.Color.FromArgb(0x1B, 0x1B, 0x1F),
            ForeColor = System.Drawing.Color.White,
            Font = new System.Drawing.Font("Segoe UI", 9.5f)
        };
        menu.Items.Add("Open / close island", null, (_, _) => toggleIsland());
        if (hideShowIsland is not null) menu.Items.Add("Hide / show island", null, (_, _) => hideShowIsland());
        menu.Items.Add("Focus dock", null, (_, _) => openDock());
        menu.Items.Add("Clipboard history", null, (_, _) => openClipboard());
        menu.Items.Add("Settings…", null, (_, _) => openSettings());
        menu.Items.Add("Reset layout", null, (_, _) => reset());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit NOVA", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "NOVA",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => toggleIsland();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>Dark colours for the tray menu so it matches the rest of NOVA instead of the classic Windows menu.</summary>
internal sealed class DarkMenuColors : WinForms.ProfessionalColorTable
{
    static readonly System.Drawing.Color Back = System.Drawing.Color.FromArgb(0x1B, 0x1B, 0x1F);
    static readonly System.Drawing.Color Hot = System.Drawing.Color.FromArgb(0x3A, 0x3A, 0x44);
    static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(0x4A, 0x4A, 0x54);

    public override System.Drawing.Color ToolStripDropDownBackground => Back;
    public override System.Drawing.Color ImageMarginGradientBegin => Back;
    public override System.Drawing.Color ImageMarginGradientMiddle => Back;
    public override System.Drawing.Color ImageMarginGradientEnd => Back;
    public override System.Drawing.Color MenuBorder => Line;
    public override System.Drawing.Color MenuItemBorder => Hot;
    public override System.Drawing.Color MenuItemSelected => Hot;
    public override System.Drawing.Color MenuItemSelectedGradientBegin => Hot;
    public override System.Drawing.Color MenuItemSelectedGradientEnd => Hot;
    public override System.Drawing.Color MenuItemPressedGradientBegin => Hot;
    public override System.Drawing.Color MenuItemPressedGradientMiddle => Hot;
    public override System.Drawing.Color MenuItemPressedGradientEnd => Hot;
    public override System.Drawing.Color SeparatorDark => Line;
    public override System.Drawing.Color SeparatorLight => Back;
}
