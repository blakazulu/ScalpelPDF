using System.Windows.Media;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class ColorConvertTests
    {
        public static IEnumerable<object[]> RoundTripColors()
        {
            yield return new object[] { Colors.Red };
            yield return new object[] { Colors.Lime };   // pure green
            yield return new object[] { Colors.Blue };
            yield return new object[] { Color.FromRgb(128, 128, 128) }; // gray
            yield return new object[] { Colors.White };
            yield return new object[] { Colors.Black };
            yield return new object[] { Color.FromRgb(17, 99, 200) };   // arbitrary
            yield return new object[] { Color.FromRgb(240, 68, 88) };   // surgical red
        }

        [Theory]
        [MemberData(nameof(RoundTripColors))]
        public void RgbToHsv_To_Rgb_RoundTrips(Color c)
        {
            var (h, s, v) = ColorConvert.RgbToHsv(c);
            var back = ColorConvert.HsvToColor(h, s, v);
            // Exact integer round-trip is expected for the byte->HSV->byte path.
            Assert.Equal(c.R, back.R);
            Assert.Equal(c.G, back.G);
            Assert.Equal(c.B, back.B);
        }

        [Fact]
        public void RgbToHsv_KnownValues()
        {
            var (h, s, v) = ColorConvert.RgbToHsv(Colors.Red);
            Assert.Equal(0, h, 3);
            Assert.Equal(1, s, 3);
            Assert.Equal(1, v, 3);

            (h, s, v) = ColorConvert.RgbToHsv(Colors.White);
            Assert.Equal(0, s, 3);
            Assert.Equal(1, v, 3);

            (_, _, v) = ColorConvert.RgbToHsv(Colors.Black);
            Assert.Equal(0, v, 3);
        }

        [Fact]
        public void HsvToColor_Hue_Wraps_And_Clamps()
        {
            // Hue 360 == hue 0 (red); over-range s/v clamp without throwing.
            var a = ColorConvert.HsvToColor(360, 1, 1);
            var b = ColorConvert.HsvToColor(0, 1, 1);
            Assert.Equal(b, a);

            var clamped = ColorConvert.HsvToColor(120, 5, 5);
            Assert.Equal(Colors.Lime, clamped);
        }

        [Fact]
        public void HsvToColor_PreservesAlpha()
        {
            var c = ColorConvert.HsvToColor(0, 1, 1, 128);
            Assert.Equal(128, c.A);
        }

        [Theory]
        [InlineData("#FF0000", 255, 0, 0)]
        [InlineData("00FF00", 0, 255, 0)]
        [InlineData("#00f", 0, 0, 255)]      // shorthand
        [InlineData("fff", 255, 255, 255)]   // shorthand, no hash
        [InlineData("  #123456  ", 0x12, 0x34, 0x56)] // trimmed
        public void TryParseHex_Accepts_Valid(string input, int r, int g, int b)
        {
            Assert.True(ColorConvert.TryParseHex(input, out Color c));
            Assert.Equal((byte)r, c.R);
            Assert.Equal((byte)g, c.G);
            Assert.Equal((byte)b, c.B);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("xyz")]
        [InlineData("#12")]
        [InlineData("#12345")]
        [InlineData("#1234567")]
        [InlineData("nothex")]
        [InlineData("#GGGGGG")]
        public void TryParseHex_Rejects_Garbage(string? input)
        {
            Assert.False(ColorConvert.TryParseHex(input, out _));
        }

        [Theory]
        [MemberData(nameof(RoundTripColors))]
        public void ToHex_TryParseHex_RoundTrips(Color c)
        {
            string hex = ColorConvert.ToHex(c);
            Assert.True(ColorConvert.TryParseHex(hex, out Color back));
            Assert.Equal(c.R, back.R);
            Assert.Equal(c.G, back.G);
            Assert.Equal(c.B, back.B);
        }

        [Fact]
        public void ToHex_Format()
        {
            Assert.Equal("#FF0000", ColorConvert.ToHex(Colors.Red));
            Assert.Equal("#000000", ColorConvert.ToHex(Colors.Black));
            Assert.Equal("#123456", ColorConvert.ToHex(Color.FromRgb(0x12, 0x34, 0x56)));
        }
    }
}
