using System;

namespace Scalpel.Services
{
    /// <summary>
    /// The affine transform that maps annotation coordinates (canvas pixels, top-left origin,
    /// y down, laid out over the page as the user SEES it) onto the surface PdfSharpCore's
    /// XGraphics actually draws into.
    ///
    /// <para>
    /// Scalpel keeps page rotation out of its working document: after any page operation,
    /// <c>SaveTempAndReload</c> writes <c>/Rotate 0</c> into the temp file and remembers the real
    /// angle separately, then rotates the rendered bitmap instead. That leaves the two frames
    /// disagreeing at save time - the canvas is landscape while the page (and therefore the
    /// XGraphics surface) is still portrait - so an annotation burned with a plain scale lands
    /// rotated, offset and scaled on swapped axes.
    /// </para>
    /// <para>
    /// A page that still carries its own <c>/Rotate</c> needs none of this: PDFium sizes its
    /// bitmap in the rotated frame and XGraphics presents a rotated surface, so the two already
    /// agree. <see cref="For"/> detects which case it is from the surface size.
    /// </para>
    /// </summary>
    public static class BurnRotation
    {
        /// <summary>
        /// A 2D affine transform in the same component order XGraphics uses:
        /// <c>x' = x*M11 + y*M21 + OffsetX</c>, <c>y' = x*M12 + y*M22 + OffsetY</c>.
        /// </summary>
        public readonly record struct Transform(
            double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
        {
            /// <summary>The transform that changes nothing.</summary>
            public static Transform Identity => new(1, 0, 0, 1, 0, 0);

            /// <summary>True when this is a plain scale with no rotation or translation.</summary>
            public bool IsAxisAligned => Math.Abs(M12) < 1e-9 && Math.Abs(M21) < 1e-9;

            /// <summary>Applies the transform to a point.</summary>
            public (double X, double Y) Apply(double x, double y)
                => (x * M11 + y * M21 + OffsetX, x * M12 + y * M22 + OffsetY);
        }

        /// <summary>
        /// Builds the canvas-to-surface transform.
        /// </summary>
        /// <param name="rotation">Visual rotation of the page in degrees (0, 90, 180, 270).</param>
        /// <param name="surfaceW">Width of the XGraphics surface, in points.</param>
        /// <param name="surfaceH">Height of the XGraphics surface, in points.</param>
        /// <param name="renderW">Width of the rendered bitmap the annotations were placed on.</param>
        /// <param name="renderH">Height of that bitmap.</param>
        public static Transform For(int rotation, double surfaceW, double surfaceH,
                                    double renderW, double renderH)
        {
            if (renderW <= 0 || renderH <= 0 || surfaceW <= 0 || surfaceH <= 0)
                return Transform.Identity;

            int rot = ((rotation % 360) + 360) % 360;
            if (rot is not (90 or 180 or 270))
                return new Transform(surfaceW / renderW, 0, 0, surfaceH / renderH, 0, 0);

            bool quarterTurn = rot is 90 or 270;

            // Visual size in points. For a quarter turn the surface is the transposed page, so
            // when the surface is ALREADY the visual frame its width matches the canvas' aspect.
            bool surfaceIsVisual = quarterTurn
                ? SameAspect(surfaceW, surfaceH, renderW, renderH)
                : true;   // a half turn never transposes, so the frames always agree in shape

            if (surfaceIsVisual && quarterTurn)
            {
                // XGraphics is already drawing in the rotated frame (the page kept its /Rotate),
                // so a plain scale is all that is needed.
                return new Transform(surfaceW / renderW, 0, 0, surfaceH / renderH, 0, 0);
            }

            // The surface is the unrotated page. Scale canvas pixels to visual points, then map
            // visual coordinates onto the unrotated page.
            double visualW = quarterTurn ? surfaceH : surfaceW;
            double visualH = quarterTurn ? surfaceW : surfaceH;
            double sx = visualW / renderW;
            double sy = visualH / renderH;

            return rot switch
            {
                // 90 deg clockwise: page top-left appears at the visual top-right.
                //   px = vy,          py = surfaceH - vx
                90 => new Transform(0, -sx, sy, 0, 0, surfaceH),

                // 180: both axes flip.
                //   px = surfaceW - vx, py = surfaceH - vy
                180 => new Transform(-sx, 0, 0, -sy, surfaceW, surfaceH),

                // 270 (90 deg counter-clockwise): page top-left appears at the visual bottom-left.
                //   px = surfaceW - vy, py = vx
                _ => new Transform(0, sx, -sy, 0, surfaceW, 0),
            };
        }

        /// <summary>
        /// True when the two sizes describe the same shape (within a loose tolerance, because a
        /// rendered bitmap is rounded to whole pixels).
        /// </summary>
        private static bool SameAspect(double aw, double ah, double bw, double bh)
        {
            if (ah <= 0 || bh <= 0) return false;
            double ra = aw / ah, rb = bw / bh;
            return Math.Abs(ra - rb) <= 0.05 * Math.Max(ra, rb);
        }
    }
}
