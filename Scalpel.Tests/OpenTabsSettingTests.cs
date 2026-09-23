using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class OpenTabsSettingTests
    {
        [Fact]
        public void Round_trips_paths_and_active_index()
        {
            string wire = OpenTabsSetting.Serialize([@"C:\a.pdf", @"D:\b c.pdf"], 1);
            var (paths, active) = OpenTabsSetting.Parse(wire, _ => true);
            Assert.Equal(new[] { @"C:\a.pdf", @"D:\b c.pdf" }, paths);
            Assert.Equal(1, active);
        }

        [Fact]
        public void Dropped_paths_shift_the_active_index()
        {
            string wire = OpenTabsSetting.Serialize([@"C:\gone.pdf", @"C:\a.pdf", @"C:\b.pdf"], 2);
            var (paths, active) = OpenTabsSetting.Parse(wire, p => p != @"C:\gone.pdf");
            Assert.Equal(new[] { @"C:\a.pdf", @"C:\b.pdf" }, paths);
            Assert.Equal(1, active);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        public void Bad_values_yield_nothing(string? wire)
        {
            var (paths, active) = OpenTabsSetting.Parse(wire, _ => true);
            Assert.Empty(paths);
            Assert.Equal(-1, active);
        }
    }
}
