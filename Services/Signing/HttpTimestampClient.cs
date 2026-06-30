using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Tsp;

namespace Scalpel.Services.Signing
{
    /// <summary>
    /// <see cref="ITimestampClient"/> over HTTP: requests an RFC-3161 timestamp token from a TSA
    /// (Time-Stamp Authority) URL using BouncyCastle's TSP stack. Network-bound — kept out of the
    /// unit-test project (signing is tested against an in-memory fake timestamp client instead).
    /// </summary>
    public sealed class HttpTimestampClient : ITimestampClient
    {
        /// <summary>A free, public TSA used by default when the user doesn't override it.</summary>
        public const string DefaultTsaUrl = "http://timestamp.digicert.com";

        private readonly string _url;
        private readonly TimeSpan _timeout;

        public HttpTimestampClient(string? url = null, TimeSpan? timeout = null)
        {
            _url = string.IsNullOrWhiteSpace(url) ? DefaultTsaUrl : url!.Trim();
            _timeout = timeout ?? TimeSpan.FromSeconds(20);
        }

        public byte[] GetTimestampToken(byte[] data)
        {
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(data);

            var reqGen = new TimeStampRequestGenerator();
            reqGen.SetCertReq(true); // ask the TSA to embed its certificate in the token
            var nonce = BigInteger.ValueOf(DateTime.UtcNow.Ticks);
            TimeStampRequest req = reqGen.Generate(TspAlgorithms.Sha256, digest, nonce);
            byte[] reqBytes = req.GetEncoded();

            byte[] respBytes;
            using (var http = new HttpClient { Timeout = _timeout })
            using (var content = new ByteArrayContent(reqBytes))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
                HttpResponseMessage resp = http.PostAsync(_url, content).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                respBytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }

            var response = new TimeStampResponse(respBytes);
            response.Validate(req); // matches nonce + request; throws on mismatch
            TimeStampToken token = response.TimeStampToken
                ?? throw new InvalidOperationException("The timestamp authority did not return a token.");
            return token.GetEncoded();
        }
    }
}
