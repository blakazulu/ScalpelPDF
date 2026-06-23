using System;
using System.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TrueTypeNameTests
    {
        private static string FontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        [Fact]
        public void Read_Arial_ReturnsArialRegular()
        {
            string p = Path.Combine(FontsDir, "arial.ttf");
            Assert.True(File.Exists(p), $"expected {p} on a Windows machine");
            var names = TrueTypeName.Read(File.ReadAllBytes(p));
            Assert.Equal("Arial", names.Family);
            Assert.DoesNotContain("Bold", names.Subfamily, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Read_ArialBold_SubfamilyContainsBold()
        {
            string p = Path.Combine(FontsDir, "arialbd.ttf");
            Assert.True(File.Exists(p), $"expected {p} on a Windows machine");
            var names = TrueTypeName.Read(File.ReadAllBytes(p));
            Assert.Equal("Arial", names.Family);
            Assert.Contains("Bold", names.Subfamily, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Read_GeistRegular_FromRepo_FamilyIsGeist()
        {
            // Repo-relative: tests run from Scalpel.Tests/bin/<cfg>/net48; walk up to repo root.
            string repo = RepoRoot();
            string p = Path.Combine(repo, "Resources", "Fonts", "Geist-Regular.ttf");
            Assert.True(File.Exists(p), $"expected bundled font at {p}");
            var names = TrueTypeName.Read(File.ReadAllBytes(p));
            Assert.Equal("Geist", names.Family);
        }

        [Fact]
        public void Read_Garbage_ReturnsEmpty()
        {
            var names = TrueTypeName.Read(new byte[] { 1, 2, 3, 4 });
            Assert.Equal("", names.Family);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }
    }
}
