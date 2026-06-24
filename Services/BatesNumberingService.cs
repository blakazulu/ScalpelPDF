using System;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Scalpel.Services
{
    /// <summary>Where on the page a stamp is placed.</summary>
    public enum StampPosition
    {
        TopLeft, TopCenter, TopRight,
        BottomLeft, BottomCenter, BottomRight
    }

    /// <summary>
    /// Configuration for a per-page text stamp. Covers Bates numbering, page numbers,
    /// and plain header/footer text — all the same operation with a different template.
    /// Template placeholders: <c>{n}</c> running counter (zero-padded to <see cref="DigitCount"/>),
    /// <c>{page}</c> 1-based page number, <c>{total}</c> total page count.
    /// </summary>
    public sealed class StampOptions
    {
        public string Template { get; set; } = "{n}";
        public int StartNumber { get; set; } = 1;
        public int DigitCount { get; set; } = 0;
        public StampPosition Position { get; set; } = StampPosition.BottomRight;
        public double FontSize { get; set; } = 10;
        public double MarginPoints { get; set; } = 24;
        public string FontFamily { get; set; } = "Geist";
        public (byte R, byte G, byte B) Color { get; set; } = (0, 0, 0);
        /// <summary>1-based inclusive first page to stamp. Null = from the first page.</summary>
        public int? FromPage { get; set; }
        /// <summary>1-based inclusive last page to stamp. Null = through the last page.</summary>
        public int? ToPage { get; set; }
    }

    /// <summary>
    /// Stamps Bates numbers / page numbers / header-footer text onto PDF pages.
    /// Pure PdfSharpCore; assumes a font resolver is already configured.
    /// </summary>
    public static class BatesNumberingService
    {
        public static void Stamp(PdfDocument doc, StampOptions opts)
        {
            if (doc is null) throw new ArgumentNullException(nameof(doc));
            if (opts is null) throw new ArgumentNullException(nameof(opts));

            int total = doc.PageCount;
            int from = Math.Max(1, opts.FromPage ?? 1);
            int to = Math.Min(total, opts.ToPage ?? total);
            var color = XColor.FromArgb(255, opts.Color.R, opts.Color.G, opts.Color.B);
            var brush = new XSolidBrush(color);

            int counter = opts.StartNumber;
            for (int pageNum = from; pageNum <= to; pageNum++)
            {
                var page = doc.Pages[pageNum - 1];
                string text = Format(opts.Template, counter, opts.DigitCount, pageNum, total);
                counter++;
                if (string.IsNullOrEmpty(text)) continue;

                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var font = new XFont(opts.FontFamily, opts.FontSize, XFontStyle.Regular);
                double textWidth = gfx.MeasureString(text, font).Width;
                double pageW = page.Width.Point;
                double pageH = page.Height.Point;
                var (x, baselineY) = Place(opts.Position, opts.MarginPoints, opts.FontSize,
                    pageW, pageH, textWidth);
                gfx.DrawString(text, font, brush, x, baselineY);
            }
        }

        public static void StampFile(string inputPath, string outputPath, StampOptions opts)
        {
            using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
            Stamp(doc, opts);
            doc.Save(outputPath);
        }

        internal static string Format(string template, int counter, int digitCount, int pageNum, int total)
        {
            string num = digitCount > 0
                ? counter.ToString().PadLeft(digitCount, '0')
                : counter.ToString();
            return (template ?? "")
                .Replace("{n}", num)
                .Replace("{page}", pageNum.ToString())
                .Replace("{total}", total.ToString());
        }

        private static (double x, double baselineY) Place(StampPosition pos, double margin,
            double fontSize, double pageW, double pageH, double textWidth)
        {
            bool top = pos is StampPosition.TopLeft or StampPosition.TopCenter or StampPosition.TopRight;
            bool left = pos is StampPosition.TopLeft or StampPosition.BottomLeft;
            bool right = pos is StampPosition.TopRight or StampPosition.BottomRight;

            double baselineY = top ? margin + fontSize : pageH - margin;
            double x = left ? margin
                : right ? pageW - margin - textWidth
                : (pageW - textWidth) / 2.0; // center
            return (x, baselineY);
        }
    }
}
