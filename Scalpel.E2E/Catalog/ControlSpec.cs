namespace Scalpel.E2E;

public enum Surface
{
    AlwaysVisible, ViewMode, EditMode, PagesMode, SignMode, SettingsOverlay, ToolsMenu
}

public sealed record ControlSpec(
    string AutomationId,
    Surface Surface,
    string? AssertionKey);
