using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Scalpel.Services
{
    /// <summary>How much a preflight finding should worry the user.</summary>
    public enum PreflightSeverity
    {
        /// <summary>Something worth knowing about the document.</summary>
        Info,
        /// <summary>Likely to cause a visible problem when printed or sent on.</summary>
        Warning,
        /// <summary>Will be rejected or rendered wrongly by other software.</summary>
        Problem,
    }

    /// <summary>One preflight observation.</summary>
    /// <param name="Severity">How serious it is.</param>
    /// <param name="Category">Grouping label, e.g. "Images" or "Fonts".</param>
    /// <param name="Message">One-line, user-facing description.</param>
    /// <param name="Pages">1-based page numbers the finding applies to, empty for document-wide.</param>
    public sealed record PreflightFinding(
        PreflightSeverity Severity, string Category, string Message, IReadOnlyList<int> Pages);

    /// <summary>The result of checking one document.</summary>
    public sealed record PreflightReport(
        int PageCount,
        IReadOnlyList<PreflightFinding> Findings)
    {
        /// <summary>Findings that will break something elsewhere.</summary>
        public int ProblemCount => Findings.Count(f => f.Severity == PreflightSeverity.Problem);

        /// <summary>Findings that are likely to look wrong.</summary>
        public int WarningCount => Findings.Count(f => f.Severity == PreflightSeverity.Warning);

        /// <summary>True when nothing worse than information was found.</summary>
        public bool IsClean => ProblemCount == 0 && WarningCount == 0;
    }

    /// <summary>
    /// Checks a PDF for the things that go wrong when a document leaves your machine: pages a
    /// viewer will refuse, images that will print blurry, fonts the recipient does not have,
    /// and structure that other software reads as damage.
    ///
    /// <para>Deliberately WPF-free and read-only so it can be unit tested and run over a whole
    /// folder. Every check is defensive: a malformed document must produce findings, not an
    /// exception.</para>
    /// </summary>
    public static class PreflightService
    {
        /// <summary>Below this effective resolution an image looks soft in print.</summary>
        public const double LowDpiThreshold = 150.0;

        /// <summary>Below this it is visibly blocky.</summary>
        public const double VeryLowDpiThreshold = 72.0;

        /// <summary>Runs every check against a file.</summary>
        public static PreflightReport Inspect(string path)
        {
            var findings = new List<PreflightFinding>();
            int pageCount = 0;

            PdfDocument? doc = null;
            try
            {
                try
                {
                    doc = PdfReader.Open(path, PdfDocumentOpenMode.ReadOnly);
                }
                catch (Exception ex)
                {
                    findings.Add(new PreflightFinding(PreflightSeverity.Problem, "Structure",
                        "This file could not be read as a PDF: " + ex.Message, []));
                    return new PreflightReport(0, findings);
                }

                pageCount = doc.PageCount;
                if (pageCount == 0)
                    findings.Add(new PreflightFinding(PreflightSeverity.Problem, "Structure",
                        "The document contains no pages.", []));

                CheckPageGeometry(doc, findings);
                CheckStructure(doc, findings);
                CheckFonts(doc, findings);
            }
            catch (Exception ex)
            {
                findings.Add(new PreflightFinding(PreflightSeverity.Warning, "Structure",
                    "Some checks could not complete: " + ex.Message, []));
            }
            finally { try { doc?.Dispose(); } catch { } }

            // Image resolution needs real placement geometry, which PdfPig provides.
            CheckImages(path, findings);

            return new PreflightReport(pageCount, findings);
        }

        // ---- checks ---------------------------------------------------------

        private static void CheckPageGeometry(PdfDocument doc, List<PreflightFinding> findings)
        {
            var outOfRange = new List<int>();
            var missingTrim = new List<int>();
            var rotated = new List<int>();
            var sizes = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < doc.PageCount; i++)
            {
                PdfPage page;
                try { page = doc.Pages[i]; } catch { continue; }

                double w, h;
                try { w = page.Width.Point; h = page.Height.Point; } catch { continue; }

                if (!PdfSaveGuard.IsInRange(w) || !PdfSaveGuard.IsInRange(h)) outOfRange.Add(i + 1);
                sizes.Add($"{Math.Round(w)}x{Math.Round(h)}");

                try
                {
                    if (!page.Elements.ContainsKey("/TrimBox") && !page.Elements.ContainsKey("/ArtBox"))
                        missingTrim.Add(i + 1);
                    if (((page.Rotate % 360) + 360) % 360 != 0) rotated.Add(i + 1);
                }
                catch { }
            }

            if (outOfRange.Count > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Problem, "Page size",
                    $"{outOfRange.Count} page(s) fall outside the 3 to 14400 point range other readers accept. " +
                    "Acrobat refuses to display these.", outOfRange));

            if (sizes.Count > 1)
                findings.Add(new PreflightFinding(PreflightSeverity.Info, "Page size",
                    $"The document mixes {sizes.Count} different page sizes ({string.Join(", ", sizes.Take(4))}" +
                    (sizes.Count > 4 ? ", ..." : "") + ").", []));

            if (rotated.Count > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Info, "Page size",
                    $"{rotated.Count} page(s) carry a rotation.", rotated));

            if (missingTrim.Count == doc.PageCount && doc.PageCount > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Info, "Print boxes",
                    "No page declares a trim or art box. Commercial printers usually ask for one.", []));
            else if (missingTrim.Count > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Warning, "Print boxes",
                    $"{missingTrim.Count} page(s) have no trim or art box while others do.", missingTrim));
        }

        private static void CheckStructure(PdfDocument doc, List<PreflightFinding> findings)
        {
            try
            {
                var catalog = doc.Internals.Catalog.Elements;

                if (catalog.ContainsKey("/AcroForm"))
                {
                    var fields = catalog.GetDictionary("/AcroForm")?.Elements.GetArray("/Fields");
                    int n = fields?.Elements.Count ?? 0;
                    if (n > 0)
                        findings.Add(new PreflightFinding(PreflightSeverity.Info, "Forms",
                            $"The document has {n} fillable form field(s). Flatten it if the recipient " +
                            "should not be able to change the values.", []));
                }

                if (catalog.ContainsKey("/Perms"))
                    findings.Add(new PreflightFinding(PreflightSeverity.Warning, "Signatures",
                        "The document carries certification permissions. Editing it will invalidate them.", []));

                // Annotations by kind, so the user knows what will travel with the file.
                var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < doc.PageCount; i++)
                {
                    PdfArray? annots;
                    try { annots = doc.Pages[i].Elements.GetArray("/Annots"); } catch { continue; }
                    if (annots is null) continue;
                    for (int a = 0; a < annots.Elements.Count; a++)
                    {
                        try
                        {
                            var d = annots.Elements[a] as PdfDictionary
                                    ?? Deref(annots.Elements[a]) as PdfDictionary;
                            var sub = d?.Elements.GetName("/Subtype");
                            if (string.IsNullOrEmpty(sub)) continue;
                            byKind[sub!] = byKind.TryGetValue(sub!, out int c) ? c + 1 : 1;
                        }
                        catch { }
                    }
                }
                if (byKind.Count > 0)
                    findings.Add(new PreflightFinding(PreflightSeverity.Info, "Annotations",
                        "Annotations present: " + string.Join(", ",
                            byKind.OrderByDescending(k => k.Value)
                                  .Select(k => $"{k.Key.TrimStart('/')} x{k.Value}")), []));
            }
            catch { }
        }

        private static void CheckFonts(PdfDocument doc, List<PreflightFinding> findings)
        {
            var notEmbedded = new SortedSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < doc.PageCount; i++)
                {
                    PdfDictionary? fonts;
                    try
                    {
                        fonts = doc.Pages[i].Elements.GetDictionary("/Resources")?
                                   .Elements.GetDictionary("/Font");
                    }
                    catch { continue; }
                    if (fonts is null) continue;

                    foreach (var key in fonts.Elements.Keys.ToList())
                    {
                        try
                        {
                            var font = fonts.Elements.GetDictionary(key);
                            if (font is null) continue;
                            var name = font.Elements.GetName("/BaseFont") ?? "(unnamed)";

                            // Composite fonts hold the descriptor on the descendant.
                            var descriptor = font.Elements.GetDictionary("/FontDescriptor");
                            if (descriptor is null)
                            {
                                var desc = font.Elements.GetArray("/DescendantFonts");
                                if (desc is not null && desc.Elements.Count > 0)
                                {
                                    var d0 = desc.Elements[0] as PdfDictionary
                                             ?? Deref(desc.Elements[0]) as PdfDictionary;
                                    descriptor = d0?.Elements.GetDictionary("/FontDescriptor");
                                }
                            }
                            if (descriptor is null)
                            {
                                // No descriptor at all: one of the 14 standard fonts, which the
                                // reader supplies. Worth flagging only because substitution shifts
                                // metrics on machines without a close match.
                                notEmbedded.Add(name.TrimStart('/'));
                                continue;
                            }

                            bool embedded = descriptor.Elements.ContainsKey("/FontFile")
                                            || descriptor.Elements.ContainsKey("/FontFile2")
                                            || descriptor.Elements.ContainsKey("/FontFile3");
                            if (!embedded) notEmbedded.Add(name.TrimStart('/'));
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (notEmbedded.Count > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Warning, "Fonts",
                    $"{notEmbedded.Count} font(s) are not embedded and will be substituted on machines " +
                    $"without them: {string.Join(", ", notEmbedded.Take(6))}" +
                    (notEmbedded.Count > 6 ? ", ..." : ""), []));
        }

        private static void CheckImages(string path, List<PreflightFinding> findings)
        {
            var lowPages = new SortedSet<int>();
            var veryLowPages = new SortedSet<int>();
            double worst = double.MaxValue;
            int imageCount = 0;

            try
            {
                using var pig = UglyToad.PdfPig.PdfDocument.Open(path);
                for (int p = 1; p <= pig.NumberOfPages; p++)
                {
                    UglyToad.PdfPig.Content.Page page;
                    try { page = pig.GetPage(p); } catch { continue; }

                    foreach (var img in SafeImages(page))
                    {
                        try
                        {
                            imageCount++;
                            double wIn = img.Bounds.Width / 72.0;
                            double hIn = img.Bounds.Height / 72.0;
                            if (wIn <= 0 || hIn <= 0) continue;

                            // Effective resolution is pixels actually available per printed inch.
                            double dpiX = img.WidthInSamples / wIn;
                            double dpiY = img.HeightInSamples / hIn;
                            double dpi = Math.Min(dpiX, dpiY);
                            if (double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi <= 0) continue;

                            worst = Math.Min(worst, dpi);
                            if (dpi < VeryLowDpiThreshold) veryLowPages.Add(p);
                            else if (dpi < LowDpiThreshold) lowPages.Add(p);
                        }
                        catch { }
                    }
                }
            }
            catch { return; }

            if (imageCount == 0) return;

            if (veryLowPages.Count > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Problem, "Images",
                    $"{veryLowPages.Count} page(s) contain images below {VeryLowDpiThreshold:0} DPI " +
                    $"(lowest {worst.ToString("0", CultureInfo.InvariantCulture)} DPI). These will look " +
                    "blocky in print.", [.. veryLowPages]));

            if (lowPages.Count > 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Warning, "Images",
                    $"{lowPages.Count} page(s) contain images below {LowDpiThreshold:0} DPI. " +
                    "These are fine on screen but soft in print.", [.. lowPages]));

            if (veryLowPages.Count == 0 && lowPages.Count == 0)
                findings.Add(new PreflightFinding(PreflightSeverity.Info, "Images",
                    $"{imageCount} image(s), all at or above {LowDpiThreshold:0} DPI.", []));
        }

        /// <summary>
        /// Resolves an indirect reference. PdfSharpCore's PdfReference type is internal, so the
        /// Value property is read reflectively, as elsewhere in Scalpel's PDF plumbing.
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

        private static IEnumerable<UglyToad.PdfPig.Content.IPdfImage> SafeImages(
            UglyToad.PdfPig.Content.Page page)
        {
            try { return page.GetImages().ToList(); }
            catch { return []; }
        }
    }
}
