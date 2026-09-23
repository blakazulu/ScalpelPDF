using System;
using System.Collections.Generic;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Splits one line of text into visual-order segments, each tagged with a font family that
    /// actually has glyphs for it. PdfSharpCore draws a string in exactly one font, so a mixed
    /// line such as "צוות סקאלפל / The Scalpel Team" drawn wholly in Noto Sans Hebrew (which has
    /// no Latin letters, digits or most punctuation) renders the Latin part as .notdef boxes.
    /// Here every character goes to the candidate font when it covers it, otherwise to the first
    /// bundled fallback that does. Pure and defensive: never throws.
    /// </summary>
    public static class ScriptRuns
    {
        /// <summary>Bundled faces tried, in order, for characters the candidate lacks.</summary>
        public static readonly string[] Fallbacks = ["Noto Sans Hebrew", "Noto Sans Arabic", "Noto Sans"];

        /// <summary>Logical line -> visual (left-to-right glyph) order: Arabic is shaped first,
        /// then RTL text is reordered. LTR text is returned unchanged.</summary>
        public static string ToVisual(string? logical)
        {
            if (string.IsNullOrEmpty(logical)) return logical ?? "";
            try
            {
                if (!BidiReorder.ContainsRtl(logical)) return logical!;
                string shaped = ArabicShaper.ContainsArabic(logical) ? ArabicShaper.Shape(logical!) : logical!;
                return BidiReorder.ToVisual(shaped);
            }
            catch { return logical!; }
        }

        /// <summary>
        /// Splits an already-visual string into segments, left to right. <paramref name="covers"/>
        /// answers "does family F have a glyph for codepoint C". Whitespace and punctuation stay
        /// in the current segment's font when it covers them, so a line does not fragment at every
        /// space.
        /// </summary>
        public static List<(string Text, string Family)> Split(
            string? visual, string candidate, Func<string, int, bool> covers,
            IReadOnlyList<string>? fallbacks = null)
        {
            fallbacks ??= Fallbacks;
            var segs = new List<(string Text, string Family)>();
            if (string.IsNullOrEmpty(visual)) return segs;
            try
            {
                var sb = new StringBuilder();
                string? cur = null;
                foreach (char c in visual!)
                {
                    string face = FaceFor(c, cur, candidate, covers, fallbacks);
                    if (cur is not null && face != cur)
                    {
                        segs.Add((sb.ToString(), cur));
                        sb.Clear();
                    }
                    cur = face;
                    sb.Append(c);
                }
                if (sb.Length > 0) segs.Add((sb.ToString(), cur!));
                return segs;
            }
            catch { return [(visual!, candidate)]; }
        }

        private static string FaceFor(char c, string? cur, string candidate, Func<string, int, bool> covers,
            IReadOnlyList<string> fallbacks)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) return cur ?? candidate;
            bool neutral = !char.IsLetterOrDigit(c);
            if (neutral && cur is not null && Covers(covers, cur, c)) return cur;
            if (Covers(covers, candidate, c)) return candidate;
            foreach (var f in fallbacks)
                if (Covers(covers, f, c)) return f;
            return cur ?? candidate;
        }

        private static bool Covers(Func<string, int, bool> covers, string family, char c)
        {
            try { return covers(family, c); } catch { return false; }
        }
    }
}
