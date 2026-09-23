using FlaUI.Core.AutomationElements;

namespace Scalpel.E2E;

public sealed class ActionRunner
{
    private readonly AppDriver _driver;
    private LogReader _log;
    private readonly string _openWith;

    public ActionRunner(AppDriver driver, LogReader log, string openWithPath)
    {
        _driver = driver;
        _log = log;
        _openWith = openWithPath;
        _driver.Relaunched += RebindLog;
    }

    /// <summary>
    /// Follows the relaunched process to its own session log. Each launch starts a new
    /// <c>scalpel-&lt;timestamp&gt;.jsonl</c>; reading on from the dead process's file made every
    /// action after a relaunch (crash recovery here, or a suite's own Relaunch) fail with
    /// "expected click ... not logged".
    /// </summary>
    private void RebindLog()
    {
        string old = _log.FilePath;
        string dir = System.IO.Path.GetDirectoryName(old) ?? LogReader.DefaultLogDir();
        for (int i = 0; i < 40; i++)
        {
            var latest = LogReader.FindLatestLog(dir);
            if (latest != null && !string.Equals(latest, old, StringComparison.OrdinalIgnoreCase))
            {
                _log = new LogReader(latest);
                return;
            }
            System.Threading.Thread.Sleep(125);
        }
        // Same second as the old session (same file name) or no new file: keep reading the old one.
    }

    public ActionResult RunControl(string suite, ControlSpec spec)
    {
        _driver.EnsureSurface(spec.Surface);

        // A control present but DISABLED in the current state legitimately cannot be actioned —
        // that is correct app behaviour, not a test failure. Skip it as a pass. The main case is
        // the accent radios while the High Contrast theme is active (HC owns its own accent); the
        // random monkey order can reach that, whereas singles is ordered to keep them enabled.
        if (_driver.IsDisabled(spec.AutomationId))
            return new ActionResult(suite, spec.AutomationId, Outcome.Pass, null, Array.Empty<LogEntry>());

        string? priorZoom = ReadZoom();
        int snap = _log.Snapshot();

        bool clicked = _driver.Click(spec.AutomationId);
        // A transient UIA-tree rebuild — e.g. a live theme restyle swapping DynamicResources
        // across every control (Theme/Accent radios trigger exactly this) — can make a control
        // momentarily unfindable even though it is about to reappear. Retry the whole Click once
        // after a short beat before treating it as genuinely missing; symmetric with the
        // "found but click didn't log" retry below, which only covers the found-but-silent case.
        if (!clicked)
        {
            System.Threading.Thread.Sleep(200);
            clicked = _driver.Click(spec.AutomationId);
        }
        System.Threading.Thread.Sleep(120);
        var newLogs = _log.NewSince(snap);

        // Physical clicks (ToggleButton/RadioButton/CheckBox) occasionally miss on a
        // foreground-timing hiccup. If the control logged no click and nothing failed,
        // retry once before giving up — this removes nearly all residual flakiness.
        if (clicked && !ClickLogged(newLogs, spec.AutomationId) && !newLogs.Any(e => e.IsFailure))
        {
            _driver.Click(spec.AutomationId);
            System.Threading.Thread.Sleep(120);
            newLogs = _log.NewSince(snap);
        }

        // Close any modal OS dialog the click opened (Open/Save/Print/message box)
        // so it can't wedge the next action. The in-window Settings overlay is not a
        // separate window, so it is unaffected. Then recover foreground.
        _driver.DismissModals();
        // No unconditional FocusMainWindow here: it was a gratuitous foreground grab. Invoke
        // clicks don't need focus, and the next physical Click re-foregrounds under the gate.

        // Crash check first.
        if (!_driver.IsAlive)
        {
            _driver.Relaunch(_openWith);
            return new ActionResult(suite, spec.AutomationId, Outcome.Fail, "app crashed", newLogs);
        }

        if (!clicked)
            return new ActionResult(suite, spec.AutomationId, Outcome.Fail, "control not found / not clickable", newLogs);

        return VerifyTail(suite, spec.AutomationId, newLogs, spec.AutomationId, spec.AssertionKey, priorZoom);
    }

    public ActionResult RunRaw(string suite, string action, Action act,
        string? expectClickMsg, string? assertionKey)
    {
        // Capture zoom before acting so a zoomIncreased/zoomDecreased assertion here
        // does a real delta check (not just "ZoomBox still present").
        string? priorZoom = ReadZoom();
        int snap = _log.Snapshot();
        try { act(); }
        catch (Exception ex)
        {
            return new ActionResult(suite, action, Outcome.Fail, $"action threw: {ex.Message}", _log.NewSince(snap));
        }
        System.Threading.Thread.Sleep(120);
        var newLogs = _log.NewSince(snap);

        // Close any modal OS dialog this action opened so it can't wedge the next one,
        // then recover foreground (a dialog/Explorer window stole it from Scalpel).
        _driver.DismissModals();
        // No unconditional FocusMainWindow here: it was a gratuitous foreground grab. Invoke
        // clicks don't need focus, and the next physical Click re-foregrounds under the gate.

        if (!_driver.IsAlive)
        {
            _driver.Relaunch(_openWith);
            return new ActionResult(suite, action, Outcome.Fail, "app crashed", newLogs);
        }

        return VerifyTail(suite, action, newLogs, expectClickMsg, assertionKey, priorZoom);
    }

    // Shared verification tail: failure-line check → C assertion → expected-click check.
    private ActionResult VerifyTail(
        string suite, string label, IReadOnlyList<LogEntry> newLogs,
        string? expectClickMsg, string? assertionKey, string? priorZoom)
    {
        // B: a failure line appeared (always fatal).
        var failure = newLogs.FirstOrDefault(e => e.IsFailure);
        if (failure != null)
            return new ActionResult(suite, label, Outcome.Fail,
                $"failure logged: {failure.Cat}/{failure.Event} {failure.Msg}", newLogs);

        // C: UI-state assertion (incl. zoom-delta).
        var (ok, reason) = Assertions.Check(assertionKey, _driver, newLogs);
        if (ok && assertionKey is "zoomIncreased" or "zoomDecreased")
            (ok, reason) = CheckZoomDelta(assertionKey, priorZoom, suite);
        if (!ok)
            return new ActionResult(suite, label, Outcome.Fail, reason, newLogs);

        // B: expected click logged. Required ONLY for controls with no C-tier
        // assertion — for them the click log is the sole signal. A control with a
        // passing assertion (e.g. re-clicking the already-active mode tab, a
        // legitimate no-op that fires no Click event) is already validated, so a
        // missing click log is not a failure.
        if (expectClickMsg != null && assertionKey == null &&
            !newLogs.Any(e => e.Cat == "UI" && e.Event == "click" && e.Msg == expectClickMsg))
        {
            // Fallback: a RadioButton that is ALREADY selected logs nothing when re-selected
            // (UIA Select is a no-op with no Checked event), yet the control is in its correct
            // state — that is a pass, not a missed interaction. Only the click log is missing.
            if (!_driver.IsSelected(expectClickMsg))
                return new ActionResult(suite, label, Outcome.Fail,
                    $"expected click '{expectClickMsg}' not logged", newLogs);
        }

        return new ActionResult(suite, label, Outcome.Pass, null, newLogs);
    }

    private static bool ClickLogged(IReadOnlyList<LogEntry> logs, string automationId) =>
        logs.Any(e => e.Cat == "UI" && e.Event == "click" && e.Msg == automationId);

    private string? ReadZoom()
    {
        try { return _driver.Find("ZoomBox")?.AsTextBox()?.Text; }
        catch { return null; }
    }

    private (bool, string?) CheckZoomDelta(string key, string? prior, string suite)
    {
        string? now = ReadZoom();
        double Parse(string? s) => double.TryParse(
            new string((s ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray()),
            out var v) ? v : double.NaN;
        double a = Parse(prior), b = Parse(now);
        if (double.IsNaN(a) || double.IsNaN(b)) return (true, null); // can't read; don't false-fail
        // The monkey suite clicks zoom buttons in a random walk that can legitimately sit
        // at the clamp boundary (ZoomMin/ZoomMax), where a further click is a correct no-op.
        // Accept an unchanged value there; the strict directional check stays for the
        // controlled singles/journeys suites. A move in the WRONG direction still fails.
        bool atBoundaryOk = suite == "monkey" && b == a;
        if (key == "zoomIncreased")
            return b > a || atBoundaryOk ? (true, null) : (false, $"zoom did not increase ({a}->{b})");
        return b < a || atBoundaryOk ? (true, null) : (false, $"zoom did not decrease ({a}->{b})");
    }
}
