using System.Collections.Generic;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class OcrTextJoinerTests
    {
        static OcrWord W(string t, double x, double y, double w = 10, double h = 10)
            => new OcrWord { Text = t, XPt = x, YPt = y, WidthPt = w, HeightPt = h };

        [Fact] public void Empty_ReturnsEmpty()
            => Assert.Equal("", OcrTextJoiner.Join(new List<OcrWord>()));

        [Fact] public void SingleWord_ReturnsIt()
            => Assert.Equal("hello", OcrTextJoiner.Join(new[] { W("hello", 0, 0) }));

        [Fact] public void SameLine_OrderedLeftToRight()
        {
            var r = OcrTextJoiner.Join(new[] { W("world", 50, 0), W("hello", 0, 0) });
            Assert.Equal("hello world", r);
        }

        [Fact] public void DifferentLines_TopToBottom()
        {
            var r = OcrTextJoiner.Join(new[] { W("second", 0, 100), W("first", 0, 0) });
            Assert.Equal("first\nsecond", r);
        }

        [Fact] public void Grid_TwoByTwo_OrdersRowsThenColumns()
        {
            var r = OcrTextJoiner.Join(new[] {
                W("d", 50, 100), W("a", 0, 0), W("c", 0, 100), W("b", 50, 0) });
            Assert.Equal("a b\nc d", r);
        }
    }
}