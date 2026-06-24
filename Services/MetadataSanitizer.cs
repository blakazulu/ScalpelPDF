using System;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Scalpel.Services
{
    /// <summary>Snapshot of a PDF's document-information metadata (for display + tests).</summary>
    public sealed class PdfMetadata
    {
        public string Author { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Keywords { get; set; } = "";
        public string Creator { get; set; } = "";
        public string Producer { get; set; } = "";
    }

    /// <summary>
    /// Removes identifying metadata from a PDF: the Document Information dictionary
    /// (author, title, subject, keywords, creator) and the XMP metadata stream.
    /// This is the "Sanitize Document" step that true redaction requires.
    /// Note: the PDF writer re-stamps /Creator and /Producer with a generic, non-identifying
    /// tool id on save — the user's original values are removed, which is what sanitize guarantees.
    /// </summary>
    public static class MetadataSanitizer
    {
        private static readonly string[] InfoKeys =
        {
            "/Author", "/Title", "/Subject", "/Keywords", "/Creator", "/Producer",
        };

        public static void Sanitize(PdfDocument doc)
        {
            if (doc is null) throw new ArgumentNullException(nameof(doc));

            // Clear the Document Information dictionary.
            foreach (var key in InfoKeys)
                doc.Info.Elements.Remove(key);

            // Remove the XMP metadata stream referenced by the catalog.
            doc.Internals.Catalog.Elements.Remove("/Metadata");
        }

        public static void SanitizeFile(string inputPath, string outputPath)
        {
            using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
            Sanitize(doc);
            doc.Save(outputPath);
        }

        public static PdfMetadata ReadMetadata(string path)
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
            return new PdfMetadata
            {
                Author = doc.Info.Author ?? "",
                Title = doc.Info.Title ?? "",
                Subject = doc.Info.Subject ?? "",
                Keywords = doc.Info.Keywords ?? "",
                Creator = doc.Info.Creator ?? "",
                Producer = doc.Info.Producer ?? "",
            };
        }
    }
}
