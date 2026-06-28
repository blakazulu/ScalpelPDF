using System;
using System.Collections.Generic;
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

        /// <summary>Where on-demand fast-quality downloads are written (always the writable per-user location).</summary>
        public static string DownloadTessdataDir => Path.Combine(UserOcrDir, "tessdata");

        /// <summary>Where on-demand best-quality downloads are written (sibling of <see cref="DownloadTessdataDir"/>).</summary>
        public static string DownloadTessdataDirBest => Path.Combine(UserOcrDir, "tessdata-best");

        /// <summary>Official tessdata language file URL. Uses <c>tessdata_best</c> repo when <paramref name="best"/> is true,
        /// otherwise <c>tessdata_fast</c> (trusted source; data, not executable code).</summary>
        public static string LanguageUrl(string lang, bool best = false) =>
            best
                ? $"https://github.com/tesseract-ocr/tessdata_best/raw/main/{lang}.traineddata"
                : $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{lang}.traineddata";

        /// <summary>Candidate tessdata directories, in priority order: the bundled <c>ocr</c> folder
        /// (installed build), the located engine's OWN sibling <c>tessdata</c> (a full Tesseract
        /// install ships language data there — preferring it avoids re-downloading what's already
        /// present), then the writable per-user download dir.</summary>
        private static IEnumerable<string> TessdataDirs()
        {
            yield return Path.Combine(AppOcrDir, "tessdata");

            string? exe = FindTesseractExe();
            string? exeDir = string.IsNullOrEmpty(exe) ? null : Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(exeDir))
                yield return Path.Combine(exeDir!, "tessdata");

            yield return DownloadTessdataDir;
        }

        /// <summary>The tessdata directory that actually contains <paramref name="lang"/>, or the
        /// appropriate writable download dir if none yet. Pass this to the engine via <c>--tessdata-dir</c>.
        /// When <paramref name="best"/> is true only the best-quality download dir is searched;
        /// when false the full priority chain (bundled → sibling → fast download dir) is used.</summary>
        public static string ResolveTessdataDir(string lang, bool best = false)
        {
            // Best-quality data only ever lives in its own download dir (bundled/system tessdata is fast).
            if (best) return DownloadTessdataDirBest;
            foreach (var dir in TessdataDirs())
                if (File.Exists(Path.Combine(dir, $"{lang}.traineddata"))) return dir;
            return DownloadTessdataDir;
        }

        /// <summary>Returns true if the given language data file is already present on disk.
        /// When <paramref name="best"/> is true only the best-quality download dir is checked;
        /// when false the full priority chain is searched.</summary>
        public static bool HasLanguage(string lang, bool best = false)
        {
            if (best)
                return File.Exists(Path.Combine(DownloadTessdataDirBest, $"{lang}.traineddata"));
            foreach (var dir in TessdataDirs())
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

        /// <summary>Downloads the given language's data into <see cref="DownloadTessdataDir"/> (fast)
        /// or <see cref="DownloadTessdataDirBest"/> (best), depending on <paramref name="best"/>.
        /// Returns true on success. Caller must have obtained the user's consent first.</summary>
        public static bool DownloadLanguage(string lang, bool best = false)
        {
            try
            {
                string targetDir = best ? DownloadTessdataDirBest : DownloadTessdataDir;
                Directory.CreateDirectory(targetDir);
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string dest = Path.Combine(targetDir, $"{lang}.traineddata");
                string tmp = dest + ".part";
                using (var wc = new WebClient())
                    wc.DownloadFile(LanguageUrl(lang, best), tmp);

                // Basic sanity: traineddata files are well over 100 KB.
                if (new FileInfo(tmp).Length < 100_000) { try { File.Delete(tmp); } catch { } return false; }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Supported OCR languages: (ISO 639-2 code, display name) pairs.</summary>
        public static readonly (string Code, string Name)[] Languages =
        {
            ("eng",     "English"),
            ("spa",     "Spanish"),
            ("fra",     "French"),
            ("deu",     "German"),
            ("por",     "Portuguese"),
            ("ita",     "Italian"),
            ("nld",     "Dutch"),
            ("rus",     "Russian"),
            ("chi_sim", "Chinese (Simplified)"),
            ("chi_tra", "Chinese (Traditional)"),
            ("jpn",     "Japanese"),
            ("kor",     "Korean"),
            ("ara",     "Arabic"),
            ("heb",     "Hebrew"),
            ("hin",     "Hindi"),
        };
    }
}
