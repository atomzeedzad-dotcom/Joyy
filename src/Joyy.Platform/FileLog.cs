using System.Globalization;

namespace Joyy.Platform;

/// <summary>Daily rolling text log in %LOCALAPPDATA%\Joyy\logs, keeping the last 7 files. Thread-safe.</summary>
public sealed class FileLog
{
    const int KeepFiles = 7;

    readonly object _gate = new();

    public FileLog(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Joyy", "logs");
        System.IO.Directory.CreateDirectory(Directory);
        Prune();
    }

    public string Directory { get; }

    public void Write(string level, string message)
    {
        // Invariant culture: a Thai-locale PC would otherwise write Buddhist-era years (2569).
        var now = DateTime.Now;
        var inv = CultureInfo.InvariantCulture;
        string line = $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff", inv)} [{level}] {message}{Environment.NewLine}";
        string file = Path.Combine(Directory, $"joyy-{now.ToString("yyyyMMdd", inv)}.log");
        lock (_gate)
        {
            try { File.AppendAllText(file, line); }
            catch (IOException) { /* logging must never take the app down */ }
        }
    }

    void Prune()
    {
        try
        {
            foreach (var old in new DirectoryInfo(Directory).GetFiles("joyy-*.log")
                         .OrderByDescending(f => f.Name).Skip(KeepFiles))
                old.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
