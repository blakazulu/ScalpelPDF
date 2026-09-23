using System.Collections.Concurrent;

namespace Scalpel.E2E;

/// <summary>
/// One isolated app instance and everything bound to it: its own Scalpel process (via the
/// driver), its own corpus copy, its own session-log reader/runner, and its own result bucket.
/// The only state shared between instances is the machine-wide foreground/cursor — arbitrated
/// by <see cref="AppDriver.ForegroundGate"/>.
/// </summary>
public sealed class InstanceContext : IDisposable
{
    public int Index { get; }
    public AppDriver Driver { get; }
    public ActionRunner Runner { get; }
    public string OpenWith { get; }
    public string HebrewPath { get; }
    public string MissingFontPath { get; }
    public RunReport Report { get; } = new();

    public InstanceContext(int index, AppDriver driver, ActionRunner runner,
        string openWith, string hebrewPath, string missingFontPath)
    {
        Index = index; Driver = driver; Runner = runner;
        OpenWith = openWith; HebrewPath = hebrewPath; MissingFontPath = missingFontPath;
    }

    public void Dispose() { try { Driver.Dispose(); } catch { } }
}

/// <summary>
/// Runs the selected suites concurrently across a pool of isolated app instances. Suites are
/// independent jobs pulled from a shared queue by per-instance workers; each instance runs its
/// jobs sequentially (app state is per-instance and must stay serial), but instances run in
/// parallel. Physical clicks/typing serialize on <see cref="AppDriver.ForegroundGate"/>;
/// everything else (UIA Invoke clicks, UIA/log/PdfPig reads) overlaps freely.
/// </summary>
public static class ParallelOrchestrator
{
    public static int DefaultInstances(int jobCount) =>
        Math.Max(1, Math.Min(jobCount, Math.Max(2, Environment.ProcessorCount / 2)));

    public static RunReport Run(string appPath, IReadOnlyList<string> suites, int seed, int instances)
    {
        var jobs = new ConcurrentQueue<(string Name, Action<InstanceContext> Run)>();
        foreach (var s in suites) jobs.Enqueue(MakeJob(s, seed));

        int k = Math.Max(1, Math.Min(instances, jobs.Count));
        Console.WriteLine($"[parallel] {jobs.Count} suite(s) across {k} instance(s) " +
                          $"(cores={Environment.ProcessorCount})");

        // Launch instances sequentially so their foreground-grabbing startup doesn't race.
        var pool = new List<InstanceContext>();
        for (int i = 0; i < k; i++)
        {
            Console.WriteLine($"[parallel] launching instance {i}...");
            pool.Add(LaunchInstance(appPath, i));
        }

        // Each worker pulls jobs until the queue drains and runs them on its own instance.
        var workers = pool.Select(ctx => Task.Run(() =>
        {
            while (jobs.TryDequeue(out var job))
            {
                Console.WriteLine($"[instance {ctx.Index}] {job.Name}...");
                ctx.Driver.CurrentSuite = job.Name;
                try { ctx.Driver.ResetToBaseState(); } catch { }
                try { job.Run(ctx); }
                catch (Exception ex)
                {
                    ctx.Report.Results.Add(new ActionResult(job.Name, "orchestrator",
                        Outcome.Fail, $"job threw: {ex.Message}", Array.Empty<LogEntry>()));
                }
            }
        })).ToArray();
        Task.WaitAll(workers);

        // Merge per-instance reports into one (single-threaded — no locking needed here).
        // Each instance is closed first so how its close went is part of the report.
        var merged = new RunReport();
        foreach (var ctx in pool)
        {
            try { ctx.Driver.Shutdown(); } catch { }
            merged.Results.AddRange(ctx.Report.Results);
            ctx.Driver.AddCloseEventsTo(merged);
            foreach (var u in ctx.Report.UntestedControls)
                if (!merged.UntestedControls.Contains(u)) merged.UntestedControls.Add(u);
        }
        foreach (var ctx in pool) ctx.Dispose();
        return merged;
    }

    private static InstanceContext LaunchInstance(string appPath, int i)
    {
        string corpusDir = Path.Combine(Path.GetTempPath(), $"scalpel-e2e-corpus-{i}");
        var corpus = Corpus.Generate(corpusDir);
        string openWith = corpus.First(c => c.Key == "simple-1p").Path;
        string hebrew = corpus.FirstOrDefault(c => c.Key == "hebrew-1p")?.Path ?? "";
        string missing = corpus.FirstOrDefault(c => c.Key == "missingfont-1p")?.Path ?? "";

        // Fresh private log dir so ResolveLogPath finds only THIS instance's session log.
        string logDir = Path.Combine(Path.GetTempPath(), $"scalpel-e2e-logs-{i}");
        try { if (Directory.Exists(logDir)) Directory.Delete(logDir, recursive: true); } catch { }
        Directory.CreateDirectory(logDir);

        var driver = AppDriver.Launch(appPath, openWith, logDir);
        System.Threading.Thread.Sleep(800); // let app.start + open.success flush
        string? logPath = driver.ResolveLogPath();
        var reader = new LogReader(logPath ?? Path.Combine(logDir, "missing.jsonl"));
        var runner = new ActionRunner(driver, reader, openWith);

        return new InstanceContext(i, driver, runner, openWith, hebrew, missing);
    }

    private static (string, Action<InstanceContext>) MakeJob(string suite, int seed) => suite switch
    {
        "singles"  => ("singles",  ctx => SinglesSuite.Run(ctx.Driver, ctx.Runner, ctx.Report)),
        "journeys" => ("journeys", ctx => JourneysSuite.Run(ctx.Driver, ctx.Runner, ctx.Report)),
        "pairwise" => ("pairwise", ctx => PairwiseSuite.Run(ctx.Driver, ctx.Runner, ctx.Report)),
        "monkey"   => ("monkey",   ctx => MonkeySuite.Run(ctx.Driver, ctx.Runner, ctx.Report, seed)),
        "fonts"    => ("fonts",    ctx => FontHebrewSuite.Run(ctx.Driver, ctx.Runner, ctx.Report,
                                          ctx.OpenWith, ctx.HebrewPath, ctx.MissingFontPath)),
        _          => (suite, _ => { }),
    };
}
