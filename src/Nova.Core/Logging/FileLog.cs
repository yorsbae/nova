namespace Nova.Core.Logging;

/// <summary>Local-only, best-effort logging. Never logs user content; never throws.</summary>
public static class FileLog
{
    static readonly object Gate = new();
    public static string PathOnDisk => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA", "nova.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk)!);
                var fi = new FileInfo(PathOnDisk);
                if (fi.Exists && fi.Length > 512 * 1024) fi.MoveTo(PathOnDisk + ".old", true);
                File.AppendAllText(PathOnDisk, $"{DateTime.Now:s} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
