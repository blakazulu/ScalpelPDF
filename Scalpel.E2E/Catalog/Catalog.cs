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

        // File group / zoom (always visible). NOTE: the Open button (OpenMenuBtn) is NOT
        // auto-clicked — invoking it raises a modal OS file picker that would wedge the
        // single-pass scan; it is listed in SinglesSuite.ExcludedFromCoverage. The Save
        // button (SaveAsBtn → Save_Click → SaveInPlace) saves with no dialog and is safe.
        new("SaveAsBtn",     Surface.AlwaysVisible, null),
        new("ZoomOutBtn",    Surface.AlwaysVisible, "zoomDecreased"),
        new("ZoomInBtn",     Surface.AlwaysVisible, "zoomIncreased"),
        new("SidebarToggleBtn", Surface.AlwaysVisible, null),
        // Document tab strip (under the ribbon, always shown - MainWindow.Tabs.cs). The "+" button
        // is always present and safe to click (NewDocument() opens a blank tab). TabMoreBtn (the
        // "all tabs" chevron) only enters the UIA tree with 7+ tabs open, which the flat singles
        // scan never reaches - it is excluded in SinglesSuite.ExcludedFromCoverage and exercised
        // directly in TabsSuite instead. The chips themselves (BuildTabChip) are built in code with
        // no AutomationId, so they never appear in the coverage cross-check either.
        new("TabAddBtn", Surface.AlwaysVisible, null),
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
        new("ToolLineBtn",      Surface.EditMode, null),
        new("ToolCropBtn",      Surface.EditMode, null),

        // Sign mode panel
        new("ToolSignatureBtn", Surface.SignMode, null),

        // Tools menu (document-level operations). Each item lives in a ContextMenu, which WPF
        // renders in its own popup window - hence AppDriver.FindInPopups. Surface.ToolsMenu
        // reopens the menu before each item, because clicking one closes it.
        //
        // Every one of these opens a modal (a tool form, a confirm, or an OS file dialog) that
        // ActionRunner's DismissModals closes straight after, which cancels the operation - so the
        // scan exercises each handler's real entry path without altering the document. They are
        // listed BEFORE SettingsBtn so the settings overlay is not up while the menu is open.
        // The button that opens the menu, exercised in its own right.
        new("ToolsMenuBtn",                   Surface.AlwaysVisible, null),
        new("ToolsNumberingMenuItem",         Surface.ToolsMenu, null),
        new("ToolsWatermarkMenuItem",         Surface.ToolsMenu, null),
        new("ToolsTransformMenuItem",         Surface.ToolsMenu, null),
        new("ToolsCompressMenuItem",          Surface.ToolsMenu, null),
        new("ToolsDocumentInfoMenuItem",      Surface.ToolsMenu, null),
        new("ToolsPreflightMenuItem",         Surface.ToolsMenu, null),
        new("ToolsCommentsMenuItem",          Surface.ToolsMenu, null),
        new("ToolsExportImagesMenuItem",      Surface.ToolsMenu, null),
        new("ToolsCompareMenuItem",           Surface.ToolsMenu, null),
        new("ToolsMeasureMenuItem",           Surface.ToolsMenu, null),
        new("ToolsStraightenMenuItem",        Surface.ToolsMenu, null),
        new("ToolsProtectMenuItem",           Surface.ToolsMenu, null),
        new("ToolsSignMenuItem",              Surface.ToolsMenu, null),
        new("ToolsSanitizeMenuItem",          Surface.ToolsMenu, null),
        new("ToolsRedactMenuItem",            Surface.ToolsMenu, null),
        // OCR entries: each first calls EnsureOcrReady, which either reports the engine is
        // missing (a dialog the runner dismisses) or proceeds. Included because both paths are
        // safe and neither writes to the document without a further confirmation.
        new("ToolsOcrLanguageMenuItem",       Surface.ToolsMenu, null),
        new("ToolsOcrExtractTextMenuItem",    Surface.ToolsMenu, null),
        new("ToolsOcrPageToClipboardMenuItem", Surface.ToolsMenu, null),
        new("ToolsOcrRegionMenuItem",         Surface.ToolsMenu, null),
        new("ToolsOcrFormMenuItem",           Surface.ToolsMenu, null),
        new("ToolsOcrMenuItem",               Surface.ToolsMenu, null),

        // Settings overlay — SettingsBtn opens it and must be tested first so the
        // overlay is up for the controls that live inside it.
        new("SettingsBtn",        Surface.AlwaysVisible, "settingsOverlayOpen"),
        // Accent BEFORE Theme on purpose: selecting High Contrast (a theme) DISABLES the
        // accent picker (HC owns its own accent), so the accent radios must be exercised
        // while a non-HC theme is still active, or Select() hits a disabled control.
        new("AccentAmberRadio",   Surface.SettingsOverlay, null),
        new("AccentRedRadio",     Surface.SettingsOverlay, null),
        new("AccentGreenRadio",   Surface.SettingsOverlay, null),
        new("AccentCyanRadio",    Surface.SettingsOverlay, null),
        new("ThemeDarkRadio",     Surface.SettingsOverlay, null),
        new("ThemeLightRadio",    Surface.SettingsOverlay, null),
        new("ThemeHCRadio",       Surface.SettingsOverlay, null),
        new("LangEnRadio",        Surface.SettingsOverlay, null),
        new("LangEsRadio",        Surface.SettingsOverlay, null),
        new("LangZhTWRadio",      Surface.SettingsOverlay, null),
        new("LangZhCNRadio",      Surface.SettingsOverlay, null),
        new("LangBnRadio",        Surface.SettingsOverlay, null),
        new("LangTrRadio",        Surface.SettingsOverlay, null),
        new("LangHeRadio",        Surface.SettingsOverlay, null),
        new("LangArRadio",        Surface.SettingsOverlay, null),
        new("LangRuRadio",        Surface.SettingsOverlay, null),
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
