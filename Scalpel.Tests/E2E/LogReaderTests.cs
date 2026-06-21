using System.IO;
using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class LogReaderTests
{
    private static string WriteTemp(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scalpel-test-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string ClickLine(string name) =>
        $"{{\"ts\":\"2026-06-21T05:11:44.007Z\",\"level\":\"INFO\",\"cat\":\"UI\",\"event\":\"click\",\"msg\":\"{name}\"}}";

    [Fact]
    public void NewSince_ReturnsOnlyAppendedLines()
    {
        var path = WriteTemp(ClickLine("A"), ClickLine("B"));
        var reader = new LogReader(path);
        int snap = reader.Snapshot();
        Assert.Equal(2, snap);

        File.AppendAllLines(path, [ClickLine("C")]);
        var fresh = reader.NewSince(snap);
        Assert.Single(fresh);
        Assert.Equal("C", fresh[0].Msg);

        File.Delete(path);
    }

    [Fact]
    public void NewSince_SkipsUnparseableLines()
    {
        var path = WriteTemp(ClickLine("A"));
        var reader = new LogReader(path);
        int snap = reader.Snapshot();
        File.AppendAllLines(path, ["", "garbage", ClickLine("B")]);
        var fresh = reader.NewSince(snap);
        Assert.Single(fresh);
        Assert.Equal("B", fresh[0].Msg);
        File.Delete(path);
    }

    [Fact]
    public void FindLatestLog_PicksNewestByWriteTime()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var older = Path.Combine(dir, "scalpel-20260101-000000.jsonl");
        var newer = Path.Combine(dir, "scalpel-20260621-000000.jsonl");
        File.WriteAllText(older, ClickLine("old"));
        File.WriteAllText(newer, ClickLine("new"));
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 6, 21));

        Assert.Equal(newer, LogReader.FindLatestLog(dir));
        Directory.Delete(dir, true);
    }
}
