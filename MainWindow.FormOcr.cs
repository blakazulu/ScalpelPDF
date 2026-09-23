using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using PdfSharpCore.Pdf;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Fill form fields by OCR
        // ============================================================

        /// <summary>
        /// Reads what is printed or written inside each fillable field on the current page and
        /// puts it into that field.
        /// <para>The case this answers: a form that was filled in on paper, then scanned back to
        /// PDF over the original fillable template. The boxes are still there and still empty,
        /// while the answers only exist as pixels. OCR is run per field rather than over the whole
        /// page, so each box is read on its own and the field's own constraints - a numeric-looking
        /// name, a maximum length, a list of allowed choices - are used to clean up the result.</para>
        /// </summary>
        private async void ToolsOcrForm_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            int pageIdx = PageList.SelectedIndex;
            if (pageIdx < 0) pageIdx = 0;
            if (pageIdx >= _doc!.PageCount) return;

            var widgets = CollectFormOcrWidgets(pageIdx);
            if (widgets.Count == 0)
            {
                ScalpelDialog.Show(this, Loc("Str_FormOcr_NoFields"), Loc("Str_Tool_FormOcr"),
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pinned before the first await (including the OCR-data download EnsureOcrReady may
            // run): the recognized field values must land back in the document that started form
            // OCR even though tab switching is refused for the whole run.
            var session = _s;
            using var op = _longOps.Begin();

            var ready = await EnsureOcrReady();
            if (ready is null) return;

            _pageRotations.TryGetValue(pageIdx, out int userRotation);

            string src;
            try
            {
                src = App.MakeTempFile("formocr", session.Id);
                PdfSaveGuard.Save(_doc, src);
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, string.Format(Loc("Str_FormOcr_Failed"), ex.Message),
                                   Loc("Str_Tool_FormOcr"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SetStatus(string.Format(Loc("Str_FormOcr_Running"), widgets.Count));

            Dictionary<int, string> filled;
            try
            {
                filled = await Task.Run(() => ReadFieldsByOcr(src, pageIdx, widgets, userRotation,
                                                              ready.Value.exe, ready.Value.tessdata,
                                                              ready.Value.lang));
            }
            catch (Exception ex)
            {
                Logger.Error("Tools", "formocr.fail", ex.Message, ex);
                ScalpelDialog.Show(this, string.Format(Loc("Str_FormOcr_Failed"), ex.Message),
                                   Loc("Str_Tool_FormOcr"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (filled.Count == 0)
            {
                SetStatus(Loc("Str_FormOcr_Empty"));
                return;
            }

            // Write into the pinned session, not the _formTextValues/_isDirty shim: if the active
            // tab had somehow moved on, this must still fill the document that was OCR'd.
            foreach (var entry in filled)
                session.FormText[entry.Key] = entry.Value;

            if (ReferenceEquals(session, _s))
            {
                MarkDirty(true);
                // Repaint the overlays so the recognized values are visible straight away.
                if (_renderDims.TryGetValue(pageIdx, out var fdims))
                    RenderFormFields(pageIdx, (int)fdims.w, (int)fdims.h);
                SetStatus(string.Format(Loc("Str_FormOcr_Done"), filled.Count));
            }
            else
            {
                session.IsDirty = true;
                RefreshTabStrip();
            }
        }

        /// <summary>
        /// Runs OCR once per field region. Each widget is mapped on its own so the recognized text
        /// stays paired with the field it came from - <see cref="FormOcrPolicy.MapRegions"/> drops
        /// regions that are too small to read, which would otherwise shift the pairing.
        /// </summary>
        private Dictionary<int, string> ReadFieldsByOcr(
            string sourcePath, int pageIdx, List<(int ObjNum, FormOcrWidget Widget)> widgets,
            int userRotation, string exePath, string tessdata, string lang)
        {
            var results = new Dictionary<int, string>();

            using var rast = new DocnetPageRasterizer(sourcePath, 3000);
            if (pageIdx >= rast.PageCount) return results;
            var engine = new TesseractCliOcrEngine(exePath, tessdata, lang);

            // A large notional pixel grid, since regions are converted straight back to fractions.
            const int Grid = 10000;
            int done = 0;

            foreach (var (objNum, widget) in widgets)
            {
                done++;
                int step = done;
                Dispatcher.Invoke(() => SetStatus(
                    string.Format(Loc("Str_FormOcr_Progress"), step, widgets.Count)));

                var regions = FormOcrPolicy.MapRegions([widget], Grid, Grid, userRotation);
                if (regions.Count == 0) continue;
                var region = regions[0];

                double fracX = region.Left / (double)Grid;
                double fracY = region.Top / (double)Grid;
                double fracW = (region.Right - region.Left) / (double)Grid;
                double fracH = (region.Bottom - region.Top) / (double)Grid;

                string text;
                try
                {
                    text = OcrService.RecognizeRegionText(rast, engine, pageIdx,
                                                          fracX, fracY, fracW, fracH, userRotation);
                }
                catch { continue; }   // one unreadable box must not abandon the rest

                text = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                if (text.Length == 0) continue;

                // Snap to the field's own vocabulary where it has one, then honour its length cap.
                if (region.ChoiceValues.Count > 0)
                    text = FormOcrPolicy.ClosestChoice(text, region.ChoiceValues);
                if (region.MaximumLength > 0 && text.Length > region.MaximumLength)
                    text = text.Substring(0, region.MaximumLength);

                results[objNum] = text;
            }

            return results;
        }

        /// <summary>
        /// Collects the page's text and choice widgets in PDF user space, which is the frame
        /// <see cref="FormOcrPolicy"/> works in.
        /// </summary>
        private List<(int ObjNum, FormOcrWidget Widget)> CollectFormOcrWidgets(int pageIndex)
        {
            var found = new List<(int, FormOcrWidget)>();
            try
            {
                if (_doc is null || pageIndex < 0 || pageIndex >= _doc.PageCount) return found;

                var page = _doc.Pages[pageIndex];
                var media = page.MediaBox;
                double boxLeft = media.X1, boxBottom = media.Y1;
                double boxW = page.Width.Point, boxH = page.Height.Point;
                int rotation = ((page.Rotate % 360) + 360) % 360;

                var annots = page.Elements.GetArray("/Annots");
                if (annots is null) return found;

                for (int i = 0; i < annots.Elements.Count; i++)
                {
                    var elem = annots.Elements[i];
                    var ann = elem as PdfDictionary ?? DerefItem(elem) as PdfDictionary;
                    if (ann is null) continue;
                    if (!(ann.Elements["/Subtype"]?.ToString() ?? "").Contains("Widget")) continue;

                    var rect = ann.Elements.GetArray("/Rect");
                    if (rect is null || rect.Elements.Count < 4) continue;
                    double x1 = rect.Elements.GetReal(0), y1 = rect.Elements.GetReal(1);
                    double x2 = rect.Elements.GetReal(2), y2 = rect.Elements.GetReal(3);
                    if (x1 > x2) (x1, x2) = (x2, x1);
                    if (y1 > y2) (y1, y2) = (y2, y1);

                    string fieldType = ReadInheritedName(ann, "/FT");
                    var kind = fieldType.Contains("Tx") ? FormOcrFieldKind.Text
                             : fieldType.Contains("Ch") ? FormOcrFieldKind.Choice
                             : FormOcrFieldKind.Other;
                    if (kind == FormOcrFieldKind.Other) continue;

                    int flags = ReadFieldFlags(ann);
                    if ((flags & 1) != 0) continue;              // read-only fields are not ours to fill

                    var options = new List<string>();
                    if (ann.Elements.GetArray("/Opt") is PdfArray opt)
                    {
                        for (int j = 0; j < opt.Elements.Count; j++)
                        {
                            var o = opt.Elements[j];
                            if (o is PdfString ps) options.Add(ps.Value);
                            else if (o is PdfArray pa && pa.Elements.Count >= 2)
                                options.Add((pa.Elements[1] as PdfString)?.Value ?? "");
                        }
                    }

                    int objNum = GetObjectNumber(elem);
                    if (objNum < 0) objNum = -(pageIndex * 10000 + i);

                    found.Add((objNum, new FormOcrWidget(
                        ReadInheritedString(ann, "/T"), kind, flags,
                        ReadInheritedInt(ann, "/MaxLen"), options,
                        x1, y1, x2, y2,
                        boxLeft, boxBottom, boxW, boxH, rotation)));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Tools", "formocr.collect.fail", "Could not read form widgets",
                            new { error = ex.Message });
            }
            return found;
        }

        /// <summary>Reads an inheritable name entry (like /FT) up the /Parent chain.</summary>
        private string ReadInheritedName(PdfDictionary? field, string key)
        {
            var node = field;
            int guard = 0;
            while (node is not null && guard++ < 32)
            {
                if (node.Elements[key] is not null) return node.Elements[key]?.ToString() ?? "";
                var parent = node.Elements["/Parent"];
                if (parent is null) break;
                node = parent as PdfDictionary ?? DerefItem(parent) as PdfDictionary;
            }
            return "";
        }

        /// <summary>Reads an inheritable string entry (like /T) up the /Parent chain.</summary>
        private string ReadInheritedString(PdfDictionary? field, string key)
        {
            var node = field;
            int guard = 0;
            while (node is not null && guard++ < 32)
            {
                if (node.Elements[key] is PdfString s) return s.Value;
                var parent = node.Elements["/Parent"];
                if (parent is null) break;
                node = parent as PdfDictionary ?? DerefItem(parent) as PdfDictionary;
            }
            return "";
        }
    }
}
