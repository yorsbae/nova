namespace Nova.Core.Launch;

public static class LaunchValidator
{
    static readonly string[] Allowed = { ".exe", ".lnk" };

    public static bool TryValidate(string? path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path)) { error = "No path was provided."; return false; }
        if (!System.IO.Path.IsPathRooted(path)) { error = "Path must be absolute."; return false; }
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (!Allowed.Contains(ext)) { error = $"File type '{ext}' is not allowed (use .exe or .lnk)."; return false; }
        if (!File.Exists(path)) { error = "File was not found."; return false; }
        return true;
    }
}
