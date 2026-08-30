using System;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Scalpel.Services
{
    /// <summary>
    /// Opening a PDF for modification with recovery from PdfSharpCore's own cross-reference
    /// quirks (WPF-free). PdfSharpCore sometimes writes files it cannot read back (see
    /// <see cref="PdfSaveGuard"/>); PDFium reads them fine, so the recovery is: re-save the file
    /// through a PDFium-backed writer into a temp copy and open that instead.
    /// </summary>
    public static class PdfReopen
    {
        /// <summary>Heuristic: does this exception describe a damaged/non-standard PDF structure?</summary>
        public static bool IsXRefException(Exception ex) =>
            ex.Message.IndexOf("XRef", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("cross-reference", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("trailer", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("Invalid PDF file", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("startxref", StringComparison.OrdinalIgnoreCase) >= 0 ||
            ex.Message.IndexOf("Unexpected token", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Open <paramref name="path"/> in Modify mode. If PdfSharpCore rejects the structure,
        /// call <paramref name="resave"/>(source, dest) to rewrite it into <paramref name="makeTemp"/>()
        /// and open that. <paramref name="openedPath"/> is whichever file was actually opened.
        /// Rethrows the original exception if recovery is impossible.
        /// </summary>
        public static PdfDocument OpenModify(string path, Func<string, string, bool> resave, Func<string> makeTemp, out string openedPath)
        {
            try
            {
                var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
                openedPath = path;
                return doc;
            }
            catch (Exception ex) when (IsXRefException(ex))
            {
                string fixedPath;
                bool ok;
                try
                {
                    fixedPath = makeTemp();
                    ok = resave(path, fixedPath);
                }
                catch { ok = false; fixedPath = ""; }
                if (!ok) throw;
                Logger.Warn("File", "reopen.recovered", "PdfSharpCore rejected its own output; recovered via PDFium re-save",
                    new { path, fixedPath, error = ex.Message });
                var doc = PdfReader.Open(fixedPath, PdfDocumentOpenMode.Modify);
                openedPath = fixedPath;
                return doc;
            }
        }
    }
}
