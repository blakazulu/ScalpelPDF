using System;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Scalpel.Services
{
    /// <summary>
    /// Configuration for a geometric page transform: quarter-turn rotation, fine deskew,
    /// uniform scale, and horizontal/vertical flips, applied over a page range.
    /// </summary>
    public sealed class TransformOptions
    {
        /// <summary>Number of clockwise 90° turns (0..3).</summary>
        public int QuarterTurns { get; set; }
        /// <summary>Fine deskew angle in degrees (clockwise). Small values (e.g. ±5°).</summary>
        public double FineAngleDegrees { get; set; }
        /// <summary>Uniform scale factor (1.0 = unchanged).</summary>
        public double Scale { get; set; } = 1.0;
        public bool FlipHorizontal { get; set; }
        public bool FlipVertical { get; set; }
        /// <summary>true: the page grows to fit the transformed content; false: keep the original page box.</summary>
        public bool ResizePage { get; set; } = true;
        /// <summary>1-based inclusive first page to transform. Null = from the first page.</summary>
        public int? FromPage { get; set; }
        /// <summary>1-based inclusive last page to transform. Null = through the last page.</summary>
        public int? ToPage { get; set; }
    }

    /// <summary>
    /// Geometrically transforms PDF pages. A lossless fast path handles pure quarter-turn
    /// rotation by adjusting each page's <c>/Rotate</c> entry (vector text preserved); any
    /// flip / fine-angle / scale request falls back to rasterizing each in-range page,
    /// transforming the bitmap with ImageSharp, and rebuilding the page from the image.
    /// WPF-free; assumes a font resolver is configured for the out-of-range page copy path.
    /// </summary>
    public static partial class TransformService
    {
        /// <summary>
        /// Factory for the page rasterizer used by the flip/fine-angle/scale path. The app wires
        /// in the native Docnet rasterizer (see <c>TransformService.Docnet.cs</c>); the unit-test
        /// project links only this file, exercises the lossless + pure-math paths, and leaves this
        /// null (those paths never rasterize).
        /// </summary>
#pragma warning disable CS0649 // assigned by the app-only TransformService.Docnet.cs static ctor (not linked into tests)
        internal static Func<string, IPageRasterizer>? RasterizerFactory;
#pragma warning restore CS0649
        /// <summary>
        /// Pure size math: the page size in points after a quarter-turn (odd turns swap
        /// width/height) and uniform scale. Fine-angle growth is intentionally excluded so
        /// the result is deterministic and unit-testable.
        /// </summary>
        public static (double w, double h) TransformedSizePt(double wPt, double hPt, TransformOptions o)
        {
            if (o is null) throw new ArgumentNullException(nameof(o));
            double scale = o.Scale <= 0 ? 1.0 : o.Scale;
            bool swap = ((o.QuarterTurns % 4) + 4) % 4 % 2 == 1;
            double w = swap ? hPt : wPt;
            double h = swap ? wPt : hPt;
            return (w * scale, h * scale);
        }

        /// <summary>True when only a quarter-turn is requested (no flip/scale/fine angle).</summary>
        private static bool IsPureQuarterTurn(TransformOptions o)
            => !o.FlipHorizontal && !o.FlipVertical
               && Math.Abs(o.FineAngleDegrees) < 0.001
               && Math.Abs((o.Scale <= 0 ? 1.0 : o.Scale) - 1.0) < 0.001;

        public static void ApplyFile(string inputPath, string outputPath, TransformOptions opts)
        {
            if (inputPath is null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));
            if (opts is null) throw new ArgumentNullException(nameof(opts));

            int turns = ((opts.QuarterTurns % 4) + 4) % 4;

            if (IsPureQuarterTurn(opts))
            {
                ApplyLosslessRotation(inputPath, outputPath, opts, turns);
                return;
            }

            ApplyRasterized(inputPath, outputPath, opts, turns);
        }

        // ---- lossless quarter-turn: bump each in-range page's /Rotate -----------------------------

        private static void ApplyLosslessRotation(string inputPath, string outputPath, TransformOptions opts, int turns)
        {
            using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
            int total = doc.PageCount;
            int from = Math.Max(1, opts.FromPage ?? 1);
            int to = Math.Min(total, opts.ToPage ?? total);
            int delta = turns * 90;

            for (int p = from; p <= to; p++)
            {
                try
                {
                    var page = doc.Pages[p - 1];
                    page.Rotate = ((page.Rotate + delta) % 360 + 360) % 360;
                }
                catch { /* skip a page that won't accept the rotation rather than abort */ }
            }
            PdfSaveGuard.Save(doc, outputPath);
        }

        // ---- rasterized: flip / fine-angle / scale ------------------------------------------------

        private static void ApplyRasterized(string inputPath, string outputPath, TransformOptions opts, int turns)
        {
            var factory = RasterizerFactory
                ?? throw new InvalidOperationException("No page rasterizer is configured for the transform.");
            var rast = factory(inputPath);
            using var rastDisposable = rast as IDisposable;
            using var srcDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            using var outDoc = new PdfDocument();

            int total = rast.PageCount;
            int from = Math.Max(1, opts.FromPage ?? 1);
            int to = Math.Min(total, opts.ToPage ?? total);

            var rotMode = turns switch
            {
                1 => RotateMode.Rotate90,
                2 => RotateMode.Rotate180,
                3 => RotateMode.Rotate270,
                _ => RotateMode.None,
            };
            var flipMode = (opts.FlipHorizontal, opts.FlipVertical) switch
            {
                (true, true) => FlipMode.None, // both flips = 180°, applied via an extra Rotate below
                (true, false) => FlipMode.Horizontal,
                (false, true) => FlipMode.Vertical,
                _ => FlipMode.None,
            };
            bool bothFlips = opts.FlipHorizontal && opts.FlipVertical;
            double scale = opts.Scale <= 0 ? 1.0 : opts.Scale;

            for (int i = 0; i < total; i++)
            {
                int pageNum = i + 1;
                bool inRange = pageNum >= from && pageNum <= to;

                if (!inRange)
                {
                    TryCopyThrough(outDoc, srcDoc, i);
                    continue;
                }

                try
                {
                    var raster = rast.RenderPage(i);
                    var (wPt, hPt) = rast.PageSizePt(i);

                    byte[] transformed;
                    int outPxW, outPxH;
                    using (var img = Image.Load(raster.ImageBytes))
                    {
                        img.Mutate(x =>
                        {
                            if (rotMode != RotateMode.None || flipMode != FlipMode.None)
                                x.RotateFlip(rotMode, flipMode);
                            if (bothFlips) x.Rotate(180f);
                            if (Math.Abs(opts.FineAngleDegrees) >= 0.001)
                                x.Rotate((float)opts.FineAngleDegrees);
                        });
                        outPxW = img.Width;
                        outPxH = img.Height;
                        using var ms = new System.IO.MemoryStream();
                        img.Save(ms, new PngEncoder());
                        transformed = ms.ToArray();
                    }

                    // Pixels-per-point of the source render (proportional, so X == Y).
                    double ppp = raster.PixelWidth > 0 && wPt > 0 ? raster.PixelWidth / wPt : 1.0;
                    double contentWpt = outPxW / ppp * scale;
                    double contentHpt = outPxH / ppp * scale;

                    if (opts.ResizePage)
                        AppendCenteredImagePage(outDoc, transformed, contentWpt, contentHpt, contentWpt, contentHpt);
                    else
                        AppendCenteredImagePage(outDoc, transformed, wPt, hPt, contentWpt, contentHpt);
                }
                catch
                {
                    // A page that won't rasterize/transform is copied through unchanged rather
                    // than aborting the whole file.
                    TryCopyThrough(outDoc, srcDoc, i);
                }
            }

            PdfSaveGuard.Save(outDoc, outputPath);
        }

        private static void TryCopyThrough(PdfDocument outDoc, PdfDocument srcDoc, int index)
        {
            try { outDoc.AddPage(srcDoc.Pages[index]); }
            catch { /* if even the copy fails, drop the page rather than crash the run */ }
        }

        /// <summary>Adds a page of <paramref name="pageWpt"/> x <paramref name="pageHpt"/> and draws the
        /// image at <paramref name="contentWpt"/> x <paramref name="contentHpt"/>, scaled to fit and
        /// centered (so a "keep original page box" transform never overflows the page).</summary>
        private static void AppendCenteredImagePage(PdfDocument doc, byte[] imageBytes,
            double pageWpt, double pageHpt, double contentWpt, double contentHpt)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(Math.Max(1, pageWpt));
            page.Height = XUnit.FromPoint(Math.Max(1, pageHpt));

            double fit = Math.Min(pageWpt / Math.Max(1e-6, contentWpt), pageHpt / Math.Max(1e-6, contentHpt));
            if (fit > 1.0) fit = 1.0; // never upscale beyond the page when keeping the original box
            double drawW = contentWpt * fit;
            double drawH = contentHpt * fit;
            double x = (pageWpt - drawW) / 2.0;
            double y = (pageHpt - drawH) / 2.0;

            using var gfx = XGraphics.FromPdfPage(page);
            byte[] copy = (byte[])imageBytes.Clone();
            var xImg = XImage.FromStream(() => new System.IO.MemoryStream(copy));
            gfx.DrawImage(xImg, x, y, drawW, drawH);
        }
    }
}
