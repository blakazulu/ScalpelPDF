using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TabListModelTests
    {
        private sealed class Tab { public string? Path; public Tab(string? p) { Path = p; } }

        private static (TabListModel<Tab> m, Tab a, Tab b, Tab c) Three()
        {
            var m = new TabListModel<Tab>();
            Tab a = new(@"C:\a.pdf"), b = new(@"C:\b.pdf"), c = new(@"C:\c.pdf");
            m.Add(a); m.Add(b); m.Add(c);
            return (m, a, b, c);
        }

        [Fact]
        public void Add_appends_and_activates()
        {
            var (m, _, _, c) = Three();
            Assert.Equal(3, m.Count);
            Assert.Same(c, m.Active);
        }

        [Fact]
        public void Add_without_activate_keeps_the_active_tab()
        {
            var (m, _, _, c) = Three();
            var d = new Tab(@"C:\d.pdf");
            m.Add(d, activate: false);
            Assert.Same(c, m.Active);
            Assert.Same(d, m.Items.Last());
        }

        [Fact]
        public void Removing_the_active_tab_activates_the_right_neighbour()
        {
            var (m, a, b, c) = Three();
            m.Activate(b);
            Assert.Same(c, m.Remove(b));
            Assert.Same(c, m.Active);
        }

        [Fact]
        public void Removing_the_last_active_tab_activates_the_left_neighbour()
        {
            var (m, _, b, c) = Three();
            Assert.Same(b, m.Remove(c));
        }

        [Fact]
        public void Removing_a_background_tab_keeps_the_active_tab()
        {
            var (m, a, _, c) = Three();
            Assert.Same(c, m.Remove(a));
        }

        [Fact]
        public void Removing_the_only_tab_leaves_no_active_tab()
        {
            var m = new TabListModel<Tab>();
            var a = new Tab(null);
            m.Add(a);
            Assert.Null(m.Remove(a));
            Assert.Equal(0, m.Count);
        }

        [Fact]
        public void Removing_twice_is_a_harmless_no_op()   // upstream #353
        {
            var (m, a, _, c) = Three();
            m.Remove(a);
            Assert.Same(c, m.Remove(a));
            Assert.Equal(2, m.Count);
        }

        [Fact]
        public void Cycle_wraps_in_both_directions()
        {
            var (m, a, _, c) = Three();
            Assert.Same(a, m.Cycle(forward: true));
            m.Activate(a);
            Assert.Same(c, m.Cycle(forward: false));
        }

        [Fact]
        public void Cycle_does_not_change_the_active_tab()
        {
            var (m, _, _, c) = Three();
            m.Cycle(forward: true);
            Assert.Same(c, m.Active);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(9, 2)]    // 9 = last tab, like Chrome
        public void ByNumber_maps_digits_to_tabs(int n, int expectedIndex)
        {
            var (m, _, _, _) = Three();
            Assert.Same(m.Items[expectedIndex], m.ByNumber(n));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(10)]
        public void ByNumber_out_of_range_is_null(int n)
        {
            var (m, _, _, _) = Three();
            Assert.Null(m.ByNumber(n));
        }

        [Fact]
        public void Move_reorders_and_clamps()
        {
            var (m, a, b, c) = Three();
            m.Move(0, 99);
            Assert.Equal(new[] { b, c, a }, m.Items);
        }

        [Fact]
        public void OthersThan_and_RightOf_return_snapshots()
        {
            var (m, a, b, c) = Three();
            Assert.Equal(new[] { a, c }, m.OthersThan(b));
            Assert.Equal(new[] { c }, m.RightOf(b));
            Assert.Empty(m.RightOf(c));
        }

        [Fact]
        public void FindByPath_ignores_case_and_relative_segments()
        {
            var (m, _, b, _) = Three();
            Assert.Same(b, m.FindByPath(@"c:\X\..\B.PDF", t => t.Path));
            Assert.Null(m.FindByPath(@"C:\zzz.pdf", t => t.Path));
        }

        [Fact]
        public void DocumentPath_Same_handles_nulls()
        {
            Assert.False(DocumentPath.Same(null, @"C:\a.pdf"));
            Assert.False(DocumentPath.Same(null, null));
            Assert.True(DocumentPath.Same(@"C:\A.pdf", @"c:\a.PDF"));
        }
    }
}
