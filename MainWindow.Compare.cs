using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Compare with another PDF
        // ============================================================

        /// <summary>
        /// Renders this document and another one page by page and reports where they differ.
        /// The question it answers is the one people open two windows side by side for: "is this
        /// the same contract I signed, and if not, which pages changed?"
        /// </summary>
        private async void ToolsCompare_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            var dlg = new OpenFileDialog
            {
                Title = Loc("Str_Cmp_Pick"),
                Filter = "PDF files|*.pdf",
                CheckFileExists = true,
            };
            SeedPickerFolder(dlg, LastFolders.Image);
            // No forwarded open may swap the active tab while the dialog is up: the code below
            // acts on `_s` once it closes (same guard as Save As).
            bool? picked;
            BeginTabOp();
            try { picked = dlg.ShowDialog(this); }
            finally { EndTabOp(); }
            if (picked != true) return;
            string other = dlg.FileName;
            RememberPickerFolder(LastFolders.Image, other);

            // Compare only reads the two documents and reports a text summary; it never adopts a
            // result or changes the dirty flag, so no session needs to be pinned. The gate still
            // has to be taken so the document cannot be closed out from under the render.
            using var op = _longOps.Begin();

            string mine;
            try
            {
                CommitActiveTextBox();
                mine = App.MakeTempFile("cmp", _s.Id);
                WriteFormValuesToDocument();
                PdfSaveGuard.Save(_doc!, mine);
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, string.Format(Loc("Str_Cmp_Failed"), ex.Message),
                                   Loc("Str_Tool_Compare"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SetStatus(Loc("Str_Cmp_Running"));
            string report;
            try
            {
                report = await Task.Run(() => CompareDocuments(mine, other));
            }
            catch (Exception ex)
            {
                Logger.Error("Tools", "compare.fail", ex.Message, ex);
                ScalpelDialog.Show(this, string.Format(Loc("Str_Cmp_Failed"), ex.Message),
                                   Loc("Str_Tool_Compare"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SetStatus(Loc("Str_Cmp_Done"));
            ScalpelDialog.Show(this, report, Loc("Str_Tool_Compare"),
                               MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Compares two files page by page and returns the plain-text report the dialog shows.
        /// Runs entirely off the UI thread; both rasterizers take the PDFium gate in turn.
        /// </summary>
        private string CompareDocuments(string leftPath, string rightPath)
        {
            var sb = new StringBuilder();

            // Both readers are opened at the same pixel budget so equal pages produce equal
            // rasters; a mismatch in size is itself reported as a difference.
            using var left = new DocnetPageRasterizer(leftPath, 1400);
            using var right = new DocnetPageRasterizer(rightPath, 1400);

            int shared = Math.Min(left.PageCount, right.PageCount);
            var changed = new List<(int page, double fraction, int regions)>();

            for (int i = 0; i < shared; i++)
            {
                var a = RenderBgra(left, i);
                var b = RenderBgra(right, i);
                var result = PdfPageDifference.Compare(a.pixels, a.w, a.h, b.pixels, b.w, b.h);
                if (result.IsDifferent)
                    changed.Add((i + 1, result.ChangedFraction, result.Regions.Count));
            }

            sb.AppendLine(string.Format(Loc("Str_Cmp_Header"),
                _originalFile is not null ? System.IO.Path.GetFileName(_originalFile) : _s.DisplayName,
                System.IO.Path.GetFileName(rightPath)));
            sb.AppendLine();

            if (left.PageCount != right.PageCount)
            {
                sb.AppendLine(string.Format(Loc("Str_Cmp_PageCount"), left.PageCount, right.PageCount));
                sb.AppendLine();
            }

            if (changed.Count == 0)
            {
                sb.AppendLine(left.PageCount == right.PageCount
                    ? Loc("Str_Cmp_Identical")
                    : Loc("Str_Cmp_SharedIdentical"));
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine(string.Format(Loc("Str_Cmp_Summary"), changed.Count, shared));
            foreach (var (page, fraction, regions) in changed)
                sb.AppendLine(string.Format(Loc("Str_Cmp_Page"), page,
                                            Math.Round(fraction * 100, 1), regions));
            return sb.ToString().TrimEnd();
        }

        /// <summary>Renders one page to raw BGRA, which is what the pixel comparison wants.</summary>
        private static (byte[] pixels, int w, int h) RenderBgra(DocnetPageRasterizer rast, int page)
        {
            var raster = rast.RenderPage(page);
            using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(
                                raster.ImageBytes);
            var buffer = new byte[img.Width * img.Height * 4];
            img.CopyPixelDataTo(buffer);
            return (buffer, img.Width, img.Height);
        }
    }
}
