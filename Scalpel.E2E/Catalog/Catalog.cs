using System.Linq;

namespace Scalpel.E2E;

public static class Catalog
{
    public static IReadOnlyList<ControlSpec> All { get; } =
    [
        // Mode tabs (always visible)
        new("ModeViewTab",  Surface.AlwaysVisible, false, "modeViewActive"),
        new("ModeEditTab",  Surface.AlwaysVisible, false, "modeEditActive"),
        new("ModePagesTab", Surface.AlwaysVisible, false, "modePagesActive"),
        new("ModeSignTab",  Surface.AlwaysVisible, false, "modeSignActive"),

        // File group / zoom (always visible)
        new("OpenMenuBtn",   Surface.AlwaysVisible, false, null),
        new("SaveAsBtn",     Surface.AlwaysVisible, true,  null),
        new("SaveMenuBtn",   Surface.AlwaysVisible, true,  null),
        new("ZoomOutBtn",    Surface.AlwaysVisible, true,  "zoomDecreased"),
        new("ZoomInBtn",     Surface.AlwaysVisible, true,  "zoomIncreased"),
        new("SidebarToggleBtn", Surface.AlwaysVisible, true, null),
        // SettingsBtn is intentionally ordered LAST among the non-settings controls
        // (just before the settings group below) so the overlay it opens never
        // obscures the mode/view/edit/tool controls tested before it.

        // View mode panel
        new("ViewSingleBtn",     Surface.ViewMode, true, null),
        new("ViewContinuousBtn", Surface.ViewMode, true, null),
        new("ViewTwoPageBtn",    Surface.ViewMode, true, null),
        new("ViewGridBtn",       Surface.ViewMode, true, null),
        new("ViewFitBtn",        Surface.ViewMode, true, null),

        // Edit mode panel
        new("ToolSelectBtn",    Surface.EditMode, true, null),
        new("ToolTextBtn",      Surface.EditMode, true, null),
        new("ToolHighlightBtn", Surface.EditMode, true, null),
        new("ToolDrawBtn",      Surface.EditMode, true, null),
        new("ToolImageBtn",     Surface.EditMode, true, null),
        new("ToolCropBtn",      Surface.EditMode, true, null),

        // Sign mode panel
        new("ToolSignatureBtn", Surface.SignMode, true, null),

        // Settings overlay — SettingsBtn opens it and must be tested first so the
        // overlay is up for the controls that live inside it.
        new("SettingsBtn",        Surface.AlwaysVisible, false, "settingsOverlayOpen"),
        new("ThemeDarkRadio",     Surface.SettingsOverlay, false, null),
        new("ThemeLightRadio",    Surface.SettingsOverlay, false, null),
        new("ThemeHCRadio",       Surface.SettingsOverlay, false, null),
        new("ThemeBloodRadio",    Surface.SettingsOverlay, false, null),
        new("ThemeGreedRadio",    Surface.SettingsOverlay, false, null),
        new("ThemeCyanoticRadio", Surface.SettingsOverlay, false, null),
        new("LangEnRadio",        Surface.SettingsOverlay, false, null),
        new("LangEsRadio",        Surface.SettingsOverlay, false, null),
        new("LangZhTWRadio",      Surface.SettingsOverlay, false, null),
        new("LangZhCNRadio",      Surface.SettingsOverlay, false, null),
        new("LangBnRadio",        Surface.SettingsOverlay, false, null),
        new("LangTrRadio",        Surface.SettingsOverlay, false, null),
        new("OpenLogsBtn",        Surface.SettingsOverlay, false, null),
        // NOTE: LogEnabledCheck and ClearLogsBtn are deliberately NOT auto-exercised.
        // LogEnabledCheck toggles the very logging the harness reads to verify results
        // (clicking it blinds the harness mid-run); ClearLogsBtn deletes session logs.
        // Exercising them would sabotage the harness's own observability. They are
        // listed in SinglesSuite.ExcludedFromCoverage so they don't read as gaps.
    ];

    public static ControlSpec? Find(string automationId) =>
        All.FirstOrDefault(c => c.AutomationId == automationId);

    public static IReadOnlyList<string> KnownIds { get; } =
        [.. All.Select(c => c.AutomationId)];
}
