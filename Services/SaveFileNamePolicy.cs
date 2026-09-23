using System;
using System.IO;

namespace Scalpel.Services
{
    /// <summary>Decides whether a Save dialog's typed file name gets the default extension.</summary>
    public static class SaveFileNamePolicy
    {
        /// <summary>Appends <paramref name="extension"/> to <paramref name="path"/> when the dialog
        /// adds extensions and the name has none - or, with <paramref name="requireExtension"/>,
        /// whenever the name does not already end in that extension (so "exam.final" becomes
        /// "exam.final.pdf" instead of being mistaken for a ".final" file).</summary>
        public static string ApplyExtension(string path, string? extension, bool addExtension, bool requireExtension)
        {
            if (!addExtension || string.IsNullOrWhiteSpace(extension)) return path;

            string normalized = extension!.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
            string current = Path.GetExtension(path);
            if (current.Equals(normalized, StringComparison.OrdinalIgnoreCase)) return path;
            if (current.Length == 0 || requireExtension) return path + normalized;
            return path;
        }
    }
}
