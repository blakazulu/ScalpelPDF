# Scalpel — System Design (Ribbon / Clinical)

> Single source of truth for the redesigned UI. Every layout, component, and color
> change must be reflected here first, then implemented against these values.
> Supersedes the toolbar layout described in `docs/DESIGN-SYSTEM.md` (which still
> documents the `_Shared.xaml` design-system mechanics: fonts, icons, type scale).

This redesign replaces the old **two-band** chrome (mode-tab strip **+** tool toolbar)
with a single **Microsoft-Office-style ribbon**, and adopts the **"Clinical"** visual
language (cool steel chrome, surgical-red accent) as the default look. All three base
themes (Dark / Light / High Contrast) and all four accents (Amber / Red / Green / Cyan)
are preserved — the ribbon and the new tokens render correctly in every combination.

---

## 1. Layout

```
┌───────────────────────────────────────────────────────────────────────────┐
│ ● Scalpel PDF │ [Open][Save][Print][Undo]   Acquisition.pdf      — □ ✕     │  Row 0  Title bar + QAT   (ChromeBg, 40px)
├───────────────────────────────────────────────────────────────────────────┤
│ View · Edit · Pages · Sign                  Tools▾ About What's-New ⌕ ⚙     │  Row 1  Ribbon tab strip  (RibbonBg, auto)
├───────────────────────────────────────────────────────────────────────────┤
│  ▭ ▤ ▥ ▦   │  ⤢   ⟳        contextual command groups, captioned           │  Row 2  Ribbon band       (RibbonBand, auto)
│  LAYOUT    │  DISPLAY                                                       │
├──────────┬────────────────────────────────────────────────────────────────┤
│ pages    │                                                                 │  Row 3  Content           (sidebar + canvas)
│ sidebar  │                  document canvas                                │
├──────────┴────────────────────────────────────────────────────────────────┤
│ Ready · 12 pages · 1.4 MB        PORTABLE        v1.5.1   ⊖ 100% ⊕          │  Row 4  Status bar + zoom (ChromeBg, 30px)
└───────────────────────────────────────────────────────────────────────────┘
```

**Window root** — `Grid` with 5 rows: `40 / Auto / Auto / * / 30`.

### Row 0 — Title bar + Quick Access Toolbar
- Background `ChromeBg`; draggable (`TitleBar_MouseLeftButtonDown`).
- Left: `● Scalpel` + `PDF` (`AccentLogo`) wordmark, then a 1px `BorderDim` divider, then the
  **QAT** — icon-only `QatButton`s: **Open, Save, Print, Undo** (reuse `Open_Click`,
  `Save_Click`, `Print_Click`, `Undo_Click`).
- Center: `FileNameLabel` (`ChromeTextDim`, tabular).
- Right: window chrome **min / max / close** (`ChromeButton` / `ChromeCloseButton`).

### Row 1 — Ribbon tab strip
- Background `RibbonBg`; bottom 1px `BorderDim`.
- Left: the four mode tabs as grouped `RadioButton`s (`RibbonTab` style, `GroupName="AppModeTabs"`,
  `Checked="ModeTab_Checked"`, `Tag="View|Edit|Pages|Sign"`). The active tab adopts the `RibbonBand`
  fill and shows a 2.5px `Accent` bar along its **top** edge so it reads as one surface with the band.
- Right: `Tools ▾` (context menu), `About`, `What's New` (`StudioModeTabButton`), and icon buttons
  `Search` + `Settings` (`ChromeTextButton` / `StudioIconButton`).

### Row 2 — Ribbon band (contextual)
- Background `RibbonBand`; bottom 1px `BorderDim`; horizontally scrollable on narrow windows.
- Holds the four mode panels (`ModePanelView/Edit/Pages/Sign`); `SetMode` shows exactly one.
- Each panel is a row of **groups**. A group = a horizontal row of command buttons + a small
  uppercase **caption** (`RibbonGroupLabel`, `FsSidebarLabel`). Groups are separated by 1px
  `BorderDim` dividers.

| Tab   | Groups (caption → buttons) |
|-------|----------------------------|
| View  | **Layout** → Single·Continuous·Two-page·Grid · **Display** → Fit·Rotate |
| Edit  | **Tools** → Select·Text·Highlight·Draw·Image·Crop · **History** → Undo·Clear |
| Pages | **Organize** → Merge·Extract·Insert·Delete · **Arrange** → Move-up·Move-down·Rotate |
| Sign  | **Signature** → Place signature · **Forms** → Fill form |

- **Command button** (`RibbonButton`): vertical, **icon over label**, `min-width 80`, single-line
  label (no wrap), `RibbonBtnHover` on hover. Active tools (`Select`…`Crop`) are highlighted by
  `SetTool` via `SetResourceReference(Background→AccentDim, Foreground→Accent)`, so icon **and**
  label recolor to the accent.
- **View-mode toggles** (`RibbonToggle`): same vertical shape; `IsChecked` → `AccentDim` fill +
  `AccentText`.
- **Destructive** buttons (`Clear`, `Delete`): `RibbonButton` with `Foreground="{DynamicResource DangerRed}"`.

### Row 3 — Content
Unchanged: page-thumbnail sidebar (collapsible, with the 24px toggle strip) + `GridSplitter` +
document canvas / drop zone.

### Row 4 — Status bar + zoom
- Background `ChromeBg`.
- Left: `StatusText`. Center: `PortableBadge` + Install. Right: `VersionLabel` · © link, then the
  **zoom cluster** relocated from the old toolbar: `ZoomOutBtn` ⊖ · `ZoomBox` (Fit/50–200%) · `ZoomInBtn` ⊕.

### Motion (ribbon tab switch)
- **Indicator glide** — the active-tab accent bar animates its X position/width to the new tab.
- **Command cascade** — on tab switch the new band's buttons stagger in (rise + scale-up + de-blur,
  slight overshoot), left→right (~48ms apart).
- `prefers-reduced-motion` collapses both to an instant swap. (WPF: `Storyboard` on tab
  `Checked`; HTML mock encodes the reference timing.)

---

## 2. Typography (`_Shared.xaml`)

`FontUI` = **Geist** (→ Segoe UI Variable → Segoe UI). `FontIcon` = **Tabler** subset.
Type scale (doubles): `FsDialogTitle 16 · FsTab 13 · FsButton 12.5 · FsBody 13 · FsContext 11.5 ·
FsSidebarLabel 10 · FsStatus 10.5`. Ribbon command labels use `FsContext` (11.5); group captions use
`FsSidebarLabel` (10, uppercase). **Minimum on-screen text size is 10** (captions); body/labels ≥ 11.5.

---

## 3. Color tokens

Existing tokens are unchanged; the redesign **adds** the semantic tokens below to each base theme.
Accent overlays continue to override only the accent keys.

### New semantic tokens (added to every base theme)

| Token | Role |
|-------|------|
| `ChromeBg` | Title bar + status bar background |
| `ChromeText` | Primary text/icons on chrome |
| `ChromeTextDim` | Secondary text on chrome (filename, version) |
| `ChromeHover` | Hover fill for chrome / QAT buttons |
| `RibbonBg` | Ribbon tab strip background |
| `RibbonBand` | Ribbon command-band background (active tab fill) |
| `RibbonGroupLabel` | Group caption text |
| `RibbonBtnHover` | Ribbon command-button hover fill |
| `ZoomTrack` | Zoom control track / inactive rail |

### Base-theme values

| Token | Dark | Light (Clinical) | High Contrast |
|-------|------|------------------|---------------|
| `BgCanvas` | `#0A0B0E` | `#DFE3E8` | `#000000` |
| `BgSidebar` | `#0D0F12` | `#ECEEF1` | `#000000` |
| `BgPanel` | `#181B21` | `#FDFDFE` | `#0A0A0A` |
| `BgControl` | `#23272F` | `#EEF0F3` | `#141414` |
| `BgHover` | `#2A2E36` | `#E4E8EC` | `#1F1F1F` |
| `TabCloseHover` (tab chip close button hover) | `#3A3F4A` | `#D3D9E0` | `#3A3A3A` |
| `BorderDim` | `#20242B` | `#E0E3E8` | `#FFFFFF` |
| `TextPrimary` | `#E7E9EE` | `#1A1D22` | `#FFFFFF` |
| `TextSecondary` | `#7C818C` | `#525A64` | `#E0E0E0` |
| `TextFooter` | `#5B616C` | `#9AA1AB` | `#BFBFBF` |
| `DangerRed` | `#EF4444` | `#DC2626` | `#FF5555` |
| **`ChromeBg`** | `#13161C` | `#2C3A4C` | `#000000` |
| **`ChromeText`** | `#E8EAEF` | `#FFFFFF` | `#FFFFFF` |
| **`ChromeTextDim`** | `#8990A0` | `#AEB9C7` | `#E0E0E0` |
| **`ChromeHover`** | `#2A2E36` | `#3C4C60` | `#1F1F1F` |
| **`RibbonBg`** | `#1B1F27` | `#FFFFFF` | `#000000` |
| **`RibbonBand`** | `#171A21` | `#F2F5F8` | `#0A0A0A` |
| **`RibbonGroupLabel`** | `#5C6270` | `#8A93A0` | `#BFBFBF` |
| **`RibbonBtnHover`** | `#232833` | `#E9EDF1` | `#1F1F1F` |
| **`ZoomTrack`** | `#2A2E36` | `#46566B` | `#FFFFFF` |

### Accent values (overlay keys)

Amber is built into each base theme (no overlay). Red is retuned to **surgical red** to carry the
Clinical identity. Green/Cyan unchanged. High Contrast ignores accents (fixed `#FFB000`).

| Accent key | Amber (Dark) | Amber (Light) | Red (Dark) | Red (Light) | Green (Dark) | Green (Light) | Cyan (Dark) | Cyan (Light) |
|------------|------|------|------|------|------|------|------|------|
| `Accent` | `#F2A93B` | `#F2A93B` | `#F04458` | `#E11D38` | `#22C55E` | `#22C55E` | `#22D3EE` | `#22D3EE` |
| `AccentText` | `#F6C170` | `#9A6B14` | `#FF8A98` | `#B11228` | `#4ADE80` | `#15803D` | `#67E8F9` | `#0E7490` |
| `AccentDim` | `#36280D` | `#FBEAD0` | `#3A1219` | `#FDEAEC` | `#0D2C1B` | `#D6F5E0` | `#0C2A30` | `#D2F1F7` |
| `AccentBorder` | `#B07515` | `#E0B36A` | `#8C1C2C` | `#E11D38` | `#166534` | `#86D6A1` | `#155E75` | `#86D2E0` |
| `AccentLogo` | `#F2A93B` | `#C07F12` | `#F04458` | `#C8122B` | `#22C55E` | `#15803D` | `#22D3EE` | `#0E7490` |

Default fresh-install look: **Light + Red** (`ThemeMigration.Resolve` fallback) → the Clinical mock.
Existing users keep their persisted Theme/Accent.

---

## 4. Component styles (`_Shared.xaml`)

Retained: `StudioToolButton`, `StudioPrimaryButton`, `StudioDangerButton`, `StudioModeTabButton`,
`StudioIconButton`, `StudioPill`, `StudioSwatch`, `StudioOverlayCard`, `SettingsSectionToggle`,
plus implicit `ScrollBar` / `ContextMenu` / `MenuItem` / `Separator`.

Added for the ribbon:

| Style | Target | Notes |
|-------|--------|-------|
| `QatButton` | Button | Icon-only 30×30 on chrome; `ChromeText` fg, `ChromeHover` hover. |
| `ChromeTextButton` | Button | Icon+label on chrome (Search/Settings); `ChromeText`. |
| `RibbonTab` | ToggleButton | Tab connected to band: active = `RibbonBand` fill + top `Accent` bar + `AccentText`. |
| `RibbonButton` | Button | Vertical icon-over-label, `min-width 80`, single line; `Background`/`Foreground` template-bound so `SetTool` highlight works; hover `RibbonBtnHover`. |
| `RibbonToggle` | ToggleButton | Vertical command toggle (view modes); `IsChecked` → `AccentDim` + `AccentText`. |

`ChromeButton` (window chrome) is repointed from `TextPrimary`/`BgHover` to `ChromeText`/`ChromeHover`
so it reads correctly on the steel Light title bar.

Group captions are defined as non-localized `Grp_*` strings in `_Shared.xaml`
(`Grp_Layout`, `Grp_Display`, `Grp_Tools`, `Grp_History`, `Grp_Organize`, `Grp_Arrange`,
`Grp_Signature`, `Grp_Forms`) — kept out of the locale files to avoid a partial-key blank-out;
localization of captions is a tracked follow-up.

---

## 5. Code-behind contract (do not break)

The ribbon is a pure **view** restructure. The following must keep their `x:Name`s and handlers:

- Mode: `ModeViewTab/EditTab/PagesTab/SignTab` (tabs), `ModePanelView/Edit/Pages/Sign` (bands),
  `SetMode(AppMode)`, `ModeTab_Checked`, `_suppressModeEvents`.
- Tools: `ToolSelectBtn/TextBtn/HighlightBtn/DrawBtn/ImageBtn/CropBtn/SignatureBtn` + `SetTool`
  (highlight via `SetResourceReference`).
- View toggles: `ViewSingleBtn/ContinuousBtn/TwoPageBtn/GridBtn`.
- Zoom: `ZoomBox` (resolved by `FindName("ZoomBox")`), `ZoomOutBtn`, `ZoomInBtn`,
  `ZoomBox_SelectionChanged`, `SyncZoomBox`.
- All Click handlers listed in the table above remain wired to the same methods.

---

## 6. Brand & assets — what lives where

### Logo / icon

The app icon was redesigned to match the Clinical language: a **steel Fluent squircle**
holding a tilted white document, with a **scalpel whose cutting edge glows surgical-red**
(steel `#2C3A4C` + red `#E11D38` + paper). A **simplified glyph** (steel tile + page + one
bold diagonal — steel handle into a red edge) is substituted at small sizes (≤ 56px) so the
mark stays legible in the taskbar / alt-tab.

**Pipeline (`branding/` is the source of truth):**

| File | Purpose |
|------|---------|
| `branding/scalpel-icon.svg` | Vector source — the full mark (used > 56px) |
| `branding/scalpel-glyph.svg` | Vector source — the simplified small-size glyph (≤ 56px) |
| `branding/scalpel-master-1024.png` | 1024 raster of the full mark (Pillow can't read SVG, so this is rendered from the SVG and committed) |
| `branding/scalpel-glyph-master-1024.png` | 1024 raster of the glyph |
| `branding/scalpel_logo.py` | Generator — slices both masters into every asset and (with `--export`) deploys them |
| `branding/scalpel.ico`, `scalpel-1024.png`, `tiles/`, `preview_*.png` | Generated outputs / QA previews |

**Regenerate:** edit a `.svg` → re-render its `*-master-1024.png` with headless Chrome
(`--default-background-color=00000000 --window-size=1024,1024 --screenshot=…`, pointing at a
1024 wrapper such as `design-mockups/logo/master.html` / `glyphmaster.html`) → run
`python branding/scalpel_logo.py --export` from the repo root.

**Deployed destinations (written by `--export`):**

| Location | Asset(s) | Used by |
|----------|----------|---------|
| `Resources/scalpel.ico` | multi-res `.ico` (16/24/32/48 = glyph, 64/128/256 = full) | EXE icon (`<ApplicationIcon>` in `Scalpel.csproj`) + WPF window icon (`MainWindow.xaml` `Icon=`) |
| `packaging/Assets/Square44x44Logo.png` … `Square310x310Logo.png`, `StoreLogo.png`, `Wide310x150Logo.png`, `SplashScreen.png` | MSIX tiles (44 / 50 use the glyph; 71+ use the full mark) | the MSIX/Store package (`packaging/AppxManifest.xml`) |
| `store-assets/StoreListingLogo_300x300.png` | 300×300 Store listing logo | Microsoft Store listing |

### Screenshots

`store-assets/screenshots/*.png` — six **1920×1080** PNGs of the new design across themes:
`01-view-light` (Clinical hero), `02-edit-dark`, `03-pages-light`, `04-sign-dark`,
`05-highcontrast`, `06-grid-green`. Rendered with headless Chrome from the theme-parametrised
`design-mockups/store/shot.html` (query params drive theme/accent/mode); content kept in the top
two-thirds, no marketing text (per `docs/MS-STORE-REQUIREMENTS.md`).

### Design references (`design-mockups/`)

Non-shipping HTML references for the redesign (kept for comparison, not built into the app):
- `5-ribbon-clinical.html` — the interactive ribbon UI reference (the approved direction).
- `store/shot.html` — theme-parametrised screen used to render the store screenshots.
- `logo/` — `concepts.html` (the 3 logo directions), `chosen.html`, `master.html`,
  `glyph.html`, `glyphmaster.html`, `icon300.html` (icon render wrappers).
