using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Scalpel.E2E;

/// <summary>
/// End-to-end coverage for Chrome-style document tabs (the 2026-09-22 chrome-tabs plan): a live
/// <c>DocumentSession</c> per tab, the option-C chip strip, multi-open, the busy gate
/// (<c>LongOperationGate</c>) that blocks switching during a long operation, and the per-tab
/// dirty/close/save prompts. Each scenario below relaunches onto its own private corpus copy (the
/// same isolation pattern <see cref="SaveVerifySuite"/> uses) so the scenarios never interfere with
/// each other or with suites that run before/after this one.
///
/// The tab chips themselves (<c>BuildTabChip</c> in <c>MainWindow.Tabs.cs</c>) are Borders built in
/// code-behind with no automation peer, so a chip cannot be Invoked. Each chip's label TextBlock
/// carries the automation id <c>TabChipLabel</c> (Name = file name, in strip order), which is how
/// the scenarios read the tab list and locate a chip for a physical right-click (the tab menu's
/// items carry <c>TabMenu*</c> ids). Everything else drives tabs the way a keyboard-only user
/// would (Ctrl+O, Ctrl+W, Ctrl+Tab, Ctrl+1..9, Ctrl+PageUp/Down, Ctrl+Shift+S, Ctrl+T), and reads
/// back which document is showing via <c>FileNameLabel</c> (<see cref="AppDriver.ReadText"/>) - a
/// `DisplayName` (bare file name) that is unique per corpus file used here, so it doubles as
/// "which tab is active".
///
/// Not automated (manual checks): drag-to-reorder a chip (a real mouse drag across chips) and
/// middle-click-to-close - both need raw physical mouse gestures that cannot be driven reliably.
/// </summary>
public static class TabsSuite
{
    public static void Run(AppDriver driver, RunReport report, string openWithPath, string largeFilePath)
    {
        const string Suite = "tabs";
        string dir = System.IO.Path.GetDirectoryName(openWithPath)!;
        string fileA = System.IO.Path.Combine(dir, "tabs-a.pdf");
        string fileB = System.IO.Path.Combine(dir, "tabs-b.pdf");
        string fileC = System.IO.Path.Combine(dir, "tabs-c.pdf");
        string fileD = System.IO.Path.Combine(dir, "tabs-d.pdf");
        string multiPage = System.IO.Path.Combine(dir, "tabs-multi-8p.pdf");
        string hugePath = System.IO.Path.Combine(dir, "tabs-huge-150p.pdf");
        // The startup restore only remembers durable files: SaveOpenTabsSetting skips anything
        // under %TEMP% (IsTransientPath), which is where the corpus lives. The restore scenario's
        // files therefore go to a harness-owned folder outside %TEMP%, deleted at the end.
        string restoreDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScalpelE2E-restore");
        string restoreA = System.IO.Path.Combine(restoreDir, "restore-a.pdf");
        string restoreB = System.IO.Path.Combine(restoreDir, "restore-b.pdf");
        string restoreC = System.IO.Path.Combine(restoreDir, "restore-c.pdf");
        try
        {
            File.Copy(openWithPath, fileA, overwrite: true);
            File.Copy(openWithPath, fileB, overwrite: true);
            File.Copy(openWithPath, fileC, overwrite: true);
            File.Copy(openWithPath, fileD, overwrite: true);
            WriteNumberedPdf(multiPage, 8);
            WriteNumberedPdf(hugePath, 150);
            Directory.CreateDirectory(restoreDir);
            File.Copy(openWithPath, restoreA, overwrite: true);
            File.Copy(openWithPath, restoreB, overwrite: true);
            File.Copy(openWithPath, restoreC, overwrite: true);
        }
        catch { }

        SwitchPreservesAnnotationAndDirty(driver, report, Suite, fileA, fileB);
        CloseDirtyTab_Cancel(driver, report, Suite, fileA);
        MultiOpen_ThreeFiles(driver, report, Suite, fileA, fileB, fileC);
        AllTabsChevron_WithManyTabs(driver, report, Suite, fileA);
        SaveAsRenamesTab(driver, report, Suite, fileA);
        TabNumberAndPageKeys(driver, report, Suite, fileA, fileB, fileC, fileD);
        ZoomPresetsMoved(driver, report, Suite, fileA);
        PerTabViewStateKept(driver, report, Suite, multiPage, fileB);
        AlreadyOpenSwitches(driver, report, Suite, fileA, fileB);
        TabContextMenu(driver, report, Suite, fileA, fileB, fileC, fileD);
        TempFilesFreedOnTabClose(driver, report, Suite, fileA, fileB);
        RestoreTabsAtStartup(driver, report, Suite, restoreA, restoreB, restoreC);
        ForwardedMultiOpen(driver, report, Suite, fileA, fileB, fileC);
        // A long operation needs enough pages that Compress is still running a moment later;
        // run this before the window-close scenario, which ends the process.
        LongOperationBlocksSwitching(driver, report, Suite, largeFilePath, fileB);
        OpenDuringLongOperationIsQueued(driver, report, Suite, hugePath, fileB);
        // Ends the app (both dirty tabs answered "Don't save") - keep this last.
        WindowClose_TwoDirtyTabs(driver, report, Suite, fileA, fileB);

        // Leave a clean single-document baseline for whatever suite runs after this one.
        driver.Relaunch(openWithPath);
        System.Threading.Thread.Sleep(800);
        // The restored-tabs files are no longer open anywhere now; the OpenTabs setting that
        // pointed at them is put back to the user's own value by AppSettingsGuard at exit.
        try { Directory.Delete(restoreDir, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------
    // Scenario 1: open two files, annotate A, switch to B and back - the annotation (and its
    // dirty flag) must survive the round trip - then the dirty marker's real effect: closing that
    // tab raises the save prompt, and "Don't save" actually closes it.
    // ---------------------------------------------------------------
    private static void SwitchPreservesAnnotationAndDirty(
        AppDriver driver, RunReport report, string suite, string fileA, string fileB)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        string nameA = System.IO.Path.GetFileName(fileA);
        string nameB = System.IO.Path.GetFileName(fileB);

        report.Results.Add(Result(suite, "open-two:tab-a-active",
            driver.ReadText("FileNameLabel") == nameA,
            $"expected the active tab to be '{nameA}', got '{driver.ReadText("FileNameLabel")}'"));

        PlaceTextAnnotation(driver, "TABSCHECK-A");

        PressCtrl(driver, VirtualKeyShort.KEY_O);
        bool openedB = driver.DriveOpenDialog(fileB);
        System.Threading.Thread.Sleep(1200);
        report.Results.Add(Result(suite, "open-two:open-b-dialog", openedB,
            "the Open dialog for file B did not appear/confirm"));
        report.Results.Add(Result(suite, "open-two:tab-b-active",
            driver.ReadText("FileNameLabel") == nameB,
            $"expected the active tab to be '{nameB}' after opening it, got '{driver.ReadText("FileNameLabel")}'"));

        // Switch back to A. With exactly two tabs open, Ctrl+Tab's direction doesn't matter.
        PressCtrl(driver, VirtualKeyShort.TAB);
        System.Threading.Thread.Sleep(500);
        report.Results.Add(Result(suite, "switch:ctrl-tab-back-to-a",
            driver.ReadText("FileNameLabel") == nameA,
            $"Ctrl+Tab did not return to '{nameA}' (now showing '{driver.ReadText("FileNameLabel")}')"));

        // Proof the annotation (and the dirty flag it set) survived the switch: closing A now must
        // raise the unsaved-changes prompt rather than close silently.
        PressCtrl(driver, VirtualKeyShort.KEY_W);
        System.Threading.Thread.Sleep(600);
        bool promptShown = driver.HasOpenModal();
        report.Results.Add(Result(suite, "dirty-marker:prompt-on-close-after-switch", promptShown,
            "closing a tab whose annotation survived a switch did not raise the save prompt"));

        // "Don't save" (No): the tab closes without writing anything, landing back on B.
        bool answered = promptShown && driver.AnswerDialog("No");
        System.Threading.Thread.Sleep(600);
        report.Results.Add(Result(suite, "close-dirty:dont-save-answers-prompt", answered,
            "\"No\" (Don't save) was not found on the close prompt"));
        report.Results.Add(Result(suite, "close-dirty:lands-on-remaining-tab",
            driver.ReadText("FileNameLabel") == nameB,
            $"expected to land back on '{nameB}' after closing A, got '{driver.ReadText("FileNameLabel")}'"));
    }

    // ---------------------------------------------------------------
    // Scenario 2: close a dirty tab, choose Cancel - the tab (and the app) must stay exactly as
    // they were.
    // ---------------------------------------------------------------
    private static void CloseDirtyTab_Cancel(AppDriver driver, RunReport report, string suite, string fileA)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        string nameA = System.IO.Path.GetFileName(fileA);

        PlaceTextAnnotation(driver, "TABSCHECK-CANCEL");

        PressCtrl(driver, VirtualKeyShort.KEY_W);
        System.Threading.Thread.Sleep(600);
        bool promptShown = driver.HasOpenModal();
        bool answered = promptShown && driver.AnswerDialog("Cancel");
        System.Threading.Thread.Sleep(600);

        report.Results.Add(Result(suite, "close-dirty:cancel-keeps-tab-open",
            promptShown && answered && driver.IsAlive && driver.ReadText("FileNameLabel") == nameA,
            "Cancel on the close prompt did not leave the dirty tab open and untouched"));
    }

    // ---------------------------------------------------------------
    // Scenario 3: open several files at once. Real OLE drag-and-drop cannot be simulated reliably
    // through UI Automation against a WPF DragEventArgs target, so this drives the identical
    // OpenManyInTabs() entry point through the Open dialog's multiselect (OpenFileDialog.Multiselect
    // = true, wired in Task 8) instead of a physical drop onto DropZone/PagePreviewPanel -
    // DropZone_Drop calls the exact same method with the exact same semantics.
    // ---------------------------------------------------------------
    private static void MultiOpen_ThreeFiles(
        AppDriver driver, RunReport report, string suite, string fileA, string fileB, string fileC)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        string nameA = System.IO.Path.GetFileName(fileA);
        string nameB = System.IO.Path.GetFileName(fileB);
        string nameC = System.IO.Path.GetFileName(fileC);

        PressCtrl(driver, VirtualKeyShort.KEY_O);
        // A native multiselect Open dialog accepts several quoted names in the filename box - the
        // same text a user's own multi-select produces.
        string multi = $"\"{fileA}\" \"{fileB}\" \"{fileC}\"";
        bool opened = driver.DriveOpenDialog(multi);
        System.Threading.Thread.Sleep(1800);
        report.Results.Add(Result(suite, "multi-open:dialog-confirmed", opened,
            "the multiselect Open dialog did not confirm"));

        // fileA is already the open tab, so OpenInTab switches to it instead of duplicating it;
        // the batch lands on list[0] (fileA), which is already showing - no unwanted tab switch.
        report.Results.Add(Result(suite, "multi-open:stays-on-already-open-first",
            driver.ReadText("FileNameLabel") == nameA,
            $"expected to remain on '{nameA}' (already open), got '{driver.ReadText("FileNameLabel")}'"));

        // Cycle through every tab and confirm both new files actually opened alongside it.
        var seen = new HashSet<string?>();
        for (int i = 0; i < 3; i++)
        {
            PressCtrl(driver, VirtualKeyShort.TAB);
            System.Threading.Thread.Sleep(400);
            seen.Add(driver.ReadText("FileNameLabel"));
        }
        report.Results.Add(Result(suite, "multi-open:both-new-files-opened",
            seen.Contains(nameB) && seen.Contains(nameC),
            $"cycling tabs after a 3-file open did not reach both new files (saw: {string.Join(", ", seen)})"));
    }

    // ---------------------------------------------------------------
    // Scenario 3b: the "all tabs" chevron (TabMoreBtn) only enters the UIA tree once more tabs are
    // open than comfortably fit (TabAllTabsMenuThreshold = 6 in MainWindow.Tabs.cs) - a state the
    // flat singles scan never reaches, hence its own exclusion there and its own coverage here.
    // ---------------------------------------------------------------
    private static void AllTabsChevron_WithManyTabs(
        AppDriver driver, RunReport report, string suite, string fileA)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);

        for (int i = 0; i < 7; i++)
        {
            PressCtrl(driver, VirtualKeyShort.KEY_T);   // Ctrl+T: a new blank tab each time
            System.Threading.Thread.Sleep(250);
        }

        bool chevronPresent = driver.Find("TabMoreBtn") != null;
        report.Results.Add(Result(suite, "chevron:appears-with-many-tabs", chevronPresent,
            "TabMoreBtn did not appear after opening more than 6 tabs"));

        // A WPF ContextMenu is not a separate window to UI Automation: it shows up inside the main
        // window's tree (ControlType Menu, class ContextMenu), so HasOpenModal (which counts
        // windows) can never see it. Look for the menu itself, and require one entry per open tab.
        int tabCount = ChipLabels(driver).Count;
        bool clicked = chevronPresent && driver.Click("TabMoreBtn");
        FlaUI.Core.AutomationElements.AutomationElement[] items = [];
        bool menuOpen = clicked && WaitFor(() => (items = OpenContextMenuItems(driver)).Length > 0, 2500);
        report.Results.Add(Result(suite, "chevron:opens-all-tabs-menu",
            menuOpen && items.Length == tabCount,
            $"clicking TabMoreBtn did not open the all-tabs menu with one entry per tab (clicked: {clicked}, entries: {items.Length}, tabs: {tabCount})"));

        // Picking an entry switches to that tab (and closes the menu, so nothing is left open).
        string nameA = Nm(fileA);
        bool picked = false;
        try
        {
            var first = items.FirstOrDefault();
            if (first != null && first.Patterns.Invoke.IsSupported) { first.Patterns.Invoke.Pattern.Invoke(); picked = true; }
        }
        catch { }
        report.Results.Add(Result(suite, "chevron:menu-entry-switches-tab",
            picked && WaitForActive(driver, nameA, 3000),
            $"choosing '{nameA}' in the all-tabs menu did not switch to it (showing '{driver.ReadText("FileNameLabel")}')"));
    }

    /// <summary>The items of the context menu open in the main window right now (empty if none).</summary>
    private static FlaUI.Core.AutomationElements.AutomationElement[] OpenContextMenuItems(AppDriver driver)
    {
        try
        {
            var menu = driver.MainWindow.FindFirstDescendant(cf =>
                cf.ByControlType(FlaUI.Core.Definitions.ControlType.Menu).And(cf.ByClassName("ContextMenu")));
            return menu?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)) ?? [];
        }
        catch { return []; }
    }

    // ---------------------------------------------------------------
    // Scenario 4: Save As renames the tab that was saved.
    // ---------------------------------------------------------------
    private static void SaveAsRenamesTab(AppDriver driver, RunReport report, string suite, string fileA)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);

        string renamed = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fileA)!, "tabs-a-renamed.pdf");
        try { File.Delete(renamed); } catch { }

        PressCtrlShift(driver, VirtualKeyShort.KEY_S);
        bool saved = driver.DriveSaveDialog(renamed);
        System.Threading.Thread.Sleep(1500);

        report.Results.Add(Result(suite, "save-as:dialog-confirmed", saved,
            "the Save As dialog did not appear/confirm"));
        report.Results.Add(Result(suite, "save-as:file-written", File.Exists(renamed),
            $"'{renamed}' was not created by Save As"));
        string wantName = System.IO.Path.GetFileName(renamed);
        report.Results.Add(Result(suite, "save-as:tab-renamed",
            driver.ReadText("FileNameLabel") == wantName,
            $"expected the tab to show '{wantName}' after Save As, got '{driver.ReadText("FileNameLabel")}'"));
    }

    // ---------------------------------------------------------------
    // Scenario 5: a long operation (Compress) blocks switching until it finishes, and shows the
    // Str_Tab_Busy toast instead of silently doing nothing. Uses the 50-page corpus file so the
    // operation is still running when the very next action lands.
    // ---------------------------------------------------------------
    private static void LongOperationBlocksSwitching(
        AppDriver driver, RunReport report, string suite, string largeFilePath, string fileB)
    {
        driver.Relaunch(largeFilePath);
        System.Threading.Thread.Sleep(1200);
        string nameLarge = System.IO.Path.GetFileName(largeFilePath);
        string nameB = System.IO.Path.GetFileName(fileB);

        PressCtrl(driver, VirtualKeyShort.KEY_O);
        driver.DriveOpenDialog(fileB);
        System.Threading.Thread.Sleep(1200);

        // Get back onto the large document before starting the operation on it.
        PressCtrl(driver, VirtualKeyShort.TAB);
        System.Threading.Thread.Sleep(400);
        if (driver.ReadText("FileNameLabel") != nameLarge)
        {
            PressCtrl(driver, VirtualKeyShort.TAB);
            System.Threading.Thread.Sleep(400);
        }

        driver.EnsureSurface(Surface.ToolsMenu);
        driver.Click("ToolsCompressMenuItem");
        System.Threading.Thread.Sleep(500);
        // ToolsCompress_Click's own gate (_longOps.Begin()) and the Task.Run of the rasterizer both
        // start synchronously once this confirms - no sleep here on purpose, so the very next
        // action lands while the 50-page compress is still running.
        bool confirmed = driver.AnswerDialog("Compress");
        report.Results.Add(Result(suite, "busy:compress-confirmed", confirmed,
            "the Compress PDF dialog did not confirm"));

        PressCtrl(driver, VirtualKeyShort.TAB);
        System.Threading.Thread.Sleep(300);
        string? duringOp = driver.ReadText("FileNameLabel");
        string? toast = driver.ReadText("ToastText");
        report.Results.Add(Result(suite, "busy:switch-blocked-during-operation",
            duringOp == nameLarge,
            $"Ctrl+Tab switched away from '{nameLarge}' while Compress was still running (now '{duringOp}')"));
        report.Results.Add(Result(suite, "busy:toast-shown",
            toast != null && toast.Contains("Finish or cancel"),
            $"no busy toast appeared while a long operation was running (ToastText='{toast}')"));

        // Let compression finish (a 50-page document at 2200 DPI takes a few seconds), then
        // confirm switching resumes normally.
        System.Threading.Thread.Sleep(9000);
        PressCtrl(driver, VirtualKeyShort.TAB);
        System.Threading.Thread.Sleep(500);
        report.Results.Add(Result(suite, "busy:switch-resumes-after-operation",
            driver.ReadText("FileNameLabel") == nameB,
            $"switching a tab did not resume once the long operation finished (now '{driver.ReadText("FileNameLabel")}')"));
    }

    // ---------------------------------------------------------------
    // Scenario 6: closing the window with two dirty tabs asks about each one in turn (active tab
    // first); Cancel on either aborts the whole close, and answering every prompt lets it proceed.
    // Ends the process - callers should run this last.
    // ---------------------------------------------------------------
    private static void WindowClose_TwoDirtyTabs(
        AppDriver driver, RunReport report, string suite, string fileA, string fileB)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        PlaceTextAnnotation(driver, "TABSCHECK-WCA");

        PressCtrl(driver, VirtualKeyShort.KEY_O);
        driver.DriveOpenDialog(fileB);
        System.Threading.Thread.Sleep(1200);
        PlaceTextAnnotation(driver, "TABSCHECK-WCB");

        // First attempt: Cancel on the first (active-tab) prompt must abort the close entirely.
        driver.CloseMainWindow();
        System.Threading.Thread.Sleep(700);
        bool firstPromptShown = driver.HasOpenModal();
        bool cancelled = firstPromptShown && driver.AnswerDialog("Cancel");
        System.Threading.Thread.Sleep(600);
        report.Results.Add(Result(suite, "window-close:cancel-aborts-close",
            firstPromptShown && cancelled && driver.IsAlive,
            "Cancel on a dirty-tab prompt during window close did not keep the window open"));

        // Second attempt: answer every dirty tab's prompt with "No" (Don't save) - the window must
        // then actually close.
        driver.CloseMainWindow();
        System.Threading.Thread.Sleep(700);
        bool answeredFirst = driver.AnswerDialog("No");
        System.Threading.Thread.Sleep(700);
        bool secondPromptShown = driver.HasOpenModal();
        bool answeredSecond = secondPromptShown && driver.AnswerDialog("No");
        System.Threading.Thread.Sleep(1200);

        report.Results.Add(Result(suite, "window-close:prompts-active-tab-first",
            answeredFirst, "the active tab's dirty prompt did not appear/answer during window close"));
        report.Results.Add(Result(suite, "window-close:prompts-second-dirty-tab",
            secondPromptShown && answeredSecond,
            "the second dirty tab's prompt did not appear/answer during window close"));
        report.Results.Add(Result(suite, "window-close:closes-once-all-answered",
            !driver.IsAlive,
            "the window did not actually close after every dirty tab was answered"));
    }

    // ---------------------------------------------------------------
    // Scenario 7: Ctrl+1..Ctrl+8 jump to that tab, Ctrl+9 to the LAST tab (browser rule), and
    // Ctrl+PageDown / Ctrl+PageUp cycle forward / back, with four tabs open.
    // ---------------------------------------------------------------
    private static void TabNumberAndPageKeys(AppDriver driver, RunReport report, string suite,
        string fileA, string fileB, string fileC, string fileD)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        OpenViaDialog(driver, fileB, fileC, fileD);
        string a = Nm(fileA), b = Nm(fileB), c = Nm(fileC), d = Nm(fileD);

        var chips = WaitForChips(driver, 4);
        report.Results.Add(Result(suite, "tab-keys:setup-four-tabs",
            SameList(chips, a, b, c, d), $"expected tabs [{a}, {b}, {c}, {d}], got [{string.Join(", ", chips)}]"));

        ExpectActiveAfter(driver, report, suite, "tab-keys:ctrl-1-first-tab", () => PressCtrl(driver, VirtualKeyShort.KEY_1), a);
        ExpectActiveAfter(driver, report, suite, "tab-keys:ctrl-2-second-tab", () => PressCtrl(driver, VirtualKeyShort.KEY_2), b);
        ExpectActiveAfter(driver, report, suite, "tab-keys:ctrl-3-third-tab", () => PressCtrl(driver, VirtualKeyShort.KEY_3), c);
        ExpectActiveAfter(driver, report, suite, "tab-keys:ctrl-9-last-tab", () => PressCtrl(driver, VirtualKeyShort.KEY_9), d);

        PressCtrl(driver, VirtualKeyShort.KEY_1);
        WaitForActive(driver, a);
        ExpectActiveAfter(driver, report, suite, "tab-keys:ctrl-pagedown-next", () => PressCtrl(driver, VirtualKeyShort.NEXT), b);
        ExpectActiveAfter(driver, report, suite, "tab-keys:ctrl-pagedown-again", () => PressCtrl(driver, VirtualKeyShort.NEXT), c);
        ExpectActiveAfter(driver, report, suite, "tab-keys:ctrl-pageup-previous", () => PressCtrl(driver, VirtualKeyShort.PRIOR), b);
    }

    // ---------------------------------------------------------------
    // Scenario 8: the zoom presets moved off Ctrl+digit. Ctrl+Shift+2 = fit width, Ctrl+Shift+3 =
    // fit page, Ctrl+0 = 100%, read back from the status-bar ZoomBox (SyncZoomBox shows the fit
    // entry's name while a fit mode is on, else the percentage). Ctrl+1 used to be "actual size";
    // it now belongs to the tabs and must leave the zoom alone.
    // ---------------------------------------------------------------
    private static void ZoomPresetsMoved(AppDriver driver, RunReport report, string suite, string fileA)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1500);   // the open's own Background fit-to-width lands first

        PressCtrlShift(driver, VirtualKeyShort.KEY_3);
        string? z = WaitForValue(driver, "ZoomBox", "Fit Page");
        report.Results.Add(Result(suite, "zoom-keys:ctrl-shift-3-fit-page", z == "Fit Page",
            $"Ctrl+Shift+3 should fit the page (ZoomBox 'Fit Page'), ZoomBox reads '{z}'"));

        PressCtrlShift(driver, VirtualKeyShort.KEY_2);
        z = WaitForValue(driver, "ZoomBox", "Fit Width");
        report.Results.Add(Result(suite, "zoom-keys:ctrl-shift-2-fit-width", z == "Fit Width",
            $"Ctrl+Shift+2 should fit the width (ZoomBox 'Fit Width'), ZoomBox reads '{z}'"));

        // Ctrl+1 with a single tab open: it switches to tab 1 (already shown) - the old "actual
        // size" preset would have turned the fit off and shown 100%.
        PressCtrl(driver, VirtualKeyShort.KEY_1);
        System.Threading.Thread.Sleep(900);
        z = driver.ReadValue("ZoomBox");
        report.Results.Add(Result(suite, "zoom-keys:ctrl-1-leaves-zoom", z == "Fit Width",
            $"Ctrl+1 changed the zoom (ZoomBox now '{z}', expected it to stay 'Fit Width')"));

        PressCtrl(driver, VirtualKeyShort.KEY_0);
        z = WaitForValue(driver, "ZoomBox", "100%");
        report.Results.Add(Result(suite, "zoom-keys:ctrl-0-actual-size", z == "100%",
            $"Ctrl+0 should zoom to 100%, ZoomBox reads '{z}'"));
    }

    // ---------------------------------------------------------------
    // Scenario 9: per-tab view state. In Continuous view, zoom tab A by hand (Ctrl+numpad-plus)
    // and move to page 3, open B (which fits itself), come back: A must show its own zoom and page.
    // ---------------------------------------------------------------
    private static void PerTabViewStateKept(AppDriver driver, RunReport report, string suite,
        string multiPage, string fileB)
    {
        driver.Relaunch(multiPage);
        System.Threading.Thread.Sleep(1500);
        string a = Nm(multiPage), b = Nm(fileB);

        driver.EnsureSurface(Surface.ViewMode);
        driver.Click("ViewContinuousBtn");
        System.Threading.Thread.Sleep(1500);
        bool continuous = driver.IsSelected("ViewContinuousBtn");
        report.Results.Add(Result(suite, "view-state:continuous-on", continuous,
            "ViewContinuousBtn did not switch the view to Continuous"));

        PressCtrl(driver, VirtualKeyShort.ADD);
        System.Threading.Thread.Sleep(400);
        PressCtrl(driver, VirtualKeyShort.ADD);
        System.Threading.Thread.Sleep(900);
        string? zoomA = driver.ReadValue("ZoomBox");
        bool manual = zoomA != null && zoomA.EndsWith("%");
        report.Results.Add(Result(suite, "view-state:manual-zoom-set", manual,
            $"Ctrl+numpad-plus did not leave a manual percentage zoom on tab A (ZoomBox '{zoomA}')"));

        PressPlain(driver, VirtualKeyShort.DOWN);
        System.Threading.Thread.Sleep(400);
        PressPlain(driver, VirtualKeyShort.DOWN);
        System.Threading.Thread.Sleep(1000);
        string? pageA = driver.ReadValue("PageJumpBox");
        report.Results.Add(Result(suite, "view-state:moved-to-page-3", pageA == "3",
            $"two Down presses should land on page 3, PageJumpBox reads '{pageA}'"));

        PressCtrl(driver, VirtualKeyShort.KEY_O);
        driver.DriveOpenDialog(fileB);
        bool onB = WaitForActive(driver, b, 4000);
        System.Threading.Thread.Sleep(1200);
        string? zoomB = driver.ReadValue("ZoomBox");
        report.Results.Add(Result(suite, "view-state:second-tab-opened", onB,
            $"opening '{b}' did not make it the active tab (showing '{driver.ReadText("FileNameLabel")}')"));

        PressCtrl(driver, VirtualKeyShort.KEY_1);
        bool backOnA = WaitForActive(driver, a, 4000);
        // The restore runs at Loaded -> Background -> ContextIdle; give it all a moment.
        System.Threading.Thread.Sleep(2000);
        string? zoomBack = driver.ReadValue("ZoomBox");
        string? pageBack = driver.ReadValue("PageJumpBox");
        report.Results.Add(Result(suite, "view-state:back-on-first-tab", backOnA,
            $"Ctrl+1 did not return to '{a}' (showing '{driver.ReadText("FileNameLabel")}')"));
        report.Results.Add(Result(suite, "view-state:zoom-kept-per-tab", manual && zoomBack == zoomA,
            $"tab A's manual zoom was not kept across a switch: before '{zoomA}', tab B '{zoomB}', after '{zoomBack}'"));
        report.Results.Add(Result(suite, "view-state:page-kept-per-tab", pageBack == "3",
            $"tab A's page was not kept across a switch: before '{pageA}', after '{pageBack}'"));
    }

    // ---------------------------------------------------------------
    // Scenario 10: opening a file that is already open switches to its tab (with the
    // Str_Tab_AlreadyOpen toast) instead of opening it twice.
    // ---------------------------------------------------------------
    private static void AlreadyOpenSwitches(AppDriver driver, RunReport report, string suite,
        string fileA, string fileB)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        string a = Nm(fileA), b = Nm(fileB);
        OpenViaDialog(driver, fileB);
        WaitForActive(driver, b, 4000);
        var before = WaitForChips(driver, 2);

        PressCtrl(driver, VirtualKeyShort.KEY_O);
        bool dlg = driver.DriveOpenDialog(fileA);
        bool onA = WaitForActive(driver, a, 4000);
        string? toast = driver.ReadText("ToastText");
        System.Threading.Thread.Sleep(500);
        var after = ChipLabels(driver);

        report.Results.Add(Result(suite, "already-open:switches-to-its-tab", dlg && onA,
            $"re-opening '{a}' did not switch to its tab (showing '{driver.ReadText("FileNameLabel")}')"));
        report.Results.Add(Result(suite, "already-open:tab-count-unchanged",
            before.Count == 2 && SameList(after, a, b),
            $"tabs before [{string.Join(", ", before)}], after [{string.Join(", ", after)}] - expected [{a}, {b}] both times"));
        report.Results.Add(Result(suite, "already-open:toast-shown",
            toast != null && toast.Contains("already open"),
            $"no 'already open' toast appeared (ToastText='{toast}')"));
    }

    // ---------------------------------------------------------------
    // Scenario 11: the tab right-click menu (a physical right-click on the chip - the menu is
    // only raised by MouseRightButtonUp; its items carry TabMenu* automation ids). Copy path puts
    // the file's full path on the clipboard; Close tabs to the right and Close other tabs close
    // exactly the right tabs, relative to the chip that was right-clicked, not the active one.
    // ---------------------------------------------------------------
    private static void TabContextMenu(AppDriver driver, RunReport report, string suite,
        string fileA, string fileB, string fileC, string fileD)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        string a = Nm(fileA), b = Nm(fileB), c = Nm(fileC), d = Nm(fileD);
        OpenViaDialog(driver, fileB, fileC, fileD);
        var chips = WaitForChips(driver, 4);
        report.Results.Add(Result(suite, "tab-menu:setup-four-tabs", SameList(chips, a, b, c, d),
            $"expected tabs [{a}, {b}, {c}, {d}], got [{string.Join(", ", chips)}]"));

        // Copy path (on a background tab's chip). The user's clipboard text is put back after.
        string? originalClipboard = ReadClipboardText();
        SetClipboardText("scalpel-e2e-sentinel");
        bool copyInvoked = RightClickChip(driver, c) && InvokeMenuItem(driver, "TabMenuCopyPath");
        System.Threading.Thread.Sleep(500);
        string? copied = ReadClipboardText();
        report.Results.Add(Result(suite, "tab-menu:copy-path", copyInvoked && PathSame(copied, fileC),
            $"Copy path on '{c}' left '{copied}' on the clipboard, expected '{fileC}' (menu invoked: {copyInvoked})"));
        if (originalClipboard != null) SetClipboardText(originalClipboard);

        // Close tabs to the right of B while A is the active tab: C and D go, A stays active.
        PressCtrl(driver, VirtualKeyShort.KEY_1);
        WaitForActive(driver, a);
        bool rightInvoked = RightClickChip(driver, b) && InvokeMenuItem(driver, "TabMenuCloseRight");
        System.Threading.Thread.Sleep(1200);
        chips = ChipLabels(driver);
        report.Results.Add(Result(suite, "tab-menu:close-tabs-to-right",
            rightInvoked && SameList(chips, a, b) && driver.ReadText("FileNameLabel") == a,
            $"Close tabs to the right of '{b}' left [{string.Join(", ", chips)}] with '{driver.ReadText("FileNameLabel")}' active - expected [{a}, {b}] with '{a}' active (menu invoked: {rightInvoked})"));

        // Close other tabs on B while C (reopened) is the active tab: only B is left, and shown.
        OpenViaDialog(driver, fileC);
        WaitForActive(driver, c, 4000);
        bool othersInvoked = RightClickChip(driver, b) && InvokeMenuItem(driver, "TabMenuCloseOthers");
        System.Threading.Thread.Sleep(1500);
        chips = ChipLabels(driver);
        report.Results.Add(Result(suite, "tab-menu:close-other-tabs",
            othersInvoked && SameList(chips, b) && driver.ReadText("FileNameLabel") == b,
            $"Close other tabs on '{b}' left [{string.Join(", ", chips)}] with '{driver.ReadText("FileNameLabel")}' active - expected only '{b}' (menu invoked: {othersInvoked})"));
        driver.DismissModals();
    }

    // ---------------------------------------------------------------
    // Scenario 12: per-tab temp cleanup (R14). A page rotation reloads the tab from a new working
    // file scalpel_p<pid>_temp_*.pdf, owned by that tab. Closing the tab (Don't save) deletes its
    // own working files at once, while another open tab's files stay on disk. Temp names carry
    // only the PID, so each tab's files are told apart by what appeared during its own rotation.
    // ---------------------------------------------------------------
    private static void TempFilesFreedOnTabClose(AppDriver driver, RunReport report, string suite,
        string fileA, string fileB)
    {
        driver.Relaunch(fileA);
        System.Threading.Thread.Sleep(1000);
        string a = Nm(fileA), b = Nm(fileB);
        OpenViaDialog(driver, fileB);
        WaitForActive(driver, b, 4000);
        int pid = driver.ProcessId;

        var s0 = TempFilesOf(pid);
        bool rotB = InvokeRotate(driver);
        System.Threading.Thread.Sleep(1500);
        var filesB = TempFilesOf(pid).Except(s0, StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();

        PressCtrl(driver, VirtualKeyShort.KEY_1);
        WaitForActive(driver, a);
        System.Threading.Thread.Sleep(500);
        var s2 = TempFilesOf(pid);
        bool rotA = InvokeRotate(driver);
        System.Threading.Thread.Sleep(1500);
        var filesA = TempFilesOf(pid).Except(s2, StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();

        report.Results.Add(Result(suite, "temp-cleanup:rotation-made-working-files",
            rotA && rotB && filesA.Count > 0 && filesB.Count > 0,
            $"rotating each tab should create its own scalpel_p{pid}_*.pdf working file(s): rotate invoked A={rotA} B={rotB}, new files A={filesA.Count} B={filesB.Count}"));

        PressCtrl(driver, VirtualKeyShort.KEY_W);
        System.Threading.Thread.Sleep(800);
        if (driver.HasOpenModal()) driver.AnswerDialog("No");   // rotated = unsaved: Don't save
        System.Threading.Thread.Sleep(1200);

        var leftA = filesA.Where(File.Exists).ToList();
        var lostB = filesB.Where(f => !File.Exists(f)).ToList();
        report.Results.Add(Result(suite, "temp-cleanup:closed-tab-files-deleted",
            filesA.Count > 0 && leftA.Count == 0,
            $"closing '{a}' left its working file(s) behind: {string.Join(", ", leftA.Select(System.IO.Path.GetFileName))}"));
        report.Results.Add(Result(suite, "temp-cleanup:other-tab-files-kept",
            filesB.Count > 0 && lostB.Count == 0 && driver.ReadText("FileNameLabel") == b,
            $"closing '{a}' deleted the still-open tab '{b}' working file(s) ({string.Join(", ", lostB.Select(System.IO.Path.GetFileName))}) or did not land on it (showing '{driver.ReadText("FileNameLabel")}')"));
    }

    // ---------------------------------------------------------------
    // Scenario 13: restore at startup (Task 12) plus the final-review Critical bug. Open three
    // durable files, make the 2nd active, close the app gracefully (no dirty tabs, so OnClosing
    // persists OpenTabs), relaunch with NO file argument: three chips come back, the 2nd active
    // and loaded, the other two deferred. Ctrl+W on it must activate AND LOAD its right
    // neighbour (a deferred tab), not leave an empty tab showing.
    // ---------------------------------------------------------------
    private static void RestoreTabsAtStartup(AppDriver driver, RunReport report, string suite,
        string restoreA, string restoreB, string restoreC)
    {
        string a = Nm(restoreA), b = Nm(restoreB), c = Nm(restoreC);
        driver.Relaunch(restoreA);
        System.Threading.Thread.Sleep(1000);
        OpenViaDialog(driver, restoreB, restoreC);
        WaitForChips(driver, 3);
        PressCtrl(driver, VirtualKeyShort.KEY_2);
        bool setupOk = WaitForActive(driver, b, 3000) && SameList(ChipLabels(driver), a, b, c);
        report.Results.Add(Result(suite, "restore:setup-three-tabs-second-active", setupOk,
            $"setup: expected [{a}, {b}, {c}] with '{b}' active, got [{string.Join(", ", ChipLabels(driver))}] with '{driver.ReadText("FileNameLabel")}'"));

        // Graceful close (WM_CLOSE, nothing dirty) persists OpenTabs; no file argument restores it.
        driver.Relaunch(null);
        var chips = WaitForChips(driver, 3, 8000);
        bool activeB = WaitForActive(driver, b, 6000);
        report.Results.Add(Result(suite, "restore:three-chips-back", SameList(chips, a, b, c),
            $"after a no-argument relaunch expected tabs [{a}, {b}, {c}], got [{string.Join(", ", chips)}]"));
        report.Results.Add(Result(suite, "restore:second-tab-active-and-loaded",
            activeB && HasPages(driver),
            $"expected '{b}' active with its pages shown, got '{driver.ReadText("FileNameLabel")}' (PageTotalLabel '{driver.ReadText("PageTotalLabel")}')"));

        // The Critical: closing the active restored tab must load the neighbour that takes over.
        PressCtrl(driver, VirtualKeyShort.KEY_W);
        bool onC = WaitForActive(driver, c, 6000);
        System.Threading.Thread.Sleep(800);
        chips = ChipLabels(driver);
        report.Results.Add(Result(suite, "restore:ctrl-w-loads-deferred-neighbour",
            onC && HasPages(driver) && SameList(chips, a, c),
            $"after Ctrl+W on the restored active tab expected '{c}' active AND loaded with tabs [{a}, {c}]; got '{driver.ReadText("FileNameLabel")}' (PageTotalLabel '{driver.ReadText("PageTotalLabel")}', tabs [{string.Join(", ", chips)}])"));

        // The other deferred tab loads on first activation too.
        PressCtrl(driver, VirtualKeyShort.KEY_1);
        bool onA = WaitForActive(driver, a, 6000);
        System.Threading.Thread.Sleep(600);
        report.Results.Add(Result(suite, "restore:deferred-tab-loads-on-activation",
            onA && HasPages(driver),
            $"Ctrl+1 onto the deferred '{a}' did not load it (showing '{driver.ReadText("FileNameLabel")}', PageTotalLabel '{driver.ReadText("PageTotalLabel")}')"));
    }

    // ---------------------------------------------------------------
    // Scenario 14: a second Scalpel launch forwards its files to the running window. Every other
    // scenario runs the app with SCALPEL_MULTI_INSTANCE=1, which switches single-instance off
    // entirely (no mutex, no pipe server) - so this one relaunches the primary WITHOUT it, then
    // starts a second process (also without it) with two file paths. The second process must
    // exit and both files must open as tabs in the first window, landing on the first of them.
    // Refused (reported as a failure, not faked) when some other Scalpel - e.g. the user's own -
    // already owns the single-instance mutex: the second launch would forward THERE.
    // ---------------------------------------------------------------
    private static void ForwardedMultiOpen(AppDriver driver, RunReport report, string suite,
        string fileA, string fileB, string fileC)
    {
        string a = Nm(fileA), b = Nm(fileB), c = Nm(fileC);
        if (SingleInstanceMutexExists())
        {
            report.Results.Add(Result(suite, "forward:no-foreign-primary", false,
                "another Scalpel process already owns the single-instance mutex (is Scalpel open outside the harness?) - " +
                "a second launch would forward to it, so forwarding cannot be checked. Close it and re-run."));
            return;
        }

        driver.Relaunch(fileA, multiInstance: false);
        System.Threading.Thread.Sleep(1500);
        bool serving = SingleInstanceMutexExists();
        report.Results.Add(Result(suite, "forward:primary-owns-single-instance", serving,
            "the app launched without SCALPEL_MULTI_INSTANCE did not create the single-instance mutex"));

        bool exited = false;
        int exitCode = -1;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(driver.ExePath, $"\"{fileB}\" \"{fileC}\"")
                { UseShellExecute = false };
            psi.Environment.Remove("SCALPEL_MULTI_INSTANCE");
            using var second = System.Diagnostics.Process.Start(psi);
            if (second != null)
            {
                exited = second.WaitForExit(15000);
                if (exited) exitCode = second.ExitCode;
                else { try { second.Kill(); second.WaitForExit(2000); } catch { } }
            }
        }
        catch { }
        report.Results.Add(Result(suite, "forward:second-process-exits", exited && exitCode == 0,
            $"the second Scalpel process did not hand off and exit (exited={exited}, code={exitCode}) - it ran its own window instead"));

        var chips = WaitForChips(driver, 3, 8000);
        bool onB = WaitForActive(driver, b, 4000);
        report.Results.Add(Result(suite, "forward:both-files-open-as-tabs", SameList(chips, a, b, c),
            $"expected the forwarded files to join the first window as tabs [{a}, {b}, {c}], got [{string.Join(", ", chips)}]"));
        report.Results.Add(Result(suite, "forward:lands-on-first-forwarded-file", onB,
            $"expected the first forwarded file '{b}' to be active, showing '{driver.ReadText("FileNameLabel")}'"));
    }

    // ---------------------------------------------------------------
    // Scenario 15: an open requested during a long operation is queued, not lost (R13). Compress
    // a 150-page document, Ctrl+O another file while it runs: nothing opens yet (busy toast, the
    // compressing tab stays), and once the operation finishes the queued file opens by itself.
    // ---------------------------------------------------------------
    private static void OpenDuringLongOperationIsQueued(AppDriver driver, RunReport report, string suite,
        string hugePath, string fileB)
    {
        driver.Relaunch(hugePath);
        System.Threading.Thread.Sleep(2000);
        string huge = Nm(hugePath), b = Nm(fileB);

        driver.EnsureSurface(Surface.ToolsMenu);
        driver.Click("ToolsCompressMenuItem");
        System.Threading.Thread.Sleep(500);
        bool confirmed = driver.AnswerDialog("Compress");
        report.Results.Add(Result(suite, "queued-open:compress-confirmed", confirmed,
            "the Compress PDF dialog did not confirm"));

        PressCtrl(driver, VirtualKeyShort.KEY_O);
        bool dlg = driver.DriveOpenDialog(fileB);
        System.Threading.Thread.Sleep(400);
        string? during = driver.ReadText("FileNameLabel");
        string? toast = driver.ReadText("ToastText");
        report.Results.Add(Result(suite, "queued-open:not-opened-while-busy",
            dlg && during == huge && toast != null && toast.Contains("Finish or cancel"),
            $"an open requested mid-operation should wait (busy toast, '{huge}' still shown); got '{during}', ToastText='{toast}' " +
            "(if the compress already finished, the window was too short to test)"));

        // 150 pages at the compressor's 2200 px raster take a while; poll rather than guess.
        bool opened = WaitForActive(driver, b, 120000, 500);
        System.Threading.Thread.Sleep(800);
        var chips = ChipLabels(driver);
        report.Results.Add(Result(suite, "queued-open:opens-after-operation",
            opened && SameList(chips, huge, b),
            $"the queued open of '{b}' never happened after the operation ended (showing '{driver.ReadText("FileNameLabel")}', tabs [{string.Join(", ", chips)}])"));
        driver.DismissModals();
    }

    // ---------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------

    // Edit-mode → Text tool → click canvas → type marker → commit (re-click the tool). Mirrors
    // SaveVerifySuite.PlaceTextAnnotation.
    private static void PlaceTextAnnotation(AppDriver driver, string text)
    {
        driver.EnsureSurface(Surface.EditMode);
        System.Threading.Thread.Sleep(200);
        driver.Click("ToolTextBtn");
        System.Threading.Thread.Sleep(200);
        driver.WithForeground(() =>
        {
            driver.PlaceAndSetAnnotationText(text);
            System.Threading.Thread.Sleep(200);
            driver.Click("ToolTextBtn"); // re-click commits the active TextBox
            System.Threading.Thread.Sleep(300);
        });
        System.Threading.Thread.Sleep(400);
    }

    private static void PressCtrl(AppDriver driver, VirtualKeyShort key) =>
        driver.WithForeground(() =>
        {
            System.Threading.Thread.Sleep(150);
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL)) Keyboard.Press(key);
        });

    private static void PressCtrlShift(AppDriver driver, VirtualKeyShort key) =>
        driver.WithForeground(() =>
        {
            System.Threading.Thread.Sleep(150);
            using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
                Keyboard.Press(key);
        });

    private static string Nm(string path) => System.IO.Path.GetFileName(path);

    private static bool SameList(IReadOnlyList<string> got, params string[] want) =>
        got.Count == want.Length && got.Zip(want, (g, w) => g == w).All(x => x);

    private static bool PathSame(string? a, string b)
    {
        if (string.IsNullOrEmpty(a)) return false;
        try { return string.Equals(System.IO.Path.GetFullPath(a!.Trim()), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs, int stepMs = 200)
    {
        for (int waited = 0; ; waited += stepMs)
        {
            try { if (condition()) return true; } catch { }
            if (waited >= timeoutMs) return false;
            System.Threading.Thread.Sleep(stepMs);
        }
    }

    /// <summary>Polls until the active document (FileNameLabel) is <paramref name="name"/>.</summary>
    private static bool WaitForActive(AppDriver driver, string name, int timeoutMs = 2000, int stepMs = 200) =>
        WaitFor(() => driver.ReadText("FileNameLabel") == name, timeoutMs, stepMs);

    /// <summary>Polls a control's UIA Value until it equals <paramref name="want"/>; returns the last read.</summary>
    private static string? WaitForValue(AppDriver driver, string automationId, string want, int timeoutMs = 2500)
    {
        string? last = null;
        WaitFor(() => (last = driver.ReadValue(automationId)) == want, timeoutMs);
        return last;
    }

    /// <summary>Runs <paramref name="act"/> and records whether <paramref name="want"/> became active.</summary>
    private static void ExpectActiveAfter(AppDriver driver, RunReport report, string suite, string action,
        Action act, string want)
    {
        act();
        bool ok = WaitForActive(driver, want);
        report.Results.Add(Result(suite, action, ok,
            $"expected '{want}' to be the active tab, showing '{driver.ReadText("FileNameLabel")}'"));
    }

    /// <summary>
    /// The tab chips' file names in strip order. Each chip's label TextBlock carries the
    /// automation id <c>TabChipLabel</c> (the chip Border itself has no automation peer).
    /// </summary>
    private static List<string> ChipLabels(AppDriver driver)
    {
        try
        {
            return driver.MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabChipLabel"))
                .Select(e => { try { return e.Name ?? ""; } catch { return ""; } })
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>Polls until exactly <paramref name="count"/> chips are shown; returns the last read.</summary>
    private static List<string> WaitForChips(AppDriver driver, int count, int timeoutMs = 4000)
    {
        List<string> last = [];
        WaitFor(() => (last = ChipLabels(driver)).Count == count, timeoutMs);
        return last;
    }

    /// <summary>True when the shown document has pages: the jump box's "/ N" total is N &gt;= 1.</summary>
    private static bool HasPages(AppDriver driver)
    {
        string? total = driver.ReadText("PageTotalLabel");
        if (string.IsNullOrEmpty(total)) return false;
        string digits = new(total!.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) && n >= 1;
    }

    /// <summary>Ctrl+O and confirm the Open dialog with one file, or several (multiselect).</summary>
    private static bool OpenViaDialog(AppDriver driver, params string[] files)
    {
        PressCtrl(driver, VirtualKeyShort.KEY_O);
        string arg = files.Length == 1 ? files[0] : string.Join(" ", files.Select(f => $"\"{f}\""));
        bool ok = driver.DriveOpenDialog(arg);
        System.Threading.Thread.Sleep(1200 + 400 * files.Length);
        return ok;
    }

    /// <summary>
    /// Physically right-clicks the chip labelled <paramref name="name"/> (the tab menu is raised by
    /// the chip's MouseRightButtonUp, which no UIA pattern can fire), then waits for the menu.
    /// </summary>
    private static bool RightClickChip(AppDriver driver, string name)
    {
        try
        {
            var label = driver.MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabChipLabel"))
                .FirstOrDefault(e => { try { return e.Name == name; } catch { return false; } });
            if (label == null) return false;
            var r = label.BoundingRectangle;
            if (r.Width <= 0 || r.Height <= 0) return false;
            var pt = new System.Drawing.Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
            driver.WithForeground(() =>
            {
                Mouse.MoveTo(pt);
                System.Threading.Thread.Sleep(120);
                Mouse.Click(MouseButton.Right);
            });
            return WaitFor(() => driver.Find("TabMenuClose") != null, 2500);
        }
        catch { return false; }
    }

    /// <summary>Invokes an item of the open tab menu (a WPF ContextMenu popup) by automation id.</summary>
    private static bool InvokeMenuItem(AppDriver driver, string automationId)
    {
        if (!WaitFor(() => driver.Find(automationId) != null, 2000)) return false;
        return driver.Click(automationId);
    }

    /// <summary>
    /// Invokes the Pages ribbon's Rotate button (no x:Name - found by its tooltip, which WPF
    /// surfaces as the UIA HelpText) on the active tab's selected page.
    /// </summary>
    private static bool InvokeRotate(AppDriver driver)
    {
        driver.EnsureSurface(Surface.PagesMode);
        System.Threading.Thread.Sleep(300);
        try
        {
            var btn = driver.MainWindow.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
                .FirstOrDefault(e =>
                {
                    try
                    {
                        return (e.HelpText ?? "").StartsWith("Rotate Selected Pages Clockwise", StringComparison.Ordinal)
                            && !e.Properties.IsOffscreen.ValueOrDefault;
                    }
                    catch { return false; }
                });
            if (btn == null || !btn.Patterns.Invoke.IsSupported) return false;
            btn.Patterns.Invoke.Pattern.Invoke();
            return true;
        }
        catch { return false; }
    }

    /// <summary>This process's live working files: scalpel_p{pid}_*.pdf in %LOCALAPPDATA%\Scalpel\Temp.</summary>
    private static List<string> TempFilesOf(int pid)
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scalpel", "Temp");
            if (pid <= 0 || !Directory.Exists(dir)) return [];
            return Directory.GetFiles(dir, $"scalpel_p{pid}_*.pdf").ToList();
        }
        catch { return []; }
    }

    /// <summary>
    /// Whether some Scalpel process owns the single-instance mutex (Services/SingleInstance.cs
    /// builds the same name from the user and session).
    /// </summary>
    private static bool SingleInstanceMutexExists()
    {
        try
        {
            string suffix = $"{Environment.UserDomainName}.{Environment.UserName}.s{System.Diagnostics.Process.GetCurrentProcess().SessionId}"
                .ToLowerInvariant();
            if (System.Threading.Mutex.TryOpenExisting($@"Local\ScalpelPDF.SingleInstance.{suffix}", out var m))
            {
                m.Dispose();
                return true;
            }
        }
        catch (UnauthorizedAccessException) { return true; }   // exists, just not openable by us
        catch { }
        return false;
    }

    // The clipboard needs an STA thread; the harness's own threads are MTA.
    private static string? ReadClipboardText()
    {
        string? text = null;
        RunSta(() => { if (System.Windows.Forms.Clipboard.ContainsText()) text = System.Windows.Forms.Clipboard.GetText(); });
        return text;
    }

    private static void SetClipboardText(string text) =>
        RunSta(() => System.Windows.Forms.Clipboard.SetText(text));

    private static void RunSta(Action body)
    {
        var t = new System.Threading.Thread(() => { try { body(); } catch { } });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join(3000);
    }

    /// <summary>A plain N-page PDF (one "Page i of N" line per page), like Corpus.WriteLarge.</summary>
    private static void WriteNumberedPdf(string path, int pages)
    {
        using var doc = new PdfSharpCore.Pdf.PdfDocument();
        var font = new PdfSharpCore.Drawing.XFont("Arial", 20);
        for (int i = 1; i <= pages; i++)
        {
            var page = doc.AddPage();
            using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
            gfx.DrawString($"Page {i} of {pages}", font, PdfSharpCore.Drawing.XBrushes.Black,
                new PdfSharpCore.Drawing.XRect(0, 0, page.Width, page.Height), PdfSharpCore.Drawing.XStringFormats.Center);
        }
        doc.Save(path);
    }

    private static void PressPlain(AppDriver driver, VirtualKeyShort key) =>
        driver.WithForeground(() =>
        {
            System.Threading.Thread.Sleep(150);
            Keyboard.Press(key);
        });

    private static ActionResult Result(string suite, string action, bool ok, string? reason = null) =>
        new(suite, action, ok ? Outcome.Pass : Outcome.Fail, ok ? null : reason, Array.Empty<LogEntry>());
}
