using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TabCloseOrderTests
    {
        [Fact]
        public void Active_dirty_tab_is_asked_about_first_then_left_to_right()
        {
            string[] tabs = ["a", "b", "c", "d"];
            var dirty = new[] { "a", "c", "d" };
            var order = TabCloseOrder.DirtyFirstActive(tabs, "c", t => System.Array.IndexOf(dirty, t) >= 0);
            Assert.Equal(new[] { "c", "a", "d" }, order);
        }

        [Fact]
        public void Clean_tabs_are_never_listed()
        {
            string[] tabs = ["a", "b"];
            Assert.Empty(TabCloseOrder.DirtyFirstActive(tabs, "a", _ => false));
        }

        [Fact]
        public void Clean_active_tab_is_skipped_and_the_rest_keep_strip_order()
        {
            string[] tabs = ["a", "b", "c"];
            var order = TabCloseOrder.DirtyFirstActive(tabs, "b", t => t != "b");
            Assert.Equal(new[] { "a", "c" }, order);
        }

        [Fact]
        public void No_active_tab_lists_dirty_tabs_left_to_right()
        {
            string[] tabs = ["a", "b", "c"];
            var order = TabCloseOrder.DirtyFirstActive(tabs, null, t => t != "b");
            Assert.Equal(new[] { "a", "c" }, order);
        }
    }
}
