using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Scalpel.E2E;

public sealed class AppDriver : IDisposable
{
    // --- Win32 forced-foreground (works around the SetForegroundWindow lock) ---
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const int SW_RESTORE = 9;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP   = 0x0202;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_XVIRTUALSCREEN  = 76;
    private const int SM_YVIRTUALSCREEN  = 77;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct INPUT
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public uint type;
        [System.Runtime.InteropServices.FieldOffset(4)] public MOUSEINPUT mi;
        [System.Runtime.InteropServices.FieldOffset(4)] public KEYBDINPUT ki;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // The foreground + physical mouse/keyboard is a SINGLE machine-wide resource. Every
    // physical action (a real click on a ToggleButton/RadioButton/CheckBox, or canvas
    // mouse/keyboard input) acquires this gate, foregrounds its window, acts, and releases.
    // UIA Invoke clicks and all UIA/log/PdfPig reads never touch it, so they run fully
    // concurrently across instances. One permit = at most one physical action at a time.
    public static readonly SemaphoreSlim ForegroundGate = new(1, 1);

    private readonly UIA3Automation _automation;
    private Application _app;
    private readonly string _exePath;
    private readonly string? _logDir;

    /// <summary>
    /// The Windows Display Settings monitor number ("Identify" numbering) every launched/relaunched
    /// Scalpel window is moved onto right after it appears — keeps the app off the developer's
    /// primary display, so it isn't fighting foreground with whatever else is being worked on there
    /// (physical clicks require the app window to be foregrounded; losing it mid-run is a source of
    /// flakiness). Set from <c>--monitor</c> in Program.cs; defaults to 1. 0 or a monitor that can't
    /// be resolved leaves the window wherever WPF's CenterScreen startup location put it.
    /// </summary>
    public static int TargetMonitor { get; set; } = 1;

    private AppDriver(string exePath, Application app, UIA3Automation automation, string? logDir)
    {
        _exePath = exePath;
        _app = app;
        _automation = automation;
        _logDir = logDir;
    }

    /// <summary>
    /// Launch an isolated Scalpel instance. <paramref name="logDir"/> (optional) is passed to
    /// the app via the SCALPEL_LOG_DIR env var so this instance writes its session log to a
    /// private directory — required when several instances run in parallel (the default log
    /// file name is timestamp-to-second and would otherwise collide).
    /// </summary>
    public static AppDriver Launch(string exePath, string openWithPath, string? logDir = null)
    {
        var automation = new UIA3Automation();
        var psi = new ProcessStartInfo(exePath, $"\"{openWithPath}\"") { UseShellExecute = false };
        // The harness runs several instances side by side; without this the second launch
        // would forward its file to the first window and exit (single-instance mode).
        psi.Environment["SCALPEL_MULTI_INSTANCE"] = "1";
        if (!string.IsNullOrEmpty(logDir))
        {
            Directory.CreateDirectory(logDir);
            psi.Environment["SCALPEL_LOG_DIR"] = logDir;
        }
        var app = Application.Launch(psi);
        var driver = new AppDriver(exePath, app, automation, logDir) { _launchArg = openWithPath };
        driver.WaitForMainWindow();
        driver.PinMainWindowHandle();
        driver.FocusMainWindow();
        return driver;
    }

    /// <summary>
    /// Wait for and return this instance's session log path. When launched with a private
    /// <c>logDir</c> the lookup is unambiguous; otherwise it falls back to the latest log
    /// in the default directory. Polls up to ~5s for the file to appear.
    /// </summary>
    public string? ResolveLogPath()
    {
        string dir = _logDir ?? LogReader.DefaultLogDir();
        for (int i = 0; i < 20; i++)
        {
            var p = LogReader.FindLatestLog(dir);
            if (p != null) return p;
            System.Threading.Thread.Sleep(250);
        }
        return null;
    }

    /// <summary>
    /// Run a multi-step PHYSICAL sequence (canvas mouse + keyboard) under the global
    /// foreground gate: acquire the gate, foreground this window, run <paramref name="body"/>,
    /// release. Use this to wrap annotation place→type→commit and double-click-edit flows so
    /// they hold the cursor exclusively. Single physical clicks via <see cref="Click"/> are
    /// already gated internally.
    /// </summary>
    public void WithForeground(Action body)
    {
        ForegroundGate.Wait();
        try
        {
            FocusMainWindow();
            System.Threading.Thread.Sleep(60);
            body();
        }
        finally { ForegroundGate.Release(); }
    }

    /// <summary>Current screen bounds of the main window (Win32 GetWindowRect), for diagnostics.</summary>
    public RectangleDiag? CurrentWindowBounds
    {
        get
        {
            try
            {
                IntPtr hwnd = MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT r)) return null;
                return new RectangleDiag(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch { return null; }
        }
    }

    public readonly struct RectangleDiag
    {
        public RectangleDiag(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
        public int X { get; } public int Y { get; } public int Width { get; } public int Height { get; }
    }

    /// <summary>Lists every attached monitor for the <c>--monitors</c> diagnostic.</summary>
    public static IReadOnlyList<(int index, string deviceName, bool primary, System.Drawing.Rectangle bounds)> ListMonitors()
    {
        var result = new List<(int, string, bool, System.Drawing.Rectangle)>();
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
            result.Add((i + 1, screens[i].DeviceName, screens[i].Primary, screens[i].Bounds));
        return result;
    }

    /// <summary>
    /// Bring the Scalpel window to the foreground. Physical clicks (the only way to
    /// raise WPF Click on ToggleButton/RadioButton/CheckBox) land on the foreground
    /// window, so focus must be recovered after a dialog or Explorer window steals it.
    /// Best-effort: never throws.
    /// </summary>
    public void FocusMainWindow()
    {
        try
        {
            IntPtr hwnd = MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd == IntPtr.Zero) { try { MainWindow.SetForeground(); } catch { } return; }
            ForceForeground(hwnd);
        }
        catch { try { MainWindow.SetForeground(); } catch { } }
    }

    // Reliably bring hwnd to the foreground. Windows refuses SetForegroundWindow
    // from a background process unless we briefly attach our input queue to the
    // current foreground window's thread — the standard workaround.
    private static void ForceForeground(IntPtr hwnd)
    {
        try
        {
            ShowWindow(hwnd, SW_RESTORE);
            BringWindowToTop(hwnd);
            uint foreThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint thisThread = GetCurrentThreadId();
            if (foreThread != thisThread)
            {
                AttachThreadInput(thisThread, foreThread, true);
                SetForegroundWindow(hwnd);
                AttachThreadInput(thisThread, foreThread, false);
            }
            else
            {
                SetForegroundWindow(hwnd);
            }
        }
        catch { }
    }

    private void WaitForMainWindow()
    {
        // Retry up to ~15s for the window to appear and become responsive.
        for (int i = 0; i < 60; i++)
        {
            try
            {
                var w = _app.GetMainWindow(_automation, TimeSpan.FromMilliseconds(500));
                if (w != null) { PositionOnTargetMonitor(); return; }
            }
            catch { }
            System.Threading.Thread.Sleep(250);
        }
        throw new InvalidOperationException("Scalpel main window did not appear.");
    }

    // Moves the just-launched window (centered, same size) onto TargetMonitor. WPF's
    // WindowStartupLocation="CenterScreen" always centers on the PRIMARY monitor regardless of
    // where the process was started from, so this is the only way to land it elsewhere. Best-effort:
    // a monitor that can't be resolved (bad number, single-monitor machine) leaves the window as-is.
    private void PositionOnTargetMonitor()
    {
        if (TargetMonitor <= 0) return;
        try
        {
            IntPtr hwnd = MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd == IntPtr.Zero) return;
            var bounds = ResolveMonitorBounds(TargetMonitor);
            if (bounds == null) return;
            if (!GetWindowRect(hwnd, out RECT r)) return;
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            int x = bounds.Value.X + Math.Max(0, (bounds.Value.Width - w) / 2);
            int y = bounds.Value.Y + Math.Max(0, (bounds.Value.Height - h) / 2);
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
        }
        catch { }
    }

    /// <summary>The window height the settings baseline gives every launch: 1300 px, or the target
    /// monitor's working-area height less a margin when that is smaller.</summary>
    public static int BaselineWindowHeight()
    {
        int h = 1300;
        try
        {
            var b = ResolveMonitorBounds(TargetMonitor <= 0 ? 1 : TargetMonitor);
            var screen = b is null ? System.Windows.Forms.Screen.PrimaryScreen
                                   : System.Windows.Forms.Screen.FromRectangle(b.Value);
            h = Math.Min(h, screen.WorkingArea.Height - 40);
        }
        catch { }
        return Math.Max(700, h);
    }

    // Resolves a Windows Display Settings monitor number to its screen bounds. Screen.AllScreens
    // doesn't expose that "Identify" number directly, so this matches the trailing digits of each
    // screen's DeviceName ("\\.\DISPLAY1" -> 1) — the convention Display Settings numbering follows
    // on a standard single-adapter multi-monitor setup. Falls back to a 1-based index into
    // AllScreens (in enumeration order) if no DeviceName matches, so an unusual naming scheme still
    // resolves to *some* monitor rather than silently doing nothing.
    private static System.Drawing.Rectangle? ResolveMonitorBounds(int displayNumber)
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            foreach (var s in screens)
            {
                string name = s.DeviceName ?? "";
                int i = name.Length;
                while (i > 0 && char.IsDigit(name[i - 1])) i--;
                if (i < name.Length && int.TryParse(name.Substring(i), out int n) && n == displayNumber)
                    return s.Bounds;
            }
            if (displayNumber >= 1 && displayNumber <= screens.Length)
                return screens[displayNumber - 1].Bounds;
        }
        catch { }
        return null;
    }

    private Window? _mainWindowCache;

    public Window MainWindow
    {
        get
        {
            // Reuse the cached window while it is still valid — MainWindow is read on nearly every
            // operation, and re-resolving (GetAllTopLevelWindows + ClassName probes) each time is
            // far too slow. A cheap handle read tells us the cached element is still live.
            if (_mainWindowCache != null)
            {
                try
                {
                    if (_mainWindowCache.Properties.NativeWindowHandle.ValueOrDefault != IntPtr.Zero)
                        return _mainWindowCache;
                }
                catch { }
            }
            return _mainWindowCache = ResolveMainWindow();
        }
    }

    /// <summary>
    /// Resolves Scalpel's own application window.
    ///
    /// <para>WPF can keep a stray top-level Popup HWND (a tooltip/adorner) alongside the real
    /// window, and FlaUI.GetMainWindow sometimes returns that Popup - which has none of our
    /// controls, making every Find() fail.</para>
    ///
    /// <para>Selecting "the first top-level that is not a Popup" is NOT enough, and used to be a
    /// real defect: a native Open/Save dialog has ClassName "#32770", which passes that test, so
    /// while a file dialog was open the driver could cache the DIALOG as the main window. Every
    /// Find() then searched the dialog, the coverage cross-check reported the dialog's own chrome
    /// (UpButton, DropDown, GUID-named parts) as un-exercised Scalpel controls, and - worst -
    /// DismissModals would treat the dialog as the window to protect and close the real one.</para>
    ///
    /// <para>So the process's own main-window handle, captured at launch before any dialog can
    /// exist, is the authority. The class-name heuristic is only a fallback.</para>
    /// </summary>
    private Window ResolveMainWindow()
    {
        try
        {
            var tops = _app.GetAllTopLevelWindows(_automation);

            if (_pinnedMainHandle != IntPtr.Zero)
            {
                var pinned = tops.FirstOrDefault(w =>
                {
                    try { return w.Properties.NativeWindowHandle.ValueOrDefault == _pinnedMainHandle; }
                    catch { return false; }
                });
                if (pinned != null) return pinned;
            }

            // Fallback: a real WPF application window, never a Popup and never a native dialog.
            var real = tops.FirstOrDefault(w =>
            {
                try
                {
                    string cls = w.ClassName ?? "";
                    return cls != "Popup" && cls != "#32770";
                }
                catch { return false; }
            });
            if (real != null) return real;
        }
        catch { }
        return _app.GetMainWindow(_automation, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The process's main window handle, pinned once while no dialog can be open. Everything that
    /// needs to tell "the app" apart from "a window the app put up" keys off this.
    /// </summary>
    private IntPtr _pinnedMainHandle;

    private void PinMainWindowHandle()
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(_app.ProcessId);
            for (int i = 0; i < 40 && proc.MainWindowHandle == IntPtr.Zero; i++)
            {
                System.Threading.Thread.Sleep(100);
                proc.Refresh();
            }
            if (proc.MainWindowHandle != IntPtr.Zero) _pinnedMainHandle = proc.MainWindowHandle;
        }
        catch { }
    }

    public bool IsAlive
    {
        get
        {
            try { return !_app.HasExited && MainWindow != null; }
            catch { return false; }
        }
    }

    /// <summary>The Scalpel.exe this driver launches (a scenario that spawns a second process
    /// - the single-instance forwarding check - needs the same binary).</summary>
    public string ExePath => _exePath;

    /// <summary>PID of the current app process, or -1 if it cannot be read. Temp working files
    /// carry it in their name (<c>scalpel_p{pid}_*.pdf</c>).</summary>
    public int ProcessId
    {
        get { try { return _app.ProcessId; } catch { return -1; } }
    }

    /// <summary>
    /// Relaunch the app. <paramref name="openWithPath"/> null starts it with NO file argument, so
    /// the app runs its startup tab restore (the <c>OpenTabs</c> setting) instead of opening a
    /// file. <paramref name="multiInstance"/> false leaves <c>SCALPEL_MULTI_INSTANCE</c> unset, so
    /// the new process becomes the single-instance primary and serves forwarded launches - only
    /// the forwarding scenario wants that; every other launch keeps multi-instance on. The settings
    /// baseline is applied either way (it does not touch <c>OpenTabs</c>/<c>LastFile</c>, so the
    /// tabs persisted by the graceful close below survive into the relaunch).
    /// </summary>
    public void Relaunch(string? openWithPath, bool multiInstance = true)
    {
        // Fully tear down the old instance BEFORE launching the new one. Closing without waiting
        // raced the new launch: while the heavy first instance was still shutting down, the new
        // process's command-line file-open could be dropped, leaving the relaunched app with no
        // document loaded (no page to place annotations on). Wait for the old PID to actually exit.
        CloseApp();
        _launchNo++;
        _launchArg = openWithPath;
        _mainWindowCache = null; // the relaunched app is a new window — drop the stale cache
        _lastMainHandle = IntPtr.Zero;
        _pinnedMainHandle = IntPtr.Zero;
        // Re-apply the settings baseline so the relaunched app opens in a known state. Prior suites
        // persist theme/locale/view-mode by clicking those controls; without this the relaunch
        // would inherit, e.g., an RTL locale that breaks canvas annotation placement.
        AppSettingsGuard.WriteBaseline();
        string args = openWithPath is null ? "" : $"\"{openWithPath}\"";
        var psi = new ProcessStartInfo(_exePath, args) { UseShellExecute = false };
        if (multiInstance) psi.Environment["SCALPEL_MULTI_INSTANCE"] = "1"; // see Launch()
        else psi.Environment.Remove("SCALPEL_MULTI_INSTANCE");
        // Preserve this instance's private log dir across relaunches, otherwise the new
        // session would log to the shared default dir and collide with sibling instances.
        if (!string.IsNullOrEmpty(_logDir)) psi.Environment["SCALPEL_LOG_DIR"] = _logDir;
        _app = Application.Launch(psi);
        WaitForMainWindow();
        // Pin the NEW process's window. (This used to run before the launch, against the process
        // that had just exited, so every relaunched instance ran unpinned on the class-name
        // fallback - which can pick a dialog while one is up.)
        PinMainWindowHandle();
        try { Relaunched?.Invoke(); } catch { }
    }

    /// <summary>Raised after every <see cref="Relaunch"/>. The new process writes a NEW session log,
    /// so anything reading the old one (ActionRunner's LogReader) must rebind or it goes blind: after
    /// one crash-recovery relaunch every later click read as "not logged".</summary>
    public event Action? Relaunched;

    public AutomationElement? Find(string automationId)
    {
        try
        {
            var inWindow = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (inWindow != null) return inWindow;
        }
        catch { }

        // A WPF ContextMenu renders in its own popup HWND, so its items are NOT descendants of
        // the main window - the Tools menu items are invisible to a main-window-only search.
        // Fall back to the other top-level windows this app owns.
        return FindInPopups(automationId);
    }

    /// <summary>
    /// Looks for a control in the app's popup / dialog windows (anything top-level that is not
    /// the main window). This is what makes menu items reachable.
    /// </summary>
    private AutomationElement? FindInPopups(string automationId)
    {
        try
        {
            IntPtr mainHandle = GetMainWindowHandle();
            foreach (var w in _app.GetAllTopLevelWindows(_automation))
            {
                try
                {
                    if (w.Properties.NativeWindowHandle.ValueOrDefault == mainHandle) continue;
                    var hit = w.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
                    if (hit != null) return hit;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    public bool Click(string automationId)
    {
        var el = Find(automationId);
        if (el == null) return false;
        // Bring the target into view first: controls at the bottom of the tall
        // Settings overlay are clipped offscreen, where a physical click misses.
        try { if (el.Patterns.ScrollItem.IsSupported) el.Patterns.ScrollItem.Pattern.ScrollIntoView(); } catch { }
        try
        {
            // Drive each control through the mechanism that actually runs its real handler,
            // preferring the ones that work on a BACKGROUND window (no shared cursor, no
            // foreground race) so the run is deterministic in an automation host:
            //
            //   * Plain Button  → Invoke: raises ButtonBase.Click (the app logs it). Background-safe.
            //   * RadioButton   → SelectionItem.Select: raises Checked, which is what every radio's
            //                     handler (ModeTab_Checked, Accent*/Lang*/Theme*_Checked) listens to.
            //                     The app now also logs Checked, so this is observable. Background-safe.
            //                     Re-selecting an already-selected radio fires nothing — that is fine;
            //                     the verifier falls back to the selected STATE.
            //   * ToggleButton  → physical click. The view-mode toggles act on their *Click* handler
            //                     (ViewSingle_Click …), and TogglePattern.Toggle() raises only
            //                     Checked/Unchecked, NOT Click — so it would log without switching the
            //                     view. A physical click is the only thing that runs their handler;
            //                     it is serialized through the foreground gate and retried once.
            if (el.Patterns.Invoke.IsSupported)
            {
                el.Patterns.Invoke.Pattern.Invoke();
            }
            else if (el.Patterns.SelectionItem.IsSupported)
            {
                el.Patterns.SelectionItem.Pattern.Select();
            }
            else if (el.Patterns.Toggle.IsSupported)
            {
                //   * Toggle-only control → TogglePattern.Toggle (background-safe, no foreground race).
                //     The view-mode radios (ViewSingle/Continuous/TwoPage/GridBtn) expose TogglePattern
                //     (not SelectionItem/Invoke) and act on their *Checked* handler (ViewContinuous_Checked
                //     …), which Toggle() raises — so it runs the real handler AND logs the click, unlike a
                //     physical click which needs the foreground. Only drive Off→On (selecting the mode); a
                //     control already On is in the desired state and the verifier falls back to that state.
                if (el.Patterns.Toggle.Pattern.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.Off)
                    el.Patterns.Toggle.Pattern.Toggle();
            }
            else
            {
                ForegroundGate.Wait();
                try
                {
                    FocusMainWindow();
                    System.Threading.Thread.Sleep(60);
                    el.Click();
                    // Self-heal a missed physical click: if a toggle didn't register, re-foreground
                    // and click once more within the same gate hold.
                    if (el.Patterns.Toggle.IsSupported &&
                        el.Patterns.Toggle.Pattern.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.Off)
                    {
                        System.Threading.Thread.Sleep(90);
                        FocusMainWindow();
                        System.Threading.Thread.Sleep(60);
                        el.Click();
                    }
                }
                finally { ForegroundGate.Release(); }
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// True if the control identified by <paramref name="automationId"/> is currently
    /// selected/checked (RadioButton via SelectionItem, ToggleButton/CheckBox via Toggle).
    /// Returns false when the control is absent or its state can't be read.
    /// </summary>
    public bool IsSelected(string automationId)
    {
        var el = Find(automationId);
        if (el == null) return false;
        try
        {
            if (el.Patterns.SelectionItem.IsSupported)
                return el.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault;
            if (el.Patterns.Toggle.IsSupported)
                return el.Patterns.Toggle.Pattern.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On;
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// True if the control exists in the tree but is currently disabled (IsEnabled == false).
    /// Returns false when the control is absent, so the caller's normal "not found" path still
    /// fires. Used to skip controls that are legitimately non-interactable in the current state
    /// (e.g. the accent radios while High Contrast is the active theme).
    /// </summary>
    public bool IsDisabled(string automationId)
    {
        var el = Find(automationId);
        if (el == null) return false;
        try { return !el.Properties.IsEnabled.ValueOrDefault; }
        catch { return false; }
    }

    public void EnsureSurface(Surface surface)
    {
        switch (surface)
        {
            case Surface.ViewMode:  Click("ModeViewTab");  break;
            case Surface.EditMode:  Click("ModeEditTab");  break;
            case Surface.PagesMode: Click("ModePagesTab"); break;
            case Surface.SignMode:  Click("ModeSignTab");  break;
            case Surface.SettingsOverlay:
                // The Settings overlay is opened by the SettingsBtn control, which the
                // catalog orders immediately before the settings group; it then stays
                // open across the group (theme/lang/log clicks don't close it). Open it
                // only if it is somehow not already open. We do NOT toggle the overlay
                // for other surfaces — UIA visibility lag made that race and reopen it.
                if (!IsSettingsOverlayOpen()) Click("SettingsBtn");
                // The overlay's Theme/Accent/Language groups are collapsible accordion
                // sections that open collapsed by default, so their radios are absent
                // from the UIA tree until the section is expanded. Expand all three via
                // the Toggle pattern (programmatic — no foreground/cursor needed) so the
                // catalogued radios are reachable.
                ExpandSettingsSections();
                break;
            case Surface.ToolsMenu:
                // The Tools menu is a ContextMenu that FileMenu_Click opens on the button.
                // Clicking an item closes it again, so it is reopened before each item rather
                // than left open across the group.
                Click("ToolsMenuBtn");
                System.Threading.Thread.Sleep(300);
                break;
            case Surface.AlwaysVisible: default: break;
        }
        System.Threading.Thread.Sleep(150);
    }

    /// <summary>
    /// Return the app to a known base state between suites that share one session:
    /// close the Settings overlay if open and switch to View mode. A settle lets
    /// the overlay's collapse propagate to UIA before the next suite reads state.
    /// </summary>
    public void ResetToBaseState()
    {
        FocusMainWindow();
        if (IsSettingsOverlayOpen())
        {
            Click("SettingsBtn");
            System.Threading.Thread.Sleep(250);
        }
        Click("ModeViewTab");
        System.Threading.Thread.Sleep(200);
    }

    /// <summary>
    /// True when the Settings overlay is open, detected by the Theme section header
    /// toggle — a control that renders inside the overlay and, unlike the theme
    /// radios, is present whether or not its accordion section is expanded. (The
    /// radios collapse out of the tree when their section is closed, which is the
    /// default, so they are not a reliable open-signal.) When the overlay is
    /// collapsed, the header toggle is absent from the UIA tree.
    /// </summary>
    public bool IsSettingsOverlayOpen()
    {
        var el = Find("ThemeHeaderToggle");
        if (el == null) return false;
        try { return !el.Properties.IsOffscreen.ValueOrDefault; }
        catch { return false; }
    }

    /// <summary>
    /// Expand the three collapsible Settings accordion sections (Theme / Accent /
    /// Language) so their radios are present in the UIA tree. Uses the UIA Toggle
    /// pattern, which flips IsChecked programmatically without a physical click — so
    /// it needs neither the foreground nor the shared cursor and never races other
    /// instances. Idempotent: a section already expanded is left alone.
    /// </summary>
    public void ExpandSettingsSections()
    {
        foreach (var id in new[] { "ThemeHeaderToggle", "AccentHeaderToggle", "LangHeaderToggle" })
        {
            var el = Find(id);
            if (el == null) continue;
            try
            {
                if (el.Patterns.Toggle.IsSupported &&
                    el.Patterns.Toggle.Pattern.ToggleState.Value != FlaUI.Core.Definitions.ToggleState.On)
                    el.Patterns.Toggle.Pattern.Toggle();
            }
            catch { }
        }
        System.Threading.Thread.Sleep(80); // let the visibility bindings propagate to UIA
    }

    /// <summary>
    /// Resize the main window via the UIA TransformPattern.
    /// FlaUI 4.x: Window.Patterns.Transform.Pattern.Resize(double width, double height).
    /// Note: <c>Window.SetTransform</c> does not exist in FlaUI 4.0.0 — use the pattern directly.
    /// </summary>
    public void Resize(int width, int height)
    {
        try
        {
            var w = MainWindow;
            if (w.Patterns.Transform.IsSupported)
                w.Patterns.Transform.Pattern.Resize(width, height);
        }
        catch { }
    }

    // Last successfully-read main-window handle. The HWND is stable for a window's lifetime,
    // so caching it lets DismissModals identify the main window even when a transient UIA read
    // returns 0 (which happens while the visual tree is rebuilding during a mode switch).
    private IntPtr _lastMainHandle;

    private IntPtr GetMainWindowHandle()
    {
        // The pinned handle is authoritative: deriving this from MainWindow is circular, and if
        // that cache ever held a dialog then DismissModals would protect the dialog and close the
        // real window instead.
        if (_pinnedMainHandle != IntPtr.Zero) return _pinnedMainHandle;
        try
        {
            var h = MainWindow?.Properties.NativeWindowHandle.ValueOrDefault ?? IntPtr.Zero;
            if (h != IntPtr.Zero) _lastMainHandle = h;
        }
        catch { }
        return _lastMainHandle;
    }

    /// <summary>
    /// Close any unexpected top-level windows that are not the main window — modal dialogs (file
    /// pickers, message boxes) AND stray non-modal Popup HWNDs that WPF can leave behind (a stale
    /// tooltip/adorner popup otherwise confuses window resolution). The critical safety rule: if
    /// the main-window handle can't be determined, do NOTHING — leaving a stray window is far
    /// better than killing the run by closing the main window (the bug this guard fixes).
    /// </summary>
    public void DismissModals()
    {
        // Several passes: a native Windows file dialog often ignores the first Close (it is still
        // finishing its own initialization), and closing one dialog can reveal another behind it.
        for (int pass = 0; pass < 4; pass++)
        {
            if (!DismissModalsOnce()) return;   // nothing left to close
            System.Threading.Thread.Sleep(200);
        }
    }

    /// <summary>True when any window other than the main window is still up.</summary>
    public bool HasOpenModal()
    {
        try
        {
            IntPtr mainHandle = GetMainWindowHandle();
            if (mainHandle == IntPtr.Zero) return false;
            return DialogWindows(mainHandle).Count > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Every window of the app except the main one: the unowned top-level windows (WPF popups and
    /// menus) AND the dialogs the main window owns.
    /// <para>UIA lists an owned window as a child of its owner, not of the desktop, so
    /// <c>GetAllTopLevelWindows</c> alone never sees a dialog shown with an owner - and that is
    /// every dialog Scalpel shows (ScalpelDialog, the tool forms, the native Open/Save dialogs,
    /// all shown with <c>Owner = this</c> / <c>ShowDialog(this)</c>). Looking only there made
    /// AnswerDialog, DriveOpenDialog/DriveSaveDialog, HasOpenModal and DismissModals blind to
    /// them: the dialog stayed up, which disables the main window, and the next relaunch's close
    /// was refused.</para>
    /// </summary>
    private List<Window> DialogWindows(IntPtr mainHandle)
    {
        var found = new List<Window>();
        var seen = new HashSet<IntPtr>();
        void Add(Window w)
        {
            try
            {
                IntPtr h = w.Properties.NativeWindowHandle.ValueOrDefault;
                if (h == mainHandle || !seen.Add(h)) return;
                found.Add(w);
                // A dialog can own one of its own (e.g. a native Save dialog's overwrite prompt).
                foreach (var inner in w.ModalWindows) Add(inner);
            }
            catch { }
        }
        try { foreach (var w in _app.GetAllTopLevelWindows(_automation)) Add(w); } catch { }
        try { foreach (var w in MainWindow.ModalWindows) Add(w); } catch { }
        return found;
    }

    /// <summary>
    /// Closes every top-level window that is not the main window. Returns true if any were found.
    /// <para>Escalates: Close(), then the dialog's own Cancel button, then Escape. A native
    /// Open/Save dialog frequently survives Close() but always answers Cancel - and leaving one
    /// open puts its chrome (UpButton, DropDown, SearchBoxSearchButton, GUID-named parts) into the
    /// automation tree, where the coverage cross-check reports them as un-exercised Scalpel
    /// controls.</para>
    /// </summary>
    private bool DismissModalsOnce()
    {
        bool found = false;
        try
        {
            IntPtr mainHandle = GetMainWindowHandle();
            if (mainHandle == IntPtr.Zero) return false; // can't identify main → never risk closing it

            foreach (var w in DialogWindows(mainHandle))
            {
                try
                {
                    found = true;
                    IntPtr h = IntPtr.Zero;
                    try { h = w.Properties.NativeWindowHandle.ValueOrDefault; } catch { }

                    try { w.AsWindow()?.Close(); } catch { }

                    // Still there? Ask it to cancel the way a user would.
                    if (StillOpen(h))
                    {
                        try
                        {
                            var cancel = w.FindFirstDescendant(cf => cf.ByName("Cancel"))?.AsButton();
                            if (cancel != null && cancel.Patterns.Invoke.IsSupported)
                                cancel.Patterns.Invoke.Pattern.Invoke();
                        }
                        catch { }
                    }

                    // Last resort: a physical Escape. It goes to whatever window has the keyboard,
                    // and Escape in Scalpel's MAIN window closes the app (KeyboardShortcuts: with the
                    // Select tool active a second Esc exits). Close()/Cancel usually take effect a
                    // moment later, so pressing blind landed the key on the main window once the
                    // dialog was gone and killed the run ("app crashed" at the first Tools menu
                    // item). Only press it while this very window is still up and in front.
                    if (StillOpen(h))
                    {
                        try
                        {
                            w.Focus();
                            System.Threading.Thread.Sleep(60);
                            // A dialog takes the foreground; a WPF popup (menu) never does, it owns
                            // the keyboard while the main window stays foreground.
                            bool popup = (w.ClassName ?? "") == "Popup";
                            if (IsWindow(h) && IsWindowVisible(h) && (popup || GetForegroundWindow() == h))
                                FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return found;
    }

    /// <summary>True while <paramref name="h"/> is still a visible window, after giving a close that
    /// was just requested up to ~400 ms to take effect (WPF closes asynchronously).</summary>
    private static bool StillOpen(IntPtr h)
    {
        if (h == IntPtr.Zero) return false;
        for (int i = 0; i < 8; i++)
        {
            if (!IsWindow(h) || !IsWindowVisible(h)) return false;
            System.Threading.Thread.Sleep(50);
        }
        return IsWindow(h) && IsWindowVisible(h);
    }

    public bool DriveOpenDialog(string path) => DriveFileDialog(path, confirmButtonName: "Open");
    public bool DriveSaveDialog(string path) => DriveFileDialog(path, confirmButtonName: "Save");

    /// <summary>
    /// Drive a native Windows Open/Save file dialog:
    ///   1. Wait for a top-level window that is modal or whose name contains "PDF".
    ///   2. Find the filename Edit control and type the path via TextBox.Enter().
    ///   3. Click the confirm button (Open / Save) via Button.Invoke().
    /// FlaUI 4.x: AutomationElement.AsTextBox().Enter(text) and Button.Invoke() are both valid.
    /// Window.IsModal is a direct bool property in FlaUI 4.0.0.
    /// </summary>
    private bool DriveFileDialog(string path, string confirmButtonName)
    {
        try
        {
            for (int i = 0; i < 40; i++)
            {
                IntPtr mainHandle = GetMainWindowHandle();
                if (mainHandle == IntPtr.Zero) { System.Threading.Thread.Sleep(250); continue; }
                var dialog = DialogWindows(mainHandle)
                    .FirstOrDefault(w => w.IsModal || (w.Name?.Contains("PDF") ?? false));
                if (dialog != null)
                {
                    // The file-name box by its common-dialog control id: 1148 in an Open dialog,
                    // 1001 in a Save dialog. NOT "the first Edit": in the Explorer-style dialog that
                    // is a file row's Name cell, so the path went nowhere (the Open dialog stayed up
                    // with an empty name) or was typed into the file list, whose type-ahead picked
                    // an unrelated file ("corrupted.pdf already exists - replace it?").
                    AutomationElement? edit = null;
                    for (int j = 0; j < 20 && edit == null; j++)
                    {
                        edit = dialog.FindFirstDescendant(cf => cf.ByAutomationId("1148").And(cf.ByControlType(ControlType.Edit)))
                            ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("1001").And(cf.ByControlType(ControlType.Edit)))
                            ?? dialog.FindFirstDescendant(cf => cf.ByName("File name:").And(cf.ByControlType(ControlType.Edit)));
                        if (edit == null) System.Threading.Thread.Sleep(150);
                    }
                    if (edit == null) return false;
                    if (edit.Patterns.Value.IsSupported) edit.Patterns.Value.Pattern.SetValue(path);
                    else edit.AsTextBox().Enter(path);
                    // The confirm button is IDOK (control id 1): a Button in the Save dialog, a
                    // SplitButton in the Open dialog. Matching by name alone hits the Open split
                    // button's drop-down arrow (a Button also named "Open"), and id 1 alone can be a
                    // file row (ListItem).
                    var btn = dialog.FindFirstDescendant(cf => cf.ByAutomationId("1")
                                  .And(cf.ByControlType(ControlType.Button).Or(cf.ByControlType(ControlType.SplitButton))))
                              ?? dialog.FindFirstDescendant(cf => cf.ByName(confirmButtonName).And(cf.ByControlType(ControlType.Button)));
                    if (btn == null || !btn.Patterns.Invoke.IsSupported) return false;
                    btn.Patterns.Invoke.Pattern.Invoke();
                    return true;
                }
                System.Threading.Thread.Sleep(250);
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Send a left-click via SendInput using absolute normalized virtual-screen coordinates.
    /// screenX/Y are in physical screen pixels (as reported by UIA/GetWindowRect).
    /// Normalizes using the virtual screen dimensions from GetSystemMetrics.
    /// </summary>
    private void SendInputClick(int screenX, int screenY)
    {
        int vsLeft  = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vsTop   = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vsWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vsHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // Normalize to [0,65535] range for MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
        int nx = (int)(((long)(screenX - vsLeft) * 65535 + vsWidth  - 1) / vsWidth);
        int ny = (int)(((long)(screenY - vsTop)  * 65535 + vsHeight - 1) / vsHeight);
        Console.WriteLine($"[AppDriver.SendInputClick] screen=({screenX},{screenY}) vs=({vsLeft},{vsTop},{vsWidth}x{vsHeight}) norm=({nx},{ny})");

        // First move cursor via SetCursorPos (bypasses all DPI normalization concerns).
        bool moved = SetCursorPos(screenX, screenY);
        System.Threading.Thread.Sleep(50);
        POINT actualPos;
        GetCursorPos(out actualPos);
        Console.WriteLine($"[AppDriver.SendInputClick] SetCursorPos({screenX},{screenY}) success={moved} actualCursor=({actualPos.X},{actualPos.Y})");

        // Then click at current cursor position (no MOVE flag).
        var inputs = new INPUT[]
        {
            new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0002 } }, // MOUSEEVENTF_LEFTDOWN
            new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0004 } }  // MOUSEEVENTF_LEFTUP
        };
        SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
        System.Threading.Thread.Sleep(150);
    }

    /// <summary>
    /// Click at fractional coordinates within the main window's bounding rectangle.
    /// fracX/fracY are in [0,1]: (0,0) = top-left, (1,1) = bottom-right.
    /// Uses Win32 GetWindowRect for accurate physical pixel bounds (avoids UIA DPI-scaling issues).
    /// </summary>
    public void ClickPoint(double fracX, double fracY)
    {
        FocusMainWindow();
        System.Threading.Thread.Sleep(150);

        // Get window rect via Win32 for reliable physical-pixel coordinates.
        int wx = 0, wy = 0, ww = 800, wh = 600;
        try
        {
            IntPtr hwnd = MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT r))
            {
                wx = r.Left; wy = r.Top;
                ww = r.Right - r.Left; wh = r.Bottom - r.Top;
            }
        }
        catch { }

        int x = wx + (int)(ww * fracX);
        int y = wy + (int)(wh * fracY);
        Console.WriteLine($"[AppDriver.ClickPoint] window=({wx},{wy},{ww}x{wh}) frac=({fracX},{fracY}) click=({x},{y})");
        var pt = new System.Drawing.Point(x, y);
        Mouse.MoveTo(pt);
        System.Threading.Thread.Sleep(150);
        Mouse.Click(MouseButton.Left);
        System.Threading.Thread.Sleep(150);
    }

    /// <summary>
    /// Click in the centre of the page canvas area to place an annotation.
    /// Finds the PagePreviewPanel ScrollViewer via UIA, resolves the PageImage bounds
    /// to get accurate screen coordinates, then uses UIA FromPoint + element.Click()
    /// which is the same physical-click path used for RadioButton/Button elements.
    /// Falls back to a FlaUI Mouse click on the raw screen coords if UIA lookup fails.
    /// </summary>
    public void ClickCanvas()
    {
        FocusMainWindow();
        System.Threading.Thread.Sleep(200);

        try
        {
            var scrollEl = MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("PagePreviewPanel").Or(cf.ByName("PagePreviewPanel")));

            if (scrollEl != null)
            {
                var pageImage = scrollEl.FindFirstDescendant(
                    cf => cf.ByAutomationId("PageImage").Or(cf.ByName("PageImage")));
                if (pageImage == null)
                    pageImage = scrollEl.FindFirstDescendant(
                        cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Image));

                var r = pageImage?.BoundingRectangle ?? scrollEl.BoundingRectangle;
                // Aim inside the part of the page that is actually on screen. A page taller than the
                // viewport (a small window, e.g. one the monkey suite resized) extends below it, and
                // 45% down the WHOLE page then lies on the status bar or off the window entirely.
                var visible = System.Drawing.Rectangle.Intersect(r, scrollEl.BoundingRectangle);
                if (visible.Width > 20 && visible.Height > 20) r = visible;
                int screenX = (int)(r.X + r.Width  * 0.45);
                int screenY = (int)(r.Y + r.Height * 0.45);

                // Use UIA FromPoint + element.Click() — the same physical-click mechanism
                // that reliably works for toolbar buttons in this WPF layered window.
                try
                {
                    var elemAtPoint = _automation.FromPoint(new System.Drawing.Point(screenX, screenY));
                    if (elemAtPoint != null)
                    {
                        elemAtPoint.Click();
                        System.Threading.Thread.Sleep(150);
                        return;
                    }
                }
                catch { }

                Mouse.MoveTo(screenX, screenY);
                System.Threading.Thread.Sleep(100);
                Mouse.Click(MouseButton.Left);
                System.Threading.Thread.Sleep(150);
                return;
            }
        }
        catch { }

        ClickPoint(0.55, 0.50);
    }

    /// <summary>
    /// Double-click in the centre of the page canvas to trigger "edit existing text".
    /// In EditTool.Select mode the app routes double-clicks to EditTextAtPosition,
    /// which opens an inline TextBox pre-filled with the nearest PDF word line.
    /// Uses the same coordinate resolution as ClickCanvas() (PageImage at 45%/45%).
    /// </summary>
    public void DoubleClickCanvas()
    {
        FocusMainWindow();
        System.Threading.Thread.Sleep(200);

        try
        {
            var scrollEl = MainWindow.FindFirstDescendant(
                cf => cf.ByAutomationId("PagePreviewPanel").Or(cf.ByName("PagePreviewPanel")));

            if (scrollEl != null)
            {
                var pageImage = scrollEl.FindFirstDescendant(
                    cf => cf.ByAutomationId("PageImage").Or(cf.ByName("PageImage")));
                if (pageImage == null)
                    pageImage = scrollEl.FindFirstDescendant(
                        cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Image));

                var r = pageImage?.BoundingRectangle ?? scrollEl.BoundingRectangle;
                int screenX = (int)(r.X + r.Width  * 0.45);
                int screenY = (int)(r.Y + r.Height * 0.45);

                Console.WriteLine($"[AppDriver.DoubleClickCanvas] screen=({screenX},{screenY})");

                // Move cursor first, then send two rapid clicks (double-click).
                Mouse.MoveTo(screenX, screenY);
                System.Threading.Thread.Sleep(80);
                Mouse.DoubleClick(MouseButton.Left);
                System.Threading.Thread.Sleep(150);
                return;
            }
        }
        catch { }

        // Fallback: fractional click via ClickPoint (single-monitor path).
        FocusMainWindow();
        System.Threading.Thread.Sleep(150);
        int wx = 0, wy = 0, ww = 800, wh = 600;
        try
        {
            IntPtr hwnd = MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT rect))
            {
                wx = rect.Left; wy = rect.Top;
                ww = rect.Right - rect.Left; wh = rect.Bottom - rect.Top;
            }
        }
        catch { }
        int fx = wx + (int)(ww * 0.55);
        int fy = wy + (int)(wh * 0.50);
        Mouse.MoveTo(fx, fy);
        System.Threading.Thread.Sleep(100);
        Mouse.DoubleClick(MouseButton.Left);
        System.Threading.Thread.Sleep(150);
    }

    /// <summary>
    /// Find the first unnamed Edit (TextBox) control in the main window's UIA tree.
    /// The annotation TextBox created by PlaceTextBox has no AutomationId (unlike the
    /// PageJumpBox and PART_EditableTextBox controls which are named). Returns null if
    /// no unnamed Edit control is found (e.g. if the canvas click did not place a TextBox).
    /// </summary>
    public FlaUI.Core.AutomationElements.AutomationElement? FindAnyTextBox()
    {
        try
        {
            var all = MainWindow.FindAllDescendants(
                cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
            return all.FirstOrDefault(e => string.IsNullOrEmpty(e.AutomationId));
        }
        catch { return null; }
    }

    /// <summary>
    /// Place a new annotation text box on the canvas and set its text via the UIA Value pattern,
    /// robust to a placement click that misses (a single physical canvas click can land nowhere
    /// under foreground contention, most often right after a relaunch). Clicks the canvas, waits
    /// up to ~1.8s for the text box to appear and sets it; if it never appears, clicks once more.
    /// Falls back to physical typing as a last resort. Returns true if the text was set.
    /// Requires the Text tool to already be active.
    /// </summary>
    public bool PlaceAndSetAnnotationText(string text)
    {
        ClickCanvas();
        for (int i = 0; i < 6; i++)
        {
            System.Threading.Thread.Sleep(300);
            if (SetActiveAnnotationText(text)) return true;
        }
        // The box never appeared — one more placement attempt.
        ClickCanvas();
        System.Threading.Thread.Sleep(600);
        if (SetActiveAnnotationText(text)) return true;
        // Last resort: physical typing into whatever text box exists.
        var tb = FindAnyTextBox();
        if (tb != null)
        {
            try { tb.Click(); } catch { }
            System.Threading.Thread.Sleep(150);
            TypeText(text);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Set the text of the active (unnamed) annotation TextBox via the UIA Value pattern. Unlike
    /// physical Keyboard typing this is foreground-independent and reliably lands Unicode
    /// (Hebrew / Arabic / Cyrillic) — the app's commit path reads TextBox.Text, which SetValue
    /// updates directly. Returns false if no annotation TextBox is present or it lacks the Value
    /// pattern, so the caller can fall back to physical typing.
    /// </summary>
    public bool SetActiveAnnotationText(string text)
    {
        var el = FindAnyTextBox();
        if (el == null) return false;
        try
        {
            if (el.Patterns.Value.IsSupported)
            {
                el.Patterns.Value.Pattern.SetValue(text);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Type a string into the focused element. FlaUI's Keyboard.Type handles Unicode,
    /// including Hebrew characters, via SendInput with Unicode scan codes.
    /// Does NOT call FocusMainWindow() to avoid disturbing keyboard focus within the app
    /// (e.g., a text box that was just placed by a canvas click).
    /// </summary>
    public void TypeText(string s)
    {
        Keyboard.Type(s);
        System.Threading.Thread.Sleep(150);
    }

    /// <summary>
    /// Press a virtual key (e.g. VirtualKeyShort.RETURN, VirtualKeyShort.ESCAPE).
    /// </summary>
    public void PressKey(VirtualKeyShort key)
    {
        Keyboard.Press(key);
        System.Threading.Thread.Sleep(100);
    }

    /// <summary>
    /// Best-effort text of a control's UIA Name — how a plain TextBlock (FileNameLabel, ToastText;
    /// neither has a Value pattern) surfaces its content. Returns null if the control is absent or
    /// unreadable, so callers can tell "not found" apart from an empty string.
    /// </summary>
    public string? ReadText(string automationId)
    {
        var el = Find(automationId);
        if (el == null) return null;
        try { return el.Name; } catch { return null; }
    }

    /// <summary>
    /// Best-effort UIA Value of a control - the text of a TextBox (PageJumpBox) or an editable
    /// ComboBox (ZoomBox: "Fit Width", "Fit Page", "100%", "134%"). Falls back to the Name when the
    /// control has no Value pattern. Null if the control is absent or unreadable.
    /// </summary>
    public string? ReadValue(string automationId)
    {
        var el = Find(automationId);
        if (el == null) return null;
        try
        {
            if (el.Patterns.Value.IsSupported) return el.Patterns.Value.Pattern.Value.ValueOrDefault;
            return el.Name;
        }
        catch { return null; }
    }

    /// <summary>
    /// Waits for a non-main top-level window (a <c>ScalpelDialog</c> Yes/No/Cancel prompt, or a
    /// <c>ShowToolForm</c> tool dialog) and invokes the button named <paramref name="buttonName"/>
    /// in it — e.g. "Yes"/"No"/"Cancel" for the tab close/save prompt, or a tool form's own action
    /// button ("Compress", "Apply", ...). Both dialog builders give their buttons a plain string
    /// Content, which WPF's ButtonAutomationPeer surfaces as the UIA Name — the same mechanism
    /// <see cref="DismissModalsOnce"/> already relies on for its "Cancel" fallback. Returns false if
    /// no such window/button appears within the timeout.
    /// </summary>
    public bool AnswerDialog(string buttonName, int timeoutMs = 5000)
    {
        try
        {
            IntPtr mainHandle = GetMainWindowHandle();
            for (int waited = 0; waited < timeoutMs; waited += 200)
            {
                foreach (var w in DialogWindows(mainHandle))
                {
                    try
                    {
                        var btn = w.FindFirstDescendant(cf => cf.ByName(buttonName))?.AsButton();
                        if (btn != null && btn.Patterns.Invoke.IsSupported)
                        {
                            btn.Patterns.Invoke.Pattern.Invoke();
                            return true;
                        }
                    }
                    catch { }
                }
                System.Threading.Thread.Sleep(200);
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Closes the main window through the UIA WindowPattern — the same signal a physical click on
    /// the title-bar Close button sends, so it raises WPF's <c>OnClosing</c> (the per-tab dirty-save
    /// prompt loop) exactly as the real chrome button would. The chrome Close button itself carries
    /// no AutomationId to <see cref="Click"/> by id (see <c>MainWindow.xaml</c>'s title bar).
    /// </summary>
    public bool CloseMainWindow()
    {
        try { MainWindow.Close(); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        CloseApp();
        try { _automation.Dispose(); } catch { }
    }

    /// <summary>What <see cref="CloseApp"/> had to do to end an instance, for the run report.</summary>
    public sealed record CloseEvent(CloseEventKind Kind, string Suite, string Launch, string Detail);

    public enum CloseEventKind
    {
        /// <summary>A save prompt raised by the close, answered "No". A warning, not a failure.</summary>
        SavePromptAnswered,
        /// <summary>A save prompt a scenario left open before the close, cancelled. A warning.</summary>
        LeftoverSavePrompt,
        /// <summary>Any other dialog left open (before the close or raised by it). A failure.</summary>
        LeftoverDialog,
        /// <summary>The process did not exit after the close and was killed. A failure.</summary>
        ForcedKill,
    }

    /// <summary>The suite now driving this instance; tags <see cref="CloseEvents"/>. Set by the runner.</summary>
    public string CurrentSuite { get; set; } = "harness";

    private int _launchNo = 1;
    private string? _launchArg;
    private readonly List<CloseEvent> _closeEvents = [];

    /// <summary>Every event recorded by <see cref="CloseApp"/> so far, oldest first.</summary>
    public IReadOnlyList<CloseEvent> CloseEvents { get { lock (_closeEvents) return _closeEvents.ToList(); } }

    /// <summary>Closes the current instance now (as Dispose would), so the final close is recorded
    /// in <see cref="CloseEvents"/> before the report is written. Safe to call twice.</summary>
    public void Shutdown() => CloseApp();

    /// <summary>
    /// Folds <see cref="CloseEvents"/> into <paramref name="report"/>: a forced kill (a hang at
    /// close) and a leftover dialog that is not a save prompt are failures of the suite that was
    /// running; save prompts (answered "No" or left open) are warnings carrying the prompt text.
    /// </summary>
    public void AddCloseEventsTo(RunReport report)
    {
        foreach (var e in CloseEvents)
        {
            switch (e.Kind)
            {
                case CloseEventKind.ForcedKill:
                case CloseEventKind.LeftoverDialog:
                    string action = e.Kind == CloseEventKind.ForcedKill
                        ? "close:app-did-not-exit" : "close:leftover-dialog";
                    report.Results.Add(new ActionResult(e.Suite, action, Outcome.Fail,
                        $"[{e.Launch}] {e.Detail}", Array.Empty<LogEntry>()));
                    break;
                default:
                    report.Warnings.Add($"[{e.Suite}, {e.Launch}] {e.Kind}: {e.Detail}");
                    break;
            }
        }
    }

    private string LaunchLabel =>
        $"launch #{_launchNo} ({(_launchArg is null ? "no file" : System.IO.Path.GetFileName(_launchArg))})";

    private void RecordClose(CloseEventKind kind, string detail)
    {
        var ev = new CloseEvent(kind, CurrentSuite, LaunchLabel, detail);
        lock (_closeEvents) _closeEvents.Add(ev);
        Console.WriteLine($"[AppDriver.CloseApp] {kind} [{ev.Suite}, {ev.Launch}] {detail}");
    }

    /// <summary>Title plus every text line of a dialog, e.g. "'Scalpel': Save changes to a.pdf before closing?".</summary>
    private static string DescribeDialog(Window w)
    {
        string title = "";
        try { title = w.Name ?? ""; } catch { }
        var lines = new List<string>();
        try
        {
            foreach (var t in w.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)))
            {
                try { var n = t.Name; if (!string.IsNullOrWhiteSpace(n)) lines.Add(n.Trim()); } catch { }
            }
        }
        catch { }
        return lines.Count == 0 ? $"'{title}'" : $"'{title}': {string.Join(" | ", lines.Distinct())}";
    }

    // Str_Tab_SavePrompt in every locale (Strings/*.xaml), split around its {0} file name. The
    // relaunch baseline is EnUS, but a suite that clicks the language radios (singles, monkey)
    // leaves the running instance in another locale, and its final close prompts in that one.
    private static readonly (string Before, string After)[] SavePromptParts =
    [
        ("Save changes to ", " before closing?"),                       // en-US
        ("¿Guardar los cambios en ", " antes de cerrar?"),              // es
        ("關閉前要儲存對 ", " 的變更嗎？"),                               // zh-TW
        ("关闭前要保存对 ", " 的更改吗？"),                               // zh-CN
        ("বন্ধ করার আগে ", "-এর পরিবর্তনগুলি সংরক্ষণ করবেন?"),           // bn
        ("Kapatmadan önce ", " dosyasındaki değişiklikler kaydedilsin mi?"), // tr-TR
        ("לשמור את השינויים ב-", " לפני הסגירה?"),                      // he
        ("هل تريد حفظ التغييرات في ", " قبل الإغلاق؟"),                 // ar
        ("Сохранить изменения в ", " перед закрытием?"),                // ru
    ];

    private static bool IsSavePrompt(string description) =>
        SavePromptParts.Any(p => description.Contains(p.Before) && description.Contains(p.After));

    /// <summary>The modal dialogs up right now (popups and menus are not dialogs).</summary>
    private List<Window> OpenDialogs()
    {
        try
        {
            IntPtr mainHandle = GetMainWindowHandle();
            if (mainHandle == IntPtr.Zero) return [];
            return DialogWindows(mainHandle)
                .Where(w => { try { return w.IsModal; } catch { return false; } })
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>
    /// Ends the current app instance the way a user would and waits for the process to go.
    /// <para>FlaUI's <c>Application.Close</c> only posts WM_CLOSE, and Windows refuses even that
    /// while a dialog has the main window disabled; after 5 s it prints "Application failed to
    /// exit" and kills. So: dialogs a scenario left up are cancelled first, the close is sent, and
    /// the unsaved-changes prompt a dirty tab raises is answered "No" - a test relaunch discards
    /// its edits. None of that is silent: each one is recorded in <see cref="CloseEvents"/> and
    /// ends up in the run report - an answered or leftover save prompt as a warning, any other
    /// dialog and a process that had to be killed (a real hang at close) as a failure.</para>
    /// </summary>
    private void CloseApp()
    {
        int pid = -1;
        try { pid = _app.ProcessId; } catch { }
        System.Diagnostics.Process? proc = null;
        try { if (pid > 0) proc = System.Diagnostics.Process.GetProcessById(pid); } catch { }
        try
        {
            if (proc is null || proc.HasExited) return;

            foreach (var d in OpenDialogs())
            {
                string what = DescribeDialog(d);
                RecordClose(IsSavePrompt(what) ? CloseEventKind.LeftoverSavePrompt : CloseEventKind.LeftoverDialog,
                    $"still open before closing, cancelled: {what}");
            }
            // Also clears stray popups (menus, tooltips), which are not recorded.
            DismissModals();

            try { proc.CloseMainWindow(); } catch { }
            var sw = Stopwatch.StartNew();
            var handled = new HashSet<IntPtr>();
            while (!proc.WaitForExit(200) && sw.ElapsedMilliseconds < 10000)
            {
                // One prompt per dirty tab, one after another.
                foreach (var d in OpenDialogs())
                {
                    IntPtr h = IntPtr.Zero;
                    try { h = d.Properties.NativeWindowHandle.ValueOrDefault; } catch { }
                    if (!handled.Add(h)) continue;   // already answered; the app is still closing
                    string what = DescribeDialog(d);
                    if (IsSavePrompt(what))
                    {
                        var no = d.FindFirstDescendant(cf => cf.ByName("No"))?.AsButton();
                        if (no != null && no.Patterns.Invoke.IsSupported)
                        {
                            no.Patterns.Invoke.Pattern.Invoke();
                            RecordClose(CloseEventKind.SavePromptAnswered, $"answered \"No\": {what}");
                        }
                        else handled.Remove(h);      // not ready yet; retry on the next pass
                    }
                    else
                    {
                        RecordClose(CloseEventKind.LeftoverDialog, $"unexpected dialog while closing, cancelled: {what}");
                        DismissModals();
                    }
                }
            }
            if (!proc.HasExited)
            {
                RecordClose(CloseEventKind.ForcedKill,
                    $"Scalpel (pid {pid}) did not exit within 10 s of closing - killed");
                try { proc.Kill(); proc.WaitForExit(2000); } catch { }
            }
        }
        catch { }
        finally
        {
            try { proc?.Dispose(); } catch { }
            try { _app.Dispose(); } catch { }
        }
    }
}
