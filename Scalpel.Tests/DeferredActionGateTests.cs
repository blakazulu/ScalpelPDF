using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class DeferredActionGateTests
{
    [Fact]
    public void NewRestoreInvalidatesOlderQueuedRestore()
    {
        var gate = new DeferredActionGate();
        int first = gate.Begin();
        int second = gate.Begin();

        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void CancelInvalidatesQueuedRestore()
    {
        var gate = new DeferredActionGate();
        int pending = gate.Begin();

        gate.Cancel();

        Assert.False(gate.IsCurrent(pending));
    }
}
