using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nova.Core.Configuration;

public sealed record LoadResult(NovaSettings Settings, string? Warning);

public sealed class SettingsStore
{
    public const long MaxImportBytes = 1024 * 1024;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,   // NaN coordinates round-trip
        Converters = { new JsonStringEnumConverter() }
    };

    readonly string _path;
    public SettingsStore(string? path = null) => _path = path ?? DefaultPath;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA", "settings.json");

    public string Path => _path;
    public string BackupPath => _path + ".bak";

    /// <summary>Never throws. Missing/corrupt files fall back to defaults; corrupt files are preserved as .bad.</summary>
    public LoadResult Load()
    {
        try
        {
            if (!File.Exists(_path)) return new(new NovaSettings(), null);
            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<NovaSettings>(json, Options);
            return new(SettingsValidator.Normalize(parsed), null);
        }
        catch (Exception ex)
        {
            TryPreserveCorrupt();
            return new(new NovaSettings(), $"Settings could not be read and were reset: {ex.Message}");
        }
    }

    /// <summary>Atomic save (temp file + replace) keeping one previous generation as .bak. Returns false instead of throwing.</summary>
    public bool Save(NovaSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, Serialize(settings));
            try { if (File.Exists(_path)) File.Copy(_path, BackupPath, overwrite: true); } catch { /* backup is best effort */ }
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    public NovaSettings Reset()
    {
        var defaults = new NovaSettings();
        Save(defaults);
        return defaults;
    }

    /// <summary>Writes a portable backup of the given settings to any path. Returns an error message or null.</summary>
    public static string? Export(string path, NovaSettings settings)
    {
        try { File.WriteAllText(path, Serialize(settings)); return null; }
        catch (Exception ex) { return $"Couldn't write the backup: {ex.Message}"; }
    }

    /// <summary>Reads a backup. Never touches the live settings file; the result is validated and normalized.</summary>
    public static bool TryImport(string path, out NovaSettings? settings, out string? error)
    {
        settings = null; error = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { error = "The file was not found."; return false; }
            if (info.Length > MaxImportBytes) { error = "The file is too large to be a NOVA backup."; return false; }
            var parsed = JsonSerializer.Deserialize<NovaSettings>(File.ReadAllText(path), Options);
            if (parsed is null) { error = "The file is empty."; return false; }
            settings = SettingsValidator.Normalize(parsed);
            return true;
        }
        catch (Exception ex) { error = $"This isn't a valid NOVA backup: {ex.Message}"; return false; }
    }

    static string Serialize(NovaSettings s) => JsonSerializer.Serialize(SettingsValidator.Normalize(s), Options);

    void TryPreserveCorrupt()
    {
        try { if (File.Exists(_path)) File.Move(_path, _path + ".bad", overwrite: true); } catch { }
    }
}
