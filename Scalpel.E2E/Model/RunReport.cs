namespace Scalpel.E2E;

public sealed class RunReport
{
    public List<ActionResult> Results { get; } = [];
    public List<string> UntestedControls { get; } = [];

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
