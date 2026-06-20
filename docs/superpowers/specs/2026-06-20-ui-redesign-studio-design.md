# Scalpel UI Redesign — "Studio" Design Spec

**Date:** 2026-06-20
**Status:** Approved (design) — ready for implementation planning
**Scope:** Full UX restructure + visual redesign of the main window, contextual bars, overlays, and dialogs. No changes to PDF processing pipelines, the installer/self-install logic, the pdfium integrity check, or persistence formats.

---

## 1. Goal

Make Scalpel **as simple as possible to use**, with a distinctive, premium visual identity. The current UI crams ~25 icon-only buttons into a single top row with no labels and no visible structure — high cognitive load, low discoverability. This redesign replaces that with a **task-based mode-tab** layout and a coherent design language ("Studio") applied across all themes.

Non-goals: changing what the app *does*, its file formats, its libraries, or its install model. This is a presentation-layer redesign.

---

## 2. Design language: "Studio"

A quiet, document-first "precision instrument" aesthetic. The chrome recedes so the page is the hero. Confident warm accent, generous spacing, soft depth, crisp geometric icons.

### 2.1 Color tokens

Tokens are defined per theme as `SolidColorBrush` resources (same mechanism as today — `Themes/*.xaml`, index 0 of merged dictionaries, updated in place per key). The **token key names below are the contract**; every theme file must define every key.

**Studio Dark (default theme):**

| Token | Hex | Use |
|---|---|---|
| `BgCanvas` | `#0A0B0E` | Page viewport background (behind the document) |
| `BgDark` | `#14161A` | Window background |
| `BgPanel` | `#181B21` | Toolbar / status bar |
| `BgSidebar` | `#0D0F12` | Sidebar, tab strip, title bar |
| `BgControl` | `#23272F` | Default button/control fill |
| `BgHover` | `#2A2E36` | Hover fill |
| `BorderDim` | `#20242B` | Hairline dividers, borders |
| `Accent` | `#F2A93B` | Active tab, Save, active tool, selection, focus |
| `AccentText` | `#F6C170` | Accent-colored text/glyphs on accent-dim bg |
| `AccentDim` | `#36280D` | Active/selected control background |
| `AccentBorder` | `#B07515` | Border on active/selected controls |
| `SelectionAccent` | `#F2A93B` | Text/thumbnail selection |
| `TextPrimary` | `#E7E9EE` | Primary text |
| `TextSecondary` | `#7C818C` | Labels, secondary text |
| `TextDim` | `#5B616C` | Disabled/tertiary |
| `DangerRed` | `#EF4444` | Destructive actions (Delete, Clear, Close-discard) |
| `BgModal` | `#14161A` | Overlay/dialog surface |
| `BgOverlay` | `#BB000000` | Scrim behind overlays |
| `BgScrollThumb` | `#5BF2A93B` | Scrollbar thumb (accent @ ~35% alpha) |

**Other themes** (rebuilt in the same geometry/type, identity preserved via accent + canvas):
- **Studio Light** — warm-white canvas/window (`#DFE3E8` / `#F6F7F9`), panel `#FDFDFE`, control `#EEF0F3`, line `#E0E3E8`, text `#1A1D22`, accent amber `#F2A93B` (accent-dim `#FBEAD0`, accent-text `#9A6B14`). Keep contrast AA.
- **High Contrast** — pure black/white, amber or system-highlight accent, heavier borders.
- **Blood** — Studio dark palette, accent red family (`#EF4444` / dim `#3A1414`).
- **Greed** — Studio dark palette, accent green family (`#22C55E` / dim `#0D2C1B`) — the heritage green lives on here.
- **Cyanotic** — Studio dark palette, accent cyan family (`#22D3EE` / dim `#0C2A30`).

Red (`DangerRed`) stays red in every theme.

### 2.2 Typography

- **Geist** — bundled as an embedded font (`Resources/Fonts/Geist*.ttf`), referenced via a `pack://...#Geist` `FontFamily` resource key `FontUI`. Used for all UI text. Tabular numerals (`FontVariant`/OpenType `tnum` where the renderer supports it; otherwise rely on Geist's even digit widths) for page counts, zoom %, coordinates.
- Fallback: if embedding fails, fall back to `Segoe UI Variable, Segoe UI`.

**Type scale:**

| Role | Size | Weight |
|---|---|---|
| Dialog/overlay title | 16 | 600 |
| Mode tab label | 13 | 500 |
| Toolbar button label | 12.5 | 450 |
| Dialog/overlay body | 13 | 400 |
| Contextual settings bar | 11.5 | 450 |
| Sidebar section label (uppercase, tracked) | 10 | 600 |
| Status bar | 10.5 | 400 |

### 2.3 Iconography

- **Tabler Icons** — bundled as an embedded icon font (`Resources/Fonts/tabler-icons.ttf`), `FontFamily` key `FontIcon`. Replaces `Segoe MDL2 Assets` throughout.
- A single source-of-truth map (code constants or a `ResourceDictionary` of `x:String` glyph codepoints, key per icon e.g. `Ico_Save`, `Ico_Open`) so glyphs are referenced by name, not magic codepoints scattered in XAML.
- Icon sizes: 18px in toolbar/tabs, 19px in icon-only contexts, 15–16px in menus.

### 2.4 Geometry & spacing

- Corner radius: 8px (buttons, cards, tabs), 7px (small pills/zoom), 6px (swatches), 10–12px (overlays/dialogs).
- Row heights: title bar 32, tab strip 40, toolbar 48, contextual bar 34, status bar 26.
- Gaps: 6–7px between controls; 16–20px between toolbar groups (with 1px `BorderDim` divider).
- Toolbar buttons are **icon + text label** (low cognitive load). The left tool tools in Edit/Sign show label; Settings/Search in the tab strip are icon-only with tooltips.

### 2.5 Signature element

The one memorable detail: a **hairline amber underline** that visually connects the active mode tab to its toolbar (the tab "owns" the bar below it), combined with the **large soft page shadow** (`0 18px 50px rgba(0,0,0,.7)` on dark) that makes the document float. Keep everything else quiet.

---

## 3. Layout & structure

Top to bottom:

```
┌─────────────────────────────────────────────────────────┐
│ ● Scalpel — report.pdf                      —  ▢  ✕    │  title bar (32)
├─────────────────────────────────────────────────────────┤
│ View   [Edit]   Pages   Sign                  🔍   ⚙     │  tab strip (40)
├─────────────────────────────────────────────────────────┤
│ 📂Open 💾Save 🖨Print │ ➤Select T Text 🖍 ✎ 🖼 ⌗ │ −125%+ │  toolbar (48)
├─────────────────────────────────────────────────────────┤
│ Text · Size [16] ■■■■■                                    │  contextual bar (only when a tool is active)
├──────────┬──────────────────────────────────────────────┤
│ PAGES    │                                               │
│ OUTLINE  │              ┌──────────┐                      │
│ ┌──────┐ │              │          │                      │
│ │ ▣ p1 │ │              │   page   │                      │
│ │   p2 │ │              │          │                      │
│ └──────┘ │              └──────────┘                      │
├──────────┴──────────────────────────────────────────────┤
│ Page 1 of 12 · Opened report.pdf            [PORTABLE] v1.4.0 │  status bar (26)
└─────────────────────────────────────────────────────────┘
```

### 3.1 Persistent regions (visible in every mode)

- **Title bar:** app dot + filename; window minimize/maximize/close (close still prompts on unsaved changes).
- **Tab strip:** the four mode tabs (left) + Search and Settings icon buttons (right). Active tab uses `AccentDim` bg, `AccentBorder`, `AccentText`, and the hairline-underline signature.
- **Toolbar — File group (left, always):** Open, Save (accent-styled primary), Print, plus a **"File ▾" overflow menu** holding New, Close File, Save Flattened.
- **Toolbar — Zoom (right, always):** Zoom −, zoom value box (presets + Fit Width / Fit Page), Zoom +.
- **Sidebar:** PAGES / OUTLINE sub-tabs, page-jump box ("Go to page" + "of N"), thumbnail list (click/drag-reorder/multi-select/right-click menu), collapse toggle, and the sidebar bottom bar (Keyboard shortcuts, Settings). Draggable splitter retained.
- **Status bar:** status messages, page indicator; right side: PORTABLE badge + Install button (non-packaged only — gated by `App.IsPortable()`/`IsPackaged()`), version label → About overlay.

### 3.2 Mode-specific toolbar (middle of the toolbar, swaps per tab)

| Mode | Tools (left→right) |
|---|---|
| **View** | Single · Continuous · Two-page · Grid · Fit · Rotate CW |
| **Edit** | Select · Text · Highlight · Draw · Image · Crop ‖ Undo · Clear |
| **Pages** | Merge · Extract · Insert blank · Delete · Move up · Move down · Rotate |
| **Sign** | Signatures (popup: saved list / Create / Import) · Fill form |

- **Default mode on open:** View. Switching to Edit while in Continuous view still surfaces the existing guidance ("Switch to Single Page to use editing tools.") — tools live under Edit but the page-layout constraint is unchanged.
- **Mode state** is held in a `MainWindow` field (`enum AppMode { View, Edit, Pages, Sign }`). Switching a tab toggles the visibility of the corresponding mode panel and updates the active-tab visuals.

### 3.3 Contextual settings bar

Appears directly under the toolbar **only when an Edit tool with settings is active**, replacing today's per-tool bars:
- **Text:** font-size dropdown (8–72 / free), 10 color swatches.
- **Highlight:** 10 color swatches, opacity slider.
- **Draw:** 10 color swatches, stroke-size slider, opacity slider.
- **Crop:** coordinate fields, This Page / Range / All Pages / Remove Crop / Remove All / Cancel (draggable, Enter/Esc).
- Signature placement and Image placement keep their existing flows (popup / file pick → click to place → resize handle).

---

## 4. Overlays & dialogs (restyled to Studio)

All adopt Geist, amber accent, Tabler icons, consistent radius/spacing, `BgModal` surface over `BgOverlay` scrim, each with a close (✕).

- **Settings overlay:** THEME (6 radios) + LANGUAGE (6 radios). **VIEW MODE is removed** (now the View tab). Changes apply live & persist, as today.
- **Shortcuts overlay** (Ctrl+?): restyled table.
- **About overlay:** version, publisher, Authenticode thumbprint, EXE SHA-256.
- **Search bar** (Ctrl+F): floating bar, restyled; Enter/Shift+Enter next/prev, Esc close.
- **Print Preview dialog:** restyled controls (printer, orientation, copies, range, live preview).
- **Signature draw window:** restyled Clear / Save Signature / close.
- **Confirmation dialogs** (discard unsaved, delete pages, overwrite-on-save): restyled to match; destructive buttons use `DangerRed`.

---

## 5. Implementation approach (WPF)

The app is a WPF monolith: `MainWindow.xaml` (~1300 lines) + `MainWindow.xaml.cs` (~9200 lines, one `partial class`). The redesign is concentrated in XAML + styles + theme dictionaries, with **additive** code-behind for mode switching. Existing handlers are reused.

### 5.1 Fonts
- Add `Resources/Fonts/Geist-*.ttf` and `Resources/Fonts/tabler-icons.ttf` as `Resource` build items.
- Define `FontUI` and `FontIcon` `FontFamily` resources (e.g. in a new `Themes/_Shared.xaml` or `App.xaml`), via `pack://application:,,,/Resources/Fonts/#<family-name>`.
- Verify Costura single-file publish embeds the fonts (they're WPF resources, so they ride inside the assembly — confirm at publish).

### 5.2 Styles
- New shared **non-color** style dictionary (`Themes/_Shared.xaml`, merged once) holding restyled: toolbar button (icon+label), mode tab toggle, primary (Save) button, danger button, "File ▾" menu button, pill/zoom box, color swatch, slider, scrollbar, context menu, overlay card, dialog buttons. All colors via `DynamicResource` token keys.
- Replace the current `ToolbarButton`/`ToolbarToggleButton`/`ChromeButton` styles in `MainWindow.xaml` `Window.Resources` with the new set (or move them to `_Shared.xaml`).

### 5.3 Theme dictionaries
- Rewrite all six `Themes/*.xaml` to define the **full token key set** from §2.1 (add the new keys: `BgControl`, `AccentText`, `AccentBorder`, etc.). Keep the in-place per-key update contract (`ThemeManager`) intact — verify `ThemeManager` enumerates/sets the new keys.
- Add an `Ico_*` glyph map resource and a `LocaleManager`-independent icon dictionary.

### 5.4 Toolbar restructure (`MainWindow.xaml`)
- Replace the single toolbar row with: **tab strip** (4 `ToggleButton`/`RadioButton` mode tabs + Search/Settings) → **toolbar grid** = persistent File group + **four mode panels** (only the active one `Visibility.Visible`) + persistent Zoom group → **contextual bar** host.
- Each mode panel reuses the existing buttons/handlers; we are regrouping XAML, not rewriting logic.
- `MainWindow.xaml.cs`: add `AppMode` field + `SetMode(AppMode)` that toggles panel visibility and tab visuals; wire tab `Checked` handlers; default to `View`. Move View-mode selection (Single/Continuous/Two/Grid) handlers from the Settings overlay to View-tab buttons (same underlying methods).

### 5.5 Localization
- Every new/renamed label needs a key in **all six** `Strings/*.xaml` files (per the index-1-replaced-wholesale rule). New keys: mode tab labels (`Str_Mode_View/Edit/Pages/Sign`), "File" menu items if relabeled, any new tooltips. Removed: Settings "View Mode" group strings (or repurposed for the View tab).

### 5.6 What is explicitly NOT touched
- PDF open/save/merge/split/render pipelines, the Docnet/PdfSharpCore/PdfPig split, annotation burn-in, `App.xaml.cs` install/uninstall/crash/integrity logic, registry/settings keys, `signatures.json`, temp-file lifecycle, MSIX packaging.

---

## 6. Testing & verification

- `dotnet build` and `dotnet test` must stay green (tests cover services, not UI).
- Manual verification matrix (via the `run`/`verify` flow): open a PDF; switch all four modes; exercise one tool per mode; confirm contextual bar shows/hides; switch all six themes (verify no `ResourceReferenceKeyNotFoundException` during live switch — the new token keys must all resolve); switch a couple of languages (verify no blank `DynamicResource` labels); open each overlay/dialog; confirm fonts and Tabler glyphs render (and the fallback path if embedding misbehaves); confirm tabular numerals align in zoom/page counts.
- Accessibility floor: visible keyboard focus on all controls, AA contrast in Light/High Contrast, tooltips on all icon-only controls.

---

## 7. Risks & mitigations

- **Token-key coverage gap** → a theme missing a new key throws on live switch. Mitigation: define the full key set in all six files; add a quick dev-time check that each theme dict contains every key.
- **String-key coverage gap** → a label blanks out in a locale. Mitigation: add every new key to all six locale files; spot-check each language.
- **Font embedding under Costura single-file** → glyphs may not load. Mitigation: verify in a Release publish, keep the `Segoe UI Variable`/`Segoe MDL2` fallback so the app is never unusable.
- **Monolith churn** → large XAML edit risks regressions. Mitigation: regroup existing controls/handlers rather than rewrite; verify each mode's tools call the same methods as today; lean on the UI-REFERENCE control inventory as the checklist.

---

## 8. Out of scope / future

- Consolidating or reducing the number of themes.
- Command palette / keyboard-first launcher.
- Re-architecting `MainWindow` out of its monolith (a separate, larger effort).
