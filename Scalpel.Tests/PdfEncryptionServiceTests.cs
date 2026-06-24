using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Security;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")]
    public class PdfEncryptionServiceTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        private static string MakeBlankPdf(int pages = 2)
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_enc_in_{Guid.NewGuid():N}.pdf");
            using var doc = new PdfDocument();
            for (int i = 0; i < pages; i++) doc.AddPage();
            doc.Save(path);
            return path;
        }

        private static string Tmp() =>
            Path.Combine(Path.GetTempPath(), $"scalpel_enc_out_{Guid.NewGuid():N}.pdf");

        [Fact]
        public void Protect_ThenOpenWithoutPassword_Fails()
        {
            string input = MakeBlankPdf();
            string output = Tmp();
            try
            {
                PdfEncryptionService.Protect(input, output,
                    new EncryptionOptions { UserPassword = "secret" });
                Assert.ThrowsAny<Exception>(() =>
                    PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly));
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Protect_ThenOpenWithPassword_Succeeds_AndPreservesPages()
        {
            string input = MakeBlankPdf(3);
            string output = Tmp();
            try
            {
                PdfEncryptionService.Protect(input, output,
                    new EncryptionOptions { UserPassword = "secret" });
                using var doc = PdfReader.Open(output, "secret", PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(3, doc.PageCount);
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Protect_MarksFileEncrypted()
        {
            string input = MakeBlankPdf();
            string output = Tmp();
            try
            {
                Assert.False(PdfEncryptionService.IsEncrypted(input));
                PdfEncryptionService.Protect(input, output,
                    new EncryptionOptions { UserPassword = "pw" });
                Assert.True(PdfEncryptionService.IsEncrypted(output));
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void RemovePassword_ProducesFileOpenableWithoutPassword()
        {
            string input = MakeBlankPdf(2);
            string protectedPath = Tmp();
            string output = Tmp();
            try
            {
                PdfEncryptionService.Protect(input, protectedPath,
                    new EncryptionOptions { UserPassword = "secret" });
                PdfEncryptionService.RemovePassword(protectedPath, output, "secret");

                Assert.False(PdfEncryptionService.IsEncrypted(output));
                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(2, doc.PageCount);
            }
            finally { Cleanup(input, protectedPath, output); }
        }

        [Fact]
        public void Protect_DisallowPrint_IsReflectedInPermissions()
        {
            string input = MakeBlankPdf();
            string output = Tmp();
            try
            {
                PdfEncryptionService.Protect(input, output, new EncryptionOptions
                {
                    UserPassword = "u",
                    OwnerPassword = "o",
                    AllowPrint = false,
                });
                using var doc = PdfReader.Open(output, "o", PdfDocumentOpenMode.ReadOnly);
                Assert.False(doc.SecuritySettings.PermitPrint);
            }
            finally { Cleanup(input, output); }
        }

        private static void Cleanup(params string[] paths)
        {
            foreach (var p in paths)
                if (p != null && File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
