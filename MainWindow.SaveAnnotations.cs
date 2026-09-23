using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Save annotations to PDF
        // ============================================================

        /// <summary>True if <paramref name="family"/> (exact face) maps <paramref name="codepoint"/>.</summary>
        private static bool FontCovers(string family, bool bold, bool italic, int codepoint)
        {
            if (Scalpel.Services.PdfFontResolver.Instance.TryGetExactFontBytes(family, bold, italic, out var bytes))
                return Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, codepoint);
            return false;
        }

        /// <summary>Draw one line of text. RTL text is shaped (Arabic) and reordered to visual
        /// order, then the line is split into font runs (<see cref="ScriptRuns"/>) so each part is
        /// drawn in a face that has its glyphs: the candidate where it covers the character,
        /// otherwise a fallback. A mixed Hebrew/English line therefore keeps its Latin letters and
        /// digits instead of rendering them as boxes. RTL lines right-align to
        /// <paramref name="rightX"/> when it exceeds <paramref name="leftX"/> (edits with known
        /// bounds); everything else left-aligns at leftX. <paramref name="forceCandidate"/> (an
        /// extracted embedded font already verified to cover the text) skips the substitution.
        ///
        /// <para>PdfSharpCore 1.3.67 ignores simulated styles, so bold or italic only appears
        /// when the face really exists. Styled text therefore prefers Arial (which ships real
        /// bold and italic faces covering Latin, Hebrew, Arabic and Cyrillic) over the bundled
        /// Noto faces, and a face with no italic is slanted with a shear instead. A family that
        /// PdfSharpCore cannot build (a damaged embedded subset, say) falls back to Arial rather
        /// than throwing - the original text is already covered by then, and a throw would save
        /// a blank box in its place.</para></summary>
        private static void DrawTextRun(XGraphics gfx, string text, string candidateFamily,
            double fontSizePx, XFontStyle style, XBrush brush,
            double leftX, double rightX, double baselineY, bool forceCandidate = false)
        {
            bool bold = style == XFontStyle.Bold || style == XFontStyle.BoldItalic;
            bool italic = style == XFontStyle.Italic || style == XFontStyle.BoldItalic;
            bool rtl = Scalpel.Services.BidiReorder.ContainsRtl(text);
            var resolver = Scalpel.Services.PdfFontResolver.Instance;

            string visual = Scalpel.Services.ScriptRuns.ToVisual(text);
            List<(string Text, string Family)> segs;
            if (forceCandidate)
                segs = [(visual, candidateFamily)];
            else
            {
                var bytesByFamily = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
                bool Covers(string family, int cp)
                {
                    if (!bytesByFamily.TryGetValue(family, out var b))
                        bytesByFamily[family] = b = resolver.TryGetExactFontBytes(family, bold, italic, out var fb) ? fb : null;
                    return b is not null && Scalpel.Services.TrueTypeCmap.CoversCodepoint(b, cp);
                }
                IReadOnlyList<string> fallbacks = bold || italic
                    ? ["Arial", .. Scalpel.Services.ScriptRuns.Fallbacks]
                    : Scalpel.Services.ScriptRuns.Fallbacks;
                segs = Scalpel.Services.ScriptRuns.Split(visual, candidateFamily, Covers, fallbacks);
            }

            var fonts = new Dictionary<string, XFont>(StringComparer.OrdinalIgnoreCase);
            XFont FontOf(string family)
            {
                if (fonts.TryGetValue(family, out var f)) return f;
                foreach (var fam in new[] { family, "Arial", "Noto Sans" })
                {
                    try
                    {
                        f = new XFont(fam, fontSizePx, style);
                        gfx.MeasureString("x", f);      // builds the face now, not mid-draw
                        return fonts[family] = f;
                    }
                    catch (Exception ex)
                    {
                        Scalpel.Services.Logger.Warn("Save", "font.build.fail",
                            "Font could not be used for burning text; falling back",
                            new { family = fam, error = ex.Message });
                    }
                }
                return fonts[family] = new XFont("Arial", fontSizePx, XFontStyle.Regular);
            }

            var widths = new double[segs.Count];
            double total = 0;
            for (int i = 0; i < segs.Count; i++)
                total += widths[i] = gfx.MeasureString(segs[i].Text, FontOf(segs[i].Family)).Width;

            double x = rtl && rightX > leftX ? rightX - total : leftX;
            for (int i = 0; i < segs.Count; i++)
            {
                var font = FontOf(segs[i].Family);
                // Only a KNOWN family missing its italic face: an unknown one already resolves
                // to Arial's real italic, and shearing that would slant it twice.
                bool shear = italic
                    && resolver.TryGetExactFontBytes(segs[i].Family, false, false, out _)
                    && !resolver.HasExactFace(segs[i].Family, bold, italic: true);
                if (shear)
                {
                    // Slant about the baseline: x' = x - 0.21 * y, the usual synthetic oblique.
                    var state = gfx.Save();
                    gfx.TranslateTransform(x, baselineY);
                    gfx.MultiplyTransform(new XMatrix(1, 0, -0.21, 1, 0, 0));
                    gfx.DrawString(segs[i].Text, font, brush, 0, 0);
                    gfx.Restore(state);
                }
                else gfx.DrawString(segs[i].Text, font, brush, x, baselineY);
                x += widths[i];
            }
        }

        private void DrawAnnotationsOnDocument()
        {
            if (_doc is null) return;

            // Strip link annotation borders so they don't render as colored rectangles
            // (e.g. strikethrough-like lines) in other PDF viewers.
            StripLinkAnnotationBorders(_doc);

            foreach (var kvp in _annotations)
            {
                int pageIdx = kvp.Key;
                var annots = kvp.Value;
                if (annots.Count == 0 || pageIdx >= _doc.PageCount) continue;
                if (!_renderDims.ContainsKey(pageIdx)) continue;

                var page = _doc.Pages[pageIdx];
                var (renderW, renderH) = _renderDims[pageIdx];

                // Take the ORIGINAL words out of the page before drawing over them. The opaque
                // cover below hides them on screen but leaves them in the content stream, where
                // selecting, searching or any text extractor would still return them alongside
                // the replacement - so someone who edits a salary or a name would not actually
                // have changed it. Done before the XGraphics surface is opened, because that
                // appends a new content stream and this rewrites the existing ones.
                foreach (var edit in annots.OfType<TextEditAnnotation>())
                {
                    if (string.IsNullOrWhiteSpace(edit.OriginalContent)) continue;
                    try
                    {
                        int cleared = Scalpel.Services.ContentTextRemover.RemoveText(
                                          page, edit.OriginalContent);
                        if (cleared == 0)
                            Scalpel.Services.Logger.Info("Save", "textedit.original.kept",
                                "Original text could not be located in the content stream",
                                new { page = pageIdx + 1 });
                    }
                    catch (Exception ex)
                    {
                        Scalpel.Services.Logger.Warn("Save", "textedit.remove.fail",
                            "Removing the original text failed", new { error = ex.Message });
                    }
                }

                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                // Annotations were placed on the page as the user SEES it. That frame matches the
                // XGraphics surface only when the page still carries its own /Rotate - after any
                // page operation Scalpel strips the rotation into _pageRotations and rotates the
                // bitmap instead, leaving a landscape canvas over a portrait surface. BurnRotation
                // reconciles the two; for an unrotated page it is exactly the old plain scale.
                int burnRotation = RotationOf(pageIdx);
                var burn = Scalpel.Services.BurnRotation.For(
                    burnRotation, gfx.PageSize.Width, gfx.PageSize.Height, renderW, renderH);

                double sx, sy;
                if (burn.IsAxisAligned && burn.OffsetX == 0 && burn.OffsetY == 0)
                {
                    // Plain scale: keep drawing straight into the surface, exactly as before.
                    sx = burn.M11;
                    sy = burn.M22;
                }
                else
                {
                    // Rotate/flip the surface so canvas coordinates can be used unchanged, then
                    // draw with a unit scale on top of it.
                    gfx.MultiplyTransform(new XMatrix(burn.M11, burn.M12, burn.M21, burn.M22,
                                                      burn.OffsetX, burn.OffsetY));
                    sx = 1.0;
                    sy = 1.0;
                }

                foreach (var annot in annots)
                {
                    // One malformed annotation must not abort the whole burn: it would surface as
                    // "Save failed" and lose every other annotation on the document.
                    try
                    {
                        switch (annot)
                        {
                            case TextAnnotation ta:
                                var lines = ta.Content.Split('\n');
                                double lineH = ta.FontSize * sy * 1.2;
                                double ty = ta.Position.Y * sy + ta.FontSize * sy;
                                var taColor = ta.GetColor();
                                var taBrush = new XSolidBrush(XColor.FromArgb(taColor.A, taColor.R, taColor.G, taColor.B));
                                double taLeft = ta.Position.X * sx;
                                // Burn the styling the user chose. Underline is not an XFontStyle,
                                // so it is drawn as a rule under each line at the measured width.
                                var taStyle = ta.Bold && ta.Italic ? XFontStyle.BoldItalic
                                            : ta.Bold ? XFontStyle.Bold
                                            : ta.Italic ? XFontStyle.Italic
                                            : XFontStyle.Regular;
                                var taFont = new XFont("Geist", ta.FontSize * sy, taStyle);

                                foreach (var line in lines)
                                {
                                    if (!string.IsNullOrEmpty(line))
                                    {
                                        DrawTextRun(gfx, line, "Geist", ta.FontSize * sy, taStyle,
                                            taBrush, taLeft, taLeft, ty); // rightX==leftX → left-anchored

                                        if (ta.Underline)
                                        {
                                            double lineW = gfx.MeasureString(line, taFont).Width;
                                            double rule = ty + ta.FontSize * sy * 0.16;
                                            var rulePen = new XPen(taBrush is XSolidBrush sb
                                                                       ? sb.Color : XColors.Black,
                                                                   Math.Max(0.5, ta.FontSize * sy * 0.06));
                                            gfx.DrawLine(rulePen, taLeft, rule, taLeft + lineW, rule);
                                        }
                                    }
                                    ty += lineH;
                                }
                                break;

                            case HighlightAnnotation ha:
                                var hc = ha.GetColor();
                                var hBrush = new XSolidBrush(XColor.FromArgb(hc.A, hc.R, hc.G, hc.B));
                                gfx.DrawRectangle(hBrush,
                                    ha.Bounds.X * sx, ha.Bounds.Y * sy,
                                    ha.Bounds.Width * sx, ha.Bounds.Height * sy);
                                break;

                            case InkAnnotation ia:
                                if (ia.Points.Count < 2) break;
                                var ic = ia.GetColor();
                                var pen = new XPen(XColor.FromArgb(ic.A, ic.R, ic.G, ic.B), ia.StrokeWidth * sx)
                                {
                                    LineJoin = XLineJoin.Round,
                                    LineCap = XLineCap.Round
                                };
                                for (int i = 0; i < ia.Points.Count - 1; i++)
                                {
                                    gfx.DrawLine(pen,
                                        ia.Points[i].X * sx, ia.Points[i].Y * sy,
                                        ia.Points[i + 1].X * sx, ia.Points[i + 1].Y * sy);
                                }
                                break;

                            case TextEditAnnotation tea:
                                // White-out original text area
                                var whiteRect = new XSolidBrush(XColors.White);
                                gfx.DrawRectangle(whiteRect,
                                    (tea.OriginalBounds.X - 2) * sx, (tea.OriginalBounds.Y - 2) * sy,
                                    (tea.OriginalBounds.Width + 4) * sx, (tea.OriginalBounds.Height + 4) * sy);
                                var editStyle = tea.IsBold && tea.IsItalic ? XFontStyle.BoldItalic
                                              : tea.IsBold ? XFontStyle.Bold
                                              : tea.IsItalic ? XFontStyle.Italic
                                              : XFontStyle.Regular;
                                // The original line's real baseline when it was read from the PDF;
                                // one em below the box top only as the old fallback.
                                double etyB = tea.Position.Y * sy + (tea.BaselineOffset ?? tea.FontSize) * sy;
                                double eLeft = tea.OriginalBounds.X * sx;
                                double eRight = (tea.OriginalBounds.X + tea.OriginalBounds.Width) * sx;
                                // Use the document's own embedded font when we have it (exact match),
                                // forcing it past the substitution heuristic; else the resolved family.
                                string editCandidate = tea.ExactFontFamily ?? tea.FontName;
                                DrawTextRun(gfx, tea.NewContent, editCandidate, tea.FontSize * sy, editStyle,
                                    tea.TextColor is Color tc ? new XSolidBrush(XColor.FromArgb(255, tc.R, tc.G, tc.B)) : XBrushes.Black,
                                    eLeft, eRight, etyB, forceCandidate: tea.ExactFontFamily is not null);
                                break;

                            case SignatureAnnotation sa:
                                if (sa.ImageData is not null)
                                {
                                    try
                                    {
                                        var imgBytes = Convert.FromBase64String(sa.ImageData);
                                        var xImg = XImage.FromStream(() => new System.IO.MemoryStream(imgBytes));
                                        double imgX = sa.Position.X * sx;
                                        double imgY = sa.Position.Y * sy;
                                        double imgW = sa.SourceWidth * sa.Scale * sx;
                                        double imgH = sa.SourceHeight * sa.Scale * sy;
                                        gfx.DrawImage(xImg, imgX, imgY, imgW, imgH);
                                    }
                                    catch { /* skip broken image */ }
                                }
                                else
                                {
                                    var sigPen = new XPen(XColors.Black, 2 * sa.Scale * sx)
                                    {
                                        LineJoin = XLineJoin.Round,
                                        LineCap = XLineCap.Round
                                    };
                                    foreach (var stroke in sa.Strokes)
                                    {
                                        for (int i = 0; i < stroke.Count - 1; i++)
                                        {
                                            double x1 = (sa.Position.X + stroke[i].X * sa.Scale) * sx;
                                            double y1 = (sa.Position.Y + stroke[i].Y * sa.Scale) * sy;
                                            double x2 = (sa.Position.X + stroke[i + 1].X * sa.Scale) * sx;
                                            double y2 = (sa.Position.Y + stroke[i + 1].Y * sa.Scale) * sy;
                                            gfx.DrawLine(sigPen, x1, y1, x2, y2);
                                        }
                                    }
                                }
                                break;

                            case ImageAnnotation ia:
                                try
                                {
                                    var iaBytes = Convert.FromBase64String(ia.ImageData);
                                    var xia = XImage.FromStream(() => new System.IO.MemoryStream(iaBytes));
                                    double iaX = ia.Position.X * sx;
                                    double iaY = ia.Position.Y * sy;
                                    double iaW = ia.SourceWidth * ia.Scale * sx;
                                    double iaH = ia.SourceHeight * ia.Scale * sy;
                                    gfx.DrawImage(xia, iaX, iaY, iaW, iaH);
                                }
                                catch { /* skip broken image */ }
                                break;
                        }
                    }
                    catch (Exception exAnnot)
                    {
                        Scalpel.Services.Logger.Warn("Save", "annotation.burn.skip",
                            annot.GetType().Name + ": " + exAnnot.Message);
                    }
                }
            }
        }

    }
}
