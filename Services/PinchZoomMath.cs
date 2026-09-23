using System;

namespace Scalpel.Services
{
    /// <summary>The zoom and scroll offsets to apply after one pinch step.</summary>
    public readonly record struct PinchZoomResult(
        double Zoom, double HorizontalOffset, double VerticalOffset);

    /// <summary>
    /// Zooms around a gesture origin so the page point under the fingers stays put. The
    /// scale is clamped to the zoom limits first and the offsets use the scale that was
    /// actually applied, so hitting the limit never makes the view jump.
    /// </summary>
    public static class PinchZoomMath
    {
        /// <summary>Applies one pinch <paramref name="scale"/> factor to <paramref name="oldZoom"/>.
        /// <paramref name="originX"/>/<paramref name="originY"/> are the gesture centre relative
        /// to the viewport; offsets are the current scroll position.</summary>
        public static PinchZoomResult Apply(
            double oldZoom, double scale, double minimumZoom, double maximumZoom,
            double horizontalOffset, double verticalOffset, double originX, double originY)
        {
            if (!IsFinite(oldZoom) || oldZoom <= 0)
                oldZoom = minimumZoom;
            if (!IsFinite(scale) || scale <= 0)
                scale = 1;

            double zoom = Math.Max(minimumZoom, Math.Min(maximumZoom, oldZoom * scale));
            double appliedScale = zoom / oldZoom;
            double newHorizontal = (horizontalOffset + originX) * appliedScale - originX;
            double newVertical = (verticalOffset + originY) * appliedScale - originY;
            return new PinchZoomResult(zoom, Math.Max(0, newHorizontal), Math.Max(0, newVertical));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
