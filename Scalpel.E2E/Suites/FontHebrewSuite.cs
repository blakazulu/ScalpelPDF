using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using UglyToad.PdfPig;

namespace Scalpel.E2E;

public static class FontHebrewSuite
{
    // Hebrew word "shalom": U+05E9 U+05DC U+05D5 U+05DD
    private const string HebrewShalom = "\u05E9\u05DC\u05D5\u05DD";

    public static void Run(AppDriver driver, ActionRunner runner, RunReport report, string openWithPath)
    {
        const string Suite = "fonts";

        // Step 1: Switch to Edit mode.
        report.Results.Add(runner.RunRaw(Suite, "mode:edit",
            () => driver.EnsureSurface(Surface.EditMode), null, null));

        // Step 2: Activate the Text tool.
        report.Results.Add(runner.RunRaw(Suite, "tool:text",
            () => driver.Click("ToolTextBtn"), null, null));

        // Steps 3-5 combined into ONE RunRaw so RunRaw's post-action FocusMainWindow()
        // is not called between the canvas click (which creates a TextBox), Hebrew typing,
        // and the commit — keeping keyboard focus on the annotation TextBox throughout.
        report.Results.Add(runner.RunRaw(Suite, "canvas:place-type-commit",
            () =>
            {
                // 3. Click the canvas to place an annotation TextBox.
                driver.ClickCanvas();
                // 4. Wait for the TextBox to be rendered and receive keyboard focus
                //    (focus is given via tb.Loaded += (s,e) => tb.Focus() inside PlaceTextBox).
                System.Threading.Thread.Sleep(600);
                // Click the annotation TextBox via UIA to ensure keyboard focus.
                var annTb = driver.FindAnyTextBox();
                annTb?.Click();
                System.Threading.Thread.Sleep(150);
                // 5. Type the Hebrew word. Keyboard.Type sends Unicode via SendInput/WM_CHAR.
                driver.TypeText(HebrewShalom);
                System.Threading.Thread.Sleep(200);
                // 6. Commit the annotation by re-clicking ToolTextBtn.
                //    SetTool(EditTool.Text) calls CommitActiveTextBox() first (MainWindow line ~3219),
                //    which saves the TextBox content to _annotations and removes the live TextBox.
                //    This is the only reliable commit path from automation: Enter inserts a newline
                //    (AcceptsReturn=true causes WPF's InputBinding to mark the event Handled before
                //    TextBox_KeyDown fires), and clicking the canvas creates a second empty TextBox.
                driver.Click("ToolTextBtn");
                System.Threading.Thread.Sleep(300);
            }, null, null));

        // Give the UI a moment to render the committed annotation overlay.
        System.Threading.Thread.Sleep(400);

        // Step 7: Save in-place via Ctrl+S (saves back to the corpus temp file).
        // SaveInPlace() also calls CommitActiveTextBox() as a safety net.
        report.Results.Add(runner.RunRaw(Suite, "save:ctrl-s",
            () =>
            {
                driver.FocusMainWindow();
                System.Threading.Thread.Sleep(200);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                {
                    Keyboard.Press(VirtualKeyShort.KEY_S);
                }
            }, null, null));

        // Allow the save to flush to disk.
        System.Threading.Thread.Sleep(2000);

        // Step 8: Final assertion — inspect the saved PDF with PdfPig and verify
        // at least one Hebrew-block character (U+0590–U+05FF) was burned in.
        report.Results.Add(AssertHebrewInPdf(Suite, openWithPath));
    }

    private static ActionResult AssertHebrewInPdf(string suite, string pdfPath)
    {
        var emptyLogs = Array.Empty<LogEntry>();
        try
        {
            if (!File.Exists(pdfPath))
                return new ActionResult(suite, "assert:hebrew-in-pdf", Outcome.Fail,
                    $"PDF not found at {pdfPath}", emptyLogs);

            using var doc = PdfDocument.Open(pdfPath);
            int pageCount = 0;
            bool foundHebrew = false;
            var allText = new System.Text.StringBuilder();

            foreach (var page in doc.GetPages())
            {
                pageCount++;
                string text = page.Text ?? string.Empty;
                allText.Append(text);
                foreach (char c in text)
                {
                    // Hebrew Unicode block: U+0590 – U+05FF
                    if (c >= '\u0590' && c <= '\u05FF')
                    {
                        foundHebrew = true;
                        break;
                    }
                }
                if (foundHebrew) break;
            }

            if (foundHebrew)
                return new ActionResult(suite, "assert:hebrew-in-pdf", Outcome.Pass,
                    null, emptyLogs);

            string extracted = allText.ToString();
            string preview = extracted.Length > 120 ? extracted.Substring(0, 120) : extracted;
            return new ActionResult(suite, "assert:hebrew-in-pdf", Outcome.Fail,
                $"No Hebrew-block character (U+0590–U+05FF) found in saved PDF " +
                $"(pages={pageCount}, text-len={extracted.Length}, preview='{preview}'). " +
                "Annotation may not have committed, typing may not have landed, or font lacks ToUnicode CMap.",
                emptyLogs);
        }
        catch (Exception ex)
        {
            return new ActionResult(suite, "assert:hebrew-in-pdf", Outcome.Fail,
                $"Exception inspecting saved PDF: {ex.Message}", emptyLogs);
        }
    }
}
