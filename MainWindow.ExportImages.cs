using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Export pages as images
        // ============================================================

        /// <summary>
        /// Writes the selected pages (or all of them) out as PNG or JPEG files, one per page.
        /// Colour treatment goes through <see cref="PageQualityConverter"/>, so a scanned page can
        /// be exported as grayscale or hard black-and-white for a fax or a photocopier without
        /// needing an image editor.
        /// </summary>
        private async void ToolsExportImages_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            // Honour the Pages panel selection, the way Extract already does.
            var pages = PageList.SelectedItems.Cast<PageThumbnailVm>()
                                .Select(vm => vm.PageIndex).OrderBy(i => i).ToList();
            if (pages.Count == 0)
                pages = Enumerable.Range(0, _doc!.PageCount).ToList();

            var fFormat = new ToolField(Loc("Str_Exp_Format"), ToolFieldKind.Combo, "PNG",
                                        ["PNG", "JPEG"]);
            var fColor = new ToolField(Loc("Str_Exp_Colour"), ToolFieldKind.Combo, Loc("Str_Exp_Colour_Color"),
                                       [Loc("Str_Exp_Colour_Color"), Loc("Str_Exp_Colour_Gray"),
                                        Loc("Str_Exp_Colour_Bw")]);
            var fQuality = new ToolField(Loc("Str_Exp_Resolution"), ToolFieldKind.Combo, "150 DPI",
                                         ["72 DPI", "150 DPI", "300 DPI", "600 DPI"]);

            if (!ShowToolForm(Loc("Str_Tool_ExportImages"), new[] { fFormat, fColor, fQuality },
                              Loc("Str_Exp_Export"),
                              string.Format(Loc("Str_Exp_Note"), pages.Count)))
                return;

            bool jpeg = fFormat.Value.StartsWith("JPEG", StringComparison.OrdinalIgnoreCase);
            string ext = jpeg ? "jpg" : "png";
            var mode = fColor.Value == Loc("Str_Exp_Colour_Gray") ? PageColorMode.Grayscale
                     : fColor.Value == Loc("Str_Exp_Colour_Bw") ? PageColorMode.BlackAndWhite
                     : PageColorMode.Color;
            int dpi = int.TryParse(new string(fQuality.Value.TakeWhile(char.IsDigit).ToArray()), out int d)
                      ? d : 150;

            var dlg = new SaveFileDialog
            {
                Title = Loc("Str_Tool_ExportImages"),
                Filter = jpeg ? "JPEG image|*.jpg" : "PNG image|*.png",
                FileName = Path.GetFileNameWithoutExtension(_originalFile ?? "page") + "." + ext,
                CheckFileExists = false, CheckPathExists = true,
            };
            // No forwarded open may swap the active tab while the dialog is up: the code below
            // acts on `_s` once it closes (same guard as Save As).
            bool? picked;
            BeginTabOp();
            try { picked = dlg.ShowDialog(this); }
            finally { EndTabOp(); }
            if (picked != true) return;
            string chosen = ChosenPath(dlg, ext);

            // Export only reads the document (rasterizes the chosen pages to image files); it never
            // adopts a result or changes the dirty flag, so no session needs to be pinned. The gate
            // still has to be taken so the document cannot be closed out from under the render.
            using var op = _longOps.Begin();

            string dir = Path.GetDirectoryName(chosen) ?? "";
            string stem = Path.GetFileNameWithoutExtension(chosen);
            string src = BuildWorkingSourceFile();

            // A page's long edge in pixels at the requested DPI. The rasterizer takes a pixel
            // budget rather than a DPI, so convert using the largest page in the export.
            double longestEdgePt = 792;
            try
            {
                foreach (int p in pages)
                    longestEdgePt = Math.Max(longestEdgePt,
                        Math.Max(_doc!.Pages[p].Width.Point, _doc.Pages[p].Height.Point));
            }
            catch { }
            int longEdgePx = (int)Math.Round(longestEdgePt / 72.0 * dpi);
            longEdgePx = Math.Max(200, Math.Min(10000, longEdgePx));

            SetStatus(string.Format(Loc("Str_Exp_Working"), pages.Count));
            int written = 0;
            string? failure = null;

            try
            {
                var results = await Task.Run(() =>
                {
                    var files = new List<(string path, byte[] png, int w, int h)>();
                    using var rast = new DocnetPageRasterizer(src, longEdgePx);
                    for (int i = 0; i < pages.Count; i++)
                    {
                        int p = pages[i];
                        if (p < 0 || p >= rast.PageCount) continue;
                        var page = rast.RenderPage(p);
                        // Single page keeps the plain name; a set gets a zero-padded suffix so
                        // the files sort correctly in Explorer.
                        string name = pages.Count == 1 ? $"{stem}.{ext}"
                                                       : $"{stem}-{p + 1:D3}.{ext}";
                        files.Add((Path.Combine(dir, name), page.ImageBytes, page.PixelWidth, page.PixelHeight));
                    }
                    return files;
                });

                foreach (var (path, pngBytes, _, _) in results)
                {
                    // Decoding and encoding are WPF imaging, so they belong on the UI thread's
                    // side of the fence; the expensive rasterization already happened above.
                    var decoded = DecodePng(pngBytes);
                    BitmapSource shaded = PageQualityConverter.ApplyColorMode(decoded, mode, 160);

                    // JPEG has no bilevel form, so a black-and-white page is widened to 8-bit gray
                    // before encoding. PNG stores both compact formats natively.
                    if (jpeg && shaded.Format == System.Windows.Media.PixelFormats.Indexed1)
                        shaded = new FormatConvertedBitmap(
                            shaded, System.Windows.Media.PixelFormats.Gray8, null, 0);

                    BitmapEncoder encoder = jpeg
                        ? new JpegBitmapEncoder { QualityLevel = 92 }
                        : new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(shaded));
                    using var fs = File.Create(path);
                    encoder.Save(fs);
                    written++;
                }
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                Logger.Warn("Tools", "export.images.fail", "Export as images failed", new { error = ex.Message });
            }

            if (failure is not null)
                ScalpelDialog.Show(this, string.Format(Loc("Str_Exp_Failed"), failure));
            else
                SetStatus(string.Format(Loc("Str_Exp_Done"), written, dir));
        }

        /// <summary>Decodes PNG bytes into a frozen bitmap that is safe to hand around.</summary>
        private static BitmapSource DecodePng(byte[] png)
        {
            using var ms = new MemoryStream(png);
            var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat,
                                               BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
    }
}
