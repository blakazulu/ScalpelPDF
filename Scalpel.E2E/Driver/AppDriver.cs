using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private const int SW_RESTORE = 9;

    private readonly UIA3Automation _automation;
    private Application _app;
    private readonly string _exePath;

    private AppDriver(string exePath, Application app, UIA3Automation automation)
    {
        _exePath = exePath;
        _app = app;
        _automation = automation;
    }

    public static AppDriver Launch(string exePath, string openWithPath)
    {
        var automation = new UIA3Automation();
        var psi = new ProcessStartInfo(exePath, $"\"{openWithPath}\"") { UseShellExecute = false };
        var app = Application.Launch(psi);
        var driver = new AppDriver(exePath, app, automation);
        driver.WaitForMainWindow();
        driver.FocusMainWindow();
        return driver;
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
                if (w != null) return;
            }
            catch { }
            System.Threading.Thread.Sleep(250);
        }
        throw new InvalidOperationException("Scalpel main window did not appear.");
    }

    public Window MainWindow => _app.GetMainWindow(_automation, TimeSpan.FromSeconds(5));

    public bool IsAlive
    {
        get
        {
            try { return !_app.HasExited && MainWindow != null; }
            catch { return false; }
        }
    }

    public void Relaunch(string openWithPath)
    {
        try { _app.Close(); _app.Dispose(); } catch { }
        var psi = new ProcessStartInfo(_exePath, $"\"{openWithPath}\"") { UseShellExecute = false };
        _app = Application.Launch(psi);
        WaitForMainWindow();
    }

    public AutomationElement? Find(string automationId)
    {
        try
        {
            return MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        }
        catch { return null; }
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
            // We need a REAL click that raises WPF's ButtonBase.Click routed event,
            // because the app's global logger hooks that event. Plain Buttons expose
            // the Invoke pattern, which raises Click and works regardless of window
            // focus. ToggleButton/RadioButton do NOT support Invoke, and their
            // Toggle()/SelectionItem.Select()/LegacyIAccessible.DoDefaultAction()
            // patterns change state WITHOUT raising Click — so nothing gets logged.
            // For those, only a physical click raises Click, and it requires the
            // window to be in the foreground (otherwise the synthesized click lands
            // on whatever is on top). Foreground the window first, then click.
            if (el.Patterns.Invoke.IsSupported)
            {
                el.Patterns.Invoke.Pattern.Invoke();
            }
            else
            {
                FocusMainWindow(); // robust forced-foreground; physical clicks need it
                System.Threading.Thread.Sleep(60); // let the window actually come forward
                el.Click();
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
    /// True when the Settings overlay is open, detected by a control that only
    /// renders inside it (ThemeDarkRadio). When the overlay is collapsed, that
    /// control is absent from the UIA tree.
    /// </summary>
    public bool IsSettingsOverlayOpen()
    {
        var el = Find("ThemeDarkRadio");
        if (el == null) return false;
        try { return !el.Properties.IsOffscreen.ValueOrDefault; }
        catch { return false; }
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

    /// <summary>
    /// Close any unexpected modal/dialog windows that are not the main window.
    /// FlaUI 4.x: Window.IsModal is a direct property (not a pattern call).
    /// Uses native window handles (IntPtr) for stable identity comparison instead of object equality.
    /// </summary>
    public void DismissModals()
    {
        try
        {
            // Defensively read the main window's native handle once.
            IntPtr mainWindowHandle = IntPtr.Zero;
            try
            {
                var mainWindow = MainWindow;
                if (mainWindow != null)
                {
                    mainWindowHandle = mainWindow.Properties.NativeWindowHandle;
                }
            }
            catch { }

            // Close all top-level windows except the main window (by handle).
            foreach (var w in _app.GetAllTopLevelWindows(_automation))
            {
                try
                {
                    // If we have the main window's handle and this window matches it, skip.
                    if (mainWindowHandle != IntPtr.Zero && w.Properties.NativeWindowHandle == mainWindowHandle)
                    {
                        continue;
                    }
                    w.AsWindow()?.Close();
                }
                catch { }
            }
        }
        catch { }
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
                var dialog = _app.GetAllTopLevelWindows(_automation)
                    .FirstOrDefault(w => w.IsModal || (w.Name?.Contains("PDF") ?? false));
                if (dialog != null)
                {
                    var edit = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
                    edit?.AsTextBox()?.Enter(path);
                    var btn = dialog.FindFirstDescendant(cf => cf.ByName(confirmButtonName))?.AsButton();
                    btn?.Invoke();
                    return true;
                }
                System.Threading.Thread.Sleep(250);
            }
            return false;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        try { _app.Close(); } catch { }
        try { _app.Dispose(); } catch { }
        try { _automation.Dispose(); } catch { }
    }
}
