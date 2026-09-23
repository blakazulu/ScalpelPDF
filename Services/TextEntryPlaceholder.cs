using System;
using System.Collections.Generic;
using System.Windows;

namespace Scalpel.Services
{
    /// <summary>Recognizes printed dotted or ruled blanks used as entry areas on flattened forms.</summary>
    public static class TextEntryPlaceholder
    {
        /// <summary>One text run on the page with its canvas-space bounds.</summary>
        public readonly record struct Candidate(string Text, Rect Bounds);

        /// <summary>The bounds of the placeholder run nearest <paramref name="click"/> (with a
        /// generous hit slop around each run), or null when the click is not on one.</summary>
        public static Rect? FindNearest(IEnumerable<Candidate> candidates, Point click)
        {
            Rect? best = null;
            double bestDistance = double.MaxValue;
            foreach (Candidate candidate in candidates)
            {
                if (!IsPlaceholder(candidate.Text) || candidate.Bounds.IsEmpty) continue;
                Rect hit = candidate.Bounds;
                hit.Inflate(Math.Max(8, candidate.Bounds.Height * .75),
                    Math.Max(6, candidate.Bounds.Height * .6));
                if (!hit.Contains(click)) continue;

                double dx = click.X < candidate.Bounds.Left ? candidate.Bounds.Left - click.X
                    : click.X > candidate.Bounds.Right ? click.X - candidate.Bounds.Right : 0;
                double dy = click.Y < candidate.Bounds.Top ? candidate.Bounds.Top - click.Y
                    : click.Y > candidate.Bounds.Bottom ? click.Y - candidate.Bounds.Bottom : 0;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    best = candidate.Bounds;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>True for a run made only of dots, underscores, bullets or dashes (at least
        /// four of them, whitespace ignored).</summary>
        public static bool IsPlaceholder(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            int marks = 0, other = 0;
            foreach (char ch in text!)
            {
                if (char.IsWhiteSpace(ch)) continue;
                if (ch is '.' or '_' or '\u00B7' or '\u2022' or '\u2024' or '\u2026' or '-' or '\u2010' or '\u2011')
                    marks++;
                else
                    other++;
            }
            return marks >= 4 && other == 0;
        }
    }
}
