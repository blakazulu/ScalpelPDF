using System;
using System.IO;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>Probe: what does a PdfSharpCore-written content stream actually look like?</summary>
    public class StreamShapeProbeTests
    {
        [Fact]
        public void ProbeStreamShape()
        {
            var report = new StringBuilder();
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(300);
                page.Height = XUnit.FromPoint(200);
                using (var gfx = XGraphics.FromPdfPage(page))
                    gfx.DrawString("SALARY-120000-SECRET", new XFont("Arial", 14),
                                   XBrushes.Black, 20, 60);

                var contents = page.Contents;
                report.AppendLine("contentCount=" + contents.Elements.Count);
                for (int i = 0; i < contents.Elements.Count; i++)
                {
                    var d = contents.Elements.GetDictionary(i);
                    report.AppendLine("  [" + i + "] dictNull=" + (d is null));
                    byte[]? raw = d?.Stream?.UnfilteredValue;
                    report.AppendLine("  [" + i + "] len=" + (raw?.Length ?? -1));
                    if (raw != null)
                        report.AppendLine("  [" + i + "] body=" +
                            Encoding.GetEncoding(28591).GetString(raw).Replace("\n", " | "));
                }
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "scalpel-stream-report.txt"),
                              report.ToString());
        }
    }
}
