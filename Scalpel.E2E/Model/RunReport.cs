namespace Scalpel.E2E;

public sealed class RunReport
{
    public List<ActionResult> Results { get; } = [];
    public List<string> UntestedControls { get; } = [];
    /// <summary>Things that did not fail the run but must be visible in it (e.g. every save
    /// prompt the harness answered "No" when closing an instance).</summary>
    public List<string> Warnings { get; } = [];

    public int Total() => Results.Count;
    public int Passed() => Results.Count(r => r.Outcome == Outcome.Pass);
    public int Failed() => Results.Count(r => r.Outcome == Outcome.Fail);

    public int TotalFor(string suite) => Results.Count(r => r.Suite == suite);
    public int FailedFor(string suite) =>
        Results.Count(r => r.Suite == suite && r.Outcome == Outcome.Fail);

    // Distinct suites in first-seen order.
    public IEnumerable<string> Suites()
    {
        var seen = new HashSet<string>();
        foreach (var r in Results)
            if (seen.Add(r.Suite)) yield return r.Suite;
    }
}
