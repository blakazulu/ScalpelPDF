using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Geometry for "comb" text fields: a fixed-length field whose box is divided into
    /// equal cells, one character per cell (ID numbers, dates, postal codes).
    /// </summary>
    public static class CombFieldLayout
    {
        /// <summary>Left edge (relative to the field) of cell <paramref name="index"/>.</summary>
        public static double CellLeft(double width, int cellCount, int index)
        {
            if (!IsFinite(width) || width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (cellCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellCount));
            if ((uint)index >= (uint)cellCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return width / cellCount * index;
        }

        /// <summary>The cell under a click at <paramref name="x"/> (relative to the field),
        /// clamped to the first / last cell for clicks outside the box.</summary>
        public static int CellIndexAt(double x, double width, int cellCount)
        {
            if (!IsFinite(width) || width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (cellCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (!IsFinite(x))
                throw new ArgumentOutOfRangeException(nameof(x));
            int raw = (int)Math.Floor(x / (width / cellCount));
            return Math.Max(0, Math.Min(cellCount - 1, raw));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
