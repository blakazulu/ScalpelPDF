﻿using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using UglyToad.PdfPig;

namespace Scalpel.E2E;

public static class FontHebrewSuite
{
    // Hebrew word "shalom": U+05E9 U+05DC U+05D5 U+05DD
    private const string HebrewShalom = "\u05E9\u05DC\u05D5\u05DD";

    // Extra Hebrew suffix to append during "edit existing" so text changes from original.
    // Space + "Tov" (good): U+0020 U+05D8 U+05D5 U+05D1
    private const string HebrewSuffix = " \u05D8\u05D5\u05D1";

    public static void Run(AppDriver driver, ActionRunner runner, RunReport report,
        string openWithPath, string hebrewPath = "", string missingFontPath = "")
    {
        const string Suite = "fonts";

        // ─── Scenario A: place NEW Hebrew text annotation ────────────────────────

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
        report.Results.Add(AssertHebrewInPdf(Suite, openWithPath, "assert:hebrew-in-pdf"));

        // ─── Scenario B: edit EXISTING Hebrew text ───────────────────────────────
        // Relaunch the app with the hebrew-1p fixture (has "shalom" drawn near center).
        // Switch to Edit mode, use Select tool, DOUBLE-CLICK the canvas to open the
        // inline edit box pre-filled with the PDF word, append more Hebrew, commit,
        // save, then assert a Hebrew char is still in the PDF via PdfPig.
        // We do NOT use RunRaw here because Relaunch starts a new log session.
        if (!string.IsNullOrEmpty(hebrewPath) && File.Exists(hebrewPath))
        {
            RunScenarioB(driver, report, Suite, hebrewPath);
        }
        else
        {
            report.Results.Add(new ActionResult(Suite, "B:skip", Outcome.Fail,
                $"hebrew-1p fixture not available ({hebrewPath})",
                Array.Empty<LogEntry>()));
        }

        // ─── Scenario C: font-missing toast ──────────────────────────────────────
        // Relaunch with missingfont-1p, Edit mode, Select tool, DOUBLE-CLICK the text.
        // The FontResolver should report "MadeUpFontXYZ123" not installed and show toast.
        // Assert ToastCopyBtn appears within ~2.5s.
        if (!string.IsNullOrEmpty(missingFontPath) && File.Exists(missingFontPath))
        {
            RunScenarioC(driver, report, Suite, missingFontPath);
        }
        else
        {
            report.Results.Add(new ActionResult(Suite, "C:skip", Outcome.Fail,
                $"missingfont-1p fixture not available ({missingFontPath})",
                Array.Empty<LogEntry>()));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Scenario B — edit existing Hebrew text in a PDF
    // ─────────────────────────────────────────────────────────────────────────────
    private static void RunScenarioB(AppDriver driver, RunReport report, string suite,
        string hebrewPath)
    {
        var emptyLogs = Array.Empty<LogEntry>();

        try
        {
            // B-1: relaunch with the Hebrew fixture
            driver.Relaunch(hebrewPath);
            System.Threading.Thread.Sleep(1200);

            if (!driver.IsAlive)
            {
                report.Results.Add(new ActionResult(suite, "B:relaunch", Outcome.Fail,
                    "app not alive after relaunch with hebrew-1p", emptyLogs));
                return;
            }
            report.Results.Add(new ActionResult(suite, "B:relaunch", Outcome.Pass,
                null, emptyLogs));

            // B-2: switch to Edit mode, then Select tool.
            // EditTextAtPosition fires on DOUBLE-CLICK when EditTool.Select is active
            // (Canvas_MouseLeftButtonDown line ~5169: ClickCount==2 -> EditTextAtPosition).
            driver.EnsureSurface(Surface.EditMode);
            System.Threading.Thread.Sleep(300);
            driver.Click("ToolSelectBtn");
            System.Threading.Thread.Sleep(300);

            // B-3: double-click the canvas at center (~45%) to open the edit box.
            // The Hebrew glyph drawn by Corpus.WriteHebrew1P is at ~45% of the page.
            driver.DoubleClickCanvas();
            System.Threading.Thread.Sleep(1200); // wait for PdfPig word extraction + TextBox render

            // B-4: assert the edit box appeared (unnamed TextBox in UIA tree)
            var editTb = driver.FindAnyTextBox();
            if (editTb == null)
            {
                report.Results.Add(new ActionResult(suite, "B:assert:editbox-open", Outcome.Fail,
                    "No unnamed TextBox found in UIA tree after double-click — " +
                    "edit box did not open. Canvas may not have hit the text region.",
                    emptyLogs));
                // Try to recover for scenario C.
                driver.PressKey(VirtualKeyShort.ESCAPE);
                return;
            }
            report.Results.Add(new ActionResult(suite, "B:assert:editbox-open", Outcome.Pass,
                null, emptyLogs));

            // B-5: ensure focus on the edit box, then append the Hebrew suffix.
            // Select-all then End to move caret to end; then type suffix.
            try { editTb.Click(); } catch { }
            System.Threading.Thread.Sleep(150);
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                Keyboard.Press(VirtualKeyShort.KEY_A);
            System.Threading.Thread.Sleep(100);
            Keyboard.Press(VirtualKeyShort.END);
            System.Threading.Thread.Sleep(100);
            driver.TypeText(HebrewSuffix);
            System.Threading.Thread.Sleep(300);

            // B-6: commit by pressing Enter (EditTextBox_KeyDown handles Enter for TextEditContext;
            // AcceptsReturn=false so Enter commits). Then save Ctrl+S.
            Keyboard.Press(VirtualKeyShort.RETURN);
            System.Threading.Thread.Sleep(500);

            driver.FocusMainWindow();
            System.Threading.Thread.Sleep(200);
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                Keyboard.Press(VirtualKeyShort.KEY_S);
            System.Threading.Thread.Sleep(2500);

            // B-7: assert Hebrew char still in PDF
            report.Results.Add(AssertHebrewInPdf(suite, hebrewPath, "B:assert:edit-hebrew"));
        }
        catch (Exception ex)
        {
            report.Results.Add(new ActionResult(suite, "B:exception", Outcome.Fail,
                $"Scenario B threw: {ex.Message}", emptyLogs));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Scenario C — font-missing toast appears when editing text with unknown font
    // ─────────────────────────────────────────────────────────────────────────────
    private static void RunScenarioC(AppDriver driver, RunReport report, string suite,
        string missingFontPath)
    {
        var emptyLogs = Array.Empty<LogEntry>();

        try
        {
            // C-1: relaunch with the missing-font fixture
            driver.Relaunch(missingFontPath);
            System.Threading.Thread.Sleep(1200);

            if (!driver.IsAlive)
            {
                report.Results.Add(new ActionResult(suite, "C:relaunch", Outcome.Fail,
                    "app not alive after relaunch with missingfont-1p", emptyLogs));
                return;
            }
            report.Results.Add(new ActionResult(suite, "C:relaunch", Outcome.Pass,
                null, emptyLogs));

            // C-2: Edit mode + Select tool
            driver.EnsureSurface(Surface.EditMode);
            System.Threading.Thread.Sleep(300);
            driver.Click("ToolSelectBtn");
            System.Threading.Thread.Sleep(300);

            // C-3: double-click center to open edit box -> triggers font-missing toast.
            // ShowToast fires at MainWindow.xaml.cs ~line 6504 when
            // FontResolver.Resolve(rawFont,...).IsInstalled == false.
            driver.DoubleClickCanvas();
            System.Threading.Thread.Sleep(1500); // wait for PdfPig + FontResolver + toast render

            // Check whether the edit box opened (diagnostic only, not the main assertion)
            var editTb = driver.FindAnyTextBox();
            string editBoxInfo = editTb != null ? "editbox=open" : "editbox=null";

            // C-4: assert ToastCopyBtn is present and visible within ~2.5s total.
            // Toast auto-dismisses after ~4s; we poll up to 5 x 500ms = 2.5s.
            // ToastCopyBtn: x:Name="ToastCopyBtn" -> AutomationId="ToastCopyBtn"
            bool toastFound = false;
            string toastDiag = "";
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var toastBtn = driver.Find("ToastCopyBtn");
                if (toastBtn != null)
                {
                    try
                    {
                        bool offscreen = toastBtn.Properties.IsOffscreen.ValueOrDefault;
                        if (!offscreen)
                        {
                            toastFound = true;
                            toastDiag = $"ToastCopyBtn found and onscreen (attempt={attempt + 1})";
                            break;
                        }
                        toastDiag = $"ToastCopyBtn found but offscreen (attempt={attempt + 1})";
                    }
                    catch
                    {
                        // If IsOffscreen is unreadable, treat as found.
                        toastFound = true;
                        toastDiag = $"ToastCopyBtn found (IsOffscreen unreadable, attempt={attempt + 1})";
                        break;
                    }
                }
                else
                {
                    toastDiag = $"ToastCopyBtn not in UIA tree (attempt={attempt + 1})";
                }
                System.Threading.Thread.Sleep(500);
            }

            if (toastFound)
            {
                report.Results.Add(new ActionResult(suite, "C:assert:font-toast", Outcome.Pass,
                    null, emptyLogs));
            }
            else
            {
                report.Results.Add(new ActionResult(suite, "C:assert:font-toast", Outcome.Fail,
                    $"Font-missing toast not found. {toastDiag}; {editBoxInfo}. " +
                    "ToastCopyBtn searched by AutomationId in UIA tree. " +
                    "Possible causes: double-click missed text (no PdfPig words at 45%/45%), " +
                    "or toast element collapsed/hidden in UIA (IsOffscreen=true while visible).",
                    emptyLogs));
            }

            // C-5: dismiss edit box
            driver.PressKey(VirtualKeyShort.ESCAPE);
            System.Threading.Thread.Sleep(200);
        }
        catch (Exception ex)
        {
            report.Results.Add(new ActionResult(suite, "C:exception", Outcome.Fail,
                $"Scenario C threw: {ex.Message}", emptyLogs));
        }
    }

    // Hebrew range bounds as explicit escapes: U+0590..U+05FF
    private const char HebrewBlockStart = '\u0590';
    private const char HebrewBlockEnd   = '\u05FF';

    private static ActionResult AssertHebrewInPdf(string suite, string pdfPath, string actionName)
    {
        var emptyLogs = Array.Empty<LogEntry>();
        try
        {
            if (!File.Exists(pdfPath))
                return new ActionResult(suite, actionName, Outcome.Fail,
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
                    if (c >= HebrewBlockStart && c <= HebrewBlockEnd)
                    {
                        foundHebrew = true;
                        break;
                    }
                }
                if (foundHebrew) break;
            }

            if (foundHebrew)
                return new ActionResult(suite, actionName, Outcome.Pass, null, emptyLogs);

            string extracted = allText.ToString();
            string preview = extracted.Length > 120 ? extracted.Substring(0, 120) : extracted;
            return new ActionResult(suite, actionName, Outcome.Fail,
                $"No Hebrew-block character (U+0590–U+05FF) found in saved PDF " +
                $"(pages={pageCount}, text-len={extracted.Length}, preview='{preview}'). " +
                "Annotation may not have committed, typing may not have landed, or font lacks ToUnicode CMap.",
                emptyLogs);
        }
        catch (Exception ex)
        {
            return new ActionResult(suite, actionName, Outcome.Fail,
                $"Exception inspecting saved PDF: {ex.Message}", emptyLogs);
        }
    }
}
