using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;
using DotNetX509 = System.Security.Cryptography.X509Certificates.X509Certificate2;

namespace Scalpel.Tests
{
    [Collection("FontResolver")] // PdfSharpCore GlobalFontSettings is process-global state
    public class PdfSigningServiceTests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        private static byte[] MakeBlankPdf(int pages)
        {
            EnsureResolver();
            using var doc = new PdfDocument();
            for (int i = 0; i < pages; i++)
            {
                var page = doc.AddPage();
                page.Width = PdfSharpCore.Drawing.XUnit.FromPoint(612);
                page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(792);
            }
            using var ms = new MemoryStream();
            doc.Save(ms, closeStream: false);
            return ms.ToArray();
        }

        private static DotNetX509 MakeSelfSignedCert()
        {
            var rng = new SecureRandom();
            var kpGen = new RsaKeyPairGenerator();
            kpGen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(rng, 2048));
            AsymmetricCipherKeyPair kp = kpGen.GenerateKeyPair();

            var certGen = new X509V3CertificateGenerator();
            BigInteger serial = BigIntegers.CreateRandomInRange(
                BigInteger.One, BigInteger.ValueOf(long.MaxValue), rng);
            certGen.SetSerialNumber(serial);
            var dn = new X509Name("CN=Scalpel Test Signer, O=Scalpel");
            certGen.SetIssuerDN(dn);
            certGen.SetSubjectDN(dn);
            certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(2));
            certGen.SetPublicKey(kp.Public);

            var sigFactory = new Asn1SignatureFactory("SHA256WITHRSA", kp.Private, rng);
            BcX509Certificate bcCert = certGen.Generate(sigFactory);

            var store = new Pkcs12StoreBuilder().Build();
            var certEntry = new X509CertificateEntry(bcCert);
            store.SetKeyEntry("key", new AsymmetricKeyEntry(kp.Private), new[] { certEntry });
            using var ms = new MemoryStream();
            store.Save(ms, "pw".ToCharArray(), rng);
            return new DotNetX509(ms.ToArray(), "pw", X509KeyStorageFlags.Exportable);
        }

        [Fact]
        public void ComputeByteRange_ExcludesContentsWindow()
        {
            // Contents token '<...>' starts at byte 100 and spans 50 bytes; file is 1000 bytes.
            int[] br = PdfSigningService.ComputeByteRange(100, 50, 1000);
            Assert.Equal(new[] { 0, 100, 150, 850 }, br);
            // The two signed spans plus the excluded gap must exactly tile the file.
            Assert.Equal(1000, br[1] + 50 + br[3]);
        }

        [Fact]
        public void SignBytes_ProducesParsablePdf_WithDetachedSignature_AndVerifiableCms()
        {
            byte[] pdf = MakeBlankPdf(2);
            DotNetX509 cert = MakeSelfSignedCert();

            byte[] signed = PdfSigningService.SignBytes(pdf, cert);

            // 1) Output grew and still opens with the same page count.
            Assert.True(signed.Length > pdf.Length, "signed PDF should be larger than the input");
            string outPath = Path.Combine(Path.GetTempPath(), $"scalpel_sign_{Guid.NewGuid():N}.pdf");
            try
            {
                File.WriteAllBytes(outPath, signed);
                using (var reopened = PdfReader.Open(outPath, PdfDocumentOpenMode.ReadOnly))
                    Assert.Equal(2, reopened.PageCount);
                using (var pig = UglyToad.PdfPig.PdfDocument.Open(outPath))
                    Assert.Equal(2, pig.NumberOfPages);
            }
            finally { try { File.Delete(outPath); } catch { } }

            string text = Latin1.GetString(signed);

            // 2) It is a PAdES detached signature with a ByteRange.
            Assert.Contains("/SubFilter /adbe.pkcs7.detached", text);
            Assert.Contains("/ByteRange", text);

            // 3) Extract the real ByteRange and the CMS blob, then cryptographically verify.
            int[] byteRange = ParseByteRange(text);
            byte[] signedContent = ConcatRanges(signed, byteRange);
            byte[] cms = ExtractContentsDer(signed);

            var cmsData = new CmsSignedData(new CmsProcessableByteArray(signedContent), cms);
            BcX509Certificate bcSigner = new X509CertificateParser().ReadCertificate(cert.RawData);

            var signers = cmsData.GetSignerInfos().GetSigners().Cast<SignerInformation>().ToList();
            Assert.Single(signers);
            SignerInformation si = signers[0];
            Assert.True(si.Verify(bcSigner), "the embedded CMS must verify against the signer certificate");

            // 4) The signed message-digest attribute equals SHA-256 over the ByteRange spans.
            byte[] expectedDigest = SHA256.Create().ComputeHash(signedContent);
            var mdAttr = si.SignedAttributes[CmsAttributes.MessageDigest];
            byte[] actualDigest = ((Asn1OctetString)mdAttr.AttrValues[0]).GetOctets();
            Assert.Equal(expectedDigest, actualDigest);
        }

        [Fact]
        public void SignFile_RoundTrips_FromPfxOnDisk()
        {
            byte[] pdf = MakeBlankPdf(1);
            DotNetX509 cert = MakeSelfSignedCert();
            byte[] pfx = cert.Export(X509ContentType.Pfx, "pw");

            string pfxPath = Path.Combine(Path.GetTempPath(), $"scalpel_signer_{Guid.NewGuid():N}.pfx");
            string inPath = Path.Combine(Path.GetTempPath(), $"scalpel_in_{Guid.NewGuid():N}.pdf");
            string outPath = Path.Combine(Path.GetTempPath(), $"scalpel_out_{Guid.NewGuid():N}.pdf");
            try
            {
                File.WriteAllBytes(pfxPath, pfx);
                File.WriteAllBytes(inPath, pdf);

                PdfSigningService.SignFile(inPath, outPath, pfxPath, "pw");

                Assert.True(File.Exists(outPath));
                string text = Latin1.GetString(File.ReadAllBytes(outPath));
                Assert.Contains("/adbe.pkcs7.detached", text);
                using var reopened = PdfReader.Open(outPath, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(1, reopened.PageCount);
            }
            finally
            {
                foreach (var p in new[] { pfxPath, inPath, outPath })
                    try { File.Delete(p); } catch { }
            }
        }

        // A self-contained RFC-3161 timestamp authority used to test timestamping without network.
        private sealed class FakeTsaClient : Scalpel.Services.ITimestampClient
        {
            private readonly AsymmetricCipherKeyPair _kp;
            private readonly BcX509Certificate _cert;

            public FakeTsaClient()
            {
                var rng = new SecureRandom();
                var kpGen = new RsaKeyPairGenerator();
                kpGen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(rng, 2048));
                _kp = kpGen.GenerateKeyPair();

                var certGen = new X509V3CertificateGenerator();
                certGen.SetSerialNumber(BigIntegers.CreateRandomInRange(
                    BigInteger.One, BigInteger.ValueOf(long.MaxValue), rng));
                var dn = new X509Name("CN=Scalpel Test TSA, O=Scalpel");
                certGen.SetIssuerDN(dn);
                certGen.SetSubjectDN(dn);
                certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
                certGen.SetNotAfter(DateTime.UtcNow.AddYears(2));
                certGen.SetPublicKey(_kp.Public);
                certGen.AddExtension(X509Extensions.ExtendedKeyUsage, true,
                    new ExtendedKeyUsage(KeyPurposeID.IdKPTimeStamping));
                _cert = certGen.Generate(new Asn1SignatureFactory("SHA256WITHRSA", _kp.Private, rng));
            }

            public byte[] GetTimestampToken(byte[] data)
            {
                byte[] digest = SHA256.Create().ComputeHash(data);
                var reqGen = new TimeStampRequestGenerator();
                reqGen.SetCertReq(true);
                TimeStampRequest req = reqGen.Generate(TspAlgorithms.Sha256, digest, BigInteger.ValueOf(1));

                var tokenGen = new TimeStampTokenGenerator(_kp.Private, _cert, TspAlgorithms.Sha256, "1.2.3.4.1");
                tokenGen.SetCertificates(Org.BouncyCastle.Utilities.Collections.CollectionUtilities.CreateStore(
                    new System.Collections.Generic.List<BcX509Certificate> { _cert }));
                var respGen = new TimeStampResponseGenerator(tokenGen, TspAlgorithms.Allowed);
                TimeStampResponse resp = respGen.Generate(req, BigInteger.ValueOf(1), DateTime.UtcNow);
                return resp.TimeStampToken.GetEncoded();
            }
        }

        [Fact]
        public void SignBytes_WithTimestamp_AttachesTokenAndStillVerifies()
        {
            byte[] pdf = MakeBlankPdf(1);
            DotNetX509 cert = MakeSelfSignedCert();

            byte[] signed = PdfSigningService.SignBytes(pdf, cert, null, new FakeTsaClient());

            string text = Latin1.GetString(signed);
            int[] byteRange = ParseByteRange(text);
            byte[] signedContent = ConcatRanges(signed, byteRange);
            byte[] cms = ExtractContentsDer(signed);

            var cmsData = new CmsSignedData(new CmsProcessableByteArray(signedContent), cms);
            SignerInformation si = cmsData.GetSignerInfos().GetSigners().Cast<SignerInformation>().Single();

            // The original signature still verifies (unsigned attrs are outside the signed digest).
            BcX509Certificate bcSigner = new X509CertificateParser().ReadCertificate(cert.RawData);
            Assert.True(si.Verify(bcSigner), "signature must still verify after timestamping");

            // The id-aa-signatureTimeStampToken unsigned attribute is present and parses as a token.
            Assert.NotNull(si.UnsignedAttributes);
            var tsAttr = si.UnsignedAttributes[PkcsObjectIdentifiers.IdAASignatureTimeStampToken];
            Assert.NotNull(tsAttr);
            byte[] tokenDer = tsAttr.AttrValues[0].ToAsn1Object().GetEncoded();
            var tst = new TimeStampToken(new CmsSignedData(tokenDer));
            Assert.NotNull(tst.TimeStampInfo);
        }

        [Fact]
        public void SignBytes_WithVisibleAppearance_EmitsApStream_AndStillVerifies()
        {
            byte[] pdf = MakeBlankPdf(1);
            DotNetX509 cert = MakeSelfSignedCert();
            var ap = new SignatureAppearance
            {
                X1 = 360, Y1 = 36, X2 = 560, Y2 = 100,
                ShowName = true, ShowDate = true, Reason = "I approve this document",
            };

            byte[] signed = PdfSigningService.SignBytes(pdf, cert, null, null, ap);

            string text = Latin1.GetString(signed);
            Assert.Contains("/Subtype /Form", text);            // appearance XObject present
            Assert.Contains("/AP << /N", text);                 // widget references it
            Assert.Contains("/Rect [360", text);                // a real (non-zero) rect
            Assert.DoesNotContain("/Rect [0 0 0 0]", text);     // not the invisible default
            Assert.Contains("Digitally signed by", text);       // composed appearance text
            Assert.Contains("Reason: I approve this document", text);

            // Re-opens with the page intact and the signature still verifies.
            string outPath = Path.Combine(Path.GetTempPath(), $"scalpel_apsign_{Guid.NewGuid():N}.pdf");
            try
            {
                File.WriteAllBytes(outPath, signed);
                using var reopened = PdfReader.Open(outPath, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(1, reopened.PageCount);
            }
            finally { try { File.Delete(outPath); } catch { } }

            int[] byteRange = ParseByteRange(text);
            byte[] signedContent = ConcatRanges(signed, byteRange);
            byte[] cms = ExtractContentsDer(signed);
            var cmsData = new CmsSignedData(new CmsProcessableByteArray(signedContent), cms);
            SignerInformation si = cmsData.GetSignerInfos().GetSigners().Cast<SignerInformation>().Single();
            BcX509Certificate bcSigner = new X509CertificateParser().ReadCertificate(cert.RawData);
            Assert.True(si.Verify(bcSigner), "signature must still verify with a visible appearance");
        }

        [Fact]
        public void SignFileWithCertificate_SignsUsingInMemoryCert()
        {
            // Mirrors how a Windows-cert-store certificate reaches the signer: an in-memory
            // X509Certificate2 with a private key, no .pfx file on disk.
            byte[] pdf = MakeBlankPdf(1);
            DotNetX509 cert = MakeSelfSignedCert();
            string inPath = Path.Combine(Path.GetTempPath(), $"scalpel_in_{Guid.NewGuid():N}.pdf");
            string outPath = Path.Combine(Path.GetTempPath(), $"scalpel_out_{Guid.NewGuid():N}.pdf");
            try
            {
                File.WriteAllBytes(inPath, pdf);
                PdfSigningService.SignFileWithCertificate(inPath, outPath, cert);

                Assert.True(File.Exists(outPath));
                string text = Latin1.GetString(File.ReadAllBytes(outPath));
                Assert.Contains("/adbe.pkcs7.detached", text);
                using var reopened = PdfReader.Open(outPath, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(1, reopened.PageCount);
            }
            finally
            {
                foreach (var p in new[] { inPath, outPath }) try { File.Delete(p); } catch { }
            }
        }

        [Fact]
        public void WindowsCertificateStore_Describe_IncludesSubjectAndExpiry()
        {
            DotNetX509 cert = MakeSelfSignedCert();
            string label = Scalpel.Services.Signing.WindowsCertificateStore.Describe(cert);
            Assert.Contains("Scalpel Test Signer", label);
            Assert.Contains(cert.NotAfter.ToString("yyyy-MM-dd"), label);
            Assert.Contains("—", label); // subject — issuer — expiry
        }

        [Fact]
        public void SignFile_WrongPassword_ThrowsClearError()
        {
            DotNetX509 cert = MakeSelfSignedCert();
            byte[] pfx = cert.Export(X509ContentType.Pfx, "pw");
            string pfxPath = Path.Combine(Path.GetTempPath(), $"scalpel_signer_{Guid.NewGuid():N}.pfx");
            string inPath = Path.Combine(Path.GetTempPath(), $"scalpel_in_{Guid.NewGuid():N}.pdf");
            string outPath = Path.Combine(Path.GetTempPath(), $"scalpel_out_{Guid.NewGuid():N}.pdf");
            try
            {
                File.WriteAllBytes(pfxPath, pfx);
                File.WriteAllBytes(inPath, MakeBlankPdf(1));
                Assert.ThrowsAny<Exception>(() =>
                    PdfSigningService.SignFile(inPath, outPath, pfxPath, "wrong-password"));
            }
            finally
            {
                foreach (var p in new[] { pfxPath, inPath, outPath })
                    try { File.Delete(p); } catch { }
            }
        }

        // ---- helpers ----------------------------------------------------------------------------

        private static int[] ParseByteRange(string text)
        {
            int k = text.IndexOf("/ByteRange", StringComparison.Ordinal);
            int open = text.IndexOf('[', k);
            int close = text.IndexOf(']', open);
            string inner = text.Substring(open + 1, close - open - 1);
            int[] nums = inner.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(int.Parse).ToArray();
            Assert.Equal(4, nums.Length);
            return nums;
        }

        private static byte[] ConcatRanges(byte[] data, int[] byteRange)
        {
            int len1 = byteRange[1];
            int start2 = byteRange[2];
            int len2 = byteRange[3];
            byte[] result = new byte[len1 + len2];
            Buffer.BlockCopy(data, 0, result, 0, len1);
            Buffer.BlockCopy(data, start2, result, len1, len2);
            return result;
        }

        private static byte[] ExtractContentsDer(byte[] signed)
        {
            string text = Latin1.GetString(signed);
            int c = text.IndexOf("/Contents", StringComparison.Ordinal);
            int open = text.IndexOf('<', c);
            int close = text.IndexOf('>', open);
            string hex = text.Substring(open + 1, close - open - 1);
            byte[] full = HexToBytes(hex);
            int derLen = DerTotalLength(full);
            return full.Take(derLen).ToArray();
        }

        private static byte[] HexToBytes(string hex)
        {
            int n = hex.Length / 2;
            byte[] b = new byte[n];
            for (int i = 0; i < n; i++)
                b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        // Computes the exact DER length of the first ASN.1 object so trailing zero padding is dropped.
        private static int DerTotalLength(byte[] d)
        {
            int i = 1; // skip tag
            int b = d[i++];
            int len;
            if ((b & 0x80) == 0) { len = b; }
            else
            {
                int n = b & 0x7f;
                len = 0;
                for (int k = 0; k < n; k++) len = (len << 8) | d[i++];
            }
            return i + len;
        }
    }
}
