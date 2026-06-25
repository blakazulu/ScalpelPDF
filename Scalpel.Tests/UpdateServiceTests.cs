using System;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class UpdateServiceTests
    {
        [Theory]
        [InlineData("1.7.0", "1.5.1.36", true)]   // newer minor
        [InlineData("1.5.2", "1.5.1.36", true)]   // newer build
        [InlineData("2.0.0", "1.9.9.99", true)]   // newer major
        [InlineData("1.5.1", "1.5.1.36", false)]  // same 3-part
        [InlineData("1.5.0", "1.5.1.36", false)]  // older
        [InlineData("1.5", "1.5.1.36", false)]    // missing component, older-or-equal
        [InlineData("garbage", "1.5.1.36", false)]// unparseable => not newer
        public void IsNewer_compares_three_components(string latest, string current, bool expected)
        {
            Assert.Equal(expected, UpdateService.IsNewer(latest, Version.Parse(current)));
        }

        [Fact]
        public void TryParse_reads_full_document()
        {
            var json = "{\"version\":\"1.7.0\",\"siteUrl\":\"https://s\",\"storeUrl\":\"https://store\",\"notes\":[\"a\",\"b\"]}";
            var info = UpdateService.TryParse(json);
            Assert.NotNull(info);
            Assert.Equal("1.7.0", info!.Version);
            Assert.Equal("https://s", info.SiteUrl);
            Assert.Equal("https://store", info.StoreUrl);
            Assert.Equal(new[] { "a", "b" }, info.Notes);
        }

        [Fact]
        public void TryParse_tolerates_missing_optional_fields()
        {
            var info = UpdateService.TryParse("{\"version\":\"1.7.0\"}");
            Assert.NotNull(info);
            Assert.Equal("1.7.0", info!.Version);
            Assert.Equal("", info.StoreUrl);
            Assert.Empty(info.Notes);
        }

        [Fact]
        public void TryParse_returns_null_on_garbage()
        {
            Assert.Null(UpdateService.TryParse("not json"));
            Assert.Null(UpdateService.TryParse("{\"nope\":1}")); // no version
        }

        [Fact]
        public void ResolveUrl_picks_store_when_packaged_else_site()
        {
            var info = new UpdateInfo("1.7.0", "https://site", "https://store", Array.Empty<string>());
            Assert.Equal("https://store", UpdateService.ResolveUrl(info, packaged: true));
            Assert.Equal("https://site", UpdateService.ResolveUrl(info, packaged: false));
        }

        [Fact]
        public void ResolveUrl_falls_back_to_store_search_when_store_url_empty()
        {
            var info = new UpdateInfo("1.7.0", "https://site", "", Array.Empty<string>());
            Assert.Equal(UpdateService.StoreSearchUrl, UpdateService.ResolveUrl(info, packaged: true));
        }

        [Fact]
        public void ShouldCheckNow_false_when_disabled()
        {
            var now = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(UpdateService.ShouldCheckNow(enabled: false, lastCheck: null, now: now));
        }

        [Fact]
        public void ShouldCheckNow_true_when_enabled_and_never_checked()
        {
            var now = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(UpdateService.ShouldCheckNow(enabled: true, lastCheck: null, now: now));
        }

        [Fact]
        public void ShouldCheckNow_respects_24h_throttle()
        {
            var now = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(UpdateService.ShouldCheckNow(true, now.AddHours(-1), now));   // too soon
            Assert.True(UpdateService.ShouldCheckNow(true, now.AddHours(-25), now));   // due
        }
    }
}
