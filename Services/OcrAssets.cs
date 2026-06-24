using System;
using System.IO;
using System.Net;

namespace Scalpel.Services
{
    /// <summary>
    /// Locates the Tesseract engine + language data and, for portable installs, downloads language
    /// data on demand. Packaging is distribution-aware:
    /// <list type="bullet">
    /// <item>Installed build: the engine + data ship in an <c>ocr</c> folder next to the EXE
    /// (<see cref="AppOcrDir"/>) — OCR works out of the box, no download.</item>
    /// <item>Portable build: nothing OCR-related ships; data is fetched once into
    /// <c>%LOCALAPPDATA%\Scalpel\ocr</c> (<see cref="UserOcrDir"/>) on first use.</item>
    /// </list>
    /// </summary>
    public static class OcrAssets
    {
        /// <summary>Bundled location for installed builds: <c>&lt;AppDir&gt;\ocr</c>.</summary>
        public static string AppOcrDir => Path.Combine(AppContext.BaseDirectory, "ocr");

        /// <summary>Writable per-user location for portable on-demand downloads.</summary>
        public static string UserOcrDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Scalpel", "ocr");

        /// <summary>Where on-demand downloads are written (always the writable per-user location).</summary>
        public static string DownloadTessdataDir => Path.Combine(UserOcrDir, "tessdata");

        /// <summary>Official tessdata_fast language file (trusted source; data, not executable code).</summary>
        public static string LanguageUrl(string lang) =>
            $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{lang}.traineddata";

        private static string[] TessdataDirs => new[]
        {
            Path.Combine(AppOcrDir, "tessdata"),
            DownloadTessdataDir,
        };

        /// <summary>The tessdata directory that actually contains <paramref name="lang"/>, or the
        /// writable download dir if none yet. Pass this to the engine via <c>--tessdata-dir</c>.</summary>
        public static string ResolveTessdataDir(string lang)
        {
            foreach (var dir in TessdataDirs)
                if (File.Exists(Path.Combine(dir, $"{lang}.traineddata"))) return dir;
            return DownloadTessdataDir;
        }

        public static bool HasLanguage(string lang)
        {
            foreach (var dir in TessdataDirs)
                if (File.Exists(Path.Combine(dir, $"{lang}.traineddata"))) return true;
            return false;
        }

        /// <summary>
        /// Finds a usable <c>tesseract.exe</c>: bundled next to the app (installed build), placed in
        /// the per-user OCR dir, a standard install (Program Files), or one on PATH. Returns null if
        /// none is found (we never auto-download/execute native binaries).
        /// </summary>
        public static string? FindTesseractExe()
        {
            string[] candidates =
            {
                Path.Combine(AppOcrDir, "tesseract.exe"),
                Path.Combine(UserOcrDir, "tesseract.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tesseract-OCR", "tesseract.exe"),
            };
            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            string? path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), "tesseract.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            return null;
        }

        /// <summary>Downloads the given language's data into <see cref="DownloadTessdataDir"/>.
        /// Returns true on success. Caller must have obtained the user's consent first.</summary>
        public static bool DownloadLanguage(string lang)
        {
            try
            {
                Directory.CreateDirectory(DownloadTessdataDir);
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string dest = Path.Combine(DownloadTessdataDir, $"{lang}.traineddata");
                string tmp = dest + ".part";
                using (var wc = new WebClient())
                    wc.DownloadFile(LanguageUrl(lang), tmp);

                // Basic sanity: traineddata files are well over 100 KB.
                if (new FileInfo(tmp).Length < 100_000) { try { File.Delete(tmp); } catch { } return false; }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                return true;
            }
            catch { return false; }
        }
    }
}
