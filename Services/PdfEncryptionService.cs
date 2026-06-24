using System;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Security;

namespace Scalpel.Services
{
    /// <summary>Permission flags applied when password-protecting a PDF.</summary>
    public sealed class EncryptionOptions
    {
        /// <summary>Password required to open/view the document. Empty = no open password.</summary>
        public string UserPassword { get; set; } = "";
        /// <summary>Owner/permissions password. If empty, falls back to the user password.</summary>
        public string OwnerPassword { get; set; } = "";
        public bool AllowPrint { get; set; } = true;
        public bool AllowCopy { get; set; } = true;
        public bool AllowModify { get; set; } = true;
        public bool AllowAnnotate { get; set; } = true;
    }

    /// <summary>
    /// Encrypts a PDF with a password (and permission flags) or removes a known password.
    /// Pure PdfSharpCore — fully local, no online service.
    /// </summary>
    public static class PdfEncryptionService
    {
        public static void Protect(string inputPath, string outputPath, EncryptionOptions opts)
        {
            if (opts is null) throw new ArgumentNullException(nameof(opts));

            using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
            var sec = doc.SecuritySettings;
            sec.DocumentSecurityLevel = PdfDocumentSecurityLevel.Encrypted128Bit;
            sec.UserPassword = opts.UserPassword ?? "";
            sec.OwnerPassword = string.IsNullOrEmpty(opts.OwnerPassword)
                ? (opts.UserPassword ?? "")
                : opts.OwnerPassword;

            sec.PermitPrint = opts.AllowPrint;
            sec.PermitFullQualityPrint = opts.AllowPrint;
            sec.PermitExtractContent = opts.AllowCopy;
            sec.PermitModifyDocument = opts.AllowModify;
            sec.PermitAssembleDocument = opts.AllowModify;
            sec.PermitAnnotations = opts.AllowAnnotate;
            sec.PermitFormsFill = opts.AllowAnnotate;

            doc.Save(outputPath);
        }

        public static void RemovePassword(string inputPath, string outputPath, string password)
        {
            using var doc = PdfReader.Open(inputPath, password, PdfDocumentOpenMode.Modify);
            doc.SecuritySettings.DocumentSecurityLevel = PdfDocumentSecurityLevel.None;
            doc.Save(outputPath);
        }

        public static bool IsEncrypted(string path)
        {
            try
            {
                using var doc = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
                return doc.SecuritySettings.DocumentSecurityLevel != PdfDocumentSecurityLevel.None;
            }
            catch
            {
                // A password-required failure here means the file is encrypted.
                return true;
            }
        }
    }
}
