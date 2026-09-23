using System;
using System.Collections.Generic;
using System.Text;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// A font's <c>/ToUnicode</c> CMap: turns the byte codes in a content stream back into the
    /// characters a reader sees.
    ///
    /// <para>This is needed because the bytes in a PDF are usually not the text. A subset or CID
    /// font shows "SALARY" as glyph indices - PdfSharpCore writes exactly that,
    /// <c>&lt;00360024002F...&gt;</c> - so anything that wants to find a word by its content has to
    /// map the codes through this table first.</para>
    ///
    /// <para>Only the parts that matter for reading text back are implemented: the code width from
    /// <c>codespacerange</c>, and the <c>bfchar</c> / <c>bfrange</c> mappings.</para>
    /// </summary>
    public sealed class PdfToUnicodeMap
    {
        private readonly Dictionary<int, string> _single = [];
        private readonly List<(int Low, int High, int StartValue)> _ranges = [];

        /// <summary>Bytes per code, from the codespace range. 1 for simple fonts, 2 for CID.</summary>
        public int CodeByteWidth { get; private set; } = 1;

        /// <summary>True when the map has no usable entries.</summary>
        public bool IsEmpty => _single.Count == 0 && _ranges.Count == 0;

        /// <summary>Parses a CMap program. Never throws; an unparsable map comes back empty.</summary>
        public static PdfToUnicodeMap Parse(string cmap)
        {
            var map = new PdfToUnicodeMap();
            try
            {
                if (string.IsNullOrEmpty(cmap)) return map;

                map.CodeByteWidth = ReadCodeWidth(cmap);
                map.ReadSingles(cmap);
                map.ReadRanges(cmap);
            }
            catch { }
            return map;
        }

        /// <summary>
        /// Reads the font's ToUnicode map from a page's resources, or null when it has none.
        /// </summary>
        public static PdfToUnicodeMap? ForFont(PdfDictionary? page, string fontName)
        {
            try
            {
                if (page is null || string.IsNullOrEmpty(fontName)) return null;

                var resources = Deref(page.Elements["/Resources"]) as PdfDictionary;
                var fonts = Deref(resources?.Elements["/Font"]) as PdfDictionary;
                var font = Deref(fonts?.Elements["/" + fontName.TrimStart('/')]) as PdfDictionary;
                if (font is null) return null;

                // A Type0 font keeps its ToUnicode at the top level, so no descendant walk needed.
                var toUnicode = Deref(font.Elements["/ToUnicode"]) as PdfDictionary;
                byte[]? bytes = toUnicode?.Stream?.UnfilteredValue;
                if (bytes is null || bytes.Length == 0) return null;

                return Parse(Encoding.GetEncoding(28591).GetString(bytes));
            }
            catch { return null; }
        }

        /// <summary>Decodes a run of raw code bytes into text.</summary>
        public string Decode(byte[] codes)
        {
            var sb = new StringBuilder();
            int width = CodeByteWidth == 2 ? 2 : 1;

            for (int i = 0; i + width <= codes.Length; i += width)
            {
                int code = width == 2 ? (codes[i] << 8) | codes[i + 1] : codes[i];
                sb.Append(Lookup(code));
            }
            return sb.ToString();
        }

        /// <summary>The text for one code, or the code itself as a character when unmapped.</summary>
        private string Lookup(int code)
        {
            if (_single.TryGetValue(code, out string? exact)) return exact;

            foreach (var (low, high, start) in _ranges)
                if (code >= low && code <= high)
                    return char.ConvertFromUtf32(start + (code - low));

            // Unmapped: fall back to treating the code as a character, which is right for the
            // simple-font case where codes ARE the characters.
            return code is > 0 and < 0x110000 ? char.ConvertFromUtf32(code) : string.Empty;
        }

        /// <summary>Code width in bytes, taken from the first codespacerange entry.</summary>
        private static int ReadCodeWidth(string cmap)
        {
            int at = cmap.IndexOf("begincodespacerange", StringComparison.Ordinal);
            if (at < 0) return 1;
            int end = cmap.IndexOf("endcodespacerange", at, StringComparison.Ordinal);
            if (end < 0) return 1;

            var tokens = HexTokens(cmap.Substring(at, end - at));
            // Each hex token is one bound; its digit count gives the width.
            return tokens.Count > 0 && tokens[0].Length >= 4 ? 2 : 1;
        }

        private void ReadSingles(string cmap)
        {
            foreach (var (body, _) in Sections(cmap, "beginbfchar", "endbfchar"))
            {
                var tokens = HexTokens(body);
                for (int i = 0; i + 1 < tokens.Count; i += 2)
                {
                    int code = ToInt(tokens[i]);
                    string value = HexToText(tokens[i + 1]);
                    if (code >= 0 && value.Length > 0) _single[code] = value;
                }
            }
        }

        private void ReadRanges(string cmap)
        {
            foreach (var (body, _) in Sections(cmap, "beginbfrange", "endbfrange"))
            {
                // Array form - <low> <high> [<v1> <v2> ...] - maps each code separately.
                foreach (var (arrayBody, before) in Sections(body, "[", "]"))
                {
                    var bounds = HexTokens(before);
                    var values = HexTokens(arrayBody);
                    if (bounds.Count < 2) continue;
                    int low = ToInt(bounds[bounds.Count - 2]);
                    for (int k = 0; k < values.Count; k++)
                    {
                        string value = HexToText(values[k]);
                        if (low + k >= 0 && value.Length > 0) _single[low + k] = value;
                    }
                }

                // Simple form: <low> <high> <startValue>, in triples.
                string withoutArrays = RemoveArrays(body);
                var tokens = HexTokens(withoutArrays);
                for (int i = 0; i + 2 < tokens.Count; i += 3)
                {
                    int low = ToInt(tokens[i]);
                    int high = ToInt(tokens[i + 1]);
                    int start = FirstCodePoint(tokens[i + 2]);
                    if (low >= 0 && high >= low && start > 0) _ranges.Add((low, high, start));
                }
            }
        }

        /// <summary>Every &lt;hex&gt; token in a fragment, without the angle brackets.</summary>
        private static List<string> HexTokens(string fragment)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < fragment.Length)
            {
                int open = fragment.IndexOf('<', i);
                if (open < 0) break;
                int close = fragment.IndexOf('>', open + 1);
                if (close < 0) break;
                tokens.Add(fragment.Substring(open + 1, close - open - 1).Trim());
                i = close + 1;
            }
            return tokens;
        }

        /// <summary>Each region between a start and end keyword, with the text preceding it.</summary>
        private static List<(string Body, string Before)> Sections(string text, string start, string end)
        {
            var found = new List<(string, string)>();
            int i = 0, previousEnd = 0;
            while (true)
            {
                int a = text.IndexOf(start, i, StringComparison.Ordinal);
                if (a < 0) break;
                int b = text.IndexOf(end, a + start.Length, StringComparison.Ordinal);
                if (b < 0) break;
                found.Add((text.Substring(a + start.Length, b - a - start.Length),
                           text.Substring(previousEnd, a - previousEnd)));
                i = b + end.Length;
                previousEnd = i;
            }
            return found;
        }

        private static string RemoveArrays(string body)
        {
            var sb = new StringBuilder(body.Length);
            int depth = 0;
            foreach (char c in body)
            {
                if (c == '[') { depth++; continue; }
                if (c == ']') { if (depth > 0) depth--; continue; }
                if (depth == 0) sb.Append(c);
            }
            return sb.ToString();
        }

        private static int ToInt(string hex)
        {
            try { return Convert.ToInt32(hex, 16); }
            catch { return -1; }
        }

        /// <summary>A hex value as text: UTF-16BE, so surrogate pairs survive.</summary>
        private static string HexToText(string hex)
        {
            try
            {
                if (hex.Length % 2 == 1) hex += "0";
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                return Encoding.BigEndianUnicode.GetString(bytes);
            }
            catch { return string.Empty; }
        }

        private static int FirstCodePoint(string hex)
        {
            string text = HexToText(hex);
            return text.Length == 0 ? -1 : char.ConvertToUtf32(text, 0);
        }

        private static PdfItem? Deref(PdfItem? item)
        {
            if (item is null) return null;
            try
            {
                var valueProp = item.GetType().GetProperty("Value",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (valueProp?.GetValue(item) is PdfObject resolved) return resolved;
            }
            catch { }
            return item;
        }
    }
}
