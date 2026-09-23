using System;
using System.Collections.Generic;

namespace Scalpel.Services
{
    /// <summary>One rectangle (pixel space) where two renders of a page differ.</summary>
    public readonly record struct DifferenceRegion(int X, int Y, int Width, int Height, int ChangedPixels);

    /// <summary>Outcome of comparing two page renders pixel by pixel.</summary>
    public sealed record PageDifferenceResult(
        bool DimensionsMatch,
        int ChangedPixels,
        double ChangedFraction,
        IReadOnlyList<DifferenceRegion> Regions)
    {
        /// <summary>True when the pages differ in size or in at least one pixel.</summary>
        public bool IsDifferent => !DimensionsMatch || ChangedPixels > 0;
    }

    /// <summary>
    /// Compares two BGRA renders of the same page size. Small per-channel noise (below
    /// <see cref="ChannelThreshold"/>) is ignored; changed pixels are binned into tiles and
    /// neighbouring tiles are flood-filled into rectangular regions the UI can outline.
    /// </summary>
    public static class PdfPageDifference
    {
        private const int TileSize = 12;
        private const int ChannelThreshold = 20;

        /// <summary>Compares two BGRA32 buffers (4 bytes per pixel, row-major, no padding).</summary>
        public static PageDifferenceResult Compare(
            ReadOnlySpan<byte> leftBgra, int leftWidth, int leftHeight,
            ReadOnlySpan<byte> rightBgra, int rightWidth, int rightHeight)
        {
            Validate(leftBgra, leftWidth, leftHeight, nameof(leftBgra));
            Validate(rightBgra, rightWidth, rightHeight, nameof(rightBgra));
            if (leftWidth != rightWidth || leftHeight != rightHeight)
                return new PageDifferenceResult(false, 0, 1, []);

            int tileColumns = (leftWidth + TileSize - 1) / TileSize;
            int tileRows = (leftHeight + TileSize - 1) / TileSize;
            var changedTiles = new int[tileColumns * tileRows];
            int changedPixels = 0;

            for (int y = 0; y < leftHeight; y++)
            {
                int row = y * leftWidth * 4;
                for (int x = 0; x < leftWidth; x++)
                {
                    int offset = row + x * 4;
                    if (!PixelChanged(leftBgra, rightBgra, offset)) continue;
                    changedPixels++;
                    changedTiles[(y / TileSize) * tileColumns + x / TileSize]++;
                }
            }

            if (changedPixels == 0)
                return new PageDifferenceResult(true, 0, 0, []);

            var active = new bool[changedTiles.Length];
            for (int i = 0; i < active.Length; i++)
                active[i] = changedTiles[i] >= 2;

            var visited = new bool[active.Length];
            var regions = new List<DifferenceRegion>();
            for (int ty = 0; ty < tileRows; ty++)
            for (int tx = 0; tx < tileColumns; tx++)
            {
                int seed = ty * tileColumns + tx;
                if (!active[seed] || visited[seed]) continue;

                int minX = tx, maxX = tx, minY = ty, maxY = ty, pixels = 0;
                var pending = new Queue<(int x, int y)>();
                pending.Enqueue((tx, ty));
                visited[seed] = true;
                while (pending.Count > 0)
                {
                    var (cx, cy) = pending.Dequeue();
                    minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                    minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                    pixels += changedTiles[cy * tileColumns + cx];
                    Add(cx - 1, cy); Add(cx + 1, cy); Add(cx, cy - 1); Add(cx, cy + 1);

                    void Add(int nx, int ny)
                    {
                        if ((uint)nx >= (uint)tileColumns || (uint)ny >= (uint)tileRows) return;
                        int index = ny * tileColumns + nx;
                        if (!active[index] || visited[index]) return;
                        visited[index] = true;
                        pending.Enqueue((nx, ny));
                    }
                }

                int x0 = Math.Max(0, minX * TileSize - 3);
                int y0 = Math.Max(0, minY * TileSize - 3);
                int x1 = Math.Min(leftWidth, (maxX + 1) * TileSize + 3);
                int y1 = Math.Min(leftHeight, (maxY + 1) * TileSize + 3);
                regions.Add(new DifferenceRegion(x0, y0, x1 - x0, y1 - y0, pixels));
            }

            return new PageDifferenceResult(true, changedPixels,
                changedPixels / (double)(leftWidth * leftHeight), regions);
        }

        private static bool PixelChanged(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int offset)
        {
            int db = Math.Abs(left[offset] - right[offset]);
            int dg = Math.Abs(left[offset + 1] - right[offset + 1]);
            int dr = Math.Abs(left[offset + 2] - right[offset + 2]);
            int da = Math.Abs(left[offset + 3] - right[offset + 3]);
            return Math.Max(Math.Max(db, dg), Math.Max(dr, da)) > ChannelThreshold;
        }

        private static void Validate(ReadOnlySpan<byte> pixels, int width, int height, string name)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (pixels.Length != checked(width * height * 4))
                throw new ArgumentException("Pixel data must contain one BGRA value per pixel.", name);
        }
    }
}
