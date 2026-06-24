using System.Collections.Generic;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal run-based bidi reorderer: converts a logical-order string to the visual
    /// (left-to-right glyph) order PdfSharpCore's DrawString needs. Base direction is
    /// RTL when any Hebrew is present. Not a complete UBA -- nested embeddings and
    /// directional marks are approximated; covers Hebrew with embedded numbers / Latin
    /// words / punctuation. Pure and defensive: never throws.
    /// </summary>
    public static class BidiReorder
    {
        public static bool ContainsRtl(string? s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s!) if (IsRtl(c)) return true;
            return false;
        }

        public static string ToVisual(string? logical)
        {
            if (string.IsNullOrEmpty(logical)) return logical ?? "";
            try
            {
                if (!ContainsRtl(logical)) return logical!;
                int n = logical!.Length;

                // Raw class: 'R' Hebrew, 'L' strong-LTR letter, 'E' digit, 'N' neutral.
                char[] raw = new char[n];
                for (int i = 0; i < n; i++)
                {
                    char c = logical[i];
                    if (IsRtl(c)) raw[i] = 'R';
                    else if (char.IsDigit(c)) raw[i] = 'E';
                    else if (char.IsLetter(c)) raw[i] = 'L';
                    else raw[i] = 'N';
                }

                // Resolved direction: 'R' or 'L' (digits order LTR -> 'L'). Neutrals adopt
                // a shared neighbour direction, else base (RTL).
                char[] res = new char[n];
                for (int i = 0; i < n; i++)
                {
                    if (raw[i] == 'R') res[i] = 'R';
                    else if (raw[i] == 'L' || raw[i] == 'E') res[i] = 'L';
                    else
                    {
                        char left = NearestStrong(raw, i, -1);
                        char right = NearestStrong(raw, i, +1);
                        res[i] = (left != '\0' && left == right) ? left : 'R';
                    }
                }

                // Build maximal same-direction runs in logical order.
                var runs = new List<(char Dir, int Start, int Len)>();
                int s = 0;
                while (s < n)
                {
                    int e = s + 1;
                    while (e < n && res[e] == res[s]) e++;
                    runs.Add((res[s], s, e - s));
                    s = e;
                }

                // Base RTL: emit runs right-to-left; reverse chars in R runs, keep L runs.
                var sb = new StringBuilder(n);
                for (int r = runs.Count - 1; r >= 0; r--)
                {
                    var run = runs[r];
                    if (run.Dir == 'R')
                        for (int i = run.Start + run.Len - 1; i >= run.Start; i--) sb.Append(logical[i]);
                    else
                        for (int i = run.Start; i < run.Start + run.Len; i++) sb.Append(logical[i]);
                }
                return sb.ToString();
            }
            catch { return logical!; }
        }

        private static char NearestStrong(char[] raw, int from, int dir)
        {
            for (int i = from + dir; i >= 0 && i < raw.Length; i += dir)
            {
                if (raw[i] == 'R') return 'R';
                if (raw[i] == 'L' || raw[i] == 'E') return 'L';
            }
            return '\0';
        }

        // Hebrew block U+0590-U+05FF; Hebrew presentation forms U+FB1D-U+FB4F
        private static bool IsRtl(char c)
            => (c >= '\u0590' && c <= '\u05FF') || (c >= '\uFB1D' && c <= '\uFB4F');
    }
}
