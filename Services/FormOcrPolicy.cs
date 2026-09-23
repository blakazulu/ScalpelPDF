using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>The kinds of form field the OCR policy distinguishes.</summary>
    public enum FormOcrFieldKind { Other, Text, Choice }

    /// <summary>
    /// The subset of a PDF form widget the OCR policy needs: field identity and constraints,
    /// the widget rectangle in PDF user space (points, bottom-left origin) and the page box it
    /// sits in, plus the page's /Rotate value.
    /// </summary>
    public sealed record FormOcrWidget(
        string FieldName,
        FormOcrFieldKind FieldKind,
        long Flags,
        int MaximumLength,
        IReadOnlyList<string> Options,
        double Left, double Bottom, double Right, double Top,
        double PageBoxLeft, double PageBoxBottom, double PageBoxWidth, double PageBoxHeight,
        int PageRotation);

    /// <summary>
    /// Maps fillable form fields onto a page raster so handwriting / print inside each box can
    /// be OCR'd into the matching field, and post-processes recognized text against the field's
    /// constraints (numeric whitelist, comb cells, choice lists).
    /// </summary>
    public static class FormOcrPolicy
    {
        private static readonly string[] NumericNameMarkers =
            ["amount", "total", "number", "numeric", "price", "quantity",
             "qty", "zip", "postal", "phone", "date", "currency", "tax"];

        /// <summary>PDF /Ff bit 25: the text field is a comb of MaxLen cells.</summary>
        private const long CombFlag = 1L << 24;

        /// <summary>Characters allowed when a field name looks numeric.</summary>
        public const string NumericWhitelist = "0123456789.,-+/$%(): ";

        /// <summary>One OCR region in pixel space of the rendered page.</summary>
        public sealed record Region(
            int Left, int Top, int Right, int Bottom,
            string? CharacterWhitelist, int MaximumLength,
            IReadOnlyList<string> ChoiceValues, bool IsComb);

        /// <summary>Projects text and choice widgets onto a <paramref name="pixelWidth"/> x
        /// <paramref name="pixelHeight"/> render of their page, honouring the page's own rotation
        /// plus any <paramref name="additionalRotation"/> the viewer applied. Regions thinner than
        /// three pixels on either axis are dropped.</summary>
        public static IReadOnlyList<Region> MapRegions(
            IReadOnlyList<FormOcrWidget> widgets, int pixelWidth, int pixelHeight,
            int additionalRotation = 0)
        {
            var regions = new List<Region>();
            foreach (FormOcrWidget widget in widgets)
            {
                if (widget.FieldKind is not (FormOcrFieldKind.Text or FormOcrFieldKind.Choice)
                    || widget.PageBoxWidth <= 0 || widget.PageBoxHeight <= 0)
                    continue;

                double left = (widget.Left - widget.PageBoxLeft) / widget.PageBoxWidth;
                double right = (widget.Right - widget.PageBoxLeft) / widget.PageBoxWidth;
                double top = 1 - (widget.Top - widget.PageBoxBottom) / widget.PageBoxHeight;
                double bottom = 1 - (widget.Bottom - widget.PageBoxBottom) / widget.PageBoxHeight;
                int rotation = ((widget.PageRotation + additionalRotation) % 360 + 360) % 360;
                (left, top, right, bottom) = RotateBounds(left, top, right, bottom, rotation);

                const double coordinateEpsilon = 1e-7;
                int x1 = Clamp((int)Math.Floor(left * pixelWidth + coordinateEpsilon), 0, pixelWidth - 1);
                int y1 = Clamp((int)Math.Floor(top * pixelHeight + coordinateEpsilon), 0, pixelHeight - 1);
                int x2 = Clamp((int)Math.Ceiling(right * pixelWidth - coordinateEpsilon), x1 + 1, pixelWidth);
                int y2 = Clamp((int)Math.Ceiling(bottom * pixelHeight - coordinateEpsilon), y1 + 1, pixelHeight);
                if (x2 - x1 < 3 || y2 - y1 < 3) continue;

                string? whitelist = LooksNumeric(widget.FieldName) ? NumericWhitelist : null;
                int maximumLength = widget.FieldKind == FormOcrFieldKind.Text
                    ? widget.MaximumLength : 0;
                string[] choices = [.. widget.Options
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct()];
                regions.Add(new Region(x1, y1, x2, y2, whitelist, maximumLength, choices,
                    maximumLength > 0 && x2 - x1 >= maximumLength && (widget.Flags & CombFlag) != 0));
            }
            return regions;
        }

        /// <summary>Snaps OCR <paramref name="text"/> to the nearest entry of a choice list when it
        /// is within a third of that entry's length (case-insensitive edit distance); otherwise
        /// returns the text unchanged.</summary>
        public static string ClosestChoice(string text, IReadOnlyList<string> choices)
        {
            if (choices.Count == 0) return text;
            string best = choices[0];
            int bestDistance = Distance(text, best);
            foreach (string choice in choices.Skip(1))
            {
                int distance = Distance(text, choice);
                if (distance < bestDistance) { best = choice; bestDistance = distance; }
            }
            return bestDistance <= Math.Max(1, best.Length / 3) ? best : text;
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static (double left, double top, double right, double bottom) RotateBounds(
            double left, double top, double right, double bottom, int rotation)
        {
            var points = new[] { (left, top), (right, top), (right, bottom), (left, bottom) }
                .Select(point => rotation switch
                {
                    90 => (1 - point.Item2, point.Item1),
                    180 => (1 - point.Item1, 1 - point.Item2),
                    270 => (point.Item2, 1 - point.Item1),
                    _ => point
                }).ToArray();
            return (points.Min(p => p.Item1), points.Min(p => p.Item2),
                points.Max(p => p.Item1), points.Max(p => p.Item2));
        }

        private static bool LooksNumeric(string name)
        {
            string normalized = (name ?? "").ToLowerInvariant();
            return NumericNameMarkers.Any(normalized.Contains);
        }

        private static int Distance(string left, string right)
        {
            var previous = Enumerable.Range(0, right.Length + 1).ToArray();
            for (int i = 1; i <= left.Length; i++)
            {
                var current = new int[right.Length + 1];
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + (char.ToUpperInvariant(left[i - 1]) ==
                            char.ToUpperInvariant(right[j - 1]) ? 0 : 1));
                previous = current;
            }
            return previous[right.Length];
        }
    }
}
