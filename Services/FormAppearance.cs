using System;
using System.Globalization;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Builds the content stream of a fillable text field's /AP /N appearance.
    /// <para>
    /// Kept free of PdfSharpCore types so the exact bytes a viewer will read can be unit tested.
    /// Three things matter and each of them used to be wrong:
    /// numbers must be written with invariant formatting (a comma decimal separator produces a
    /// structurally invalid stream on German, French, Spanish, Italian, Turkish and Russian
    /// systems), a multiline value must be laid out as separate lines rather than one run, and the
    /// bytes must be Windows-1252 to match the /WinAnsiEncoding the font resource declares.
    /// </para>
    /// </summary>
    public static class FormAppearance
    {
        /// <summary>Leading as a multiple of the font size, matching Acrobat's default for
        /// generated field appearances.</summary>
        public const double LineHeightFactor = 1.16;

        private const double PadX = 2.0;

        /// <summary>
        /// Builds the `/Tx BMC ... EMC` content stream for a text field.
        /// </summary>
        /// <param name="text">The field value. CR/LF pairs split lines when multiline.</param>
        /// <param name="fontName">Resource name including the slash, e.g. "/Helv".</param>
        /// <param name="combCells">When above zero the field is a comb: its box is divided into
        /// this many equal cells and one character is centred in each.</param>
        public static string BuildTextFieldContent(string? text, string fontName, double fontSize,
                                                   double fieldW, double fieldH, bool multiline,
                                                   int combCells = 0)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("/Tx BMC\nq\n");
            sb.Append(Num(0)).Append(' ').Append(Num(0)).Append(' ')
              .Append(Num(fieldW)).Append(' ').Append(Num(fieldH)).Append(" re W n\n");
            sb.Append("BT\n");
            sb.Append(fontName).Append(' ').Append(Num(fontSize)).Append(" Tf\n0 g\n");

            var value = text ?? string.Empty;

            if (combCells > 0 && !multiline)
            {
                // A comb field is ruled into equal cells and each character is centred in its own
                // cell. Writing the value as one run instead leaves it bunched against the left
                // edge, out of step with the printed boxes in every other reader.
                double textY = (fieldH - fontSize) / 2 + fontSize * 0.2;
                if (textY < 1) textY = 1;
                string flat = value.Replace("\r", "").Replace("\n", " ");
                if (flat.Length > combCells) flat = flat.Substring(0, combCells);

                for (int i = 0; i < flat.Length; i++)
                {
                    // Each glyph is placed absolutely, so no assumption is made about the width
                    // of the previous one.
                    double cellCentre = CombFieldLayout.CellLeft(fieldW, combCells, i)
                                        + fieldW / combCells / 2.0;
                    double glyphX = cellCentre - fontSize * 0.28;   // rough half-advance
                    if (glyphX < 0) glyphX = 0;
                    sb.Append("1 0 0 1 ").Append(Num(glyphX)).Append(' ')
                      .Append(Num(textY)).Append(" Tm\n");
                    sb.Append('(').Append(EscapePdfString(flat[i].ToString())).Append(") Tj\n");
                }
            }
            else if (!multiline)
            {
                // Single line: baseline centred in the field box.
                double textY = (fieldH - fontSize) / 2 + fontSize * 0.2;
                if (textY < 1) textY = 1;
                sb.Append(Num(PadX)).Append(' ').Append(Num(textY)).Append(" Td\n");
                sb.Append('(').Append(EscapePdfString(value.Replace("\r", "").Replace("\n", " ")))
                  .Append(") Tj\n");
            }
            else
            {
                double leading = fontSize * LineHeightFactor;
                // Multiline fields fill from the top down, so the first baseline sits one
                // ascent below the top edge rather than being centred.
                double firstBaseline = fieldH - PadX - fontSize * 0.85;
                if (firstBaseline < 1) firstBaseline = Math.Max(1, fieldH - fontSize);

                sb.Append(Num(leading)).Append(" TL\n");
                sb.Append(Num(PadX)).Append(' ').Append(Num(firstBaseline)).Append(" Td\n");

                var lines = SplitLines(value);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) sb.Append("T*\n");
                    sb.Append('(').Append(EscapePdfString(lines[i])).Append(") Tj\n");
                }
            }

            sb.Append("ET\nQ\nEMC");
            return sb.ToString();
        }

        /// <summary>Splits a field value into display lines on CRLF, CR or LF.</summary>
        public static string[] SplitLines(string? value)
        {
            if (string.IsNullOrEmpty(value)) return [string.Empty];
            return value!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        /// <summary>
        /// Formats a PDF number with invariant culture and no exponent, so the stream is valid
        /// regardless of the operator's regional settings.
        /// </summary>
        public static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Escapes a literal PDF string, keeping every character Windows-1252 can represent
        /// (smart quotes, dashes, the euro sign) and substituting '?' for the rest.
        /// </summary>
        public static string EscapePdfString(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var enc = WinAnsi;
            var sb = new StringBuilder(s!.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '(': sb.Append("\\("); break;
                    case ')': sb.Append("\\)"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        sb.Append(IsWinAnsiRepresentable(enc, c) ? c : '?');
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Encodes a built content stream to the bytes a PDF viewer will read.</summary>
        public static byte[] EncodeContent(string content) => WinAnsi.GetBytes(content ?? string.Empty);

        private static Encoding? _winAnsi;

        /// <summary>Windows-1252, the encoding /WinAnsiEncoding actually names.</summary>
        public static Encoding WinAnsi
        {
            get
            {
                if (_winAnsi is not null) return _winAnsi;
                try { _winAnsi = Encoding.GetEncoding(1252); }
                catch { _winAnsi = Encoding.GetEncoding("iso-8859-1"); }
                return _winAnsi;
            }
        }

        private static bool IsWinAnsiRepresentable(Encoding enc, char c)
        {
            if (c < 0x80) return true;
            try
            {
                var bytes = enc.GetBytes([c]);
                if (bytes.Length != 1) return false;
                // A single '?' byte back from a non-'?' source means the encoder gave up.
                return !(bytes[0] == (byte)'?' && c != '?');
            }
            catch { return false; }
        }
    }
}
