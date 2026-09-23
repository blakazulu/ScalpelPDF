using Microsoft.Win32;

namespace Scalpel.E2E;

/// <summary>
/// The suites click theme / accent / language / view-mode controls, which the app PERSISTS to
/// <c>HKCU\Software\Scalpel\Settings</c>. This guard snapshots those values, writes a deterministic
/// baseline for the duration of the run, and restores the user's original values on dispose. Two
/// reasons: (1) running the E2E must never silently change the user's real Scalpel preferences;
/// (2) a value left by a prior run — e.g. <c>ViewMode=Grid</c> or an RTL <c>Locale</c> — must not
/// make the next run start from a different state.
/// </summary>
public sealed class AppSettingsGuard : IDisposable
{
    private const string KeyPath = @"Software\Scalpel\Settings";
    // OpenTabs/LastFile: every app close (a real window close, or the ordinary Dispose()/Relaunch()
    // teardown between suites) runs OnClosing -> SaveWindowSettings, which persists BOTH of these
    // to the user's real registry - pointing them at the harness's temp corpus files. Without
    // guarding them, the user's next plain launch would try to restore/reopen dead test files.
    // Window geometry / FitMode: the app persists them on every close too, so the monkey suite's
    // last random resize (800x600) became the window size of every later relaunch. At that height
    // the page image runs far below the visible viewport and the fonts suite's canvas click (45% down
    // the page) landed on the status bar or the title-bar Install badge instead of the page.
    // RecentFiles: snapshot/restore only - the harness's corpus files otherwise pile into the user's
    // real recent list.
    private static readonly string[] Managed =
        { "Theme", "Accent", "Locale", "ViewMode", "ZoomLevel", "OpenTabs", "LastFile",
          "FitMode", "WindowWidth", "WindowHeight", "WindowTop", "WindowLeft", "WindowState", "RecentFiles" };
    private readonly Dictionary<string, object?> _saved = new();

    public static AppSettingsGuard SnapshotAndBaseline()
    {
        var guard = new AppSettingsGuard();
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            foreach (var name in Managed) guard._saved[name] = key.GetValue(name);
        }
        catch { }
        WriteBaseline();
        return guard;
    }

    /// <summary>
    /// Write the deterministic baseline (single-page, non-RTL English, non-HighContrast) to the
    /// registry. Suites mutate these by clicking theme/accent/language/view controls, and the app
    /// reads them at startup — so this MUST be re-applied before every app relaunch, or a relaunch
    /// inherits the contaminated state (e.g. an RTL locale that breaks canvas annotation placement).
    /// </summary>
    public static void WriteBaseline()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("Theme", "Dark");
            key.SetValue("Accent", "Amber");
            key.SetValue("Locale", "EnUS");
            key.SetValue("ViewMode", "Single");
            key.SetValue("ZoomLevel", "1"); // 100% — a prior suite's fractional zoom can leave the page un-rendered where canvas placement expects it
            key.SetValue("FitMode", "None"); // or a persisted Fit Width/Page overrides the 100% above
            // A fixed, tall-enough window (the driver then centres it on the target monitor) instead
            // of whatever size the previous instance was left at. The fonts suite double-clicks text
            // drawn at the middle of an A4 page shown at 100%, so the middle of the page must be
            // inside the viewport: the app's 700-px default height is not enough, and neither was a
            // leftover 800x600 from the monkey suite.
            key.SetValue("WindowWidth", "1000");
            key.SetValue("WindowHeight", AppDriver.BaselineWindowHeight().ToString());
            key.SetValue("WindowState", "Normal");
            foreach (var name in new[] { "WindowTop", "WindowLeft" })
                key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            foreach (var name in Managed)
            {
                if (_saved.TryGetValue(name, out var v) && v != null) key.SetValue(name, v);
                else key.DeleteValue(name, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
