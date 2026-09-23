using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// Pre-save fix-ups for PdfSharpCore documents (WPF-free).
    ///
    /// PdfSharpCore defect: merely reading <c>doc.Outlines</c> on a document that has no
    /// bookmarks creates an empty outline object and writes <c>/Outlines N 0 R</c> into the
    /// Catalog, but on save it never serialises object N - the xref entry for N ends up pointing
    /// at the xref table itself. Every PDF viewer with error recovery (PDFium, Acrobat) shrugs;
    /// PdfSharpCore's own parser throws "Unexpected token 'xref'" in every open mode. Scalpel
    /// read <c>Outlines</c> on every open (bookmarks sidebar), so every bookmark-less PDF it saved
    /// could not be re-opened or re-saved by Scalpel itself ("Save failed" after a successful
    /// write, "damaged structure" prompts on reopen).
    ///
    /// <para>Two more defects of the same family are handled here. PdfSharpCore's
    /// <c>PdfPage.CropBox</c> getter creates the entry it reads, so any code that merely inspects
    /// a crop box plants <c>/CropBox [0 0 0 0]</c> on the page - Acrobat then rejects the file
    /// with "page dimensions out of range". And an edit invalidates any digital signature the file
    /// already carried, because the signed byte range no longer matches, so stale signature values
    /// and the catalog's /Perms entry must be cleared or strict validators reject the result.</para>
    /// </summary>
    public static class PdfSaveGuard
    {
        private const string OutlinesKey = "/Outlines";

        /// <summary>Smallest page edge Adobe accepts, in points.</summary>
        public const double MinPagePoints = 3.0;

        /// <summary>Largest page edge Adobe accepts, in points.</summary>
        public const double MaxPagePoints = 14400.0;

        /// <summary>
        /// Runs every pre-save repair: dangling outline root, degenerate crop boxes and
        /// invalidated signature values. Returns true if anything was changed. Never throws.
        /// </summary>
        public static bool PrepareForSave(PdfDocument doc)
        {
            bool changed = StripDanglingOutlines(doc);
            changed |= StripDegenerateCropBoxes(doc);
            changed |= StripInvalidatedSignatures(doc);
            return changed;
        }

        /// <summary>
        /// Saves through <see cref="PrepareForSave"/>. Every write path should use this rather
        /// than calling <c>doc.Save</c> directly, so a document can never be written with a
        /// structure defect Scalpel itself knows how to repair.
        /// </summary>
        public static void Save(PdfDocument doc, string path)
        {
            PrepareForSave(doc);
            doc.Save(path);
            // Both of these can only be corrected in the finished bytes: PdfSharpCore writes the
            // producer and re-attaches the page groups during Save itself, after any object-model
            // fix-up has run.
            RewriteProducer(path);
            StripSavedTransparencyGroups(path);
        }

        /// <summary>
        /// Remove a dangling empty <c>/Outlines</c> reference from the Catalog. Real bookmarks
        /// are left untouched. Returns true if something was removed. Never throws.
        /// </summary>
        public static bool StripDanglingOutlines(PdfDocument doc)
        {
            try
            {
                var catalog = doc.Internals.Catalog.Elements;
                if (!catalog.ContainsKey(OutlinesKey)) return false;

                // Outlines.Count walks the live outline tree, so it reports 0 both for the empty
                // object PdfSharpCore plants on a read and for a parsed /Outlines dictionary whose
                // /Count lies but that has no entries. Do NOT test /First here: PdfSharpCore only
                // writes /First during serialisation, so a bookmark just added in memory has a
                // populated collection and no /First yet.
                if (doc.Outlines.Count > 0) return false;
                catalog.Remove(OutlinesKey);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Removes a page <c>/CropBox</c> that is empty, smaller than a point, or does not
        /// intersect the effective <c>/MediaBox</c>. A valid inset crop is preserved.
        /// </summary>
        public static bool StripDegenerateCropBoxes(PdfDocument doc)
        {
            bool changed = false;
            try
            {
                for (int i = 0; i < doc.PageCount; i++)
                {
                    PdfDictionary page;
                    try { page = doc.Pages[i]; } catch { continue; }

                    // Read through Elements so nothing is created by the inspection itself.
                    if (!page.Elements.ContainsKey("/CropBox")) continue;
                    var crop = ReadBox(page, "/CropBox");
                    if (crop is null) { page.Elements.Remove("/CropBox"); changed = true; continue; }

                    var media = ReadBox(page, "/MediaBox") ?? InheritedBox(page, "/MediaBox");

                    if (crop.Value.W < 1.0 || crop.Value.H < 1.0)
                    {
                        page.Elements.Remove("/CropBox");
                        changed = true;
                        continue;
                    }

                    if (media is { } m && !Intersects(crop.Value, m))
                    {
                        page.Elements.Remove("/CropBox");
                        changed = true;
                    }
                }
            }
            catch { }
            return changed;
        }

        /// <summary>
        /// Clears signature values that an edit has invalidated: the signed byte range no longer
        /// covers the file, so the value is worse than useless. The signature fields themselves
        /// are kept so the document can be re-signed, and the catalog's /Perms (DocMDP) entry is
        /// dropped because it would otherwise mark the edited file as illegally modified.
        /// </summary>
        public static bool StripInvalidatedSignatures(PdfDocument doc)
        {
            bool changed = false;
            try
            {
                var catalog = doc.Internals.Catalog.Elements;
                var acro = catalog.GetDictionary("/AcroForm");
                if (acro is not null)
                {
                    var fields = acro.Elements.GetArray("/Fields");
                    if (fields is not null)
                    {
                        var seen = new HashSet<PdfDictionary>();
                        for (int i = 0; i < fields.Elements.Count; i++)
                            changed |= ClearSignatureField(Deref(fields.Elements[i]) as PdfDictionary, seen, 0);
                    }
                }

                if (catalog.ContainsKey("/Perms")) { catalog.Remove("/Perms"); changed = true; }
            }
            catch { }
            return changed;
        }

        /// <summary>
        /// Returns the indices of pages whose width or height falls outside Adobe's supported
        /// 3 to 14400 point range. Such pages make Acrobat refuse to display the document.
        /// </summary>
        public static IReadOnlyList<int> FindPagesOutsideRange(PdfDocument doc)
        {
            var bad = new List<int>();
            try
            {
                for (int i = 0; i < doc.PageCount; i++)
                {
                    double w, h;
                    try { w = doc.Pages[i].Width.Point; h = doc.Pages[i].Height.Point; }
                    catch { continue; }
                    if (!IsInRange(w) || !IsInRange(h)) bad.Add(i);
                }
            }
            catch { }
            return bad;
        }

        /// <summary>True when a page edge length is inside Adobe's supported range.</summary>
        public static bool IsInRange(double points) =>
            points >= MinPagePoints && points <= MaxPagePoints && !double.IsNaN(points);

        /// <summary>
        /// Scales an out-of-range page edge back into Adobe's supported range, preserving the
        /// aspect ratio of the page. Returns the factor to apply to both edges.
        /// </summary>
        public static double ScaleFactorToRange(double widthPt, double heightPt)
        {
            if (widthPt <= 0 || heightPt <= 0) return 1.0;
            double factor = 1.0;
            double maxEdge = Math.Max(widthPt, heightPt);
            double minEdge = Math.Min(widthPt, heightPt);
            if (maxEdge > MaxPagePoints) factor = MaxPagePoints / maxEdge;
            if (minEdge * factor < MinPagePoints)
            {
                double up = MinPagePoints / (minEdge * factor);
                // Growing to reach the minimum must not push the long edge past the maximum.
                if (maxEdge * factor * up <= MaxPagePoints) factor *= up;
            }
            return factor;
        }

        // ---- helpers -------------------------------------------------------

        private static bool ClearSignatureField(PdfDictionary? field, HashSet<PdfDictionary> seen, int depth)
        {
            if (field is null || depth > 32 || !seen.Add(field)) return false;
            bool changed = false;
            try
            {
                var ft = field.Elements.GetName("/FT");
                if (ft == "/Sig")
                {
                    if (field.Elements.ContainsKey("/V")) { field.Elements.Remove("/V"); changed = true; }
                    if (field.Elements.ContainsKey("/AP")) { field.Elements.Remove("/AP"); changed = true; }
                }

                var kids = field.Elements.GetArray("/Kids");
                if (kids is not null)
                    for (int i = 0; i < kids.Elements.Count; i++)
                        changed |= ClearSignatureField(Deref(kids.Elements[i]) as PdfDictionary, seen, depth + 1);
            }
            catch { }
            return changed;
        }

        /// <summary>
        /// Resolves an indirect reference to the object it points at. PdfSharpCore's
        /// <c>PdfReference</c> is internal, so the Value property is read reflectively - the same
        /// approach the rest of Scalpel's PDF plumbing uses.
        /// </summary>
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

        private readonly struct Box(double x, double y, double w, double h)
        {
            public double X { get; } = x;
            public double Y { get; } = y;
            public double W { get; } = w;
            public double H { get; } = h;
        }

        private static Box? ReadBox(PdfDictionary page, string key)
        {
            try
            {
                if (Deref(page.Elements[key]) is not PdfArray arr || arr.Elements.Count < 4) return null;
                double x1 = arr.Elements.GetReal(0), y1 = arr.Elements.GetReal(1);
                double x2 = arr.Elements.GetReal(2), y2 = arr.Elements.GetReal(3);
                double x = Math.Min(x1, x2), y = Math.Min(y1, y2);
                double w = Math.Abs(x2 - x1), h = Math.Abs(y2 - y1);
                if (double.IsNaN(w) || double.IsNaN(h)) return null;
                return new Box(x, y, w, h);
            }
            catch { return null; }
        }

        private static Box? InheritedBox(PdfDictionary page, string key)
        {
            var node = Deref(page.Elements["/Parent"]) as PdfDictionary;
            int guard = 0;
            while (node is not null && guard++ < 32)
            {
                var box = ReadBox(node, key);
                if (box is not null) return box;
                node = Deref(node.Elements["/Parent"]) as PdfDictionary;
            }
            return null;
        }

        /// <summary>
        /// Blanks the boilerplate transparency <c>/Group</c> PdfSharpCore attaches to every page
        /// it draws on.
        /// <para>Scalpel does not need it: the group is added whenever an XGraphics surface is
        /// created, even for a page that only had a line of text burned onto it. It is a PDF/A-1
        /// conformance failure, and a transparency group also changes how overlapping content
        /// blends on some printers and RIPs, so a page can print differently from how it was
        /// shown. It has to be removed here rather than before Save, because Save re-attaches
        /// it.</para>
        /// <para>The entry is overwritten with spaces rather than deleted, keeping the file the
        /// same length so every cross-reference offset stays valid - whitespace between dictionary
        /// entries is insignificant, so the result is still a well-formed page dictionary. Only
        /// the exact boilerplate shape is matched, so a group carrying anything more than
        /// <c>/CS /DeviceRGB</c> and <c>/S /Transparency</c> was put there deliberately and is
        /// left alone.</para>
        /// </summary>
        public static bool StripSavedTransparencyGroups(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

                byte[] bytes = File.ReadAllBytes(path);
                bool changed = false;
                int from = 0;

                while (true)
                {
                    int at = IndexOf(bytes, "/Group", from);
                    if (at < 0) break;
                    from = at + 6;

                    int open = SkipToDictOpen(bytes, at + 6);
                    if (open < 0) continue;              // "/Group 5 0 R" and the like: not ours
                    int close = MatchDictClose(bytes, open);
                    if (close < 0) continue;

                    string body = Ascii(bytes, open + 2, close - open - 2);
                    if (!IsBoilerplateGroup(body)) continue;

                    for (int i = at; i <= close + 1; i++) bytes[i] = (byte)' ';
                    changed = true;
                }

                if (changed) File.WriteAllBytes(path, bytes);
                return changed;
            }
            catch
            {
                return false;   // the document is saved and valid; this is a conformance polish
            }
        }

        /// <summary>True for a group dictionary body that is only the DeviceRGB boilerplate.</summary>
        private static bool IsBoilerplateGroup(string body)
        {
            if (body.IndexOf("/Transparency", StringComparison.Ordinal) < 0) return false;
            if (body.IndexOf("/DeviceRGB", StringComparison.Ordinal) < 0) return false;
            if (body.IndexOf("<<", StringComparison.Ordinal) >= 0) return false;   // nested: not ours

            // Exactly two keys: /CS and /S. Anything else was set deliberately.
            int keys = 0;
            for (int i = 0; i < body.Length; i++) if (body[i] == '/') keys++;
            return keys == 4;   // /CS /DeviceRGB /S /Transparency
        }

        /// <summary>Index of the "&lt;&lt;" that opens a dictionary, skipping whitespace only.</summary>
        private static int SkipToDictOpen(byte[] bytes, int from)
        {
            for (int i = from; i < bytes.Length - 1; i++)
            {
                byte b = bytes[i];
                if (b == (byte)' ' || b == (byte)'\r' || b == (byte)'\n' || b == (byte)'\t') continue;
                return b == (byte)'<' && bytes[i + 1] == (byte)'<' ? i : -1;
            }
            return -1;
        }

        /// <summary>Index of the "&gt;&gt;" matching the dictionary opened at <paramref name="open"/>.</summary>
        private static int MatchDictClose(byte[] bytes, int open)
        {
            int depth = 0;
            for (int i = open; i < bytes.Length - 1; i++)
            {
                if (bytes[i] == (byte)'<' && bytes[i + 1] == (byte)'<') { depth++; i++; continue; }
                if (bytes[i] == (byte)'>' && bytes[i + 1] == (byte)'>')
                {
                    depth--;
                    if (depth == 0) return i;
                    i++;
                }
            }
            return -1;
        }

        /// <summary>Reads a byte range as ASCII.</summary>
        private static string Ascii(byte[] bytes, int start, int length)
        {
            var sb = new System.Text.StringBuilder(Math.Max(0, length));
            for (int i = start; i < start + length && i < bytes.Length; i++) sb.Append((char)bytes[i]);
            return sb.ToString();
        }

        /// <summary>
        /// Replaces the "PDFsharp" producer string a saved file carries with Scalpel's own.
        /// <para>PdfSharpCore stamps <c>/Producer</c> during Save and ignores anything set
        /// beforehand, so the only place to correct it is the finished bytes. The replacement is
        /// padded to exactly the original length, which leaves every cross-reference offset in the
        /// file untouched - rewriting it to a different length would corrupt the xref table.
        /// If the name does not fit, the file is left exactly as it was.</para>
        /// </summary>
        public static bool RewriteProducer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

                byte[] bytes = File.ReadAllBytes(path);
                int at = IndexOf(bytes, "/Producer");
                if (at < 0) return false;

                // Expect "/Producer (" then the literal up to the matching ")".
                int open = -1;
                for (int i = at; i < Math.Min(bytes.Length, at + 32); i++)
                {
                    if (bytes[i] == (byte)'(') { open = i; break; }
                    if (bytes[i] == (byte)'<') return false;   // hex string: not our shape
                }
                if (open < 0) return false;

                int close = -1;
                for (int i = open + 1; i < bytes.Length; i++)
                {
                    if (bytes[i] == (byte)'\\') { i++; continue; }   // escaped char
                    if (bytes[i] == (byte)')') { close = i; break; }
                }
                if (close < 0) return false;

                int room = close - open - 1;
                string replacement = "Scalpel PDF";
                if (replacement.Length > room) return false;      // no room: leave it alone

                for (int i = 0; i < room; i++)
                    bytes[open + 1 + i] = (byte)(i < replacement.Length ? replacement[i] : ' ');

                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch
            {
                return false;   // the document is already saved; a cosmetic field is not worth failing over
            }
        }

        /// <summary>Byte-wise index of an ASCII needle.</summary>
        private static int IndexOf(byte[] haystack, string needle, int from = 0)
        {
            for (int i = Math.Max(0, from); i + needle.Length <= haystack.Length; i++)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != (byte)needle[j]) { hit = false; break; }
                }
                if (hit) return i;
            }
            return -1;
        }

        private static bool Intersects(Box a, Box b) =>
            a.X < b.X + b.W + 0.01 && a.X + a.W > b.X - 0.01 &&
            a.Y < b.Y + b.H + 0.01 && a.Y + a.H > b.Y - 0.01;
    }
}
