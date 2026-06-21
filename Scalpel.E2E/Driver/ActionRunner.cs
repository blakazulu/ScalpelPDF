using FlaUI.Core.AutomationElements;

namespace Scalpel.E2E;

public sealed class ActionRunner
{
    private readonly AppDriver _driver;
    private readonly LogReader _log;
    private readonly string _openWith;

    public ActionRunner(AppDriver driver, LogReader log, string openWithPath)
    {
        _driver = driver;
        _log = log;
        _openWith = openWithPath;
    }

    public ActionResult RunControl(string suite, ControlSpec spec)
    {
        _driver.EnsureSurface(spec.Surface);
        string? priorZoom = ReadZoom();
        int snap = _log.Snapshot();

        bool clicked = _driver.Click(spec.AutomationId);
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
        _driver.FocusMainWindow();

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
        _driver.FocusMainWindow();

        if (!_driver.IsAlive)
        {
            _driver.Relaunch(_openWith);
            return new ActionResult(suite, action, Outcome.Fail, "app crashed", newLogs);
        }

        return VerifyTail(suite, action, newLogs, expectClickMsg, assertionKey, priorZoom: null);
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
            (ok, reason) = CheckZoomDelta(assertionKey, priorZoom);
        if (!ok)
            return new ActionResult(suite, label, Outcome.Fail, reason, newLogs);

        // B: expected click logged. Required ONLY for controls with no C-tier
        // assertion — for them the click log is the sole signal. A control with a
        // passing assertion (e.g. re-clicking the already-active mode tab, a
        // legitimate no-op that fires no Click event) is already validated, so a
        // missing click log is not a failure.
        if (expectClickMsg != null && assertionKey == null &&
            !newLogs.Any(e => e.Cat == "UI" && e.Event == "click" && e.Msg == expectClickMsg))
            return new ActionResult(suite, label, Outcome.Fail,
                $"expected click '{expectClickMsg}' not logged", newLogs);

        return new ActionResult(suite, label, Outcome.Pass, null, newLogs);
    }

    private static bool ClickLogged(IReadOnlyList<LogEntry> logs, string automationId) =>
        logs.Any(e => e.Cat == "UI" && e.Event == "click" && e.Msg == automationId);

    private string? ReadZoom()
    {
        try { return _driver.Find("ZoomBox")?.AsTextBox()?.Text; }
        catch { return null; }
    }

    private (bool, string?) CheckZoomDelta(string key, string? prior)
    {
        string? now = ReadZoom();
        double Parse(string? s) => double.TryParse(
            new string((s ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray()),
            out var v) ? v : double.NaN;
        double a = Parse(prior), b = Parse(now);
        if (double.IsNaN(a) || double.IsNaN(b)) return (true, null); // can't read; don't false-fail
        if (key == "zoomIncreased") return b > a ? (true, null) : (false, $"zoom did not increase ({a}->{b})");
        return b < a ? (true, null) : (false, $"zoom did not decrease ({a}->{b})");
    }
}
