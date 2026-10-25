using System.IO;
using System.Text.Json;
using Nova.Core.Configuration;
using Nova.Core.Logging;

namespace Nova.App.Integrations.Windows;

/// <summary>
/// The two optional, reversible changes NOVA can make to Windows itself: auto-hide the taskbar and use dark mode.
/// The original values are written to disk BEFORE anything is changed, so they are put back on exit, when the
/// option is switched off, or on the next start if NOVA was killed.
/// </summary>
public sealed class SystemChangesService
{
    sealed class State
    {
        public bool TaskbarSaved { get; set; }
        public bool TaskbarAutoHide { get; set; }
        public bool ThemeSaved { get; set; }
        public int? AppsUseLightTheme { get; set; }
        public int? SystemUsesLightTheme { get; set; }
    }

    readonly string _path;
    State _state = new();

    public SystemChangesService(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA", "system-restore.json");
        try { if (File.Exists(_path)) _state = JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new(); }
        catch { _state = new(); }
    }

    /// <summary>Brings Windows in line with the settings: applies what is switched on, restores what is switched off.</summary>
    public void Apply(NovaSettings s)
    {
        try
        {
            if (s.HideTaskbar)
            {
                if (!_state.TaskbarSaved)
                {
                    // Remember the original auto-hide state first. If it can't be read, assume "off" (Windows' default)
                    // rather than leaving the taskbar on screen.
                    _state.TaskbarSaved = true;
                    _state.TaskbarAutoHide = TaskbarService.GetAutoHide() ?? false;
                    Save();
                }
                TaskbarService.SetAutoHide(true);
                TaskbarService.SetHidden(true);
            }
            else RestoreTaskbar();

            if (s.WindowsDarkMode)
            {
                if (!_state.ThemeSaved)
                {
                    var (apps, system) = WindowsThemeService.Read();
                    _state.ThemeSaved = true;
                    _state.AppsUseLightTheme = apps;
                    _state.SystemUsesLightTheme = system;
                    Save();
                }
                WindowsThemeService.SetDark(true);
            }
            else RestoreTheme();
        }
        catch (Exception ex) { FileLog.Write($"System changes failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>True while NOVA has the Windows taskbar hidden.</summary>
    public bool TaskbarHiddenByNova => _state.TaskbarSaved;

    /// <summary>Cheap check, called every second or so: if Windows brought the taskbar back, hide it again.</summary>
    public void KeepTaskbarHidden()
    {
        try { if (_state.TaskbarSaved) TaskbarService.EnforceHidden(); }
        catch { /* best effort */ }
    }

    /// <summary>Called on exit: leaves Windows exactly as NOVA found it.</summary>
    public void RestoreAll()
    {
        try { RestoreTaskbar(); RestoreTheme(); }
        catch (Exception ex) { FileLog.Write($"System restore failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    void RestoreTaskbar()
    {
        if (!_state.TaskbarSaved) return;
        TaskbarService.SetHidden(false);
        TaskbarService.SetAutoHide(_state.TaskbarAutoHide);
        _state.TaskbarSaved = false;
        Save();
    }

    void RestoreTheme()
    {
        if (!_state.ThemeSaved) return;
        WindowsThemeService.Restore(_state.AppsUseLightTheme, _state.SystemUsesLightTheme);
        _state.ThemeSaved = false;
        Save();
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_state));
        }
        catch { /* best effort: the worst case is a manual restore from Windows Settings */ }
    }
}
