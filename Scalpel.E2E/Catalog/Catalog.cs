using System.Linq;

namespace Scalpel.E2E;

public static class Catalog
{
    public static IReadOnlyList<ControlSpec> All { get; } =
    [
        // Mode tabs (always visible)
        new("ModeViewTab",  Surface.AlwaysVisible, "modeViewActive"),
        new("ModeEditTab",  Surface.AlwaysVisible, "modeEditActive"),
        new("ModePagesTab", Surface.AlwaysVisible, "modePagesActive"),
        new("ModeSignTab",  Surface.AlwaysVisible, "modeSignActive"),

        // File group / zoom (always visible)
        new("OpenMenuBtn",   Surface.AlwaysVisible, null),
        new("SaveAsBtn",     Surface.AlwaysVisible, null),
        new("SaveMenuBtn",   Surface.AlwaysVisible, null),
        new("ZoomOutBtn",    Surface.AlwaysVisible, "zoomDecreased"),
        new("ZoomInBtn",     Surface.AlwaysVisible, "zoomIncreased"),
        new("SidebarToggleBtn", Surface.AlwaysVisible, null),
        // NOTE: SettingsBtn is not here — it is placed just before the settings group
        // below, so the overlay it opens never obscures the view/edit/tool controls.

        // View mode panel
        new("ViewSingleBtn",     Surface.ViewMode, null),
        new("ViewContinuousBtn", Surface.ViewMode, null),
        new("ViewTwoPageBtn",    Surface.ViewMode, null),
        new("ViewGridBtn",       Surface.ViewMode, null),
        new("ViewFitBtn",        Surface.ViewMode, null),

        // Edit mode panel
        new("ToolSelectBtn",    Surface.EditMode, null),
        new("ToolTextBtn",      Surface.EditMode, null),
        new("ToolHighlightBtn", Surface.EditMode, null),
        new("ToolDrawBtn",      Surface.EditMode, null),
        new("ToolImageBtn",     Surface.EditMode, null),
        new("ToolCropBtn",      Surface.EditMode, null),

        // Sign mode panel
        new("ToolSignatureBtn", Surface.SignMode, null),

        // Settings overlay — SettingsBtn opens it and must be tested first so the
        // overlay is up for the controls that live inside it.
        new("SettingsBtn",        Surface.AlwaysVisible, "settingsOverlayOpen"),
        new("ThemeDarkRadio",     Surface.SettingsOverlay, null),
        new("ThemeLightRadio",    Surface.SettingsOverlay, null),
        new("ThemeHCRadio",       Surface.SettingsOverlay, null),
        new("ThemeBloodRadio",    Surface.SettingsOverlay, null),
        new("ThemeGreedRadio",    Surface.SettingsOverlay, null),
        new("ThemeCyanoticRadio", Surface.SettingsOverlay, null),
        new("LangEnRadio",        Surface.SettingsOverlay, null),
        new("LangEsRadio",        Surface.SettingsOverlay, null),
        new("LangZhTWRadio",      Surface.SettingsOverlay, null),
        new("LangZhCNRadio",      Surface.SettingsOverlay, null),
        new("LangBnRadio",        Surface.SettingsOverlay, null),
        new("LangTrRadio",        Surface.SettingsOverlay, null),
        new("OpenLogsBtn",        Surface.SettingsOverlay, null),
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
