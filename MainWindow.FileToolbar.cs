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
        // File toolbar handlers
        // ============================================================

        // Generic "open the ContextMenu attached to this button" handler — used by the
        // Open split-button chevron (Open / New / Close File).
        private void FileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu is not null)
            { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; }
        }

        // Toolbar wrapper — rotates CW (90°) for the Pages mode panel button.
        private void RotatePagesToolbar_Click(object sender, RoutedEventArgs e) => RotatePages_Click(90);

        private void New_Click(object sender, RoutedEventArgs e) => NewDocument();

        private void NewDocument()
        {
            if (_isDirty)
            {
                var res = ScalpelDialog.Show(this,
                    "You have unsaved changes. Discard them and create a new document?",
                    "Scalpel", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }

            try
            {
                var newDoc = new PdfDocument();
                newDoc.AddPage(); // one blank A4 page

                var tempPath = App.MakeTempFile("new");
                newDoc.Save(tempPath);
                newDoc.Close();

                _doc?.Close();
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                FinishOpenFile("Untitled.pdf", tempPath);
                SetStatus("New blank document");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Could not create new document:\n{ex.Message}",
                    "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "PDF files|*.pdf", Title = "Open PDF" };
            if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
        }

        private void Merge_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var doc = _doc;
            var dlg = new OpenFileDialog { Filter = "PDF files|*.pdf", Title = "Select PDF to merge", Multiselect = true };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                foreach (var file in dlg.FileNames)
                {
                    int pageOffset = doc.PageCount;

                    // Open twice: Import mode for AddPage, ReadOnly for catalog access.
                    using var srcRead = PdfReader.Open(file, PdfDocumentOpenMode.ReadOnly);
                    var namedDestMap = BuildNamedDestMap(srcRead);

                    using var src = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                    for (int i = 0; i < src.PageCount; i++)
                        doc.AddPage(src.Pages[i]);

                    // Rewrite named-destination links in the newly added pages so they
                    // resolve correctly after the catalog is not imported.
                    if (namedDestMap.Count > 0)
                        RewriteNamedDestLinks(doc, pageOffset, namedDestMap);
                }
                SaveTempAndReload();
                SetStatus($"Merged {dlg.FileNames.Length} file(s) - {_doc?.PageCount} total pages");
                Scalpel.Services.Logger.Info("File", "merge.success", "PDFs merged", new { added = dlg.FileNames.Length });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "merge.fail", "Merge failed", ex);
                ScalpelDialog.Show(this, $"Merge failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Builds a map of named destination string → 0-based page index from a source document's
        /// /Dests dictionary and /Names /Dests name tree.
        /// </summary>
        private Dictionary<string, int> BuildNamedDestMap(PdfDocument src)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var catalog = src.Internals.Catalog;

                // Legacy flat /Dests dictionary
                var destsDict = catalog.Elements.GetDictionary("/Dests");
                if (destsDict != null)
                {
                    foreach (var key in destsDict.Elements.Keys)
                    {
                        PdfItem? val = DerefItem(destsDict.Elements[key] ?? new PdfInteger(-1));
                        int? idx = ResolveDestPageIndexInDoc(src, val);
                        if (idx.HasValue) map[key.TrimStart('/')] = idx.Value;
                    }
                }

                // Modern /Names /Dests name tree
                var namesDict = catalog.Elements.GetDictionary("/Names");
                var destTree  = namesDict?.Elements.GetDictionary("/Dests");
                if (destTree != null)
                    WalkNameTree(src, destTree, map);
            }
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "BuildNamedDestMap", "BuildNamedDestMap failed", ex); }
            return map;
        }

        private void WalkNameTree(PdfDocument src, PdfDictionary node, Dictionary<string, int> map)
        {
            var namesArr = node.Elements.GetArray("/Names");
            if (namesArr != null)
            {
                for (int i = 0; i + 1 < namesArr.Elements.Count; i += 2)
                {
                    var keyItem = namesArr.Elements[i];
                    string key  = keyItem is PdfString ks ? ks.Value : keyItem?.ToString()?.TrimStart('/') ?? "";
                    if (string.IsNullOrEmpty(key)) continue;
                    PdfItem? val = DerefItem(namesArr.Elements[i + 1]);
                    int? idx = ResolveDestPageIndexInDoc(src, val);
                    if (idx.HasValue) map[key] = idx.Value;
                }
            }

            var kids = node.Elements.GetArray("/Kids");
            if (kids != null)
            {
                for (int i = 0; i < kids.Elements.Count; i++)
                {
                    if (DerefItem(kids.Elements[i]) is PdfDictionary kid)
                        WalkNameTree(src, kid, map);
                }
            }
        }

        /// <summary>
        /// Resolves a destination value (PdfArray or PdfDictionary with /D) to a page index
        /// within the given source document by matching the page object number.
        /// </summary>
        private static int? ResolveDestPageIndexInDoc(PdfDocument src, PdfItem? val)
        {
            PdfArray? arr = val as PdfArray;
            if (arr is null && val is PdfDictionary vd)
                arr = vd.Elements.GetArray("/D");
            if (arr is null || arr.Elements.Count == 0) return null;

            var first = arr.Elements[0];
            int objNum = GetObjectNumber(first);
            if (objNum > 0)
            {
                for (int i = 0; i < src.PageCount; i++)
                {
                    var pgRef = src.Pages[i].Reference;
                    if (pgRef != null && pgRef.ObjectNumber == objNum) return i;
                }
            }
            else if (first is PdfInteger pi && pi.Value >= 0 && pi.Value < src.PageCount)
            {
                return pi.Value;
            }
            return null;
        }

        /// <summary>
        /// Walks all link annotations in pages [pageOffset, doc.PageCount) and rewrites any
        /// named-destination /D values to explicit [pageRef /Fit] arrays using the merged
        /// document's page references. This is needed because PdfSharpCore's import does not
        /// copy the source document's /Names /Dests catalog entries.
        /// </summary>
        private static void RewriteNamedDestLinks(PdfDocument doc, int pageOffset,
            Dictionary<string, int> namedDestMap)
        {
            for (int pi = pageOffset; pi < doc.PageCount; pi++)
            {
                try
                {
                    var page    = doc.Pages[pi];
                    var annotsArr = page.Elements.GetArray("/Annots");
                    if (annotsArr is null) continue;

                    for (int ai = 0; ai < annotsArr.Elements.Count; ai++)
                    {
                        PdfItem? elem = annotsArr.Elements[ai];
                        PdfDictionary? ann = elem as PdfDictionary
                            ?? (DerefItemStatic(elem) as PdfDictionary);
                        if (ann is null) continue;

                        var subtype = ann.Elements["/Subtype"]?.ToString() ?? "";
                        if (!subtype.Contains("Link")) continue;

                        // Check /A /D (GoTo action)
                        var actionDict = ann.Elements.GetDictionary("/A");
                        if (actionDict != null)
                        {
                            var s = actionDict.Elements["/S"]?.ToString() ?? "";
                            if (s.Contains("GoTo"))
                            {
                                var destItem = actionDict.Elements["/D"];
                                string? name = ExtractDestName(destItem);
                                if (name != null && namedDestMap.TryGetValue(name, out int srcIdx))
                                {
                                    int targetIdx = pageOffset + srcIdx;
                                    if (targetIdx < doc.PageCount)
                                        actionDict.Elements["/D"] = MakeExplicitDest(doc, targetIdx);
                                }
                            }
                        }
                        else
                        {
                            // Bare /Dest on annotation
                            var destItem = ann.Elements["/Dest"];
                            string? name = ExtractDestName(destItem);
                            if (name != null && namedDestMap.TryGetValue(name, out int srcIdx))
                            {
                                int targetIdx = pageOffset + srcIdx;
                                if (targetIdx < doc.PageCount)
                                    ann.Elements["/Dest"] = MakeExplicitDest(doc, targetIdx);
                            }
                        }
                    }
                }
                catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "RewriteNamedDestLinks", "RewriteNamedDestLinks failed", ex); }
            }
        }

        private static string? ExtractDestName(PdfItem? item)
        {
            if (item is null) return null;
            if (item is PdfString ps) return ps.Value;
            if (item is PdfName   pn) return pn.Value.TrimStart('/');
            return null;
        }

        private static PdfArray MakeExplicitDest(PdfDocument doc, int pageIndex)
        {
            var arr = new PdfArray(doc);
            arr.Elements.Add(doc.Pages[pageIndex].Reference);
            arr.Elements.Add(new PdfName("/Fit"));
            return arr;
        }

        // Static version of DerefItem for use in static helpers.
        private static PdfItem DerefItemStatic(PdfItem item)
        {
            var valueProp = item.GetType().GetProperty("Value",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (valueProp?.GetValue(item) is PdfObject resolved) return resolved;
            return item;
        }

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var currentFile = _currentFile;
            var selected = PageList.SelectedItems;
            if (selected.Count == 0) { ScalpelDialog.Show(this, "Select pages to extract."); return; }
            var dlg = new SaveFileDialog { Filter = "PDF files|*.pdf", Title = "Save extracted pages as",
                                           CheckFileExists = false, CheckPathExists = true };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var indices = new List<int>();
                foreach (PageThumbnailVm vm in selected) indices.Add(vm.PageIndex);
                using var importDoc = PdfReader.Open(currentFile, PdfDocumentOpenMode.Import);
                var newDoc = new PdfDocument();
                foreach (var idx in indices.OrderBy(i => i))
                    newDoc.AddPage(importDoc.Pages[idx]);
                newDoc.Save(dlg.FileName);
                SetStatus(string.Format(Loc("Str_Extracted"), indices.Count, System.IO.Path.GetFileName(dlg.FileName)));
                Scalpel.Services.Logger.Info("File", "extract.success", "Pages extracted", new { count = indices.Count });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "extract.fail", "Extract failed", ex);
                ScalpelDialog.Show(this, $"Split failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var doc = _doc;
            var selected = PageList.SelectedItems;
            if (selected.Count == 0) { ScalpelDialog.Show(this, "Select pages to delete."); return; }
            var result = ScalpelDialog.Show(this, $"Delete {selected.Count} {(selected.Count == 1 ? "page" : "pages")}?", "Scalpel",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                var indices = new List<int>();
                foreach (PageThumbnailVm vm in selected) indices.Add(vm.PageIndex);
                foreach (var idx in indices.OrderByDescending(i => i))
                    doc.Pages.RemoveAt(idx);
                SaveTempAndReload();
                SetStatus(string.Format(Loc("Str_Deleted"), indices.Count, _doc?.PageCount));
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Delete failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertBlankPage_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var doc = _doc;
            int insertAfter = PageList.SelectedIndex >= 0 ? PageList.SelectedIndex : doc.PageCount - 1;
            try
            {
                var blank = new PdfPage { Width = XUnit.FromPoint(595), Height = XUnit.FromPoint(842) };
                doc.Pages.Insert(insertAfter + 1, blank);
                SaveTempAndReload();
                PageList.SelectedIndex = insertAfter + 1;
                SetStatus($"Inserted blank page at position {insertAfter + 2}");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Insert failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || PageList.SelectedIndex <= 0) return;
            var doc = _doc;
            int idx = PageList.SelectedIndex;
            var page = doc.Pages[idx];
            doc.Pages.RemoveAt(idx);
            doc.Pages.Insert(idx - 1, page);
            SaveTempAndReload();
            PageList.SelectedIndex = idx - 1;
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || PageList.SelectedIndex < 0 || PageList.SelectedIndex >= _doc.PageCount - 1) return;
            var doc = _doc;
            int idx = PageList.SelectedIndex;
            var page = doc.Pages[idx];
            doc.Pages.RemoveAt(idx);
            doc.Pages.Insert(idx + 1, page);
            SaveTempAndReload();
            PageList.SelectedIndex = idx + 1;
        }

        private void SaveInPlace()
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            // Save back to the user's real file. After a page edit (crop/rotate) _currentFile is a
            // temp working copy, so the real path is kept in _originalFile. If there is no real path
            // (e.g. a repaired temp-backed open), fall back to Save As.
            if (string.IsNullOrEmpty(_originalFile)) { SaveAs_Click(this, new RoutedEventArgs()); return; }
            CommitActiveTextBox();
            string saveTarget = _originalFile!;
            try
            {
                bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
                WriteFormValuesToDocument();
                // Always strip link annotation borders regardless of user annotation count
                // so mailto/URI links don't appear as strikethrough lines in other viewers.
                StripLinkAnnotationBorders(_doc);

                if (hasAnnotations)
                {
                    // Save a clean copy of the doc (without burned annotations), burn
                    // annotations into the real file, then restore the in-memory doc
                    // from the clean copy so future saves don't double-burn.
                    var tempClean = App.MakeTempFile("clean");
                    _doc.Save(tempClean);
                    DrawAnnotationsOnDocument();
                    _doc.Save(saveTarget);
                    _doc.Close();
                    _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                    _currentFile = tempClean;
                }
                else
                {
                    _doc.Save(saveTarget);
                }

                MarkDirty(false);
                SetStatus($"Saved - {System.IO.Path.GetFileName(saveTarget)}");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Save failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Toolbar Save (the main half of the split button): saves straight back to the
        // original file. "Save As" lives in the chevron dropdown beside it. SaveInPlace
        // handles the empty-doc and no-real-path (repaired temp-backed open -> Save As)
        // cases, and this matches the Ctrl+S shortcut behaviour.
        private void Save_Click(object sender, RoutedEventArgs e) => SaveInPlace();

        // Chevron beside Save: drops the Save As menu (same pattern as FileMenu_Click).
        private void SaveMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu is not null)
            { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            CommitActiveTextBox();
            var dlg = new SaveFileDialog { Filter = "PDF files|*.pdf", Title = "Save PDF as",
                                           CheckFileExists = false, CheckPathExists = true };
            string? seed = _originalFile ?? _currentFile;
            if (!string.IsNullOrEmpty(seed))
            {
                dlg.FileName = System.IO.Path.GetFileName(seed);
                var seedDir = System.IO.Path.GetDirectoryName(_originalFile ?? "");
                if (!string.IsNullOrEmpty(seedDir) && System.IO.Directory.Exists(seedDir))
                    dlg.InitialDirectory = seedDir;
            }
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
                WriteFormValuesToDocument();
                // Always strip link annotation borders regardless of user annotation count.
                StripLinkAnnotationBorders(_doc);

                if (hasAnnotations)
                {
                    var tempClean = App.MakeTempFile("clean");
                    _doc.Save(tempClean);
                    DrawAnnotationsOnDocument();
                    _doc.Save(dlg.FileName);
                    _doc.Close();
                    _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                    _currentFile = tempClean;
                    _originalFile = dlg.FileName;
                    FileNameLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
                    MarkDirty(false);
                    SetStatus($"Saved with annotations to {System.IO.Path.GetFileName(dlg.FileName)}");
                }
                else
                {
                    _doc.Save(dlg.FileName);
                    _originalFile = dlg.FileName;
                    FileNameLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
                    MarkDirty(false);
                    SetStatus($"Saved to {System.IO.Path.GetFileName(dlg.FileName)}");
                }
                Scalpel.Services.Logger.Info("File", "save.success", "PDF saved", new { path = dlg.FileName });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "save.fail", "Save failed", ex);
                ScalpelDialog.Show(this, $"Save failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveFlattened_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            CommitActiveTextBox();
            var dlg = new SaveFileDialog { Filter = "PDF files|*.pdf", Title = "Save Flattened PDF",
                                           CheckFileExists = false, CheckPathExists = true };
            if (dlg.ShowDialog(this) != true) return;

            // Burn any pending annotations into a temp source for rasterization
            // (must happen on UI thread before we go async)
            string sourcePath;
            bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
            if (hasAnnotations)
            {
                var tempClean  = App.MakeTempFile("clean");
                var tempBurned = App.MakeTempFile("burned");
                _doc.Save(tempClean);
                DrawAnnotationsOnDocument();
                _doc.Save(tempBurned);
                _doc.Close();
                _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                _currentFile = tempClean;
                sourcePath = tempBurned;
            }
            else
            {
                var temp = App.MakeTempFile("src");
                _doc.Save(temp);
                sourcePath = temp;
            }

            int pageCount = _doc.PageCount;

            // Snapshot per-page dimensions (CropBox-aware) before going off-thread
            var pageDims = new (double widthPt, double heightPt)[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                var p = _doc.Pages[i];
                pageDims[i] = (p.Width.Point, p.Height.Point);
            }

            // Show a progress overlay so the user knows we're working
            var overlay = ShowFlattenProgress(pageCount);
            string outputPath = dlg.FileName;

            try
            {
                // Rasterize on a background thread — keeps the UI responsive
                await Task.Run(() =>
                {
                    // Rasterize pages across CPU cores. Docnet/PDFium is not thread-safe, so the
                    // pdfium render is serialized behind a lock; the PNG encode (GDI+) runs in
                    // parallel. Pages are assembled into the PDF afterwards, in order.
                    var pngPages = new byte[pageCount][];
                    var docGate  = new object();
                    int done     = 0;
                    var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) };
                    Parallel.For(0, pageCount, po, i =>
                    {
                        // Per-page pixel dimensions at 150 DPI, sized to the CropBox
                        int pw = Math.Max(1, (int)(pageDims[i].widthPt  * 150 / 72.0));
                        int ph = Math.Max(1, (int)(pageDims[i].heightPt * 150 / 72.0));
                        // PageDimensions requires dimOne <= dimTwo (short-edge, long-edge)
                        int dimMin = Math.Min(pw, ph);
                        int dimMax = Math.Max(pw, ph);

                        byte[] bgra; int rw, rh;
                        lock (docGate)
                        {
                            using var pageDocReader = DocLib.Instance.GetDocReader(sourcePath, new PageDimensions(dimMin, dimMax));
                            using var pr = pageDocReader.GetPageReader(i);
                            bgra = pr.GetImage();
                            rw   = pr.GetPageWidth();
                            rh   = pr.GetPageHeight();
                        }
                        // Encode BGRA to PNG (GDI+) outside the lock so it parallelizes.
                        pngPages[i] = RenderToPng(bgra, rw, rh);

                        int n = System.Threading.Interlocked.Increment(ref done);
                        Dispatcher.BeginInvoke(new Action(() => UpdateFlattenProgress(overlay, n, pageCount)));
                    });

                    // Assemble the output PDF in page order (PdfSharp is single-threaded).
                    var outDoc = new PdfDocument();
                    try
                    {
                        for (int i = 0; i < pageCount; i++)
                        {
                            var newPage = outDoc.AddPage();
                            newPage.Width  = XUnit.FromPoint(pageDims[i].widthPt);
                            newPage.Height = XUnit.FromPoint(pageDims[i].heightPt);
                            using var xi  = XImage.FromStream(() => new MemoryStream(pngPages[i]));
                            using var gfx = XGraphics.FromPdfPage(newPage);
                            gfx.DrawImage(xi, 0, 0, newPage.Width.Point, newPage.Height.Point);
                        }
                        outDoc.Save(outputPath);
                    }
                    finally
                    {
                        outDoc.Dispose();
                    }
                });

                MarkDirty(false);
                SetStatus($"Flattened PDF saved to {System.IO.Path.GetFileName(outputPath)}");
                Scalpel.Services.Logger.Info("File", "flatten.success", "PDF flattened", new { path = outputPath, pages = pageCount });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "flatten.fail", "Flatten failed", ex);
                try { ScalpelDialog.Show(this, $"Flatten failed:\n{ex.GetType().Name}: {ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error); }
                catch { /* dialog failed; overlay still removed in finally */ }
            }
            finally
            {
                try { HideFlattenProgress(overlay); } catch { /* ensure overlay never leaks */ }
            }
        }

        // ---- flatten progress overlay helpers ----

        private Border ShowFlattenProgress(int pageCount, string verb = "Flattening")
        {
            var progressText = new TextBlock
            {
                Text       = $"{verb} page 0 of {pageCount}...",
                Foreground = Brushes.White,
                FontSize   = 14,
                Tag        = verb   // stored so UpdateFlattenProgress can read it
            };
            var panel = new StackPanel
            {
                Orientation         = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            panel.Children.Add(progressText);

            var overlay = new Border
            {
                Background        = new SolidColorBrush(Color.FromArgb(200, 0x1a, 0x1a, 0x1a)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment   = VerticalAlignment.Stretch,
                Child             = panel,
                Tag               = "FlattenOverlay"
            };
            Panel.SetZIndex(overlay, 999);

            // Attach to the root grid
            if (Content is Grid rootGrid)
                rootGrid.Children.Add(overlay);

            return overlay;
        }

        private static void UpdateFlattenProgress(Border overlay, int current, int total)
        {
            if (overlay.Child is StackPanel panel)
                foreach (var child in panel.Children)
                    if (child is TextBlock tb && tb.Tag is string verb)
                        tb.Text = $"{verb} page {current} of {total}...";
        }

        private void HideFlattenProgress(Border overlay)
        {
            if (Content is Grid rootGrid)
                rootGrid.Children.Remove(overlay);
        }

        /// <summary>
        /// Encodes raw BGRA pixel data from pdfium to PNG without touching the UI thread.
        /// GDI+ Format32bppArgb is BGRA in memory — matches pdfium output exactly.
        /// </summary>
        private static byte[] RenderToPng(byte[] bgra, int width, int height)
        {
            var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                using var bmp = new System.Drawing.Bitmap(
                    width, height, width * 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                    pin.AddrOfPinnedObject());
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
            finally { pin.Free(); }
        }

        private async void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            CommitActiveTextBox();

            // Burn pending annotations into a temp copy on the UI thread before going off-thread
            bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
            string printPath;
            string? tempFlattened = null;
            if (hasAnnotations)
            {
                var tempClean = App.MakeTempFile("clean");
                _doc.Save(tempClean);
                DrawAnnotationsOnDocument();
                printPath = App.MakeTempFile("print");
                _doc.Save(printPath);
                tempFlattened = printPath;
                _doc.Close();
                _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                _currentFile = tempClean;
            }
            else
            {
                printPath = _currentFile;
            }

            int pageCount = _doc.PageCount;

            // Rasterize every page, then hand them to our own preview window. WPF's OS
            // PrintDialog cannot show a preview ("This app doesn't support print preview"),
            // so Scalpel renders the preview and drives printing itself.
            var overlay = ShowFlattenProgress(pageCount, "Preparing");
            bool overlayHidden = false;
            byte[][]? pngPages = null;
            int[]?    rasterW  = null;
            int[]?    rasterH  = null;

            try
            {
                // Rasterize all pages on background thread — keeps UI responsive
                await Task.Run(() =>
                {
                    pngPages = new byte[pageCount][];
                    rasterW  = new int[pageCount];
                    rasterH  = new int[pageCount];
                    using var docReader = DocLib.Instance.GetDocReader(printPath, new PageDimensions(1536, 1536));
                    for (int i = 0; i < pageCount; i++)
                    {
                        using var pr = docReader.GetPageReader(i);
                        int w = pr.GetPageWidth();
                        int h = pr.GetPageHeight();
                        pngPages[i] = RenderToPng(pr.GetImage(), w, h);
                        rasterW[i]  = w;
                        rasterH[i]  = h;
                        int captured = i;
                        Dispatcher.Invoke(() => UpdateFlattenProgress(overlay, captured + 1, pageCount));
                    }
                });

                // Decode to frozen BitmapSources on the UI thread for the preview window.
                var sources = new BitmapSource[pageCount];
                for (int i = 0; i < pageCount; i++)
                {
                    using var ms = new MemoryStream(pngPages![i]);
                    var src = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    src.Freeze();
                    sources[i] = src;
                }

                HideFlattenProgress(overlay);
                overlayHidden = true;

                var preview = new PrintPreviewWindow(this, sources, rasterW!, rasterH!);
                if (preview.ShowDialog() == true)
                {
                    SetStatus(string.Format(Loc("Str_Printed"), preview.PrintedPageCount));
                    Scalpel.Services.Logger.Info("Print", "print.success", "Document printed", new { pages = preview.PrintedPageCount });
                }
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Print", "print.fail", "Print failed", ex);
                try { ScalpelDialog.Show(this, $"Print failed:\n{ex.GetType().Name}: {ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error); }
                catch { }
            }
            finally
            {
                if (!overlayHidden) try { HideFlattenProgress(overlay); } catch { }
                if (tempFlattened != null) try { System.IO.File.Delete(tempFlattened); } catch { }
            }
        }

    }
}
