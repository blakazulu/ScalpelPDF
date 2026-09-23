using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>Resolved font info for a run of existing PDF text.</summary>
    public sealed record ResolvedFont(
        string DisplayName,   // cleaned, human-facing name (toast)
        string FamilyName,    // family to apply/draw with; substitute if not installed
        bool IsBold,
        bool IsItalic,
        bool IsInstalled);

    /// <summary>
    /// Normalizes raw PDF font names (PostScript-style, subset-prefixed) into a usable
    /// family name + style flags, and reports whether the family is installed.
    /// Pure and defensive: never throws; unknown input returns a safe default.
    /// Editor-side font name normalization + availability; distinct from <see cref="PdfFontResolver"/>
    /// which serves font bytes to PdfSharpCore for embedding.
    /// </summary>
    public static class FontResolver
    {
        private const string Fallback = "Segoe UI";
        private static readonly string[] BoldTokens   = { "bold", "black", "heavy", "semibold", "demibold" };
        private static readonly string[] ItalicTokens = { "italic", "oblique" };
        private static readonly string[] PsSuffixes   = { "psmt", "mt", "ps" }; // longest first

        /// <summary>
        /// PostScript base-font names that do not exist as installed Windows families. Without
        /// this map "Helvetica", "Courier" and friends resolve to nothing and every edited line
        /// silently falls back to the UI font, losing the document's typeface.
        /// </summary>
        private static readonly Dictionary<string, string> PsNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["helvetica"]         = "Arial",
            ["helveticaneue"]     = "Arial",
            ["arial"]             = "Arial",
            ["arialmt"]           = "Arial",
            ["arialnarrow"]       = "Arial Narrow",
            ["times"]             = "Times New Roman",
            ["timesnewroman"]     = "Times New Roman",
            ["timesnewromanps"]   = "Times New Roman",
            ["timesnewromanpsmt"] = "Times New Roman",
            ["courier"]           = "Courier New",
            ["couriernew"]        = "Courier New",
            ["couriernewps"]      = "Courier New",
            ["couriernewpsmt"]    = "Courier New",
            ["symbol"]            = "Symbol",
            ["zapfdingbats"]      = "Wingdings",
            ["segoeui"]           = "Segoe UI",
        };

        public static ResolvedFont Resolve(string? rawPdfFontName, IReadOnlyCollection<string> availableFamilies)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawPdfFontName))
                    return new ResolvedFont(Fallback, Fallback, false, false, true);

                string s = rawPdfFontName!.Trim();

                // 1. Strip subset prefix "ABCDEF+" (always 6 upper letters, but be lenient).
                int plus = s.IndexOf('+');
                if (plus >= 0 && plus <= 7) s = s[(plus + 1)..];

                // 2. Split family from style part on ',' or '-'.
                string stylePart = "";
                int sep = s.IndexOfAny(new[] { ',', '-' });
                if (sep >= 0)
                {
                    stylePart = s[(sep + 1)..];
                    s = s[..sep];
                }

                // 3. Detect style across the whole original (handles glued tokens too).
                string lowerAll = (stylePart + " " + s).ToLowerInvariant();
                bool isBold   = BoldTokens.Any(lowerAll.Contains);
                bool isItalic = ItalicTokens.Any(lowerAll.Contains);

                // 4. Drop a trailing PostScript suffix (PSMT/MT/PS) from the family token.
                string fam = s.Trim();
                foreach (var suf in PsSuffixes)
                {
                    if (fam.Length > suf.Length && fam.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                    {
                        fam = fam[..^suf.Length];
                        break;
                    }
                }

                // 5. Spacify CamelCase ("TimesNewRoman" -> "Times New Roman") so it can match
                //    WPF family names. Best-effort; leaves already-spaced or single words intact.
                string famTrimmed = fam.Trim();
                string display = Spacify(famTrimmed);
                if (string.IsNullOrWhiteSpace(display))
                    return new ResolvedFont(Fallback, Fallback, isBold, isItalic, true);

                // 6. Availability: case-insensitive match against the supplied set.
                string? match = availableFamilies?.FirstOrDefault(
                    f => string.Equals(f, display, StringComparison.OrdinalIgnoreCase));

                // 7. Not installed under its own name? Well-known PostScript base fonts have a
                //    Windows equivalent that carries the same design - Helvetica is Arial, Courier
                //    is Courier New. Without this the whole line falls back to the UI font and the
                //    document's typeface is lost. An installed family always wins over the map.
                if (match is null && PsNameMap.TryGetValue(famTrimmed.Replace(" ", ""), out var mappedFam))
                {
                    var mappedMatch = availableFamilies?.FirstOrDefault(
                        f => string.Equals(f, mappedFam, StringComparison.OrdinalIgnoreCase));
                    if (mappedMatch is not null)
                        return new ResolvedFont(mappedMatch, mappedMatch, isBold, isItalic, true);
                }

                bool installed = match is not null;
                string family = installed ? match! : Fallback;

                return new ResolvedFont(display, family, isBold, isItalic, installed);
            }
            catch
            {
                return new ResolvedFont(Fallback, Fallback, false, false, true);
            }
        }

        private static string Spacify(string name)
        {
            if (name.Length == 0 || name.Contains(' ')) return name;
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
