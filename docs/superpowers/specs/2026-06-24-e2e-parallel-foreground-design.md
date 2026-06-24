# E2E harness: parallelism + foreground minimization — design

**Date:** 2026-06-24
**Status:** Approved (design)

## Goal

Optimize the `Scalpel.E2E` harness to run with **maximum parallelism on a single machine**
(no VMs), where **only the actions that genuinely require the foreground take it**. Everything
else — UIA Invoke clicks, UIA reads, log/PdfPig verification — runs concurrently.

## The single shared resource: the foreground + physical input

A Windows desktop has exactly **one foreground window and one physical mouse/keyboard**.
The harness drives controls two ways (`AppDriver.Click`):

- **Plain `Button` → UIA `Invoke` pattern.** Programmatic; works on a **background** window;
  does **not** touch the cursor. Background-safe, parallel-safe.
- **`RadioButton`/`ToggleButton`/`CheckBox` → physical click** (FlaUI `el.Click()` / SendInput).
  WPF only raises the `Click` routed event (which the app's JSONL logger hooks) on a real
  click, and a synthetic click lands on whatever window is foreground. **Needs foreground +
  the shared cursor.**
- **Canvas clicks + `Keyboard.Type`** (annotation place/type/commit, double-click-edit) —
  physical mouse + keyboard. **Needs foreground.**

So: model the foreground/physical-input as a **single global 1-permit gate**. Physical actions
acquire it; Invoke clicks and all reads never touch it. While one worker holds the gate for a
physical click, every other worker keeps doing background work.

In the catalog this means only the **4 mode tabs + 3 theme + 4 accent + 9 language radios**
take the foreground; the ~18 plain buttons (Open/Save/Zoom/Sidebar/View*/Tool*/Settings/OpenLogs)
run gate-free.

## Architecture

### 1. Per-instance app support — `SCALPEL_LOG_DIR`

The session log is `scalpel-<timestamp-to-second>.jsonl` (no PID), so two instances launched in
the same second would share a file. Add an env hook in `App.OnStartup`:
`Logger.Init(baseDir: Environment.GetEnvironmentVariable("SCALPEL_LOG_DIR") , …)`. Unset → default
dir (no behaviour change for users). `AppDriver` sets a unique dir per instance via
`ProcessStartInfo.Environment`, making per-instance log discovery unambiguous.

### 2. Foreground gate in the driver

`AppDriver` gets a process-wide `static SemaphoreSlim ForegroundGate = new(1,1)`:

- `Click(id)`: Invoke branch → unchanged, no gate. Physical branch → `ForegroundGate.Wait();
  try { FocusMainWindow(); el.Click(); } finally { Release(); }`.
- New `public void WithForeground(Action body)` wraps a multi-step physical sequence (canvas
  place→type→commit) in one gate hold + `FocusMainWindow()`.
- The physical input helpers (`ClickCanvas`, `DoubleClickCanvas`, `TypeText`, `ClickPoint`,
  `Resize` is UIA so it stays gate-free) are only called inside `WithForeground` by suites.
- `ActionRunner` no longer calls `FocusMainWindow()` unconditionally after every action (that
  was a gratuitous foreground grab). It keeps `DismissModals` (UIA, per-instance, background-safe).
  The next physical `Click` re-foregrounds under the gate when actually needed.

Correctness guarantee: the **physical path always takes the gate at runtime**, independent of any
static classification — so the parallel scheduler can never cause two physical clicks to race the
cursor, even if a control were mis-categorised.

### 3. Instance pool + work-queue orchestrator

`InstancePool` launches **K** isolated instances (sequentially, so their foreground-grabbing
startup doesn't race). Each instance owns: its own `AppDriver`+`UIA3Automation`, its own **corpus
copy** (`scalpel-e2e-corpus-<i>`), its own **log dir**, and its own `ActionRunner` + `RunReport`.
Per-instance isolation means the only shared things across instances are the **foreground gate**
and the **cursor/keyboard** (both covered by the gate) — no shared app/UIA/file state.

`ParallelOrchestrator` holds a thread-safe queue of **suite-jobs** (`singles`, `journeys`,
`pairwise`, `monkey`, `fonts`). K worker tasks each: pull a job, run it **sequentially on their
instance** (app state is per-instance and must stay serial within an instance), repeat until the
queue drains. Suites run concurrently across instances; physical clicks serialize on the gate.

K default = `min(jobCount, max(2, Environment.ProcessorCount / 2))`, overridable via `--instances`.

Reports merge at the end (each job ran against its instance's `RunReport`; orchestrator unions
`Results` and `UntestedControls`). The exit-code contract (fail on any failure or uncatalogued
control) is unchanged.

### 4. Program flags

- `--parallel` (new default for the full run) / `--sequential` (the old one-driver path, kept for
  debugging and single-suite runs).
- `--instances <K>` to cap concurrency.
- Single-suite runs (`--suite singles`) stay single-instance sequential (nothing to parallelize
  across).

### 5. Scripts/docs

`run-all-suites.ps1` becomes a **single** `dotnet run -- --suite all --parallel` invocation
(was: a fresh process per suite, serial). README updated to explain the gate model and that
parallelism is bounded by the physical-click fraction, not eliminated.

## Why this is the single-machine optimum

- **No idle foreground time:** the gate is held only for the microseconds–seconds of an actual
  physical click/typing burst; the rest of every worker's time is background work that overlaps.
- **No false serialization:** Invoke clicks, UIA reads, PdfPig and log verification — the bulk of
  wall-clock — run fully concurrently across K instances.
- **No VMs needed:** parallelism comes from K app instances sharing one desktop, arbitrated by the
  gate, rather than K desktops.

The theoretical floor is the total physical-input time (one global serial stream of real
clicks/keystrokes); everything else compresses by ~K.

## Risks / mitigations

- **COM/UIA threading:** each instance uses its own `UIA3Automation` from its own worker task
  (threadpool = MTA); UIA is free-threaded. No cross-instance UIA sharing.
- **Registry races** (theme/locale/accent writes are global `HKCU`): harmless — each instance
  verifies its own click via its own log + its own UI-state, not the registry value.
- **Launch foreground races:** instances launch sequentially before parallel work begins.
- **Heaviness:** K capped (default ≤ ~half the cores); `--instances 1` reproduces sequential.

## Out of scope

- Sharding a single suite across instances (suite-level concurrency captures the bulk of the win;
  the foreground radios in `singles` serialize on the gate regardless).
- A headless Invoke-only rewrite (would lose the real-`Click`→logger verification the harness is
  built on).
