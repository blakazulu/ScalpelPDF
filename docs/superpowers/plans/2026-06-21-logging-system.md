# Logging System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local-only, JSONL session-logging system to Scalpel that records every click, every major operation success, and every failure to disk for QA.

**Architecture:** A new static `Services/Logger.cs` writes one JSONL file per session to `%LOCALAPPDATA%\Scalpel\logs\`. Writes are guarded by a lock and use an `AutoFlush` `StreamWriter` opened with `FileShare.ReadWrite` so writes are crash-safe and the file is readable while open. A single global WPF class handler captures all button/menu clicks; the three existing crash sinks and the major file operations add explicit outcome logs. A Settings panel section toggles logging, opens the logs folder, and clears logs.

**Tech Stack:** C# / .NET Framework 4.8, WPF, `System.Text.Json` (already referenced), xUnit.

## Global Constraints

- Target **.NET Framework 4.8** (`net48`), x64. Build requires .NET 8 SDK. `dotnet` may be at `~/.dotnet/dotnet.exe`.
- `Nullable` enabled, `ImplicitUsings` enabled, `LangVersion=latest`. Use collection expressions `[]`, target-typed `new`, switch expressions to match house style.
- All I/O wrapped in swallow-and-continue `try { } catch { }`. **The logger must never throw into a caller** — a logging failure can never crash or interrupt the app.
- Settings persist in registry `HKCU\Software\Scalpel\Settings` via `App.GetSetting` / `App.SetSetting` (string values).
- Localization rule: **every** `Str_*` key MUST exist in **all six** `Strings/*.xaml` files (en-US, es, zh-TW, zh-CN, bn, tr-TR) or a `DynamicResource` lookup blanks out.
- Tests link source files directly via `<Compile Include="..\Services\...">` — do not add a project reference.
- Build/test commands: `dotnet build` ; `dotnet test --filter "FullyQualifiedName~Logger"`.

---

### Task 1: Logger core + unit tests

**Files:**
- Create: `Services/Logger.cs`
- Create: `Scalpel.Tests/LoggerTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj:30` (add a Compile link)

**Interfaces:**
- Produces (consumed by Tasks 2-4):
  - `static class Scalpel.Services.Logger`
  - `enum Logger.Level { Debug, Info, Warn, Error }`
  - `static bool Logger.Enabled { get; set; }`
  - `static string Logger.LogDirectory { get; }`
  - `static void Logger.Init(string? baseDir = null, Level minLevel = Level.Debug, bool enabled = true)`
  - `static void Logger.SetEnabled(bool on)`
  - `static void Logger.Debug(string cat, string evt, string msg, object? data = null)`
  - `static void Logger.Info (string cat, string evt, string msg, object? data = null)`
  - `static void Logger.Warn (string cat, string evt, string msg, object? data = null)`
  - `static void Logger.Error(string cat, string evt, string msg, Exception? ex = null, object? data = null)`
  - `static void Logger.Flush()`
  - `static void Logger.Shutdown()`
  - `static void Logger.ClearLogs()`

- [ ] **Step 1: Add the Compile link to the test project**

Modify `Scalpel.Tests/Scalpel.Tests.csproj` — add inside the existing `<ItemGroup>` that holds the other `<Compile Include>` links (after line 30):

```xml
    <Compile Include="..\Services\Logger.cs" Link="Services\Logger.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `Scalpel.Tests/LoggerTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class LoggerTests : IDisposable
    {
        private readonly string _dir;

        public LoggerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "scalpel_logtest_" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Logger.Shutdown(); } catch { }
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }

        private string ReadLogLines() // returns the single session file's content
        {
            var file = Directory.GetFiles(_dir, "scalpel-*.jsonl").Single();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }

        [Fact]
        public void Info_writes_one_valid_json_line_with_expected_fields()
        {
            Logger.Init(_dir);
            Logger.Info("File", "open.success", "Opened invoice.pdf", new { pages = 12 });
            Logger.Flush();

            var lines = ReadLogLines().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
            Assert.Single(lines);

            using var doc = JsonDocument.Parse(lines[0]);
            var root = doc.RootElement;
            Assert.Equal("INFO", root.GetProperty("level").GetString());
            Assert.Equal("File", root.GetProperty("cat").GetString());
            Assert.Equal("open.success", root.GetProperty("event").GetString());
            Assert.Equal("Opened invoice.pdf", root.GetProperty("msg").GetString());
            Assert.Equal(12, root.GetProperty("data").GetProperty("pages").GetInt32());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("ts").GetString()));
        }

        [Fact]
        public void MinLevel_filters_out_lower_levels()
        {
            Logger.Init(_dir, minLevel: Logger.Level.Info);
            Logger.Debug("UI", "click", "should be dropped");
            Logger.Info("UI", "click", "should be kept");
            Logger.Flush();

            var lines = ReadLogLines().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
            Assert.Single(lines);
            Assert.Contains("should be kept", lines[0]);
        }

        [Fact]
        public void Disabled_creates_no_file_and_calls_are_noops()
        {
            Logger.Init(_dir, enabled: false);
            Logger.Info("UI", "click", "nothing");
            Logger.Flush();

            Assert.Empty(Directory.GetFiles(_dir, "scalpel-*.jsonl"));
        }

        [Fact]
        public void Init_deletes_logs_older_than_seven_days_keeps_recent()
        {
            Directory.CreateDirectory(_dir);
            var old = Path.Combine(_dir, "scalpel-20000101-000000.jsonl");
            var recent = Path.Combine(_dir, "scalpel-20990101-000000.jsonl");
            File.WriteAllText(old, "{}\n");
            File.WriteAllText(recent, "{}\n");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-8));
            File.SetLastWriteTime(recent, DateTime.Now.AddDays(-1));

            Logger.Init(_dir);
            Logger.Shutdown();

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void ClearLogs_deletes_prior_files_but_keeps_current_session()
        {
            Directory.CreateDirectory(_dir);
            var prior = Path.Combine(_dir, "scalpel-20990101-000000.jsonl");
            File.WriteAllText(prior, "{}\n");
            File.SetLastWriteTime(prior, DateTime.Now.AddDays(-1));

            Logger.Init(_dir);
            Logger.Info("App", "app.start", "x");
            Logger.ClearLogs();

            Assert.False(File.Exists(prior));
            Assert.Single(Directory.GetFiles(_dir, "scalpel-*.jsonl")); // only the live session file remains
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LoggerTests"`
Expected: FAIL — `Logger` does not exist (compile error).

- [ ] **Step 4: Implement `Services/Logger.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Scalpel.Services
{
    /// <summary>
    /// Local-only JSONL session logger. One file per app session under
    /// %LOCALAPPDATA%\Scalpel\logs. Thread-safe; never throws into callers.
    /// </summary>
    public static class Logger
    {
        public enum Level { Debug = 0, Info = 1, Warn = 2, Error = 3 }

        private static readonly object _gate = new();
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

        private static StreamWriter? _writer;
        private static string _dir = DefaultDir;
        private static string? _currentFile;
        private static Level _minLevel = Level.Debug;

        public static bool Enabled { get; set; } = true;
        public static string LogDirectory => _dir;

        private static string DefaultDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scalpel", "logs");

        /// <summary>Open a new session log, sweeping logs older than 7 days.</summary>
        public static void Init(string? baseDir = null, Level minLevel = Level.Debug, bool enabled = true)
        {
            lock (_gate)
            {
                _dir = baseDir ?? DefaultDir;
                _minLevel = minLevel;
                Enabled = enabled;
                try
                {
                    Directory.CreateDirectory(_dir);
                    SweepOldLogs(_dir, TimeSpan.FromDays(7));
                    if (enabled) OpenWriter();
                }
                catch { /* logging must never throw */ }
            }
        }

        /// <summary>Toggle logging at runtime; opens the session file lazily when enabled.</summary>
        public static void SetEnabled(bool on)
        {
            lock (_gate)
            {
                Enabled = on;
                try { if (on && _writer == null) OpenWriter(); }
                catch { }
            }
        }

        public static void Debug(string cat, string evt, string msg, object? data = null) => Write(Level.Debug, cat, evt, msg, null, data);
        public static void Info (string cat, string evt, string msg, object? data = null) => Write(Level.Info,  cat, evt, msg, null, data);
        public static void Warn (string cat, string evt, string msg, object? data = null) => Write(Level.Warn,  cat, evt, msg, null, data);
        public static void Error(string cat, string evt, string msg, Exception? ex = null, object? data = null) => Write(Level.Error, cat, evt, msg, ex, data);

        public static void Flush()
        {
            lock (_gate) { try { _writer?.Flush(); } catch { } }
        }

        public static void Shutdown()
        {
            lock (_gate)
            {
                try { _writer?.Flush(); _writer?.Dispose(); } catch { }
                _writer = null;
                _currentFile = null;
            }
        }

        /// <summary>Delete every session log except the one currently open.</summary>
        public static void ClearLogs()
        {
            lock (_gate)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(_dir, "scalpel-*.jsonl"))
                    {
                        if (_currentFile != null &&
                            string.Equals(f, _currentFile, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try { File.Delete(f); } catch { }
                    }
                }
                catch { }
            }
        }

        // ── internals ──────────────────────────────────────────────────

        private static void OpenWriter()
        {
            var name = $"scalpel-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
            _currentFile = Path.Combine(_dir, name);
            // FileShare.ReadWrite so logs can be tailed/read while the app runs.
            var fs = new FileStream(_currentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }

        private static void Write(Level level, string cat, string evt, string msg, Exception? ex, object? data)
        {
            if (!Enabled || level < _minLevel) return;
            try
            {
                var rec = new Dictionary<string, object?>
                {
                    ["ts"]    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["level"] = level.ToString().ToUpperInvariant(),
                    ["cat"]   = cat,
                    ["event"] = evt,
                    ["msg"]   = msg,
                };
                if (data != null) rec["data"] = data;
                if (ex != null)
                    rec["error"] = new { type = ex.GetType().Name, message = ex.Message, stack = ex.StackTrace };

                string line = JsonSerializer.Serialize(rec, _json);
                lock (_gate) { _writer?.WriteLine(line); }
            }
            catch { /* never throw from logging */ }
        }

        private static void SweepOldLogs(string dir, TimeSpan maxAge)
        {
            try
            {
                var cutoff = DateTime.Now - maxAge;
                foreach (var f in Directory.GetFiles(dir, "scalpel-*.jsonl"))
                    try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LoggerTests"`
Expected: PASS — 5 tests passing.

- [ ] **Step 6: Commit**

```bash
git add Services/Logger.cs Scalpel.Tests/LoggerTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat(logging): add JSONL session Logger with tests"
```

---

### Task 2: App lifecycle integration (init, exit, crash sinks, global click capture)

**Files:**
- Modify: `App.xaml.cs:110-114` (OnStartup), `App.xaml.cs:124-159` (3 crash handlers); add an `OnExit` override and a static `RegisterGlobalClickLogging` helper.

**Interfaces:**
- Consumes: `Logger.Init`, `Logger.Info`, `Logger.Error`, `Logger.Flush`, `Logger.Shutdown` (Task 1); `App.GetSetting` (existing).
- Produces: `LoggingEnabled` registry key (`"1"`/`"0"`, default treated as enabled); global `UI/click` logging active for the process.

- [ ] **Step 1: Initialize the logger in `OnStartup`**

In `App.xaml.cs`, replace the block at lines 110-114:

```csharp
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            CleanupStaleTemps();
            ThemeManager.Initialize();
            LocaleManager.Initialize();
            new MainWindow().Show();
```

with:

```csharp
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            CleanupStaleTemps();

            // Logging is on by default; "0" disables it.
            bool loggingEnabled = GetSetting("LoggingEnabled") != "0";
            Scalpel.Services.Logger.Init(enabled: loggingEnabled);
            RegisterGlobalClickLogging();
            var ver = typeof(App).Assembly.GetName().Version?.ToString() ?? "?";
            Scalpel.Services.Logger.Info("App", "app.start", $"Scalpel {ver} starting",
                new { packaged = IsPackaged() });

            ThemeManager.Initialize();
            LocaleManager.Initialize();
            new MainWindow().Show();
```

- [ ] **Step 2: Add the global click-capture helper**

Add these members to the `App` class (place them right after the `OnStartup` method, before the "Crash handling" region near line 116):

```csharp
        // ============================================================
        // Global click logging  (one handler for every button/menu click)
        // ============================================================

        private static bool _clickLoggingRegistered;

        private static void RegisterGlobalClickLogging()
        {
            if (_clickLoggingRegistered) return;
            _clickLoggingRegistered = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
                new RoutedEventHandler(OnAnyControlClicked), handledEventsToo: true);
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent,
                new RoutedEventHandler(OnAnyControlClicked), handledEventsToo: true);
        }

        private static void OnAnyControlClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var src = (e.OriginalSource as FrameworkElement) ?? sender as FrameworkElement;
                string name = !string.IsNullOrEmpty(src?.Name) ? src!.Name : src?.GetType().Name ?? "?";
                string? label = (src as ContentControl)?.Content as string
                                ?? (src as ContentControl)?.Content?.ToString();
                Scalpel.Services.Logger.Info("UI", "click", name,
                    new { label, type = src?.GetType().Name });
            }
            catch { }
        }
```

Add the matching `using` directives at the top of `App.xaml.cs` if not already present:

```csharp
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
```

(`System.Windows` for `RoutedEventHandler`/`FrameworkElement`/`EventManager` is already in scope in this WPF `Application` file; add `using System.Windows;` only if the build reports it missing.)

- [ ] **Step 3: Add an `OnExit` override**

Add to the `App` class (place after the crash-handler region):

```csharp
        protected override void OnExit(ExitEventArgs e)
        {
            Scalpel.Services.Logger.Info("App", "app.exit", "Shutting down");
            Scalpel.Services.Logger.Shutdown();
            base.OnExit(e);
        }
```

If an `OnExit` override already exists, merge these two `Logger` lines in at its start instead of adding a second override.

- [ ] **Step 4: Log + flush in the three crash sinks**

In `OnDispatcherException` (line 124), add as the first statement inside the method body:

```csharp
            Scalpel.Services.Logger.Error("Error", "crash.dispatcher", e.Exception.Message, e.Exception);
            Scalpel.Services.Logger.Flush();
```

In `OnDomainException` (line 136), add immediately after `ex` is assigned (after the `?? new Exception(...)` line):

```csharp
            Scalpel.Services.Logger.Error("Error", "crash.appdomain", ex.Message, ex);
            Scalpel.Services.Logger.Flush();
```

In `OnUnobservedTaskException` (line 155), add as the first statement after `e.SetObserved();`:

```csharp
            Scalpel.Services.Logger.Error("Error", "crash.task", e.Exception.Message, e.Exception);
            Scalpel.Services.Logger.Flush();
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add App.xaml.cs
git commit -m "feat(logging): wire logger into app lifecycle, crash sinks, global clicks"
```

---

### Task 3: Settings UI — toggle, open folder, clear logs (+ 6 locale files)

**Files:**
- Modify: `MainWindow.xaml:1039-1040` (add a Logging section to the Settings overlay `StackPanel`)
- Modify: `MainWindow.xaml.cs:272-291` (`SettingsBtn_Click` sync) and add three handlers near it
- Modify: `Strings/en-US.xaml`, `Strings/es.xaml`, `Strings/zh-TW.xaml`, `Strings/zh-CN.xaml`, `Strings/bn.xaml`, `Strings/tr-TR.xaml`

**Interfaces:**
- Consumes: `Logger.Enabled`, `Logger.SetEnabled`, `Logger.LogDirectory`, `Logger.ClearLogs` (Task 1); `App.SetSetting` (existing).
- Produces: `LogEnabledCheck`, `OpenLogsBtn`, `ClearLogsBtn` named controls; handlers `LogEnabledCheck_Changed`, `OpenLogsBtn_Click`, `ClearLogsBtn_Click`.

- [ ] **Step 1: Add the four locale keys to all six `Strings/*.xaml` files**

Add these four lines to **each** strings file (place them anywhere inside the `<ResourceDictionary>`, e.g. near the other settings keys). Use the per-language text below.

`Strings/en-US.xaml`:
```xml
    <sys:String x:Key="Str_Settings_Diagnostics">Diagnostics</sys:String>
    <sys:String x:Key="Str_Log_Enable">Enable logging</sys:String>
    <sys:String x:Key="Str_Log_OpenFolder">Open logs folder</sys:String>
    <sys:String x:Key="Str_Log_Clear">Clear logs</sys:String>
```

`Strings/es.xaml`:
```xml
    <sys:String x:Key="Str_Settings_Diagnostics">Diagnóstico</sys:String>
    <sys:String x:Key="Str_Log_Enable">Activar registro</sys:String>
    <sys:String x:Key="Str_Log_OpenFolder">Abrir carpeta de registros</sys:String>
    <sys:String x:Key="Str_Log_Clear">Borrar registros</sys:String>
```

`Strings/zh-TW.xaml`:
```xml
    <sys:String x:Key="Str_Settings_Diagnostics">診斷</sys:String>
    <sys:String x:Key="Str_Log_Enable">啟用記錄</sys:String>
    <sys:String x:Key="Str_Log_OpenFolder">開啟記錄資料夾</sys:String>
    <sys:String x:Key="Str_Log_Clear">清除記錄</sys:String>
```

`Strings/zh-CN.xaml`:
```xml
    <sys:String x:Key="Str_Settings_Diagnostics">诊断</sys:String>
    <sys:String x:Key="Str_Log_Enable">启用日志</sys:String>
    <sys:String x:Key="Str_Log_OpenFolder">打开日志文件夹</sys:String>
    <sys:String x:Key="Str_Log_Clear">清除日志</sys:String>
```

`Strings/bn.xaml`:
```xml
    <sys:String x:Key="Str_Settings_Diagnostics">ডায়াগনস্টিকস</sys:String>
    <sys:String x:Key="Str_Log_Enable">লগিং সক্রিয় করুন</sys:String>
    <sys:String x:Key="Str_Log_OpenFolder">লগ ফোল্ডার খুলুন</sys:String>
    <sys:String x:Key="Str_Log_Clear">লগ মুছুন</sys:String>
```

`Strings/tr-TR.xaml`:
```xml
    <sys:String x:Key="Str_Settings_Diagnostics">Tanılama</sys:String>
    <sys:String x:Key="Str_Log_Enable">Günlüğü etkinleştir</sys:String>
    <sys:String x:Key="Str_Log_OpenFolder">Günlük klasörünü aç</sys:String>
    <sys:String x:Key="Str_Log_Clear">Günlükleri temizle</sys:String>
```

- [ ] **Step 2: Add the Logging section to the Settings overlay**

In `MainWindow.xaml`, the language radios end at line 1039 and the `StackPanel` closes at line 1040 (`</StackPanel>`). Insert this block immediately before that `</StackPanel>`:

```xml
                    <TextBlock Text="{DynamicResource Str_Settings_Diagnostics}" Foreground="{DynamicResource Accent}"
                               FontFamily="{DynamicResource FontUI}" FontSize="11" FontWeight="SemiBold"
                               Margin="0,12,0,8"/>
                    <CheckBox x:Name="LogEnabledCheck"
                              Content="{DynamicResource Str_Log_Enable}"
                              Foreground="{DynamicResource TextPrimary}"
                              FontFamily="{DynamicResource FontUI}" FontSize="12"
                              Margin="0,0,0,8"
                              Checked="LogEnabledCheck_Changed"
                              Unchecked="LogEnabledCheck_Changed"/>
                    <StackPanel Orientation="Horizontal">
                        <Button x:Name="OpenLogsBtn"
                                Content="{DynamicResource Str_Log_OpenFolder}"
                                Style="{StaticResource StudioToolButton}"
                                Margin="0,0,8,0"
                                Click="OpenLogsBtn_Click"/>
                        <Button x:Name="ClearLogsBtn"
                                Content="{DynamicResource Str_Log_Clear}"
                                Style="{StaticResource StudioToolButton}"
                                Click="ClearLogsBtn_Click"/>
                    </StackPanel>
```

- [ ] **Step 3: Sync the checkbox when the panel opens**

In `MainWindow.xaml.cs`, in `SettingsBtn_Click`, add this line immediately before `SettingsOverlay.Visibility = Visibility.Visible;` (line 290):

```csharp
            LogEnabledCheck.IsChecked = Scalpel.Services.Logger.Enabled;
```

- [ ] **Step 4: Add the three handlers**

In `MainWindow.xaml.cs`, add after `SettingsOverlayClose_Click` (after line 300):

```csharp
        private void LogEnabledCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool on = LogEnabledCheck.IsChecked == true;
            Scalpel.Services.Logger.SetEnabled(on);
            App.SetSetting("LoggingEnabled", on ? "1" : "0");
            Scalpel.Services.Logger.Info("Settings", "logging.toggle", on ? "enabled" : "disabled");
        }

        private void OpenLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Scalpel.Services.Logger.LogDirectory);
                System.Diagnostics.Process.Start("explorer.exe", Scalpel.Services.Logger.LogDirectory);
            }
            catch { }
        }

        private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this,
                "Delete all log files except the current session?",
                "Clear logs", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                Scalpel.Services.Logger.ClearLogs();
        }
```

`App.SetSetting` is `internal static` and reachable from `MainWindow` (same assembly). `MessageBox`/`MessageBoxButton`/`MessageBoxImage`/`MessageBoxResult` are in `System.Windows`, already in scope in this file.

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors. (A blank label on any control at runtime means a locale key was missed in Step 1.)

- [ ] **Step 6: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs Strings/en-US.xaml Strings/es.xaml Strings/zh-TW.xaml Strings/zh-CN.xaml Strings/bn.xaml Strings/tr-TR.xaml
git commit -m "feat(logging): add Settings diagnostics section (toggle/open/clear)"
```

---

### Task 4: Outcome logs — replace Debug.WriteLine and log major operations

**Files:**
- Modify: `MainWindow.xaml.cs` — replace the 7 `System.Diagnostics.Debug.WriteLine` catch lines; add `Logger.Info`/`Logger.Error` at major operations.

**Interfaces:**
- Consumes: `Logger.Info`, `Logger.Error` (Task 1).
- Produces: no new symbols (instrumentation only).

- [ ] **Step 1: Replace the 7 `Debug.WriteLine` catch lines with `Logger.Error`**

These exist at (approx) lines 1906, 2432, 2569, 2599, 2641, 7218, 7335 — each of the form
`catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"<Where>: {ex}"); }`.
Replace each with a `Logger.Error` carrying the same `<Where>` label as the event id. Examples (apply the same transform to all 7, keeping each file's own `<Where>` label):

```csharp
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "GetPageLinks", "GetPageLinks failed", ex); }
```
```csharp
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "GetPageFormFields", "GetPageFormFields failed", ex); }
```
```csharp
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "WriteFormValuesToDocument", "WriteFormValuesToDocument failed", ex); }
```
```csharp
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "GenerateTextFieldAppearance", "GenerateTextFieldAppearance failed", ex); }
```
```csharp
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "GenerateCheckBoxAppearance", "GenerateCheckBoxAppearance failed", ex); }
```
```csharp
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "BuildNamedDestMap", "BuildNamedDestMap failed", ex); }
```
```csharp
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "RewriteNamedDestLinks", "RewriteNamedDestLinks failed", ex); }
```

Use Grep to confirm none remain: `Grep pattern="Debug\.WriteLine" path="MainWindow.xaml.cs"` should return **0** matches afterward.

- [ ] **Step 2: Add success/failure outcome logs at major operations**

Locate each operation handler/method below by name (Grep for the identifier) and add the logging. For each operation, add a `Logger.Info("<cat>", "<event>.success", ...)` at the point where the operation has completed successfully, and a `Logger.Error("<cat>", "<event>.fail", ..., ex)` in its catch (where one exists). Keep messages short; put counts/paths in the `data` object.

| Operation (find by name) | Category | Success event | Suggested data |
|--------------------------|----------|---------------|----------------|
| Document open path (the method that finishes loading a PDF and sets the page count) | `File` | `open.success` | `new { path, pages }` |
| Save As (the save handler that writes the output PDF) | `File` | `save.success` | `new { path }` |
| Save Flattened | `File` | `flatten.success` | `new { path, pages }` |
| Merge PDFs | `File` | `merge.success` | `new { added }` |
| Extract / split pages | `File` | `extract.success` | `new { count }` |
| Apply/place signature (the method that commits a signature annotation) | `Sign` | `sign.success` | `new { page }` |
| Print (the method invoked when the user confirms print) | `Print` | `print.success` | `new { pages }` |

If a referenced local variable (e.g. `path`, `pages`) is not in scope at the log point, use a literal that is available (e.g. the current document path field) or drop that field from `data` — do not introduce new state to log it. Where an operation already has a `try/catch`, add the `*.fail` `Logger.Error` to the existing catch rather than creating a new one.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full test suite (no regressions)**

Run: `dotnet test`
Expected: PASS — all tests green, including `LoggerTests`.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat(logging): log operation outcomes; replace Debug.WriteLine with Logger.Error"
```

---

## Notes for the implementer

- **Why locked-synchronous writes (not a background thread):** QA log volume (clicks + operation outcomes) is low-frequency; an `AutoFlush` `StreamWriter` appends in microseconds, so the UI is not meaningfully blocked, and every line — including the last one before a crash — is guaranteed on disk. This satisfies the spec's "UI never blocks / crash-safe" intent more reliably than a worker thread whose final lines can be lost at shutdown. High-frequency paths (per-tile render) are intentionally **not** instrumented.
- **Date/time in app code is fine** — the `Date.now()`/`new Date()` restriction applies only to Workflow scripts, not to the C# app.
- If `dotnet build --no-restore` fails with `NETSDK1047` after a prior `dotnet publish`, re-run **with** restore (drop `--no-restore`).
