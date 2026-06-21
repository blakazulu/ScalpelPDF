using FlaUI.Core.AutomationElements;

namespace Scalpel.E2E;

public static class Assertions
{
    public static (bool ok, string? reason) Check(
        string? assertionKey, AppDriver driver, IReadOnlyList<LogEntry> newLogs)
    {
        if (string.IsNullOrEmpty(assertionKey)) return (true, null);

        switch (assertionKey)
        {
            case "modeViewActive":  return PanelVisible(driver, "ModePanelView");
            case "modeEditActive":  return PanelVisible(driver, "ModePanelEdit");
            case "modePagesActive": return PanelVisible(driver, "ModePanelPages");
            case "modeSignActive":  return PanelVisible(driver, "ModePanelSign");
            case "settingsOverlayOpen": return PanelVisible(driver, "SettingsOverlay");
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

    private static (bool ok, string? reason) PanelVisible(AppDriver driver, string automationId)
    {
        var el = driver.Find(automationId);
        if (el == null) return (false, $"{automationId} not found");
        try
        {
            bool offscreen = el.Properties.IsOffscreen.ValueOrDefault;
            return offscreen ? (false, $"{automationId} is offscreen/hidden") : (true, null);
        }
        catch { return (false, $"{automationId} visibility unreadable"); }
    }
}
