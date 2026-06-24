using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class ArabicShaperTests
    {
        // Assert on codepoints (hex) to stay independent of source-file glyph rendering.
        // Base letters: BEH 0628, SEEN 0633, LAM 0644, ALEF 0627, MEEM 0645, FATHA 064E.
        private static string Hex(string s) => string.Join(" ", s.Select(c => ((int)c).ToString("X4")));

        [Fact]
        public void ContainsArabic_DetectsArabic()
        {
            Assert.True(ArabicShaper.ContainsArabic("سلام")); // salam
            Assert.False(ArabicShaper.ContainsArabic("hello"));
            Assert.False(ArabicShaper.ContainsArabic("שלום")); // Hebrew, not Arabic
        }

        [Fact]
        public void Shape_EmptyAndLatin_Unchanged()
        {
            Assert.Equal("", ArabicShaper.Shape(""));
            Assert.Equal("hello", ArabicShaper.Shape("hello"));
        }

        [Fact]
        public void Shape_IsolatedSingleLetter_UsesIsolatedForm()
        {
            // BEH alone -> FE8F (isolated)
            Assert.Equal("FE8F", Hex(ArabicShaper.Shape("ب")));
        }

        [Fact]
        public void Shape_DualJoinBetweenJoiners_UsesMedialForm()
        {
            // BEH BEH BEH: initial FE91, medial FE92, final FE90
            Assert.Equal("FE91 FE92 FE90", Hex(ArabicShaper.Shape("ببب")));
        }

        [Fact]
        public void Shape_AfterRightJoiner_NextStartsFresh()
        {
            // ALEF (right-join, no forward join) + BEH: ALEF isolated FE8D, BEH isolated FE8F
            Assert.Equal("FE8D FE8F", Hex(ArabicShaper.Shape("اب")));
        }

        [Fact]
        public void Shape_LamAlef_FormsIsolatedLigature()
        {
            // LAM + ALEF -> isolated lam-alef ligature U+FEFB
            Assert.Equal("FEFB", Hex(ArabicShaper.Shape("لا")));
        }

        [Fact]
        public void Shape_SalamWord_Connects()
        {
            // SEEN LAM ALEF MEEM (salam): SEEN initial FEB3; LAM+ALEF final ligature FEFC
            // (joined from SEEN on the right); MEEM isolated FEE1 (ALEF breaks the join).
            Assert.Equal("FEB3 FEFC FEE1", Hex(ArabicShaper.Shape("سلام")));
        }

        [Fact]
        public void Shape_HarakatTransparent_DoNotBreakJoining()
        {
            // BEH + FATHA + BEH: fatha transparent; BEHs still join: initial FE91, mark, final FE90
            Assert.Equal("FE91 064E FE90", Hex(ArabicShaper.Shape("بَب")));
        }
    }
}
