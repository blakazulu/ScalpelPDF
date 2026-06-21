using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace Scalpel.E2E;

public static class SinglesSuite
{
    // Buttons in the live tree that are deliberately NOT auto-clicked:
    // window-chrome buttons (clicking Close kills the app) and InstallBtn
    // (would self-install the portable app). They are excluded from the
    // "untested control" coverage gap rather than reported as a miss.
    private static readonly HashSet<string> ExcludedFromCoverage =
        ["Close", "Minimize", "Maximize", "Restore", "SystemMenuBar", "InstallBtn",
         "LogEnabledCheck", "ClearLogsBtn"];

    public static void Run(AppDriver driver, ActionRunner runner, RunReport report)
    {
        // Exercise every catalogued control once.
        foreach (var spec in Catalog.All)
            report.Results.Add(runner.RunControl("singles", spec));

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
                if (!Catalog.KnownIds.Contains(id) && !report.UntestedControls.Contains(id))
                    report.UntestedControls.Add(id);
            }
        }
        catch { }
    }
}
