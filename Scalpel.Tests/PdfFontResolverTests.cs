using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class PdfFontResolverTests
    {
        [Fact]
        public void Resolve_Arial_ReturnsNonNullFace_AndGetFontReturnsBytes()
        {
            var info = PdfFontResolver.Instance.ResolveTypeface("Arial", false, false);
            Assert.NotNull(info);
            var bytes = PdfFontResolver.Instance.GetFont(info.FaceName);
            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 1000, "a real font program is far larger than 1KB");
        }

        [Fact]
        public void Resolve_ArialBold_ResolvesToAFace()
        {
            var info = PdfFontResolver.Instance.ResolveTypeface("Arial", true, false);
            Assert.NotNull(info);
            // Either a real bold face or the regular face flagged to simulate bold.
            Assert.True(info.MustSimulateBold || info.FaceName.Length > 0);
            var bytes = PdfFontResolver.Instance.GetFont(info.FaceName);
            Assert.True(bytes.Length > 1000);
        }

        [Fact]
        public void Resolve_UnknownFamily_FallsBackNeverNull()
        {
            var info = PdfFontResolver.Instance.ResolveTypeface("ThisFontDoesNotExist123", false, false);
            Assert.NotNull(info);
            var bytes = PdfFontResolver.Instance.GetFont(info.FaceName);
            Assert.True(bytes.Length > 1000, "fallback face must yield a real font program");
        }

        [Fact]
        public void Resolve_MultiWordSystemFamily_ResolvesToRealFace()
        {
            // "Times New Roman" (times.ttf) is present on all Windows installs.
            // FaceKey format is "family|b|i" lowercased, e.g. "times new roman|0|0".
            var info = PdfFontResolver.Instance.ResolveTypeface("Times New Roman", false, false);
            Assert.NotNull(info);
            var bytes = PdfFontResolver.Instance.GetFont(info.FaceName);
            Assert.True(bytes.Length > 1000);
            // Verify it resolved to Times New Roman, NOT the Arial fallback.
            Assert.DoesNotContain("arial|", info.FaceName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("times new roman", info.FaceName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
