using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;
using DotNetX509 = System.Security.Cryptography.X509Certificates.X509Certificate2;

namespace Scalpel.Services
{
    /// <summary>
    /// Cryptographically signs a PDF with a PKCS#7 / CMS detached signature
    /// (PAdES-style, SubFilter <c>adbe.pkcs7.detached</c>, invisible signature).
    ///
    /// IMPORTANT — this is DISTINCT from Scalpel's drawn-ink "signatures". A real
    /// digital signature signs a <c>/ByteRange</c> of the file with the signature
    /// <c>/Contents</c> hole excluded, so the signed bytes must NOT change afterwards.
    /// PdfSharpCore re-serializes the whole document on Save (which would invalidate the
    /// signature), so we do NOT use it to write the signature. Instead we take the
    /// already-saved PDF bytes and append a classic <b>incremental update</b>
    /// (new objects + xref table + trailer with <c>/Prev</c>) by hand at the byte level.
    ///
    /// Pure, WPF-free, fully local — no online timestamp authority is contacted.
    /// </summary>
    /// <summary>
    /// Fetches an RFC-3161 timestamp token (DER) over the SHA-256 of the supplied data — typically a
    /// CMS signer's signature bytes. Implemented over HTTP against a TSA (<see cref="Signing.HttpTimestampClient"/>);
    /// injected so signing is testable without network access.
    /// </summary>
    public interface ITimestampClient
    {
        byte[] GetTimestampToken(byte[] data);
    }

    /// <summary>
    /// Optional visible appearance for a signature: a rectangle on the first page (PDF user space,
    /// bottom-left origin) and the text lines to draw inside it. When supplied to
    /// <see cref="PdfSigningService.SignBytes"/>, the signature widget becomes a visible field with an
    /// /AP appearance stream instead of the default invisible zero-rect. The appearance is cosmetic —
    /// it does not affect the cryptographic validity of the signature.
    /// </summary>
    public sealed class SignatureAppearance
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public bool ShowName { get; set; } = true;
        public bool ShowDate { get; set; } = true;
        public string? Reason { get; set; }
    }

    public static class PdfSigningService
    {
        // Latin-1 maps every byte 1:1 to a char, so string offsets == byte offsets.
        // This lets us parse/build the PDF structure as text while keeping exact byte math.
        private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

        // Reserved /Contents hole: 32 KiB of signature bytes -> 64 KiB of hex digits.
        // Generous for an RSA-2048/3072 CMS blob incl. cert chain AND an RFC-3161 timestamp
        // token (itself a small CMS with the TSA's cert), which adds several KiB.
        private const int ContentsReservedBytes = 32768;
        private const int ContentsHexChars = ContentsReservedBytes * 2;

        // Width of each dynamic /ByteRange field, zero-padded so length never shifts.
        private const int ByteRangeFieldWidth = 10;

        // ---- public entry points ----------------------------------------------------------------

        /// <summary>
        /// Loads a PFX/P12, signs <paramref name="inputPath"/> and writes the signed PDF to
        /// <paramref name="outputPath"/>. Defensive: throws a clear exception on bad password,
        /// missing private key, or an unsupported PDF structure.
        /// </summary>
        public static void SignFile(string inputPath, string outputPath, string pfxPath, string? pfxPassword,
            ITimestampClient? timestamp = null, SignatureAppearance? appearance = null)
        {
            if (string.IsNullOrEmpty(inputPath)) throw new ArgumentNullException(nameof(inputPath));
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));
            if (string.IsNullOrEmpty(pfxPath)) throw new ArgumentNullException(nameof(pfxPath));

            // Load the whole PFX so we can include the full certificate chain in the CMS.
            var collection = new X509Certificate2Collection();
            try
            {
                collection.Import(pfxPath, pfxPassword ?? "", X509KeyStorageFlags.Exportable);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Could not open the certificate. The password may be wrong, or the file is not a valid .pfx/.p12.", ex);
            }

            DotNetX509? signer = collection.Cast<DotNetX509>().FirstOrDefault(c => c.HasPrivateKey);
            if (signer is null)
                throw new InvalidOperationException("The certificate file does not contain a usable private key.");

            var chain = collection.Cast<DotNetX509>().Where(c => !ReferenceEquals(c, signer)).ToArray();

            byte[] pdf = File.ReadAllBytes(inputPath);
            byte[] signed = SignBytes(pdf, signer, chain, timestamp, appearance);
            File.WriteAllBytes(outputPath, signed);
        }

        /// <summary>
        /// Signs <paramref name="inputPath"/> with an already-acquired certificate (e.g. one selected
        /// from the Windows certificate store) and writes the signed PDF to <paramref name="outputPath"/>.
        /// Identical byte-level incremental-update signing as <see cref="SignFile"/>; only the source of
        /// the certificate differs (here it is supplied directly rather than loaded from a .pfx).
        /// </summary>
        public static void SignFileWithCertificate(string inputPath, string outputPath,
            DotNetX509 signer, IEnumerable<DotNetX509>? chain = null, ITimestampClient? timestamp = null,
            SignatureAppearance? appearance = null)
        {
            if (string.IsNullOrEmpty(inputPath)) throw new ArgumentNullException(nameof(inputPath));
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));
            if (signer is null) throw new ArgumentNullException(nameof(signer));
            if (!signer.HasPrivateKey)
                throw new InvalidOperationException("The selected certificate has no usable private key.");

            byte[] pdf = File.ReadAllBytes(inputPath);
            byte[] signed = SignBytes(pdf, signer, chain, timestamp, appearance);
            File.WriteAllBytes(outputPath, signed);
        }

        /// <summary>
        /// Produces a new PDF byte array equal to <paramref name="pdf"/> plus an appended
        /// incremental update carrying an invisible <c>adbe.pkcs7.detached</c> signature.
        /// </summary>
        /// <param name="pdf">An existing PDF with a classic <c>xref</c> table + trailer.</param>
        /// <param name="signerCertWithKey">Signing certificate (must carry the private key).</param>
        /// <param name="chain">Optional extra certificates (intermediates/root) to embed.</param>
        public static byte[] SignBytes(byte[] pdf, DotNetX509 signerCertWithKey, IEnumerable<DotNetX509>? chain = null,
            ITimestampClient? timestamp = null, SignatureAppearance? appearance = null)
        {
            if (pdf is null || pdf.Length == 0) throw new ArgumentException("Empty PDF.", nameof(pdf));
            if (signerCertWithKey is null) throw new ArgumentNullException(nameof(signerCertWithKey));
            if (!signerCertWithKey.HasPrivateKey)
                throw new InvalidOperationException("The signing certificate has no private key.");

            var info = ParsePdf(pdf);

            int size = info.Size;
            long prevStartxref = info.StartXref;

            // New object numbers (existing Catalog/Page are re-emitted under their own numbers).
            int sigDictNum = size;
            int sigFieldNum = size + 1;
            int acroFormNum = size + 2;
            // A visible signature needs one extra object: the /AP form XObject.
            int apXObjNum = appearance is not null ? size + 3 : -1;
            int newSize = appearance is not null ? size + 4 : size + 3;

            string utc = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string sigDate = "D:" + utc + "+00'00'";

            // ---- build the appended object block (placeholders for ByteRange + Contents) --------
            var sb = new StringBuilder();
            int baseLen = pdf.Length; // byte offset of the start of the object block

            int OffsetNow() => baseLen + sb.Length;

            // Track key offsets we must patch later.
            int sigDictOffset, sigFieldOffset, acroFormOffset, catalogOffset, pageOffset, annotsArrayOffset = -1;
            int byteRangeField1, byteRangeField2, byteRangeField3;
            int contentsOpenAngle, contentsCloseAngle;

            // 1) Signature dictionary (holds the CMS blob).
            sb.Append('\n'); // separate from prior content
            sigDictOffset = OffsetNow();
            sb.Append(sigDictNum).Append(" 0 obj\n");
            sb.Append("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached");
            sb.Append(" /M (").Append(sigDate).Append(')');
            sb.Append(" /ByteRange [0 ");
            byteRangeField1 = OffsetNow(); sb.Append(new string('0', ByteRangeFieldWidth)); sb.Append(' ');
            byteRangeField2 = OffsetNow(); sb.Append(new string('0', ByteRangeFieldWidth)); sb.Append(' ');
            byteRangeField3 = OffsetNow(); sb.Append(new string('0', ByteRangeFieldWidth)); sb.Append(']');
            sb.Append(" /Contents ");
            contentsOpenAngle = OffsetNow(); sb.Append('<');
            sb.Append(new string('0', ContentsHexChars));
            contentsCloseAngle = OffsetNow(); sb.Append('>');
            sb.Append(" >>\n");
            sb.Append("endobj\n");

            // 2) Signature field + widget (combined). Invisible by default (Rect [0 0 0 0], hidden flag);
            //    when an appearance is supplied, a real Rect on the first page + /AP /N form XObject and
            //    the Print flag (/F 4) instead.
            sb.Append('\n');
            sigFieldOffset = OffsetNow();
            sb.Append(sigFieldNum).Append(" 0 obj\n");
            sb.Append("<< /FT /Sig /Type /Annot /Subtype /Widget");
            if (appearance is not null)
            {
                sb.Append(" /Rect [").Append(F(appearance.X1)).Append(' ').Append(F(appearance.Y1)).Append(' ')
                  .Append(F(appearance.X2)).Append(' ').Append(F(appearance.Y2)).Append(']');
                sb.Append(" /F 4");
                sb.Append(" /AP << /N ").Append(apXObjNum).Append(" 0 R >>");
            }
            else
            {
                sb.Append(" /Rect [0 0 0 0] /F 132");
            }
            sb.Append(" /T (Signature1) /V ").Append(sigDictNum).Append(" 0 R");
            sb.Append(" /P ").Append(info.FirstPageNum).Append(" 0 R >>\n");
            sb.Append("endobj\n");

            // 3) AcroForm.
            sb.Append('\n');
            acroFormOffset = OffsetNow();
            sb.Append(acroFormNum).Append(" 0 obj\n");
            sb.Append("<< /Fields [").Append(sigFieldNum).Append(" 0 R] /SigFlags 3 >>\n");
            sb.Append("endobj\n");

            // 4) Updated Catalog (copy existing keys, drop any old /AcroForm, add ours).
            sb.Append('\n');
            catalogOffset = OffsetNow();
            string catalogBody = StripKey(info.RootDictBody, "/AcroForm");
            sb.Append(info.RootNum).Append(' ').Append(info.RootGen).Append(" obj\n");
            sb.Append("<< ").Append(catalogBody.Trim()).Append(" /AcroForm ").Append(acroFormNum).Append(" 0 R >>\n");
            sb.Append("endobj\n");

            // 5) Updated first Page (add the widget to /Annots), or an updated Annots array object.
            sb.Append('\n');
            pageOffset = OffsetNow();
            if (info.AnnotsArrayNum is int arrNum)
            {
                // Page already references an indirect /Annots array: re-emit it with the widget added.
                // The Page object itself is unchanged, so we don't strictly need to re-emit it, but we
                // re-emit the array under its own number.
                annotsArrayOffset = pageOffset;
                string body = info.AnnotsArrayBody!.Trim();
                int close = body.LastIndexOf(']');
                string inner = close >= 0 ? body.Substring(0, close) : body;
                sb.Append(arrNum).Append(' ').Append(info.AnnotsArrayGen).Append(" obj\n");
                sb.Append(inner.TrimEnd()).Append(' ').Append(sigFieldNum).Append(" 0 R]\n");
                sb.Append("endobj\n");
                pageOffset = -1; // page object not re-emitted in this branch
            }
            else
            {
                string pageBody = info.PageDictBody.Trim();
                string newPageBody;
                int annotsIdx = IndexOfKey(pageBody, "/Annots");
                if (annotsIdx < 0)
                {
                    newPageBody = pageBody + " /Annots [" + sigFieldNum + " 0 R]";
                }
                else
                {
                    // Inline array: insert before its closing ']'.
                    int open = pageBody.IndexOf('[', annotsIdx);
                    int close = open >= 0 ? pageBody.IndexOf(']', open) : -1;
                    if (open < 0 || close < 0)
                        throw new NotSupportedException("Unsupported /Annots layout on the first page.");
                    newPageBody = pageBody.Substring(0, close).TrimEnd()
                                + " " + sigFieldNum + " 0 R"
                                + pageBody.Substring(close);
                }
                sb.Append(info.FirstPageNum).Append(' ').Append(info.FirstPageGen).Append(" obj\n");
                sb.Append("<< ").Append(newPageBody).Append(" >>\n");
                sb.Append("endobj\n");
            }

            // 6) Optional visible-signature appearance: a Form XObject referenced by the widget's /AP /N.
            int apXObjOffset = -1;
            if (appearance is not null)
            {
                sb.Append('\n');
                apXObjOffset = OffsetNow();
                string content = BuildAppearanceStream(appearance, ComposeAppearanceLines(appearance, signerCertWithKey));
                double apW = appearance.X2 - appearance.X1, apH = appearance.Y2 - appearance.Y1;
                sb.Append(apXObjNum).Append(" 0 obj\n");
                sb.Append("<< /Type /XObject /Subtype /Form /FormType 1 /BBox [0 0 ")
                  .Append(F(apW)).Append(' ').Append(F(apH)).Append(']');
                sb.Append(" /Resources << /Font << /Helv << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>");
                sb.Append(" /Length ").Append(content.Length).Append(" >>\n");
                sb.Append("stream\n").Append(content).Append("\nendstream\n");
                sb.Append("endobj\n");
            }

            // ---- append the new xref section + trailer ------------------------------------------
            var xrefEntries = new List<(int num, int gen, int offset)>
            {
                (sigDictNum, 0, sigDictOffset),
                (sigFieldNum, 0, sigFieldOffset),
                (acroFormNum, 0, acroFormOffset),
                (info.RootNum, info.RootGen, catalogOffset),
            };
            if (annotsArrayOffset >= 0)
                xrefEntries.Add((info.AnnotsArrayNum!.Value, info.AnnotsArrayGen, annotsArrayOffset));
            else
                xrefEntries.Add((info.FirstPageNum, info.FirstPageGen, pageOffset));
            if (apXObjOffset >= 0)
                xrefEntries.Add((apXObjNum, 0, apXObjOffset));

            int xrefOffset = OffsetNow();
            sb.Append("xref\n");
            foreach (var (start, entries) in GroupConsecutive(xrefEntries))
            {
                sb.Append(start).Append(' ').Append(entries.Count).Append('\n');
                foreach (var e in entries)
                    sb.Append(e.offset.ToString("D10")).Append(' ').Append(e.gen.ToString("D5")).Append(" n\r\n");
            }
            sb.Append("trailer\n<< /Size ").Append(newSize)
              .Append(" /Root ").Append(info.RootNum).Append(' ').Append(info.RootGen).Append(" R");
            if (info.InfoRef is string infoRef)
                sb.Append(" /Info ").Append(infoRef);
            sb.Append(" /Prev ").Append(prevStartxref).Append(" >>\n");
            sb.Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");

            // ---- assemble the full file (still with placeholders) -------------------------------
            byte[] full = new byte[pdf.Length + sb.Length];
            Buffer.BlockCopy(pdf, 0, full, 0, pdf.Length);
            byte[] appended = Latin1.GetBytes(sb.ToString());
            Buffer.BlockCopy(appended, 0, full, pdf.Length, appended.Length);

            // ---- patch the real /ByteRange -------------------------------------------------------
            int contentsStart = contentsOpenAngle;
            int contentsLen = (contentsCloseAngle - contentsOpenAngle) + 1; // includes both angle brackets
            int[] byteRange = ComputeByteRange(contentsStart, contentsLen, full.Length);
            WriteFixed(full, byteRangeField1, byteRange[1]);
            WriteFixed(full, byteRangeField2, byteRange[2]);
            WriteFixed(full, byteRangeField3, byteRange[3]);

            // ---- hash the two ByteRange spans + produce the detached CMS -------------------------
            byte[] toSign = ConcatRanges(full, byteRange);
            byte[] cms = BuildDetachedCms(toSign, signerCertWithKey, chain, timestamp);
            if (cms.Length > ContentsReservedBytes)
                throw new InvalidOperationException(
                    $"Signature is larger ({cms.Length} bytes) than the reserved space ({ContentsReservedBytes}).");

            // ---- hex-encode into the /Contents hole (left-justified, zero-padded) ----------------
            // The hole is the hex digits strictly between '<' (contentsOpenAngle) and '>'.
            int hexStart = contentsOpenAngle + 1;
            WriteHex(full, hexStart, cms, ContentsHexChars);

            return full;
        }

        // ---- ByteRange math (pure + unit-testable) ----------------------------------------------

        /// <summary>
        /// Computes the PDF <c>/ByteRange</c> array for a detached signature, given the byte offset
        /// of the opening <c>&lt;</c> of <c>/Contents</c>, the length of the whole <c>&lt;...&gt;</c>
        /// token (including both angle brackets), and the total file length. The returned 4 numbers
        /// <c>[0 a b c]</c> describe the two signed spans <c>[0,a)</c> and <c>[b,b+c)</c>; the Contents
        /// token sits in the excluded gap <c>[a,b)</c>.
        /// </summary>
        public static int[] ComputeByteRange(int contentsStart, int contentsLen, int totalLen)
        {
            if (contentsStart < 0 || contentsLen <= 0 || totalLen < contentsStart + contentsLen)
                throw new ArgumentOutOfRangeException(nameof(contentsLen), "Invalid Contents window for ByteRange.");
            int afterContents = contentsStart + contentsLen;
            return new[] { 0, contentsStart, afterContents, totalLen - afterContents };
        }

        private static byte[] ConcatRanges(byte[] data, int[] byteRange)
        {
            // byteRange = [0, a, b, c] -> span1 = [0,a), span2 = [b, b+c)
            int len1 = byteRange[1];
            int start2 = byteRange[2];
            int len2 = byteRange[3];
            byte[] result = new byte[len1 + len2];
            Buffer.BlockCopy(data, 0, result, 0, len1);
            Buffer.BlockCopy(data, start2, result, len1, len2);
            return result;
        }

        // ---- CMS / PKCS#7 detached signature ----------------------------------------------------

        private static byte[] BuildDetachedCms(byte[] content, DotNetX509 signer, IEnumerable<DotNetX509>? chain,
            ITimestampClient? timestamp)
        {
            BcX509Certificate bcSigner = new X509CertificateParser().ReadCertificate(signer.RawData);

            var certList = new List<BcX509Certificate> { bcSigner };
            if (chain is not null)
                foreach (var c in chain)
                    certList.Add(new X509CertificateParser().ReadCertificate(c.RawData));

            var privateKey = ExtractBcPrivateKey(signer);

            // Signed attributes: signing-time (content-type + message-digest are added automatically).
            var signedAttrs = new Asn1EncodableVector
            {
                new Org.BouncyCastle.Asn1.Cms.Attribute(
                    CmsAttributes.SigningTime,
                    new DerSet(new Time(DateTime.UtcNow))),
            };
            var signedAttrTable = new AttributeTable(signedAttrs);

            var gen = new CmsSignedDataGenerator();
            gen.AddSigner(privateKey, bcSigner, CmsSignedGenerator.DigestSha256, signedAttrTable, null);
            gen.AddCertificates(CollectionUtilities.CreateStore(certList));

            CmsSignedData signed = gen.Generate(new CmsProcessableByteArray(content), false /* detached */);

            // Optional RFC-3161 trusted timestamp: attach a token over the signer's signature bytes as
            // the id-aa-signatureTimeStampToken unsigned attribute, so the signature proves *when* it was
            // made and stays verifiable after the signer certificate expires (PAdES-T).
            if (timestamp is not null)
                signed = AddTimestamp(signed, timestamp);

            // PAdES requires definite-length DER; GetEncoded() alone can emit indefinite-length BER.
            return signed.GetEncoded("DER");
        }

        // Re-issues each SignerInformation with an added id-aa-signatureTimeStampToken (1.2.840.113549.1.9.16.2.14)
        // unsigned attribute carrying the RFC-3161 token. Unsigned attributes are outside the signed digest, so
        // the original signature remains valid.
        private static CmsSignedData AddTimestamp(CmsSignedData signed, ITimestampClient timestamp)
        {
            var updated = new List<SignerInformation>();
            foreach (SignerInformation si in signed.GetSignerInfos().GetSigners())
            {
                byte[] token = timestamp.GetTimestampToken(si.GetSignature());
                var tsAttr = new Org.BouncyCastle.Asn1.Cms.Attribute(
                    PkcsObjectIdentifiers.IdAASignatureTimeStampToken,
                    new DerSet(Asn1Object.FromByteArray(token)));
                var unsigned = new AttributeTable(new Asn1EncodableVector { tsAttr });
                updated.Add(SignerInformation.ReplaceUnsignedAttributes(si, unsigned));
            }
            return CmsSignedData.ReplaceSigners(signed, new SignerInformationStore(updated));
        }

        // ---- visible-signature appearance helpers ----------------------------------------------

        private static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

        // Escapes a string for a PDF literal: (, ), \ are backslash-escaped; control chars and any
        // char outside Latin-1 (which the Helvetica base font can't render anyway) become spaces.
        private static string EscapePdfText(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '(' || c == ')' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32 || c > 255) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // Composes the visible-signature text lines from the signer certificate + options. English
        // labels are conventional for a burned-in digital-signature appearance (and locale-free, as
        // this service has no access to the UI string table); the user's Reason text is preserved.
        private static List<string> ComposeAppearanceLines(SignatureAppearance ap, DotNetX509 signer)
        {
            var lines = new List<string>();
            if (ap.ShowName) lines.Add("Digitally signed by " + CertCommonName(signer));
            if (ap.ShowDate) lines.Add("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            if (!string.IsNullOrWhiteSpace(ap.Reason)) lines.Add("Reason: " + ap.Reason!.Trim());
            if (lines.Count == 0) lines.Add("Digitally signed by " + CertCommonName(signer));
            return lines;
        }

        private static string CertCommonName(DotNetX509 cert)
        {
            string cn = cert.GetNameInfo(X509NameType.SimpleName, false);
            return string.IsNullOrWhiteSpace(cn) ? cert.Subject : cn;
        }

        // Builds the Form XObject content stream: a thin border plus the appearance text lines,
        // drawn top-down inside the BBox with the standard Helvetica font (/Helv).
        private static string BuildAppearanceStream(SignatureAppearance ap, IReadOnlyList<string> lines)
        {
            double w = Math.Max(1, ap.X2 - ap.X1), h = Math.Max(1, ap.Y2 - ap.Y1);
            int lineCount = Math.Max(1, lines.Count);
            double fontSize = Math.Max(6, Math.Min(11, (h - 6) / (lineCount + 0.5)));
            double leading = fontSize * 1.25;

            var s = new StringBuilder();
            s.Append("q\n");
            s.Append("0.7 0.7 0.7 RG 0.5 w\n");
            s.Append(F(0.5)).Append(' ').Append(F(0.5)).Append(' ')
             .Append(F(w - 1)).Append(' ').Append(F(h - 1)).Append(" re S\n");
            s.Append("0.1 0.1 0.1 rg\n");
            s.Append("BT\n");
            s.Append("/Helv ").Append(F(fontSize)).Append(" Tf\n");
            s.Append(F(leading)).Append(" TL\n");
            s.Append(F(3)).Append(' ').Append(F(h - leading)).Append(" Td\n");
            bool first = true;
            foreach (var line in lines)
            {
                if (!first) s.Append("T*\n");
                s.Append('(').Append(EscapePdfText(line ?? "")).Append(") Tj\n");
                first = false;
            }
            s.Append("ET\n");
            s.Append('Q');
            return s.ToString();
        }

        private static RsaPrivateCrtKeyParameters ExtractBcPrivateKey(DotNetX509 signer)
        {
            using RSA? rsa = signer.GetRSAPrivateKey();
            if (rsa is null)
                throw new InvalidOperationException("Only RSA signing keys are supported.");
            RSAParameters p = rsa.ExportParameters(true);
            return new RsaPrivateCrtKeyParameters(
                new BigInteger(1, p.Modulus),
                new BigInteger(1, p.Exponent),
                new BigInteger(1, p.D),
                new BigInteger(1, p.P),
                new BigInteger(1, p.Q),
                new BigInteger(1, p.DP),
                new BigInteger(1, p.DQ),
                new BigInteger(1, p.InverseQ));
        }

        // ---- PDF tail parsing -------------------------------------------------------------------

        private sealed class PdfInfo
        {
            public long StartXref;
            public int Size;
            public int RootNum;
            public int RootGen;
            public string RootDictBody = "";
            public int FirstPageNum;
            public int FirstPageGen;
            public string PageDictBody = "";
            public int? AnnotsArrayNum;
            public int AnnotsArrayGen;
            public string? AnnotsArrayBody;
            public string? InfoRef; // raw "N G R" if present
        }

        private static PdfInfo ParsePdf(byte[] pdf)
        {
            string text = Latin1.GetString(pdf);

            int sx = text.LastIndexOf("startxref", StringComparison.Ordinal);
            if (sx < 0) throw new NotSupportedException("PDF has no startxref — not a classic PDF.");
            long startXref = ReadLongAfter(text, sx + "startxref".Length);

            // Build a classic-xref offset map by following the /Prev chain.
            var offsets = new Dictionary<int, (int gen, int offset)>();
            string trailer = ReadXrefChain(text, (int)startXref, offsets);

            int size = ReadIntFromDict(trailer, "/Size")
                       ?? throw new NotSupportedException("Trailer has no /Size.");
            var (rootNum, rootGen) = ReadRefFromDict(trailer, "/Root")
                       ?? throw new NotSupportedException("Trailer has no /Root.");
            string? infoRef = ReadRawRefFromDict(trailer, "/Info");

            string rootBody = ReadDictBody(text, offsets, rootNum)
                       ?? throw new NotSupportedException("Could not read the document Catalog.");

            var info = new PdfInfo
            {
                StartXref = startXref,
                Size = size,
                RootNum = rootNum,
                RootGen = rootGen,
                RootDictBody = rootBody,
                InfoRef = infoRef,
            };

            // Descend the page tree to the first /Page.
            var (pagesNum, _) = ReadRefFromDict(rootBody, "/Pages")
                       ?? throw new NotSupportedException("Catalog has no /Pages.");
            var (firstPageNum, firstPageGen, firstPageBody) = FindFirstPage(text, offsets, pagesNum);

            info.FirstPageNum = firstPageNum;
            info.FirstPageGen = firstPageGen;
            info.PageDictBody = firstPageBody;

            // Detect an indirect /Annots array on the first page so we can extend it correctly.
            string? rawAnnots = ReadRawRefFromDict(firstPageBody, "/Annots");
            if (rawAnnots is not null)
            {
                var parsed = ParseRawRef(rawAnnots);
                if (parsed is (int an, int ag))
                {
                    string? arrBody = ReadObjectRaw(text, offsets, an);
                    if (arrBody is not null && arrBody.Contains('['))
                    {
                        info.AnnotsArrayNum = an;
                        info.AnnotsArrayGen = ag;
                        info.AnnotsArrayBody = arrBody;
                    }
                }
            }

            return info;
        }

        private static (int num, int gen, string body) FindFirstPage(
            string text, Dictionary<int, (int gen, int offset)> offsets, int node)
        {
            // Guard against cycles in malformed trees.
            for (int guard = 0; guard < 4096; guard++)
            {
                string body = ReadDictBody(text, offsets, node)
                    ?? throw new NotSupportedException($"Could not read page-tree object {node}.");
                bool isPage = ContainsTypeName(body, "/Page") && !ContainsTypeName(body, "/Pages");
                if (isPage)
                {
                    var (_, gen) = offsets.TryGetValue(node, out var e) ? (0, e.gen) : (0, 0);
                    return (node, gen, body);
                }
                var (firstKid, _) = ReadFirstKidRef(body)
                    ?? throw new NotSupportedException("Page tree has no /Kids.");
                node = firstKid;
            }
            throw new NotSupportedException("Page tree is too deep or cyclic.");
        }

        /// <summary>Follows the classic xref /Prev chain, filling <paramref name="offsets"/>
        /// (earlier sections only fill gaps). Returns the most-recent trailer dict body.</summary>
        private static string ReadXrefChain(string text, int xrefOffset, Dictionary<int, (int gen, int offset)> offsets)
        {
            string? topTrailer = null;
            var seen = new HashSet<int>();
            int? cur = xrefOffset;
            while (cur is int off && seen.Add(off))
            {
                string trailer = ReadXrefSection(text, off, offsets);
                topTrailer ??= trailer;
                int? prev = ReadIntFromDict(trailer, "/Prev");
                cur = prev;
            }
            if (topTrailer is null)
                throw new NotSupportedException("PDF lacks a classic xref table/trailer.");
            return topTrailer;
        }

        private static string ReadXrefSection(string text, int offset, Dictionary<int, (int gen, int offset)> offsets)
        {
            int i = offset;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (!text.Substring(i, Math.Min(4, text.Length - i)).StartsWith("xref"))
                throw new NotSupportedException("PDF uses a cross-reference stream, not a classic xref table.");
            i += 4;

            // Parse subsections until we hit "trailer".
            while (true)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (text.Substring(i, Math.Min(7, text.Length - i)).StartsWith("trailer"))
                {
                    i += 7;
                    break;
                }
                // subsection header: "<start> <count>"
                int start = ReadInt(text, ref i);
                int count = ReadInt(text, ref i);
                // Skip to start of first entry (after the EOL of the header line).
                while (i < text.Length && (text[i] == ' ' || text[i] == '\r' || text[i] == '\n')) i++;
                for (int k = 0; k < count; k++)
                {
                    // Each entry: 10-digit offset, space, 5-digit gen, space, type, 2-byte EOL.
                    if (i + 18 > text.Length) throw new NotSupportedException("Truncated xref entry.");
                    string entry = text.Substring(i, 18);
                    i += 20; // fixed 20-byte records
                    if (!int.TryParse(entry.Substring(0, 10), out int eoff)) continue;
                    if (!int.TryParse(entry.Substring(11, 5), out int egen)) continue;
                    char type = entry[17];
                    int objNum = start + k;
                    if (type == 'n' && !offsets.ContainsKey(objNum))
                        offsets[objNum] = (egen, eoff);
                }
            }

            // The trailer dict follows.
            int ds = text.IndexOf("<<", i, StringComparison.Ordinal);
            if (ds < 0) throw new NotSupportedException("Trailer dictionary not found.");
            return ExtractDictAt(text, ds);
        }

        /// <summary>Reads object <paramref name="num"/> via its xref offset and returns the body
        /// between the outermost &lt;&lt; and &gt;&gt;.</summary>
        private static string? ReadDictBody(string text, Dictionary<int, (int gen, int offset)> offsets, int num)
        {
            string? raw = ReadObjectRaw(text, offsets, num);
            if (raw is null) return null;
            int ds = raw.IndexOf("<<", StringComparison.Ordinal);
            if (ds < 0) return null;
            string dict = ExtractDictAt(raw, ds);
            // Strip the outer << >> to return just the inner body.
            return dict.Substring(2, dict.Length - 4);
        }

        /// <summary>Returns the raw content of object <paramref name="num"/> between
        /// <c>obj</c> and <c>endobj</c>.</summary>
        private static string? ReadObjectRaw(string text, Dictionary<int, (int gen, int offset)> offsets, int num)
        {
            if (!offsets.TryGetValue(num, out var e)) return null;
            int i = e.offset;
            if (i < 0 || i >= text.Length) return null;
            int objKw = text.IndexOf("obj", i, StringComparison.Ordinal);
            if (objKw < 0) return null;
            int bodyStart = objKw + 3;
            int endObj = text.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
            if (endObj < 0) return null;
            return text.Substring(bodyStart, endObj - bodyStart);
        }

        // ---- tiny PDF token helpers -------------------------------------------------------------

        /// <summary>Extracts a balanced <c>&lt;&lt; ... &gt;&gt;</c> dictionary string starting at
        /// <paramref name="start"/> (which must point at the opening <c>&lt;&lt;</c>).</summary>
        private static string ExtractDictAt(string s, int start)
        {
            int depth = 0;
            int i = start;
            while (i < s.Length - 1)
            {
                if (s[i] == '<' && s[i + 1] == '<') { depth++; i += 2; continue; }
                if (s[i] == '>' && s[i + 1] == '>')
                {
                    depth--; i += 2;
                    if (depth == 0) return s.Substring(start, i - start);
                    continue;
                }
                // Skip literal strings so '<<'/'>>' inside them don't confuse depth tracking.
                if (s[i] == '(')
                {
                    int pdepth = 1; i++;
                    while (i < s.Length && pdepth > 0)
                    {
                        if (s[i] == '\\') { i += 2; continue; }
                        if (s[i] == '(') pdepth++;
                        else if (s[i] == ')') pdepth--;
                        i++;
                    }
                    continue;
                }
                i++;
            }
            throw new NotSupportedException("Unbalanced dictionary in PDF.");
        }

        private static int IndexOfKey(string body, string key)
            => body.IndexOf(key, StringComparison.Ordinal);

        /// <summary>Removes a <c>/Key value</c> entry (value = ref, name, number, or array) from a
        /// dictionary body. Best-effort; used to drop a pre-existing /AcroForm before re-adding.</summary>
        private static string StripKey(string body, string key)
        {
            int k = body.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return body;
            int i = k + key.Length;
            // Skip whitespace.
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i < body.Length && body[i] == '[')
            {
                int close = body.IndexOf(']', i);
                i = close < 0 ? body.Length : close + 1;
            }
            else if (i + 1 < body.Length && body[i] == '<' && body[i + 1] == '<')
            {
                string dict = ExtractDictAt(body, i);
                i += dict.Length;
            }
            else
            {
                // ref "N G R", a name, or a number — consume up to the next '/' or end.
                int next = body.IndexOf('/', i);
                i = next < 0 ? body.Length : next;
            }
            return (body.Substring(0, k) + body.Substring(i)).Trim();
        }

        private static bool ContainsTypeName(string body, string typeName)
        {
            int k = body.IndexOf("/Type", StringComparison.Ordinal);
            if (k < 0) return false;
            int i = k + 5;
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            // Read the name token.
            if (i >= body.Length || body[i] != '/') return false;
            int j = i + 1;
            while (j < body.Length && !IsDelimiter(body[j]) && !char.IsWhiteSpace(body[j])) j++;
            return ("/" + body.Substring(i + 1, j - (i + 1))) == typeName;
        }

        private static (int num, int gen)? ReadFirstKidRef(string body)
        {
            int k = body.IndexOf("/Kids", StringComparison.Ordinal);
            if (k < 0) return null;
            int open = body.IndexOf('[', k);
            if (open < 0) return null;
            int i = open + 1;
            return ReadRefAt(body, ref i);
        }

        private static int? ReadIntFromDict(string dict, string key)
        {
            int k = dict.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return null;
            int i = k + key.Length;
            while (i < dict.Length && (char.IsWhiteSpace(dict[i]))) i++;
            int sign = 1;
            if (i < dict.Length && (dict[i] == '+' || dict[i] == '-')) { if (dict[i] == '-') sign = -1; i++; }
            int start = i;
            while (i < dict.Length && char.IsDigit(dict[i])) i++;
            if (i == start) return null;
            return sign * int.Parse(dict.Substring(start, i - start));
        }

        private static (int num, int gen)? ReadRefFromDict(string dict, string key)
        {
            int k = dict.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return null;
            int i = k + key.Length;
            return ReadRefAt(dict, ref i);
        }

        private static string? ReadRawRefFromDict(string dict, string key)
        {
            int k = dict.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return null;
            int i = k + key.Length;
            int save = i;
            var r = ReadRefAt(dict, ref i);
            if (r is null) return null;
            return $"{r.Value.num} {r.Value.gen} R";
        }

        private static (int, int)? ParseRawRef(string raw)
        {
            var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && int.TryParse(parts[0], out int n) && int.TryParse(parts[1], out int g))
                return (n, g);
            return null;
        }

        /// <summary>Reads a "N G R" indirect reference starting at <paramref name="i"/>
        /// (skipping leading whitespace). Advances <paramref name="i"/> past it.</summary>
        private static (int num, int gen)? ReadRefAt(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int n = TryReadInt(s, ref i);
            if (n < 0) return null;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int g = TryReadInt(s, ref i);
            if (g < 0) return null;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length && s[i] == 'R') { i++; return (n, g); }
            return null;
        }

        private static int TryReadInt(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == start) return -1;
            return int.Parse(s.Substring(start, i - start));
        }

        private static int ReadInt(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == start) throw new NotSupportedException("Expected an integer in xref.");
            return int.Parse(s.Substring(start, i - start));
        }

        private static long ReadLongAfter(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            int start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == start) throw new NotSupportedException("startxref offset missing.");
            return long.Parse(s.Substring(start, i - start));
        }

        private static bool IsDelimiter(char c)
            => c is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

        // ---- byte patching ----------------------------------------------------------------------

        private static void WriteFixed(byte[] buf, int offset, int value)
        {
            string s = value.ToString("D" + ByteRangeFieldWidth);
            if (s.Length > ByteRangeFieldWidth)
                throw new InvalidOperationException("ByteRange offset exceeds reserved field width.");
            for (int k = 0; k < ByteRangeFieldWidth; k++)
                buf[offset + k] = (byte)s[k];
        }

        private static void WriteHex(byte[] buf, int offset, byte[] data, int hexFieldLen)
        {
            const string hexDigits = "0123456789abcdef";
            int n = data.Length * 2;
            for (int k = 0; k < data.Length; k++)
            {
                buf[offset + k * 2] = (byte)hexDigits[(data[k] >> 4) & 0xF];
                buf[offset + k * 2 + 1] = (byte)hexDigits[data[k] & 0xF];
            }
            for (int k = n; k < hexFieldLen; k++)
                buf[offset + k] = (byte)'0';
        }

        // ---- xref grouping ----------------------------------------------------------------------

        private static IEnumerable<(int start, List<(int num, int gen, int offset)> entries)> GroupConsecutive(
            List<(int num, int gen, int offset)> items)
        {
            var sorted = items.OrderBy(e => e.num).ToList();
            int idx = 0;
            while (idx < sorted.Count)
            {
                int start = sorted[idx].num;
                var group = new List<(int, int, int)> { sorted[idx] };
                int prev = sorted[idx].num;
                idx++;
                while (idx < sorted.Count && sorted[idx].num == prev + 1)
                {
                    group.Add(sorted[idx]);
                    prev = sorted[idx].num;
                    idx++;
                }
                yield return (start, group);
            }
        }
    }
}
