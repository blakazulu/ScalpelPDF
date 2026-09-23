using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class UndoHistoryBudgetTests
{
    [Fact]
    public void PushBounded_DropsOldestEntriesAtTheDepthLimit()
    {
        var history = new Stack<Entry>();

        for (int value = 1; value <= 5; value++)
            UndoHistoryBudget.PushBounded(history, new Entry(value, 1), e => e.Bytes, 3, 100);

        Assert.Equal([5, 4, 3], history.Select(entry => entry.Value));
    }

    [Fact]
    public void PushBounded_DropsOldestEntriesAtTheByteLimit()
    {
        var history = new Stack<Entry>();
        UndoHistoryBudget.PushBounded(history, new Entry(1, 40), e => e.Bytes, 10, 100);
        UndoHistoryBudget.PushBounded(history, new Entry(2, 40), e => e.Bytes, 10, 100);
        UndoHistoryBudget.PushBounded(history, new Entry(3, 40), e => e.Bytes, 10, 100);

        Assert.Equal([3, 2], history.Select(entry => entry.Value));
    }

    [Fact]
    public void PushBounded_AlwaysRetainsTheNewestOversizedEntry()
    {
        var history = new Stack<Entry>();
        UndoHistoryBudget.PushBounded(history, new Entry(1, 40), e => e.Bytes, 10, 100);
        UndoHistoryBudget.PushBounded(history, new Entry(2, 250), e => e.Bytes, 10, 100);

        Entry retained = Assert.Single(history);
        Assert.Equal(2, retained.Value);
    }

    private readonly record struct Entry(int Value, long Bytes);
}
