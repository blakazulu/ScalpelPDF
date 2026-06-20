# KillerPDF — Button & Control Reference

Every interactive control in the app, by screen region, with what it does. Tooltips shown are the English (en-US) text; the same controls relabel in other languages. Most actions are **no-ops until a PDF is open** unless noted.

Layout, top to bottom: **Title bar** → **Main toolbar** → (**Sidebar** | **Page area**) → **Status bar**, with **contextual bars**, **overlays**, and **context menus** appearing as needed.

---

## 1. Title bar (window chrome)

The custom title bar (the app uses a borderless window). Drag it to move the window; double-click to maximize/restore.

| Button | Tooltip / label | What it does |
|---|---|---|
| Minimize (`—`) | — | Minimizes the window to the taskbar. |
| Maximize/Restore (`▢`) | — | Toggles between maximized and normal window size. |
| Close (`✕`) | — | Closes the app. If there are unsaved changes, prompts *"You have unsaved changes. Close KillerPDF without saving?"* first. |

---

## 2. Main toolbar

A single row of icon buttons, grouped left to right. All show a tooltip on hover.

### File group
| Button | Tooltip | Action |
|---|---|---|
| New | New Blank Document (Ctrl+N) | Creates a new blank one-page PDF. Prompts to discard if the current file has unsaved changes. |
| Open | Open (Ctrl+O) | Opens a file picker (PDF only) and loads the chosen file. |
| Close File | Close File (Ctrl+W) | Closes the current document (prompts if unsaved). Disabled when nothing is open. |
| Save | Save (Ctrl+S) | Saves changes. Offers to overwrite the original file, or save as a new file (Yes / No / Cancel). |
| Save Flattened | Save Flattened PDF (rasterize all pages — fully uneditable) | Renders every page (with all annotations burned in) to images at 150 DPI and saves a new, uneditable PDF. Shows a progress bar; your working file is untouched. |
| Print | Print (Ctrl+P) | Burns pending annotations, rasterizes pages, and opens KillerPDF's own **Print Preview** dialog (printer, orientation, copies, page range, live preview). |

### Page-management group
| Button | Tooltip | Action |
|---|---|---|
| Merge | Merge PDFs (combine additional PDFs into this one) | Picks one or more PDFs and appends their pages (and bookmarks/links) to the current document. |
| Extract | Extract Pages (save selected pages to a new PDF) | Saves the page(s) selected in the sidebar to a new PDF (Save As). Prompts if no pages are selected. |
| Delete | Delete Selected Pages | Deletes the selected page(s) after confirmation. |
| Move Up | Move Page Up (reorder) | Moves the selected page one position earlier. No-op on the first page. |
| Move Down | Move Page Down (reorder) | Moves the selected page one position later. No-op on the last page. |

### Tool group (selects the active editing tool)
| Button | Tooltip | Action |
|---|---|---|
| Select | Select Tool — click annotations, drag to select text, double-click to edit | Default tool. Click/drag/move annotations, drag to select text, double-click placed text to re-edit. |
| Text | Text Tool — click to add text | Click on a page to drop an editable text box. Shows the **text settings bar** (font size + color). |
| Highlight | Highlight Tool — drag to highlight | Drag across a page to lay down a translucent highlight. Shows the **highlight settings bar** (color + opacity). |
| Draw | Draw Tool — freehand ink | Drag to draw freehand ink. Shows the **draw settings bar** (color + stroke size + opacity). |
| Crop | Crop Tool — drag to set crop area, then apply to page(s) | Drag a crop rectangle; the **crop confirm bar** appears to apply or cancel. |
| Image | Image Tool — click to insert an image onto the page | Pick an image file (PNG/JPG/BMP), then click to place it as a resizable annotation. |
| Signature | Signature — create and place signatures | Toggles the **signature popup** (saved signatures + Create / Import). Pick one, then click a page to place it. |

### Edit group
| Button | Tooltip | Action |
|---|---|---|
| Undo | Undo Last (Ctrl+Z) | Reverts the last change — removes the last annotation, or restores the document snapshot for a page edit/rotation/crop. |
| Clear Annotations | Clear All Annotations | Removes **all** annotations from the current page (danger-colored). |

### Zoom group
| Control | Tooltip | Action |
|---|---|---|
| Zoom Out | Zoom out (Ctrl+−) | Decreases zoom by one step. In Grid view, shows fewer/larger pages. Stops at the minimum (5%). |
| Zoom box | Zoom level (Ctrl+scroll · Ctrl+=/− · Ctrl+0=reset) | Dropdown of zoom presets plus **Fit Width** and **Fit Page**. Type or pick a value; fit modes re-apply on window resize. |
| Zoom In | Zoom in (Ctrl+=) | Increases zoom by one step. In Grid view, shows more/smaller pages. Stops at the maximum. |

---

## 3. Sidebar

A collapsible left panel with two tabs.

| Control | Label / tooltip | Action |
|---|---|---|
| **PAGES** tab | PAGES | Shows the page thumbnail list. |
| **OUTLINES** tab | OUTLINES | Shows the document's bookmark/outline tree (if any). Click an entry to jump to that page. |
| Page jump box | Go to page (Enter to navigate) | Type a page number and press Enter to jump there. Gets focus → selects all for quick overtype; clamps to valid range. |
| Page total label | — | Shows "of N" next to the jump box. |
| Collapse/Expand toggle | Collapse sidebar / Expand sidebar | Collapses the sidebar to a thin strip (chevron flips), or restores its width. Width is remembered per tab. |

**Page thumbnail list** (PAGES tab): click a thumbnail to navigate; drag to reorder pages; Ctrl/Shift-click for multi-select; right-click for the **page context menu** (§7).

**Sidebar bottom bar:**
| Button | Tooltip | Action |
|---|---|---|
| Keyboard shortcuts | Keyboard shortcuts (Ctrl+?) | Opens the **Shortcuts overlay** (§8). |
| Settings | Settings | Opens the **Settings overlay** (§6). |

A draggable splitter between the sidebar and page area resizes the sidebar.

---

## 4. Contextual tool bars

These appear only while the matching tool is active.

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
Draggable by its header. Enter applies the current page; Escape cancels.
| Control | Action |
|---|---|
| Four coordinate fields | The crop rectangle in PDF points; editable for precision. |
| **This Page** | Applies the crop to the current page (Enter). |
| Range field + **Range** | Applies the crop to the pages in a range string (e.g. `1-3,5`). Invalid ranges are rejected. |
| **All Pages** | Applies the crop to every page (multi-page docs only). |
| **Remove Crop** | Clears the CropBox/TrimBox from this page (shown only if the page is already cropped). |
| **Remove All** | Removes the CropBox from every page (multi-page docs only). |
| **Cancel** | Discards the preview and closes the bar (Escape). |

### Signature popup (Signature tool)
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

A floating bar over the page area, built on demand.
| Control | Action |
|---|---|
| Search box | Type to search the whole document live (case-insensitive); matches are highlighted. **Enter** = next match, **Shift+Enter** = previous match. |
| Close (✕) | Closes the search bar (Esc). |

---

## 6. Settings overlay (gear icon)

Opens a centered panel; radios reflect the current state. Changes apply live and persist across sessions.

| Group | Options | Action |
|---|---|---|
| **THEME** | Dark · Light · High Contrast · Blood · Greed · Cyanotic | Switches the color theme immediately (also recolors the native title bar). |
| **LANGUAGE** | English · Español · 中文 (繁體) · 中文 (简体) · বাংলা · Türkçe | Switches the UI language immediately. |
| **VIEW MODE** | Continuous · Single Page · Two Page · Grid | Sets how pages are laid out; closes the overlay on pick. Editing tools work best in Single/Two-Page — Continuous shows *"Switch to Single Page to use editing tools."* |
| Close (✕) | — | Closes the Settings overlay. |

---

## 7. Context menus (right-click)

### On the page area
Copy Text · Print · quick tool switch (Select / Text / Highlight / Draw) · Rotate Page CW / CCW · Delete Selected (annotation) · Undo Last · Clear Page Annotations.

### On a page thumbnail (sidebar)
Insert Blank Page After · Rotate CW / CCW · Move Page Up / Down · Extract Page(s) · Delete Page(s). All operate on the current multi-selection where applicable; Delete confirms first.

---

## 8. Status bar & overlays

**Status bar** (bottom): shows status messages (e.g. *"Opened … — N page(s)"*, *"Page X of N"*), and on the right a **version label** — click it to open the **About overlay** (version, publisher, Authenticode thumbprint, EXE SHA-256). In portable (non-installed, non-Store) mode it also shows a **PORTABLE** badge and an **Install KillerPDF…** button that installs to your user profile and relaunches. The Install button does **not** appear in the Microsoft Store / MSIX build.

**Overlays** each have a close (✕): **Settings** (§6), **Shortcuts** (§9), and **About**.

---

## 9. Keyboard shortcuts (Ctrl+? overlay)

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
| Editing | Ctrl+Z | Undo |
| Editing | Delete | Delete selected annotation |
| Editing | Enter / Esc | Confirm / cancel text or crop |
| Search & select | Ctrl+F | Find / search |
| Search & select | Enter / Shift+Enter | Next / previous result |
| Search & select | Ctrl+A | Select all text on page |
| Search & select | Ctrl+C | Copy selected text |
| Help | Ctrl+? | Toggle this shortcut list |

---

*Icons are from the Segoe MDL2 Assets font; the tooltip text above is the source of truth for each button's purpose. This reference is generated from `MainWindow.xaml`, the handlers in `MainWindow.xaml.cs`, and `Strings/en-US.xaml`.*
