# Scalpel Design System — "Studio"

The visual + interaction system for Scalpel's WPF UI. This is the living reference for how the app looks and how to extend it consistently. It documents what is **actually implemented** (token names, style keys, glyph keys, file locations) — not aspirations.

- **Pre-build decision record:** `docs/superpowers/specs/2026-06-20-ui-redesign-studio-design.md`
- **Control-by-control map:** `docs/UI-REFERENCE.md`
- **This file:** the design system itself (tokens, type, icons, components, patterns, how to extend).

---

## 1. Philosophy

"Studio" is a quiet, document-first **precision-instrument** aesthetic. The chrome recedes so the PDF is the hero: a calm dark (or light) surface, generous spacing, a single confident **amber** accent, crisp geometric icons, and one signature flourish (the amber tab underline + the soft page shadow). Boldness is spent in one place; everything else stays disciplined.

Three rules govern every change:
1. **Colors come from theme tokens** via `DynamicResource` — never hardcode a chrome color.
2. **Every theme defines every token key; every locale defines every string key.** A missing key fails silently (stale value / blank label).
3. **Reference fonts and icons by resource key** (`FontUI`, `FontIcon`, `Ico_*`), never by raw family name or codepoint scattered in markup.

---

## 2. Layout structure

Top to bottom, the main window (`MainWindow.xaml`, a 5-row `Grid`):

```
┌─────────────────────────────────────────────────────────┐
│ ● Scalpel — report.pdf                      —  ▢  ✕    │  Row 0 · title bar      (h 32)
├─────────────────────────────────────────────────────────┤
│ View  [Edit]  Pages  Sign                     🔍   ⚙     │  Row 1 · mode tab strip (h 40)
├─────────────────────────────────────────────────────────┤
│ 📂Open 💾Save 🖨Print ▾ │ «mode tools» │  −  125% +       │  Row 2 · toolbar        (h 48)
│ Text · Size [16] ■■■■■                                    │  (contextual bar: only when a tool is active)
├──────────┬──────────────────────────────────────────────┤
│ PAGES    │                                               │
│ OUTLINE  │                ┌──────────┐                    │  Row 3 · sidebar | page area
│ thumbs   │                │   page   │                    │
├──────────┴──────────────────────────────────────────────┤
│ Page 1 of 12 · status                  [PORTABLE] v1.5.x │  Row 4 · status bar     (h 26)
└─────────────────────────────────────────────────────────┘
```

**Persistent in every mode:** title bar; mode tab strip (incl. Search 🔍 + Settings ⚙ on the right); toolbar File group (Open · Save · Print · **File ▾** = New/Close/Save Flattened) and Zoom group (− · zoom box · +); sidebar; status bar.

**Row heights / geometry:** title 32 · tab strip 40 · toolbar 48 · contextual bar 34 · status 26. Corner radius: **8** (buttons/tabs/cards), **7** (pills/zoom), **6** (swatches), **12** (overlays/dialogs). Control gaps 6–7px; group dividers are a 1px `BorderDim` rule with ~10px margins.

---

## 3. Mode system

The toolbar is task-organized by an `enum AppMode { View, Edit, Pages, Sign }` (in `MainWindow.xaml.cs`).

- `SetMode(AppMode)` shows exactly one `ModePanel{View,Edit,Pages,Sign}` and sets the four tab `IsChecked` states, guarded by `_suppressModeEvents` to avoid re-entrancy.
- The tabs are **grouped `RadioButton`s** (`GroupName="AppModeTabs"`, styled via `StudioModeTab`) so clicking the active tab can't deselect it.
- Default mode on launch is **View**.

| Mode | Tools (in `ModePanel…`) |
|---|---|
| **View** | Single · Continuous · Two-page · Grid · Fit · Rotate |
| **Edit** | Select · Text · Highlight · Draw · Image · Crop ‖ Undo · Clear (danger) — selecting a tool reveals the **contextual settings bar** |
| **Pages** | Merge · Extract · Insert blank · Delete (danger) · Move up · Move down · Rotate |
| **Sign** | Signatures (saved list + Create / Import popup) — form fields are filled inline, no separate button |

Edit-tool buttons are plain `Button`s (not toggles): `SetTool(EditTool)` highlights the active one via `SetResourceReference` on Background (`AccentDim`) / Foreground (`Accent`), so it tracks live theme switches.

---

## 4. Color tokens

Tokens are `SolidColorBrush` resources defined **identically-keyed** in all six `Themes/*.xaml`. `ThemeManager.LoadDict` copies the new theme's keys into the live merged dict (index 0) on switch — so the key set must match across every theme or a missing key keeps its stale value.

**Token set (24 brushes + `GrainOpacity` + 4 `SystemColors.*` highlight keys) — Studio Dark (default) values:**

| Token | Hex | Role |
|---|---|---|
| `BgCanvas` | `#0A0B0E` | Page viewport (behind the document) |
| `BgDark` | `#14161A` | Window background |
| `BgPanel` | `#181B21` | Toolbar / status bar |
| `BgSidebar` | `#0D0F12` | Sidebar, tab strip, title bar |
| `BgModal` | `#14161A` | Overlay / dialog surface |
| `BgHover` | `#2A2E36` | Hover fill |
| `BgControl` | `#23272F` | Default button/control fill |
| `BorderDim` | `#20242B` | Hairline dividers / borders |
| `Accent` | `#F2A93B` | Active tab, Save, active tool, selection, focus |
| `AccentText` | `#F6C170` | Accent text/glyph on `AccentDim` |
| `AccentDim` | `#36280D` | Active/selected control fill |
| `AccentBorder` | `#B07515` | Border on active/selected controls |
| `SelectionAccent` | `#F2A93B` | Text / thumbnail selection |
| `AccentLogo` | `#F2A93B` | Title-bar app dot |
| `TextPrimary` | `#E7E9EE` | Primary text |
| `TextSecondary` | `#7C818C` | Labels / secondary text |
| `TextDim` | `#5B616C` | Disabled / tertiary |
| `TextFooter` | `#5B616C` | Footer text |
| `DangerRed` | `#EF4444` | Destructive actions (always red) |
| `BgDragHandle` | `#3A3F47` | Splitter grip |
| `BgScrollThumb` | `#5BF2A93B` | Scrollbar thumb (accent @ ~35%) |
| `BgOverlay` | `#BB000000` | Scrim behind overlays |
| `GrainOpacity` | `0.12` | Grain-texture opacity (`sys:Double`) |
| 4× `SystemColors.*Highlight*` | `#36280D` / `#F6C170` | Native text-selection brushes |

**The six themes** share the Studio geometry/type/icons; identity comes from canvas + accent:

| Theme | Surface | Accent family |
|---|---|---|
| **Dark** (default) | near-black `#0A0B0E`/`#14161A` | amber `#F2A93B` |
| **Light** | warm-white `#DFE3E8`/`#F6F7F9`, ink text `#1A1D22` | amber `#F2A93B` (text `#9A6B14`) |
| **High Contrast** | pure black/white, heavy borders | `#FFB000` |
| **Blood** | Studio dark | red `#EF4444` |
| **Greed** | Studio dark | green `#22C55E` (the heritage green) |
| **Cyanotic** | Studio dark | cyan `#22D3EE` |

`DangerRed` stays a red in every theme.

---

## 5. Typography

- **`FontUI`** = bundled **Geist** (`#Geist`, weights 400/500/600), fallback `Segoe UI Variable, Segoe UI`. All UI text. The `MainWindow` root sets `FontFamily="{DynamicResource FontUI}"` so everything inherits Geist by default.
- Tabular numerals (`Typography.NumeralAlignment="Tabular"`) on page counts, zoom %, coordinates, and hashes so digits align.
- Monospace content (Authenticode thumbprint, SHA-256, keyboard chords) stays **Consolas** — that's content, not chrome.

**Type scale** (`sys:Double` keys in `_Shared.xaml`):

| Key | px | Role |
|---|---|---|
| `FsDialogTitle` | 16 | Overlay / dialog titles (SemiBold) |
| `FsTab` | 13 | Mode tab labels (Medium) |
| `FsButton` | 12.5 | Toolbar button labels |
| `FsBody` | 13 | Dialog / overlay body |
| `FsContext` | 11.5 | Contextual settings bar |
| `FsSidebarLabel` | 10 | Sidebar section labels (uppercase, tracked) |
| `FsStatus` | 10.5 | Status bar |

---

## 6. Iconography

- **`FontIcon`** = bundled **Tabler Icons** (`#tabler-icons`), fallback `Segoe MDL2 Assets`. The shipped TTF is a **subset** (`Resources/Fonts/tabler-icons.ttf`, ~12 KB, 39 glyphs) so the single-file EXE stays ~6 MB.
- Glyphs are referenced by name through the **`Ico_*` map** in `_Shared.xaml` (41 keys), e.g. `Content="{StaticResource Ico_Save}"` in XAML or `(string)FindResource("Ico_Save")` in code-behind — never raw codepoints in markup.
- Icon sizes: 18px in toolbar/tabs, ~19px icon-only, 15–16px in menus.

Available keys include: `Ico_New/Open/Close/Save/Flatten/Print`, `Ico_Merge/Extract/InsertPage/Delete/MoveUp/MoveDown/Rotate`, `Ico_Select/Text/Highlight/Draw/Image/Crop/Signature/FillForm`, `Ico_Undo/Clear`, `Ico_ZoomIn/ZoomOut`, `Ico_Search/Settings/Shortcuts`, `Ico_View/Single/Continuous/TwoPage/Grid/Fit`, `Ico_Chevron/ChevronLeft/ChevronRight`, `Ico_Min/Max/X/WinClose`.

---

## 7. Components (`Themes/_Shared.xaml`)

Reusable styles, all token-driven. Use these instead of inlining brushes/templates.

| Style key | Target | Use |
|---|---|---|
| `StudioToolButton` | `Button` | Standard toolbar action (icon + Geist label) — `BgControl` rest, `BgHover` hover |
| `StudioPrimaryButton` | `Button` | Primary action (Save) — `AccentDim` fill + `AccentBorder`, `AccentText` |
| `StudioDangerButton` | `Button` | Destructive action (Clear, Delete) — `DangerRed` foreground |
| `StudioModeTab` | `ToggleButton` (applied to grouped `RadioButton`s) | Mode tabs — checked = `AccentDim`/`AccentBorder`/`AccentText` + 2px amber **underline** (the signature element) |
| `StudioIconButton` | `Button` | Icon-only (Search, Settings, chrome, overlay close) — transparent → `BgHover`; needs a `ToolTip` |
| `StudioToolToggle` | `ToggleButton` | Toggle-state tool (View-mode buttons) — checked = `AccentDim`/`AccentBorder`/`AccentText` |
| `StudioPill` | `Border` | Zoom value box / small value chips — radius 7 |
| `StudioSwatch` | `ToggleButton` | 16×16 color swatch — `Accent` 2px border when checked |
| `StudioOverlayCard` | `Border` | Overlay / dialog surface — `BgModal` + `BorderDim` + radius 12 + soft shadow |
| *(implicit)* `ScrollBar` | — | Thin track; thumb `BgScrollThumb` → `Accent` on hover |
| *(implicit)* `ContextMenu` / `MenuItem` / `Separator` | — | Themed menus (`BgPanel`/`BorderDim`/`FontUI`) |

The zoom `ComboBox` (`DarkComboBox`/`DarkComboToggle`/`DarkComboItem`) and the title-bar `ChromeButton`/`ChromeCloseButton` remain styled in `MainWindow.xaml` (they're window-specific), retargeted to the same tokens + `FontUI`/`FontIcon`.

**Signature element:** the active mode tab's 2px amber underline (in `StudioModeTab`) + the large soft page shadow (`0 18px 50px rgba(0,0,0,.7)` on dark). Keep everything else quiet.

The contextual settings bars (Text/Highlight/Draw/Crop), the Search bar, the Signature/Password windows, the Print Preview window, and `KillerDialog` are built in **code-behind** (`MainWindow.xaml.cs`, `PrintPreviewWindow.cs`); they pull the same resources via `Application.Current.FindResource(...)` / `SetResourceReference(...)`.

---

## 8. File map & resource contract

- `App.xaml` merges three dictionaries in a **load-bearing order**: **[0]** theme (`Themes/Dark.xaml`, swapped by `ThemeManager`) · **[1]** strings (`Strings/en-US.xaml`, swapped by `LocaleManager`) · **[2]** `Themes/_Shared.xaml` (fonts, type scale, `Ico_*` map, Studio styles). New app-level dictionaries go at index ≥ 2.
- `Themes/*.xaml` — six theme token dicts (identical key sets).
- `Themes/_Shared.xaml` — the design system (non-color).
- `Strings/*.xaml` — six locale string dicts (identical key sets).
- `Resources/Fonts/Geist-{Regular,Medium,SemiBold}.ttf`, `tabler-icons.ttf` — bundled as `Resource` build items in `Scalpel.csproj`.
- `Services/ThemeManager.cs` (in-place per-key theme update + DWM title bar) · `Services/LocaleManager.cs` (wholesale strings swap).

---

## 9. How to extend

**Add a theme:** create `Themes/<Name>.xaml` defining the **full** token key set (copy Dark, retune surface + accent family); add a `Theme` enum entry + `pack://` case in `ThemeManager`; add a Settings radio + `Theme<Name>Radio_Checked` handler + a sync line in `SettingsBtn_Click`. Verify all theme files share an identical key set.

**Add a tool / mode action:** add the `Button` (or `ToggleButton`) to the right `ModePanel…` in `MainWindow.xaml` using `StudioToolButton`/`StudioToolToggle`, an `Ico_*` icon, and a `Str_Lbl_*` label; wire its existing or new `_Click` handler. If it's an Edit tool, route through `SetTool`.

**Add an icon:** if the glyph isn't in the subset, re-subset the font (`python -m fontTools.subset Resources/Fonts/tabler-icons.ttf --unicodes=<existing+new> --drop-tables+=GSUB,GPOS,GDEF --output-file=…`) and add an `Ico_<Name>` entry to `_Shared.xaml`. Reference by key only.

**Add a string:** add the key to **all six** `Strings/*.xaml` (a key missing from any locale blanks that label in that language). Reuse `Str_Lbl_*` for labels and `Str_TT_*` for tooltips. See `Strings/TRANSLATING.md`.

**Add a control:** prefer an existing `Studio*` style. If you need a new one, put it in `_Shared.xaml`, drive all colors from `DynamicResource` tokens, fonts from `FontUI`/`FontIcon`, sizes from `Fs*`.
