using FlaUI.Core.AutomationElements;

namespace Scalpel.E2E;

public static class Assertions
{
    public static (bool ok, string? reason) Check(
        string? assertionKey, AppDriver driver, IReadOnlyList<LogEntry> newLogs)
    {
        if (string.IsNullOrEmpty(assertionKey)) return (true, null);

        // Mode is verified by the mode-tab RadioButton's selected state. The
        // ModePanel* elements are StackPanels, which WPF does NOT surface in the
        // UIA tree, so Find()ing them always fails — the tab's SelectionItem
        // state is the reliable signal that the mode switched.
        switch (assertionKey)
        {
            case "modeViewActive":  return TabSelected(driver, "ModeViewTab");
            case "modeEditActive":  return TabSelected(driver, "ModeEditTab");
            case "modePagesActive": return TabSelected(driver, "ModePagesTab");
            case "modeSignActive":  return TabSelected(driver, "ModeSignTab");
            case "settingsOverlayOpen":
                // The overlay container isn't in the UIA tree either; assert a
                // control that only lives inside the open Settings overlay.
                return ControlOnscreen(driver, "ThemeDarkRadio",
                    "Settings overlay did not open (ThemeDarkRadio not visible)");
            case "zoomIncreased":
            case "zoomDecreased":
                // Value-delta comparison is performed by ActionRunner; here we only
                // confirm the zoom controls are still present and reachable.
                return driver.Find("ZoomBox") != null
                    ? (true, null)
                    : (false, "ZoomBox not found after zoom action");
            default:
                return (true, null);
        }
    }

    private static (bool ok, string? reason) TabSelected(AppDriver driver, string tabId) =>
        driver.IsSelected(tabId)
            ? (true, null)
            : (false, $"{tabId} is not selected after click");

    private static (bool ok, string? reason) ControlOnscreen(
        AppDriver driver, string automationId, string failReason)
    {
        var el = driver.Find(automationId);
        if (el == null) return (false, failReason);
        try
        {
            bool offscreen = el.Properties.IsOffscreen.ValueOrDefault;
            return offscreen ? (false, failReason) : (true, null);
        }
        catch { return (false, $"{automationId} visibility unreadable"); }
    }
}
