using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Scalpel.E2E;

public static class SinglesSuite
{
    // Buttons in the live tree that are deliberately NOT auto-clicked:
    // window-chrome buttons (clicking Close kills the app) and InstallBtn
    // (would self-install the portable app). They are excluded from the
    // "untested control" coverage gap rather than reported as a miss.
    //
    // The second group opens content that is incompatible with the flat,
    // single-pass singles scan: ModeAboutTab/ModeWhatsNewTab raise *in-window*
    // overlays (DismissModals, which only closes separate top-level windows,
    // cannot dismiss them, so they would cover every control clicked after
    // them). Those two are instead exercised with explicit open→dismiss pairs
    // in JourneysSuite (Journey 5). ToolsMenuBtn used to be excluded here for
    // the same reason, but the catalog now drives the Tools menu directly:
    // Surface.ToolsMenu reopens it before each item and AppDriver looks into
    // popup windows, so the button and all its items are genuinely exercised. The three Settings section-header toggles collapse the very
    // theme/accent/language groups whose radios the harness needs visible, so
    // they stay excluded entirely — same rationale as LogEnabledCheck/
    // ClearLogsBtn sabotaging the harness's own observability.
    private static readonly HashSet<string> ExcludedFromCoverage =
        ["Close", "Minimize", "Maximize", "Restore", "SystemMenuBar", "InstallBtn",
         "LogEnabledCheck", "ClearLogsBtn", "OpenMenuBtn",
         "ModeAboutTab", "ModeWhatsNewTab",
         "ThemeHeaderToggle", "AccentHeaderToggle", "LangHeaderToggle",
         // The document-tab-strip "all tabs" chevron (MainWindow.Tabs.cs) only enters the UIA tree
         // once 7+ tabs are open - a state the flat, single-document singles scan never reaches.
         // TabsSuite opens enough tabs to exercise it directly instead.
         "TabMoreBtn"];

    /// <summary>
    /// True for a control that belongs to a native Windows dialog rather than to Scalpel.
    /// <para>A file dialog is an OWNED window, so while one is up its controls appear as
    /// descendants of the main window and the coverage cross-check would report each as an
    /// un-exercised Scalpel control. The Tools menu opens several such dialogs. Modals are
    /// dismissed before the cross-check; this is the belt-and-braces filter for one that is still
    /// closing, and it only ever matches ids no Scalpel control uses.</para>
    /// </summary>
    private static bool IsWindowsDialogChrome(string id)
    {
        // Common shell-dialog part names.
        if (id is "HelpButton" or "SplitMenuButton" or "DropDown" or "SearchBoxSearchButton"
               or "UpButton" or "DownButton" or "UpPageButton" or "DownPageButton"
               or "Minimize-Restore" or "Maximize-Restore" or "NavigationBar" or "TitleBar")
            return true;

        // GUID-named parts, e.g. {7DDC1264-7E4D-4F74-BBC0-D191987C8D0F}.
        if (id.Length > 2 && id[0] == '{' && id[id.Length - 1] == '}') return true;

        // Purely numeric ids ("1", "2") - shell control ordinals. No Scalpel control is named
        // this way; every one of ours is a descriptive x:Name.
        return id.Length > 0 && id.All(char.IsDigit);
    }

    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // Exercise every catalogued control once.
        foreach (var spec in Catalog.All)
            report.Results.Add(runner.RunControl("singles", spec));

        // The Tools items open modals (tool forms, confirms, OS file dialogs). Make sure none is
        // still up before the coverage cross-check: an open file dialog puts its own chrome
        // (UpButton, DropDown, SearchBoxSearchButton, GUID-named parts) into the automation tree,
        // and every one of those would be reported as an un-exercised Scalpel control.
        for (int i = 0; i < 5 && driver.HasOpenModal(); i++)
        {
            driver.DismissModals();
            System.Threading.Thread.Sleep(300);
        }
        driver.ResetToBaseState();

        // Coverage cross-check: any button in the live tree not in the catalog
        // (and not a deliberately-excluded chrome/install button).
        try
        {
            var buttons = driver.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            foreach (var b in buttons)
            {
                string id = b.Properties.AutomationId.ValueOrDefault ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                if (ExcludedFromCoverage.Contains(id)) continue;
                if (IsWindowsDialogChrome(id)) continue;
                if (!Catalog.KnownIds.Contains(id) && !report.UntestedControls.Contains(id))
                    report.UntestedControls.Add(id);
            }
        }
        catch { }
    }
}
