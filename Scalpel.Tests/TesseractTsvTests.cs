using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TesseractTsvTests
    {
        private const string Header =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext";

        private static string Row(int level, int left, int top, int w, int h, string conf, string text)
            => $"{level}\t1\t1\t1\t1\t1\t{left}\t{top}\t{w}\t{h}\t{conf}\t{text}";

        [Fact]
        public void Parse_ExtractsWordsAtPixelBoxes_WhenScale1to1()
        {
            string tsv = string.Join("\n",
                Header,
                Row(1, 0, 0, 600, 800, "-1", ""),       // page block, skipped
                Row(5, 72, 100, 80, 20, "96.5", "HELLO"),
                Row(5, 160, 100, 85, 20, "95.1", "WORLD"));

            var r = TesseractTsv.Parse(tsv, 600, 800, 600, 800);

            Assert.Equal(2, r.Words.Count);
            Assert.Equal("HELLO", r.Words[0].Text);
            Assert.Equal(72, r.Words[0].XPt, 1);
            Assert.Equal(100, r.Words[0].YPt, 1);
            Assert.Equal(80, r.Words[0].WidthPt, 1);
            Assert.Equal("WORLD", r.Words[1].Text);
        }

        [Fact]
        public void Parse_MapsPixelsToPoints_WhenDownscaled()
        {
            string tsv = string.Join("\n", Header, Row(5, 72, 100, 80, 20, "90", "HELLO"));
            // image 600x800 px -> page 300x400 pt => scale 0.5
            var r = TesseractTsv.Parse(tsv, 600, 800, 300, 400);

            Assert.Single(r.Words);
            Assert.Equal(36, r.Words[0].XPt, 1);
            Assert.Equal(50, r.Words[0].YPt, 1);
            Assert.Equal(40, r.Words[0].WidthPt, 1);
            Assert.Equal(10, r.Words[0].HeightPt, 1);
        }

        [Fact]
        public void Parse_SkipsEmptyTextAndNonWordRows()
        {
            string tsv = string.Join("\n",
                Header,
                Row(4, 0, 0, 100, 100, "-1", ""),         // line, non-word
                Row(5, 10, 10, 10, 10, "0", "   "),        // whitespace text
                Row(5, 20, 20, 30, 10, "88", "REAL"));

            var r = TesseractTsv.Parse(tsv, 200, 200, 200, 200);

            Assert.Single(r.Words);
            Assert.Equal("REAL", r.Words[0].Text);
        }

        [Fact]
        public void Parse_RespectsMinConfidence()
        {
            string tsv = string.Join("\n",
                Header,
                Row(5, 0, 0, 10, 10, "20", "LOWCONF"),
                Row(5, 0, 0, 10, 10, "95", "HIGHCONF"));

            var r = TesseractTsv.Parse(tsv, 100, 100, 100, 100, minConfidence: 60);

            Assert.Single(r.Words);
            Assert.Equal("HIGHCONF", r.Words[0].Text);
        }

        [Fact]
        public void Parse_EmptyInput_ReturnsNoWords()
        {
            Assert.Empty(TesseractTsv.Parse("", 100, 100, 100, 100).Words);
            Assert.Empty(TesseractTsv.Parse(Header, 100, 100, 100, 100).Words);
        }
    }
}
