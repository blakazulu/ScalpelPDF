using System;
using System.IO;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")]
    public class MetadataSanitizerTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        private static string MakePdfWithMetadata()
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_meta_in_{Guid.NewGuid():N}.pdf");
            using var doc = new PdfDocument();
            doc.AddPage();
            doc.Info.Author = "John Doe";
            doc.Info.Title = "Confidential Report";
            doc.Info.Subject = "Internal";
            doc.Info.Keywords = "secret,private";
            doc.Info.Creator = "AcmeApp 1.0";
            doc.Save(path);
            return path;
        }

        private static string Tmp() =>
            Path.Combine(Path.GetTempPath(), $"scalpel_meta_out_{Guid.NewGuid():N}.pdf");

        [Fact]
        public void ReadMetadata_ReturnsAuthorAndTitle()
        {
            string input = MakePdfWithMetadata();
            try
            {
                var meta = MetadataSanitizer.ReadMetadata(input);
                Assert.Equal("John Doe", meta.Author);
                Assert.Equal("Confidential Report", meta.Title);
            }
            finally { Cleanup(input); }
        }

        [Fact]
        public void Sanitize_ClearsIdentifyingFields()
        {
            string input = MakePdfWithMetadata();
            string output = Tmp();
            try
            {
                MetadataSanitizer.SanitizeFile(input, output);
                var meta = MetadataSanitizer.ReadMetadata(output);
                Assert.Equal("", meta.Author);
                Assert.Equal("", meta.Title);
                Assert.Equal("", meta.Subject);
                Assert.Equal("", meta.Keywords);
                // Creator/Producer are re-stamped by the PDF writer with a generic, non-identifying
                // tool id on save; what matters is the user's original value is gone.
                Assert.DoesNotContain("AcmeApp", meta.Creator);
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Sanitize_RemovesXmpMetadataStream()
        {
            EnsureResolver();
            string input = Path.Combine(Path.GetTempPath(), $"scalpel_meta_xmp_{Guid.NewGuid():N}.pdf");
            string output = Tmp();
            try
            {
                // Inject an XMP metadata stream into the catalog.
                using (var doc = new PdfDocument())
                {
                    doc.AddPage();
                    var xmp = new PdfDictionary(doc);
                    xmp.Elements["/Type"] = new PdfName("/Metadata");
                    xmp.Elements["/Subtype"] = new PdfName("/XML");
                    doc.Internals.AddObject(xmp);
                    doc.Internals.Catalog.Elements["/Metadata"] = xmp.Reference;
                    doc.Save(input);
                }

                MetadataSanitizer.SanitizeFile(input, output);

                using var outDoc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.False(outDoc.Internals.Catalog.Elements.ContainsKey("/Metadata"),
                    "XMP /Metadata stream should be removed from the catalog");
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Sanitize_PreservesPageCount()
        {
            EnsureResolver();
            string input = Path.Combine(Path.GetTempPath(), $"scalpel_meta_pc_{Guid.NewGuid():N}.pdf");
            string output = Tmp();
            try
            {
                using (var doc = new PdfDocument())
                {
                    doc.AddPage(); doc.AddPage(); doc.AddPage();
                    doc.Info.Author = "X";
                    doc.Save(input);
                }
                MetadataSanitizer.SanitizeFile(input, output);
                using var outDoc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(3, outDoc.PageCount);
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
