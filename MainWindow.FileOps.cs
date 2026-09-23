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

        /// <summary>
        /// The path chosen in a Save dialog, with the expected extension guaranteed.
        /// <para>A Save dialog only appends its default extension when the typed name has none at
        /// all, so "report.final" was written as a file Windows then treats as a .final document -
        /// it will not reopen in Scalpel and does not show a PDF icon. When
        /// <paramref name="requireExtension"/> is set, anything not already ending in the wanted
        /// extension gets it appended.</para>
        /// </summary>
        private static string ChosenPath(Microsoft.Win32.FileDialog dlg, string extension,
                                         bool requireExtension = true)
            => Scalpel.Services.SaveFileNamePolicy.ApplyExtension(
                   dlg.FileName, extension, addExtension: true, requireExtension: requireExtension);

        private void OpenFile(string path)
        {
            // Any render still streaming tiles for the document being replaced must stop before
            // the new one loads, or its pages arrive late and paint into the wrong document.
            try
            {
                _continuousRenderCts?.Cancel();
                _secondaryRenderCts?.Cancel();
            }
            catch { }

            // OpenFile always writes into the ACTIVE session `_s`. OpenInTab makes that a fresh
            // (or the empty placeholder) session first, so `_doc` here is null and nothing another
            // tab owns is ever touched; OpenInTab also defers any open that arrives while this one
            // is still running (e.g. inside a password prompt), so `_s` cannot change under us.
            // The only caller that opens into a session that already holds a document is the
            // SaveInPlace recovery reload, which is replacing that session's own copy.
            DiscardAttempt();

            // Files on UNC / network shares - notably the WSL \\wsl$ 9P filesystem - can hand
            // back partial reads, making the PDF parser see a truncated file ("Unexpected EOF").
            // Copy such files to a local temp via File.ReadAllBytes (which reads to EOF) and open
            // from there. `path` stays the user's real path for display and Save.
            string srcPath = path;
            if (IsNetworkPath(path))
            {
                try
                {
                    var localCopy = App.MakeTempFile("netopen", _s.Id);
                    File.WriteAllBytes(localCopy, File.ReadAllBytes(path));
                    srcPath = localCopy;
                }
                catch { srcPath = path; }
            }

            try
            {
                _doc = PdfReader.Open(srcPath, PdfDocumentOpenMode.Modify);
                // PdfSharp cannot save modified encrypted PDFs — it copies unmodified encrypted
                // stream bytes verbatim but fails when it has to re-serialize a dirty object.
                // Strip encryption silently at open time via Import so all edits work correctly.
                if (PdfFileHasEncryption(srcPath))
                {
                    // PdfSharp can read encrypted PDFs but cannot re-save them once modified.
                    // Strip encryption now via PDFium (lossless), falling back to Import mode.
                    _doc.Close(); _doc = null;
                    var repairedPath = App.MakeTempFile("repaired", _s.Id);
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
                    DiscardAttempt();
                    _doc = PdfReader.Open(srcPath, PdfDocumentOpenMode.ReadOnly);
                    _currentFile = srcPath;
                    FinishOpenFile(path, srcPath);
                    _openedProtected = true;
                    SetStatus(string.Format(Loc("Str_OpenedReadOnly"), System.IO.Path.GetFileName(path), _doc.PageCount));
                }
                catch (Exception ex2)
                {
                    ReportOpenFailure(path, ex2, ex);
                }
            }
            catch (Exception ex) when (IsPasswordException(ex))
            {
                string? pw = PromptForPassword(path);
                if (pw is null) return;
                try
                {
                    DiscardAttempt();
                    _doc = PdfReader.Open(srcPath, pw, PdfDocumentOpenMode.Modify);
                    // Save a decrypted temp copy so Docnet can render without needing the password
                    var tempDec = App.MakeTempFile("dec", _s.Id);
                    Scalpel.Services.PdfSaveGuard.Save(_doc, tempDec);
                    _doc.Close();
                    _doc = PdfReader.Open(tempDec, PdfDocumentOpenMode.Modify);
                    _currentFile = tempDec;
                    FinishOpenFile(path, tempDec);
                    // Scalpel decrypts to a working copy at open time, so every later save writes
                    // an unprotected PDF. Remember that so the user can be told, and so "Remove
                    // password" can present it as a deliberate action.
                    _openedProtected = true;
                }
                catch (Exception ex2)
                {
                    ReportOpenFailure(path, ex2, ex);
                }
            }
            catch (Exception ex) when (IsXRefException(ex))
            {
                // First: PdfSharpCore may just be rejecting a file it (or an older Scalpel)
                // wrote itself - a dangling empty /Outlines xref entry (Services/PdfSaveGuard.cs).
                // PDFium reads those fine, so re-save losslessly through it and open the copy for
                // full editing; _originalFile stays the user's path so Save writes back there.
                try
                {
                    DiscardAttempt();
                    var recovered = App.MakeTempFile("recovered", _s.Id);
                    if (TryPdfiumStripEncryption(srcPath, recovered))
                    {
                        _doc = PdfReader.Open(recovered, PdfDocumentOpenMode.Modify);
                        _currentFile = recovered;
                        FinishOpenFile(path, recovered);
                        Scalpel.Services.Logger.Info("File", "open.recovered", "Structure rejected by PdfSharpCore; opened via PDFium re-save",
                            new { path, error = ex.Message });
                        SetStatus($"Opened {System.IO.Path.GetFileName(path)} ({_doc.PageCount} pages) - recovered via PDFium.");
                        return;
                    }
                }
                catch (Exception rex)
                {
                    Scalpel.Services.Logger.Warn("File", "open.recover.fail", rex.Message, new { path });
                    DiscardAttempt();
                }

                // Some PDFs have malformed or non-standard XRef tables that PdfSharp can't
                // open in Modify mode. Fall back to ReadOnly; if that also fails, offer repair.
                try
                {
                    DiscardAttempt();
                    _doc = PdfReader.Open(srcPath, PdfDocumentOpenMode.ReadOnly);
                    _currentFile = srcPath;
                    FinishOpenFile(path, srcPath);
                    SetStatus(string.Format(Loc("Str_OpenedReadOnlyXRef"), System.IO.Path.GetFileName(path), _doc.PageCount));
                    ScalpelDialog.Show(this,
                        $"\"{System.IO.Path.GetFileName(path)}\" has a non-standard structure and was opened read-only.\n\nEditing, saving, and some other features may not work correctly.",
                        "Scalpel", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex2)
                {
                    // ReadOnly also failed — offer to repair.
                    Scalpel.Services.Logger.Error("File", "open.fail", "Damaged structure; offering repair", ex2, new
                    {
                        path,
                        trigger = new { type = ex.GetType().Name, message = ex.Message },
                    });
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
                    DiscardAttempt();
                    var repairedPath = App.MakeTempFile("repaired", _s.Id);
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
                    ReportOpenFailure(path, ex2, ex);
                }
            }
            catch (Exception ex)
            {
                ReportOpenFailure(path, ex);
            }
        }

        /// <summary>
        /// Disposes the document of the session being opened into: the previous copy of the same
        /// session (a reload) or a failed attempt from an earlier step of the fallback chain.
        /// Never another tab's document - see the note at the top of OpenFile.
        /// </summary>
        private void DiscardAttempt()
        {
            if (_doc is null) return;
            try { _doc.Close(); } catch { }
            _doc = null;
        }

        /// <summary>
        /// Log a failed open (with the exception that ended the fallback chain and, when the
        /// failure happened inside a fallback, the original exception that triggered it) and
        /// show the standard "could not open" dialog.
        /// </summary>
        private void ReportOpenFailure(string path, Exception ex, Exception? trigger = null)
        {
            DiscardAttempt();   // a half-finished attempt must not be left as the tab's document
            Scalpel.Services.Logger.Error("File", "open.fail", "Could not open PDF", ex, new
            {
                path,
                trigger = trigger is null ? null : new { type = trigger.GetType().Name, message = trigger.Message },
            });
            // The session has no document now, so it must not keep the file's identity either
            // (it would block reopening the file as "already open").
            ForgetPaths(_s);
            ScalpelDialog.Show(this, string.Format(Loc("Str_Dlg_FailedOpen"), ex.Message), Loc("Str_Dlg_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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

        private static bool IsXRefException(Exception ex) => Scalpel.Services.PdfReopen.IsXRefException(ex);

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
                // Warm PDFium on its own thread, so its one-time init never happens
                // on whichever thread happens to render first.
                try { Scalpel.Services.PdfiumGate.Run(() => { _ = DocLib.Instance; }); } catch { }

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
                    Scalpel.Services.Logger.Warn("File", "repair.pdfium.fail", "FPDF_LoadDocument returned null", new { source = sourcePath });
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
                Scalpel.Services.Logger.Warn("File", "repair.pdfium.fail", "TryPdfiumSaveWithZeroRotations failed: " + ex.Message, new
                {
                    source = sourcePath,
                    error  = new { type = ex.GetType().FullName, message = ex.Message, stack = ex.StackTrace },
                });
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
                Scalpel.Services.PdfSaveGuard.Save(cleanDoc, destPath);
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
                DiscardAttempt();
                PdfDocument repairedDoc;
                using (var importDoc = PdfReader.Open(path, PdfDocumentOpenMode.Import))
                {
                    repairedDoc = new PdfDocument();
                    for (int i = 0; i < importDoc.PageCount; i++)
                        repairedDoc.Pages.Add(importDoc.Pages[i]);
                }
                var repairedPath = App.MakeTempFile("repaired", _s.Id);
                Scalpel.Services.PdfSaveGuard.Save(repairedDoc, repairedPath);
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

            DiscardAttempt();
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

            // PDFium runs on its own thread; the PDF assembly below stays on this one.
            var docReader = Scalpel.Services.PdfiumGate.Run(
                () => Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(RenderPx, RenderPx)));
            try
            {
            int pageCount = Scalpel.Services.PdfiumGate.Run(() => docReader.GetPageCount());
            if (pageCount <= 0) throw new InvalidOperationException("PDFium could not read any pages.");

            var newDoc = new PdfDocument();

            for (int i = 0; i < pageCount; i++)
            {
                int page_i = i;
                var (raw, bw, bh) = Scalpel.Services.PdfiumGate.Run(() =>
                {
                    using var pr = docReader.GetPageReader(page_i);
                    return (pr.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(),
                                        Scalpel.Services.AnnotationRenderPolicy.ForOutput()),
                            pr.GetPageWidth(), pr.GetPageHeight());
                });
                if (bw <= 0 || bh <= 0) continue;
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

            var repairedPath = App.MakeTempFile("repaired", _s.Id);
            Scalpel.Services.PdfSaveGuard.Save(newDoc, repairedPath);
            newDoc.Close();

            DiscardAttempt();
            _doc = PdfReader.Open(repairedPath, PdfDocumentOpenMode.Modify);
            _currentFile = repairedPath;
            FinishOpenFile(path, repairedPath);
            MarkDirty(true); // repaired copy lives in temp — user must Save As
            SetStatus(string.Format(Loc("Str_OpenedRasterRepair"), System.IO.Path.GetFileName(path), _doc.PageCount));
            }
            finally { Scalpel.Services.PdfiumGate.Run(() => docReader.Dispose()); }
            ScalpelDialog.Show(this,
                $"\"{System.IO.Path.GetFileName(path)}\" was repaired by rasterizing through PDFium.\n\nText is not selectable in the repaired copy. Use Save As to write it to a new location.",
                "Scalpel", MessageBoxButton.OK, MessageBoxImage.None);
        }

        private static bool IsOwnerPasswordException(Exception ex) =>
            ex.Message.IndexOf("owner", StringComparison.OrdinalIgnoreCase) >= 0 &&
            ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <param name="displayPath">The user's real file, or null for a new untitled document
        /// (shown as "Untitled.pdf"; Save then goes through Save As).</param>
        private void FinishOpenFile(string? displayPath, string workingPath)
        {
            try { if (displayPath is not null && System.IO.File.Exists(displayPath)) App.AddRecentFile(displayPath); } catch { }
            _currentFile = workingPath;
            _originalFile = displayPath;
            // Model first (no UI), then the UI is bound to the session. A tab switch reuses the
            // bind half without the reset (MainWindow.Tabs.cs).
            ResetSessionContent(_s);
            _docHasFormFields = DocumentHasFormFields();
            BindSessionToUi(_s, newContent: true);
            Scalpel.Services.Logger.Info("File", "open.success", "PDF opened", new { path = displayPath ?? _s.DisplayName, pages = _doc!.PageCount });
            RefreshTabStrip();   // the tab now shows this document's name
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
                Title = Loc("Str_Pwd_Title"),
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
                Text       = Loc("Str_Pwd_Title"),
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

        // _thumbCts (the in-flight thumbnail load) lives on the session: MainWindow.DocumentSession.cs.

        private void RefreshPageList()
        {
            // Cancel any in-flight thumbnail load for the previous file.
            _thumbCts?.Cancel();
            _thumbCts = new System.Threading.CancellationTokenSource();
            var ct = _thumbCts.Token;

            if (_doc is null || _currentFile is null)
            {
                PageList.ItemsSource = null;
                _s.Thumbs = null;
                _s.ThumbsForPath = null;
                return;
            }

            int    pageCount = _doc.PageCount;
            string filePath  = _currentFile;

            // Snapshot rotations on the UI thread before going to background.
            var rotSnap = new Dictionary<int, int>(_pageRotations);

            // Carry forward any existing thumbnails so the list never flashes blank
            // during reload (e.g. after a rotation).  New thumbnails replace them as
            // the background loader finishes each page.
            //
            // Only THIS session's own thumbnails may seed the list. They used to be read back from
            // PageList.ItemsSource, which after opening another file still held the previous
            // document's pages, so the sidebar briefly showed the wrong document. The session's
            // Thumbs are cleared by ResetSessionContent whenever its content is replaced, so a
            // non-null ThumbsForPath means "same document, reloaded" (a temp reload after a rotate
            // or crop moves to a new working file, which is why this is not a path comparison).
            bool sameSource = _s.Thumbs is not null && _s.ThumbsForPath is not null;
            var oldItems = sameSource ? _s.Thumbs : null;

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
            _s.Thumbs = items;
            _s.ThumbsForPath = _currentFile;

            // Load thumbnails sequentially on a background thread via a single doc reader.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // PDFium runs on its own thread; only the native calls are marshalled.
                    var docReader = Scalpel.Services.PdfiumGate.Run(
                        () => Scalpel.Services.PinnedDocReader.Open(filePath, new PageDimensions(128, 256)));
                    try
                    {
                    for (int i = 0; i < pageCount; i++)
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            int page = i;
                            var (raw, tw, th) = Scalpel.Services.PdfiumGate.Run(() =>
                            {
                                using var pr = docReader.GetPageReader(page);
                                return (pr.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(),
                                                    Scalpel.Services.AnnotationRenderPolicy.ForOutput()),
                                        pr.GetPageWidth(), pr.GetPageHeight());
                            });
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
                    finally { Scalpel.Services.PdfiumGate.Run(() => docReader.Dispose()); }
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
                int scaledMax = Scalpel.Services.ViewerRenderResolution.Primary(
                    dpiScaleX, dpiScaleY, _zoomLevel);
                _lastRenderZoom = _zoomLevel;

                // PDFium runs on its own thread. This whole read is one short marshalled call.
                string renderFile = _currentFile;
                bool hasForms = _docHasFormFields;
                var (rawBytes, width, height) = Scalpel.Services.PdfiumGate.Run(() =>
                {
                    using var docReader = Scalpel.Services.PinnedDocReader.Open(renderFile, new PageDimensions(scaledMax, scaledMax));
                    using var pageReader = docReader.GetPageReader(pageIndex);
                    return (pageReader.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(),
                                                Scalpel.Services.AnnotationRenderPolicy.ForViewer(hasForms)),
                            pageReader.GetPageWidth(), pageReader.GetPageHeight());
                });

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
                int gen = _sessionGeneration;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    if (IsStale(gen)) return;   // another document is shown now
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
            // In two-page view the facing page is as prominent as the primary one, so it gets
            // the same pixel budget instead of the smaller neighbour budget - that mismatch is
            // why the right-hand page looked soft next to the left one.
            var secondaryDpi = VisualTreeHelper.GetDpi(this);
            int SecondaryMax = Scalpel.Services.ViewerRenderResolution.Secondary(
                _viewMode == ViewMode.TwoPage, secondaryDpi.DpiScaleX, secondaryDpi.DpiScaleY, _zoomLevel);
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
                    // Only the native calls go to the PDFium thread. The Dispatcher.Invoke below
                    // stays on this task's thread - running it on the PDFium thread would block
                    // that thread on the UI while the UI could be waiting for PDFium work of its
                    // own, which is a deadlock.
                    bool hasForms = _docHasFormFields;
                    var docReader = Scalpel.Services.PdfiumGate.Run(
                        () => Scalpel.Services.PinnedDocReader.Open(currentFile, new PageDimensions(SecondaryMax, SecondaryMax)));
                    try
                    {
                    for (int i = primaryPageIdx + 1; i < limit; i++)
                    {
                        if (cts.IsCancellationRequested) break;
                        int page = i;
                        var (rawBytes, w, h) = Scalpel.Services.PdfiumGate.Run(() =>
                        {
                            using var pageReader = docReader.GetPageReader(page);
                            return (pageReader.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(),
                                                        Scalpel.Services.AnnotationRenderPolicy.ForViewer(hasForms)),
                                    pageReader.GetPageWidth(), pageReader.GetPageHeight());
                        });
                        if (w <= 0 || h <= 0 || rawBytes is null) continue;
                        if (secRotations.TryGetValue(i, out int rot))
                            (rawBytes, w, h) = RotateBitmap(rawBytes, w, h, rot);

                        int pi = i, pw = w, ph = h;
                        byte[] bytes = rawBytes;
                        Dispatcher.Invoke(() =>
                        {
                            if (cts.IsCancellationRequested || _doc is null) return;
                            if (_viewMode != ViewMode.Grid && _viewMode != ViewMode.TwoPage) return;
                            // A render started for the previous document must never paint into
                            // the one that has since been opened in its place.
                            if (!PathEq(currentFile, _currentFile)) return;
                            AddSecondaryTile(pi, pw, ph, bytes, primaryDipW);
                        });
                    }
                    }
                    finally { Scalpel.Services.PdfiumGate.Run(() => docReader.Dispose()); }
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
                ToolTip = string.Format(Loc("Str_PageN"), pi + 1)
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

        /// <summary>
        /// True when the document declares at least one AcroForm field. Read defensively: a
        /// malformed form dictionary must not stop the document opening.
        /// </summary>
        private bool DocumentHasFormFields() => DocumentHasFormFieldsIn(_doc);

        /// <summary>
        /// True when <paramref name="doc"/> declares at least one AcroForm field. Read defensively:
        /// a malformed form dictionary must not stop the document opening. Takes the document
        /// explicitly (rather than the <c>_doc</c> shim) so a long operation adopting its result
        /// into a pinned session - not necessarily the active one - checks the right document.
        /// </summary>
        private static bool DocumentHasFormFieldsIn(PdfDocument? doc)
        {
            try
            {
                var acro = doc?.Internals.Catalog.Elements.GetDictionary("/AcroForm");
                var fields = acro?.Elements.GetArray("/Fields");
                return fields is not null && fields.Elements.Count > 0;
            }
            catch { return false; }
        }

        /// <summary>Look up a localized string. Falls back to the key name if missing.</summary>
        private string Loc(string key)
            => Application.Current.TryFindResource(key) as string ?? key;

                private void SetStatus(string text)
        {
            StatusText.Text = text;
            CrashReporter.PushStatusMessage(text);
        }

        /// <summary>Clicking the status line shows the open file's size, like Shift+F4.</summary>
        private void StatusText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ShowFileSizeStatus();

        /// <summary>Starts a picker in the folder last used for that kind of file.</summary>
        private static void SeedPickerFolder(Microsoft.Win32.FileDialog dlg, string purpose)
        {
            var dir = Scalpel.Services.LastFolders.Resolve(
                App.GetSetting(Scalpel.Services.LastFolders.SettingName(purpose)));
            if (dir is not null) dlg.InitialDirectory = dir;
        }

        /// <summary>Records the folder a file was just picked from, per kind of picker.</summary>
        private static void RememberPickerFolder(string purpose, string? pickedPath)
        {
            var dir = Scalpel.Services.LastFolders.FromPickedPath(pickedPath);
            if (dir is not null)
                App.SetSetting(Scalpel.Services.LastFolders.SettingName(purpose), dir);
        }

        private System.Windows.Threading.DispatcherTimer? _fileSizeTimer;

        /// <summary>
        /// Shift+F4 (or a click on the status text): shows the open file's size for a few seconds
        /// and then restores whatever the status line was showing before.
        /// </summary>
        private void ShowFileSizeStatus()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentFile) || !System.IO.File.Exists(_currentFile)) return;
                long bytes = new System.IO.FileInfo(_currentFile!).Length;
                string previous = StatusText.Text;
                SetStatus(string.Format(Loc("Str_St_FileSize"), FormatBytes(bytes), bytes.ToString("N0")));

                _fileSizeTimer?.Stop();
                _fileSizeTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = System.TimeSpan.FromSeconds(4)
                };
                _fileSizeTimer.Tick += (_, _) =>
                {
                    _fileSizeTimer?.Stop();
                    _fileSizeTimer = null;
                    // Only restore if nothing else has written a status in the meantime.
                    if (StatusText.Text.StartsWith(FormatBytes(bytes), System.StringComparison.Ordinal))
                        SetStatus(previous);
                };
                _fileSizeTimer.Start();
            }
            catch { /* a status readout must never break anything */ }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
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
