# Scalpel — Logging

Scalpel writes a **local-only diagnostic log** of what happens during a session: app start/exit, every button/menu click, the outcome of major operations (open, save, merge, etc.), and any error or crash. It exists to make functionality **testable and reproducible** — you can run the app, exercise a feature, and read back exactly what happened.

Like everything else in Scalpel, the log is **private and offline**: it is written only to the local machine and is **never transmitted anywhere** (no telemetry, no cloud). See [`OVERVIEW.md`](OVERVIEW.md) for the product at large.

---

## 1. Where the logs are (per user)

Logs live under the current user's local app-data folder, one subfolder, one file per app session:

```
%LOCALAPPDATA%\Scalpel\logs\scalpel-<YYYYMMDD-HHmmss>.jsonl
```

`%LOCALAPPDATA%` is **per-user** — it resolves to a different folder for each Windows account:

| User signed in as | Actual log folder |
|-------------------|-------------------|
| `Liraz`           | `C:\Users\Liraz\AppData\Local\Scalpel\logs\` |
| `Alice`           | `C:\Users\Alice\AppData\Local\Scalpel\logs\` |
| any `<user>`      | `C:\Users\<user>\AppData\Local\Scalpel\logs\` |

There is no shared/machine-wide log — each user only ever sees their own. A user with a roaming profile still gets a **local** (non-roaming) folder, so logs do not follow them between machines.

**One file per run.** Each launch creates a new file named with the local-time launch timestamp, e.g. `scalpel-20260621-081144.jsonl`. This keeps every test run cleanly separated. The timestamps *inside* the file are UTC (see the schema below); the filename uses local time.

**Microsoft Store / MSIX build.** Verified on a real Store install (package `LirazShakaAmir.ScalpelPDF_6wbaw9fmp3y9t`, Aug 2026): the packaged app writes its logs to the **same real** `%LOCALAPPDATA%\Scalpel\logs\` folder as the portable build. Only the registry is virtualized (`%LOCALAPPDATA%\Packages\<PackageFamilyName>\SystemAppData\Helium\User.dat`); `LocalCache\Local\Scalpel` does not get created. If the real folder is ever empty on a Store machine, check the virtualized fallback anyway:

```
%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\Scalpel\logs\
```

The in-app **Open logs folder** button (see §5) always opens the correct location regardless of install type — use it rather than typing the path by hand.

---

## 2. Format: JSONL

The file is **JSONL** — one JSON object per line, so it is both human-skimmable and machine-parseable (one `JSON.parse` per line; no streaming parser needed). Each line has this shape:

```jsonc
{
  "ts":    "2026-06-21T05:11:44.007Z",   // UTC timestamp, ISO-8601, millisecond precision
  "level": "INFO",                        // DEBUG | INFO | WARN | ERROR
  "cat":   "File",                        // category (see §4)
  "event": "open.success",                // short dot-namespaced event id
  "msg":   "PDF opened",                  // human-readable summary
  "data":  { "path": "...", "pages": 2 }, // optional structured details
  "error": {                              // present only on logged exceptions
    "type": "IOException", "message": "...", "stack": "..."
  }
}
```

`data` and `error` are optional; the first five fields are always present.

### Real example (an actual session)

```json
{"ts":"2026-06-21T05:11:44.007Z","level":"INFO","cat":"App","event":"app.start","msg":"Scalpel 1.5.1.8 starting","data":{"packaged":false}}
{"ts":"2026-06-21T05:11:44.784Z","level":"INFO","cat":"File","event":"open.success","msg":"PDF opened","data":{"path":"C:\\Users\\Liraz\\Documents\\invoice.pdf","pages":2}}
{"ts":"2026-06-21T05:11:46.994Z","level":"INFO","cat":"UI","event":"click","msg":"ZoomInBtn","data":{"label":"...","type":"Button"}}
{"ts":"2026-06-21T05:11:50.722Z","level":"INFO","cat":"UI","event":"click","msg":"SettingsBtn","data":{"label":"...","type":"Button"}}
```

---

## 3. Levels

| Level   | Meaning |
|---------|---------|
| `DEBUG` | Verbose / high-frequency detail. |
| `INFO`  | Normal events: clicks, successful operations, lifecycle. **Most lines are INFO.** |
| `WARN`  | Something recoverable went wrong. |
| `ERROR` | A failure or a caught/uncaught exception (carries the `error` object). |

All levels are captured by default. (Internally a minimum level can be raised to filter low-priority lines, but the shipped default records everything from `DEBUG` up.)

---

## 4. What gets logged (categories & events)

| Category   | Example events | When |
|------------|----------------|------|
| `App`      | `app.start`, `app.exit`, `instance.forwarded`, `instance.forward.fail`, `instance.pipe.fail` | Startup (with version + packaged flag) and graceful shutdown. `instance.forwarded` means a second launch (file-association double-click / Store tile) handed its file to this already-running window (`Services/SingleInstance.cs`); the forwarding process itself writes no log. Set `SCALPEL_MULTI_INSTANCE=1` to run separate processes (the E2E harness does). |
| `UI`       | `click` | **Every** button / menu click, via one global handler. `msg` is the control's name (e.g. `SettingsBtn`). ScrollBar repeat-buttons are intentionally skipped to avoid hot-path spam. |
| `File`     | `open.success`/`open.fail`/`open.recovered`/`open.recover.fail`, `adopt.success` (a Tools operation's output replaced the working copy), `reopen.recovered`, `repair.pdfium.fail`, `save.success`/`save.fail`/`save.reload.fail`, `flatten.success`/`.fail`, `merge.success`/`.fail`, `extract.success`/`.fail` | Outcome of document operations, with counts/paths in `data`. `open.fail` carries the exception that ended the open fallback chain plus, in `data.trigger`, the original exception that started the fallback. |
| `Sign`     | `sign.success`, `signature.load.fail`/`signature.save.fail` | A signature was placed on a page; `signatures.json` could not be read/written. |
| `Dialog`   | `dialog.error`, `dialog.warning` | **Every** error/warning dialog the app shows (`ScalpelDialog.Show` with an Error/Warning icon), logged centrally with the dialog text as `msg` and the title in `data`. Guarantees a user's "I got an error saying X" is always findable, even if the call site emits no `*.fail` of its own. |
| `Print`    | `print.success`/`print.fail` | A print job was confirmed/completed. |
| `Settings` | `logging.toggle` | The logging on/off setting changed. |
| `Error`    | `crash.dispatcher`, `crash.appdomain`, `crash.task`, plus named internal failures (e.g. `GetPageFormFields`) | Unhandled exceptions (all three crash sinks log **and flush** before the crash dialog) and caught internal errors. |

### Crash safety

Each line is flushed to disk the moment it is written (`AutoFlush`), and the three crash handlers explicitly flush again before showing the crash dialog. **A crash — or even a hard process kill — does not lose the preceding lines.** Note the corollary: `app.exit` is only written on a *graceful* shutdown (window close); a force-kill skips it, which is expected.

---

## 5. Controlling logging (in-app)

Open **Settings → Diagnostics**:

- **Enable logging** — on by default. Unchecking it stops writing immediately for the rest of the session and persists the choice.
- **Open logs folder** — opens the per-user `logs\` folder in Explorer (works in both portable and Store installs).
- **Clear logs** — after confirmation, deletes every session log **except the one currently open**.

**Retention:** on every startup, Scalpel automatically deletes session logs older than **90 days** (`Logger.MaxLogAge`) and, as a backstop, trims the oldest files so that at most **200** sessions remain (`Logger.MaxLogFiles`). Sessions are typically a few KB, so the folder stays small. The window used to be 7 days; that was too short - users report problems weeks after they happen, and the offending session had already been swept.

**Persistence:** the on/off choice is stored in the registry at `HKCU\Software\Scalpel\Settings`, value `LoggingEnabled` (`"1"` = on, `"0"` = off; absent = on).

---

## 6. Using the logs for QA

Because the format is JSONL, standard tools work directly.

**PowerShell** — read the newest session and pull the failures:

```powershell
$dir   = "$env:LOCALAPPDATA\Scalpel\logs"
$latest = Get-ChildItem $dir -Filter 'scalpel-*.jsonl' | Sort-Object LastWriteTime -Desc | Select -First 1
Get-Content $latest.FullName | ForEach-Object { $_ | ConvertFrom-Json } |
  Where-Object level -in 'WARN','ERROR' |
  Select-Object ts, cat, event, msg
```

**`jq`** — every click in order, or just operation outcomes:

```bash
jq -r 'select(.cat=="UI") | "\(.ts) \(.msg)"'              scalpel-*.jsonl   # clicks
jq -c 'select(.event|endswith(".success") or endswith(".fail"))' scalpel-*.jsonl  # outcomes
```

**Reproducing a bug report:** ask the user to **Open logs folder**, zip the relevant `scalpel-*.jsonl`, and attach it. The `app.start` line pins the exact version; the click trail shows precisely what they did before the failing `*.fail` / `crash.*` line.

---

## 7. Implementation pointers

- **`Services/Logger.cs`** — the static, thread-safe, never-throws logger (file lifecycle, JSONL serialization, retention sweep, enable/disable).
- **`App.xaml.cs`** — `Logger.Init`/`Shutdown`, the global click handler, and the three crash sinks.
- **`MainWindow.xaml.cs`** — the Settings → Diagnostics controls and the per-operation outcome logs.
- **`Scalpel.Tests/LoggerTests.cs`** — unit tests (line shape, level filtering, disable no-op, 90-day retention + 200-file cap, clear, exception serialization). Any test class that calls `Logger.Init` must carry `[Collection("Logger")]` - the logger is a process-wide static and xUnit otherwise runs classes in parallel.
- **`MainWindow.Dialogs.cs`** — `ScalpelDialog.Show` emits `Dialog/dialog.error` / `dialog.warning` for every Error/Warning dialog; **`MainWindow.FileOps.cs`** — `ReportOpenFailure` emits `File/open.fail` at the end of the open fallback chain.

The design and rationale are recorded in [`superpowers/specs/2026-06-21-logging-system-design.md`](superpowers/specs/2026-06-21-logging-system-design.md).
