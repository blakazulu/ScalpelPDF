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
        // Context menu
        // ============================================================

        private void ApplyGrainTexture()
        {
            // Sparse bright-speck film grain — same style as the first pass,
            // tuned so the texture is visible without being chunky.
            const int size = 256;
            var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[size * size * 4]; // start fully transparent
            var rng = new Random(1337);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (rng.Next(4) != 0) continue;       // ~25% pixel density
                byte v = (byte)rng.Next(160, 255);     // bright specks
                byte a = (byte)rng.Next(30, 80);       // low-ish alpha for subtlety
                pixels[i]     = v;
                pixels[i + 1] = v;
                pixels[i + 2] = v;
                pixels[i + 3] = a;
            }
            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            GrainBrush.ImageSource = bmp;
        }

        /// <summary>Generated film-grain tile, exposed so secondary windows (e.g. the
        /// print preview) can paint the same texture over their document area.</summary>
        public ImageSource? GrainTexture => GrainBrush?.ImageSource;

        private void BuildContextMenu()
        {
            var menu = new ContextMenu();

            menu.Items.Add(MakeMenuItem("Copy Text", (s, e) => CopySelectedText(), "Ctrl+C"));
            menu.Items.Add(MakeMenuItem("Print", (s, e) => Print_Click(s!, e), "Ctrl+P"));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Select Tool", (s, e) => SetTool(EditTool.Select)));
            menu.Items.Add(MakeMenuItem("Text Tool", (s, e) => SetTool(EditTool.Text)));
            menu.Items.Add(MakeMenuItem("Highlight Tool", (s, e) => SetTool(EditTool.Highlight)));
            menu.Items.Add(MakeMenuItem("Draw Tool", (s, e) => SetTool(EditTool.Draw)));
            menu.Items.Add(MakeMenuItem("Line Tool", (s, e) => SetTool(EditTool.Line)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Rotate Page CW",  (s, e) => RotatePages_Click(90)));
            menu.Items.Add(MakeMenuItem("Rotate Page CCW", (s, e) => RotatePages_Click(-90)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Delete Selected", (s, e) => DeleteSelected(), "Delete"));
            menu.Items.Add(MakeMenuItem("Undo Last", (s, e) => Undo_Click(s!, e), "Ctrl+Z"));
            menu.Items.Add(MakeMenuItem("Clear Page Annotations", (s, e) => ClearAnnotations_Click(s!, e)));

            _annotationCanvas.ContextMenu = menu;
        }

        private void PageList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_doc is null) return;
            var menu = new ContextMenu();
            menu.Items.Add(MakeMenuItem("Insert Blank Page After", (s, ev) => InsertBlankPage_Click(s!, ev)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Rotate CW",  (s, ev) => RotatePages_Click(90)));
            menu.Items.Add(MakeMenuItem("Rotate CCW", (s, ev) => RotatePages_Click(-90)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Move Page Up",   (s, ev) => MoveUp_Click(s!, ev)));
            menu.Items.Add(MakeMenuItem("Move Page Down", (s, ev) => MoveDown_Click(s!, ev)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Extract Page(s)", (s, ev) => Split_Click(s!, ev)));
            menu.Items.Add(MakeMenuItem("Delete Page(s)", (s, ev) => Delete_Click(s!, ev)));
            menu.PlacementTarget = PageList;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void RotatePages_Click(int delta)
        {
            if (_doc is null) return;
            var selected = PageList.SelectedItems;
            if (selected.Count == 0) return;
            try
            {
                var indices = new List<int>();
                foreach (PageThumbnailVm vm in selected) indices.Add(vm.PageIndex);
                foreach (var idx in indices)
                    _doc.Pages[idx].Rotate = ((_doc.Pages[idx].Rotate + delta) % 360 + 360) % 360;
                int restoreIdx = PageList.SelectedIndex;
                SaveTempAndReload();
                PageList.SelectedIndex = Math.Min(restoreIdx, PageList.Items.Count - 1);
                // After a rotation the page aspect ratio changes; always fit-to-page so the
                // full rotated page is visible regardless of the previous zoom level.
                FitToPage();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)FitToPage);
                SetStatus(string.Format(Loc("Str_Rotated"), indices.Count));
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, string.Format(Loc("Str_RotateFailed"), ex.Message), Loc("Str_Dlg_AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static MenuItem MakeMenuItem(string header, RoutedEventHandler click, string? gesture = null)
        {
            var item = new MenuItem { Header = header };
            item.Click += click;
            if (gesture != null)
                item.InputGestureText = gesture;
            return item;
        }

    }
}
