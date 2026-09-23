using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Decides whether a link target taken out of an untrusted PDF may be handed to the shell.
    /// <para>
    /// A PDF's /URI action is attacker-controlled text. Passing it straight to
    /// <c>Process.Start(UseShellExecute = true)</c> lets a document launch any registered protocol
    /// handler, open a local executable, or reach a UNC share, so only web and mail targets are
    /// allowed through. Everything else is refused and reported to the user.
    /// </para>
    /// </summary>
    public static class LinkTargetPolicy
    {
        /// <summary>
        /// Validates and normalizes a raw PDF link target.
        /// Returns true and sets <paramref name="target"/> to the exact string to open, or returns
        /// false when the target must not be launched.
        /// </summary>
        public static bool TryNormalize(string? raw, out string target)
        {
            target = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var s = raw!.Trim();

            // Reject anything with control characters or embedded whitespace-newlines: a real URL
            // does not contain them, and they are a classic way to hide a second target.
            foreach (var ch in s)
                if (char.IsControl(ch)) return false;

            // A scheme-qualified target must be one of the three we trust.
            int colon = s.IndexOf(':');
            if (colon > 0)
            {
                var scheme = s.Substring(0, colon);
                // A "scheme" containing a slash or dot is really a bare host with a port or path
                // (example.com:8080/x), which falls through to the bare-host handling below.
                if (scheme.IndexOf('/') < 0 && scheme.IndexOf('.') < 0 && scheme.IndexOf('\\') < 0)
                {
                    if (!IsAllowedScheme(scheme)) return false;

                    if (scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase))
                    {
                        if (s.Length <= colon + 1) return false;
                        target = s;
                        return true;
                    }

                    // http / https: must parse as an absolute URI with a real host.
                    if (!Uri.TryCreate(s, UriKind.Absolute, out var abs)) return false;
                    if (string.IsNullOrEmpty(abs.Host)) return false;
                    target = abs.AbsoluteUri;
                    return true;
                }
            }

            // No usable scheme. A bare dotted host ("www.example.com", "example.com/path") is what
            // most real-world PDFs carry, so promote it to https rather than refusing it.
            if (s.StartsWith("//", StringComparison.Ordinal)) return false;   // protocol-relative
            if (s.StartsWith("\\", StringComparison.Ordinal)) return false;   // UNC path
            if (s.IndexOf('\\') >= 0) return false;                           // Windows path
            if (s.IndexOf('.') < 0) return false;                             // not a host at all

            var hostPart = s;
            int cut = hostPart.IndexOfAny(['/', '?', '#']);
            if (cut >= 0) hostPart = hostPart.Substring(0, cut);
            int portCut = hostPart.IndexOf(':');
            if (portCut >= 0) hostPart = hostPart.Substring(0, portCut);
            if (hostPart.Length == 0 || hostPart.StartsWith(".", StringComparison.Ordinal)
                                     || hostPart.EndsWith(".", StringComparison.Ordinal))
                return false;
            if (hostPart.IndexOf('.') < 0) return false;

            var promoted = "https://" + s;
            if (!Uri.TryCreate(promoted, UriKind.Absolute, out var pabs)) return false;
            if (string.IsNullOrEmpty(pabs.Host)) return false;
            target = pabs.AbsoluteUri;
            return true;
        }

        private static bool IsAllowedScheme(string scheme) =>
            scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
    }
}
