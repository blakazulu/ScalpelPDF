using Scalpel.E2E;

internal static class Program
{
    private static int Main(string[] args)
    {
        string suite = ArgVal(args, "--suite") ?? "all";
        string? appPath = ArgVal(args, "--app") ?? FindDefaultApp();
        string reportDir = ArgVal(args, "--report-dir") ?? Path.Combine(Directory.GetCurrentDirectory(), "e2e-reports");
        int seed = int.TryParse(ArgVal(args, "--seed"), out var s) ? s : 1234;
        string stamp = ArgVal(args, "--stamp") ?? "run"; // caller can pass a timestamp; scripts can't use DateTime.Now

        if (appPath == null || !File.Exists(appPath))
        {
            Console.Error.WriteLine("Scalpel.exe not found. Pass --app <path>.");
            return 2;
        }

        // Resolve to a full path with normalized separators: a relative or
        // forward-slash --app passes File.Exists but CreateProcess (used by
        // FlaUI's launch with UseShellExecute=false) cannot find it.
        appPath = Path.GetFullPath(appPath);

        // 1. Fixtures.
        string corpusDir = Path.Combine(Path.GetTempPath(), "scalpel-e2e-corpus");
        var corpus = Corpus.Generate(corpusDir);
        string openWith = corpus.First(c => c.Key == "simple-1p").Path;
        string hebrewPath = corpus.FirstOrDefault(c => c.Key == "hebrew-1p")?.Path ?? "";
        string missingFontPath = corpus.FirstOrDefault(c => c.Key == "missingfont-1p")?.Path ?? "";

        // 2. Decide which suites to run. --suite accepts "all" or a comma-separated subset
        //    (e.g. --suite journeys,pairwise) so a fast/bounded run is possible.
        bool all = suite == "all";
        var requested = suite.Split(',')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var selected = new[] { "singles", "journeys", "pairwise", "monkey", "fonts", "save" }
            .Where(s => all || requested.Contains(s)).ToList();
        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"Unknown --suite '{suite}'. Use singles|journeys|pairwise|monkey|fonts|save|all, or a comma-separated subset.");
            return 2;
        }

        // 2/3. Run the selected suites. Parallel mode (default for a full run, or --parallel)
        // spreads the suites across a pool of isolated app instances; only physical clicks/typing
        // take the shared foreground (AppDriver.ForegroundGate). --sequential forces the classic
        // one-instance path; a single-suite run is inherently sequential (nothing to spread).
        bool sequential = args.Contains("--sequential");
        bool parallelFlag = args.Contains("--parallel");
        bool useParallel = !sequential && (parallelFlag || suite == "all") && selected.Count > 1;
        int instances = int.TryParse(ArgVal(args, "--instances"), out var iv) && iv > 0
            ? iv : ParallelOrchestrator.DefaultInstances(selected.Count);

        RunReport report = useParallel
            ? ParallelOrchestrator.Run(appPath, selected, seed, instances)
            : RunSequential(appPath, openWith, hebrewPath, missingFontPath, selected, seed);

        // 4. Report.
        var (md, json) = Reporter.Write(report, reportDir, stamp);
        Console.WriteLine(Reporter.ToMarkdown(report));
        Console.WriteLine($"\nReport written:\n  {md}\n  {json}");

        // 5. Exit code: fail the run on any failure or any uncatalogued control.
        return (report.Failed() == 0 && report.UntestedControls.Count == 0) ? 0 : 1;
    }

    // Classic one-instance, one-after-another path. Used for single-suite runs, --sequential,
    // and as the debugging fallback. Mirrors the original harness behaviour exactly.
    private static RunReport RunSequential(string appPath, string openWith, string hebrewPath,
        string missingFontPath, IReadOnlyList<string> selected, int seed)
    {
        using var driver = AppDriver.Launch(appPath, openWith);
        System.Threading.Thread.Sleep(800); // let app.start + open.success flush
        string? logPath = driver.ResolveLogPath();
        if (logPath == null)
        {
            Console.Error.WriteLine("No session log found — is logging enabled?");
            return new RunReport();
        }
        var runner = new ActionRunner(driver, new LogReader(logPath), openWith);
        var report = new RunReport();

        foreach (var suiteName in selected)
        {
            Console.WriteLine($"[suite] {suiteName}...");
            driver.ResetToBaseState();
            switch (suiteName)
            {
                case "singles":  SinglesSuite.Run(driver, runner, report); break;
                case "journeys": JourneysSuite.Run(driver, runner, report); break;
                case "pairwise": PairwiseSuite.Run(driver, runner, report); break;
                case "monkey":   MonkeySuite.Run(driver, runner, report, seed); break;
                case "fonts":    FontHebrewSuite.Run(driver, runner, report, openWith, hebrewPath, missingFontPath); break;
                case "save":     SaveVerifySuite.Run(driver, report, openWith); break;
            }
        }
        return report;
    }

    private static string? ArgVal(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }

    private static string? FindDefaultApp()
    {
        // Newest Scalpel.exe under a bin output dir, preferring a published build.
        // Match path SEGMENTS (not raw substrings) so an unrelated folder that merely
        // contains "release" in its name can't be picked up.
        try
        {
            var root = Directory.GetCurrentDirectory();
            char sep = Path.DirectorySeparatorChar;
            string[] wanted = ["publish", "Release", "Debug"];

            bool InBinOutput(string p)
            {
                var segs = p.Split(sep, Path.AltDirectorySeparatorChar);
                return segs.Contains("bin") && segs.Any(s => wanted.Contains(s));
            }

            int Rank(string p) => p.Contains($"{sep}publish{sep}") ? 0
                                : p.Contains($"{sep}Release{sep}") ? 1 : 2;

            return Directory.GetFiles(root, "Scalpel.exe", SearchOption.AllDirectories)
                .Where(InBinOutput)
                .OrderBy(Rank)
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch { return null; }
    }
}
