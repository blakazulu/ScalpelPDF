using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;

namespace Scalpel.Services.Signing
{
    /// <summary>
    /// Best-effort collector of revocation material for a certificate chain, used to populate a DSS
    /// for long-term validation (LTV). Currently fetches CRLs from each certificate's CRL Distribution
    /// Points (HTTP). OCSP is not yet collected (a documented next step). Network-bound and entirely
    /// defensive — any failure (no CDP extension, offline, self-signed test certs) yields fewer items,
    /// never an exception. App-only; not unit-tested (needs real CA-chained certs + network).
    /// </summary>
    public static class RevocationCollector
    {
        /// <summary>Downloads the DER-encoded CRLs reachable from the chain's CRL Distribution Points.</summary>
        public static List<byte[]> CollectCrls(IEnumerable<X509Certificate2> certs, TimeSpan? timeout = null)
        {
            var result = new List<byte[]>();
            if (certs is null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };

            foreach (var cert in certs)
            {
                foreach (var url in ExtractCrlUrls(cert))
                {
                    if (!seen.Add(url)) continue;
                    try
                    {
                        byte[] data = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                        if (data is { Length: > 0 }) result.Add(data);
                    }
                    catch { /* best-effort: skip unreachable/invalid CRLs */ }
                }
            }
            return result;
        }

        // Parses the CRL Distribution Points extension (2.5.29.31) for HTTP(S) URLs.
        private static IEnumerable<string> ExtractCrlUrls(X509Certificate2 cert)
        {
            var ext = cert.Extensions
                .OfType<System.Security.Cryptography.X509Certificates.X509Extension>()
                .FirstOrDefault(e => e.Oid?.Value == "2.5.29.31");
            if (ext is null) yield break;

            CrlDistPoint cdp;
            DistributionPoint[] points;
            try
            {
                cdp = CrlDistPoint.GetInstance(Asn1Object.FromByteArray(ext.RawData));
                points = cdp.GetDistributionPoints();
            }
            catch { yield break; }

            foreach (var dp in points)
            {
                var dpn = dp.DistributionPointName;
                if (dpn is null || dpn.PointType != DistributionPointName.FullName) continue;
                GeneralName[] names;
                try { names = GeneralNames.GetInstance(dpn.Name).GetNames(); }
                catch { continue; }

                foreach (var gn in names)
                {
                    if (gn.TagNo != GeneralName.UniformResourceIdentifier) continue;
                    string url;
                    try { url = DerIA5String.GetInstance(gn.Name).GetString(); }
                    catch { continue; }
                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        yield return url;
                }
            }
        }
    }
}
