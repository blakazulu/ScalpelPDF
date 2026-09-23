using System.IO;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class PageRotationTests
    {
        private static double[] MediaBox(string path)
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var mb = doc.Pages[0].Elements.GetArray("/MediaBox")!;
            return [.. Enumerable.Range(0, 4).Select(i => mb.Elements.GetReal(i))];
        }

        [Fact]
        public void StrippingAndRestoringRotation_LeavesTheMediaBoxAlone()
        {
            // Regression: PdfPage.Rotate's setter swaps the MediaBox on an odd quarter-turn
            // change, so every working-copy strip turned a rotated portrait page landscape.
            string dir = Path.Combine(Path.GetTempPath(), "scalpel-rot-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                string cur = Path.Combine(dir, "src.pdf");
                using (var doc = new PdfDocument())
                {
                    doc.AddPage().Rotate = 90;
                    doc.Save(cur);
                }
                double[] original = MediaBox(cur);

                int held = 0;
                for (int n = 0; n < 4; n++)   // four quarter turns, each through a strip + reload
                {
                    using var d = PdfReader.Open(cur, PdfDocumentOpenMode.Modify);
                    if (n > 0) PageRotation.Set(d.Pages[0], held);
                    PageRotation.Set(d.Pages[0], d.Pages[0].Rotate + 90);
                    held = PageRotation.Normalize(d.Pages[0].Rotate);
                    PageRotation.Set(d.Pages[0], 0);
                    cur = Path.Combine(dir, $"step{n}.pdf");
                    d.Save(cur);
                }

                Assert.Equal(original, MediaBox(cur));
                Assert.Equal(90, held);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(450, 90)]
        [InlineData(-90, 270)]
        [InlineData(360, 0)]
        public void Normalize_FoldsAnyMultipleOf90(int input, int expected)
            => Assert.Equal(expected, PageRotation.Normalize(input));
    }
}
