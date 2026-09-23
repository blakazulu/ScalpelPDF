using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>
    /// Picks the line of text a double-click lands on, for in-place text editing. Works on plain
    /// word boxes in canvas space (y grows downward) so it can be tested without a PDF.
    ///
    /// <para>The whole visual line is chosen, not just the words whose boxes the click's height
    /// happens to cross: words are grouped into lines by vertical overlap first, so a hyphen, a
    /// superscript or dot leaders whose small boxes miss the click still belong to the line (they
    /// sit under the white cover, so leaving them out of the edit deleted them). A line is only
    /// split at a gap several ems wide - a column gutter or a table's cell gap - so a
    /// double-click on one column never merges it with its neighbour, while ordinary and
    /// justified word spacing keep the line whole.</para>
    /// </summary>
    public static class LinePicker
    {
        public readonly record struct WordBox(double Left, double Top, double Right, double Bottom)
        {
            public double Height => Bottom - Top;
            public double MidY => (Top + Bottom) / 2;
        }

        /// <summary>Gap, in multiples of the line's typical word height, that starts a new
        /// segment. Word spaces are ~0.3em and justified text rarely passes 1em; a column
        /// gutter or a table's cell gap is far wider.</summary>
        public const double SegmentGapEm = 2.5;

        /// <summary>
        /// Indexes (into <paramref name="words"/>) of the words making up the line segment under
        /// (<paramref name="x"/>, <paramref name="y"/>), left to right. When the click is not on
        /// any line, the nearest line is taken only if its nearest edge is within
        /// <paramref name="maxSnap"/>; otherwise the result is empty.
        /// </summary>
        public static List<int> Pick(IReadOnlyList<WordBox> words, double x, double y, double maxSnap = 8)
        {
            var none = new List<int>();
            if (words is null || words.Count == 0) return none;
            try
            {
                var lines = GroupIntoLines(words);

                // The line whose band holds the click (with a little slack); between two tightly
                // spaced lines, the one whose centre is closer.
                const double slack = 3;
                var hit = lines
                    .Where(l => y >= l.Top - slack && y <= l.Bottom + slack)
                    .OrderBy(l => Math.Abs((l.Top + l.Bottom) / 2 - y))
                    .FirstOrDefault();
                if (hit is null)
                {
                    static double EdgeDistance(Line l, double py) => py < l.Top ? l.Top - py : py - l.Bottom;
                    var nearest = lines.OrderBy(l => EdgeDistance(l, y)).First();
                    if (EdgeDistance(nearest, y) > maxSnap) return none;
                    hit = nearest;
                }

                var ordered = hit.Members.OrderBy(i => words[i].Left).ToList();
                double em = Median(ordered.Select(i => words[i].Height));
                double gapLimit = Math.Max(em, 1) * SegmentGapEm;

                var segments = new List<List<int>> { new() { ordered[0] } };
                for (int k = 1; k < ordered.Count; k++)
                {
                    double gap = words[ordered[k]].Left - words[ordered[k - 1]].Right;
                    if (gap > gapLimit) segments.Add([]);
                    segments[^1].Add(ordered[k]);
                }

                // The segment under the click, else the horizontally closest one.
                return segments
                    .OrderBy(seg =>
                    {
                        double l = words[seg[0]].Left, r = words[seg[^1]].Right;
                        return x < l ? l - x : x > r ? x - r : 0;
                    })
                    .First();
            }
            catch { return none; }
        }

        private sealed class Line
        {
            public List<int> Members { get; } = [];
            public double Top, Bottom;
        }

        /// <summary>A word joins a line when their vertical bands overlap by at least half the
        /// smaller height (the rule the copy path uses); bands grow as members join.</summary>
        private static List<Line> GroupIntoLines(IReadOnlyList<WordBox> words)
        {
            var lines = new List<Line>();
            // Tallest first, so a line's band is set by its real text before small marks join.
            foreach (int i in Enumerable.Range(0, words.Count).OrderByDescending(i => words[i].Height))
            {
                var w = words[i];
                Line? found = null;
                foreach (var l in lines)
                {
                    double overlap = Math.Min(l.Bottom, w.Bottom) - Math.Max(l.Top, w.Top);
                    double minH = Math.Min(l.Bottom - l.Top, w.Height);
                    if (minH > 0 ? overlap >= minH * 0.5 : w.MidY >= l.Top && w.MidY <= l.Bottom)
                    { found = l; break; }
                }
                if (found is null)
                {
                    found = new Line { Top = w.Top, Bottom = w.Bottom };
                    lines.Add(found);
                }
                else
                {
                    found.Top = Math.Min(found.Top, w.Top);
                    found.Bottom = Math.Max(found.Bottom, w.Bottom);
                }
                found.Members.Add(i);
            }
            return lines;
        }

        private static double Median(IEnumerable<double> values)
        {
            var v = values.Where(d => d > 0).OrderBy(d => d).ToList();
            return v.Count == 0 ? 0 : v[v.Count / 2];
        }
    }
}
