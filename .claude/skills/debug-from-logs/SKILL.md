---
name: debug-from-logs
description: Use when investigating a Scalpel bug, crash, regression, or unexpected behavior, or when asked to read, triage, or verify Scalpel's session logs — the JSONL files under %LOCALAPPDATA%\Scalpel\logs. Symptoms include a *.fail or crash.* event, an operation that "didn't work," or a log line that looks wrong.
---

# Debug Scalpel from its session logs

Scalpel writes a JSONL diagnostic log per session (clicks, operation outcomes, errors, crashes). This skill turns that log into a fix: **read the log → find the anomaly → trace the event back to the code that emitted it → confirm it's a real bug in the current code → fix → re-run and re-read the log to prove it.**

Core principle: **a log line is a symptom, not a verdict.** Every finding must be confirmed against the current source before you change anything — the code may already be fixed, or the line may be benign.

Full format/location reference: [`docs/LOGGING.md`](../../../docs/LOGGING.md).

## Step 1 — Locate and read the log

Logs are per-user: `%LOCALAPPDATA%\Scalpel\logs\scalpel-<YYYYMMDD-HHmmss>.jsonl` (one file per app launch; filename is local launch time, timestamps inside are UTC).

```powershell
$dir    = "$env:LOCALAPPDATA\Scalpel\logs"
$latest = Get-ChildItem $dir -Filter 'scalpel-*.jsonl' | Sort-Object LastWriteTime -Desc | Select -First 1
$lines  = Get-Content $latest.FullName | ForEach-Object { $_ | ConvertFrom-Json }
$lines | Select ts, level, cat, event, msg | Format-Table -Auto
```

Use the newest file unless the user named a session or timeframe. For a packaged/Store install the same path is virtualized under `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\Scalpel\logs`.

## Step 2 — Scan for anomalies

Look for, in priority order:

| Signal | How to spot it | What it usually means |
|--------|----------------|-----------------------|
| Crash | `event` starts `crash.` (`crash.dispatcher/appdomain/task`), has an `error` object | Unhandled exception — read `error.type`/`stack`. |
| Operation failure | `event` ends `.fail` (`save.fail`, `merge.fail`, …) or `level` is `ERROR`/`WARN` | An operation threw; `error` carries the cause. |
| Behavioral anomaly | An event fires with **no plausible preceding user action**, repeats far more than expected, or is **missing** when it should appear | A side-effect bug (e.g. an event emitted on panel-open instead of on user action). |

```powershell
$lines | Where-Object { $_.level -in 'ERROR','WARN' -or $_.event -like '*.fail' -or $_.event -like 'crash.*' }
```

For behavioral anomalies, read the trail in order: each `UI/click` (the `msg` is the control name) should explain the events that follow it. An outcome event with no triggering click before it is suspicious.

## Step 3 — Trace the event to the code that emitted it

Every log call is `Logger.<Level>("<cat>", "<event>", ...)`. Grep for the exact pair — this lands you on the emitting line:

```bash
grep -rn 'Logger\.\(Info\|Warn\|Error\|Debug\)("<cat>", "<event>"' --include=*.cs .
# e.g.  Logger.Info("Settings", "logging.toggle"   →  MainWindow.xaml.cs handler
```

Implementation pointers (from `docs/LOGGING.md`):
- `Services/Logger.cs` — the logger itself.
- `App.xaml.cs` — `app.start`/`app.exit`, the global `UI/click` handler, the three `crash.*` sinks.
- `MainWindow.xaml.cs` — every `File`/`Sign`/`Print`/`Settings` outcome and the Diagnostics controls.

## Step 4 — Verify it's a real bug in the current code

Read the method around the emitting line and answer:
- For a `.fail`/`crash.*`: is the exception a genuine defect, or expected/handled input (e.g. user cancelled, malformed PDF the fallback chain handles)? Check the surrounding `try/catch`.
- For a behavioral anomaly: does the code path actually fire this event for the reason the log implies? Reproduce the trigger mentally from the call site.
- **Confirm it isn't already fixed.** The log may predate a fix. If the current code already prevents the symptom, say so and stop — do not invent a change.

State the root cause in one sentence before touching anything.

## Step 5 — Fix, then prove it with a fresh log

1. Apply the smallest fix consistent with the codebase conventions (see `CLAUDE.md`: defensive `try/catch`, suppress-flags like `_suppressModeEvents` for programmatic-event side-effects, etc.).
2. Build: `~/.dotnet/dotnet.exe build -c Debug` (expect `0 Error`; ignore `MSB3027/MSB3021` pdfium-copy errors caused by a running Scalpel.exe — those are not code errors).
3. **Re-run and re-read.** Reproduce the original trigger, then re-open the newest log and confirm the anomalous line is gone (or the `.fail`/`crash.*` no longer occurs). A fix you didn't re-observe in the log is unverified. Driving the GUI: launch `bin\Debug\net48\Scalpel.exe` and invoke controls by `x:Name` via UI Automation (`InvokePattern`); note `ToggleButton.Toggle()` does **not** raise `Click`, so it won't produce a `UI/click` line — use it only for state, not for click testing.

## Common mistakes

- **Treating the log as ground truth for the current code.** Always re-check the source (Step 4) — the symptom may be stale.
- **Fixing without re-reading a fresh log.** The log is your test oracle; close the loop (Step 5).
- **Chasing benign lines.** A caught exception inside the PDF fallback chain (e.g. `GetPageLinks` on a malformed file) is often expected, not a bug.
- **Forcing a `UI/click` in tests via `Toggle()`.** It won't appear; real clicks do.
- **Building with Scalpel.exe still running.** Close it first or the build's file-copy step fails (not a compile error).
