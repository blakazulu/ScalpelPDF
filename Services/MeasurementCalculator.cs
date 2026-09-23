using System;

namespace Scalpel.Services
{
    /// <summary>A measured distance in every unit the measure tool reports, plus the
    /// displayed page size (in points) it was computed against.</summary>
    public readonly record struct MeasurementValues(
        double Points, double Inches, double Millimetres,
        double PageWidthPoints, double PageHeightPoints);

    /// <summary>
    /// Converts a canvas-space drag into a real-world distance on the page. Independent of
    /// render resolution: the canvas delta is scaled by the displayed page size in points.
    /// </summary>
    public static class MeasurementCalculator
    {
        /// <summary>Measures a drag of (<paramref name="canvasDx"/>, <paramref name="canvasDy"/>)
        /// over a page rendered at <paramref name="renderWidth"/> x <paramref name="renderHeight"/>
        /// pixels. <paramref name="rotation"/> (degrees) swaps the page axes for quarter turns.</summary>
        public static MeasurementValues Calculate(
            double pageWidthPoints, double pageHeightPoints, int rotation,
            double renderWidth, double renderHeight, double canvasDx, double canvasDy)
        {
            if (pageWidthPoints <= 0 || pageHeightPoints <= 0 || renderWidth <= 0 || renderHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(renderWidth));
            bool quarterTurn = rotation is 90 or 270;
            double displayWidth = quarterTurn ? pageHeightPoints : pageWidthPoints;
            double displayHeight = quarterTurn ? pageWidthPoints : pageHeightPoints;
            double dxPoints = canvasDx * displayWidth / renderWidth;
            double dyPoints = canvasDy * displayHeight / renderHeight;
            double points = Math.Sqrt(dxPoints * dxPoints + dyPoints * dyPoints);
            double inches = points / 72.0;
            return new MeasurementValues(points, inches, inches * 25.4, displayWidth, displayHeight);
        }
    }
}
