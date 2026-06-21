namespace Scalpel.E2E;

public enum Outcome { Pass, Fail }

public sealed record ActionResult(
    string Suite,
    string Action,
    Outcome Outcome,
    string? FailReason,
    IReadOnlyList<LogEntry> LogContext);
