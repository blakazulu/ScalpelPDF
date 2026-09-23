using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// Sets a page's /Rotate without disturbing its geometry. PdfSharpCore's
    /// <c>PdfPage.Rotate</c> setter also swaps the MediaBox's width and height whenever the new
    /// angle differs from the old one by an odd number of quarter turns (it tries to keep the
    /// page's "orientation"). Scalpel changes /Rotate constantly - it strips it to 0 for every
    /// working copy and puts it back after - so each page operation silently turned a rotated
    /// portrait page's MediaBox landscape: after rotating a page four times it no longer matched
    /// the page it started as, and its text sat outside where it was drawn. Writing the entry
    /// directly changes the rotation and nothing else.
    /// </summary>
    public static class PageRotation
    {
        /// <summary>Normalizes any multiple of 90 (including negatives) to 0/90/180/270.</summary>
        public static int Normalize(int degrees)
        {
            int r = degrees % 360;
            if (r < 0) r += 360;
            return r - r % 90;
        }

        public static void Set(PdfPage page, int degrees)
        {
            int r = Normalize(degrees);
            page.Elements.SetInteger("/Rotate", r);
            // The page writer also flips the MediaBox when the page's Orientation flag says
            // Landscape - a flag PdfSharpCore works out once, from /Rotate, when the page is
            // loaded. A page read at 90 degrees is "Landscape" for good, so writing it after its
            // rotation went to 0 flipped the box anyway. Keep the flag in step with /Rotate so the
            // writer's flip and the rotation cancel exactly as they did when the page was loaded.
            try
            {
                page.Orientation = r is 90 or 270
                    ? PdfSharpCore.PageOrientation.Landscape
                    : PdfSharpCore.PageOrientation.Portrait;
            }
            catch { /* the entry above is what matters; the flag only guards the writer */ }
        }
    }
}
