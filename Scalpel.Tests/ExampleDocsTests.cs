using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Generates three FULL bilingual (Hebrew + English) sample documents into docs/samples/ and
    /// verifies them end-to-end: each opens, embeds its font programs, renders Hebrew right-to-left,
    /// and — crucially for the "edit existing Hebrew text" feature — its Hebrew lines round-trip
    /// back to LOGICAL order through the same extraction path the editor uses
    /// (PdfPig.GetWords + BidiReorder.JoinWordsLogical).
    /// </summary>
    public class ExampleDocsTests
    {
        // ---- shared font / bidi setup (mirrors MainWindow.DrawTextRun without WPF) -------------

        private const string HebFont = "Noto Sans Hebrew";
        private const string LatFont = "Noto Sans";

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }

        private static void EnsureFonts()
        {
            string fonts = Path.Combine(RepoRoot(), "Resources", "Fonts");
            void Reg(string fam, string file)
            {
                string p = Path.Combine(fonts, file);
                if (File.Exists(p))
                    PdfFontResolver.Instance.RegisterBundledFont(fam, File.ReadAllBytes(p), false, false);
            }
            Reg(HebFont, "NotoSansHebrew-Regular.ttf");
            Reg(LatFont, "NotoSans-Regular.ttf");
            if (PdfSharpCore.Fonts.GlobalFontSettings.FontResolver is null)
                PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        // logical -> visual (left-to-right glyph) order PdfSharpCore needs for RTL.
        private static string Visual(string logical)
        {
            string shaped = ArabicShaper.ContainsArabic(logical) ? ArabicShaper.Shape(logical) : logical;
            return BidiReorder.ToVisual(shaped);
        }

        // ---- minimal bilingual page layout ----------------------------------------------------

        private sealed class Pager
        {
            public PdfDocument Doc = null!;
            public PdfPage Page = null!;
            public XGraphics Gfx = null!;
            public double Y;
            public const double Margin = 54;
            public double ContentW => Page.Width - 2 * Margin;
            public double RightX => Page.Width - Margin;

            public void NewPage(double w = 595, double h = 842) // A4 in points
            {
                Gfx?.Dispose();
                Page = Doc.AddPage();
                Page.Width = w; Page.Height = h;
                Gfx = XGraphics.FromPdfPage(Page);
                Y = Margin;
            }

            public void Space(double h) => Y += h;

            private void EnsureRoom(double need)
            {
                if (Y + need > Page.Height - Margin) NewPage(Page.Width, Page.Height);
            }

            /// <summary>Header bar with a bilingual title (English left, Hebrew right).</summary>
            public void Banner(string en, string he, XColor bar)
            {
                Gfx.DrawRectangle(new XSolidBrush(bar), new XRect(0, 0, Page.Width, 64));
                var ef = new XFont(LatFont, 18, XFontStyle.Bold);
                var hf = new XFont(HebFont, 18, XFontStyle.Bold);
                Gfx.DrawString(en, ef, XBrushes.White, new XPoint(Margin, 40));
                string vis = Visual(he);
                double hw = Gfx.MeasureString(vis, hf).Width;
                Gfx.DrawString(vis, hf, XBrushes.White, new XPoint(RightX - hw, 40));
                Y = 84;
            }

            public void Heading(string text, bool rtl, double size = 14)
            {
                EnsureRoom(size * 2);
                var font = new XFont(rtl ? HebFont : LatFont, size, XFontStyle.Bold);
                var slate = new XSolidBrush(XColor.FromArgb(255, 30, 41, 59));
                if (rtl)
                {
                    string vis = Visual(text);
                    double w = Gfx.MeasureString(vis, font).Width;
                    Gfx.DrawString(vis, font, slate, new XPoint(RightX - w, Y));
                }
                else Gfx.DrawString(text, font, slate, new XPoint(Margin, Y));
                Y += size * 1.7;
            }

            /// <summary>Word-wrapped paragraph; RTL paragraphs render right-aligned, logical order preserved per line.</summary>
            public void Para(string text, bool rtl, double size = 11)
            {
                var font = new XFont(rtl ? HebFont : LatFont, size, XFontStyle.Regular);
                double lineH = size * 1.5;
                var words = text.Split(' ');
                var line = new List<string>();
                void Flush()
                {
                    if (line.Count == 0) return;
                    EnsureRoom(lineH);
                    string logical = string.Join(" ", line);
                    if (rtl)
                    {
                        string vis = Visual(logical);
                        double w = Gfx.MeasureString(vis, font).Width;
                        Gfx.DrawString(vis, font, XBrushes.Black, new XPoint(RightX - w, Y));
                    }
                    else Gfx.DrawString(logical, font, XBrushes.Black, new XPoint(Margin, Y));
                    Y += lineH;
                    line.Clear();
                }
                foreach (var word in words)
                {
                    var trial = string.Join(" ", line.Append(word));
                    double tw = Gfx.MeasureString(rtl ? Visual(trial) : trial, font).Width;
                    if (tw > ContentW && line.Count > 0) Flush();
                    line.Add(word);
                }
                Flush();
            }

            /// <summary>A right-to-left label/value row: Hebrew label on the right, value on the left.</summary>
            public void Row(string heLabel, string value)
            {
                EnsureRoom(18);
                var hf = new XFont(HebFont, 11, XFontStyle.Bold);
                var vf = new XFont(LatFont, 11, XFontStyle.Regular);
                string vis = Visual(heLabel);
                double w = Gfx.MeasureString(vis, hf).Width;
                Gfx.DrawString(vis, hf, XBrushes.Black, new XPoint(RightX - w, Y));
                Gfx.DrawString(value, vf, XBrushes.Black, new XPoint(Margin, Y));
                Y += 18;
            }
        }

        // ---- the three documents --------------------------------------------------------------
        // A known multi-word Hebrew line embedded in every doc, used to prove the edit round-trip.
        // "מסמך עברית מלא ומאומת" = "full, verified Hebrew document" (logical order).
        private const string ProofLine =
            "מסמך עברית מלא ומאומת";

        private static readonly XColor Steel = XColor.FromArgb(255, 30, 41, 59);
        private static readonly XColor Surgical = XColor.FromArgb(255, 225, 29, 56);

        private static void BuildInvoice(string path)
        {
            using var doc = new PdfDocument { };
            doc.Info.Title = "Scalpel — Tax Invoice / חשבונית מס";
            var p = new Pager { Doc = doc };
            p.NewPage();
            p.Banner("Scalpel Software Ltd.", "סקאלפל תוכנה בע״מ", Steel);

            p.Heading("חשבונית מס 2026-0427", rtl: true, 16);
            p.Space(4);
            p.Row("תאריך:", "26 June 2026");
            p.Row("לכבוד:", "Dr. A. Levi Clinic");
            p.Row("מספר עוסק:", "514783920");
            p.Space(10);

            p.Heading("פירוט שירותים / Line items", rtl: true, 13);
            p.Para("רישוי שנתי של תוכנת סקאלפל לעריכת מסמכי PDF, כולל תמיכה מלאה בעברית, חתימה דיגיטלית והשחרה מאובטחת של מידע רגיש.", rtl: true);
            p.Para("Annual license for the Scalpel PDF editor, including full Hebrew support, digital signing, and secure redaction of sensitive information.", rtl: false);
            p.Space(6);
            p.Row("סך הכל לתשלום:", "₪ 1,290.00");
            p.Space(14);

            p.Heading("הערות / Notes", rtl: true, 13);
            p.Para(ProofLine + ".", rtl: true);
            p.Para("Questions? Contact billing@scalpel.example or call +972-3-555-0142.", rtl: false);
            p.Gfx.Dispose();
            doc.Save(path);
        }

        private static void BuildLetter(string path)
        {
            using var doc = new PdfDocument { };
            doc.Info.Title = "Scalpel — Cover Letter / מכתב מלווה";
            var p = new Pager { Doc = doc };
            p.NewPage();
            p.Banner("Scalpel Software Ltd.", "סקאלפל תוכנה בע״מ", Surgical);

            p.Row("תאריך:", "26 June 2026");
            p.Space(10);
            p.Heading("שלום רב,", rtl: true, 14);
            p.Para("אנו שמחים להודיע כי הגרסה החדשה של סקאלפל כוללת תמיכה מלאה בעברית: ניתן לפתוח מסמך קיים, לערוך טקסט עברי קיים כך שייראה זהה למקור, ולשמור אותו חזרה כקובץ PDF.", rtl: true);
            p.Para("בנוסף שיפרנו את כלי ההשחרה כך שדפים עם סימון יומרו לתמונה שטוחה, והטקסט שמתחת לריבוע השחור יוסר לחלוטין ואינו ניתן לשחזור.", rtl: true);
            p.Para(ProofLine + " — " + "תודה שבחרתם בנו.", rtl: true);
            p.Space(8);
            p.Para("We are delighted to share that the latest Scalpel release adds full Hebrew support. You can open an existing document, edit its Hebrew text in place, and save it back to PDF.", rtl: false);
            p.Space(16);
            p.Heading("בברכה,", rtl: true, 13);
            p.Para("צוות סקאלפל / The Scalpel Team", rtl: true);
            p.Gfx.Dispose();
            doc.Save(path);
        }

        private static void BuildReport(string path)
        {
            using var doc = new PdfDocument { };
            doc.Info.Title = "Scalpel — Feature Report / דוח תכונות";
            var p = new Pager { Doc = doc };
            p.NewPage();
            p.Banner("Feature Report", "דוח תכונות", Steel);

            p.Heading("1. סקירה כללית / Overview", rtl: true, 13);
            p.Para("סקאלפל הוא עורך PDF מקומי וניתן לנשיאה עבור Windows. הוא מאפשר צפייה, הוספת הערות, מיזוג ופיצול, עריכת טקסט, חתימה, מילוי טפסים והשחרה — הכול ללא חיבור לאינטרנט וללא איסוף נתונים.", rtl: true);
            p.Para("Scalpel is a local-only, portable PDF editor for Windows: view, annotate, merge and split, edit text, sign, fill forms, and redact — with no network access and no data collection.", rtl: false);
            p.Space(8);

            p.Heading("2. עברית ושפות RTL / Hebrew and RTL", rtl: true, 13);
            p.Para("טקסט בעברית ובערבית נשמר בכיוון הנכון: המילים מסודרות מימין לשמאל, והאותיות בתוך כל מילה נשמרות בסדר הלוגי הנכון.", rtl: true);
            p.Para(ProofLine + ".", rtl: true);
            p.Space(8);

            p.Heading("3. השחרה מאובטחת / Secure redaction", rtl: true, 13);
            p.Para("בעת השחרה, כל דף המכיל סימון מומר לתמונה שטוחה עם ריבוע שחור אטום מעל האזור. הטקסט המקורי נמחק לחלוטין ואינו ניתן לחיפוש או לשחזור.", rtl: true);
            p.Para("During redaction, every marked page is flattened to an image with an opaque black box over the area. The underlying text is permanently removed — not selectable, searchable, or recoverable.", rtl: false);
            p.Gfx.Dispose();
            doc.Save(path);
        }

        // A 3-page bilingual user handbook.
        private static void BuildHandbook(string path)
        {
            using var doc = new PdfDocument { };
            doc.Info.Title = "Scalpel — User Handbook / מדריך למשתמש";
            var p = new Pager { Doc = doc };

            // ---- Page 1 ----
            p.NewPage();
            p.Banner("User Handbook", "מדריך למשתמש", Steel);
            p.Heading("1. מבוא / Introduction", rtl: true, 13);
            p.Para("סקאלפל הוא עורך PDF מקומי לחלוטין: הוא פועל ללא חיבור לאינטרנט, אינו אוסף נתונים, ואינו דורש התקנה — קובץ הפעלה יחיד וקטן.", rtl: true);
            p.Para("Scalpel is a fully local PDF editor: it runs offline, collects no data, and needs no installation — a single small executable.", rtl: false);
            p.Space(6);
            p.Para("מדריך זה מסביר כיצד לפתוח מסמך, לערוך טקסט עברי קיים, לחתום, ולהשתמש בתפריט הכלים.", rtl: true);
            p.Para(ProofLine + ".", rtl: true);

            // ---- Page 2 ----
            p.NewPage();
            p.Heading("2. עריכת טקסט עברי / Editing Hebrew text", rtl: true, 13);
            p.Para("כדי לערוך טקסט קיים, לחצו פעמיים על השורה הרצויה. תיבת עריכה תיפתח מעל הטקסט, מיושרת לימין, והמילים יישמרו בסדר הלוגי הנכון.", rtl: true);
            p.Para("To edit existing text, double-click the line. An edit box opens over it, right-aligned, and the words keep their correct logical order.", rtl: false);
            p.Space(6);
            p.Para("לאחר העריכה, הטקסט המקורי מוסתר והטקסט החדש מצויר במקומו באמצעות גופן התומך בעברית.", rtl: true);
            p.Para("בסיום, שמרו את המסמך חזרה לקובץ PDF דרך כפתור השמירה.", rtl: true);

            // ---- Page 3 ----
            p.NewPage();
            p.Heading("3. תפריט הכלים / The Tools menu", rtl: true, 13);
            p.Para("תפריט הכלים כולל מספור עמודים, דחיסה, זיהוי תווים אופטי (OCR), הגנת סיסמה, השחרה והסרת מטא-נתונים — כולם פועלים מקומית.", rtl: true);
            p.Para("The Tools menu offers page numbering, compression, OCR, password protection, redaction, and metadata removal — all running locally.", rtl: false);
            p.Space(6);
            p.Para("תודה שבחרתם בסקאלפל. נשמח לשמוע משוב ולשפר את התוכנה עבורכם.", rtl: true);
            p.Para("Thank you for choosing Scalpel.", rtl: false);
            p.Gfx.Dispose();
            doc.Save(path);
        }

        // A 5-page bilingual license agreement (numbered clauses).
        private static void BuildContract(string path)
        {
            using var doc = new PdfDocument { };
            doc.Info.Title = "Scalpel — License Agreement / הסכם רישיון";
            var p = new Pager { Doc = doc };

            // ---- Page 1: parties + preamble ----
            p.NewPage();
            p.Banner("License Agreement", "הסכם רישיון", Surgical);
            p.Heading("הצדדים / Parties", rtl: true, 13);
            p.Row("בין:", "Scalpel Software Ltd.");
            p.Row("לבין:", "Dr. A. Levi Clinic");
            p.Row("תאריך:", "26 June 2026");
            p.Space(8);
            p.Para("הסכם זה קובע את תנאי השימוש בתוכנת סקאלפל. בחתימה על מסמך זה, הצדדים מאשרים את הבנת התנאים והסכמתם להם.", rtl: true);
            p.Para("This agreement sets out the terms for using the Scalpel software. By signing, the parties confirm they understand and accept the terms.", rtl: false);
            p.Para(ProofLine + ".", rtl: true);

            // ---- Page 2: clauses 1-2 ----
            p.NewPage();
            p.Heading("1. הענקת רישיון / Grant of license", rtl: true, 13);
            p.Para("מוענק בזאת רישיון אישי, לא בלעדי ולא ניתן להעברה, לשימוש בתוכנה על מספר בלתי מוגבל של מחשבים בבעלות בעל הרישיון.", rtl: true);
            p.Para("A personal, non-exclusive, non-transferable license is granted to use the software on any number of computers owned by the licensee.", rtl: false);
            p.Space(8);
            p.Heading("2. פרטיות / Privacy", rtl: true, 13);
            p.Para("התוכנה אינה אוספת מידע על המשתמש או על הקבצים שלו, ואינה שולחת נתונים לרשת. כל העיבוד מתבצע במחשב המקומי בלבד.", rtl: true);
            p.Para("The software collects no information about the user or their files and sends no data over the network. All processing is local.", rtl: false);

            // ---- Page 3: clauses 3-4 ----
            p.NewPage();
            p.Heading("3. עדכונים / Updates", rtl: true, 13);
            p.Para("ניתן להפעיל בדיקת עדכונים אופציונלית בהגדרות. הבדיקה אינה שולחת מידע מזהה ומיועדת רק להודיע על גרסה חדשה.", rtl: true);
            p.Para("An optional update check can be enabled in Settings. It sends no identifying information and only notifies you of a new version.", rtl: false);
            p.Space(8);
            p.Heading("4. אחריות / Warranty", rtl: true, 13);
            p.Para("התוכנה מסופקת כמות שהיא, ללא אחריות מכל סוג. בעל הרישיון אחראי לגיבוי המסמכים שלו לפני עריכה.", rtl: true);
            p.Para("The software is provided as-is, without warranty of any kind. The licensee is responsible for backing up documents before editing.", rtl: false);

            // ---- Page 4: clauses 5-6 ----
            p.NewPage();
            p.Heading("5. רישיון קוד פתוח / Open-source license", rtl: true, 13);
            p.Para("סקאלפל מופץ תחת רישיון GPLv3. קוד המקור זמין, וכל משתמש רשאי ללמוד אותו, לשנותו ולהפיצו בהתאם לתנאי הרישיון.", rtl: true);
            p.Para("Scalpel is distributed under the GPLv3 license. The source code is available, and anyone may study, modify, and share it under those terms.", rtl: false);
            p.Space(8);
            p.Heading("6. סיום / Termination", rtl: true, 13);
            p.Para("הרישיון תקף כל עוד המשתמש פועל בהתאם לתנאים. הפרת תנאי מהותי עשויה להביא לסיום הרישיון.", rtl: true);
            p.Para("The license remains valid as long as the user complies with the terms. A material breach may end the license.", rtl: false);

            // ---- Page 5: signatures ----
            p.NewPage();
            p.Heading("7. חתימות / Signatures", rtl: true, 13);
            p.Para("הצדדים מאשרים כי קראו את ההסכם, הבינו את תנאיו, וחתמו עליו מרצונם החופשי.", rtl: true);
            p.Para("The parties confirm they have read this agreement, understood its terms, and signed it of their own free will.", rtl: false);
            p.Space(24);
            p.Row("חתימת הספק:", "______________________");
            p.Space(10);
            p.Row("חתימת הלקוח:", "______________________");
            p.Gfx.Dispose();
            doc.Save(path);
        }

        // ---- the test -------------------------------------------------------------------------

        public static IEnumerable<object[]> Docs()
        {
            yield return new object[] { "scalpel-sample-invoice.pdf", (Action<string>)BuildInvoice, 1 };
            yield return new object[] { "scalpel-sample-letter.pdf", (Action<string>)BuildLetter, 1 };
            yield return new object[] { "scalpel-sample-report.pdf", (Action<string>)BuildReport, 1 };
            yield return new object[] { "scalpel-sample-handbook.pdf", (Action<string>)BuildHandbook, 3 };
            yield return new object[] { "scalpel-sample-contract.pdf", (Action<string>)BuildContract, 5 };
        }

        [Theory]
        [MemberData(nameof(Docs))]
        public void ExampleDoc_Generates_Embeds_AndHebrewRoundTripsLogical(string fileName, Action<string> build, int expectedPages)
        {
            EnsureFonts();
            string outDir = Path.Combine(RepoRoot(), "docs", "samples");
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, fileName);

            build(path);

            Assert.True(File.Exists(path), $"{fileName} was not written");
            Assert.True(FontEmbeddingTests.HasEmbeddedFontProgram(path),
                $"{fileName} must embed its font programs (no reliance on system fonts)");

            // The decisive editing check: locate the embedded proof line and reconstruct it via the
            // SAME path the editor uses (words left-to-right + visual order -> JoinWordsLogical).
            // It must come back in LOGICAL order, identical to what we drew.
            using var pdf = PigDocument.Open(path);
            Assert.Equal(expectedPages, pdf.NumberOfPages);
            string[] target = ProofLine.Split(' ');

            string? recovered = null;
            foreach (var page in pdf.GetPages())
            {
                var words = page.GetWords()
                    .Select(w => (Text: w.Text, Left: w.BoundingBox.Left))
                    .ToList();

                // Find the run of words (on one line) whose reconstruction equals the proof line.
                // Group by rough Y to isolate lines, then test each line.
                foreach (var lineGroup in page.GetWords()
                             .GroupBy(w => Math.Round(w.BoundingBox.Bottom)))
                {
                    var lineWords = lineGroup
                        .Select(w => (Text: w.Text, Left: w.BoundingBox.Left))
                        .ToList();
                    string logical = BidiReorder.JoinWordsLogical(lineWords);
                    if (logical.Contains(target[0]) && logical.Contains(target[^1]))
                    {
                        recovered = logical;
                        break;
                    }
                }
                if (recovered != null) break;
            }

            Assert.False(recovered is null, $"{fileName}: proof Hebrew line not found via word extraction");
            // Every logical word must be present in the recovered line, in order — i.e. not reversed.
            int idx = -1;
            foreach (var word in target)
            {
                int at = recovered!.IndexOf(word, StringComparison.Ordinal);
                Assert.True(at > idx, $"{fileName}: Hebrew word '{word}' out of logical order in '{recovered}'");
                idx = at;
            }
        }
    }
}
