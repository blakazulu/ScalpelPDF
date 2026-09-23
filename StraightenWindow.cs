using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Scalpel
{
    /// <summary>
    /// Corner picker for straightening a photographed page.
    /// <para>Drag the four handles onto the corners of the page in the photo and the perspective
    /// is undone, turning a phone snap of a document into something square enough to read, print
    /// and OCR. The maths is <see cref="Scalpel.Services.PerspectiveWarp"/>; this window only
    /// collects the four corners.</para>
    /// </summary>
    internal sealed class StraightenWindow : Window
    {
        private readonly BitmapSource _source;
        private readonly Canvas _canvas = new();
        private readonly Image _preview = new();
        private readonly Polygon _outline = new();
        private readonly Ellipse[] _handles = new Ellipse[4];

        // Corners as fractions of the image (top-left, top-right, bottom-right, bottom-left),
        // which is what PerspectiveWarp wants and stays valid at any preview size.
        private readonly Point[] _corners =
            [new(0.06, 0.06), new(0.94, 0.06), new(0.94, 0.94), new(0.06, 0.94)];

        private int _dragging = -1;

        /// <summary>The straightened page, set when the user accepts.</summary>
        public BitmapSource? Result { get; private set; }

        public StraightenWindow(Window owner, BitmapSource source, Func<string, string> loc)
        {
            _source = source;
            Owner = owner;
            Title = loc("Str_Straighten_Title");
            Width = 780;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("BgModal");
            FontFamily = (FontFamily)Application.Current.FindResource("FontUI");

            var root = new DockPanel { Margin = new Thickness(14) };

            var hint = new TextBlock
            {
                Text = loc("Str_Straighten_Hint"),
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            DockPanel.SetDock(hint, Dock.Top);
            root.Children.Add(hint);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            var cancel = new Button
            {
                Content = loc("Str_Ocr_Progress_Cancel"),
                Style = (Style)Application.Current.FindResource("StudioToolButton"),
                MinWidth = 96,
                Margin = new Thickness(0, 0, 8, 0),
            };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            var apply = new Button
            {
                Content = loc("Str_Straighten_Apply"),
                Style = (Style)Application.Current.FindResource("StudioPrimaryButton"),
                MinWidth = 120,
            };
            apply.Click += (_, _) => Accept(loc);
            buttons.Children.Add(cancel);
            buttons.Children.Add(apply);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _preview.Source = source;
            _preview.Stretch = Stretch.Uniform;
            _canvas.Background = Brushes.Transparent;
            _canvas.ClipToBounds = true;

            var host = new Grid();
            host.Children.Add(_preview);
            host.Children.Add(_canvas);
            root.Children.Add(host);

            _outline.Stroke = (Brush)Application.Current.FindResource("Accent");
            _outline.StrokeThickness = 1.5;
            _outline.Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            _outline.IsHitTestVisible = false;
            _canvas.Children.Add(_outline);

            for (int i = 0; i < 4; i++)
            {
                var handle = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = (Brush)Application.Current.FindResource("Accent"),
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Cursor = Cursors.SizeAll,
                };
                _handles[i] = handle;
                _canvas.Children.Add(handle);
            }

            _canvas.MouseLeftButtonDown += CanvasMouseDown;
            _canvas.MouseMove += CanvasMouseMove;
            _canvas.MouseLeftButtonUp += (_, _) => { _dragging = -1; _canvas.ReleaseMouseCapture(); };
            host.SizeChanged += (_, _) => Redraw();
            Loaded += (_, _) => Redraw();

            Content = root;
        }

        /// <summary>The rectangle the image actually occupies inside the letterboxed preview.</summary>
        private Rect ImageBounds()
        {
            double availW = _canvas.ActualWidth, availH = _canvas.ActualHeight;
            if (availW <= 0 || availH <= 0 || _source.PixelWidth <= 0 || _source.PixelHeight <= 0)
                return new Rect(0, 0, Math.Max(1, availW), Math.Max(1, availH));

            double scale = Math.Min(availW / _source.PixelWidth, availH / _source.PixelHeight);
            double w = _source.PixelWidth * scale, h = _source.PixelHeight * scale;
            return new Rect((availW - w) / 2, (availH - h) / 2, w, h);
        }

        private void Redraw()
        {
            Rect bounds = ImageBounds();
            _outline.Points.Clear();
            for (int i = 0; i < 4; i++)
            {
                double x = bounds.X + _corners[i].X * bounds.Width;
                double y = bounds.Y + _corners[i].Y * bounds.Height;
                _outline.Points.Add(new Point(x, y));
                Canvas.SetLeft(_handles[i], x - _handles[i].Width / 2);
                Canvas.SetTop(_handles[i], y - _handles[i].Height / 2);
            }
        }

        private void CanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(_canvas);
            Rect bounds = ImageBounds();
            double best = double.MaxValue;
            int pick = -1;
            for (int i = 0; i < 4; i++)
            {
                double x = bounds.X + _corners[i].X * bounds.Width;
                double y = bounds.Y + _corners[i].Y * bounds.Height;
                double d = (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
                if (d < best) { best = d; pick = i; }
            }
            // Only grab a handle the click was actually near, so a stray click does not yank one.
            if (pick >= 0 && best <= 30 * 30)
            {
                _dragging = pick;
                _canvas.CaptureMouse();
            }
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            Rect bounds = ImageBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            Point p = e.GetPosition(_canvas);
            _corners[_dragging] = new Point(
                Math.Max(0, Math.Min(1, (p.X - bounds.X) / bounds.Width)),
                Math.Max(0, Math.Min(1, (p.Y - bounds.Y) / bounds.Height)));
            Redraw();
        }

        private void Accept(Func<string, string> loc)
        {
            try
            {
                if (Scalpel.Services.PerspectiveWarp.IsIdentity(_corners))
                {
                    // Nothing was moved, so there is nothing to correct.
                    DialogResult = false;
                    Close();
                    return;
                }
                Result = Scalpel.Services.PerspectiveWarp.Apply(_source, _corners);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                // The corners can be dragged into a crossed shape, which has no valid warp.
                ScalpelDialog.Show(this, ex.Message, loc("Str_Straighten_Title"),
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
