using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Reconstructs a single LOGICAL-order line from words as a PDF text extractor (PdfPig)
        /// returns them: words ordered left-to-right by x, and each word's characters in VISUAL
        /// order. For a Hebrew/Arabic line both axes are flipped from logical — the logical-first
        /// word sits at the largest x, and within a word the logical-first letter is the rightmost.
        /// So for an RTL line we walk words right-to-left AND reverse each RTL word's characters;
        /// LTR words/numbers keep their order. An LTR line is returned left-to-right unchanged.
        /// Pure and defensive: never throws.
        /// </summary>
        public static string JoinWordsLogical(IReadOnlyList<(string Text, double Left)> words)
        {
            if (words is null || words.Count == 0) return "";
            try
            {
                bool rtl = ContainsRtl(string.Concat(words.Select(w => w.Text)));
                if (!rtl)
                    return string.Join(" ", words.OrderBy(w => w.Left).Select(w => w.Text));

                return string.Join(" ", words
                    .OrderByDescending(w => w.Left)
                    .Select(w => ContainsRtl(w.Text) ? ReverseChars(w.Text) : w.Text));
            }
            catch { return string.Join(" ", words.Select(w => w.Text)); }
        }

        private static string ReverseChars(string s)
        {
            char[] a = s.ToCharArray();
            System.Array.Reverse(a);
            return new string(a);
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

        // RTL scripts: Hebrew (U+0590-05FF) + Hebrew presentation forms (U+FB1D-FB4F);
        // Arabic (U+0600-06FF), Arabic Supplement (U+0750-077F), and Arabic presentation
        // forms A (U+FB50-FDFF) and B (U+FE70-FEFF). Latin digits/letters are not RTL.
        private static bool IsRtl(char c)
            => (c >= '\u0590' && c <= '\u05FF')
            || (c >= '\uFB1D' && c <= '\uFB4F')
            || (c >= '\u0600' && c <= '\u06FF')
            || (c >= '\u0750' && c <= '\u077F')
            || (c >= '\uFB50' && c <= '\uFDFF')
            || (c >= '\uFE70' && c <= '\uFEFF');
    }
}
