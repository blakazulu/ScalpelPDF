﻿using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using UglyToad.PdfPig;

namespace Scalpel.E2E;

public static class FontHebrewSuite
{
    // Hebrew word "shalom": U+05E9 U+05DC U+05D5 U+05DD
    private const string HebrewShalom = "\u05E9\u05DC\u05D5\u05DD";

    // Arabic word "salam": U+0633 U+0644 U+0627 U+0645 (seen lam alef meem)
    private const string ArabicSalam = "\u0633\u0644\u0627\u0645";

    // Russian word "Privet": U+041F U+0440 U+0438 U+0432 U+0435 U+0442
    private const string RussianPrivet = "\u041F\u0440\u0438\u0432\u0435\u0442";

    // Extra Hebrew suffix to append during "edit existing" so text changes from original.
    // Space + "Tov" (good): U+0020 U+05D8 U+05D5 U+05D1
    private const string HebrewSuffix = " \u05D8\u05D5\u05D1";

    public static void Run(AppDriver driver, ActionRunner runner, RunReport report,
        string openWithPath, string hebrewPath = "", string missingFontPath = "")
    {
        const string Suite = "fonts";

        // Pristine per-scenario copies, taken before any scenario edits a doc. Each new-text
        // scenario relaunches onto its OWN copy so it types onto a clean first page AND never
        // mutates the shared corpus file — otherwise Scenario A's burned-in Hebrew would land
        // exactly where a later suite (the save suite) clicks to place its own text.
        string aDoc = openWithPath + ".scenarioA.pdf";
        string dDoc = openWithPath + ".scenarioD.pdf";
        string eDoc = openWithPath + ".scenarioE.pdf";
        try { File.Copy(openWithPath, aDoc, overwrite: true); } catch { }
        try { File.Copy(openWithPath, dDoc, overwrite: true); } catch { }
        try { File.Copy(openWithPath, eDoc, overwrite: true); } catch { }

        // ─── Scenario A: place NEW Hebrew text annotation ────────────────────────

        // Relaunch onto Scenario A's pristine copy so the shared corpus doc stays untouched.
        driver.Relaunch(aDoc);
        System.Threading.Thread.Sleep(1200);

        // Step 1: Edit mode. The relaunch above re-applied the settings baseline (single-page
        // view, 100% zoom), so the page preview is already in the right layout for canvas
        // placement — only the ribbon mode needs switching.
        report.Results.Add(runner.RunRaw(Suite, "mode:edit",
            () => driver.EnsureSurface(Surface.EditMode), null, null));

        // Step 2: Activate the Text tool.
        report.Results.Add(runner.RunRaw(Suite, "tool:text",
            () => driver.Click("ToolTextBtn"), null, null));

        // Steps 3-5 combined into ONE RunRaw so RunRaw's post-action FocusMainWindow()
        // is not called between the canvas click (which creates a TextBox), Hebrew typing,
        // and the commit — keeping keyboard focus on the annotation TextBox throughout.
        report.Results.Add(runner.RunRaw(Suite, "canvas:place-type-commit",
            () => driver.WithForeground(() =>
            {
                // 3-5. Place the annotation box on the canvas and set the Hebrew word via the UIA
                //      Value pattern (foreground-independent; reliably lands Unicode), retrying the
                //      placement click if it misses.
                driver.PlaceAndSetAnnotationText(HebrewShalom);
                System.Threading.Thread.Sleep(200);
                // 6. Commit the annotation by re-clicking ToolTextBtn.
                //    SetTool(EditTool.Text) calls CommitActiveTextBox() first (MainWindow line ~3219),
                //    which saves the TextBox content to _annotations and removes the live TextBox.
                //    This is the only reliable commit path from automation: Enter inserts a newline
                //    (AcceptsReturn=true causes WPF's InputBinding to mark the event Handled before
                //    TextBox_KeyDown fires), and clicking the canvas creates a second empty TextBox.
                driver.Click("ToolTextBtn");
                System.Threading.Thread.Sleep(300);
            }), null, null));

        // Give the UI a moment to render the committed annotation overlay.
        System.Threading.Thread.Sleep(400);

        // Step 7: Save in-place via Ctrl+S (saves back to the corpus temp file).
        // SaveInPlace() also calls CommitActiveTextBox() as a safety net.
        report.Results.Add(runner.RunRaw(Suite, "save:ctrl-s",
            () => driver.WithForeground(() =>
            {
                System.Threading.Thread.Sleep(200);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                {
                    Keyboard.Press(VirtualKeyShort.KEY_S);
                }
            }), null, null));

        // Allow the save to flush to disk.
        System.Threading.Thread.Sleep(2000);

        // Step 8: Final assertion — inspect the saved PDF with PdfPig and verify
        // at least one Hebrew-block character (U+0590–U+05FF) was burned in.
        report.Results.Add(AssertHebrewInPdf(Suite, aDoc, "assert:hebrew-in-pdf"));

        // ─── Scenario D: place NEW Arabic text annotation ────────────────────────
        // Same place→type→commit→save flow, with Arabic. Exercises ArabicShaper
        // (cursive joining), BidiReorder (Arabic ranges), and PickFace → Noto Sans Arabic.
        // Assert (PdfPig) a char in an Arabic block burned in: base (U+0600–06FF) or
        // presentation forms A/B (U+FB50–FDFF, U+FE70–FEFF, what the shaper emits).
        // Relaunch onto a pristine copy first so this is a clean first-annotation and the
        // assertion reads exactly the file that was edited.
        driver.Relaunch(dDoc);
        System.Threading.Thread.Sleep(1200);
        PlaceNewTextScenario(driver, runner, report, Suite, "D", ArabicSalam);
        report.Results.Add(AssertScriptInPdf(Suite, dDoc, "D:assert:arabic-in-pdf", "Arabic",
            new[] { ('؀', 'ۿ'), ('ﭐ', '﷿'), ('ﹰ', '﻿') }));

        // ─── Scenario E: place NEW Russian (Cyrillic) text annotation ────────────
        // LTR but the candidate "Geist" lacks Cyrillic; PickFace must fall back to
        // Noto Sans so it renders glyphs, not .notdef boxes. Assert a Cyrillic-block
        // char (U+0400–04FF) is present (proves real glyphs were drawn).
        driver.Relaunch(eDoc);
        System.Threading.Thread.Sleep(1200);
        PlaceNewTextScenario(driver, runner, report, Suite, "E", RussianPrivet);
        report.Results.Add(AssertScriptInPdf(Suite, eDoc, "E:assert:cyrillic-in-pdf", "Cyrillic",
            new[] { ('Ѐ', 'ӿ') }));

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

        // ─── Scenario F: the edit-a-line flow ─────────────────────────────────────
        // Double-click picks the WHOLE line; re-editing shows the NEW text (not the old words
        // still in the file); saving removes only that line's original text, never the similar
        // "SUBTOTAL" line; the replacement is drawn in the line's own font, not a fallback.
        string editLinePath = string.IsNullOrEmpty(hebrewPath) ? ""
            : Path.Combine(Path.GetDirectoryName(hebrewPath) ?? "", "editline-1p.pdf");
        if (File.Exists(editLinePath))
            RunScenarioF(driver, report, Suite, editLinePath);
        else
            report.Results.Add(new ActionResult(Suite, "F:skip", Outcome.Fail,
                $"editline-1p fixture not available ({editLinePath})", Array.Empty<LogEntry>()));

        // ─── Scenario G: editing on a rotated page ───────────────────────────────
        string rotatedPath = string.IsNullOrEmpty(hebrewPath) ? ""
            : Path.Combine(Path.GetDirectoryName(hebrewPath) ?? "", "rotated-1p.pdf");
        if (File.Exists(rotatedPath))
            RunScenarioG(driver, report, Suite, rotatedPath);
        else
            report.Results.Add(new ActionResult(Suite, "G:skip", Outcome.Fail,
                $"rotated-1p fixture not available ({rotatedPath})", Array.Empty<LogEntry>()));

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
            driver.WithForeground(() => driver.DoubleClickCanvas());
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

            // B-5/B-6: under the foreground gate — focus the edit box, append the Hebrew suffix,
            // commit with Enter (EditTextBox_KeyDown handles it; AcceptsReturn=false), then save.
            driver.WithForeground(() =>
            {
                try { editTb.Click(); } catch { }
                System.Threading.Thread.Sleep(150);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                    Keyboard.Press(VirtualKeyShort.KEY_A);
                System.Threading.Thread.Sleep(100);
                Keyboard.Press(VirtualKeyShort.END);
                System.Threading.Thread.Sleep(100);
                driver.TypeText(HebrewSuffix);
                System.Threading.Thread.Sleep(300);

                Keyboard.Press(VirtualKeyShort.RETURN);
                System.Threading.Thread.Sleep(500);

                System.Threading.Thread.Sleep(200);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                    Keyboard.Press(VirtualKeyShort.KEY_S);
            });
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
    // Scenario F — double-click edit of a whole line, re-edit, save
    // ─────────────────────────────────────────────────────────────────────────────
    private static void RunScenarioF(AppDriver driver, RunReport report, string suite, string path)
    {
        var none = Array.Empty<LogEntry>();
        void Check(string name, bool ok, string detail)
            => report.Results.Add(new ActionResult(suite, name, ok ? Outcome.Pass : Outcome.Fail, ok ? null : detail, none));

        try
        {
            driver.Relaunch(path);
            System.Threading.Thread.Sleep(1200);
            if (!driver.IsAlive) { Check("F:relaunch", false, "app not alive"); return; }
            driver.EnsureSurface(Surface.EditMode);
            System.Threading.Thread.Sleep(300);
            driver.Click("ToolSelectBtn");
            System.Threading.Thread.Sleep(300);

            // F-1: double-click the TOTAL line on its LAST word: the box must hold the whole line.
            driver.WithForeground(() => driver.DoubleClickCanvas(0.52, 0.45));
            System.Threading.Thread.Sleep(1200);
            string? first = driver.ReadActiveTextBox();
            Check("F:assert:whole-line", first == "TOTAL 100 USD",
                $"edit box held '{first ?? "(no box)"}', expected the whole line 'TOTAL 100 USD'");
            if (first is null) return;

            // F-2: change it and commit with Enter.
            driver.SetActiveAnnotationText("TOTAL 250 USD");
            driver.WithForeground(() =>
            {
                try { driver.FindAnyTextBox()?.Click(); } catch { }
                System.Threading.Thread.Sleep(150);
                Keyboard.Press(VirtualKeyShort.END);
                Keyboard.Press(VirtualKeyShort.RETURN);
            });
            System.Threading.Thread.Sleep(600);

            // F-3: double-click the same line again, a little above centre: the box must show
            // the EDITED text, not the original words that are still in the working file.
            driver.WithForeground(() => driver.DoubleClickCanvas(0.40, 0.443));
            System.Threading.Thread.Sleep(1200);
            string? again = driver.ReadActiveTextBox();
            Check("F:assert:reedit-shows-new-text", again == "TOTAL 250 USD",
                $"re-edit box held '{again ?? "(no box)"}', expected 'TOTAL 250 USD'");

            // F-4: leave the re-edit unchanged and save.
            driver.WithForeground(() =>
            {
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                System.Threading.Thread.Sleep(300);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                    Keyboard.Press(VirtualKeyShort.KEY_S);
            });
            System.Threading.Thread.Sleep(2500);

            // F-5: the saved file.
            using var doc = PdfDocument.Open(path);
            var words = doc.GetPage(1).GetWords().ToList();
            string all = string.Join(" ", words.Select(w => w.Text));
            Check("F:assert:new-text-saved", words.Any(w => w.Text == "250"),
                $"'250' not found in saved text: '{all}'");
            Check("F:assert:original-removed", words.Count(w => w.Text == "100") == 1,
                $"expected exactly one '100' (the SUBTOTAL line's) after removing the edited original: '{all}'");
            Check("F:assert:similar-line-kept", words.Any(w => w.Text == "SUBTOTAL"),
                $"the SUBTOTAL line was removed: '{all}'");
            var newFonts = words.Where(w => w.Text == "250")
                .SelectMany(w => w.Letters).Select(l => l.FontName ?? "").Distinct().ToList();
            Check("F:assert:line-font-used", newFonts.Count > 0 && newFonts.All(f => f.Contains("Arial")),
                $"replacement drawn in [{string.Join(", ", newFonts)}], expected the line's own Arial");
        }
        catch (Exception ex)
        {
            Check("F:exception", false, $"Scenario F threw: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Scenario G — double-click edit on a rotated page, before and after a page operation
    // ─────────────────────────────────────────────────────────────────────────────
    private static void RunScenarioG(AppDriver driver, RunReport report, string suite, string path)
    {
        var none = Array.Empty<LogEntry>();
        void Check(string name, bool ok, string detail)
            => report.Results.Add(new ActionResult(suite, name, ok ? Outcome.Pass : Outcome.Fail, ok ? null : detail, none));
        const string Expected = "ROTATED LINE OK";

        string? OpenEditAtCentre()
        {
            driver.EnsureSurface(Surface.EditMode);
            System.Threading.Thread.Sleep(300);
            driver.Click("ToolSelectBtn");
            System.Threading.Thread.Sleep(300);
            driver.WithForeground(() => driver.DoubleClickCanvas(0.5, 0.5));
            System.Threading.Thread.Sleep(1200);
            string? text = driver.ReadActiveTextBox();
            driver.WithForeground(() => Keyboard.Press(VirtualKeyShort.ESCAPE));
            System.Threading.Thread.Sleep(300);
            return text;
        }

        try
        {
            driver.Relaunch(path);
            System.Threading.Thread.Sleep(1200);
            if (!driver.IsAlive) { Check("G:relaunch", false, "app not alive"); return; }

            // G-1: the file still carries /Rotate 90.
            string? fresh = OpenEditAtCentre();
            Check("G:assert:rotated-page-line", fresh == Expected,
                $"edit box held '{fresh ?? "(no box)"}' on the /Rotate 90 page, expected '{Expected}'");

            // G-2: four quarter turns bring the text upright again, but now Scalpel holds the
            // rotation itself and the working file says /Rotate 0.
            int turns = 0;
            for (int i = 0; i < 4; i++)
            {
                if (TabsSuite.InvokeRotate(driver)) turns++;
                System.Threading.Thread.Sleep(900);
            }
            if (turns != 4) { Check("G:rotate", false, $"rotate invoked {turns}/4 times"); return; }
            string? afterOp = OpenEditAtCentre();
            Check("G:assert:after-page-op-line", afterOp == Expected,
                $"edit box held '{afterOp ?? "(no box)"}' after rotating the page, expected '{Expected}'");

            if (driver.HasOpenModal()) driver.AnswerDialog("No");
        }
        catch (Exception ex)
        {
            Check("G:exception", false, $"Scenario G threw: {ex.Message}");
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
            // The fixture centres "Hello World" on the page (XStringFormats.Center), so click the
            // page centre. The old 45% click landed ~40pt above the text and only worked because
            // a double-click on empty space used to jump to the nearest line anywhere on the page.
            driver.WithForeground(() => driver.DoubleClickCanvas(0.5, 0.5));
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

    // Shared place\u2192type\u2192commit\u2192save flow for a NEW text annotation (mirrors Scenario A).
    // Activates the Text tool, clicks the canvas, types, commits by re-clicking the tool,
    // then saves in place via Ctrl+S. Each step is its own logged action.
    private static void PlaceNewTextScenario(AppDriver driver, ActionRunner runner,
        RunReport report, string suite, string label, string text)
    {
        // Edit mode. The relaunch re-applied the settings baseline (single-page view), so only
        // the ribbon mode needs switching; ToolTextBtn lives in the Edit ribbon panel.
        report.Results.Add(runner.RunRaw(suite, $"{label}:mode:edit",
            () => driver.EnsureSurface(Surface.EditMode), null, null));

        report.Results.Add(runner.RunRaw(suite, $"{label}:tool:text",
            () => driver.Click("ToolTextBtn"), null, null));

        report.Results.Add(runner.RunRaw(suite, $"{label}:canvas:place-type-commit",
            () => driver.WithForeground(() =>
            {
                driver.PlaceAndSetAnnotationText(text);
                System.Threading.Thread.Sleep(200);
                driver.Click("ToolTextBtn"); // re-click commits the active TextBox
                System.Threading.Thread.Sleep(300);
            }), null, null));

        System.Threading.Thread.Sleep(400);

        report.Results.Add(runner.RunRaw(suite, $"{label}:save:ctrl-s",
            () => driver.WithForeground(() =>
            {
                System.Threading.Thread.Sleep(200);
                using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
                    Keyboard.Press(VirtualKeyShort.KEY_S);
            }), null, null));

        System.Threading.Thread.Sleep(2000);
    }

    // Generic "a char in one of these Unicode ranges was burned into the saved PDF" assertion.
    private static ActionResult AssertScriptInPdf(string suite, string pdfPath, string actionName,
        string scriptLabel, (char lo, char hi)[] ranges)
    {
        var emptyLogs = Array.Empty<LogEntry>();
        try
        {
            if (!File.Exists(pdfPath))
                return new ActionResult(suite, actionName, Outcome.Fail,
                    $"PDF not found at {pdfPath}", emptyLogs);

            using var doc = PdfDocument.Open(pdfPath);
            var allText = new System.Text.StringBuilder();
            int pageCount = 0;
            foreach (var page in doc.GetPages())
            {
                pageCount++;
                string text = page.Text ?? string.Empty;
                allText.Append(text);
                foreach (char c in text)
                    foreach (var (lo, hi) in ranges)
                        if (c >= lo && c <= hi)
                            return new ActionResult(suite, actionName, Outcome.Pass, null, emptyLogs);
            }

            string extracted = allText.ToString();
            string preview = extracted.Length > 120 ? extracted.Substring(0, 120) : extracted;
            return new ActionResult(suite, actionName, Outcome.Fail,
                $"No {scriptLabel} character found in saved PDF (pages={pageCount}, " +
                $"text-len={extracted.Length}, preview='{preview}'). Annotation may not have " +
                "committed, typing may not have landed, or font lacks a ToUnicode CMap.",
                emptyLogs);
        }
        catch (Exception ex)
        {
            return new ActionResult(suite, actionName, Outcome.Fail,
                $"Exception inspecting saved PDF: {ex.Message}", emptyLogs);
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
