using System.IO;

namespace Scalpel.E2E;

public sealed class LogReader
{
    private readonly string _path;
    public LogReader(string filePath) => _path = filePath;
    public string FilePath => _path;

    public static string DefaultLogDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scalpel", "logs");

    public static string? FindLatestLog(string logDir)
    {
        try
        {
            var dir = new DirectoryInfo(logDir);
            if (!dir.Exists) return null;
            return dir.GetFiles("scalpel-*.jsonl")
                      .OrderByDescending(f => f.LastWriteTimeUtc)
                      .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    private string[] ReadAllLinesShared()
    {
        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var lines = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null) lines.Add(line);
            return [.. lines];
        }
        catch { return []; }
    }

    public int Snapshot() => ReadAllLinesShared().Length;

    public IReadOnlyList<LogEntry> NewSince(int snapshot)
    {
        var all = ReadAllLinesShared();
        var result = new List<LogEntry>();
        for (int i = Math.Max(0, snapshot); i < all.Length; i++)
        {
            var e = LogEntry.Parse(all[i]);
            if (e != null) result.Add(e);
        }
        return result;
    }
}
