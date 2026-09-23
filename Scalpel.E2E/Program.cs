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
        // Which Windows Display Settings monitor number every launched app window is moved onto.
        // Defaults to 1 so the run doesn't fight the developer's primary display for foreground;
        // pass --monitor 0 to leave the window wherever WPF's CenterScreen startup puts it.
        AppDriver.TargetMonitor = int.TryParse(ArgVal(args, "--monitor"), out var mon) ? mon : 1;

        // --monitors: list every attached monitor (bounds + primary flag) and, if --app also
        // resolved, launch Scalpel and report which monitor it actually landed on. A standalone
        // diagnostic for confirming --monitor targets the intended physical screen.
        if (args.Contains("--monitors"))
            return RunMonitorsDiagnostic(appPath);

        if (appPath == null || !File.Exists(appPath))
        {
            Console.Error.WriteLine("Scalpel.exe not found. Pass --app <path>.");
            return 2;
        }

        // Resolve to a full path with normalized separators: a relative or
        // forward-slash --app passes File.Exists but CreateProcess (used by
        // FlaUI's launch with UseShellExecute=false) cannot find it.
        appPath = Path.GetFullPath(appPath);

        // Snapshot the user's persisted Scalpel settings, baseline them for a deterministic run,
        // and restore them when this method returns (the suites mutate theme/accent/locale/view).
        using var settingsGuard = AppSettingsGuard.SnapshotAndBaseline();

        // 1. Fixtures.
        string corpusDir = Path.Combine(Path.GetTempPath(), "scalpel-e2e-corpus");
        var corpus = Corpus.Generate(corpusDir);
        string openWith = corpus.First(c => c.Key == "simple-1p").Path;
        string hebrewPath = corpus.FirstOrDefault(c => c.Key == "hebrew-1p")?.Path ?? "";
        string missingFontPath = corpus.FirstOrDefault(c => c.Key == "missingfont-1p")?.Path ?? "";
        string largePath = corpus.FirstOrDefault(c => c.Key == "large-50p")?.Path ?? "";

        // 2. Decide which suites to run. --suite accepts "all" or a comma-separated subset
        //    (e.g. --suite journeys,pairwise) so a fast/bounded run is possible.
        bool all = suite == "all";
        var requested = suite.Split(',')
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var selected = new[] { "singles", "journeys", "pairwise", "monkey", "fonts", "save", "tabs" }
            .Where(s => all || requested.Contains(s)).ToList();
        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"Unknown --suite '{suite}'. Use singles|journeys|pairwise|monkey|fonts|save|tabs|all, or a comma-separated subset.");
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
            : RunSequential(appPath, openWith, hebrewPath, missingFontPath, largePath, selected, seed);

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
        string missingFontPath, string largePath, IReadOnlyList<string> selected, int seed)
    {
        // Pristine snapshot of the corpus doc, taken BEFORE the app opens (or re-saves) it. The
        // fonts/save suites relaunch onto private copies for isolation; copying the live openWith
        // file once the app has held and re-saved it (e.g. singles' Save-in-place) yields a copy a
        // freshly-relaunched app refuses to open. Copy from this untouched snapshot instead.
        string cleanCorpus = openWith + ".clean.pdf";
        try { File.Copy(openWith, cleanCorpus, overwrite: true); } catch { }

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
            driver.CurrentSuite = suiteName;
            driver.ResetToBaseState();
            switch (suiteName)
            {
                case "singles":  SinglesSuite.Run(driver, runner, report); break;
                case "journeys": JourneysSuite.Run(driver, runner, report); break;
                case "pairwise": PairwiseSuite.Run(driver, runner, report); break;
                case "monkey":   MonkeySuite.Run(driver, runner, report, seed); break;
                case "fonts":    FontHebrewSuite.Run(driver, runner, report, cleanCorpus, hebrewPath, missingFontPath); break;
                case "save":     SaveVerifySuite.Run(driver, report, cleanCorpus); break;
                case "tabs":     TabsSuite.Run(driver, report, cleanCorpus, largePath); break;
            }
        }
        // Close the last instance now rather than in Dispose, so its close is reported too.
        driver.Shutdown();
        driver.AddCloseEventsTo(report);
        return report;
    }

    // Prints every attached monitor (the number/bounds/primary flag), and — if a Scalpel.exe was
    // resolved — launches it (respecting --monitor, default 1) and reports which monitor its window
    // landed on, so the intended physical screen can be confirmed against Windows Display Settings'
    // "Identify" numbering before trusting a full run to use it.
    private static int RunMonitorsDiagnostic(string? appPath)
    {
        var monitors = AppDriver.ListMonitors();
        Console.WriteLine("Attached monitors:");
        foreach (var (index, deviceName, primary, bounds) in monitors)
            Console.WriteLine($"  {index}: {deviceName}{(primary ? " (PRIMARY)" : "")} " +
                               $"bounds=[{bounds.X},{bounds.Y},{bounds.Width}x{bounds.Height}]");

        if (appPath == null || !File.Exists(appPath))
        {
            Console.WriteLine("(no --app resolved — skipping launch-and-verify)");
            return 0;
        }

        string corpusDir = Path.Combine(Path.GetTempPath(), "scalpel-e2e-corpus");
        var corpus = Corpus.Generate(corpusDir);
        string openWith = corpus.First(c => c.Key == "simple-1p").Path;

        Console.WriteLine($"\nLaunching {Path.GetFullPath(appPath)} targeting monitor {AppDriver.TargetMonitor}...");
        using var driver = AppDriver.Launch(Path.GetFullPath(appPath), openWith);
        System.Threading.Thread.Sleep(500);
        var wb = driver.CurrentWindowBounds;
        if (wb == null) { Console.WriteLine("Could not read the window's position."); return 1; }

        Console.WriteLine($"Window landed at [{wb.Value.X},{wb.Value.Y},{wb.Value.Width}x{wb.Value.Height}]");
        var containing = monitors.FirstOrDefault(m =>
            wb.Value.X >= m.bounds.X && wb.Value.X < m.bounds.X + m.bounds.Width &&
            wb.Value.Y >= m.bounds.Y && wb.Value.Y < m.bounds.Y + m.bounds.Height);
        Console.WriteLine(containing.deviceName != null
            ? $"-> That is monitor {containing.index} ({containing.deviceName}{(containing.primary ? ", PRIMARY" : "")})."
            : "-> Could not determine which monitor that falls within.");
        return 0;
    }

    private static string? ArgVal(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }

    private static string? FindDefaultApp()
    {
        // Newest Scalpel.exe under a bin output dir, preferring a published build ONLY as a
        // tiebreaker. Freshness must dominate: a `dotnet publish` from days ago being silently
        // preferred over a just-built Debug exe means the suite tests a stale binary with no
        // indication anything is wrong — exactly what happened when this compared Rank first,
        // caught after `ToolLineBtn` (added this session) tested as completely absent because the
        // resolved exe predated that feature by two days. Match path SEGMENTS (not raw substrings)
        // so an unrelated folder that merely contains "release" in its name can't be picked up.
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
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(Rank)
                .FirstOrDefault();
        }
        catch { return null; }
    }
}
