using System.Collections.Generic;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class UndoBudgetCoordinatorTests
    {
        private static Stack<long> S(params long[] oldestFirst)
        {
            var s = new Stack<long>();
            foreach (var x in oldestFirst) s.Push(x);
            return s;
        }

        [Fact]
        public void Under_budget_nothing_is_trimmed()
        {
            var a = S(10, 10); var b = S(10);
            Assert.Equal(30, UndoBudgetCoordinator.TrimAcross([a, b], x => x, 100));
            Assert.Equal(2, a.Count);
        }

        [Fact]
        public void Oldest_entries_of_the_least_recent_tab_go_first()
        {
            var old = S(50, 40);      // least recently used tab
            var active = S(30);
            long left = UndoBudgetCoordinator.TrimAcross([old, active], x => x, 80);
            Assert.Equal(70, left);
            Assert.Equal(new long[] { 40 }, old.ToArray());   // the 50 (oldest) was dropped
            Assert.Single(active);
        }

        [Fact]
        public void The_newest_entry_of_the_active_tab_is_always_kept()
        {
            var active = S(10, 500);
            UndoBudgetCoordinator.TrimAcross([active], x => x, 100);
            Assert.Equal(new long[] { 500 }, active.ToArray());
        }
    }
}
