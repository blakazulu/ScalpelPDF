using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Scalpel.E2E;

public static class Reporter
{
    public static string ToMarkdown(RunReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scalpel E2E Test Report");
        sb.AppendLine();
        sb.AppendLine($"**Total:** {report.Total()} &nbsp; **Passed:** {report.Passed()} &nbsp; **Failed:** {report.Failed()} &nbsp; **Untested controls:** {report.UntestedControls.Count}");
        sb.AppendLine();
        sb.AppendLine("## Summary by suite");
        sb.AppendLine();
        sb.AppendLine("| Suite | Total | Passed | Failed |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var s in report.Suites())
        {
            int total = report.TotalFor(s);
            int failed = report.FailedFor(s);
            sb.AppendLine($"| {s} | {total} | {total - failed} | {failed} |");
        }
        sb.AppendLine();

        var failures = report.Results.Where(r => r.Outcome == Outcome.Fail).ToList();
        sb.AppendLine($"## Failures ({failures.Count})");
        sb.AppendLine();
        if (failures.Count == 0)
        {
            sb.AppendLine("_None._");
        }
        else
        {
            foreach (var f in failures)
            {
                sb.AppendLine($"### [{f.Suite}] {f.Action}");
                sb.AppendLine();
                sb.AppendLine($"**Reason:** {f.FailReason}");
                sb.AppendLine();
                if (f.LogContext.Count > 0)
                {
                    sb.AppendLine("Log context:");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    foreach (var e in f.LogContext)
                        sb.AppendLine($"{e.Ts:O} {e.Level} {e.Cat}/{e.Event} {e.Msg}");
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine($"## Warnings ({report.Warnings.Count})");
        sb.AppendLine();
        if (report.Warnings.Count == 0)
            sb.AppendLine("_None._");
        else
            foreach (var w in report.Warnings)
                sb.AppendLine($"- {w}");
        sb.AppendLine();

        sb.AppendLine("## Controls never exercised");
        sb.AppendLine();
        if (report.UntestedControls.Count == 0)
            sb.AppendLine("_None — full coverage._");
        else
            foreach (var c in report.UntestedControls)
                sb.AppendLine($"- {c}");

        return sb.ToString();
    }

    public static string ToJson(RunReport report)
    {
        var payload = new
        {
            total = report.Total(),
            passed = report.Passed(),
            failed = report.Failed(),
            untestedControls = report.UntestedControls,
            warnings = report.Warnings,
            suites = report.Suites().Select(s => new
            {
                name = s,
                total = report.TotalFor(s),
                failed = report.FailedFor(s),
            }),
            results = report.Results.Select(r => new
            {
                suite = r.Suite,
                action = r.Action,
                outcome = r.Outcome.ToString(),
                failReason = r.FailReason,
                logContext = r.LogContext.Select(e => new
                {
                    ts = e.Ts, level = e.Level, cat = e.Cat, ev = e.Event, msg = e.Msg,
                }),
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static (string mdPath, string jsonPath) Write(RunReport report, string dir, string timestamp)
    {
        Directory.CreateDirectory(dir);
        string md = Path.Combine(dir, $"test-report-{timestamp}.md");
        string json = Path.Combine(dir, $"test-report-{timestamp}.json");
        File.WriteAllText(md, ToMarkdown(report));
        File.WriteAllText(json, ToJson(report));
        return (md, json);
    }
}
