using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class WheelPageFlipGateTests
{
    private static readonly DateTime Start = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OneStandardNotchAtEdge_ConfirmsPageFlip()
    {
        var gate = new WheelPageFlipGate();

        Assert.True(gate.TryConfirm(-120, Start));
    }

    [Fact]
    public void MomentumImmediatelyAfterContentScroll_DoesNotFlip()
    {
        var gate = new WheelPageFlipGate();
        gate.NoteContentScroll(Start);

        Assert.False(gate.TryConfirm(-120, Start.AddMilliseconds(50)));
        Assert.False(gate.TryConfirm(-120, Start.AddMilliseconds(150)));
    }

    [Fact]
    public void PrecisionDeltasAccumulateInOneDirection()
    {
        var gate = new WheelPageFlipGate();

        Assert.False(gate.TryConfirm(-40, Start));
        Assert.False(gate.TryConfirm(-40, Start.AddMilliseconds(50)));
        Assert.True(gate.TryConfirm(-40, Start.AddMilliseconds(100)));
    }

    [Fact]
    public void OppositePrecisionDirection_RestartsConfirmation()
    {
        var gate = new WheelPageFlipGate();

        Assert.False(gate.TryConfirm(-60, Start));
        Assert.False(gate.TryConfirm(60, Start.AddMilliseconds(100)));
        Assert.True(gate.TryConfirm(60, Start.AddMilliseconds(200)));
    }

    [Fact]
    public void SlowPrecisionDeltas_DoNotCombineIntoPageFlip()
    {
        var gate = new WheelPageFlipGate();

        Assert.False(gate.TryConfirm(-60, Start));
        Assert.False(gate.TryConfirm(-60, Start.AddMilliseconds(700)));
    }
}
