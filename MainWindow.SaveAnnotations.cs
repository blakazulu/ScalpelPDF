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

        /// <summary>Pick a bundled face covering the script of <paramref name="text"/>:
        /// Arabic → Noto Sans Arabic, Hebrew → Noto Sans Hebrew, Cyrillic → Noto Sans,
        /// otherwise the candidate (Latin). Falls back to the candidate if a bundled face
        /// isn't registered, so a missing font never blanks the text.</summary>
        private static string PickFace(string text, string candidate, bool bold, bool italic)
        {
            foreach (char c in text)
            {
                // Arabic base (0600-06FF) or presentation forms A/B (FB50-FDFF, FE70-FEFF)
                if ((c >= '؀' && c <= 'ۿ') || (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿'))
                    return FontCovers(candidate, bold, italic, 0x0628) ? candidate : "Noto Sans Arabic";
                // Hebrew (0590-05FF) or Hebrew presentation forms (FB1D-FB4F)
                if ((c >= '֐' && c <= '׿') || (c >= 'יִ' && c <= 'ﭏ'))
                    return FontCovers(candidate, bold, italic, 0x05D0) ? candidate : "Noto Sans Hebrew";
                // Cyrillic (0400-04FF)
                if (c >= 'Ѐ' && c <= 'ӿ')
                    return FontCovers(candidate, bold, italic, 0x0410) ? candidate : "Noto Sans";
            }
            return candidate;
        }

        /// <summary>Draw one line of text, handling RTL: reorder to visual order, pick a
        /// Hebrew-capable font (the candidate if it covers Hebrew, else bundled Noto), and
        /// right-align to <paramref name="rightX"/> when it exceeds <paramref name="leftX"/>
        /// (edits with known bounds); otherwise left-align at leftX. LTR text is unchanged.</summary>
        private static void DrawTextRun(XGraphics gfx, string text, string candidateFamily,
            double fontSizePx, XFontStyle style, XBrush brush,
            double leftX, double rightX, double baselineY, bool forceCandidate = false)
        {
            bool bold = style == XFontStyle.Bold || style == XFontStyle.BoldItalic;
            bool italic = style == XFontStyle.Italic || style == XFontStyle.BoldItalic;

            if (!Scalpel.Services.BidiReorder.ContainsRtl(text))
            {
                // LTR (incl. Cyrillic): pick a covering face so Russian doesn't render as boxes.
                // forceCandidate (an extracted embedded font already verified to cover the text)
                // bypasses the script-substitution heuristic so the exact font is used.
                string ltrFace = forceCandidate ? candidateFamily : PickFace(text, candidateFamily, bold, italic);
                gfx.DrawString(text, new XFont(ltrFace, fontSizePx, style), brush, leftX, baselineY);
                return;
            }

            // RTL: shape Arabic (cursive joining) BEFORE reordering, then reverse to visual order.
            string shaped = Scalpel.Services.ArabicShaper.ContainsArabic(text)
                ? Scalpel.Services.ArabicShaper.Shape(text)
                : text;
            string family = forceCandidate ? candidateFamily : PickFace(shaped, candidateFamily, bold, italic);
            var font = new XFont(family, fontSizePx, style);
            string visual = Scalpel.Services.BidiReorder.ToVisual(shaped);
            double width = gfx.MeasureString(visual, font).Width;
            double x = rightX > leftX ? rightX - width : leftX;
            gfx.DrawString(visual, font, brush, x, baselineY);
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
                double sx = page.Width.Point / renderW;
                double sy = page.Height.Point / renderH;

                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                foreach (var annot in annots)
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
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrEmpty(line))
                                    DrawTextRun(gfx, line, "Geist", ta.FontSize * sy, XFontStyle.Regular,
                                        taBrush, taLeft, taLeft, ty); // rightX==leftX → left-anchored
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
                            double etyB = tea.Position.Y * sy + tea.FontSize * sy;
                            double eLeft = tea.OriginalBounds.X * sx;
                            double eRight = (tea.OriginalBounds.X + tea.OriginalBounds.Width) * sx;
                            // Use the document's own embedded font when we have it (exact match),
                            // forcing it past the substitution heuristic; else the resolved family.
                            string editCandidate = tea.ExactFontFamily ?? tea.FontName;
                            DrawTextRun(gfx, tea.NewContent, editCandidate, tea.FontSize * sy, editStyle,
                                XBrushes.Black, eLeft, eRight, etyB, forceCandidate: tea.ExactFontFamily is not null);
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
            }
        }

    }
}
