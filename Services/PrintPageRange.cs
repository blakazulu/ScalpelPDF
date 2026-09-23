using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>Which pages of the chosen range are actually printed.</summary>
    public enum PrintSubset
    {
        /// <summary>Every page in the range.</summary>
        All,
        /// <summary>Only pages whose 1-based number is odd.</summary>
        Odd,
        /// <summary>Only pages whose 1-based number is even.</summary>
        Even,
    }

    /// <summary>
    /// Parses a printed page range such as "1-3,5,9-" into 0-based page indices.
    /// <para>
    /// Two behaviours matter and both used to be wrong. Bounds are clamped before the loop runs,
    /// so a range like "1-2147483647" cannot spin the UI thread for minutes. And a range that
    /// matches no page returns an EMPTY list rather than silently falling back to every page,
    /// which is what made a typo print the whole document.
    /// </para>
    /// </summary>
    public static class PrintPageRange
    {
        /// <summary>
        /// Parses <paramref name="text"/> against a document of <paramref name="pageCount"/> pages
        /// and applies <paramref name="subset"/>. A blank range means every page.
        /// </summary>
        public static IReadOnlyList<int> Parse(string? text, int pageCount, PrintSubset subset = PrintSubset.All)
        {
            if (pageCount <= 0) return [];

            var trimmed = text?.Trim() ?? string.Empty;
            IEnumerable<int> baseSet;

            if (trimmed.Length == 0)
            {
                baseSet = Enumerable.Range(0, pageCount);
            }
            else
            {
                var set = new SortedSet<int>();
                foreach (var raw in trimmed.Split(','))
                {
                    var part = raw.Trim();
                    if (part.Length == 0) continue;

                    int dash = part.IndexOf('-');
                    if (dash >= 0)
                    {
                        var left = part.Substring(0, dash).Trim();
                        var right = part.Substring(dash + 1).Trim();

                        // An open end ("5-" or "-5") means "to the end" / "from the start".
                        bool hasA = long.TryParse(left, out long a);
                        bool hasB = long.TryParse(right, out long b);
                        if (!hasA && left.Length > 0) continue;
                        if (!hasB && right.Length > 0) continue;
                        if (!hasA) a = 1;
                        if (!hasB) b = pageCount;
                        if (a > b) (a, b) = (b, a);

                        // Clamp BEFORE iterating: an unclamped upper bound of int.MaxValue makes
                        // this loop run for minutes and freezes the window.
                        long lo = Math.Max(1, a);
                        long hi = Math.Min(pageCount, b);
                        for (long i = lo; i <= hi; i++) set.Add((int)(i - 1));
                    }
                    else if (long.TryParse(part, out long v))
                    {
                        if (v >= 1 && v <= pageCount) set.Add((int)(v - 1));
                    }
                }
                baseSet = set;
            }

            return subset switch
            {
                PrintSubset.Odd => [.. baseSet.Where(i => (i + 1) % 2 == 1)],
                PrintSubset.Even => [.. baseSet.Where(i => (i + 1) % 2 == 0)],
                _ => [.. baseSet],
            };
        }

        /// <summary>
        /// Number of physical sheets a job needs: one per selected page, halved (rounded up)
        /// when printing two-sided.
        /// </summary>
        public static int SheetCount(int selectedPages, int copies, bool duplex)
        {
            if (selectedPages <= 0 || copies <= 0) return 0;
            int perCopy = duplex ? (selectedPages + 1) / 2 : selectedPages;
            return perCopy * copies;
        }
    }
}
