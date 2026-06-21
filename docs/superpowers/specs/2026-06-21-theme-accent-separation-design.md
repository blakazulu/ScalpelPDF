# Theme / Accent Separation — Design

**Date:** 2026-06-21
**Status:** Approved (design); pending implementation plan

## Problem

The app has a single `Theme` enum with six values: `Dark, Light, HighContrast, Blood, Greed, Cyanotic`. Four of these (`Dark, Blood, Greed, Cyanotic`) share identical dark backgrounds and differ **only** in accent hue (amber, red, green, cyan). `Light` and `HighContrast` are distinct base looks, each hard-wired to the amber accent.

Because base look and accent are conflated into one enum, choosing a colored accent (Blood/Greed/Cyanotic) silently forces dark backgrounds. There is no way to get, e.g., **Light + red accent**. Users report that "changing the accent always reverts to dark mode."

## Goal

Split the one axis into two orthogonal axes:

- **Base theme** — `Dark`, `Light`, `HighContrast` (background/text look)
- **Accent** — `Amber`, `Red`, `Green`, `Cyan` (hue)

Accent applies to **Dark and Light only**. **High Contrast keeps its own fixed high-visibility amber/white accent** and ignores the accent picker (accessibility intent). Changing the accent must never change the base theme, and vice versa.

## Decisions (from brainstorming)

1. **High Contrast** = third base theme with a fixed accent; the accent picker is disabled while HC is active.
2. **Accents** = the existing four hues, surfaced with neutral color names: Amber (`#F2A93B`, current default), Red (`#EF4444`, was Blood), Green (`#22C55E`, was Greed), Cyan (`#22D3EE`, was Cyanotic).
3. **Accent picker UI** = a radio-button list styled exactly like the existing theme radios (consistent with current Settings), placed under the theme radios.

## Architecture — Theme-aware accent overlays (chosen)

The ~11 accent-affecting resource keys are split out as a thin overlay applied on top of a complete base dictionary.

**Accent-affecting keys** (everything else is base-only):
`Accent`, `AccentText`, `AccentDim`, `AccentBorder`, `SelectionAccent`, `AccentLogo`, `BgScrollThumb`, and the four `SystemColors` keys (`HighlightBrushKey` = AccentDim color, `HighlightTextBrushKey` = AccentText color, plus the two `InactiveSelectionHighlight…` variants).

**Base-only keys:** `BgCanvas, BgDark, BgPanel, BgSidebar, BgModal, BgHover, BgControl, BorderDim, TextPrimary, TextSecondary, TextDim, TextFooter, DangerRed, BgDragHandle, BgOverlay, GrainOpacity`.

### Files

- **Base themes (complete, 3 files):** `Themes/Dark.xaml`, `Themes/Light.xaml`, `Themes/HighContrast.xaml`.
  - `Dark.xaml` and `Light.xaml` keep their built-in **Amber** accent as the default (so Amber needs no overlay).
  - `HighContrast.xaml` keeps its fixed amber/white accent baked in (never overlaid).
- **Accent overlays (stripped to the ~11 accent keys, 6 files):**
  `Themes/Accents/Dark_Red.xaml`, `Dark_Green.xaml`, `Dark_Cyan.xaml`,
  `Themes/Accents/Light_Red.xaml`, `Light_Green.xaml`, `Light_Cyan.xaml`.
  - Dark overlays are the accent keys lifted out of the old `Blood`/`Greed`/`Cyanotic` files.
  - Light overlays are **new** — light-mode shades authored to mirror how Light-amber relates to Dark-amber.
- **Removed:** `Themes/Blood.xaml`, `Themes/Greed.xaml`, `Themes/Cyanotic.xaml`.

> Invariant (per CLAUDE.md): every file in a layer group must define the **identical key set**. All 3 base files share the full key set; all 6 accent overlays share the accent-key set. Base+overlay together cover every key, and a fresh `LoadDict` re-applies base then overlay, overwriting all keys on every switch.

### Light-mode accent shades (to author)

Reference — how Light-amber differs from Dark-amber:

| Key | Dark-amber | Light-amber |
|---|---|---|
| `Accent` | `#F2A93B` | `#F2A93B` (saturated, unchanged) |
| `SelectionAccent` | `#F2A93B` | `#F2A93B` (saturated — must stay readable on white page) |
| `AccentText` | `#F6C170` (light) | `#9A6B14` (dark, readable on light bg) |
| `AccentDim` | `#36280D` (dark tint) | `#FBEAD0` (pale tint) |
| `AccentBorder` | `#B07515` | `#E0B36A` |
| `AccentLogo` | `#F2A93B` | `#C07F12` (darker) |
| `BgScrollThumb` | `#5B<hue>` (alpha 5B) | `#66<darker>` (alpha 66) |
| `Highlight` / `HighlightText` | = AccentDim / AccentText | = AccentDim / AccentText |

The same transform is applied to Red/Green/Cyan to produce their Light overlays: keep `Accent`/`SelectionAccent` at the saturated hue; darken `AccentText`/`AccentLogo` for legibility on light backgrounds; make `AccentDim` a pale tint and `AccentBorder` a mid shade; darken the scroll thumb and raise its alpha to `66`. Exact hexes are chosen during implementation and visually checked.

## ThemeManager API

```csharp
internal enum Theme  { Dark, Light, HighContrast }
internal enum Accent { Amber, Red, Green, Cyan }

static Theme  CurrentTheme  { get; }
static Accent CurrentAccent { get; }

static void Initialize();              // restore + migrate saved values
static void ApplyTheme(Theme t);       // sets base, keeps current accent, persists
static void ApplyAccent(Accent a);     // sets accent, keeps current base, persists
static void ApplyDwm(IntPtr hwnd);     // dark titlebar unless CurrentTheme == Light
static event Action? ThemeChanged;     // fired after either apply
```

- Both `ApplyTheme`/`ApplyAccent` funnel into `LoadDict(CurrentTheme, CurrentAccent)` and raise `ThemeChanged`.
- `LoadDict(theme, accent)`:
  1. Apply the base dict (`Dark`/`Light`/`HighContrast`) in place to `MergedDictionaries[0]` (existing per-key update — preserves the no-structural-change behavior that avoids `ResourceReferenceKeyNotFoundException`).
  2. If `theme ∈ {Dark, Light}` **and** `accent != Amber`: overlay `Accents/{theme}_{accent}.xaml` keys on top (same in-place per-key update).
  3. HC and Amber take no overlay.
  4. Keep the existing `SystemIdle` `RefreshIcons` nudge.
- DWM: dark unless `CurrentTheme == Light` (HC stays dark). Unchanged logic, keyed off base only.

### Migration (in `Initialize`)

Read the legacy `Theme` setting and map to the new pair, then read the new `Accent` setting if present:

| Legacy `Theme` value | → `Theme` | → `Accent` |
|---|---|---|
| `Dark` | `Dark` | `Amber` |
| `Light` | `Light` | `Amber` |
| `HighContrast` | `HighContrast` | `Amber` |
| `Blood` | `Dark` | `Red` |
| `Greed` | `Dark` | `Green` |
| `Cyanotic` | `Dark` | `Cyan` |
| (unparsable) | `Dark` | `Amber` |

After migration, write back the normalized `Theme` and `Accent` keys so subsequent loads are clean. A standalone `Accent` setting, when present and valid, wins over the value derived from a legacy theme name.

## Settings UI (`MainWindow.xaml` / `.cs`)

- **Theme group** (`GroupName="ThemeGroup"`) shrinks to three radios: `ThemeDarkRadio`, `ThemeLightRadio`, `ThemeHCRadio`. Remove `ThemeBloodRadio`, `ThemeGreedRadio`, `ThemeCyanoticRadio` and their handlers.
- **New Accent group** (`GroupName="AccentGroup"`) under a `Str_Accent` header: `AccentAmberRadio`, `AccentRedRadio`, `AccentGreenRadio`, `AccentCyanRadio`, same `ThemeRadio` style, with `Accent…Radio_Checked` handlers calling `ThemeManager.ApplyAccent(...)`.
- **HC disables accents:** when `ThemeHCRadio` is checked, the four accent radios are disabled (`IsEnabled=false`, greyed). Re-enabled for Dark/Light. Handled in the theme handlers and the settings-open sync.
- **Sync block** (currently `MainWindow.xaml.cs:276–282`): set both the theme and accent radio `IsChecked` from `CurrentTheme`/`CurrentAccent`, and set accent-group enabled state from whether base is HC. Mirror the existing pattern (direct `IsChecked` assignment; handlers re-applying the same value are idempotent).
- `OnThemeChanged` continues to call `RefreshSelectionAccent()` so live selection/crop visuals re-tint.

## Localization

- Add to **all six** locale files (`Strings/*.xaml`): `Str_Accent` (section header) and `Str_Accent_Amber`, `Str_Accent_Red`, `Str_Accent_Green`, `Str_Accent_Cyan`.
- Remove the now-unused `Str_Theme_Blood`, `Str_Theme_Greed`, `Str_Theme_Cyanotic` from all six files.
- Keep `Str_Theme_Dark`, `Str_Theme_Light`, `Str_Theme_HighContrast`.
- Per the locale invariant: every key must exist in every locale file or a `DynamicResource` blanks out in that language.

## Out of scope (YAGNI)

- No custom/user-defined accent colors (fixed set of 4).
- No accent support for High Contrast.
- No color-swatch UI (radio list chosen).
- No change to the theme/accent persistence location (HKCU registry).

## Testing / verification

- Build (`dotnet build`) clean.
- Manual matrix check: Dark×{Amber,Red,Green,Cyan} and Light×{Amber,Red,Green,Cyan} each render with correct backgrounds **and** correct accent shades (text legible, no stale keys); High Contrast renders fixed amber and greys the accent radios.
- Switching accent never changes the base; switching base preserves the chosen accent (and restores it when leaving HC).
- Migration: a registry with legacy `Theme=Blood` (and no `Accent` key) loads as Dark + Red on first run, then persists `Theme=Dark`, `Accent=Red`.
- DWM titlebar dark for Dark/HC, light for Light, regardless of accent.
```
