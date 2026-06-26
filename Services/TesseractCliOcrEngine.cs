using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SixLabors.ImageSharp;

namespace Scalpel.Services
{
    /// <summary>
    /// <see cref="IOcrEngine"/> that shells out to a local <c>tesseract.exe</c> with TSV output and
    /// parses the result via <see cref="TesseractTsv"/>. No native P/Invoke, no NuGet — the engine is
    /// a user-provided/located binary, keeping Scalpel's own footprint unchanged.
    /// </summary>
    public sealed class TesseractCliOcrEngine : IOcrEngine
    {
        private readonly string _exePath;
        private readonly string _tessdataDir;
        private readonly string _lang;
        private readonly double _minConfidence;

        public TesseractCliOcrEngine(string exePath, string tessdataDir, string lang = "eng", double minConfidence = 0)
        {
            _exePath = exePath;
            _tessdataDir = tessdataDir;
            _lang = lang;
            _minConfidence = minConfidence;
        }

        public OcrPageResult Recognize(byte[] imageBytes, double pageWidthPt, double pageHeightPt)
        {
            int pxW, pxH;
            using (var probe = Image.Load(imageBytes)) { pxW = probe.Width; pxH = probe.Height; }

            string tmpImg = Path.Combine(Path.GetTempPath(), $"scalpel_ocr_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(tmpImg, imageBytes);
            try
            {
                string tsv = RunTesseract(tmpImg);
                return TesseractTsv.Parse(tsv, pxW, pxH, pageWidthPt, pageHeightPt, _minConfidence);
            }
            finally { try { File.Delete(tmpImg); } catch { } }
        }

        /// <summary>
        /// Builds the tesseract command line for TSV-on-stdout. We request TSV via
        /// <c>-c tessedit_create_tsv=1</c> rather than the <c>tsv</c> config file, because when
        /// <c>--tessdata-dir</c> is overridden to Scalpel's own folder (the portable download dir,
        /// which holds only the language data) the <c>configs/tsv</c> file is absent — tesseract then
        /// errors with "Can't open tsv" and silently emits plain text, yielding a non-searchable OCR
        /// layer. Setting the parameter directly needs no config file. net48 has no ArgumentList, so
        /// this is a quoted argument string.
        /// </summary>
        internal static string BuildArguments(string imagePath, string tessdataDir, string lang)
        {
            var sb = new StringBuilder();
            sb.Append('"').Append(imagePath).Append("\" stdout");
            if (!string.IsNullOrEmpty(tessdataDir))
                sb.Append(" --tessdata-dir \"").Append(tessdataDir).Append('"');
            sb.Append(" -l ").Append(lang).Append(" -c tessedit_create_tsv=1");
            return sb.ToString();
        }

        private string RunTesseract(string imagePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = BuildArguments(imagePath, _tessdataDir, _lang),
            };

            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return stdout;
        }
    }
}
