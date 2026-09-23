using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Content;

namespace Scalpel.Services
{
    /// <summary>
    /// Reading-order helpers for PdfPig word geometry (PDF space: points, bottom-left origin,
    /// so a larger Top is higher on the page). Words are grouped into visual lines by vertical
    /// overlap and ordered top-to-bottom; multi-column pages are detected and emitted one
    /// column at a time; each line chooses its own horizontal direction so mixed-language
    /// pages work too.
    /// </summary>
    public static class TextRunService
    {
        /// <summary>A visual line: its words (in reading order once ordered) and vertical band.</summary>
        public sealed class Band
        {
            public List<Word> Words { get; }
            public double Top { get; set; }
            public double Bottom { get; set; }
            public bool RightToLeft { get; set; }

            public Band(List<Word> words, double top, double bottom)
            {
                Words = words;
                Top = top;
                Bottom = bottom;
            }
        }

        /// <summary>Flattened reading order for a page's words: lines top-to-bottom, columns
        /// left-to-right (whole column at a time), words in each line's detected direction.</summary>
        public static IReadOnlyList<Word> OrderColumnAware(IEnumerable<Word> words)
        {
            var result = new List<Word>();
            foreach (Band band in OrderBands(words))
                result.AddRange(band.Words);
            return result;
        }

        /// <summary>Same as <see cref="OrderColumnAware(IEnumerable{Word})"/> but keeps the line
        /// structure: each band's words are sorted in that band's reading direction.</summary>
        public static List<Band> OrderBands(IEnumerable<Word> words)
        {
            var bands = GroupIntoLines(words);
            if (bands.Count == 0) return bands;

            // Reading order: lines top-to-bottom (PDF Y grows upward, so larger Top first).
            bands.Sort((a, b) => b.Top.CompareTo(a.Top));

            // A Y band spans the whole page, so on a two-column layout every "line" would mix
            // both columns and a drag down one column would sweep its neighbour. Split each band
            // into segments at column-gutter-sized gaps, cluster narrow segments into columns by
            // X overlap, and emit whole columns left-to-right (top-to-bottom inside each). Wide
            // segments - titles and footers spanning the text width - close the open column
            // section, so a title / two columns / footer page keeps a sane order. A single-column
            // page yields one cluster and comes out in exactly the plain top-to-bottom order.
            bands = ReorderColumns(bands);

            foreach (Band band in bands)
            {
                bool rtl = IsRightToLeftText(band.Words.Select(w => w.Text));
                band.RightToLeft = rtl;
                band.Words.Sort(rtl
                    ? (a, b) => b.BoundingBox.Right.CompareTo(a.BoundingBox.Right)
                    : (a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            }
            return bands;
        }

        /// <summary>Groups words into lines: a word joins a line when its vertical band overlaps
        /// the line's band by at least half the smaller height. Bands grow as members join.
        /// Output order is arrival order (unsorted).</summary>
        public static List<Band> GroupIntoLines(IEnumerable<Word> words)
        {
            var lines = new List<Band>();
            foreach (Word w in words)
            {
                var bb = w.BoundingBox;
                double wTop = bb.Top, wBottom = bb.Bottom;
                int found = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    double lTop = lines[i].Top, lBottom = lines[i].Bottom;
                    double overlap = Math.Min(lTop, wTop) - Math.Max(lBottom, wBottom);
                    double minH = Math.Min(lTop - lBottom, wTop - wBottom);
                    if (minH > 0 && overlap >= minH * 0.5) { found = i; break; }
                }
                if (found < 0)
                    lines.Add(new Band([w], wTop, wBottom));
                else
                {
                    Band line = lines[found];
                    line.Words.Add(w);
                    line.Top = Math.Max(line.Top, wTop);
                    line.Bottom = Math.Min(line.Bottom, wBottom);
                }
            }
            return lines;
        }

        /// <summary>Column-aware reordering of bands that already run top-to-bottom. The result
        /// is the same band shape, reordered (and split at column gutters) so the flattened word
        /// order reads one column at a time.</summary>
        public static List<Band> ReorderColumns(List<Band> bands)
        {
            if (bands.Count < 2) return bands;

            double textL = double.MaxValue, textR = double.MinValue;
            foreach (Band band in bands)
                foreach (Word w in band.Words)
                {
                    if (w.BoundingBox.Left < textL) textL = w.BoundingBox.Left;
                    if (w.BoundingBox.Right > textR) textR = w.BoundingBox.Right;
                }
            double wideW = (textR - textL) * 0.62;   // spans most of the text width = not a column line

            var reordered = new List<Band>();
            var pending = new List<(List<Word> Words, double Top, double Bottom, double L, double R)>();

            void Flush()
            {
                if (pending.Count == 0) return;
                // Cluster segments into columns by X-interval overlap (>= half the narrower range).
                var cols = new List<(double L, double R, List<int> Idx)>();
                var byLeft = Enumerable.Range(0, pending.Count).OrderBy(i => pending[i].L).ToList();
                foreach (int i in byLeft)
                {
                    var (_, _, _, L, R) = pending[i];
                    int hit = -1;
                    for (int c = 0; c < cols.Count && hit < 0; c++)
                    {
                        double ov = Math.Min(cols[c].R, R) - Math.Max(cols[c].L, L);
                        double minW = Math.Min(cols[c].R - cols[c].L, R - L);
                        if (minW > 0 && ov >= minW * 0.5) hit = c;
                    }
                    if (hit < 0) cols.Add((L, R, [i]));
                    else
                    {
                        var c0 = cols[hit];
                        c0.Idx.Add(i);
                        cols[hit] = (Math.Min(c0.L, L), Math.Max(c0.R, R), c0.Idx);
                    }
                }
                foreach (var (_, _, idx) in cols.OrderBy(c => c.L))
                    foreach (int i in idx.OrderByDescending(i => pending[i].Top))
                        reordered.Add(new Band(pending[i].Words, pending[i].Top, pending[i].Bottom));
                pending.Clear();
            }

            foreach (Band band in bands)
            {
                if (band.Words.Count == 0) continue;
                var sorted = band.Words.OrderBy(w => w.BoundingBox.Left).ToList();
                // Split threshold: well past a word space (~0.25em) but below a column gutter.
                double tw = 0; int tn = 0;
                foreach (Word w in sorted) { tw += w.BoundingBox.Width; tn += Math.Max(1, w.Text.Length); }
                double gapT = Math.Max(10, (tn > 0 ? tw / tn : 5) * 3);

                var segs = new List<List<Word>> { new() { sorted[0] } };
                for (int i = 1; i < sorted.Count; i++)
                {
                    if (sorted[i].BoundingBox.Left - sorted[i - 1].BoundingBox.Right > gapT)
                        segs.Add([]);
                    segs[segs.Count - 1].Add(sorted[i]);
                }
                foreach (List<Word> sws in segs)
                {
                    double sT = double.MinValue, sB = double.MaxValue, sL = double.MaxValue, sR = double.MinValue;
                    foreach (Word w in sws)
                    {
                        if (w.BoundingBox.Top > sT) sT = w.BoundingBox.Top;
                        if (w.BoundingBox.Bottom < sB) sB = w.BoundingBox.Bottom;
                        if (w.BoundingBox.Left < sL) sL = w.BoundingBox.Left;
                        if (w.BoundingBox.Right > sR) sR = w.BoundingBox.Right;
                    }
                    if (sR - sL >= wideW) { Flush(); reordered.Add(new Band(sws, sT, sB)); }
                    else pending.Add((sws, sT, sB, sL, sR));
                }
            }
            Flush();
            return reordered;
        }

        /// <summary>True when the text has more Hebrew/Arabic-script letters than other letters.</summary>
        public static bool IsRightToLeftText(IEnumerable<string> values)
        {
            int rtl = 0, ltr = 0;
            foreach (string value in values)
            {
                if (value is null) continue;
                foreach (char c in value)
                {
                    if ((c >= '\u0590' && c <= '\u08FF') ||
                        (c >= '\uFB1D' && c <= '\uFDFF') ||
                        (c >= '\uFE70' && c <= '\uFEFF')) rtl++;
                    else if (char.IsLetter(c)) ltr++;
                }
            }
            return rtl > ltr;
        }
    }
}
