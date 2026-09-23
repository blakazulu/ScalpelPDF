using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Pixel budget for rasterizing the pages the viewer shows. The primary page tracks
    /// DPI scale and zoom (capped so a 400% zoom on a 4K display cannot allocate a
    /// gigabyte bitmap); secondary pages (grid / continuous neighbours) keep a smaller,
    /// memory-limited budget unless the two-page layout shows them at full size.
    /// </summary>
    public static class ViewerRenderResolution
    {
        /// <summary>Long-edge pixel size for the page under the user's eye.</summary>
        public static int Primary(double dpiScaleX, double dpiScaleY, double zoomLevel) =>
            (int)Math.Min(6144,
                2048 * Math.Max(dpiScaleX, dpiScaleY) * Math.Max(1.0, zoomLevel));

        /// <summary>Long-edge pixel size for the other visible pages. In two-page mode the
        /// facing page is just as prominent as the primary one and shares its budget.</summary>
        public static int Secondary(bool twoPage, double dpiScaleX, double dpiScaleY, double zoomLevel) =>
            twoPage
                ? Primary(dpiScaleX, dpiScaleY, zoomLevel)
                : (int)Math.Min(3072, 1536 * Math.Max(1.0, Math.Max(dpiScaleX, dpiScaleY)));
    }
}
