using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Measure tool
        // ============================================================
        //
        // Drag across the page to read a real-world distance. The reading is independent of
        // zoom and render resolution because MeasurementCalculator scales the canvas delta by
        // the page's size in points, not by pixels.

        private Line? _measureLine;
        private Point _measureStart;

        /// <summary>Turns on the measure mode from the Tools menu.</summary>
        private void ToolsMeasure_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;
            SetTool(EditTool.Measure);
            SetStatus(Loc("Str_Measure_Hint"));
        }

        /// <summary>Begins a measurement drag.</summary>
        private void BeginMeasure(Point pos)
        {
            ClearMeasureOverlay();
            _measureStart = pos;
            _measureLine = new Line
            {
                X1 = pos.X, Y1 = pos.Y, X2 = pos.X, Y2 = pos.Y,
                Stroke = AccentBrush(),
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                IsHitTestVisible = false,
            };
            _activeCanvas?.Children.Add(_measureLine);
            _activeCanvas?.CaptureMouse();
        }

        /// <summary>Updates the rubber band and the live readout.</summary>
        private void UpdateMeasure(Point pos, int pageIdx)
        {
            if (_measureLine is null) return;

            // Shift constrains to the horizontal, vertical or diagonal, like the line tool.
            Point end = (System.Windows.Input.Keyboard.Modifiers
                         & System.Windows.Input.ModifierKeys.Shift) != 0
                        ? LineSnap.SnapEndpoint(_measureStart, pos)
                        : pos;

            _measureLine.X2 = end.X;
            _measureLine.Y2 = end.Y;
            SetStatus(DescribeMeasurement(_measureStart, end, pageIdx));
        }

        /// <summary>Finishes the drag, leaving the reading on the status bar.</summary>
        private void EndMeasure(Point pos, int pageIdx)
        {
            if (_measureLine is null) return;
            UpdateMeasure(pos, pageIdx);
            _activeCanvas?.ReleaseMouseCapture();
        }

        /// <summary>Formats a canvas-space drag as a distance in points, inches and millimetres.</summary>
        private string DescribeMeasurement(Point from, Point to, int pageIdx)
        {
            try
            {
                if (_doc is null || !_renderDims.TryGetValue(pageIdx, out var dims))
                    return Loc("Str_Measure_Hint");

                var page = _doc.Pages[pageIdx];
                _pageRotations.TryGetValue(pageIdx, out int rot);

                var m = MeasurementCalculator.Calculate(
                            page.Width.Point, page.Height.Point, rot,
                            dims.w, dims.h, to.X - from.X, to.Y - from.Y);

                return string.Format(Loc("Str_Measure_Reading"),
                                     Math.Round(m.Millimetres, 1),
                                     Math.Round(m.Inches, 2),
                                     Math.Round(m.Points, 1));
            }
            catch { return Loc("Str_Measure_Hint"); }
        }

        /// <summary>Removes the measurement rubber band, if one is showing.</summary>
        private void ClearMeasureOverlay()
        {
            if (_measureLine is null) return;
            RemoveFromOwner(_measureLine);
            _measureLine = null;
        }
    }
}
