using System;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace Scalpel.Services
{
    /// <summary>
    /// Pure color-math helpers for the RGB color picker: RGB&lt;-&gt;HSV conversion and
    /// HTML-hex parse/format. WPF-free apart from <see cref="Color"/> (the app's native
    /// color type). No UI, no state — safe to unit test.
    /// </summary>
    public static class ColorConvert
    {
        /// <summary>RGB -&gt; HSV. h in 0..360, s and v in 0..1. Alpha is ignored.</summary>
        public static (double h, double s, double v) RgbToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            double h = 0;
            if (d > 0.00001)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * (((b - r) / d) + 2);
                else h = 60 * (((r - g) / d) + 4);
            }
            if (h < 0) h += 360;
            double s = max <= 0 ? 0 : d / max;
            return (h, s, max);
        }

        /// <summary>HSV -&gt; Color. h wraps mod 360; s and v are clamped to 0..1.</summary>
        public static Color HsvToColor(double h, double s, double v, byte a = 255)
        {
            h = ((h % 360) + 360) % 360;
            s = Clamp01(s);
            v = Clamp01(v);
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(
                a,
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        /// <summary>Format as "#RRGGBB" (alpha dropped).</summary>
        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>
        /// Parse a hex color. Accepts "#RGB", "#RRGGBB", or the same without the leading '#'.
        /// Returns an opaque <see cref="Color"/>. Garbage input yields false.
        /// </summary>
        public static bool TryParseHex(string? s, out Color c)
        {
            c = Colors.Black;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s!.Trim().TrimStart('#');
            if (s.Length == 3) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
            if (s.Length != 6) return false;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            c = Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            return true;
        }

        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
    }
}
