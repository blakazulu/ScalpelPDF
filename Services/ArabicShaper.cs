using System.Collections.Generic;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal Arabic cursive shaper: maps logical-order Arabic base letters to their
    /// contextual Arabic Presentation Forms-B glyphs (U+FE70–FEFF) using the joining
    /// algorithm, and collapses lam+alef into the mandatory ligature. Combining marks
    /// (harakat) are transparent (do not break joining). Does NOT reorder — that is left
    /// to <see cref="BidiReorder.ToVisual"/>. PdfSharpCore's DrawString applies no GSUB,
    /// so substituting presentation forms here is what makes burned-in Arabic connect.
    /// Pure; never throws.
    /// </summary>
    public static class ArabicShaper
    {
        // Joining type per base letter.
        private enum J { U /*non-joining*/, R /*right-joining only*/, D /*dual*/, T /*transparent*/ }

        // forms: [isolated, final, initial, medial] as char codepoints (0 = n/a).
        private static readonly Dictionary<char, (J join, char[] forms)> Table = Build();

        public static bool ContainsArabic(string? s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s!)
                if (c >= '؀' && c <= 'ۿ') return true;
            return false;
        }

        public static string Shape(string? logical)
        {
            if (string.IsNullOrEmpty(logical)) return logical ?? "";
            try
            {
                if (!ContainsArabic(logical)) return logical!;
                int n = logical!.Length;
                var outp = new StringBuilder(n);

                for (int i = 0; i < n; i++)
                {
                    char c = logical[i];
                    if (!Table.TryGetValue(c, out var info)) { outp.Append(c); continue; }
                    if (info.join == J.T) { outp.Append(c); continue; } // harakat stay in place

                    // Lam-Alef mandatory ligature: LAM followed (skipping marks) by an alef variant.
                    if (c == 'ل' && NextAlef(logical, i, out int alefIdx, out char alefVar))
                    {
                        bool joinsBack = JoinsPrev(logical, i);
                        var (iso, fin) = LamAlef(alefVar);
                        outp.Append(joinsBack ? fin : iso);
                        i = alefIdx; // consume the alef too
                        continue;
                    }

                    bool prev = JoinsPrev(logical, i);                       // a dual letter precedes me
                    bool next = info.join == J.D && JoinsNext(logical, i);   // I join forward and next accepts
                    int slot = (prev, next) switch
                    {
                        (false, false) => 0, // isolated
                        (true,  false) => 1, // final
                        (false, true ) => 2, // initial
                        (true,  true ) => 3, // medial
                    };
                    if (info.join != J.D && slot >= 2) slot -= 2; // R/U letters: only isolated/final
                    char g = info.forms[slot];
                    outp.Append(g != '\0' ? g : c);
                }
                return outp.ToString();
            }
            catch { return logical!; }
        }

        // The previous non-transparent base is dual-joining (so it connects to me).
        private static bool JoinsPrev(string s, int i)
        {
            for (int k = i - 1; k >= 0; k--)
            {
                if (!Table.TryGetValue(s[k], out var info)) return false;
                if (info.join == J.T) continue;
                return info.join == J.D;
            }
            return false;
        }

        // The next non-transparent base accepts a join from the right (dual or right-joining).
        private static bool JoinsNext(string s, int i)
        {
            for (int k = i + 1; k < s.Length; k++)
            {
                if (!Table.TryGetValue(s[k], out var info)) return false;
                if (info.join == J.T) continue;
                return info.join == J.D || info.join == J.R;
            }
            return false;
        }

        // Next non-transparent base is an alef variant (for lam-alef ligature).
        private static bool NextAlef(string s, int i, out int idx, out char alef)
        {
            idx = -1; alef = '\0';
            for (int k = i + 1; k < s.Length; k++)
            {
                if (!Table.TryGetValue(s[k], out var info)) return false;
                if (info.join == J.T) continue;
                char c = s[k];
                if (c == 'ا' || c == 'آ' || c == 'أ' || c == 'إ')
                { idx = k; alef = c; return true; }
                return false;
            }
            return false;
        }

        // (isolated, final) lam-alef ligature by alef variant.
        private static (char iso, char fin) LamAlef(char alef) => alef switch
        {
            'آ' => ('ﻵ', 'ﻶ'), // madda
            'أ' => ('ﻷ', 'ﻸ'), // hamza above
            'إ' => ('ﻹ', 'ﻺ'), // hamza below
            _        => ('ﻻ', 'ﻼ'), // plain alef (0627)
        };

        private static Dictionary<char, (J, char[])> Build()
        {
            var d = new Dictionary<char, (J, char[])>();
            // forms: isolated, final, initial, medial (0 where not applicable)
            void Add(char b, J j, int iso, int fin, int ini = 0, int med = 0)
                => d[b] = (j, new[] { (char)iso, (char)fin, (char)ini, (char)med });

            Add('ء', J.U, 0xFE80, 0xFE80);                         // HAMZA
            Add('آ', J.R, 0xFE81, 0xFE82);                         // ALEF MADDA
            Add('أ', J.R, 0xFE83, 0xFE84);                         // ALEF HAMZA ABOVE
            Add('ؤ', J.R, 0xFE85, 0xFE86);                         // WAW HAMZA
            Add('إ', J.R, 0xFE87, 0xFE88);                         // ALEF HAMZA BELOW
            Add('ئ', J.D, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C);         // YEH HAMZA
            Add('ا', J.R, 0xFE8D, 0xFE8E);                         // ALEF
            Add('ب', J.D, 0xFE8F, 0xFE90, 0xFE91, 0xFE92);         // BEH
            Add('ة', J.R, 0xFE93, 0xFE94);                         // TEH MARBUTA
            Add('ت', J.D, 0xFE95, 0xFE96, 0xFE97, 0xFE98);         // TEH
            Add('ث', J.D, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C);         // THEH
            Add('ج', J.D, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0);         // JEEM
            Add('ح', J.D, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4);         // HAH
            Add('خ', J.D, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8);         // KHAH
            Add('د', J.R, 0xFEA9, 0xFEAA);                         // DAL
            Add('ذ', J.R, 0xFEAB, 0xFEAC);                         // THAL
            Add('ر', J.R, 0xFEAD, 0xFEAE);                         // REH
            Add('ز', J.R, 0xFEAF, 0xFEB0);                         // ZAIN
            Add('س', J.D, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4);         // SEEN
            Add('ش', J.D, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8);         // SHEEN
            Add('ص', J.D, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC);         // SAD
            Add('ض', J.D, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0);         // DAD
            Add('ط', J.D, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4);         // TAH
            Add('ظ', J.D, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8);         // ZAH
            Add('ع', J.D, 0xFEC9, 0xFECA, 0xFECB, 0xFECC);         // AIN
            Add('غ', J.D, 0xFECD, 0xFECE, 0xFECF, 0xFED0);         // GHAIN
            Add('ف', J.D, 0xFED1, 0xFED2, 0xFED3, 0xFED4);         // FEH
            Add('ق', J.D, 0xFED5, 0xFED6, 0xFED7, 0xFED8);         // QAF
            Add('ك', J.D, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC);         // KAF
            Add('ل', J.D, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0);         // LAM
            Add('م', J.D, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4);         // MEEM
            Add('ن', J.D, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8);         // NOON
            Add('ه', J.D, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC);         // HEH
            Add('و', J.R, 0xFEED, 0xFEEE);                         // WAW
            Add('ى', J.R, 0xFEEF, 0xFEF0);                         // ALEF MAKSURA
            Add('ي', J.D, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4);         // YEH

            // Transparent combining marks (harakat, superscript alef, shadda, sukun).
            foreach (char m in new[] { 'ً','ٌ','ٍ','َ','ُ','ِ','ّ','ْ','ٰ' })
                d[m] = (J.T, new[] { m, m, m, m });

            return d;
        }
    }
}
