# Scalpel.E2E — end-to-end UI test harness

Drives the **real** `Scalpel.exe` through Windows UI Automation (FlaUI), verifies
behaviour against the JSONL session logs (see [`docs/LOGGING.md`](../docs/LOGGING.md))
plus targeted UI-state assertions, and emits a Markdown + JSON report.

Design: [`docs/superpowers/specs/2026-06-21-e2e-test-harness-design.md`](../docs/superpowers/specs/2026-06-21-e2e-test-harness-design.md).
Plan: [`docs/superpowers/plans/2026-06-21-e2e-test-harness.md`](../docs/superpowers/plans/2026-06-21-e2e-test-harness.md).

## What it does

| Suite | What it covers |
|-------|----------------|
| `singles`  | Every catalogued control pressed once, in a valid context. Plus a live-tree **coverage cross-check** that flags any clickable button not in the catalog. |
| `journeys` | Scripted realistic workflows (view/zoom tour, edit-tool tour, sign mode, settings round-trip). |
| `pairwise` | Every ordered pair of Edit-mode tools (state-leak detection). |
| `monkey`   | A **seeded** random-action stress run incl. window resize. The seed makes any crash reproducible from the JSONL click-trail. |
| `fonts`    | Drives the new font/Hebrew features via the **canvas** (3 scenarios): **A** place a NEW Hebrew text annotation (Text tool → click → type Hebrew → commit by re-clicking the tool → Ctrl+S) and assert (PdfPig) a Hebrew-block char (U+0590–U+05FF) burned in; **B** edit EXISTING Hebrew (Select tool → double-click the text → append → commit → save) and assert Hebrew in output; **C** the font-missing **toast** — open a PDF whose text names an uninstalled font, double-click it, assert `ToastCopyBtn` becomes visible. Exercises `BidiReorder`, the Noto-Hebrew fallback, `DrawTextRun`, `FontResolver`, and the toast. |
| `tabs`     | Chrome-style document tabs (sequential only - not in the `--parallel` pool): switch keeps annotations/dirty, close prompts (Save / Don't save / Cancel), multi-open, the all-tabs chevron, Save As renames, Ctrl+1..9 / Ctrl+PageUp/Down, zoom presets on Ctrl+Shift+2/3 and Ctrl+0, per-tab zoom + page, already-open switch, the tab right-click menu (Copy path / Close tabs to the right / Close other tabs), per-tab temp cleanup, restore of open tabs at startup (no-argument relaunch), single-instance forwarding of a second launch (needs no other Scalpel running), opens queued behind a long operation, window close with dirty tabs. Manual only: drag-to-reorder and middle-click close. |

Verification per action: the app stays alive (no crash), the expected `UI/click`
event appears in the log, no `ERROR`/`crash.*`/`*.fail` line appears, and — for
high-value actions — a UI-state assertion holds (mode tab selected, zoom changed,
settings overlay open, …).

## Build

The harness builds with the **.NET 8 SDK** (targets `net48`, x64). `dotnet` may be
at `~/.dotnet/dotnet.exe`.

```powershell
dotnet publish ..\Scalpel.csproj -c Release       # produce the EXE under test
dotnet build Scalpel.E2E\Scalpel.E2E.csproj
```

The FlaUI-free pieces (`LogEntry`, `LogReader`, `Reporter`, `Corpus`, `Catalog`,
result models) are unit-tested via the main `Scalpel.Tests` project (`dotnet test`).

## Run

**Recommended — the full set in parallel:**

```powershell
pwsh -File Scalpel.E2E\run-all-suites.ps1
```

This runs `--suite all --parallel`: the suites are spread across a pool of isolated app
instances and run concurrently (see "Parallelism + foreground" below).

Single suite (sequential, one instance):

```powershell
dotnet run --project Scalpel.E2E -- --suite singles `
  --app bin\Release\net48\publish\Scalpel.exe --report-dir e2e-reports --stamp run1
```

Flags: `--suite singles|journeys|pairwise|monkey|fonts|save|tabs|all`, `--parallel` (default for
`all`), `--sequential` (force the classic one-instance path), `--instances <K>` (cap
concurrency; default `min(jobCount, max(2, cores/2))`), `--app <Scalpel.exe>`,
`--report-dir <dir>`, `--seed <int>` (monkey), `--stamp <id>` (names the report files;
scripts can't use the wall clock). Exit code is non-zero on any failure or any
uncatalogued control. Drop real PDFs into `..\tests\fixtures\` to fold them in.

## Parallelism + foreground

The harness drives controls two ways. **Plain `Button`s** use the UIA `Invoke` pattern —
it works on a **background** window and never touches the cursor, so it is parallel-safe.
**`ToggleButton`/`RadioButton`/`CheckBox`** (and canvas mouse/keyboard input) need a
**physical** click: WPF raises the `Click` the app's logger hooks only on a real click,
which lands on whatever window is foreground.

So the foreground + physical cursor/keyboard is modelled as a single machine-wide
1-permit gate (`AppDriver.ForegroundGate`). Physical actions acquire it (foreground →
act → release); Invoke clicks and all UIA/log/PdfPig verification never touch it. The
runner does this on **one machine, no VMs**: a pool of isolated app instances (each with
its own process, corpus copy, private log dir via `SCALPEL_LOG_DIR`, and result bucket)
runs the suites concurrently; only the genuine physical-input fraction serializes on the
gate, while everything else overlaps. Wall-clock approaches the total physical-input time
plus the slowest single suite, rather than the sum of all suites.

`--instances 1` (or `--sequential`) reproduces the classic serial behaviour for debugging.

## Known limitations / intentional exclusions

- **Not auto-exercised:** `LogEnabledCheck` and `ClearLogsBtn` (they toggle/delete the
  very logging the harness reads to verify results), `InstallBtn` (would self-install
  the portable app), and window-chrome buttons (`Close`/`Minimize`/`Maximize`). These
  are excluded from the coverage gap, not silently missed.
- **`ViewGridBtn`** can intermittently fail to register its click in the dense singles
  sequence (its hit-point shifts after preceding view-mode switches). It works in
  isolation; treat a lone `ViewGridBtn` miss as harness flakiness, not an app bug.
- Monkey failures are mostly `control not found` — a random click on a control that
  isn't valid in the current mode. Expected noise; the signal is **crashes**, of which
  there have been none across the development runs.
