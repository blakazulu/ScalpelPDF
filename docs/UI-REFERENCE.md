# Scalpel — Button & Control Reference

Every interactive control in the app, by screen region, with what it does. Tooltips shown are the English (en-US) text; the same controls relabel in other languages. Most actions are **no-ops until a PDF is open** unless noted.

Layout, top to bottom: **Title bar** → **Mode tab strip** → **Toolbar** → (**Contextual bar**, when an Edit tool is active) → (**Sidebar** | **Page area**) → **Status bar**, with **overlays** and **context menus** appearing as needed.

---

## 1. Title bar (window chrome)

The custom title bar (the app uses a borderless window). Shows the app name and the name of the currently open file. Drag it to move the window; double-click to maximize/restore.

| Button | Tooltip / label | What it does |
|---|---|---|
| Minimize (`—`) | — | Minimizes the window to the taskbar. |
| Maximize/Restore (`▢`) | — | Toggles between maximized and normal window size. |
| Close (`✕`) | — | Closes the app. If there are unsaved changes, prompts *"You have unsaved changes. Close Scalpel without saving?"* first. |

---

## 2. Mode tab strip

A horizontal strip directly under the title bar. The four **mode tabs** on the left determine which tools appear in the middle of the toolbar. The active tab is highlighted in amber with a hairline underline that visually connects it to the toolbar below. Two icon-only buttons sit on the right and are always available, regardless of which mode is active.

### Mode tabs (mutually exclusive)

| Tab | What it does |
|---|---|
| **View** | Switches the toolbar to page-layout controls: Single · Continuous · Two-page · Grid · Fit · Rotate. This is the **default mode** when a PDF is opened. |
| **Edit** | Switches the toolbar to annotation and editing tools: Select · Text · Highlight · Draw · Image · Crop · Undo · Clear. |
| **Pages** | Switches the toolbar to document-structure tools: Merge · Extract · Insert blank · Delete · Move up · Move down · Rotate. |
| **Sign** | Switches the toolbar to signature placement: Signatures popup (saved list + Create / Import). |

### Right-side icon buttons (always visible)

| Button | Tooltip | Action |
|---|---|---|
| Search (🔍) | Find (Ctrl+F) | Opens the floating **Search bar** over the page area (§5). |
| Settings (⚙) | Settings | Opens the **Settings overlay** (§6). |

---

## 3. Toolbar

A single row beneath the tab strip. The left and right sides are **persistent** (present in every mode); the middle swaps to match the active mode tab.

### Left — File group (always visible)

| Control | Tooltip | Action |
|---|---|---|
| Open | Open (Ctrl+O) | Opens a file picker (PDF only) and loads the chosen file. |
| Save | Save (Ctrl+S) | Saves changes (amber primary button). Offers to overwrite the original file, or save as a new file (Yes / No / Cancel). |
| Print | Print (Ctrl+P) | Burns pending annotations, rasterizes pages, and opens Scalpel's own **Print Preview** dialog (printer, orientation, copies, page range, live preview). |
| **File ▾** (overflow menu) | — | Drops down three additional file commands: |
| → New | New Blank Document (Ctrl+N) | Creates a new blank one-page PDF. Prompts to discard if the current file has unsaved changes. |
| → Close File | Close File (Ctrl+W) | Closes the current document (prompts if unsaved). Disabled when nothing is open. |
| → Save Flattened | Save Flattened PDF | Renders every page (with all annotations burned in) to images at 150 DPI and saves a new, uneditable PDF. Shows a progress bar; your working file is untouched. |

### Middle — Mode-specific tools (swaps per active tab)

#### View mode tools

| Button | Action |
|---|---|
| Single | Displays one page at a time. |
| Continuous | Displays all pages in a scrollable vertical strip. Editing tools (available in the **Edit** tab) display a warning in this layout — switch to Single or Two-page to use them. |
| Two-page | Displays two pages side by side. |
| Grid | Displays pages as a miniature grid; zoom controls adjust how many columns are shown. |
| Fit | Zooms to fit the current page within the window. |
| Rotate CW | Rotates the current page 90° clockwise (visual only until saved). |

#### Edit mode tools

| Button | Tooltip | Action |
|---|---|
| Select | Select Tool | Default tool. Click/drag/move annotations; drag to select text; double-click a placed text annotation to re-edit it. |
| Text | Text Tool — click to add text | Click on a page to drop an editable text box. Activates the **text settings bar** (§4). |
| Highlight | Highlight Tool — drag to highlight | Drag across a page to lay down a translucent highlight. Activates the **highlight settings bar** (§4). |
| Draw | Draw Tool — freehand ink | Drag to draw freehand ink. Activates the **draw settings bar** (§4). |
| Image | Image Tool — click to insert an image | Pick an image file (PNG/JPG/BMP), then click to place it as a resizable annotation. |
| Crop | Crop Tool — drag to set crop area | Drag a crop rectangle; the **crop confirm bar** appears to apply or cancel (§4). |
| Undo | Undo Last (Ctrl+Z) | Reverts the last change — removes the last annotation, or restores the snapshot for a page edit/rotation/crop. |
| Clear | Clear All Annotations | Removes **all** annotations from the current page (danger-colored). |

#### Pages mode tools

| Button | Tooltip | Action |
|---|---|
| Merge | Merge PDFs | Picks one or more PDFs and appends their pages (and bookmarks/links) to the current document. |
| Extract | Extract Pages | Saves the page(s) selected in the sidebar to a new PDF (Save As). Prompts if no pages are selected. |
| Insert blank | Insert Blank Page | Inserts a new blank page after the current page. |
| Delete | Delete Selected Pages | Deletes the selected page(s) after confirmation (danger-colored). |
| Move up | Move Page Up | Moves the selected page one position earlier. No-op on the first page. |
| Move down | Move Page Down | Moves the selected page one position later. No-op on the last page. |
| Rotate | Rotate Page | Rotates the selected page(s) 90° clockwise. |

#### Sign mode tools

| Control | Action |
|---|---|
| **Signatures** | Toggles the **signature popup** — a saved-signature list plus **Create** and **Import** buttons. Pick a signature, then click a page to place it (drag the corner handle to resize). Interactive form fields on the page are filled directly by clicking them — no separate tool required; field values are saved automatically on Save. |

### Right — Zoom group (always visible)

| Control | Tooltip | Action |
|---|---|
| Zoom Out (`−`) | Zoom out (Ctrl+−) | Decreases zoom by one step. In Grid view, shows fewer/larger pages. Stops at the minimum (5%). |
| Zoom box | Zoom level (Ctrl+scroll · Ctrl+=/− · Ctrl+0=reset) | Dropdown of zoom presets plus **Fit Width** and **Fit Page**. Type or pick a value; fit modes re-apply on window resize. |
| Zoom In (`+`) | Zoom in (Ctrl+=) | Increases zoom by one step. In Grid view, shows more/smaller pages. Stops at the maximum. |

---

## 4. Contextual settings bar (Edit mode only)

Appears directly below the toolbar only while an Edit tool with settings is active. Dismisses when you switch tools, modes, or click Select.

### Text settings bar (Text tool)
- **Font-size dropdown** — preset sizes (8–72) or free entry; applies to the active/new text box.
- **Color swatches** — ten colors (Red, SaddleBrown, Orange, Gold, LimeGreen, DodgerBlue, MediumPurple, DeepPink, White, Black). Click to set text color; the active swatch is outlined. Default: Black.

### Highlight settings bar (Highlight tool)
- **Color swatches** — same ten colors (default yellow/gold).
- **Opacity slider** — translucency of the highlight (default ~31%).

### Draw settings bar (Draw tool)
- **Color swatches** — same ten colors (default Red).
- **Size slider** — stroke width, 1–20 px.
- **Opacity slider** — stroke translucency.

### Crop confirm bar (Crop tool, after dragging a rectangle)
Draggable by its header. Enter applies to the current page; Escape cancels.

| Control | Action |
|---|---|
| Four coordinate fields | The crop rectangle in PDF points; editable for precision. |
| **This Page** | Applies the crop to the current page (Enter). |
| Range field + **Range** | Applies the crop to the pages in a range string (e.g. `1-3,5`). Invalid ranges are rejected. |
| **All Pages** | Applies the crop to every page (multi-page docs only). |
| **Remove Crop** | Clears the CropBox/TrimBox from this page (shown only if the page is already cropped). |
| **Remove All** | Removes the CropBox from every page (multi-page docs only). |
| **Cancel** | Discards the preview and closes the bar (Escape). |

### Signature popup (Sign mode — Signatures)
- **Saved-signature list** — thumbnails of stored signatures; click one to arm it for placement, then click a page to drop it (drag the corner handle to resize). Each has a **delete (✕)** button.
- **Create Signature** — opens the signature drawing window (below).
- **Import Image** — picks a PNG/JPG/BMP to use as a signature.

**Signature drawing window** (from *Create Signature*):

| Button | Action |
|---|---|
| Clear | Erases the drawing canvas. |
| Save Signature | Saves the drawn signature for reuse and arms it for placement. |
| Close (✕) | Closes the window without saving. |

---

## 5. Search bar (Ctrl+F)

Opened via the Search button (🔍) in the tab strip, or with Ctrl+F. A floating bar over the page area, built on demand.

| Control | Action |
|---|---|
| Search box | Type to search the whole document live (case-insensitive); matches are highlighted. **Enter** = next match, **Shift+Enter** = previous match. |
| Close (✕) | Closes the search bar (Esc). |

---

## 6. Settings overlay (⚙ in the tab strip)

Opens a centered panel; radios reflect the current state. Changes apply live and persist across sessions.

| Group | Options | Action |
|---|---|---|
| **THEME** | Dark · Light · High Contrast | Switches the base color theme immediately (`ThemeDarkRadio`/`ThemeLightRadio`/`ThemeHCRadio` → `ApplyTheme`; also recolors the native title bar). |
| **ACCENT** | Amber · Red · Green · Cyan | Switches the accent color overlay (`AccentAmberRadio`/`AccentRedRadio`/`AccentGreenRadio`/`AccentCyanRadio` → `ApplyAccent`). Applies to Dark and Light only — all four radios are **disabled** when High Contrast is active (HC uses its own fixed accent). |
| **LANGUAGE** | English · Español · 中文 (繁體) · 中文 (简体) · বাংলা · Türkçe | Switches the UI language immediately. |
| Close (✕) | — | Closes the Settings overlay. |

> **Note:** Page layout (Single / Continuous / Two-page / Grid) is set from the **View tab** toolbar, not from Settings.

---

## 7. Sidebar

A collapsible left panel with two tabs. A draggable splitter between the sidebar and page area resizes it.

| Control | Label / tooltip | Action |
|---|---|---|
| **PAGES** tab | PAGES | Shows the page thumbnail list. |
| **OUTLINES** tab | OUTLINES | Shows the document's bookmark/outline tree (if any). Click an entry to jump to that page. |
| Page jump box | Go to page (Enter to navigate) | Type a page number and press Enter to jump there. Gets focus → selects all for quick overtype; clamps to valid range. |
| Page total label | — | Shows "of N" next to the jump box. |
| Collapse/Expand toggle | Collapse sidebar / Expand sidebar | Collapses the sidebar to a thin strip (chevron flips), or restores its width. Width is remembered per tab. |

**Page thumbnail list** (PAGES tab): click a thumbnail to navigate; drag to reorder pages; Ctrl/Shift-click for multi-select; right-click for the **page context menu** (§8).

**Sidebar bottom bar:**

| Button | Tooltip | Action |
|---|---|---|
| Keyboard shortcuts | Keyboard shortcuts (Ctrl+?) | Opens the **Shortcuts overlay** (§9). |
| Settings | Settings | Opens the **Settings overlay** (§6). |

---

## 8. Context menus (right-click)

### On the page area
Copy Text · Print · quick tool switch (Select / Text / Highlight / Draw) · Rotate Page CW / CCW · Delete Selected (annotation) · Undo Last · Clear Page Annotations.

### On a page thumbnail (sidebar)
Insert Blank Page After · Rotate CW / CCW · Move Page Up / Down · Extract Page(s) · Delete Page(s). All operate on the current multi-selection where applicable; Delete confirms first.

---

## 9. Status bar & overlays

**Status bar** (bottom): shows status messages (e.g. *"Opened … — N page(s)"*, *"Page X of N"*), and on the right a **version label** — click it to open the **About overlay** (version, publisher, Authenticode thumbprint, EXE SHA-256). In portable (non-installed, non-Store) mode it also shows a **PORTABLE** badge and an **Install Scalpel…** button that installs to your user profile and relaunches. The Install button does **not** appear in the Microsoft Store / MSIX build.

**Overlays** each have a close (✕): **Settings** (§6), **Shortcuts** (§9), and **About**.

---

## 10. Keyboard shortcuts (Ctrl+? overlay)

| Area | Shortcut | Action |
|---|---|---|
| File | Ctrl+O / Ctrl+N / Ctrl+W | Open / New blank / Close file |
| File | Ctrl+S · Ctrl+Shift+S | Save · Save As |
| File | Ctrl+P | Print |
| Navigation | ← / → (or PgUp/PgDn) | Previous / next page |
| Navigation | Ctrl+Scroll | Zoom in/out anchored at the cursor |
| Navigation | Ctrl+= / Ctrl+− | Zoom in / out |
| Navigation | Ctrl+0 | Reset zoom to 100% |
| Navigation | Middle-mouse drag | Pan the view |
| Editing | Ctrl+Z | Undo (Edit tab must be active) |
| Editing | Delete | Delete selected annotation |
| Editing | Enter / Esc | Confirm / cancel text or crop |
| Search & select | Ctrl+F | Find / search (opens Search bar from any mode) |
| Search & select | Enter / Shift+Enter | Next / previous result |
| Search & select | Ctrl+A | Select all text on page |
| Search & select | Ctrl+C | Copy selected text |
| Help | Ctrl+? | Toggle this shortcut list |

---

## 11. Studio design language

Scalpel uses the **"Studio"** visual language: a document-first aesthetic where chrome recedes so the page is the focus. Key characteristics:

- **Geist** — bundled UI font (embedded in the EXE via Costura/WPF resource). Used for all labels, overlays, and status text. Tabular numerals keep zoom percentages and page counts visually aligned. Fallback: `Segoe UI Variable, Segoe UI`.
- **Tabler Icons** — bundled icon font (embedded). Replaces the legacy Segoe MDL2 Assets set throughout the app. Each icon is referenced by a named resource key (e.g. `Ico_Save`, `Ico_Open`) rather than hardcoded codepoints.
- **Amber accent** (`#F2A93B`) — active mode tab, primary Save button, active tool highlight, focus ring, scrollbar thumb. The accent color is independently selectable (Amber · Red · Green · Cyan) for Dark and Light themes; High Contrast uses its own fixed amber/white accent.
- **Hairline amber underline** — the active mode tab carries a bottom border that visually "connects" the tab to the toolbar below it.
- The same geometry, spacing, and font apply across all themes. The theme system is two-axis: base theme (Dark · Light · High Contrast) + accent (Amber · Red · Green · Cyan).

---

*Icons are from the **Tabler Icons** font (bundled); the tooltip text above is the source of truth for each button's purpose. This reference is generated from `MainWindow.xaml`, the handlers in `MainWindow.xaml.cs`, and `Strings/en-US.xaml`.*
