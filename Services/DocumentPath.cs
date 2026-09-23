using System;
using System.IO;

namespace Scalpel.Services
{
    /// <summary>Identity of a document on disk: two spellings of the same file compare equal.</summary>
    public static class DocumentPath
    {
        public static string? Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        public static bool Same(string? a, string? b)
        {
            var na = Normalize(a);
            var nb = Normalize(b);
            return na is not null && nb is not null
                && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }
    }
}
