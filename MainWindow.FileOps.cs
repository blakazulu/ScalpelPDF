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
        // File operations
        // ============================================================

        private void OpenFile(string path)
        {
            // Files on UNC / network shares - notably the WSL \\wsl$ 9P filesystem - can hand
            // back partial reads, making the PDF parser see a truncated file ("Unexpected EOF").
            // Copy such files to a local temp via File.ReadAllBytes (which reads to EOF) and open
            // from there. `path` stays the user's real path for display and Save.
            string srcPath = path;
            if (IsNetworkPath(path))
            {
                try
                {
                    var localCopy = App.MakeTempFile("netopen");
                    File.WriteAllBytes(localCopy, File.ReadAllBytes(path));
                    srcPath = localCopy;
                }
                catch { srcPath = path; }
            }

            try
            {
                if (_doc is not null) { _doc.Close(); _doc = null; }
                _doc = PdfReader.Open(srcPath, PdfDocumentOpenMode.Modify);
                // PdfSharp cannot save modified encrypted PDFs — it copies unmodified encrypted
                // stream bytes verbatim but fails when it has to re-serialize a dirty object.
                // Strip encryption silently at open time via Import so all edits work correctly.
                if (PdfFileHasEncryption(srcPath))
                {
                    // PdfSharp can read encrypted PDFs but cannot re-save them once modified.
                    // Strip encryption now via PDFium (lossless), falling back to Import mode.
                    _doc.Close(); _doc = null;
                    var repairedPath = App.MakeTempFile("repaired");
                    bool ok = TryPdfiumStripEncryption(srcPath, repairedPath)
                           || TryImportRepairToPath(srcPath, repairedPath);
                    if (!ok) { TryRepairAndOpen(srcPath); return; }
                    _doc = PdfReader.Open(repairedPath, PdfDocumentOpenMode.Modify);
                    _currentFile = repairedPath;
                    FinishOpenFile(path, repairedPath);
                    MarkDirty(true);
                    return;
                }
                _currentFile = srcPath;
                FinishOpenFile(path, srcPath);
            }
            catch (Exception ex) when (IsOwnerPasswordException(ex))
            {
                // PDF has owner/permissions restrictions but no open password —
                // open read-only so the user can still view and print it.
                try
                {
                    if (_doc is not null) { _doc.Close(); _doc = null; }
                    _doc = PdfReader.Open(srcPath, PdfDocumentOpenMode.ReadOnly);
                    _currentFile = srcPath;
                    FinishOpenFile(path, srcPath);
                    SetStatus(string.Format(Loc("Str_OpenedReadOnly"), System.IO.Path.GetFileName(path), _doc.PageCount));
                }
                catch (Exception ex2)
                {
                    ScalpelDialog.Show(this, string.Format(Loc("Str_Dlg_FailedOpen"), ex2.Message), Loc("Str_Dlg_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) when (IsPasswordException(ex))
            {
                string? pw = PromptForPassword(path);
                if (pw is null) return;
                try
                {
                    if (_doc is not null) { _doc.Close(); _doc = null; }
                    _doc = PdfReader.Open(srcPath, pw, PdfDocumentOpenMode.Modify);
                    // Save a decrypted temp copy so Docnet can render without needing the password
                    var tempDec = App.MakeTempFile("dec");
                    _doc.Save(tempDec);
                    _doc.Close();
                    _doc = PdfReader.Open(tempDec, PdfDocumentOpenMode.Modify);
                    _currentFile = tempDec;
                    FinishOpenFile(path, tempDec);
                }
                catch (Exception ex2)
                {
                    ScalpelDialog.Show(this, string.Format(Loc("Str_Dlg_FailedOpen"), ex2.Message), Loc("Str_Dlg_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex) when (IsXRefException(ex))
            {
                // Some PDFs have malformed or non-standard XRef tables that PdfSharp can't
                // open in Modify mode. Fall back to ReadOnly; if that also fails, offer repair.
                try
                {
                    if (_doc is not null) { _doc.Close(); _doc = null; }
                    _doc = PdfReader.Open(srcPath, PdfDocumentOpenMode.ReadOnly);
                    _currentFile = srcPath;
                    FinishOpenFile(path, srcPath);
                    SetStatus(string.Format(Loc("Str_OpenedReadOnlyXRef"), System.IO.Path.GetFileName(path), _doc.PageCount));
                    ScalpelDialog.Show(this,
                        $"\"{System.IO.Path.GetFileName(path)}\" has a non-standard structure and was opened read-only.\n\nEditing, saving, and some other features may not work correctly.",
                        "Scalpel", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch
                {
                    // ReadOnly also failed — offer to repair.
                    var result = ScalpelDialog.Show(this,
                        $"This PDF has a damaged structure and couldn't be opened.\n\nWould you like Scalpel to attempt a repair? A repaired copy will be created — the original file will not be changed.\n\nNote: repaired files may be missing bookmarks, forms, and other interactive features.",
                        "Scalpel", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                        TryRepairAndOpen(srcPath);
                }
            }
            catch (Exception ex) when (IsEofParseException(ex))
            {
                // PdfSharpCore rejects some structurally-valid PDFs with "Unexpected EOF" even
                // though PDFium (and every common viewer) reads them fine. Re-save the file
                // losslessly through PDFium to a clean temp and open that; fall back to an
                // import repair, then to a rasterize repair as a last resort.
                try
                {
                    if (_doc is not null) { _doc.Close(); _doc = null; }
                    var repairedPath = App.MakeTempFile("repaired");
                    bool ok = TryPdfiumStripEncryption(srcPath, repairedPath)
                           || TryImportRepairToPath(srcPath, repairedPath);
                    if (!ok) { TryRepairAndOpen(srcPath); return; }
                    _doc = PdfReader.Open(repairedPath, PdfDocumentOpenMode.Modify);
                    _currentFile = repairedPath;
                    FinishOpenFile(path, repairedPath);
                    // Open clean: the normalized copy is content-equivalent, so a view-only open
                    // should not nag to save. _currentFile points at the temp and _originalFile at
                    // the user's path, so the original is only ever overwritten if they choose to
                    // Save after making edits (FinishOpenFile already cleared the dirty flag).
                    SetStatus($"Opened {System.IO.Path.GetFileName(path)} ({_doc.PageCount} pages) - recovered via PDFium.");
                }
                catch (Exception ex2)
                {
                    ScalpelDialog.Show(this, string.Format(Loc("Str_Dlg_FailedOpen"), ex2.Message), Loc("Str_Dlg_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, string.Format(Loc("Str_Dlg_FailedOpen"), ex.Message), Loc("Str_Dlg_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // PdfSharpCore throws on some structurally-valid PDFs that PDFium opens fine - most
        // often "Unexpected EOF" from SharpZipLib's Flate inflater while reading a FlateDecode
        // cross-reference stream (multi-revision PDFs with incremental updates / dangling xref
        // entries that tolerant parsers ignore). Match by message AND exception type across the
        // whole inner-exception chain so a wrapped SharpZipBaseException is still recovered.
        private static bool IsEofParseException(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                string msg  = e.Message ?? string.Empty;
                string type = e.GetType().FullName ?? string.Empty;
                if (msg.IndexOf("EOF", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("end of file", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("Inflater", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("FlateDecode", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("SharpZip", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsXRefException(Exception ex) =>
            ex.Message.IndexOf("XRef", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("cross-reference", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("trailer", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("Invalid PDF file", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("startxref", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("Unexpected token", StringComparison.OrdinalIgnoreCase) >= 0;

        // True for UNC paths (\\server\share, \\wsl$\..., \\wsl.localhost\...) and mapped
        // network drives. Such files are copied locally before opening to avoid 9P short reads.
        private static bool IsNetworkPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            try
            {
                var root = System.IO.Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && root!.Length >= 2 && root[1] == ':')
                    return new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Imports pages from <paramref name="sourcePath"/> into a fresh PdfDocument and saves it
        /// to <paramref name="destPath"/>. Returns true on success, false on failure.
        /// Unlike TryRepairAndOpen this has no UI side-effects and can be used mid-operation.
        /// </summary>
        /// <param name="stripRotations">
        // ── PDFium P/Invoke ──────────────────────────────────────────────────────────
        // PDFium (pdfium.dll) is already shipped with Docnet. We use it here to strip
        // encryption from PDFs that PdfSharpCore can read but cannot re-save when modified.

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FPDF_LoadDocument(
            [MarshalAs(UnmanagedType.LPStr)] string filePath,
            [MarshalAs(UnmanagedType.LPStr)] string? password);

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDF_CloseDocument(IntPtr document);

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool FPDF_SaveWithVersion(
            IntPtr document, ref FPDF_FILEWRITE fileWrite, uint flags, int fileVersion);

        [StructLayout(LayoutKind.Sequential)]
        private struct FPDF_FILEWRITE
        {
            public int version;          // must be 1
            public IntPtr WriteBlock;    // cdecl: int WriteBlock(FPDF_FILEWRITE*, const void*, unsigned long)
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PdfWriteBlockDelegate(IntPtr pThis, IntPtr pData, uint size);

        private const uint FPDF_REMOVE_SECURITY = 3;

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDF_GetPageCount(IntPtr document);

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FPDF_LoadPage(IntPtr document, int page_index);

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDF_ClosePage(IntPtr page);

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDFPage_SetRotation(IntPtr page, int rotation);

        [DllImport("pdfium.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool FPDFPage_GenerateContent(IntPtr page);

        /// <summary>
        /// Returns true if the PDF file has an /Encrypt entry in its trailer.
        /// Scans the last 2 KB so it's fast; works regardless of how PdfSharp
        /// reports security state after authenticating with an empty password.
        /// </summary>
        private static bool PdfFileHasEncryption(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                long scan = Math.Min(2048, fs.Length);
                fs.Seek(-scan, SeekOrigin.End);
                var buf = new byte[scan];
                _ = fs.Read(buf, 0, buf.Length);
                // Look for /Encrypt in the raw bytes (Latin-1 safe)
                var text = System.Text.Encoding.GetEncoding(1252).GetString(buf);
                return text.Contains("/Encrypt");
            }
            catch { return false; }
        }

        /// <summary>
        /// Uses PDFium to save a copy of <paramref name="sourcePath"/> with all security/encryption
        /// removed. Returns true on success. Falls back gracefully if PDFium is unavailable.
        /// PDFium is already initialised by Docnet; no separate init call is needed.
        /// </summary>
        private static bool TryPdfiumStripEncryption(string sourcePath, string destPath)
        {
            try
            {
                // Ensure PDFium is initialised — Docnet does this lazily on first use,
                // so force it now before we call PDFium P/Invoke directly.
                try { _ = DocLib.Instance; } catch { }

                var doc = FPDF_LoadDocument(sourcePath, null);
                if (doc == IntPtr.Zero) return false;
                try
                {
                    using var ms = new MemoryStream();
                    PdfWriteBlockDelegate cb = (_, pData, size) =>
                    {
                        var buf = new byte[size];
                        Marshal.Copy(pData, buf, 0, (int)size);
                        ms.Write(buf, 0, (int)size);
                        return 1;
                    };
                    var gch = GCHandle.Alloc(cb);
                    try
                    {
                        var fw = new FPDF_FILEWRITE
                        {
                            version = 1,
                            WriteBlock = Marshal.GetFunctionPointerForDelegate(cb)
                        };
                        if (!FPDF_SaveWithVersion(doc, ref fw, FPDF_REMOVE_SECURITY, 0))
                            return false;
                    }
                    finally { gch.Free(); }
                    File.WriteAllBytes(destPath, ms.ToArray());
                    return true;
                }
                finally { FPDF_CloseDocument(doc); }
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Uses PDFium to load <paramref name="sourcePath"/>, zero-out all page /Rotate values,
        /// strip encryption, and save to <paramref name="destPath"/>. Returns true on success.
        /// Called from SaveTempAndReload's xref-error fallback — PDFium is guaranteed to be
        /// initialised by then because the page preview has already rendered via Docnet.
        /// </summary>
        private static bool TryPdfiumSaveWithZeroRotations(string sourcePath, string destPath)
        {
            try
            {
                var doc = FPDF_LoadDocument(sourcePath, null);
                if (doc == IntPtr.Zero)
                {
                    try { File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scalpel_pdfium_debug.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FPDF_LoadDocument returned null for: {sourcePath}\n\n"); } catch { }
                    return false;
                }
                try
                {
                    int pageCount = FPDF_GetPageCount(doc);
                    for (int i = 0; i < pageCount; i++)
                    {
                        var page = FPDF_LoadPage(doc, i);
                        if (page == IntPtr.Zero) continue;
                        try
                        {
                            FPDFPage_SetRotation(page, 0);   // strip /Rotate so Docnet renders cleanly
                            FPDFPage_GenerateContent(page);
                        }
                        finally { FPDF_ClosePage(page); }
                    }

                    using var ms = new MemoryStream();
                    PdfWriteBlockDelegate cb = (_, pData, size) =>
                    {
                        var buf = new byte[size];
                        Marshal.Copy(pData, buf, 0, (int)size);
                        ms.Write(buf, 0, (int)size);
                        return 1;
                    };
                    var gch = GCHandle.Alloc(cb);
                    try
                    {
                        var fw = new FPDF_FILEWRITE
                        {
                            version = 1,
                            WriteBlock = Marshal.GetFunctionPointerForDelegate(cb)
                        };
                        if (!FPDF_SaveWithVersion(doc, ref fw, FPDF_REMOVE_SECURITY, 0))
                            return false;
                    }
                    finally { gch.Free(); }

                    File.WriteAllBytes(destPath, ms.ToArray());
                    return true;
                }
                finally { FPDF_CloseDocument(doc); }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scalpel_pdfium_debug.txt"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TryPdfiumSaveWithZeroRotations failed\n" +
                        $"  source: {sourcePath}\n" +
                        $"  type:   {ex.GetType().FullName}\n" +
                        $"  msg:    {ex.Message}\n" +
                        $"  stack:  {ex.StackTrace}\n\n");
                }
                catch { /* log failure is non-fatal */ }
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────

        /// <param name="stripRotations">
        /// Pass true when called from SaveTempAndReload (rotations already stripped in source).
        /// Pass false for open-time repair so original page rotations are preserved.
        /// </param>
        private static bool TryImportRepairToPath(string sourcePath, string destPath, bool stripRotations = false)
        {
            try
            {
                using var importDoc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
                var cleanDoc = new PdfDocument();
                for (int i = 0; i < importDoc.PageCount; i++)
                    cleanDoc.Pages.Add(importDoc.Pages[i]);
                if (stripRotations)
                    for (int i = 0; i < cleanDoc.PageCount; i++)
                        cleanDoc.Pages[i].Rotate = 0;
                cleanDoc.Save(destPath);
                cleanDoc.Close();
                return true;
            }
            catch { return false; }
        }

        private void TryRepairAndOpen(string path)
        {
            // Strategy 1: PdfSharpCore Import mode — page-copy, more lenient than Modify/ReadOnly.
            // Works when the XRef is partially corrupt but the object data is intact.
            try
            {
                if (_doc is not null) { _doc.Close(); _doc = null; }
                PdfDocument repairedDoc;
                using (var importDoc = PdfReader.Open(path, PdfDocumentOpenMode.Import))
                {
                    repairedDoc = new PdfDocument();
                    for (int i = 0; i < importDoc.PageCount; i++)
                        repairedDoc.Pages.Add(importDoc.Pages[i]);
                }
                var repairedPath = App.MakeTempFile("repaired");
                repairedDoc.Save(repairedPath);
                repairedDoc.Close();
                _doc = PdfReader.Open(repairedPath, PdfDocumentOpenMode.Modify);
                _currentFile = repairedPath;
                FinishOpenFile(path, repairedPath);
                MarkDirty(true); // repaired copy lives in temp — user must Save As
                SetStatus(string.Format(Loc("Str_OpenedRepaired"), System.IO.Path.GetFileName(path), _doc.PageCount));
                ScalpelDialog.Show(this,
                    $"\"{System.IO.Path.GetFileName(path)}\" was repaired successfully.\n\nBookmarks, forms, and other interactive features may have been lost. Use Save As to write the repaired file to a new location.",
                    "Scalpel", MessageBoxButton.OK, MessageBoxImage.None);
                return;
            }
            catch { }

            // Strategy 2: PDFium rasterize repair.
            // PDFium has its own internal XRef recovery that handles damage PdfSharpCore cannot.
            // Each page is rendered to a bitmap and rebuilt into a clean PDF.
            // Text will not be selectable in the result, but the file will open and be printable.
            try
            {
                RepairViaDocnetRasterize(path);
                return;
            }
            catch { }

            ScalpelDialog.Show(this,
                "Repair failed — the file is too severely damaged to recover.\n\nTry opening the original in a different application (Adobe Acrobat, browsers) which may have additional recovery options.",
                "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Repair fallback: uses PDFium (Docnet) to render each page to a bitmap, then rebuilds
        /// a clean PdfSharpCore document from those bitmaps. Works on files where the XRef/trailer
        /// is too damaged for PdfSharpCore's parser but PDFium's recovery logic can still render.
        /// </summary>
        private void RepairViaDocnetRasterize(string path)
        {
            const int RenderPx = 2048;

            using var docReader = DocLib.Instance.GetDocReader(path, new PageDimensions(RenderPx, RenderPx));
            int pageCount = docReader.GetPageCount();
            if (pageCount <= 0) throw new InvalidOperationException("PDFium could not read any pages.");

            var newDoc = new PdfDocument();

            for (int i = 0; i < pageCount; i++)
            {
                using var pr = docReader.GetPageReader(i);
                int bw = pr.GetPageWidth();
                int bh = pr.GetPageHeight();
                if (bw <= 0 || bh <= 0) continue;

                var raw = pr.GetImage();
                if (raw is null || raw.Length == 0) continue;

                // Encode the raw BGRA frame as PNG via WPF BitmapEncoder (UI thread is fine here).
                var wb = new WriteableBitmap(bw, bh, 96, 96, PixelFormats.Bgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, bw, bh), raw, bw * 4, 0);
                wb.Freeze();

                byte[] pngBytes;
                using (var ms = new System.IO.MemoryStream())
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(wb));
                    enc.Save(ms);
                    pngBytes = ms.ToArray();
                }

                // Build the page at correct aspect ratio scaled to A4-ish width.
                double pageW = 595.28;
                double pageH = pageW * bh / bw;

                var page = newDoc.AddPage();
                page.Width  = XUnit.FromPoint(pageW);
                page.Height = XUnit.FromPoint(pageH);

                using var gfx = XGraphics.FromPdfPage(page);
                var xImg = XImage.FromStream(() => new System.IO.MemoryStream(pngBytes));
                gfx.DrawImage(xImg, 0, 0, pageW, pageH);
            }

            if (newDoc.PageCount == 0)
                throw new InvalidOperationException("PDFium rendered 0 usable pages.");

            var repairedPath = App.MakeTempFile("repaired");
            newDoc.Save(repairedPath);
            newDoc.Close();

            if (_doc is not null) { _doc.Close(); _doc = null; }
            _doc = PdfReader.Open(repairedPath, PdfDocumentOpenMode.Modify);
            _currentFile = repairedPath;
            FinishOpenFile(path, repairedPath);
            MarkDirty(true); // repaired copy lives in temp — user must Save As
            SetStatus(string.Format(Loc("Str_OpenedRasterRepair"), System.IO.Path.GetFileName(path), _doc.PageCount));
            ScalpelDialog.Show(this,
                $"\"{System.IO.Path.GetFileName(path)}\" was repaired by rasterizing through PDFium.\n\nText is not selectable in the repaired copy. Use Save As to write it to a new location.",
                "Scalpel", MessageBoxButton.OK, MessageBoxImage.None);
        }

        private static bool IsOwnerPasswordException(Exception ex) =>
            ex.Message.IndexOf("owner", StringComparison.OrdinalIgnoreCase) >= 0 &&
            ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0;

        private void FinishOpenFile(string displayPath, string workingPath)
        {
            try { if (System.IO.File.Exists(displayPath)) App.AddRecentFile(displayPath); } catch { }
            _currentFile = workingPath;
            _originalFile = displayPath;
            FileNameLabel.Text = System.IO.Path.GetFileName(displayPath);
            _annotations.Clear();
            _undoStack.Clear();
            _renderDims.Clear();
            _formTextValues.Clear();
            _formCheckValues.Clear();
            _formRadioValues.Clear();
            _allSearchRects.Clear();
            _searchResultPages.Clear();
            _searchPageCursor = -1;
            ClearSecondaryPages();
            ClearSelection();
            RefreshPageList();
            LoadOutlines();
            DropZone.Visibility = Visibility.Collapsed;
            PagePreviewPanel.Visibility = Visibility.Visible;
            if (_closeFileBtnRef != null) _closeFileBtnRef.IsEnabled = true;
            _pageJumpBox.IsEnabled = true;
            _pageTotalLabel.Text = $"/ {_doc!.PageCount}";
            MarkDirty(false);
            if (_doc!.PageCount > 0)
            {
                PageList.SelectedIndex = 0;
                // If Continuous mode is persisted from a previous session, SelectionChanged
                // returns early (no RenderPage call), so we have to bootstrap the panels here.
                if (_viewMode == ViewMode.Continuous)
                {
                    _pageContentPanel.Visibility = Visibility.Collapsed;
                    _continuousPanel.Visibility  = Visibility.Visible;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                        () => SetupContinuousView(0));
                }
                // Auto-fit to width once the first page has rendered and layout has settled.
                // DispatcherPriority.Background is lower than Loaded, so this fires after
                // all pending RenderPage / RefreshPageView callbacks have completed.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    (Action)(() =>
                    {
                        // Grid opens to its 3-across default; other modes fit to width. Background
                        // runs after every Loaded callback, so this is the final word on open and
                        // must be view-aware or it collapses the grid back to a single page.
                        if (_viewMode == ViewMode.Grid)
                            SetZoom(GridZoomForN(Math.Min(_doc?.PageCount ?? 1, 3)));
                        else
                            FitToWidth();  // Single, Two-Page, and Continuous open fit-to-width
                    }));
            }
            SetStatus(string.Format(Loc("Str_Opened"), System.IO.Path.GetFileName(displayPath), _doc.PageCount));
            Scalpel.Services.Logger.Info("File", "open.success", "PDF opened", new { path = displayPath, pages = _doc.PageCount });
            AddTab(_originalFile); // register/refresh the document tab (no-op for non-disk paths)
        }

        private static bool IsPasswordException(Exception ex) =>
            ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("protected", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("encrypted", StringComparison.OrdinalIgnoreCase) >= 0;

        private string? PromptForPassword(string filename)
        {
            string? result = null;
            var fontUI  = (FontFamily)Application.Current.FindResource("FontUI");
            var bgModal = (Brush)Application.Current.FindResource("BgModal");
            var bgCtrl  = (Brush)Application.Current.FindResource("BgControl");
            var fgPri   = (Brush)Application.Current.FindResource("TextPrimary");
            var fgSec   = (Brush)Application.Current.FindResource("TextSecondary");
            var bdrDim  = (Brush)Application.Current.FindResource("BorderDim");
            var accent  = (Brush)Application.Current.FindResource("Accent");

            var win = new Window
            {
                Title = "Password Required",
                Width = 360,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = fontUI
            };

            var outerBorder = new Border
            {
                Background      = bgModal,
                BorderBrush     = (Brush)Application.Current.FindResource("BorderDim"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(12),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4, Direction = 270
                }
            };

            // Title bar
            var titleBar = new Border
            {
                Background   = (Brush)Application.Current.FindResource("BgPanel"),
                Padding      = new Thickness(16, 10, 8, 10),
                CornerRadius = new CornerRadius(11, 11, 0, 0)
            };
            titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };
            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleGrid.Children.Add(new TextBlock
            {
                Text       = "Password Required",
                Foreground = fgPri,
                FontWeight = FontWeights.SemiBold,
                FontSize   = (double)Application.Current.FindResource("FsDialogTitle"),
                FontFamily = fontUI,
                VerticalAlignment = VerticalAlignment.Center
            });
            var closeTitleBtn = new Button
            {
                Style   = (Style)Application.Current.FindResource("StudioIconButton"),
                Content = Application.Current.FindResource("Ico_WinClose")
            };
            closeTitleBtn.Click += (_, _2) => { win.DialogResult = false; };
            Grid.SetColumn(closeTitleBtn, 1);
            titleGrid.Children.Add(closeTitleBtn);
            titleBar.Child = titleGrid;

            // Body
            var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(new TextBlock
            {
                Text         = $"\"{System.IO.Path.GetFileName(filename)}\" is password protected.",
                Foreground   = fgPri,
                FontFamily   = fontUI,
                FontSize     = (double)Application.Current.FindResource("FsBody"),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 10)
            });
            var pwBox = new PasswordBox
            {
                Margin           = new Thickness(0, 0, 0, 14),
                Background       = bgCtrl,
                Foreground       = fgPri,
                BorderBrush      = bdrDim,
                CaretBrush       = accent,
                Padding          = new Thickness(8, 6, 8, 6)
            };
            sp.Children.Add(pwBox);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button
            {
                Content = "Cancel",
                Style   = (Style)Application.Current.FindResource("StudioToolButton"),
                Width   = 80,
                Margin  = new Thickness(0, 0, 8, 0)
            };
            var okBtn = new Button
            {
                Content = "Open",
                Style   = (Style)Application.Current.FindResource("StudioPrimaryButton"),
                Width   = 80
            };
            okBtn.Click     += (s, ev) => { result = pwBox.Password; win.DialogResult = true; };
            cancelBtn.Click += (s, ev) => { win.DialogResult = false; };
            pwBox.KeyDown   += (s, ev) => { if (ev.Key == Key.Enter) { result = pwBox.Password; win.DialogResult = true; } };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(okBtn);
            sp.Children.Add(btnRow);

            var root = new StackPanel();
            root.Children.Add(titleBar);
            root.Children.Add(sp);
            outerBorder.Child = root;
            win.Content = outerBorder;
            return win.ShowDialog() == true ? result : null;
        }

        // Cancels the previous thumbnail background load when the file changes.
        private System.Threading.CancellationTokenSource? _thumbCts;

        private void RefreshPageList()
        {
            // Cancel any in-flight thumbnail load for the previous file.
            _thumbCts?.Cancel();
            _thumbCts = new System.Threading.CancellationTokenSource();
            var ct = _thumbCts.Token;

            if (_doc is null || _currentFile is null)
            {
                PageList.ItemsSource = null;
                return;
            }

            int    pageCount = _doc.PageCount;
            string filePath  = _currentFile;

            // Snapshot rotations on the UI thread before going to background.
            var rotSnap = new Dictionary<int, int>(_pageRotations);

            // Carry forward any existing thumbnails so the list never flashes blank
            // during reload (e.g. after a rotation).  New thumbnails replace them as
            // the background loader finishes each page.
            var oldItems = PageList.ItemsSource is PageThumbnailVm[] oi ? oi : null;

            var items = new PageThumbnailVm[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                rotSnap.TryGetValue(i, out int rot);
                items[i] = new PageThumbnailVm(i, filePath, rot);
                // Seed with stale thumbnail — better than blank while reloading
                if (oldItems != null && i < oldItems.Length)
                {
                    var prev = oldItems[i].Thumbnail;
                    if (prev != null) items[i].SetThumbnailDirect(prev);
                }
            }
            PageList.ItemsSource = items;

            // Load thumbnails sequentially on a background thread via a single doc reader.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(128, 256));
                    for (int i = 0; i < pageCount; i++)
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            using var pr  = docReader.GetPageReader(i);
                            int tw  = pr.GetPageWidth();
                            int th  = pr.GetPageHeight();
                            var raw = pr.GetImage();
                            if (tw <= 0 || th <= 0 || raw == null || raw.Length < tw * th * 4)
                                continue;
                            rotSnap.TryGetValue(i, out int rot);
                            if (rot != 0)
                                (raw, tw, th) = RotateBitmap(raw, tw, th, rot);
                            var src = PageThumbnailVm.BuildThumbFromRaw(raw, tw, th);
                            if (src != null && !ct.IsCancellationRequested)
                                items[i].SetThumbnail(src);
                        }
                        catch { /* skip failed thumbnail; item shows label-only */ }
                    }
                }
                catch { /* docReader open failed; all items remain label-only */ }
            }, ct);
        }

        private void RenderPage(int pageIndex)
        {
            if (_currentFile is null || _doc is null) return;
            try
            {
                // Scale render resolution to match display DPI AND current zoom so the
                // bitmap stays sharp when zoomed in.  Base 2048 means Fit Width on a
                // wide monitor stays crisp; zoom factor ensures 1:1 pixels at 2× zoom.
                // Capped at 6144 to keep memory manageable.
                var dpiInfo = VisualTreeHelper.GetDpi(this);
                double dpiScaleX = dpiInfo.DpiScaleX;
                double dpiScaleY = dpiInfo.DpiScaleY;
                int scaledMax = (int)Math.Min(6144,
                    2048 * Math.Max(dpiScaleX, dpiScaleY) * Math.Max(1.0, _zoomLevel));
                _lastRenderZoom = _zoomLevel;

                using var docReader = DocLib.Instance.GetDocReader(_currentFile, new PageDimensions(scaledMax, scaledMax));
                using var pageReader = docReader.GetPageReader(pageIndex);

                int width = pageReader.GetPageWidth();
                int height = pageReader.GetPageHeight();
                var rawBytes = pageReader.GetImage();

                // Apply rotation: the temp file has /Rotate stripped so Docnet renders
                // unrotated (no clipping); rotate the pixel buffer to match the visual.
                if (_pageRotations.TryGetValue(pageIndex, out int pgRot) && pgRot != 0)
                    (rawBytes, width, height) = RotateBitmap(rawBytes, width, height, pgRot);

                if (width <= 0 || height <= 0 || rawBytes == null || rawBytes.Length == 0)
                {
                    PageImage.Source = null;
                    SetStatus(string.Format(Loc("Str_PageRenderError"), pageIndex + 1));
                    return;
                }

                // Convert pixel dimensions to WPF DIPs so the annotation canvas and
                // link overlays are sized in the same coordinate space that WPF uses for
                // layout.  Divide by the zoom factor so the canvas size (and therefore the
                // coordinate map used by DrawAnnotationsOnDocument) stays stable across
                // zoom re-renders — the bitmap just gets more pixels per DIP.
                // LayoutTransform handles the visual zoom, not the canvas dimensions.
                double zoomFactor = Math.Max(1.0, _zoomLevel);
                int dipW = (int)Math.Round(width  / dpiScaleX / zoomFactor);
                int dipH = (int)Math.Round(height / dpiScaleY / zoomFactor);
                _renderDims[pageIndex] = (dipW, dipH);

                // Scale bitmap DPI up so the extra pixels display within the same DIP area.
                double bitmapDpiX = 96.0 * width  / dipW;
                double bitmapDpiY = 96.0 * height / dipH;
                var bitmap = new WriteableBitmap(width, height, bitmapDpiX, bitmapDpiY, PixelFormats.Bgra32, null);
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), rawBytes, width * 4, 0);

                PageImage.Source = bitmap;
                _annotationCanvas.Width  = dipW;
                _annotationCanvas.Height = dipH;
                _annotationCanvas.Tag    = pageIndex;   // so clicks on the primary page resolve to the
                                                        // page actually shown (page 0 in grid), not the
                                                        // selected index - otherwise annotations on it
                                                        // are unhittable and clicks "do nothing".
                ClearSelection();
                ClearSecondaryPages();
                RenderAllAnnotations(pageIndex);
                SetStatus(string.Format(Loc("Str_PageOf"), pageIndex + 1, _doc!.PageCount));
                // Defer additional pages until layout has settled so ActualWidth is valid.
                // RenderPageLinks runs AFTER RenderAdditionalPages so ClearSecondaryPages
                // inside RenderAdditionalPages doesn't wipe the overlays we just added.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    RenderAdditionalPages(pageIndex);
                    RenderPageLinks(pageIndex, dipW, dipH);
                });
            }
            catch (Exception ex)
            {
                PageImage.Source = null;
                SetStatus(string.Format(Loc("Str_RenderError"), ex.Message));
            }
        }

        /// <summary>
        /// Clears all dynamically-added secondary page borders from the panel,
        /// leaving only the first child (the primary page border).
        /// </summary>
        private void ClearSecondaryPages()
        {
            if (_pageContentPanel is null) return;
            // Explicitly null out Image sources before removing so the GC can
            // reclaim the WriteableBitmap backing arrays promptly.
            while (_pageContentPanel.Children.Count > 1)
            {
                var child = _pageContentPanel.Children[^1];
                if (child is Border b && b.Child is Grid g)
                {
                    foreach (var gc in g.Children)
                        if (gc is Image img) img.Source = null;
                }
                _pageContentPanel.Children.RemoveAt(_pageContentPanel.Children.Count - 1);
            }
            // NOTE: do NOT reset _pageContentPanel.Width here.  Width is managed exclusively
            // by RenderAdditionalPages (which runs only via Dispatcher) so that no synchronous
            // call to ClearSecondaryPages triggers an intermediate layout pass that would cause
            // the primary page to flash centered and then jerk back to left-aligned.
            // Clear any link overlays from the annotation canvas.
            foreach (var lo in _linkOverlays)
                _annotationCanvas.Children.Remove(lo);
            _linkOverlays.Clear();
        }

        /// <summary>
        /// Renders secondary pages as a grid. Panel-width setup is synchronous so layout
        /// is correct immediately; Docnet pixel rendering runs on a background thread so
        /// the UI stays responsive. WPF element creation returns to the UI thread.
        /// </summary>
        private async void RenderAdditionalPages(int primaryPageIdx)
        {
            if (_currentFile is null || _doc is null) return;
            // Grid is a stable overview anchored at page 0 (independent of the selected page), so it
            // always shows the whole document instead of only the selected page onward.
            if (_viewMode == ViewMode.Grid) primaryPageIdx = 0;
            ClearSecondaryPages();

            double viewportW = PagePreviewPanel.ActualWidth;
            if (viewportW <= 0 || _doc.PageCount <= 1)
            {
                _pageContentPanel.Width = double.NaN;
                return;
            }

            // Snap the WrapPanel width to a whole number of page-width slots.
            double primaryPageW = _annotationCanvas.Width > 0 ? _annotationCanvas.Width : 595;
            double pageSlotW = primaryPageW + 12;
            double availablePreZoom = (viewportW - 24) / _zoomLevel;
            int pagesPerRow = _viewMode == ViewMode.TwoPage ? 2 : Math.Max(1, (int)(availablePreZoom / pageSlotW));
            double panelW = pagesPerRow * pageSlotW;
            if (panelW > 0) _pageContentPanel.Width = panelW;

            // Cancel any previously running secondary render.
            _secondaryRenderCts?.Cancel();
            _secondaryRenderCts = new System.Threading.CancellationTokenSource();
            var cts = _secondaryRenderCts;

            // Secondary pages: fixed 1536 px cap regardless of DPI/zoom.
            const int SecondaryMax = 1536;
            // Grid shows the whole document; Two-Page shows one secondary; other modes peek ahead.
            int limit = _viewMode == ViewMode.Grid
                ? _doc.PageCount
                : Math.Min(_doc.PageCount, primaryPageIdx + 1 + (_viewMode == ViewMode.TwoPage ? 1 : 25));
            if (limit <= primaryPageIdx + 1) return;

            string currentFile = _currentFile;

            // Collect rotations on the UI thread before the background task.
            var secRotations = new Dictionary<int, int>();
            for (int i = primaryPageIdx + 1; i < limit; i++)
                if (_pageRotations.TryGetValue(i, out int r) && r != 0)
                    secRotations[i] = r;

            // Capture the primary page width and reset the tile map on the UI thread before
            // streaming tiles in from the background render.
            double primaryDipW = _annotationCanvas.Width > 0 ? _annotationCanvas.Width : 595;
            _continuousCanvases.Clear();

            // Render pixels on a background thread and attach each page tile to the UI as soon
            // as it is ready, so large documents fill in progressively instead of blocking
            // until every page has been rendered.
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    using var docReader = DocLib.Instance.GetDocReader(currentFile, new PageDimensions(SecondaryMax, SecondaryMax));
                    for (int i = primaryPageIdx + 1; i < limit; i++)
                    {
                        if (cts.IsCancellationRequested) break;
                        using var pageReader = docReader.GetPageReader(i);
                        int w = pageReader.GetPageWidth();
                        int h = pageReader.GetPageHeight();
                        var rawBytes = pageReader.GetImage();
                        if (w <= 0 || h <= 0 || rawBytes is null) continue;
                        if (secRotations.TryGetValue(i, out int rot))
                            (rawBytes, w, h) = RotateBitmap(rawBytes, w, h, rot);

                        int pi = i, pw = w, ph = h;
                        byte[] bytes = rawBytes;
                        Dispatcher.Invoke(() =>
                        {
                            if (cts.IsCancellationRequested || _doc is null) return;
                            if (_viewMode != ViewMode.Grid && _viewMode != ViewMode.TwoPage) return;
                            AddSecondaryTile(pi, pw, ph, bytes, primaryDipW);
                        });
                    }
                }, cts.Token);
            }
            catch { return; }
        }

        /// <summary>
        /// Builds one secondary-page tile (image + annotation overlay + links) and appends it
        /// to the page content panel. Must run on the UI thread.
        /// </summary>
        private void AddSecondaryTile(int pi, int w, int h, byte[] rawBytes, double primaryDipW)
        {
            int pageDipW = (int)Math.Round(primaryDipW);
            int pageDipH = (int)Math.Round(primaryDipW * h / w);
            double bitmapDpiX = 96.0 * w / pageDipW;
            double bitmapDpiY = 96.0 * h / pageDipH;

            // Do NOT overwrite _renderDims if the page was already rendered as primary -
            // its annotation coordinate mapping must stay intact.
            if (!_renderDims.ContainsKey(pi))
                _renderDims[pi] = (pageDipW, pageDipH);

            var bitmap = new WriteableBitmap(w, h, bitmapDpiX, bitmapDpiY, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, w, h), rawBytes, w * 4, 0);

            var img = new Image { Source = bitmap, Stretch = Stretch.None };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var overlay = new Canvas
            {
                Width = pageDipW, Height = pageDipH,
                Background = Brushes.Transparent,
                Cursor = CursorForTool(_currentTool),
                Tag = pi,
                ToolTip = $"Page {pi + 1}"
            };
            int capturedPi = pi;
            overlay.PreviewMouseLeftButtonDown += (s, ev) =>
            {
                if (_currentTool == EditTool.Select)
                {
                    var hit = ev.GetPosition((Canvas)s);
                    bool onAnnot = (_annotations.TryGetValue(capturedPi, out var list)
                                    && list.Any(a => HitTestAnnotation(a, hit, out _)))
                                   || _selectedAnnotation?.PageIndex == capturedPi;
                    if (onAnnot) Canvas_MouseLeftButtonDown(s, ev);
                    else PageList.SelectedIndex = capturedPi;
                }
                else Canvas_MouseLeftButtonDown(s, ev);
            };
            overlay.MouseMove                += Canvas_MouseMove;
            overlay.PreviewMouseLeftButtonUp += Canvas_MouseLeftButtonUp;
            _continuousCanvases[pi] = overlay;

            var pageGrid = new Grid();
            pageGrid.Children.Add(img);
            pageGrid.Children.Add(overlay);
            AddSecondaryPageLinks(pi, pageGrid, pageDipW, pageDipH);

            _pageContentPanel.Children.Add(new Border
            {
                Background = Brushes.White,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 12),
                Child = pageGrid
            });
            RenderAllAnnotations(pi);
        }

        /// <summary>Look up a localized string. Falls back to the key name if missing.</summary>
        private string Loc(string key)
            => Application.Current.TryFindResource(key) as string ?? key;

                private void SetStatus(string text)
        {
            StatusText.Text = text;
            CrashReporter.PushStatusMessage(text);
        }

        private System.Windows.Threading.DispatcherTimer? _toastTimer;

        /// <summary>Shows a transient toast banner; auto-dismisses after ~4s. Never throws.</summary>
        private void ShowToast(string message, string? copyText = null)
        {
            try
            {
                ToastHost.BeginAnimation(UIElement.OpacityProperty, null); // clear any HoldEnd animation from HideToast
                ToastText.Text = message;
                ToastCopyBtn.Visibility = string.IsNullOrEmpty(copyText) ? Visibility.Collapsed : Visibility.Visible;
                ToastCopyBtn.Tag = copyText;
                ToastHost.Opacity = 1;
                ToastHost.Visibility = Visibility.Visible;

                _toastTimer?.Stop();
                _toastTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(4)
                };
                _toastTimer.Tick += (_, __) => { _toastTimer?.Stop(); HideToast(); };
                _toastTimer.Start();
            }
            catch { /* a missing toast must never break editing */ }
        }

        private void HideToast()
        {
            try
            {
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, __) => ToastHost.Visibility = Visibility.Collapsed;
                ToastHost.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch { ToastHost.Visibility = Visibility.Collapsed; }
        }

        private void ToastCopyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ToastCopyBtn.Tag is string s && !string.IsNullOrEmpty(s))
                    Clipboard.SetText(s);
            }
            catch { }
        }

        private static IReadOnlyCollection<string>? _availableFamiliesCache;
        /// <summary>System + bundled font families, cached; used for FontResolver availability checks.</summary>
        private static FontWeight ToWeight(bool bold) => bold ? FontWeights.Bold : FontWeights.Normal;
        private static FontStyle ToStyle(bool italic) => italic ? FontStyles.Italic : FontStyles.Normal;

        private static IReadOnlyCollection<string> AvailableFontFamilies()
        {
            if (_availableFamiliesCache is not null) return _availableFamiliesCache;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies)
                {
                    if (!string.IsNullOrWhiteSpace(ff.Source)) set.Add(ff.Source);
                    foreach (var n in ff.FamilyNames.Values) set.Add(n);
                }
            }
            catch { /* minimal fallback below */ }
            set.Add("Segoe UI");
            _availableFamiliesCache = set;
            return set;
        }

        private void VersionLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowAboutOverlay();
        }

        /// <summary>
        /// Re-renders secondary pages and then link overlays for the current page.
        /// Must be called via Dispatcher so layout is settled before RenderAdditionalPages
        /// reads ActualWidth. All zoom-change and sidebar-toggle dispatch sites use this
        /// instead of a bare RenderAdditionalPages call so link overlays are never left
        /// cleared without being re-added.
        /// </summary>
        private void RefreshPageView(int pageIndex)
        {
            if (_viewMode == ViewMode.Continuous)
                return; // continuous mode manages its own rendering

            // Grid fits its columns to the viewport, so it never needs a horizontal scrollbar.
            // Leaving it on Auto shows a stray (green) thumb across the bottom when the tile panel
            // overflows by the vertical scrollbar's width. Disable it for grid, Auto elsewhere.
            PagePreviewPanel.HorizontalScrollBarVisibility =
                _viewMode == ViewMode.Grid ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            // Single page is centered; drop the right/bottom tile-gap margin that grid/two-page
            // need for spacing (it would otherwise push the lone page a few px left of center).
            if (_pageContentPanel is not null && _pageContentPanel.Children.Count > 0
                && _pageContentPanel.Children[0] is Border primaryBorder)
                primaryBorder.Margin = _viewMode == ViewMode.Single
                    ? new Thickness(0) : new Thickness(0, 0, 12, 12);
            if (_viewMode == ViewMode.Grid || _viewMode == ViewMode.TwoPage)
                RenderAdditionalPages(pageIndex);
            else
            {
                ClearSecondaryPages();
                if (_pageContentPanel is not null)
                    _pageContentPanel.Width = double.NaN;
            }
            if (_renderDims.TryGetValue(pageIndex, out var dims))
                RenderPageLinks(pageIndex, dims.w, dims.h);
        }



    }
}
