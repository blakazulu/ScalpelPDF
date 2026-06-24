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

        // 2. Launch ONE app session and locate its log. We keep a single session
        // (rather than relaunching per suite) because Windows foreground-lock stops
        // a freshly launched process from reliably taking foreground, and physical
        // clicks need the window foregrounded. The first launch wins foreground; we
        // reset app state between suites instead.
        bool all = suite == "all";
        var selected = new[] { "singles", "journeys", "pairwise", "monkey", "fonts" }
            .Where(s => all || suite == s).ToList();
        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"Unknown --suite '{suite}'. Use singles|journeys|pairwise|monkey|fonts|all.");
            return 2;
        }

        using var driver = AppDriver.Launch(appPath, openWith);
        System.Threading.Thread.Sleep(800); // let app.start + open.success flush
        string? logPath = LogReader.FindLatestLog(LogReader.DefaultLogDir());
        if (logPath == null)
        {
            Console.Error.WriteLine("No session log found — is logging enabled?");
            return 3;
        }
        var runner = new ActionRunner(driver, new LogReader(logPath), openWith);
        var report = new RunReport();

        // 3. Run each requested suite, resetting to a base state in between so one
        // suite's end-state (open overlay, non-View mode) can't break the next.
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
            }
        }

        // 4. Report.
        var (md, json) = Reporter.Write(report, reportDir, stamp);
        Console.WriteLine(Reporter.ToMarkdown(report));
        Console.WriteLine($"\nReport written:\n  {md}\n  {json}");

        // 5. Exit code: fail the run on any failure or any uncatalogued control.
        return (report.Failed() == 0 && report.UntestedControls.Count == 0) ? 0 : 1;
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
