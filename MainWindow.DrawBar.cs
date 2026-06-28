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
        // Draw/Highlight settings bar
        // ============================================================

        private static readonly Color[] SwatchColors =
        [
            Colors.Red, Colors.SaddleBrown, Colors.Orange, Colors.Gold,
            Colors.LimeGreen, Colors.DodgerBlue, Colors.MediumPurple,
            Colors.DeepPink, Colors.White, Colors.Black
        ];

        // Frozen cached brushes for hot-path UI construction
        private static readonly SolidColorBrush _swatchDimBorder   = Freeze(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));
        private static readonly SolidColorBrush _drawBarBackground  = Freeze(new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)));
        private static readonly SolidColorBrush _thumbBorderBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));

        private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private void ShowDrawSettings(EditTool tool)
        {
            if (_drawSettingsBar is not null)
            {
                var previewGrid = PagePreviewPanel.Parent as Grid;
                previewGrid?.Children.Remove(_drawSettingsBar);
                _drawSettingsBar = null;
            }

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };

            // Color label
            var colorLbl = new TextBlock
            {
                Text = "Color:",
                FontFamily = (FontFamily)FindResource("FontUI"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            };
            colorLbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            panel.Children.Add(colorLbl);

            // Color swatches
            var activeColor = (tool == EditTool.Draw || tool == EditTool.Line) ? _drawColor : Color.FromRgb(_highlightColor.R, _highlightColor.G, _highlightColor.B);
            foreach (var color in SwatchColors)
            {
                bool isActive = color == activeColor;
                var swatch = new Border
                {
                    Width = 18, Height = 18,
                    Background = Freeze(new SolidColorBrush(color)),
                    BorderThickness = new Thickness(isActive ? 2 : 1),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Tag = color
                };
                if (isActive)
                    swatch.SetResourceReference(Border.BorderBrushProperty, "Accent");
                else
                    swatch.SetResourceReference(Border.BorderBrushProperty, "BorderDim");
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    var c = (Color)((Border)s!).Tag;
                    if (tool == EditTool.Draw || tool == EditTool.Line)
                        _drawColor = Color.FromArgb(_drawOpacity, c.R, c.G, c.B);
                    else
                        _highlightColor = Color.FromArgb(_highlightColor.A, c.R, c.G, c.B);
                    ShowDrawSettings(tool); // refresh selection
                };
                panel.Children.Add(swatch);
            }

            // Separator
            var sep1 = new Rectangle { Width = 1, Margin = new Thickness(8, 2, 8, 2) };
            sep1.SetResourceReference(Rectangle.FillProperty, "BorderDim");
            panel.Children.Add(sep1);

            // Size slider (draw / line only)
            if (tool == EditTool.Draw || tool == EditTool.Line)
            {
                var sizeLbl = new TextBlock
                {
                    Text = "Size:",
                    FontFamily = (FontFamily)FindResource("FontUI"), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
                };
                sizeLbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                panel.Children.Add(sizeLbl);

                var sizeSlider = new Slider
                {
                    Minimum = 1, Maximum = 20, Value = _drawWidth,
                    Width = 80, VerticalAlignment = VerticalAlignment.Center,
                    TickFrequency = 1, IsSnapToTickEnabled = true
                };
                sizeSlider.ValueChanged += (s, e) => _drawWidth = e.NewValue;
                panel.Children.Add(sizeSlider);

                var sizeLabel = new TextBlock
                {
                    Text = $"{_drawWidth:F0}px",
                    FontFamily = (FontFamily)FindResource("FontUI"), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0)
                };
                sizeLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                sizeSlider.ValueChanged += (s, e) => sizeLabel.Text = $"{e.NewValue:F0}px";
                panel.Children.Add(sizeLabel);

                var sep2 = new Rectangle { Width = 1, Margin = new Thickness(8, 2, 8, 2) };
                sep2.SetResourceReference(Rectangle.FillProperty, "BorderDim");
                panel.Children.Add(sep2);
            }

            // Opacity slider
            var opacityLbl = new TextBlock
            {
                Text = "Opacity:",
                FontFamily = (FontFamily)FindResource("FontUI"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            };
            opacityLbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            panel.Children.Add(opacityLbl);

            byte currentOpacity = (tool == EditTool.Draw || tool == EditTool.Line) ? _drawOpacity : _highlightColor.A;
            var opacitySlider = new Slider
            {
                Minimum = 10, Maximum = 255, Value = currentOpacity,
                Width = 80, VerticalAlignment = VerticalAlignment.Center
            };
            var opacityLabel = new TextBlock
            {
                Text = $"{(int)(currentOpacity / 255.0 * 100)}%",
                FontFamily = (FontFamily)FindResource("FontUI"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0)
            };
            opacityLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            opacitySlider.ValueChanged += (s, e) =>
            {
                byte a = (byte)e.NewValue;
                opacityLabel.Text = $"{(int)(a / 255.0 * 100)}%";
                if (tool == EditTool.Draw || tool == EditTool.Line)
                {
                    _drawOpacity = a;
                    _drawColor = Color.FromArgb(a, _drawColor.R, _drawColor.G, _drawColor.B);
                }
                else
                {
                    _highlightColor = Color.FromArgb(a, _highlightColor.R, _highlightColor.G, _highlightColor.B);
                }
            };
            panel.Children.Add(opacitySlider);
            panel.Children.Add(opacityLabel);

            _drawSettingsBar = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Child = panel,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _drawSettingsBar.SetResourceReference(Border.BackgroundProperty,  "BgPanel");
            _drawSettingsBar.SetResourceReference(Border.BorderBrushProperty, "BorderDim");

            var previewArea = PagePreviewPanel.Parent as Grid;
            if (previewArea is not null)
            {
                Panel.SetZIndex(_drawSettingsBar, 100);
                previewArea.Children.Add(_drawSettingsBar);
            }
        }

        private void HideDrawSettings()
        {
            if (_drawSettingsBar is not null)
            {
                var previewGrid = PagePreviewPanel.Parent as Grid;
                previewGrid?.Children.Remove(_drawSettingsBar);
                _drawSettingsBar = null;
            }
        }

    }
}
