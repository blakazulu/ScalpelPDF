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
            };
            // tesseract <img> stdout --tessdata-dir <dir> -l <lang> tsv
            // net48 has no ArgumentList — build a quoted argument string.
            var sb = new StringBuilder();
            sb.Append('"').Append(imagePath).Append("\" stdout");
            if (!string.IsNullOrEmpty(_tessdataDir))
                sb.Append(" --tessdata-dir \"").Append(_tessdataDir).Append('"');
            sb.Append(" -l ").Append(_lang).Append(" tsv");
            psi.Arguments = sb.ToString();

            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return stdout;
        }
    }
}
