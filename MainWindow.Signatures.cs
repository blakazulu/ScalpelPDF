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
        // Signatures
        // ============================================================

        private void LoadSignatures() => _signatureStore.Load();

        private void PersistSignatures() => _signatureStore.Persist();

        private void ShowSignaturePopup()
        {
            // NOTE: this popup is rebuilt on every open. All event handlers here are lambdas
            // on the popup's own child elements — no external source subscriptions, so no leak.
            // If SignatureStore.Signatures ever becomes ObservableCollection and this popup
            // subscribes to CollectionChanged, use CollectionChangedEventManager instead of +=.
            HideSignaturePopup();

            var stack = new StackPanel { Margin = new Thickness(4) };

            // Title
            stack.Children.Add(new TextBlock
            {
                Text = "Signatures",
                Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                FontFamily = (FontFamily)FindResource("FontUI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(4, 2, 4, 6)
            });

            // Saved signatures
            if (_signatureStore.Signatures.Count > 0)
            {
                var scroll = new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                var listPanel = new StackPanel();

                foreach (var sig in _signatureStore.Signatures)
                {
                    var sigCopy = sig; // capture for lambda
                    var item = new Border
                    {
                        Background = Brushes.White,
                        BorderBrush = _swatchDimBorder,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(4, 2, 4, 2),
                        Padding = new Thickness(4),
                        Cursor = Cursors.Hand,
                        Height = 60,
                        Width = 220
                    };

                    // Render mini signature preview
                    if (sigCopy.ImageData is not null)
                    {
                        try
                        {
                            var imgBytes = Convert.FromBase64String(sigCopy.ImageData);
                            var bmpImg = new System.Windows.Media.Imaging.BitmapImage();
                            bmpImg.BeginInit();
                            bmpImg.StreamSource = new System.IO.MemoryStream(imgBytes);
                            bmpImg.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmpImg.EndInit();
                            item.Child = new System.Windows.Controls.Image
                            {
                                Source = bmpImg,
                                Width = 210, Height = 50,
                                Stretch = System.Windows.Media.Stretch.Uniform,
                                IsHitTestVisible = false
                            };
                        }
                        catch { item.Child = new TextBlock { Text = "(image)", IsHitTestVisible = false }; }
                    }
                    else
                    {
                        var canvas = new Canvas
                        {
                            Width = 210, Height = 50,
                            Background = Brushes.Transparent,
                            IsHitTestVisible = false
                        };
                        RenderSignaturePreview(canvas, sigCopy, 210, 50);
                        item.Child = canvas;
                    }

                    item.MouseLeftButtonDown += (s, e) =>
                    {
                        // Re-assert the Signature tool so placement works even if the tool was
                        // reset to Select while the dropdown was reshown (e.g. after Create/Import).
                        if (_currentTool != EditTool.Signature) SetTool(EditTool.Signature);
                        _pendingSignature = sigCopy;
                        HideSignaturePopup();
                        _annotationCanvas.Cursor = Cursors.Cross;
                        SetStatus("Click on the page to place your signature");
                    };
                    item.MouseEnter += (s, e) =>
                        ((Border)s!).BorderBrush = (SolidColorBrush)FindResource("Accent");
                    item.MouseLeave += (s, e) =>
                        ((Border)s!).BorderBrush = _swatchDimBorder;

                    // Wrap in grid with delete button
                    var itemGrid = new Grid();
                    itemGrid.Children.Add(item);

                    var delBtn = new Button
                    {
                        Content = (string)FindResource("Ico_WinClose"),
                        FontFamily = (FontFamily)FindResource("FontIcon"),
                        FontSize = 10,
                        Width = 18, Height = 18,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 0, 2, 0),
                        Background = Brushes.Transparent,
                        Foreground = (SolidColorBrush)FindResource("DangerRed"),
                        BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand,
                        Padding = new Thickness(0),
                        Style = (Style)FindResource("ToolbarButton")
                    };
                    delBtn.Click += (s, e) =>
                    {
                        _signatureStore.Remove(sigCopy);
                        PersistSignatures();
                        ShowSignaturePopup(); // refresh
                    };
                    itemGrid.Children.Add(delBtn);
                    listPanel.Children.Add(itemGrid);
                }
                scroll.Content = listPanel;
                stack.Children.Add(scroll);
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "No saved signatures",
                    Foreground = (SolidColorBrush)FindResource("TextSecondary"),
                    FontFamily = (FontFamily)FindResource("FontUI"),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(4, 4, 4, 8),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            // Separator
            stack.Children.Add(new Rectangle
            {
                Height = 1,
                Fill = (SolidColorBrush)FindResource("BorderDim"),
                Margin = new Thickness(4, 4, 4, 4)
            });

            // Create Signature button
            var createBtn = new Button
            {
                Content = "Create Signature",
                Style = (Style)FindResource("DarkButton"),
                Background = (SolidColorBrush)FindResource("AccentDim"),
                Foreground = (SolidColorBrush)FindResource("Accent"),
                BorderBrush = (SolidColorBrush)FindResource("AccentDim"),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            createBtn.Click += (s, e) =>
            {
                HideSignaturePopup();
                OpenSignatureCreator();
            };
            stack.Children.Add(createBtn);

            // Import image button
            var importBtn = new Button
            {
                Content = "Import Image",
                Style = (Style)FindResource("DarkButton"),
                Background = (SolidColorBrush)FindResource("AccentDim"),
                Foreground = (SolidColorBrush)FindResource("Accent"),
                BorderBrush = (SolidColorBrush)FindResource("AccentDim"),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(4, 2, 4, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            importBtn.Click += (s, e) =>
            {
                HideSignaturePopup();
                ImportImageSignature();
            };
            stack.Children.Add(importBtn);

            // The card itself — surface colors come from theme tokens so it follows Light/Dark/HC.
            var card = new Border
            {
                Background = (SolidColorBrush)FindResource("BgModal"),
                BorderBrush = (SolidColorBrush)FindResource("BorderDim"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                Child = stack,
                MinWidth = 240,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 12, Opacity = 0.4, ShadowDepth = 4
                }
            };

            // Drop it down directly beneath the Sign tool button rather than floating in the
            // page area. A real Popup closes itself when the user clicks elsewhere.
            _signaturePopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = card,
                PlacementTarget = _toolSignatureBtn,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                HorizontalOffset = 0,
                VerticalOffset = 4,
            };
            // When the dropdown closes (incl. clicking outside, which a Popup does itself), drop our
            // reference so the Sign button reopens in ONE click. If nothing was picked, leave Sign
            // mode entirely so the button no longer looks "active".
            _signaturePopup.Closed += (_, _) =>
            {
                _signaturePopup = null;
                if (_pendingSignature is null && _currentTool == EditTool.Signature)
                    SetTool(EditTool.Select);
            };
            _signaturePopup.IsOpen = true;
        }

        private void HideSignaturePopup()
        {
            if (_signaturePopup is not null)
            {
                _signaturePopup.IsOpen = false;
                _signaturePopup = null;
            }
        }

        private void RenderSignaturePreview(Canvas canvas, SavedSignature sig, double targetW, double targetH)
        {
            double scaleX = targetW / sig.CanvasWidth;
            double scaleY = targetH / sig.CanvasHeight;
            double scale = Math.Min(scaleX, scaleY) * 0.9;

            double offsetX = (targetW - sig.CanvasWidth * scale) / 2;
            double offsetY = (targetH - sig.CanvasHeight * scale) / 2;

            foreach (var stroke in sig.Strokes)
            {
                if (stroke.Count < 2) continue;
                var poly = new Polyline
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.5,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                foreach (var pt in stroke)
                    poly.Points.Add(new Point(pt.X * scale + offsetX, pt.Y * scale + offsetY));
                canvas.Children.Add(poly);
            }
        }

        private void OpenSignatureCreator()
        {
            var fontUI  = (FontFamily)Application.Current.FindResource("FontUI");
            var bgModal = (Brush)Application.Current.FindResource("BgModal");

            var win = new Window
            {
                Title = "Create Signature",
                Width = 460, Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = fontUI
            };

            // Outer chrome
            var outerChrome = new Border
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
            var rootStack = new StackPanel();

            // Title bar
            var titleBar = new Border
            {
                Background   = (Brush)Application.Current.FindResource("BgPanel"),
                Padding      = new Thickness(14, 8, 8, 8),
                CornerRadius = new CornerRadius(11, 11, 0, 0)
            };
            titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };
            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleText = new TextBlock
            {
                Text       = "Create Signature",
                Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
                FontWeight = FontWeights.SemiBold,
                FontSize   = (double)Application.Current.FindResource("FsDialogTitle"),
                FontFamily = fontUI,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 0);
            var closeWinBtn = new Button
            {
                Style             = (Style)Application.Current.FindResource("StudioIconButton"),
                Content           = Application.Current.FindResource("Ico_WinClose"),
                VerticalAlignment = VerticalAlignment.Center
            };
            closeWinBtn.Click += (_, _2) => win.Close();
            Grid.SetColumn(closeWinBtn, 1);
            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(closeWinBtn);
            titleBar.Child = titleGrid;
            rootStack.Children.Add(titleBar);

            var contentArea = new StackPanel();

            // Drawing canvas
            var canvasBorder = new Border
            {
                Background = Brushes.White,
                Margin = new Thickness(12, 12, 12, 4),
                CornerRadius = new CornerRadius(4),
                Height = 170
            };
            var drawCanvas = new Canvas
            {
                Background = Brushes.White,
                ClipToBounds = true,
                Cursor = Cursors.Pen
            };
            canvasBorder.Child = drawCanvas;

            // Placeholder text
            var placeholder = new TextBlock
            {
                Text = "Draw your signature here",
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                FontFamily = fontUI,
                FontSize = 14, FontStyle = FontStyles.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            drawCanvas.Children.Add(placeholder);

            // Drawing state
            var strokes = new List<List<Point>>();
            List<Point>? currentStroke = null;
            Polyline? currentPoly = null;

            drawCanvas.MouseLeftButtonDown += (s, e) =>
            {
                if (placeholder.Visibility == Visibility.Visible)
                    placeholder.Visibility = Visibility.Collapsed;
                currentStroke = [];
                var pos = e.GetPosition(drawCanvas);
                currentStroke.Add(pos);
                currentPoly = new Polyline
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                currentPoly.Points.Add(pos);
                drawCanvas.Children.Add(currentPoly);
                drawCanvas.CaptureMouse();
            };

            drawCanvas.MouseMove += (s, e) =>
            {
                if (currentStroke is null || currentPoly is null) return;
                var pos = e.GetPosition(drawCanvas);
                pos.X = Math.Max(0, Math.Min(drawCanvas.ActualWidth, pos.X));
                pos.Y = Math.Max(0, Math.Min(drawCanvas.ActualHeight, pos.Y));
                currentStroke.Add(pos);
                currentPoly.Points.Add(pos);
            };

            drawCanvas.MouseLeftButtonUp += (s, e) =>
            {
                if (currentStroke is not null && currentStroke.Count > 1)
                    strokes.Add(currentStroke);
                else if (currentPoly is not null)
                    drawCanvas.Children.Remove(currentPoly);
                currentStroke = null;
                currentPoly = null;
                drawCanvas.ReleaseMouseCapture();
            };

            contentArea.Children.Add(canvasBorder);

            // Buttons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 4, 12, 12)
            };

            var clearBtn = new Button
            {
                Content = "Clear",
                Style   = (Style)Application.Current.FindResource("StudioToolButton"),
                Margin  = new Thickness(0, 0, 8, 0)
            };
            clearBtn.Click += (s, e) =>
            {
                strokes.Clear();
                drawCanvas.Children.Clear();
                placeholder.Visibility = Visibility.Visible;
                drawCanvas.Children.Add(placeholder);
            };

            var saveBtn = new Button
            {
                Content = "Save Signature",
                Style   = (Style)Application.Current.FindResource("StudioPrimaryButton")
            };
            saveBtn.Click += (s, e) =>
            {
                if (strokes.Count == 0)
                {
                    ScalpelDialog.Show(this, "Draw a signature first.", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                double cw = drawCanvas.ActualWidth > 0 ? drawCanvas.ActualWidth : 400;
                double ch = drawCanvas.ActualHeight > 0 ? drawCanvas.ActualHeight : 150;

                var saved = new SavedSignature
                {
                    CanvasWidth = cw,
                    CanvasHeight = ch,
                    Name = $"Signature {_signatureStore.Signatures.Count + 1}"
                };
                foreach (var stroke in strokes)
                {
                    var sPts = stroke.Select(p => new SerializablePoint { X = p.X, Y = p.Y }).ToList();
                    saved.Strokes.Add(sPts);
                }
                _signatureStore.Add(saved);
                PersistSignatures();

                // Auto-select the new signature for placement
                _pendingSignature = saved;
                _annotationCanvas.Cursor = Cursors.Cross;
                SetStatus("Signature saved - click on the page to place it");

                win.Close();
            };

            btnPanel.Children.Add(clearBtn);
            btnPanel.Children.Add(saveBtn);
            contentArea.Children.Add(btnPanel);

            rootStack.Children.Add(contentArea);
            outerChrome.Child = rootStack;
            win.Content = outerChrome;
            win.ShowDialog();
        }

        private void ImportImageSignature()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
                Title = "Import Signature Image"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage(new Uri(dlg.FileName));
                byte[] pngBytes;
                using (var ms = new System.IO.MemoryStream())
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    encoder.Save(ms);
                    pngBytes = ms.ToArray();
                }

                var saved = new SavedSignature
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
                    CanvasWidth = bmp.PixelWidth,
                    CanvasHeight = bmp.PixelHeight,
                    ImageData = Convert.ToBase64String(pngBytes)
                };
                _signatureStore.Add(saved);
                PersistSignatures();

                _pendingSignature = saved;
                _annotationCanvas.Cursor = Cursors.Cross;
                SetStatus("Image loaded - click on the page to place it");
                ShowSignaturePopup(); // refresh to show the new entry
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Failed to import image:\n{ex.Message}", "Scalpel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlaceSignature(Point pos, int pageIdx)
        {
            if (_pendingSignature is null) return;

            var sig = _pendingSignature;
            double scale = 0.5;

            var annot = new SignatureAnnotation
            {
                PageIndex = pageIdx,
                Position = pos,
                Scale = scale,
                SourceWidth = sig.CanvasWidth,
                SourceHeight = sig.CanvasHeight,
                ImageData = sig.ImageData
            };

            // Drawn signature — convert serializable points to WPF points
            if (sig.ImageData is null)
            {
                foreach (var stroke in sig.Strokes)
                    annot.Strokes.Add([..stroke.Select(p => new Point(p.X, p.Y))]);
            }

            AddAnnotation(annot);
            RenderAllAnnotations(pageIdx);

            // Auto-switch to Select and select the placed signature so the user
            // can immediately reposition or resize without an extra click.
            SetTool(EditTool.Select);
            double sigW = sig.CanvasWidth * scale;
            double sigH = sig.CanvasHeight * scale;
            SelectAnnotation(annot, new Rect(pos.X, pos.Y, sigW, sigH));
            SetStatus("Signature placed — drag to reposition, use the corner handle to resize");
            Scalpel.Services.Logger.Info("Sign", "sign.success", "Signature placed", new { page = pageIdx + 1 });
        }

        private void PlaceImageFromDialog(Point pos, int pageIdx)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Insert Image",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.tif|All files|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                var imgBytes = File.ReadAllBytes(dlg.FileName);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(imgBytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                double srcW = bmp.PixelWidth > 0 ? bmp.PixelWidth : 400;
                double srcH = bmp.PixelHeight > 0 ? bmp.PixelHeight : 300;

                // Default the placed image to ~50% of the page's longest side (in render-dim
                // units) so it is a usable size regardless of page dimensions, never upscaling
                // beyond the source's native resolution.
                double pageMax = _renderDims.TryGetValue(pageIdx, out var rdImg)
                    ? Math.Max(rdImg.w, rdImg.h) : 2048.0;
                double MaxCanvasDim = pageMax * 0.5;
                double scale = Math.Min(1.0, Math.Min(MaxCanvasDim / srcW, MaxCanvasDim / srcH));

                var imgAnnot = new ImageAnnotation
                {
                    PageIndex = pageIdx,
                    Position = pos,
                    Scale = scale,
                    SourceWidth = srcW,
                    SourceHeight = srcH,
                    ImageData = Convert.ToBase64String(imgBytes)
                };

                // Switch to Select FIRST so placement renders last and nothing wipes the image
                // (calling SetTool between render and select was what made the image vanish).
                SetTool(EditTool.Select);
                AddAnnotation(imgAnnot);
                RenderAllAnnotations(pageIdx);
                double w = srcW * scale;
                double h = srcH * scale;
                SelectAnnotation(imgAnnot, new Rect(pos.X, pos.Y, w, h));
                SetStatus("Image placed - drag to reposition, use the corner handle to resize");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Could not load image:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
