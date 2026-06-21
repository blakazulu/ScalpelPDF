namespace Scalpel.E2E;

public enum Surface
{
    AlwaysVisible, ViewMode, EditMode, PagesMode, SignMode, SettingsOverlay
}

public sealed record ControlSpec(
    string AutomationId,
    Surface Surface,
    string? AssertionKey);
