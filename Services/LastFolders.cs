using System;
using System.IO;

namespace Scalpel.Services
{
    /// <summary>
    /// Remembers the folder last used for each KIND of file picker.
    /// <para>
    /// Picking a signature image and picking a PDF are different journeys through the filesystem;
    /// sharing one "last folder" between them means the image picker keeps opening wherever the
    /// last document lived. Each purpose gets its own remembered folder.
    /// </para>
    /// </summary>
    public static class LastFolders
    {
        /// <summary>Purpose keys. Free-form, but keep them stable: they become setting names.</summary>
        public const string Image = "Image";

        /// <summary>Signature images imported into the signature store.</summary>
        public const string Signature = "Signature";

        /// <summary>Images placed as stamps.</summary>
        public const string Stamp = "Stamp";

        /// <summary>Watermark artwork.</summary>
        public const string Watermark = "Watermark";

        /// <summary>Certificates for digital signing.</summary>
        public const string Certificate = "Certificate";

        /// <summary>The registry setting name that stores one purpose's folder.</summary>
        public static string SettingName(string purpose) => "LastFolder." + purpose;

        /// <summary>
        /// The folder to hand a picker as its starting directory, or null when nothing usable is
        /// remembered (the caller then leaves the dialog to its own default).
        /// </summary>
        public static string? Resolve(string? remembered)
        {
            if (string.IsNullOrWhiteSpace(remembered)) return null;
            try { return Directory.Exists(remembered) ? remembered : null; }
            catch { return null; }
        }

        /// <summary>
        /// The folder to remember from a path the user just picked, or null when the path yields
        /// nothing usable.
        /// </summary>
        public static string? FromPickedPath(string? pickedPath)
        {
            if (string.IsNullOrWhiteSpace(pickedPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(pickedPath);
                return string.IsNullOrWhiteSpace(dir) ? null : dir;
            }
            catch { return null; }
        }
    }
}
