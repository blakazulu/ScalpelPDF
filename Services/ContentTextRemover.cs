using System;
using System.Collections.Generic;
using System.Text;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// Removes a specific run of text from a page's content stream.
    ///
    /// <para><b>Why this exists.</b> Editing text used to be done by painting an opaque white
    /// rectangle over the original and drawing the replacement on top. That hides the old words on
    /// screen but leaves them in the content stream, so selecting, searching or running any text
    /// extractor over the saved file returns BOTH the original and the replacement. Someone who
    /// edits a salary, a name or an address reasonably believes they changed it; they had not.</para>
    ///
    /// <para><b>Why by text and not by position.</b> Working out where a run sits on the page means
    /// tracking the text and transformation matrices through the whole stream, and getting that
    /// subtly wrong deletes the wrong words. Scalpel already knows the exact original string (the
    /// edit captured it), so the run is matched by its content instead - far less to get wrong.</para>
    ///
    /// <para><b>Safety rule.</b> Only the string operands are emptied; every operator, array and
    /// byte of structure is left exactly where it was, so the stream cannot become malformed. If no
    /// unambiguous match is found - most often a subset font with a custom encoding, where the
    /// bytes in the file are not the characters on the page - nothing is changed at all and the
    /// caller keeps the opaque cover as its fallback.</para>
    /// </summary>
    public static class ContentTextRemover
    {
        /// <summary>One text-showing operand found in the stream.</summary>
        private readonly record struct Operand(int Start, int Length, string Text);

        /// <summary>
        /// Clears every show-text operand that makes up <paramref name="text"/> on this page.
        /// Returns the number of operands cleared; 0 means nothing was changed.
        /// </summary>
        public static int RemoveText(PdfPage page, string text)
        {
            if (page is null || string.IsNullOrEmpty(text)) return 0;
            try
            {
                var contents = page.Contents;

                // The run must be unique on the whole PAGE, not just in one of its streams: two
                // lines reading "Name: Dan" can sit in different content streams, and removing
                // the first one found would delete the line the user did not edit.
                int matches = 0;
                for (int i = 0; i < contents.Elements.Count; i++)
                {
                    try
                    {
                        byte[]? raw = contents.Elements.GetDictionary(i)?.Stream?.UnfilteredValue;
                        if (raw is not null && raw.Length > 0) matches += CountMatches(raw, text, page);
                    }
                    catch { }
                }
                if (matches != 1) return 0;

                int total = 0;
                for (int i = 0; i < contents.Elements.Count; i++)
                {
                    try
                    {
                        // GetDictionary resolves the indirect reference; a content stream is a
                        // dictionary with a stream attached.
                        var content = contents.Elements.GetDictionary(i);
                        byte[]? raw = content?.Stream?.UnfilteredValue;
                        if (raw is null || raw.Length == 0) continue;

                        byte[]? edited = RemoveFromStream(raw, text, out int cleared, page);
                        if (edited is null || cleared == 0) continue;

                        // Store it back uncompressed: the bytes changed length, and rewriting the
                        // filtered form would mean re-deflating for no benefit here.
                        content!.Stream.Value = edited;
                        content.Elements.Remove("/Filter");
                        content.Elements.Remove("/DecodeParms");
                        content.Elements.SetInteger("/Length", edited.Length);
                        total += cleared;
                    }
                    catch { }   // one unreadable stream must not abandon the others
                }
                return total;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Returns the stream with the operands spelling <paramref name="target"/> emptied, or null
        /// when the text is not found. Exposed for testing.
        /// </summary>
        public static byte[]? RemoveFromStream(byte[] stream, string target, out int cleared,
                                               PdfDictionary? page = null)
        {
            cleared = 0;
            if (stream is null || stream.Length == 0 || string.IsNullOrEmpty(target)) return null;

            var operands = FindShowTextOperands(stream, page);
            if (operands.Count == 0) return null;

            // Match across consecutive operands: a single visible word is frequently split into
            // several show operations by kerning, so "SECRET" can be (SEC) (RET) in the file.
            string needle = Normalize(target);
            if (needle.Length == 0) return null;

            var hits = FindRuns(operands, needle);
            if (hits.Count != 1) return null;     // absent, or ambiguous: change nothing

            var (from, to) = hits[0];
            var result = new List<byte>(stream.Length);
            int cursor = 0;
            for (int i = from; i <= to; i++)
            {
                var op = operands[i];
                result.AddRange(new ArraySegment<byte>(stream, cursor, op.Start - cursor));
                // The operand is replaced by an empty string of the same kind, so the operator
                // that follows it still receives exactly one operand.
                result.Add((byte)'(');
                result.Add((byte)')');
                cursor = op.Start + op.Length;
                cleared++;
            }
            result.AddRange(new ArraySegment<byte>(stream, cursor, stream.Length - cursor));
            return result.ToArray();
        }

        /// <summary>Number of places in this stream where <paramref name="target"/> could be
        /// removed safely (see <see cref="FindRuns"/>).</summary>
        public static int CountMatches(byte[] stream, string target, PdfDictionary? page = null)
        {
            if (stream is null || stream.Length == 0 || string.IsNullOrEmpty(target)) return 0;
            string needle = Normalize(target);
            if (needle.Length == 0) return 0;
            var operands = FindShowTextOperands(stream, page);
            return operands.Count == 0 ? 0 : FindRuns(operands, needle).Count;
        }

        /// <summary>
        /// Finds every run of operands that spells exactly the needle.
        ///
        /// <para>The page's shown text is concatenated once, remembering which operand produced
        /// each character, so a match maps back to precisely the operands it covers. Growing a
        /// span from the first operand instead would swallow everything before the match - on
        /// "(KEEP THIS) Tj (SECRET) Tj" the concatenation contains the needle by operand 1, and
        /// clearing operands 0..1 would delete the line above as well.</para>
        ///
        /// <para>A match only counts when it starts at the first character of an operand and ends
        /// at the last character of one. Operands are cleared whole, so a match inside a longer
        /// operand would take its neighbours with it: editing "TOTAL 100" must not empty
        /// "(SUBTOTAL 100)", and "100" must not empty "(Invoice 2100)".</para>
        /// </summary>
        private static List<(int From, int To)> FindRuns(List<Operand> operands, string needle)
        {
            var runs = new List<(int From, int To)>();
            var text = new StringBuilder();
            var owner = new List<int>();          // owner[i] = operand that produced character i

            for (int i = 0; i < operands.Count; i++)
            {
                string piece = Normalize(operands[i].Text);
                text.Append(piece);
                for (int k = 0; k < piece.Length; k++) owner.Add(i);
            }

            string all = text.ToString();
            for (int at = all.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = all.IndexOf(needle, at + 1, StringComparison.Ordinal))
            {
                int last = at + needle.Length - 1;
                if (last >= owner.Count) break;
                bool startsOperand = at == 0 || owner[at - 1] != owner[at];
                bool endsOperand = last == owner.Count - 1 || owner[last + 1] != owner[last];
                if (startsOperand && endsOperand) runs.Add((owner[at], owner[last]));
            }
            return runs;
        }

        /// <summary>
        /// Whitespace is not comparable between a content stream and the text a viewer shows: a
        /// run may carry trailing spaces, and a line break in the file is a positioning operator
        /// rather than a character. Compare on the visible characters only.
        /// </summary>
        private static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// Locates every string operand belonging to a text-showing operator (Tj, TJ, ' and "),
        /// in stream order.
        /// </summary>
        private static List<Operand> FindShowTextOperands(byte[] s, PdfDictionary? page)
        {
            var found = new List<Operand>();
            var maps = new Dictionary<string, PdfToUnicodeMap?>(StringComparer.Ordinal);
            PdfToUnicodeMap? active = null;      // the font selected by the most recent Tf
            int i = 0;
            while (i < s.Length)
            {
                byte b = s[i];

                // Track the selected font: its ToUnicode map is what turns the codes in the
                // following strings back into readable characters.
                if (b == (byte)'/')
                {
                    int nameEnd = i + 1;
                    while (nameEnd < s.Length && !IsDelimiter(s[nameEnd])) nameEnd++;
                    string name = Latin1(s, i + 1, nameEnd - i - 1);
                    if (FollowedByTf(s, nameEnd))
                    {
                        if (!maps.TryGetValue(name, out active))
                        {
                            active = PdfToUnicodeMap.ForFont(page, name);
                            maps[name] = active;
                        }
                    }
                    i = nameEnd;
                    continue;
                }

                if (b == (byte)'%')                       // comment to end of line
                {
                    while (i < s.Length && s[i] != (byte)'\n' && s[i] != (byte)'\r') i++;
                    continue;
                }

                if (b == (byte)'(')
                {
                    int end = SkipLiteralString(s, i);
                    if (end < 0) break;
                    if (OperatorShowsText(s, end + 1))
                        found.Add(new Operand(i, end - i + 1, DecodeLiteral(s, i, end, active)));
                    i = end + 1;
                    continue;
                }

                if (b == (byte)'<' && i + 1 < s.Length && s[i + 1] != (byte)'<')
                {
                    int end = Array.IndexOf(s, (byte)'>', i);
                    if (end < 0) break;
                    if (OperatorShowsText(s, end + 1))
                        found.Add(new Operand(i, end - i + 1, DecodeHex(s, i, end, active)));
                    i = end + 1;
                    continue;
                }

                i++;
            }
            return found;
        }

        /// <summary>
        /// True when the next operator after this operand shows text. Inside a TJ array the
        /// operand is followed by more array items before the operator, so the scan steps over
        /// numbers, whitespace and the closing bracket to find it.
        /// </summary>
        private static bool OperatorShowsText(byte[] s, int from)
        {
            int i = from;
            int guard = 0;
            while (i < s.Length && guard++ < 4096)
            {
                byte b = s[i];
                if (b == (byte)' ' || b == (byte)'\r' || b == (byte)'\n' || b == (byte)'\t'
                    || b == 0 || b == (byte)'\f') { i++; continue; }
                if (b == (byte)']' || b == (byte)'-' || b == (byte)'.' || b == (byte)'+'
                    || (b >= (byte)'0' && b <= (byte)'9')) { i++; continue; }

                // Another string in the same TJ array: still heading for a text operator.
                if (b == (byte)'(') { int e = SkipLiteralString(s, i); if (e < 0) return false; i = e + 1; continue; }
                if (b == (byte)'<') { int e = Array.IndexOf(s, (byte)'>', i); if (e < 0) return false; i = e + 1; continue; }

                if (b == (byte)'T' && i + 1 < s.Length && (s[i + 1] == (byte)'j' || s[i + 1] == (byte)'J'))
                    return true;
                return b == (byte)'\'' || b == (byte)'"';
            }
            return false;
        }

        /// <summary>Index of the ')' closing the literal string that starts at <paramref name="start"/>.</summary>
        private static int SkipLiteralString(byte[] s, int start)
        {
            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                byte b = s[i];
                if (b == (byte)'\\') { i++; continue; }          // escaped character
                if (b == (byte)'(') depth++;
                else if (b == (byte)')') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>
        /// Decodes a literal string operand: PDF escapes first, then the font's ToUnicode map if
        /// there is one (the bytes are codes, not characters, in a subset or CID font).
        /// </summary>
        private static string DecodeLiteral(byte[] s, int start, int end, PdfToUnicodeMap? map)
        {
            var sb = new StringBuilder();
            for (int i = start + 1; i < end; i++)
            {
                byte b = s[i];
                if (b != (byte)'\\') { sb.Append((char)b); continue; }

                if (++i > end) break;
                byte e = s[i];
                switch (e)
                {
                    case (byte)'n': sb.Append('\n'); break;
                    case (byte)'r': sb.Append('\r'); break;
                    case (byte)'t': sb.Append('\t'); break;
                    case (byte)'b': sb.Append('\b'); break;
                    case (byte)'f': sb.Append('\f'); break;
                    case (byte)'(': sb.Append('('); break;
                    case (byte)')': sb.Append(')'); break;
                    case (byte)'\\': sb.Append('\\'); break;
                    default:
                        if (e >= (byte)'0' && e <= (byte)'7')
                        {
                            int value = e - '0';
                            for (int k = 0; k < 2 && i + 1 <= end && s[i + 1] >= (byte)'0' && s[i + 1] <= (byte)'7'; k++)
                                value = value * 8 + (s[++i] - '0');
                            sb.Append((char)value);
                        }
                        else sb.Append((char)e);
                        break;
                }
            }
            return MapThrough(sb.ToString(), map);
        }

        /// <summary>Decodes a hex string operand. Two hex digits per byte, odd trailing digit padded.</summary>
        private static string DecodeHex(byte[] s, int start, int end, PdfToUnicodeMap? map)
        {
            var digits = new StringBuilder();
            for (int i = start + 1; i < end; i++)
            {
                char c = (char)s[i];
                if (Uri.IsHexDigit(c)) digits.Append(c);
            }
            if (digits.Length % 2 == 1) digits.Append('0');

            var sb = new StringBuilder(digits.Length / 2);
            for (int i = 0; i + 1 < digits.Length; i += 2)
                sb.Append((char)Convert.ToInt32(digits.ToString(i, 2), 16));
            return MapThrough(sb.ToString(), map);
        }

        /// <summary>
        /// Turns raw character codes into the text a reader shows. Without a map the codes are
        /// taken as characters, which is correct for a simple font with a standard encoding.
        /// </summary>
        private static string MapThrough(string raw, PdfToUnicodeMap? map)
        {
            if (map is null || map.IsEmpty) return raw;
            var bytes = new byte[raw.Length];
            for (int i = 0; i < raw.Length; i++) bytes[i] = (byte)raw[i];
            return map.Decode(bytes);
        }

        /// <summary>True for a PDF delimiter or whitespace byte, which ends a name token.</summary>
        private static bool IsDelimiter(byte b)
            => b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0 or (byte)'\f'
                 or (byte)'/' or (byte)'[' or (byte)']' or (byte)'<' or (byte)'>'
                 or (byte)'(' or (byte)')' or (byte)'{' or (byte)'}' or (byte)'%';

        /// <summary>Reads a byte range as Latin-1 text.</summary>
        private static string Latin1(byte[] s, int start, int length)
        {
            var sb = new StringBuilder(Math.Max(0, length));
            for (int i = start; i < start + length && i < s.Length; i++) sb.Append((char)s[i]);
            return sb.ToString();
        }

        /// <summary>True when the next operator after a name is Tf (a font selection).</summary>
        private static bool FollowedByTf(byte[] s, int from)
        {
            int i = from;
            int guard = 0;
            while (i < s.Length && guard++ < 64)
            {
                byte b = s[i];
                if (b == (byte)' ' || b == (byte)'\r' || b == (byte)'\n'
                    || b == (byte)'\t' || b == (byte)'.' || b == (byte)'-' || b == (byte)'+'
                    || (b >= (byte)'0' && b <= (byte)'9')) { i++; continue; }
                return b == (byte)'T' && i + 1 < s.Length && s[i + 1] == (byte)'f';
            }
            return false;
        }
    }
}
