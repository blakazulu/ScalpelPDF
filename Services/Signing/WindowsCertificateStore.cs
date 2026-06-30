using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Scalpel.Services.Signing
{
    /// <summary>
    /// Lists signing-capable certificates from the current user's personal Windows certificate store,
    /// builds a certificate's issuer chain, and formats a short label for a picker. Windows-only and
    /// guarded behind <see cref="IsAvailable"/> so the surrounding signing flow stays portable for a
    /// future cross-platform port (off Windows the methods return empty). Pure given a certificate —
    /// <see cref="Describe"/> is unit-tested.
    /// </summary>
    public static class WindowsCertificateStore
    {
        /// <summary>True when the personal certificate store is available (i.e. on Windows).</summary>
        public static bool IsAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Returns certificates from <c>CurrentUser\My</c> that carry a private key, are currently
        /// valid (time-wise), and whose key usage permits digital signatures (or declares no usage),
        /// newest-expiring first. Empty when the store is unavailable or unreadable.
        /// </summary>
        public static IReadOnlyList<X509Certificate2> ListSigningCertificates()
        {
            if (!IsAvailable) return Array.Empty<X509Certificate2>();
            var results = new List<X509Certificate2>();
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                DateTime now = DateTime.Now;
                foreach (var cert in store.Certificates)
                {
                    if (!cert.HasPrivateKey) continue;
                    if (now < cert.NotBefore || now > cert.NotAfter) continue;
                    if (!AllowsDigitalSignature(cert)) continue;
                    results.Add(cert);
                }
            }
            catch { /* store unavailable -> empty list */ }
            return results.OrderByDescending(c => c.NotAfter).ToList();
        }

        // No KeyUsage extension => unrestricted. Otherwise require DigitalSignature or NonRepudiation.
        private static bool AllowsDigitalSignature(X509Certificate2 cert)
        {
            var ku = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
            if (ku is null) return true;
            return (ku.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) != 0;
        }

        /// <summary>
        /// Builds the issuer chain for <paramref name="signer"/> (intermediates + root), excluding the
        /// signer itself. Revocation is not checked (offline). Empty array if no chain builds.
        /// </summary>
        public static X509Certificate2[] BuildChain(X509Certificate2 signer)
        {
            if (signer is null) return Array.Empty<X509Certificate2>();
            try
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                chain.Build(signer);
                return chain.ChainElements
                    .Cast<X509ChainElement>()
                    .Select(el => el.Certificate)
                    .Where(c => !c.Equals(signer))
                    .ToArray();
            }
            catch { return Array.Empty<X509Certificate2>(); }
        }

        /// <summary>A short human label: "CN — issuer CN — expires yyyy-MM-dd".</summary>
        public static string Describe(X509Certificate2 cert)
        {
            if (cert is null) return "";
            string subject = ExtractCn(cert.Subject) ?? cert.Subject;
            string issuer = ExtractCn(cert.Issuer) ?? cert.Issuer;
            return $"{subject} — {issuer} — {cert.NotAfter:yyyy-MM-dd}";
        }

        private static string? ExtractCn(string distinguishedName)
        {
            if (string.IsNullOrEmpty(distinguishedName)) return null;
            foreach (var part in distinguishedName.Split(','))
            {
                var p = part.Trim();
                if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return p.Substring(3).Trim();
            }
            return null;
        }
    }
}
