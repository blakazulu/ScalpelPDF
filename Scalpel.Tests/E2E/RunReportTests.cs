using Scalpel.E2E;
using Xunit;

namespace Scalpel.Tests.E2E;

public class RunReportTests
{
    [Fact]
    public void Counts_AggregateBySuiteAndOverall()
    {
        var r = new RunReport();
        r.Results.Add(new ActionResult("singles", "ZoomInBtn", Outcome.Pass, null, []));
        r.Results.Add(new ActionResult("singles", "ToolDrawBtn", Outcome.Fail, "no click logged", []));
        r.Results.Add(new ActionResult("journeys", "open", Outcome.Pass, null, []));

        Assert.Equal(3, r.Total());
        Assert.Equal(2, r.Passed());
        Assert.Equal(1, r.Failed());
        Assert.Equal(2, r.TotalFor("singles"));
        Assert.Equal(1, r.FailedFor("singles"));
        Assert.Equal(0, r.FailedFor("journeys"));
        Assert.Equal(["singles", "journeys"], r.Suites());
    }
}
