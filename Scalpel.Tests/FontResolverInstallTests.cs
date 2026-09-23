using System.IO;
using System.Runtime.CompilerServices;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using Xunit;

namespace System.Runtime.CompilerServices
{
    // net48 has no ModuleInitializerAttribute; the C# compiler only matches it by name.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace Scalpel.Tests
{
    internal static class FontResolverModuleInit
    {
        // PdfSharpCore refuses a resolver change once any font was resolved, so install ours
        // before the first test can create an XFont through PdfSharpCore's default resolver.
        [ModuleInitializer]
        internal static void Init() => PdfFontResolver.Install();
    }

    public class FontResolverInstallTests
    {
        [Fact]
        public void Install_MakesOurResolverTheActiveOne()
        {
            Assert.True(PdfFontResolver.Install());
            Assert.Same(PdfFontResolver.Instance, PdfSharpCore.Fonts.GlobalFontSettings.FontResolver);
        }

        [Fact]
        public void DrawnText_EmbedsTheBundledFace_NotAnArbitrarySystemFont()
        {
            // Regression: a "FontResolver is null" guard installed PdfSharpCore's default resolver,
            // so every bundled family burned in as whatever system font it fell back to.
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj"))) dir = dir.Parent;
            PdfFontResolver.Instance.RegisterBundledFont("Noto Sans",
                File.ReadAllBytes(Path.Combine(dir!.FullName, "Resources", "Fonts", "NotoSans-Regular.ttf")), false, false);
            PdfFontResolver.Install();

            var doc = new PdfDocument();
            using (var g = XGraphics.FromPdfPage(doc.AddPage()))
                g.DrawString("Hello", new XFont("Noto Sans", 12), XBrushes.Black, 50, 50);
            using var ms = new MemoryStream();
            doc.Save(ms);
            string raw = System.Text.Encoding.ASCII.GetString(ms.ToArray());

            // PDF names escape spaces: "/ABCDEF+Noto#20Sans#20Regular".
            Assert.Contains("+Noto#20Sans", raw);
        }
    }
}
