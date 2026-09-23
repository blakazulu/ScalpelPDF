using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Scalpel.Services
{
    /// <summary>One comment carried by a PDF.</summary>
    /// <param name="PageNumber">1-based page the comment sits on.</param>
    /// <param name="Kind">Annotation subtype without the slash, e.g. "Text" or "Highlight".</param>
    /// <param name="Author">/T, the person who wrote it. Empty when the file does not say.</param>
    /// <param name="Contents">The comment text.</param>
    /// <param name="Modified">/M as written in the file, unparsed. Empty when absent.</param>
    /// <param name="IsReply">True when this is a reply to another comment (/IRT).</param>
    /// <param name="PageIndex">0-based page, for locating the annotation again.</param>
    /// <param name="AnnotIndex">Position in that page's /Annots array, for locating it again.</param>
    public sealed record PdfComment(
        int PageNumber, string Kind, string Author, string Contents, string Modified, bool IsReply,
        int PageIndex = -1, int AnnotIndex = -1);

    /// <summary>
    /// Reads the comments a PDF already carries: sticky notes, and the notes attached to
    /// highlights, strikeouts, stamps and drawings by whoever reviewed the document.
    ///
    /// <para>Scalpel now paints these annotations onto the page, but a sticky note shows only its
    /// icon - the words live in the annotation's /Contents and in a popup that is not rendered.
    /// This makes them readable without needing another PDF reader.</para>
    /// </summary>
    public static class PdfComments
    {
        /// <summary>Annotation subtypes that are markup carrying a human comment.</summary>
        private static readonly HashSet<string> CommentKinds = new(StringComparer.Ordinal)
        {
            "Text", "FreeText", "Highlight", "Underline", "StrikeOut", "Squiggly",
            "Square", "Circle", "Line", "Polygon", "PolyLine", "Ink", "Stamp",
            "Caret", "FileAttachment", "Sound",
        };

        /// <summary>Reads every comment in the file, in page order. Never throws.</summary>
        public static IReadOnlyList<PdfComment> Read(string path)
        {
            try
            {
                using var doc = PdfReader.Open(path, PdfDocumentOpenMode.ReadOnly);
                return Read(doc);
            }
            catch { return []; }
        }

        /// <summary>Reads every comment from an open document, in page order. Never throws.</summary>
        public static IReadOnlyList<PdfComment> Read(PdfDocument doc)
        {
            var result = new List<PdfComment>();
            try
            {
                for (int i = 0; i < doc.PageCount; i++)
                {
                    PdfArray? annots;
                    try { annots = doc.Pages[i].Elements.GetArray("/Annots"); }
                    catch { continue; }
                    if (annots is null) continue;

                    for (int a = 0; a < annots.Elements.Count; a++)
                    {
                        try
                        {
                            var d = annots.Elements[a] as PdfDictionary
                                    ?? Deref(annots.Elements[a]) as PdfDictionary;
                            if (d is null) continue;

                            var sub = (d.Elements.GetName("/Subtype") ?? string.Empty).TrimStart('/');
                            if (!CommentKinds.Contains(sub)) continue;

                            string contents = Text(d, "/Contents");
                            string author = Text(d, "/T");
                            // A markup annotation with neither text nor author is decoration, not
                            // a comment - a plain highlight someone left without a note.
                            if (contents.Length == 0 && author.Length == 0) continue;

                            result.Add(new PdfComment(
                                i + 1, sub, author, contents, Text(d, "/M"),
                                d.Elements.ContainsKey("/IRT"), i, a));
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Replaces a comment's text in an open document. Returns true when it was written.
        /// <para>The annotation's own appearance stream is dropped at the same time: a viewer
        /// that finds a cached appearance will draw the OLD words, so leaving it behind would
        /// show the edit in Scalpel and the original everywhere else. Clearing it asks every
        /// reader to redraw from /Contents, which is now the single source of truth.</para>
        /// </summary>
        public static bool UpdateContents(PdfDocument doc, PdfComment comment, string newText)
        {
            var d = Locate(doc, comment);
            if (d is null) return false;
            try
            {
                d.Elements["/Contents"] = new PdfString(newText ?? string.Empty);
                d.Elements["/M"] = new PdfString(NowStamp());
                d.Elements.Remove("/AP");
                return true;
            }
            catch { return false; }
        }

        /// <summary>Sets a comment's author. Returns true when it was written.</summary>
        public static bool UpdateAuthor(PdfDocument doc, PdfComment comment, string newAuthor)
        {
            var d = Locate(doc, comment);
            if (d is null) return false;
            try
            {
                d.Elements["/T"] = new PdfString(newAuthor ?? string.Empty);
                d.Elements["/M"] = new PdfString(NowStamp());
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Removes comments from an open document. Returns how many were removed.
        /// <para>Deleting shifts every later annotation's index, so the removals are applied from
        /// the highest index down within each page - taking them in the caller's order would make
        /// each deletion after the first hit the wrong annotation.</para>
        /// </summary>
        public static int Delete(PdfDocument doc, IEnumerable<PdfComment> comments)
        {
            int removed = 0;
            try
            {
                var ordered = comments
                    .Where(c => c.PageIndex >= 0 && c.AnnotIndex >= 0)
                    .OrderByDescending(c => c.PageIndex)
                    .ThenByDescending(c => c.AnnotIndex);

                foreach (var c in ordered)
                {
                    try
                    {
                        if (c.PageIndex >= doc.PageCount) continue;
                        var annots = doc.Pages[c.PageIndex].Elements.GetArray("/Annots");
                        if (annots is null || c.AnnotIndex >= annots.Elements.Count) continue;

                        // Confirm it is still the annotation we were told about before removing
                        // anything - an index alone is not proof after other edits.
                        if (!Matches(annots.Elements[c.AnnotIndex], c)) continue;

                        annots.Elements.RemoveAt(c.AnnotIndex);
                        removed++;
                    }
                    catch { }
                }
            }
            catch { }
            return removed;
        }

        /// <summary>Finds the annotation dictionary a comment came from, or null.</summary>
        private static PdfDictionary? Locate(PdfDocument doc, PdfComment comment)
        {
            try
            {
                if (comment.PageIndex < 0 || comment.PageIndex >= doc.PageCount) return null;
                var annots = doc.Pages[comment.PageIndex].Elements.GetArray("/Annots");
                if (annots is null) return null;
                if (comment.AnnotIndex < 0 || comment.AnnotIndex >= annots.Elements.Count) return null;

                var item = annots.Elements[comment.AnnotIndex];
                if (!Matches(item, comment)) return null;
                return item as PdfDictionary ?? Deref(item) as PdfDictionary;
            }
            catch { return null; }
        }

        /// <summary>True when the annotation at this slot is still the one the comment describes.</summary>
        private static bool Matches(PdfItem? item, PdfComment comment)
        {
            try
            {
                var d = item as PdfDictionary ?? Deref(item) as PdfDictionary;
                if (d is null) return false;
                var sub = (d.Elements.GetName("/Subtype") ?? string.Empty).TrimStart('/');
                return string.Equals(sub, comment.Kind, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        /// <summary>The current time as a PDF date string (D:YYYYMMDDHHmmSS).</summary>
        private static string NowStamp()
            => "D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        /// <summary>Formats comments as the plain text a report or dialog shows.</summary>
        public static string Format(IReadOnlyList<PdfComment> comments)
        {
            if (comments.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var group in comments.GroupBy(c => c.PageNumber).OrderBy(g => g.Key))
            {
                sb.Append("Page ").Append(group.Key.ToString(CultureInfo.InvariantCulture)).AppendLine();
                foreach (var c in group)
                {
                    sb.Append("  ");
                    if (c.IsReply) sb.Append("reply ");
                    sb.Append('[').Append(c.Kind).Append(']');
                    if (c.Author.Length > 0) sb.Append(' ').Append(c.Author);
                    sb.AppendLine();
                    if (c.Contents.Length > 0)
                        foreach (var line in c.Contents.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                            sb.Append("      ").AppendLine(line);
                }
            }
            return sb.ToString();
        }

        private static string Text(PdfDictionary d, string key)
        {
            try
            {
                var item = Deref(d.Elements[key]);
                return item switch
                {
                    PdfString s => s.Value ?? string.Empty,
                    null => string.Empty,
                    _ => item.ToString() ?? string.Empty,
                };
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Resolves an indirect reference. PdfSharpCore's PdfReference is internal, so the Value
        /// property is read reflectively, as elsewhere in Scalpel's PDF plumbing.
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
    }
}
