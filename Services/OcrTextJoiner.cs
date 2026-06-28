using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>Joins recognized OCR words into plain text by grouping them into lines.</summary>
    public static class OcrTextJoiner
    {
        public static string Join(IReadOnlyList<OcrWord> words)
        {
            if (words == null || words.Count == 0) return "";
            var ordered = words.OrderBy(w => w.YPt).ThenBy(w => w.XPt).ToList();

            var sb = new StringBuilder();
            var line = new List<OcrWord>();
            double lineTop = ordered[0].YPt, lineH = ordered[0].HeightPt;

            void Flush()
            {
                if (line.Count == 0) return;
                sb.Append(string.Join(" ", line.OrderBy(w => w.XPt).Select(w => w.Text)).TrimEnd());
                sb.Append('\n');
                line.Clear();
            }

            foreach (var w in ordered)
            {
                if (line.Count > 0 && w.YPt - lineTop > 0.5 * lineH)
                {
                    Flush();
                    lineTop = w.YPt; lineH = w.HeightPt;
                }
                line.Add(w);
            }
            Flush();
            return sb.ToString().TrimEnd('\n');
        }
    }
}