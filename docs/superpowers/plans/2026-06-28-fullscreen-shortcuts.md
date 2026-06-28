# Full-screen (F11) + Keyboard Shortcuts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** F11 full-screen mode (hide chrome, fill monitor, restore cleanly) + keyboard shortcuts (F1/F2/F5–F8, V/T/H/D/L/I), with shortcut-overlay and localization updates.

**Architecture:** New `MainWindow.FullScreen.cs` partial toggles chrome visibility + window bounds (reusing existing monitor P/Invoke). New arms in the existing `OnPreviewKeyDown`. No new pure logic.

**Tech Stack:** C# / .NET Framework 4.8, WPF. Build via `~/.dotnet/dotnet.exe`.

## Global Constraints
- Build via `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`; tests `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj` (baseline 187 stays green).
- pdfium lock gotcha: close running `Scalpel.exe` before building.
- All edited/new files UTF-8-BOM + CRLF.
- New `Str_*` keys go in ALL 9 `Strings/*.xaml`.
- REUSE the existing `MonitorFromWindow`/`GetMonitorInfo`/`MONITORINFO`/`RECT`/`MONITOR_DEFAULTTONEAREST` in `MainWindow.Settings.cs` — do NOT redeclare (duplicate P/Invoke = build error).
- Reuse existing `SetViewMode`, `SetTool`, `SetMode`, `ShowAboutOverlay`, `ShortcutOverlay`, `Loc`, `_sidebarCol`, `_sidebarBorder`.

---

## Task 1: XAML names + `MainWindow.FullScreen.cs`

**Files:** `MainWindow.xaml` (add x:Names only), create `MainWindow.FullScreen.cs`.
**Interfaces — Produces:** `ToggleFullScreen()`, `bool _fullScreen`.

- [ ] **Step 1: Add x:Names in `MainWindow.xaml` (names only, no layout change).** READ the file first to confirm structure, then:
  - The root `<Grid>` (the one with the 5 `RowDefinition`s, ~line 435) → add `x:Name="RootGrid"`.
  - The `<Border Grid.Row="0">` title bar (~line 449) → `x:Name="TitleBarBorder"`.
  - The `<Border Grid.Row="1">` ribbon tab strip (~line 495) → `x:Name="RibbonTabBorder"`.
  - The `<Border Grid.Row="2">` ribbon band (~line 522) → `x:Name="RibbonBandBorder"`.
  - The `<Border Grid.Row="4">` status bar (~line 927) → `x:Name="StatusBarBorder"`.
  (If any already has an x:Name, keep it and use that name in Step 2 instead.)

- [ ] **Step 2: Create `MainWindow.FullScreen.cs`** (UTF-8-BOM, CRLF). Adapt element/field names to what Step 1 and the codebase actually use (verify `_sidebarCol`, `_sidebarBorder`, `SidebarSplitter`, `PagePreviewPanel`, and the splitter column index by reading `MainWindow.xaml`/`MainWindow.ToolSelection.cs`):
```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Scalpel
{
    public partial class MainWindow
    {
        private bool _fullScreen;
        private GridLength _fsRow0, _fsRow1, _fsRow2, _fsRow4, _fsSidebarW, _fsSplitterW;
        private double _fsSidebarMin;
        private WindowState _fsPrevState;
        private bool _fsPrevTopmost;
        private ResizeMode _fsPrevResize;
        private double _fsPrevLeft, _fsPrevTop, _fsPrevW, _fsPrevH;

        private void ToggleFullScreen() => ApplyFullScreen(!_fullScreen);

        private void ApplyFullScreen(bool entering)
        {
            _fullScreen = entering;
            var v = entering ? Visibility.Collapsed : Visibility.Visible;

            TitleBarBorder.Visibility   = v;
            RibbonTabBorder.Visibility  = v;
            RibbonBandBorder.Visibility = v;
            StatusBarBorder.Visibility  = v;
            SidebarBorder.Visibility    = v;
            SidebarSplitter.Visibility  = v;

            if (entering)
            {
                _fsRow0 = RootGrid.RowDefinitions[0].Height;
                _fsRow1 = RootGrid.RowDefinitions[1].Height;
                _fsRow2 = RootGrid.RowDefinitions[2].Height;
                _fsRow4 = RootGrid.RowDefinitions[4].Height;
                RootGrid.RowDefinitions[0].Height = new GridLength(0);
                RootGrid.RowDefinitions[1].Height = new GridLength(0);
                RootGrid.RowDefinitions[2].Height = new GridLength(0);
                RootGrid.RowDefinitions[4].Height = new GridLength(0);

                _fsSidebarW   = _sidebarCol.Width;
                _fsSidebarMin = _sidebarCol.MinWidth;
                _fsSplitterW  = MainContentGrid.ColumnDefinitions[1].Width;
                _sidebarCol.MinWidth = 0;
                _sidebarCol.Width = new GridLength(0);
                MainContentGrid.ColumnDefinitions[1].Width = new GridLength(0);

                PagePreviewPanel.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));

                _fsPrevState = WindowState; _fsPrevTopmost = Topmost; _fsPrevResize = ResizeMode;
                _fsPrevLeft = Left; _fsPrevTop = Top; _fsPrevW = Width; _fsPrevH = Height;

                var b = CurrentMonitorBoundsDip();
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;

                ShowFullScreenHint();
            }
            else
            {
                RootGrid.RowDefinitions[0].Height = _fsRow0;
                RootGrid.RowDefinitions[1].Height = _fsRow1;
                RootGrid.RowDefinitions[2].Height = _fsRow2;
                RootGrid.RowDefinitions[4].Height = _fsRow4;
                _sidebarCol.MinWidth = _fsSidebarMin;
                _sidebarCol.Width = _fsSidebarW;
                MainContentGrid.ColumnDefinitions[1].Width = _fsSplitterW;
                PagePreviewPanel.SetResourceReference(Control.BackgroundProperty, "BgCanvas");

                Topmost = _fsPrevTopmost;
                ResizeMode = _fsPrevResize;
                WindowState = WindowState.Normal;
                Left = _fsPrevLeft; Top = _fsPrevTop; Width = _fsPrevW; Height = _fsPrevH;
                if (_fsPrevState == WindowState.Maximized) WindowState = WindowState.Maximized;
            }
        }

        // Full monitor bounds (taskbar incl.) of the current monitor, in DIPs. Reuses the P/Invoke
        // (MonitorFromWindow/GetMonitorInfo/MONITORINFO/MONITOR_DEFAULTTONEAREST) declared in MainWindow.Settings.cs.
        private Rect CurrentMonitorBoundsDip()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(mon, ref info);
            var r = info.rcMonitor;
            var dpi = VisualTreeHelper.GetDpi(this);
            return new Rect(r.left / dpi.DpiScaleX, r.top / dpi.DpiScaleY,
                            (r.right - r.left) / dpi.DpiScaleX, (r.bottom - r.top) / dpi.DpiScaleY);
        }

        private void ShowFullScreenHint()
        {
            var toast = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1c, 0x1c, 0x1c)),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(18, 9, 18, 9),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 44, 0, 0), Opacity = 0, IsHitTestVisible = false,
                Child = new TextBlock { Text = Loc("Str_FullScreen_Hint"), Foreground = Brushes.White, FontSize = 13 }
            };
            Grid.SetRow(toast, 0);
            Grid.SetRowSpan(toast, RootGrid.RowDefinitions.Count);
            Panel.SetZIndex(toast, 99999);
            RootGrid.Children.Add(toast);
            toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            t.Tick += (_, __) =>
            {
                t.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                fade.Completed += (_, ___) => RootGrid.Children.Remove(toast);
                toast.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            t.Start();
        }
    }
}
```
IMPORTANT: verify the real names — `_sidebarCol` and `_sidebarBorder` (fields used by `SidebarToggle_Click`), `SidebarBorder`/`SidebarSplitter` (XAML x:Names), `MainContentGrid` (row-3 content grid x:Name), `PagePreviewPanel` (ScrollViewer x:Name), and the resource key for `PagePreviewPanel`'s normal background (the plan assumes `"BgCanvas"` — confirm against how the ScrollViewer's Background is set in XAML; if it has no explicit Background, on exit set `PagePreviewPanel.ClearValue(Control.BackgroundProperty)` instead of `SetResourceReference`). If `SidebarBorder`/`SidebarSplitter` are accessed via fields (`_sidebarBorder`) rather than x:Names, use the fields.

- [ ] **Step 3: Build** — `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug` → 0 errors.

- [ ] **Step 4: Commit**
```bash
git add MainWindow.xaml MainWindow.FullScreen.cs
git commit -m "feat(fullscreen): F11 full-screen mode (hide chrome, fill monitor, restore)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Keyboard shortcut arms

**Files:** `MainWindow.KeyboardShortcuts.cs`
**Consumes:** `ToggleFullScreen()`, `_fullScreen`, `SetViewMode(ViewMode)`, `SetMode(AppMode)`, `SetTool(EditTool)`, `ShowAboutOverlay()`, `ShortcutOverlay`.

- [ ] **Step 1: Read `OnPreviewKeyDown`** to see the exact if/else-if chain, the top guards, the F12 arm, and the current Escape arm.

- [ ] **Step 2: Add F-key + tool-letter arms.** Near the F12 arm, add (using the real method/enum names confirmed by reading the codebase):
```csharp
else if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
else if (e.Key == Key.F1)  { ShortcutOverlay.Visibility = ShortcutOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; e.Handled = true; }
else if (e.Key == Key.F2)  { ShowAboutOverlay(); e.Handled = true; }
else if (e.Key == Key.F5)  { SetViewMode(ViewMode.Single);     e.Handled = true; }
else if (e.Key == Key.F6)  { SetViewMode(ViewMode.Continuous); e.Handled = true; }
else if (e.Key == Key.F7)  { SetViewMode(ViewMode.TwoPage);    e.Handled = true; }
else if (e.Key == Key.F8)  { SetViewMode(ViewMode.Grid);       e.Handled = true; }
else if (Keyboard.Modifiers == ModifierKeys.None &&
         (e.Key == Key.V || e.Key == Key.T || e.Key == Key.H || e.Key == Key.D || e.Key == Key.L || e.Key == Key.I))
{
    SetMode(AppMode.Edit);
    SetTool(e.Key switch
    {
        Key.V => EditTool.Select,
        Key.T => EditTool.Text,
        Key.H => EditTool.Highlight,
        Key.D => EditTool.Draw,
        Key.L => EditTool.Line,
        _     => EditTool.Image,   // Key.I
    });
    e.Handled = true;
}
```
(`SetMode`/`AppMode` enum: confirm the names — CLAUDE.md says `AppMode { View, Edit, Pages, Sign }` and `SetMode(AppMode)`. If `ShowAboutOverlay` is named differently, use the real name from `MainWindow.PageSelection.cs`.)

- [ ] **Step 3: Make Esc exit full-screen first.** In the existing Escape arm, before its current behavior, add: `if (_fullScreen) { ApplyFullScreen(false); e.Handled = true; return; }` (adapt to the arm's structure so Esc exits full-screen instead of closing the app/overlays when in full-screen).

- [ ] **Step 4: Build** → 0 errors.

- [ ] **Step 5: Commit**
```bash
git add MainWindow.KeyboardShortcuts.cs
git commit -m "feat(shortcuts): F1/F2/F5-F8/F11 + Edit-tool letter keys

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Shortcut overlay rows + localization + changelog

**Files:** `MainWindow.xaml` (ShortcutOverlay rows), `Strings/*.xaml` ×9, `Services/Changelog.cs`

- [ ] **Step 1: Localization** — add to ALL 9 `Strings/*.xaml` (en-US verbatim; translate others incl. RTL he/ar):
```xml
<sys:String x:Key="Str_KS_FullScreen">Full screen</sys:String>
<sys:String x:Key="Str_KS_ViewSingle">Single page view</sys:String>
<sys:String x:Key="Str_KS_ViewContinuous">Continuous view</sys:String>
<sys:String x:Key="Str_KS_ViewTwoPage">Two-page view</sys:String>
<sys:String x:Key="Str_KS_ViewGrid">Grid view</sys:String>
<sys:String x:Key="Str_KS_Help">Keyboard shortcuts</sys:String>
<sys:String x:Key="Str_KS_About">About</sys:String>
<sys:String x:Key="Str_KS_Tools">Edit tools (Select / Text / Highlight / Draw / Line / Image)</sys:String>
<sys:String x:Key="Str_KS_DocInfo">Document info</sys:String>
<sys:String x:Key="Str_FullScreen_Hint">Press F11 or Esc to exit full screen</sys:String>
```
Use each file's existing `sys:`/`s:` prefix.

- [ ] **Step 2: Add overlay rows** — in `MainWindow.xaml`'s `ShortcutOverlay`, after the last existing `<DockPanel>` shortcut row, add rows matching the existing format (fixed-120 mono key TextBlock + localized description). Keys/desc:
  - `F11` → `Str_KS_FullScreen`
  - `F1` → `Str_KS_Help`
  - `F2` → `Str_KS_About`
  - `F5` → `Str_KS_ViewSingle`, `F6` → `Str_KS_ViewContinuous`, `F7` → `Str_KS_ViewTwoPage`, `F8` → `Str_KS_ViewGrid`
  - `F12` → `Str_KS_DocInfo`
  - `V T H D L I` → `Str_KS_Tools`
  Copy the exact `<DockPanel>...<TextBlock .../><TextBlock .../></DockPanel>` shape from an existing row; only change the key text and the description's `DynamicResource` key.

- [ ] **Step 3: Changelog** — prepend one bullet to the newest `Release` in `Services/Changelog.cs`:
```
"New full-screen mode (F11) plus keyboard shortcuts: F1 shortcuts, F2 about, F5–F8 view modes, and letter keys (V/T/H/D/L/I) to pick Edit tools.",
```

- [ ] **Step 4: Build** → 0 errors. **Locale check:** `grep -L "Str_KS_FullScreen" Strings/*.xaml` → no output.

- [ ] **Step 5: Commit**
```bash
git add MainWindow.xaml Strings/*.xaml Services/Changelog.cs
git commit -m "feat(shortcuts): list new shortcuts in overlay + localization + changelog

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Final verification

- [ ] **Step 1: Build** → 0 errors, warnings ≤ 22.
- [ ] **Step 2: Tests** → `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj` → 187 passing.
- [ ] **Step 3: Locale completeness** — `grep -L "Str_KS_FullScreen" Strings/en-US.xaml Strings/es.xaml Strings/zh-TW.xaml Strings/zh-CN.xaml Strings/bn.xaml Strings/tr-TR.xaml Strings/he.xaml Strings/ar.xaml Strings/ru.xaml` → no output.
- [ ] **Step 4: Wiring sanity** — `grep -rn "ToggleFullScreen\|Key.F11\|Key.F5\|EditTool.Line" MainWindow.FullScreen.cs MainWindow.KeyboardShortcuts.cs` shows the method defined once and the arms present.
- [ ] **Step 5: Manual smoke (owed to user — GUI cannot run headless):** F11 → all chrome gone, document fills monitor, no side strips, taskbar covered, toast shows; F11/Esc → exact restore (placement, sidebar width, maximized state); second monitor → fills correct screen. F5–F8 view modes; F1 toggles overlay (lists new keys incl. F11/F12/tools); F2 About; V/T/H/D/L/I switch Edit tools into Edit mode; none fire while typing in a field/annotation edit box.

## Self-review notes
- Spec coverage: full-screen partial + XAML names (T1), key arms incl. Esc-exit + tool letters (T2), overlay rows + 10 locale keys + changelog (T3), verification (T4). All mapped.
- Reuses existing monitor P/Invoke, SetViewMode/SetTool/SetMode/ShowAboutOverlay — no redeclaration. No pure logic ⇒ no unit test, per spec.
- Risk: element x:Names/field names — every task's first step is to READ and confirm real names before editing.
