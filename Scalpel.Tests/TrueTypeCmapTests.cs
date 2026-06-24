using System;
using System.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TrueTypeCmapTests
    {
        private const int Alef = 0x05D0;
        private static string FontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }

        [Fact]
        public void SegoeUi_CoversHebrewAlef()
        {
            string p = Path.Combine(FontsDir, "segoeui.ttf");
            Assert.True(File.Exists(p), $"expected {p} on Windows");
            Assert.True(TrueTypeCmap.CoversCodepoint(File.ReadAllBytes(p), Alef));
        }

        [Fact]
        public void SegoeUi_CoversLatinA()
        {
            string p = Path.Combine(FontsDir, "segoeui.ttf");
            Assert.True(TrueTypeCmap.CoversCodepoint(File.ReadAllBytes(p), 'A'));
        }

        [Fact]
        public void Geist_DoesNotCoverHebrew()
        {
            string p = Path.Combine(RepoRoot(), "Resources", "Fonts", "Geist-Regular.ttf");
            Assert.True(File.Exists(p), $"expected bundled font at {p}");
            Assert.False(TrueTypeCmap.CoversCodepoint(File.ReadAllBytes(p), Alef));
        }

        [Fact]
        public void Garbage_ReturnsFalse()
            => Assert.False(TrueTypeCmap.CoversCodepoint(new byte[] { 1, 2, 3, 4 }, Alef));
    }
}
