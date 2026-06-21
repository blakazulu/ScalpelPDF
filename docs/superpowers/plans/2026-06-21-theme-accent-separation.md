# Theme / Accent Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the single 6-value `Theme` enum into two orthogonal axes — base theme (Dark/Light/HighContrast) and accent (Amber/Red/Green/Cyan) — so the accent can be changed without forcing dark mode.

**Architecture:** Base theme dictionaries stay complete (own all backgrounds + a built-in Amber default). Accent is a thin overlay dictionary (~11 keys) applied on top of the base for Dark & Light only; High Contrast keeps its fixed amber and takes no overlay. Both axes persist separately in the registry, with one-time migration from the legacy single-axis values.

**Tech Stack:** C# / .NET Framework 4.8, WPF, xUnit. PdfSharpCore/PdfPig/Docnet unaffected.

**Spec:** `docs/superpowers/specs/2026-06-21-theme-accent-separation-design.md`

## Global Constraints

- Target `net48`; build with .NET 8 SDK. `LangVersion=latest`, `Nullable` enabled, `ImplicitUsings` enabled. Use collection expressions / target-typed `new` / `switch` expressions to match existing style.
- Theme resources are updated **in place per-key** on `Application.Current.Resources.MergedDictionaries[0]` — never structurally add/remove the theme dict (avoids `ResourceReferenceKeyNotFoundException` during live switches).
- Keep merged-dictionary index order: `[0]` theme, `[1]` strings, `[2]` `_Shared.xaml`.
- Every file in a layer group must define the **identical key set**: all 3 base theme files share the full key set; all 6 accent overlays share the accent-key set. A missing key keeps a stale value on switch.
- Every locale string key must exist in **all six** `Strings/*.xaml` files or a `DynamicResource` blanks out in that language.
- I/O wrapped in defensive `try/catch` that swallows and falls back (registry reads especially).
- DWM dark titlebar applies unless the **base** theme is `Light` (accent-independent).

---

### Task 1: Theme/Accent enums + pure migration helper (TDD)

Isolate the enums and the legacy-value migration into a WPF-free file so it can be unit-tested. `ThemeManager` (Task 2) will consume these.

**Files:**
- Create: `Services/ThemeMigration.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (link the new file)
- Test: `Scalpel.Tests/ThemeMigrationTests.cs`

**Interfaces:**
- Produces:
  - `enum Scalpel.Services.Theme { Dark, Light, HighContrast }`
  - `enum Scalpel.Services.Accent { Amber, Red, Green, Cyan }`
  - `static (Theme theme, Accent accent) ThemeMigration.Resolve(string? themeRaw, string? accentRaw)`

- [ ] **Step 1: Link the (not-yet-created) source file into the test project**

In `Scalpel.Tests/Scalpel.Tests.csproj`, add to the existing `<ItemGroup>` of linked `<Compile>` items (after the `SearchService.cs` line):

```xml
    <Compile Include="..\Services\ThemeMigration.cs" Link="Services\ThemeMigration.cs" />
```

- [ ] **Step 2: Write the failing tests**

Create `Scalpel.Tests/ThemeMigrationTests.cs`:

```csharp
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class ThemeMigrationTests
    {
        [Theory]
        [InlineData("Dark",  Theme.Dark,         Accent.Amber)]
        [InlineData("Light", Theme.Light,        Accent.Amber)]
        [InlineData("HighContrast", Theme.HighContrast, Accent.Amber)]
        [InlineData("Blood",    Theme.Dark, Accent.Red)]
        [InlineData("Greed",    Theme.Dark, Accent.Green)]
        [InlineData("Cyanotic", Theme.Dark, Accent.Cyan)]
        public void Resolve_maps_legacy_theme_when_no_accent_saved(
            string themeRaw, Theme expectedTheme, Accent expectedAccent)
        {
            var (theme, accent) = ThemeMigration.Resolve(themeRaw, accentRaw: null);
            Assert.Equal(expectedTheme, theme);
            Assert.Equal(expectedAccent, accent);
        }

        [Fact]
        public void Resolve_uses_explicit_accent_over_legacy_derivation()
        {
            // New-style settings: base Light, explicit accent Green.
            var (theme, accent) = ThemeMigration.Resolve("Light", "Green");
            Assert.Equal(Theme.Light, theme);
            Assert.Equal(Accent.Green, accent);
        }

        [Fact]
        public void Resolve_explicit_accent_wins_even_with_legacy_theme_name()
        {
            // Legacy "Blood" would derive Red, but a stored Accent must win.
            var (theme, accent) = ThemeMigration.Resolve("Blood", "Cyan");
            Assert.Equal(Theme.Dark, theme);
            Assert.Equal(Accent.Cyan, accent);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        public void Resolve_defaults_to_Dark_Amber_on_unknown_theme(string? themeRaw)
        {
            var (theme, accent) = ThemeMigration.Resolve(themeRaw, accentRaw: null);
            Assert.Equal(Theme.Dark, theme);
            Assert.Equal(Accent.Amber, accent);
        }

        [Fact]
        public void Resolve_ignores_invalid_accent_string()
        {
            var (theme, accent) = ThemeMigration.Resolve("Dark", "notacolor");
            Assert.Equal(Theme.Dark, theme);
            Assert.Equal(Accent.Amber, accent);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail to compile (type not defined)**

Run: `dotnet test --filter "FullyQualifiedName~ThemeMigrationTests"`
Expected: FAIL — build error, `ThemeMigration` / `Theme` / `Accent` do not exist.

- [ ] **Step 4: Write the minimal implementation**

Create `Services/ThemeMigration.cs`:

```csharp
using System;

namespace Scalpel.Services
{
    internal enum Theme  { Dark, Light, HighContrast }
    internal enum Accent { Amber, Red, Green, Cyan }

    /// <summary>
    /// Pure resolution of persisted settings into a (Theme, Accent) pair.
    /// Kept WPF-free so it is unit-testable and linkable into the test project.
    /// Handles the legacy single-axis theme names (Blood/Greed/Cyanotic).
    /// </summary>
    internal static class ThemeMigration
    {
        public static (Theme theme, Accent accent) Resolve(string? themeRaw, string? accentRaw)
        {
            // Base theme + the accent implied by a legacy single-axis value.
            var (theme, legacyAccent) = themeRaw switch
            {
                "Light"        => (Theme.Light,        Accent.Amber),
                "HighContrast" => (Theme.HighContrast, Accent.Amber),
                "Blood"        => (Theme.Dark,         Accent.Red),
                "Greed"        => (Theme.Dark,         Accent.Green),
                "Cyanotic"     => (Theme.Dark,         Accent.Cyan),
                "Dark"         => (Theme.Dark,         Accent.Amber),
                _              => (Theme.Dark,         Accent.Amber),
            };

            // An explicit, valid Accent setting always wins over the legacy derivation.
            var accent = Enum.TryParse<Accent>(accentRaw, out var a) ? a : legacyAccent;
            return (theme, accent);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ThemeMigrationTests"`
Expected: PASS (all cases).

- [ ] **Step 6: Commit**

```bash
git add Services/ThemeMigration.cs Scalpel.Tests/ThemeMigrationTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "Add Theme/Accent enums + tested legacy migration helper"
```

---

### Task 2: Two-axis ThemeManager (base + accent overlay loader)

Rewrite `ThemeManager` to drive two axes off the enums from Task 1. The accent XAML overlays don't exist yet (Task 3); Amber takes no overlay, so this task builds and runs in Amber for every base immediately. Red/Green/Cyan overlays light up once Task 3 lands.

**Files:**
- Modify: `Services/ThemeManager.cs` (full rewrite of the type body; enums now come from `ThemeMigration.cs`)

**Interfaces:**
- Consumes: `Theme`, `Accent`, `ThemeMigration.Resolve` (Task 1); `App.GetSetting`/`App.SetSetting`.
- Produces:
  - `static Theme  ThemeManager.CurrentTheme { get; }`
  - `static Accent ThemeManager.CurrentAccent { get; }`
  - `static void ThemeManager.Initialize()`
  - `static void ThemeManager.ApplyTheme(Theme t)`
  - `static void ThemeManager.ApplyAccent(Accent a)`
  - `static void ThemeManager.ApplyDwm(IntPtr hwnd)`
  - `static void ThemeManager.RefreshIcons()`
  - `static event Action? ThemeManager.ThemeChanged`

- [ ] **Step 1: Replace the enum + state + public API section**

In `Services/ThemeManager.cs`, delete the line `internal enum Theme { Dark, Light, HighContrast, Blood, Greed, Cyanotic }` (it now lives in `ThemeMigration.cs`).

Replace the `// ── State ──` and `// ── Public API ──` sections (the `_current`, `Current`, `Initialize`, `Apply` members) with:

```csharp
        // ── State ─────────────────────────────────────────────────────────

        private static Theme  _theme  = Theme.Dark;
        private static Accent _accent = Accent.Amber;

        public static Theme  CurrentTheme  => _theme;
        public static Accent CurrentAccent => _accent;

        /// <summary>Fired after the theme/accent dictionary has been updated.</summary>
        public static event Action? ThemeChanged;

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Call once at startup (before MainWindow is created) to restore the saved
        /// theme + accent, migrating legacy single-axis values. DWM title bar is
        /// applied later via ApplyDwm(hwnd) from SourceInitialized.
        /// </summary>
        public static void Initialize()
        {
            var (theme, accent) = ThemeMigration.Resolve(App.GetSetting("Theme"), App.GetSetting("Accent"));
            _theme  = theme;
            _accent = accent;
            // Normalize persisted values so later loads are clean (drops legacy names).
            App.SetSetting("Theme",  _theme.ToString());
            App.SetSetting("Accent", _accent.ToString());
            ApplyInternal(applyDwm: false);
        }

        /// <summary>Change the base theme, keep the current accent, persist, update DWM.</summary>
        public static void ApplyTheme(Theme theme)
        {
            _theme = theme;
            App.SetSetting("Theme", theme.ToString());
            ApplyInternal(applyDwm: true);
            ThemeChanged?.Invoke();
        }

        /// <summary>Change the accent, keep the current base theme, persist.</summary>
        public static void ApplyAccent(Accent accent)
        {
            _accent = accent;
            App.SetSetting("Accent", accent.ToString());
            ApplyInternal(applyDwm: false);
            ThemeChanged?.Invoke();
        }

        /// <summary>Called from Window.SourceInitialized to set the native title bar colour.</summary>
        public static void ApplyDwm(IntPtr hwnd)
        {
            SetDwm(hwnd, _theme != Theme.Light);
        }
```

- [ ] **Step 2: Replace `ApplyInternal` + `LoadDict` with the two-layer loader**

Replace the existing `ApplyInternal(Theme, bool)` and `LoadDict(Theme)` methods with:

```csharp
        private static void ApplyInternal(bool applyDwm)
        {
            LoadDict(_theme, _accent);

            if (applyDwm)
            {
                var win = Application.Current?.MainWindow;
                if (win != null)
                {
                    var hwnd = new WindowInteropHelper(win).Handle;
                    if (hwnd != IntPtr.Zero)
                        SetDwm(hwnd, _theme != Theme.Light);
                }
            }
        }

        private static Uri BaseUri(Theme theme) => theme switch
        {
            Theme.Light        => new Uri("pack://application:,,,/Themes/Light.xaml"),
            Theme.HighContrast => new Uri("pack://application:,,,/Themes/HighContrast.xaml"),
            _                  => new Uri("pack://application:,,,/Themes/Dark.xaml"),
        };

        // Accent overlay exists only for Dark/Light and only for non-Amber accents.
        // Amber is the base file's built-in default; High Contrast has a fixed accent.
        private static Uri? AccentUri(Theme theme, Accent accent)
        {
            if (theme == Theme.HighContrast || accent == Accent.Amber) return null;
            return new Uri($"pack://application:,,,/Themes/Accents/{theme}_{accent}.xaml");
        }

        private static void LoadDict(Theme theme, Accent accent)
        {
            var merged = Application.Current.Resources.MergedDictionaries;

            var baseDict = new ResourceDictionary { Source = BaseUri(theme) };

            // In-place per-key update of the theme slot [0]. Structural add/remove fires a
            // synchronous ResourcesChanged that can invoke FindResource() before the new dict
            // is in place (e.g. SwitchSidebarToPagesTab), causing ResourceReferenceKeyNotFoundException.
            if (merged.Count > 0)
            {
                var existing = merged[0];
                foreach (object key in baseDict.Keys)
                    existing[key] = baseDict[key];

                // Overlay the accent's keys on top (Dark/Light + non-Amber only).
                var accentUri = AccentUri(theme, accent);
                if (accentUri != null)
                {
                    var accentDict = new ResourceDictionary { Source = accentUri };
                    foreach (object key in accentDict.Keys)
                        existing[key] = accentDict[key];
                }
            }
            else
            {
                // First load before the slot exists: merge base, then accent overlay.
                merged.Add(baseDict);
                var accentUri = AccentUri(theme, accent);
                if (accentUri != null)
                    merged.Add(new ResourceDictionary { Source = accentUri });
            }

            // One SystemIdle pass to nudge elements whose effective value didn't auto-update
            // (e.g. ControlTemplate trigger bindings with TargetName that missed the per-key signal).
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, (Action)RefreshIcons);
        }
```

> Note: in the empty-slot branch the accent overlay is added as a second dictionary, so `[0]` is no longer the only theme dict. This branch only runs if `MergedDictionaries` is empty at first load — in practice `App.xaml` already declares the theme dict at `[0]`, so `Initialize` always takes the in-place branch. Left for safety; does not affect the live-switch path.

- [ ] **Step 3: Confirm `RefreshIcons`, `ForceRender`, `SetDwm` are unchanged**

These three methods stay exactly as-is (they reference no enum values). No edit needed.

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: BUILD SUCCEEDED. (The app now compiles; callers in `MainWindow` still reference the old API and will be fixed in Task 4 — so expect *compile errors in MainWindow* here. If building the whole solution, this task's build will fail on `MainWindow` until Task 4. Build just verifies `ThemeManager.cs` has no internal errors; full green build is asserted at the end of Task 4.)

Because `MainWindow` won't compile yet, instead verify this file in isolation by reading it back and confirming no references to `Theme.Blood/Greed/Cyanotic` or the removed `Apply(Theme)`/`Current` members remain in `ThemeManager.cs`.

Run: `git diff --stat Services/ThemeManager.cs`
Expected: shows the rewrite.

- [ ] **Step 5: Commit**

```bash
git add Services/ThemeManager.cs
git commit -m "ThemeManager: two-axis base + accent overlay loader"
```

---

### Task 3: Accent overlay XAML files + remove legacy theme files

Create the 6 accent overlays (Dark lifted from the old colored themes; Light authored fresh), and delete the obsolete `Blood/Greed/Cyanotic.xaml`. WPF auto-includes `*.xaml` as `Page`, so no `.csproj` edit is needed.

**Files:**
- Create: `Themes/Accents/Dark_Red.xaml`, `Themes/Accents/Dark_Green.xaml`, `Themes/Accents/Dark_Cyan.xaml`
- Create: `Themes/Accents/Light_Red.xaml`, `Themes/Accents/Light_Green.xaml`, `Themes/Accents/Light_Cyan.xaml`
- Delete: `Themes/Blood.xaml`, `Themes/Greed.xaml`, `Themes/Cyanotic.xaml`

**Interfaces:**
- Consumes: the `pack://…/Themes/Accents/{Theme}_{Accent}.xaml` URIs built by `ThemeManager.AccentUri` (Task 2). File names must match enum `ToString()` exactly: `Dark`, `Light` × `Red`, `Green`, `Cyan`.
- Produces: 11 accent keys each — `Accent, AccentText, AccentDim, AccentBorder, SelectionAccent, AccentLogo, BgScrollThumb`, and the four `SystemColors` keys.

- [ ] **Step 1: Create the three Dark overlays (accent keys lifted from the old files)**

`Themes/Accents/Dark_Red.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options">
    <!-- Dark base, Red accent -->
    <SolidColorBrush x:Key="Accent"        Color="#EF4444" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentText"    Color="#F87171" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentDim"     Color="#3A1414" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentBorder"  Color="#7F1D1D" po:Freeze="True"/>
    <SolidColorBrush x:Key="SelectionAccent" Color="#EF4444" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentLogo"    Color="#EF4444" po:Freeze="True"/>
    <SolidColorBrush x:Key="BgScrollThumb" Color="#5BEF4444" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"                      Color="#3A1414" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"                  Color="#F87171" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"     Color="#3A1414" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="#F87171" po:Freeze="True"/>
</ResourceDictionary>
```

`Themes/Accents/Dark_Green.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options">
    <!-- Dark base, Green accent -->
    <SolidColorBrush x:Key="Accent"        Color="#22C55E" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentText"    Color="#4ADE80" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentDim"     Color="#0D2C1B" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentBorder"  Color="#166534" po:Freeze="True"/>
    <SolidColorBrush x:Key="SelectionAccent" Color="#22C55E" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentLogo"    Color="#22C55E" po:Freeze="True"/>
    <SolidColorBrush x:Key="BgScrollThumb" Color="#5B22C55E" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"                      Color="#0D2C1B" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"                  Color="#4ADE80" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"     Color="#0D2C1B" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="#4ADE80" po:Freeze="True"/>
</ResourceDictionary>
```

`Themes/Accents/Dark_Cyan.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options">
    <!-- Dark base, Cyan accent -->
    <SolidColorBrush x:Key="Accent"        Color="#22D3EE" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentText"    Color="#67E8F9" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentDim"     Color="#0C2A30" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentBorder"  Color="#155E75" po:Freeze="True"/>
    <SolidColorBrush x:Key="SelectionAccent" Color="#22D3EE" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentLogo"    Color="#22D3EE" po:Freeze="True"/>
    <SolidColorBrush x:Key="BgScrollThumb" Color="#5B22D3EE" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"                      Color="#0C2A30" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"                  Color="#67E8F9" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"     Color="#0C2A30" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="#67E8F9" po:Freeze="True"/>
</ResourceDictionary>
```

- [ ] **Step 2: Create the three Light overlays (new light-mode shades)**

These mirror how Light-amber relates to Dark-amber: saturated `Accent`/`SelectionAccent`, darkened `AccentText`/`AccentLogo` for legibility on light backgrounds, a pale `AccentDim`, a mid `AccentBorder`, and a darker scroll thumb at alpha `66`. Cyan's `SelectionAccent` is deepened (raw `#22D3EE` is too pale on the white page).

`Themes/Accents/Light_Red.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options">
    <!-- Light base, Red accent -->
    <SolidColorBrush x:Key="Accent"        Color="#EF4444" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentText"    Color="#B91C1C" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentDim"     Color="#FBDCDC" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentBorder"  Color="#E8A0A0" po:Freeze="True"/>
    <SolidColorBrush x:Key="SelectionAccent" Color="#EF4444" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentLogo"    Color="#C81E1E" po:Freeze="True"/>
    <SolidColorBrush x:Key="BgScrollThumb" Color="#66C81E1E" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"                      Color="#FBDCDC" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"                  Color="#B91C1C" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"     Color="#FBDCDC" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="#B91C1C" po:Freeze="True"/>
</ResourceDictionary>
```

`Themes/Accents/Light_Green.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options">
    <!-- Light base, Green accent -->
    <SolidColorBrush x:Key="Accent"        Color="#22C55E" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentText"    Color="#15803D" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentDim"     Color="#D6F5E0" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentBorder"  Color="#86D6A1" po:Freeze="True"/>
    <SolidColorBrush x:Key="SelectionAccent" Color="#22C55E" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentLogo"    Color="#15803D" po:Freeze="True"/>
    <SolidColorBrush x:Key="BgScrollThumb" Color="#6615803D" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"                      Color="#D6F5E0" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"                  Color="#15803D" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"     Color="#D6F5E0" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="#15803D" po:Freeze="True"/>
</ResourceDictionary>
```

`Themes/Accents/Light_Cyan.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options">
    <!-- Light base, Cyan accent -->
    <SolidColorBrush x:Key="Accent"        Color="#22D3EE" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentText"    Color="#0E7490" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentDim"     Color="#D2F1F7" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentBorder"  Color="#86D2E0" po:Freeze="True"/>
    <SolidColorBrush x:Key="SelectionAccent" Color="#0891B2" po:Freeze="True"/>
    <SolidColorBrush x:Key="AccentLogo"    Color="#0E7490" po:Freeze="True"/>
    <SolidColorBrush x:Key="BgScrollThumb" Color="#660E7490" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}"                      Color="#D2F1F7" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}"                  Color="#0E7490" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"     Color="#D2F1F7" po:Freeze="True"/>
    <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="#0E7490" po:Freeze="True"/>
</ResourceDictionary>
```

- [ ] **Step 3: Delete the obsolete colored theme files**

```bash
git rm Themes/Blood.xaml Themes/Greed.xaml Themes/Cyanotic.xaml
```

- [ ] **Step 4: Commit**

```bash
git add Themes/Accents/
git commit -m "Add 6 accent overlay dictionaries; remove legacy Blood/Greed/Cyanotic themes"
```

---

### Task 4: Settings UI — trim theme radios, add accent radio group, wire handlers

Make the app compile against the new `ThemeManager` API and expose the accent picker. This is the task that turns the whole feature on end-to-end.

**Files:**
- Modify: `MainWindow.xaml` (theme radio group + new accent group; ~lines 974–1006)
- Modify: `MainWindow.xaml.cs` (sync block ~276–282, theme handlers ~349–365)

**Interfaces:**
- Consumes: `ThemeManager.CurrentTheme`, `CurrentAccent`, `ApplyTheme`, `ApplyAccent` (Task 2); `Str_Accent*` keys (Task 5 — already referenced via `DynamicResource`, render blank until Task 5 lands, which is acceptable mid-build).

- [ ] **Step 1: Replace the theme radio block in `MainWindow.xaml`**

Find the six `RadioButton`s in the `ThemeGroup` (currently `ThemeDarkRadio` … `ThemeCyanoticRadio`, the block ending right before the `Str_Language` `TextBlock`). Replace the three legacy color radios (`ThemeBloodRadio`, `ThemeGreedRadio`, `ThemeCyanoticRadio`) — keep Dark/Light/HC — and append the new accent group. The full replacement, from the `Str_Theme` header through just before the `Str_Language` header:

```xml
                    <TextBlock Text="{DynamicResource Str_Theme}" Foreground="{DynamicResource Accent}"
                               FontFamily="{DynamicResource FontUI}" FontSize="{DynamicResource FsLabel}"
                               FontWeight="SemiBold" Margin="0,0,0,8"/>
                    <RadioButton x:Name="ThemeDarkRadio"
                                 Content="{DynamicResource Str_Theme_Dark}"
                                 GroupName="ThemeGroup"
                                 Style="{StaticResource ThemeRadio}"
                                 Checked="ThemeDarkRadio_Checked"/>
                    <RadioButton x:Name="ThemeLightRadio"
                                 Content="{DynamicResource Str_Theme_Light}"
                                 GroupName="ThemeGroup"
                                 Style="{StaticResource ThemeRadio}"
                                 Checked="ThemeLightRadio_Checked"/>
                    <RadioButton x:Name="ThemeHCRadio"
                                 Content="{DynamicResource Str_Theme_HighContrast}"
                                 GroupName="ThemeGroup"
                                 Style="{StaticResource ThemeRadio}"
                                 Checked="ThemeHCRadio_Checked"/>

                    <TextBlock Text="{DynamicResource Str_Accent}" Foreground="{DynamicResource Accent}"
                               FontFamily="{DynamicResource FontUI}" FontSize="{DynamicResource FsLabel}"
                               FontWeight="SemiBold" Margin="0,16,0,8"/>
                    <RadioButton x:Name="AccentAmberRadio"
                                 Content="{DynamicResource Str_Accent_Amber}"
                                 GroupName="AccentGroup"
                                 Style="{StaticResource ThemeRadio}"
                                 Checked="AccentAmberRadio_Checked"/>
                    <RadioButton x:Name="AccentRedRadio"
                                 Content="{DynamicResource Str_Accent_Red}"
                                 GroupName="AccentGroup"
                                 Style="{StaticResource ThemeRadio}"
                                 Checked="AccentRedRadio_Checked"/>
                    <RadioButton x:Name="AccentGreenRadio"
                                 Content="{DynamicResource Str_Accent_Green}"
                                 GroupName="AccentGroup"
                                 Style="{StaticResource ThemeRadio}"
                                 Checked="AccentGreenRadio_Checked"/>
                    <RadioButton x:Name="AccentCyanRadio"
                                 Content="{DynamicResource Str_Accent_Cyan}"
                                 GroupName="AccentGroup"
                                 Style="{StaticResource ThemeRadio}"
                                 Checked="AccentCyanRadio_Checked"/>
```

> Match the exact header `FontFamily`/`FontSize`/`FontWeight`/`Margin` attributes the original `Str_Theme` `TextBlock` used — copy them from the surrounding markup if they differ from the snippet above (the original may render its header slightly differently). The radios reuse the existing `ThemeRadio` style unchanged.

- [ ] **Step 2: Replace the theme handlers in `MainWindow.xaml.cs`**

Replace the six handlers (`ThemeDarkRadio_Checked` … `ThemeCyanoticRadio_Checked`, ~lines 349–365) with three theme handlers + four accent handlers + an enable/disable helper:

```csharp
        private void ThemeDarkRadio_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(Theme.Dark);
            UpdateAccentRadioState();
        }

        private void ThemeLightRadio_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(Theme.Light);
            UpdateAccentRadioState();
        }

        private void ThemeHCRadio_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(Theme.HighContrast);
            UpdateAccentRadioState();
        }

        private void AccentAmberRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Amber);

        private void AccentRedRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Red);

        private void AccentGreenRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Green);

        private void AccentCyanRadio_Checked(object sender, RoutedEventArgs e)
            => ThemeManager.ApplyAccent(Accent.Cyan);

        // High Contrast owns its accent; the accent picker is inert while it is active.
        private void UpdateAccentRadioState()
        {
            bool enabled = ThemeManager.CurrentTheme != Theme.HighContrast;
            AccentAmberRadio.IsEnabled = enabled;
            AccentRedRadio.IsEnabled   = enabled;
            AccentGreenRadio.IsEnabled = enabled;
            AccentCyanRadio.IsEnabled  = enabled;
        }
```

- [ ] **Step 3: Update the settings-open sync block in `SettingsBtn_Click`**

Replace the six `Theme*Radio.IsChecked = …` lines (~277–282) with the three theme syncs, the four accent syncs, and the enable refresh:

```csharp
            var curTheme  = ThemeManager.CurrentTheme;
            ThemeDarkRadio.IsChecked  = curTheme == Theme.Dark;
            ThemeLightRadio.IsChecked = curTheme == Theme.Light;
            ThemeHCRadio.IsChecked    = curTheme == Theme.HighContrast;

            var curAccent = ThemeManager.CurrentAccent;
            AccentAmberRadio.IsChecked = curAccent == Accent.Amber;
            AccentRedRadio.IsChecked   = curAccent == Accent.Red;
            AccentGreenRadio.IsChecked = curAccent == Accent.Green;
            AccentCyanRadio.IsChecked  = curAccent == Accent.Cyan;
            UpdateAccentRadioState();
```

> `Theme`/`Accent` are in `Scalpel.Services`. `MainWindow` already brings the namespace into scope (it calls `ThemeManager.Apply`/`Theme.Dark` today). If the build reports `Theme`/`Accent` unresolved, add `using Scalpel.Services;` to the top of `MainWindow.xaml.cs`.

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build`
Expected: BUILD SUCCEEDED, 0 errors. (Accent radio labels render blank until Task 5 supplies the strings — cosmetic, not a build error.)

> If you get `NETSDK1047` ("no target for net48/win7-x64") from a prior publish pinning the RID, re-run **with** restore: `dotnet build` (drop any `--no-restore`).

- [ ] **Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "Settings: split theme radios from new accent picker; wire two-axis handlers"
```

---

### Task 5: Localization — add accent strings, remove legacy theme strings

Supply the `Str_Accent*` keys in all six locales and drop the now-unused `Str_Theme_Blood/Greed/Cyanotic`. Keeping the key sets identical across locales is mandatory.

**Files:**
- Modify: `Strings/en-US.xaml`, `Strings/es.xaml`, `Strings/zh-TW.xaml`, `Strings/zh-CN.xaml`, `Strings/bn.xaml`, `Strings/tr-TR.xaml`

**Interfaces:**
- Consumes: referenced by `DynamicResource Str_Accent`, `Str_Accent_Amber/Red/Green/Cyan` in `MainWindow.xaml` (Task 4).

- [ ] **Step 1: In every locale file, remove the three legacy theme strings**

Delete these three lines from **each** of the six files (values differ per locale; keys are identical):

```xml
    <sys:String x:Key="Str_Theme_Blood">…</sys:String>
    <sys:String x:Key="Str_Theme_Greed">…</sys:String>
    <sys:String x:Key="Str_Theme_Cyanotic">…</sys:String>
```

- [ ] **Step 2: In every locale file, add the accent header + four accent names**

Insert immediately after the `Str_Theme_HighContrast` line in each file, using the per-locale values below.

`Strings/en-US.xaml`:

```xml
    <sys:String x:Key="Str_Accent">ACCENT</sys:String>
    <sys:String x:Key="Str_Accent_Amber">Amber</sys:String>
    <sys:String x:Key="Str_Accent_Red">Red</sys:String>
    <sys:String x:Key="Str_Accent_Green">Green</sys:String>
    <sys:String x:Key="Str_Accent_Cyan">Cyan</sys:String>
```

`Strings/es.xaml`:

```xml
    <sys:String x:Key="Str_Accent">ACENTO</sys:String>
    <sys:String x:Key="Str_Accent_Amber">Ámbar</sys:String>
    <sys:String x:Key="Str_Accent_Red">Rojo</sys:String>
    <sys:String x:Key="Str_Accent_Green">Verde</sys:String>
    <sys:String x:Key="Str_Accent_Cyan">Cian</sys:String>
```

`Strings/zh-TW.xaml`:

```xml
    <sys:String x:Key="Str_Accent">強調色</sys:String>
    <sys:String x:Key="Str_Accent_Amber">琥珀色</sys:String>
    <sys:String x:Key="Str_Accent_Red">紅色</sys:String>
    <sys:String x:Key="Str_Accent_Green">綠色</sys:String>
    <sys:String x:Key="Str_Accent_Cyan">青色</sys:String>
```

`Strings/zh-CN.xaml`:

```xml
    <sys:String x:Key="Str_Accent">强调色</sys:String>
    <sys:String x:Key="Str_Accent_Amber">琥珀色</sys:String>
    <sys:String x:Key="Str_Accent_Red">红色</sys:String>
    <sys:String x:Key="Str_Accent_Green">绿色</sys:String>
    <sys:String x:Key="Str_Accent_Cyan">青色</sys:String>
```

`Strings/bn.xaml`:

```xml
    <sys:String x:Key="Str_Accent">অ্যাকসেন্ট</sys:String>
    <sys:String x:Key="Str_Accent_Amber">অ্যাম্বার</sys:String>
    <sys:String x:Key="Str_Accent_Red">লাল</sys:String>
    <sys:String x:Key="Str_Accent_Green">সবুজ</sys:String>
    <sys:String x:Key="Str_Accent_Cyan">সায়ান</sys:String>
```

`Strings/tr-TR.xaml`:

```xml
    <sys:String x:Key="Str_Accent">VURGU</sys:String>
    <sys:String x:Key="Str_Accent_Amber">Kehribar</sys:String>
    <sys:String x:Key="Str_Accent_Red">Kırmızı</sys:String>
    <sys:String x:Key="Str_Accent_Green">Yeşil</sys:String>
    <sys:String x:Key="Str_Accent_Cyan">Camgöbeği</sys:String>
```

- [ ] **Step 3: Verify key parity across locales**

Run (Bash tool):

```bash
for f in Strings/en-US.xaml Strings/es.xaml Strings/zh-TW.xaml Strings/zh-CN.xaml Strings/bn.xaml Strings/tr-TR.xaml; do
  echo "$f: $(grep -c 'Str_Accent' "$f") accent keys, $(grep -c 'Str_Theme_Blood\|Str_Theme_Greed\|Str_Theme_Cyanotic' "$f") legacy"
done
```

Expected: every file reports `5 accent keys, 0 legacy`.

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: BUILD SUCCEEDED. Accent radio labels now render in every language.

- [ ] **Step 5: Commit**

```bash
git add Strings/
git commit -m "Localize accent picker strings; drop legacy theme strings (6 locales)"
```

---

### Task 6: Full build, test, and manual verification matrix

Confirm the feature end-to-end and that nothing regressed.

**Files:** none (verification only).

- [ ] **Step 1: Clean build + full test suite**

Run: `dotnet build` then `dotnet test`
Expected: BUILD SUCCEEDED; all tests pass, including `ThemeMigrationTests`.

- [ ] **Step 2: Launch and walk the matrix**

Run: `dotnet run` (or launch the built EXE).

Verify in Settings:
- Theme = Dark, accent in {Amber, Red, Green, Cyan}: backgrounds stay dark; accent (toolbar highlight, logo, selection ring, scrollbar thumb) changes hue; **base never flips**.
- Theme = Light, same four accents: backgrounds stay light; accent text stays legible on light panels; selection ring readable on the white PDF page (especially Cyan).
- Switch accent repeatedly, then switch Dark↔Light: the chosen accent is preserved across base switches.
- Theme = High Contrast: fixed amber/white; the four accent radios are **greyed/disabled**; leaving HC for Dark/Light restores the previously chosen accent.
- Titlebar (DWM): dark for Dark and High Contrast, light for Light — independent of accent.

- [ ] **Step 3: Verify migration of an existing install**

With the app closed, set the legacy registry value and confirm it migrates:

```powershell
# Simulate an old install that was on "Blood", with no Accent key yet.
reg add "HKCU\Software\Scalpel\Settings" /v Theme /t REG_SZ /d Blood /f
reg delete "HKCU\Software\Scalpel\Settings" /v Accent /f 2>$null
```

Launch the app. Expected: opens as **Dark + Red** (looks identical to the old Blood). Close, then check the values were normalized:

```powershell
reg query "HKCU\Software\Scalpel\Settings" /v Theme    # -> Dark
reg query "HKCU\Software\Scalpel\Settings" /v Accent   # -> Red
```

- [ ] **Step 4: Update docs**

`docs/UI-REFERENCE.md` and `docs/DESIGN-SYSTEM.md` reference the six-theme model and the `Theme` enum. Update the theme/accent description and the Settings control list to the two-axis model (Dark/Light/HighContrast × Amber/Red/Green/Cyan; HC ignores accent). `CLAUDE.md`'s ThemeManager paragraph names the six themes and "identical key set" rule — update it to describe base files + accent overlays and the per-layer identical-key-set rule.

- [ ] **Step 5: Final commit**

```bash
git add docs/ CLAUDE.md
git commit -m "Docs: describe two-axis theme/accent model"
```

---

## Self-Review

**Spec coverage:**
- Two orthogonal axes + separate persistence → Tasks 1, 2. ✓
- Migration of legacy values → Task 1 (logic, tested), Task 2 (`Initialize` wiring + write-back), Task 6 Step 3 (verified). ✓
- Theme-aware accent overlays, Amber=no-overlay, HC fixed → Task 2 loader + Task 3 files. ✓
- Light-mode shades for Red/Green/Cyan → Task 3 Step 2. ✓
- Settings UI: 3 theme radios + 4 accent radios, HC disables accents → Task 4. ✓
- Localization add/remove across 6 locales → Task 5. ✓
- Remove Blood/Greed/Cyanotic.xaml → Task 3 Step 3. ✓
- DWM keyed off base only → Task 2 (`ApplyDwm`/`ApplyInternal`). ✓
- Verification matrix → Task 6. ✓

**Placeholder scan:** No TBD/TODO; all code blocks are complete; exact hexes provided (flagged tunable, not blank).

**Type consistency:** `ThemeMigration.Resolve(string?, string?) -> (Theme, Accent)` defined Task 1, consumed Task 2. `ApplyTheme`/`ApplyAccent`/`CurrentTheme`/`CurrentAccent` defined Task 2, consumed Task 4. Accent overlay file names `{Theme}_{Accent}.xaml` (Task 2 `AccentUri`) match the files created in Task 3 (`Dark_Red`, `Light_Cyan`, …) via `enum.ToString()`. `Str_Accent*` keys referenced in Task 4 are defined in Task 5 (build stays green; labels blank until then — noted).

**Note on TDD scope:** Only Task 1 (pure migration) carries unit tests — WPF resource-dictionary loading and XAML/registry wiring aren't unit-testable here and are covered by build + the Task 6 manual matrix, consistent with the repo's existing test boundaries.
```
