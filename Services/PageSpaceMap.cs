using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Maps rectangles between the rendered canvas and PDF user space, honouring page rotation.
    ///
    /// <para>The canvas shows the page as the viewer draws it: y grows downward and the page
    /// rotation has already been applied to the bitmap. PDF user space is the unrotated page with
    /// y growing upward. Every overlay that has to line up with page content - the crop box, form
    /// field widgets, link hotspots - needs this same conversion.</para>
    ///
    /// <para>It lives here because it used to be hand-derived separately at each call site, and the
    /// copies disagreed: at 180 degrees the rotation's vertical flip and the y-up/y-down flip
    /// cancel out, so y passes through unchanged. One copy applied the flip anyway and cropped the
    /// mirrored half of the page. Deriving it once, with tests, is what keeps them honest.</para>
    /// </summary>
    public static class PageSpaceMap
    {
        /// <summary>Normalizes any multiple of 90, including negatives, to 0/90/180/270.</summary>
        private static int Norm(int rotation)
        {
            int r = rotation % 360;
            if (r < 0) r += 360;
            return r - (r % 90);
        }

        // A zero dimension would divide by zero; PDFs are untrusted, so fall back to 1pt.
        private static double Safe(double v) => v > 0 && !double.IsNaN(v) ? v : 1.0;

        /// <summary>Maps one canvas point (y-down, rotated) to PDF user space (y-up, unrotated).</summary>
        public static (double X, double Y) PointToPdf(
            double cx, double cy, double pdfW, double pdfH, double canvasW, double canvasH, int rotation)
        {
            pdfW = Safe(pdfW); pdfH = Safe(pdfH);
            canvasW = Safe(canvasW); canvasH = Safe(canvasH);

            // At 90/270 the canvas is the page turned on its side, so the canvas width spans the
            // page's height and vice versa.
            return Norm(rotation) switch
            {
                90  => (cy * pdfW / canvasH,          cx * pdfH / canvasW),
                180 => (pdfW - cx * pdfW / canvasW,   cy * pdfH / canvasH),
                270 => (pdfW - cy * pdfW / canvasH,   pdfH - cx * pdfH / canvasW),
                _   => (cx * pdfW / canvasW,          pdfH - cy * pdfH / canvasH),
            };
        }

        /// <summary>Maps one PDF user-space point back to canvas coordinates.</summary>
        public static (double X, double Y) PointToCanvas(
            double x, double y, double pdfW, double pdfH, double canvasW, double canvasH, int rotation)
        {
            pdfW = Safe(pdfW); pdfH = Safe(pdfH);
            canvasW = Safe(canvasW); canvasH = Safe(canvasH);

            return Norm(rotation) switch
            {
                90  => (y * canvasW / pdfH,           x * canvasH / pdfW),
                180 => ((pdfW - x) * canvasW / pdfW,  y * canvasH / pdfH),
                270 => ((pdfH - y) * canvasW / pdfH,  (pdfW - x) * canvasH / pdfW),
                _   => (x * canvasW / pdfW,           (pdfH - y) * canvasH / pdfH),
            };
        }

        /// <summary>
        /// Converts a canvas rectangle to PDF coordinates as (x1,y1,x2,y2) with x1&lt;=x2 and
        /// y1&lt;=y2. Both opposite corners are mapped and then normalized, so the caller never has
        /// to reason about which corner ends up where at a given rotation.
        /// </summary>
        public static (double X1, double Y1, double X2, double Y2) ToPdf(
            double cx, double cy, double cw, double ch,
            double pdfW, double pdfH, double canvasW, double canvasH, int rotation)
        {
            var a = PointToPdf(cx, cy, pdfW, pdfH, canvasW, canvasH, rotation);
            var b = PointToPdf(cx + cw, cy + ch, pdfW, pdfH, canvasW, canvasH, rotation);

            return (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        /// <summary>
        /// Converts a PDF rectangle back to a canvas rectangle as (x,y,w,h) with a non-negative
        /// size. The inverse of <see cref="ToPdf"/>.
        /// </summary>
        public static (double X, double Y, double W, double H) ToCanvas(
            double x1, double y1, double x2, double y2,
            double pdfW, double pdfH, double canvasW, double canvasH, int rotation)
        {
            if (x1 > x2) (x1, x2) = (x2, x1);
            if (y1 > y2) (y1, y2) = (y2, y1);

            var a = PointToCanvas(x1, y1, pdfW, pdfH, canvasW, canvasH, rotation);
            var b = PointToCanvas(x2, y2, pdfW, pdfH, canvasW, canvasH, rotation);

            double left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
            return (left, top, Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        }
    }
}
