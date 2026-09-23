using System;
using System.IO;
using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class ScriptRunsTests
    {
        private static byte[] Font(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return File.ReadAllBytes(Path.Combine(dir!.FullName, "Resources", "Fonts", file));
        }

        private static readonly byte[] Geist = Font("Geist-Regular.ttf");
        private static readonly byte[] Hebrew = Font("NotoSansHebrew-Regular.ttf");
        private static readonly byte[] Latin = Font("NotoSans-Regular.ttf");

        // The real glyph coverage of the bundled faces (Noto Sans Arabic is not needed here).
        private static bool Covers(string family, int cp) => family switch
        {
            "Geist" => TrueTypeCmap.CoversCodepoint(Geist, cp),
            "Noto Sans Hebrew" => TrueTypeCmap.CoversCodepoint(Hebrew, cp),
            "Noto Sans" => TrueTypeCmap.CoversCodepoint(Latin, cp),
            _ => false,
        };

        private static void AssertAllCovered(System.Collections.Generic.List<(string Text, string Family)> segs)
        {
            foreach (var (text, fam) in segs)
                foreach (char c in text)
                    if (!char.IsWhiteSpace(c))
                        Assert.True(Covers(fam, c), $"U+{(int)c:X4} '{c}' drawn in {fam}, which has no glyph for it");
        }

        [Fact]
        public void BundledHebrewFace_LacksLatin_WhichIsWhyWholeLineSubstitutionBoxedIt()
        {
            Assert.True(Covers("Noto Sans Hebrew", 0x05D0));
            Assert.False(Covers("Noto Sans Hebrew", 'A'));
            Assert.False(Covers("Noto Sans Hebrew", '2'));
            Assert.False(Covers("Noto Sans Hebrew", '/'));
        }

        [Theory]
        [InlineData("צוות סקאלפל / The Scalpel Team", "Geist")]
        [InlineData("2. עברית ושפות RTL / Hebrew and RTL", "Geist")]
        [InlineData("חשבונית 2024 (Invoice)", "Noto Sans")]
        [InlineData("Hello שלום", "Geist")]
        public void MixedLine_EveryCharacterLandsInAFontThatHasIt(string logical, string candidate)
        {
            var segs = ScriptRuns.Split(ScriptRuns.ToVisual(logical), candidate, Covers);
            AssertAllCovered(segs);
            Assert.Contains(segs, s => s.Family == "Noto Sans Hebrew");
            Assert.Contains(segs, s => s.Family == candidate);
        }

        [Fact]
        public void Segments_ConcatenateBackToTheVisualString()
        {
            string visual = ScriptRuns.ToVisual("צוות סקאלפל / The Scalpel Team");
            var segs = ScriptRuns.Split(visual, "Geist", Covers);
            Assert.Equal(visual, string.Concat(segs.Select(s => s.Text)));
        }

        [Fact]
        public void PlainLatin_IsOneSegmentInTheCandidate()
        {
            var segs = ScriptRuns.Split("Hello, world. 42", "Geist", Covers);
            Assert.Single(segs);
            Assert.Equal("Geist", segs[0].Family);
        }

        [Fact]
        public void PureHebrew_DoesNotFragmentAtSpaces()
        {
            var segs = ScriptRuns.Split(ScriptRuns.ToVisual("מסמך עברית מלא ומאומת"), "Geist", Covers);
            Assert.Single(segs);
            Assert.Equal("Noto Sans Hebrew", segs[0].Family);
        }

        [Fact]
        public void Empty_ReturnsNoSegments()
            => Assert.Empty(ScriptRuns.Split("", "Geist", Covers));
    }
}
