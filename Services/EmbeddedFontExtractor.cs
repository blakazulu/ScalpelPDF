using System;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;

namespace Scalpel.Services
{
    /// <summary>
    /// Pulls an embedded font program (TrueType <c>/FontFile2</c> or OpenType <c>/FontFile3</c>)
    /// out of a PDF by font name, so edited text can be redrawn in the document's ACTUAL font when
    /// that font isn't installed on the machine. Handles simple (TrueType/Type1) and composite
    /// (Type0 → DescendantFonts → CIDFont) font dictionaries. Bare Type1 (<c>/FontFile</c>) is not
    /// returned — PdfSharpCore cannot re-embed it. Pure and defensive: never throws; returns null
    /// when nothing usable is found (caller falls back to a substitute font).
    /// Note: embedded fonts are usually SUBSET (only the glyphs the document already uses), so the
    /// caller must verify the extracted font covers the edited text before using it.
    /// </summary>
    public static class EmbeddedFontExtractor
    {
        /// <summary>Extracted font bytes for the font whose BaseFont matches <paramref name="fontNameHint"/>
        /// (subset prefix and spacing/hyphens ignored), or null. <paramref name="isOpenType"/> is true
        /// when the bytes are an OpenType/CFF program (<c>/FontFile3</c>) rather than plain TrueType.</summary>
        public static byte[]? TryExtract(string pdfPath, string fontNameHint, out bool isOpenType)
        {
            isOpenType = false;
            if (string.IsNullOrWhiteSpace(pdfPath) || string.IsNullOrWhiteSpace(fontNameHint)) return null;
            try
            {
                string target = Normalize(fontNameHint);
                if (target.Length == 0) return null;

                using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);
                for (int p = 0; p < doc.PageCount; p++)
                {
                    var resources = Deref(doc.Pages[p].Elements.GetObject("/Resources")) as PdfDictionary;
                    var fonts = Deref(resources?.Elements.GetObject("/Font")) as PdfDictionary;
                    if (fonts is null) continue;

                    foreach (var key in fonts.Elements.Keys)
                    {
                        if (Deref(fonts.Elements.GetObject(key)) is not PdfDictionary fontDict) continue;
                        if (!Matches(fontDict, target)) continue;

                        var bytes = ExtractFromFontDict(fontDict, out isOpenType);
                        if (bytes is { Length: > 0 }) return bytes;
                    }
                }
            }
            catch { /* fall through to null */ }
            return null;
        }

        private static bool Matches(PdfDictionary fontDict, string target)
        {
            if (NameMatches(fontDict.Elements.GetName("/BaseFont") ?? "", target)) return true;

            // Type0 composite: the descendant CIDFont carries the real BaseFont too.
            if (Deref(fontDict.Elements.GetObject("/DescendantFonts")) is PdfArray arr && arr.Elements.Count > 0)
                if (Deref(arr.Elements[0]) is PdfDictionary cid)
                    return NameMatches(cid.Elements.GetName("/BaseFont") ?? "", target);
            return false;
        }

        // Lenient: PDF BaseFont names often carry a style suffix ("-Regular") the hint lacks, or
        // vice versa — accept when either normalized name contains the other.
        private static bool NameMatches(string baseFontRaw, string target)
        {
            string b = Normalize(baseFontRaw);
            return b.Length > 0 && (b == target || b.Contains(target) || target.Contains(b));
        }

        private static byte[]? ExtractFromFontDict(PdfDictionary fontDict, out bool isOpenType)
        {
            isOpenType = false;

            // Resolve the FontDescriptor — directly for simple fonts, via the descendant CIDFont for Type0.
            PdfDictionary? descriptor = Deref(fontDict.Elements.GetObject("/FontDescriptor")) as PdfDictionary;
            if (descriptor is null &&
                Deref(fontDict.Elements.GetObject("/DescendantFonts")) is PdfArray arr && arr.Elements.Count > 0 &&
                Deref(arr.Elements[0]) is PdfDictionary cid)
            {
                descriptor = Deref(cid.Elements.GetObject("/FontDescriptor")) as PdfDictionary;
            }
            if (descriptor is null) return null;

            // TrueType program.
            if (Deref(descriptor.Elements.GetObject("/FontFile2")) is PdfDictionary ff2)
            {
                var bytes = StreamBytes(ff2);
                if (bytes is { Length: > 0 }) { isOpenType = false; return bytes; }
            }

            // OpenType (CFF or wrapped). Only usable when it's a full sfnt/OpenType program.
            if (Deref(descriptor.Elements.GetObject("/FontFile3")) is PdfDictionary ff3)
            {
                string sub = ff3.Elements.GetName("/Subtype") ?? "";
                var bytes = StreamBytes(ff3);
                if (bytes is { Length: > 0 } && sub.IndexOf("OpenType", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isOpenType = true;
                    return bytes;
                }
            }
            return null;
        }

        private static byte[]? StreamBytes(PdfDictionary streamDict)
        {
            try
            {
                if (streamDict.Stream is null) return null;
                streamDict.Stream.TryUnfilter();      // decode FlateDecode etc. in place
                return streamDict.Stream.Value;
            }
            catch { return null; }
        }

        private static PdfItem? Deref(PdfItem? item)
            => item is PdfReference r ? r.Value : item;

        /// <summary>Lowercased name without the "ABCDEF+" subset prefix, spaces, or hyphens.</summary>
        internal static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string s = name.TrimStart('/');
            int plus = s.IndexOf('+');
            if (plus >= 0 && plus <= 7) s = s.Substring(plus + 1);
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (c != ' ' && c != '-' && c != ',') sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}
