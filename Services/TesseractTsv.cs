using System;
using System.Globalization;

namespace Scalpel.Services
{
    /// <summary>
    /// Parses Tesseract's TSV output (`tesseract img out tsv`) into an <see cref="OcrPageResult"/>,
    /// mapping pixel word boxes to PDF points. Pure logic — unit-tested without the native engine.
    /// </summary>
    public static class TesseractTsv
    {
        /// <param name="tsv">Raw TSV text from Tesseract.</param>
        /// <param name="imagePxWidth">Width in pixels of the image OCR'd.</param>
        /// <param name="imagePxHeight">Height in pixels of the image OCR'd.</param>
        /// <param name="pageWidthPt">Target page width in points.</param>
        /// <param name="pageHeightPt">Target page height in points.</param>
        /// <param name="minConfidence">Drop words below this confidence (0–100).</param>
        public static OcrPageResult Parse(string tsv, int imagePxWidth, int imagePxHeight,
            double pageWidthPt, double pageHeightPt, double minConfidence = 0)
        {
            var result = new OcrPageResult();
            if (string.IsNullOrWhiteSpace(tsv) || imagePxWidth <= 0 || imagePxHeight <= 0)
                return result;

            double sx = pageWidthPt / imagePxWidth;
            double sy = pageHeightPt / imagePxHeight;

            var lines = tsv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
            {
                if (line.Length == 0) continue;
                var f = line.Split('\t');
                if (f.Length < 12) continue;
                if (f[0] == "level") continue; // header

                // level 5 == word
                if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level != 5)
                    continue;

                string text = f[11];
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (!double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out double conf))
                    conf = 0;
                if (conf < minConfidence) continue;

                if (!int.TryParse(f[6], out int left) ||
                    !int.TryParse(f[7], out int top) ||
                    !int.TryParse(f[8], out int width) ||
                    !int.TryParse(f[9], out int height))
                    continue;

                result.Words.Add(new OcrWord
                {
                    Text = text,
                    XPt = left * sx,
                    YPt = top * sy,
                    WidthPt = width * sx,
                    HeightPt = height * sy,
                });
            }
            return result;
        }
    }
}
