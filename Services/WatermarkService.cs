using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Scalpel.Services
{
    /// <summary>Where a watermark / stamp is placed on the page.</summary>
    public enum WatermarkPosition
    {
        Center,
        TopLeft, TopCenter, TopRight,
        MiddleLeft, MiddleRight,
        BottomLeft, BottomCenter, BottomRight,
        Tiled,
    }

    /// <summary>
    /// Configuration for a per-page watermark / stamp. A text watermark and/or an image
    /// stamp can be applied in one pass; leave <see cref="Text"/> null/blank to skip the
    /// text, and <see cref="ImagePath"/> null to skip the image.
    /// </summary>
    public sealed class WatermarkOptions
    {
        /// <summary>Watermark text (e.g. "CONFIDENTIAL"). Null/blank = no text watermark.</summary>
        public string? Text { get; set; }
        public string FontFamily { get; set; } = "Geist";
        public double FontSize { get; set; } = 48;
        public (byte R, byte G, byte B) Color { get; set; } = (128, 128, 128);
        /// <summary>0..1 opacity applied to both the text and the image stamp.</summary>
        public double Opacity { get; set; } = 0.30;
        /// <summary>Rotation of the text watermark, in degrees (counter-clockwise). The image stamp is drawn upright.</summary>
        public double RotationDegrees { get; set; } = 45;
        public WatermarkPosition Position { get; set; } = WatermarkPosition.Center;
        /// <summary>PNG/JPG file to stamp. Null = no image stamp. Missing/unloadable files are skipped silently.</summary>
        public string? ImagePath { get; set; }
        /// <summary>Image width as a multiple of half the page width (1.0 = ~50% of page width).</summary>
        public double ImageScale { get; set; } = 1.0;
        /// <summary>1-based inclusive first page to stamp. Null = from the first page.</summary>
        public int? FromPage { get; set; }
        /// <summary>1-based inclusive last page to stamp. Null = through the last page.</summary>
        public int? ToPage { get; set; }
    }

    /// <summary>
    /// Stamps a text watermark and/or an image onto PDF pages. Pure PdfSharpCore + ImageSharp;
    /// WPF-free. Assumes a font resolver is already configured (mirrors <see cref="BatesNumberingService"/>).
    /// </summary>
    public static class WatermarkService
    {
        public static void Apply(PdfDocument doc, WatermarkOptions opts)
        {
            if (doc is null) throw new ArgumentNullException(nameof(doc));
            if (opts is null) throw new ArgumentNullException(nameof(opts));

            int total = doc.PageCount;
            int from = Math.Max(1, opts.FromPage ?? 1);
            int to = Math.Min(total, opts.ToPage ?? total);
            bool hasText = !string.IsNullOrWhiteSpace(opts.Text);

            // Pre-load the image once (opacity baked into the pixels) — reused for every page.
            XImage? img = null;
            if (!string.IsNullOrWhiteSpace(opts.ImagePath))
            {
                try { img = LoadImage(opts.ImagePath!, ClampOpacity(opts.Opacity)); }
                catch { img = null; }
            }

            if (!hasText && img is null) return;

            for (int pageNum = from; pageNum <= to; pageNum++)
            {
                try
                {
                    var page = doc.Pages[pageNum - 1];
                    double pw = page.Width.Point;
                    double ph = page.Height.Point;
                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    // Image first so the text watermark reads on top of it.
                    if (img is not null) DrawImage(gfx, opts, img, pw, ph);
                    if (hasText) DrawText(gfx, opts, pw, ph);
                }
                catch { /* skip a page that won't accept the overlay rather than abort the whole run */ }
            }
        }

        public static void ApplyFile(string inputPath, string outputPath, WatermarkOptions opts)
        {
            using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
            Apply(doc, opts);
            PdfSaveGuard.Save(doc, outputPath);
        }

        private static void DrawText(XGraphics gfx, WatermarkOptions opts, double pw, double ph)
        {
            string text = opts.Text!;
            double fontSize = Math.Max(1, opts.FontSize);
            XFont font;
            try { font = new XFont(opts.FontFamily, fontSize, XFontStyle.Bold); }
            catch { font = new XFont("Arial", fontSize, XFontStyle.Bold); }

            byte a = (byte)Math.Max(0, Math.Min(255, Math.Round(ClampOpacity(opts.Opacity) * 255)));
            var (r, g, b) = opts.Color;
            var brush = new XSolidBrush(XColor.FromArgb(a, r, g, b));

            var size = gfx.MeasureString(text, font);
            double w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);

            if (opts.Position == WatermarkPosition.Tiled)
            {
                double stepX = w + pw * 0.06;
                double stepY = h + ph * 0.10;
                double diag = Math.Sqrt(pw * pw + ph * ph);
                var st = gfx.Save();
                gfx.TranslateTransform(pw / 2.0, ph / 2.0);
                gfx.RotateTransform(-opts.RotationDegrees);
                // Cover a square larger than the page diagonal so the rotated grid leaves no gaps.
                for (double y = -diag; y < diag; y += stepY)
                    for (double x = -diag; x < diag; x += stepX)
                        gfx.DrawString(text, font, brush, new XRect(x, y, w, h), XStringFormats.TopLeft);
                gfx.Restore(st);
                return;
            }

            var (cx, cy) = CenterFor(opts.Position, pw, ph, w, h);
            var state = gfx.Save();
            gfx.TranslateTransform(cx, cy);
            gfx.RotateTransform(-opts.RotationDegrees);
            gfx.DrawString(text, font, brush, new XRect(-w / 2.0, -h / 2.0, w, h), XStringFormats.Center);
            gfx.Restore(state);
        }

        private static void DrawImage(XGraphics gfx, WatermarkOptions opts, XImage img, double pw, double ph)
        {
            double scale = opts.ImageScale <= 0 ? 1.0 : opts.ImageScale;
            double w = Math.Max(1, pw * 0.5 * scale);
            double h = w * img.PixelHeight / Math.Max(1, img.PixelWidth);
            var (cx, cy) = opts.Position == WatermarkPosition.Tiled
                ? (pw / 2.0, ph / 2.0)
                : CenterFor(opts.Position, pw, ph, w, h);
            gfx.DrawImage(img, cx - w / 2.0, cy - h / 2.0, w, h);
        }

        // Returns the center point for a stamp of size (w,h) at the given anchor.
        private static (double cx, double cy) CenterFor(WatermarkPosition pos, double pw, double ph, double w, double h)
        {
            double mx = pw * 0.05, my = ph * 0.04;
            double cx = pos switch
            {
                WatermarkPosition.TopLeft or WatermarkPosition.MiddleLeft or WatermarkPosition.BottomLeft => mx + w / 2.0,
                WatermarkPosition.TopRight or WatermarkPosition.MiddleRight or WatermarkPosition.BottomRight => pw - mx - w / 2.0,
                _ => pw / 2.0,
            };
            double cy = pos switch
            {
                WatermarkPosition.TopLeft or WatermarkPosition.TopCenter or WatermarkPosition.TopRight => my + h / 2.0,
                WatermarkPosition.BottomLeft or WatermarkPosition.BottomCenter or WatermarkPosition.BottomRight => ph - my - h / 2.0,
                _ => ph / 2.0,
            };
            return (cx, cy);
        }

        private static double ClampOpacity(double o) => Math.Max(0.0, Math.Min(1.0, o));

        // Loads an image as an XImage, baking the requested opacity into the alpha channel
        // (PdfSharpCore has no per-draw image opacity). Throws on an unreadable file — callers guard.
        private static XImage LoadImage(string path, double opacity)
        {
            byte[] bytes;
            if (opacity >= 0.999)
            {
                bytes = File.ReadAllBytes(path);
            }
            else
            {
                using var image = Image.Load<Rgba32>(File.ReadAllBytes(path));
                // ImageSharp 2.x replaced Image.GetPixelRowSpan(y) with ProcessPixelRows, which
                // hands out the buffer under a lock instead of exposing raw row spans.
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            var px = row[x];
                            px.A = (byte)Math.Round(px.A * opacity);
                            row[x] = px;
                        }
                    }
                });
                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                bytes = ms.ToArray();
            }
            return XImage.FromStream(() => new MemoryStream(bytes));
        }
    }
}
