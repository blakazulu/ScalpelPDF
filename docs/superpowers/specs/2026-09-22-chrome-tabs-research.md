# Scalpel: real multi-document tabs - research for the implementation plan

Scope: C:/Code/Personal/ScalpelPDF at HEAD 98ee560 plus the uncommitted working tree (MainWindow.Tabs.cs has +71
uncommitted lines: `TabViewState`, `_tabState`, `RememberTabState`, `RestoreTabState`, `CloseOtherTabs`).
All line numbers are for the working tree as of 2026-09-22. "xaml.cs" means MainWindow.xaml.cs.

---------------------------------------------------------------------------------------------------

## A. Inventory of per-document state

### A.0 Key architectural facts that make tabs cheaper than they look

1. **No long-lived PDFium/Docnet handle exists.** Every render opens a fresh Docnet reader on the
   *working file path* and disposes it inside the same `PdfiumGate.Run` call
   (RenderPage FileOps.cs:900-908; thumbnails FileOps.cs:846-879; secondary tiles FileOps.cs:1063-1110;
   continuous RenderContinuousPages ViewMode.cs:210+). So the whole PDFium side of a document is just
   the string `_currentFile`. A background tab holds zero native handles.
2. **The editable model is PdfSharpCore `_doc` (in memory) plus pure-data overlays.** `PdfReader.Open(path, ...)`
   in PdfSharpCore 1.3.67 (Scalpel.csproj:28) opens the file inside a `using` FileStream and, in Modify mode,
   reads all indirect objects, so no file lock is held after open (believed; add a unit test that renames/deletes
   the source after `PdfReader.Open` to prove it). A `PdfDocument` is an ordinary managed object graph: keeping
   N of them alive is only a memory question, not a correctness one.
3. **Annotations are plain data.** `PageAnnotation` and subclasses (Models/EditingTypes.cs:8-110) hold only
   primitives, `Point`, `Rect`, `List<Point>` and base64 image strings - no UIElement references. The WPF
   overlays are regenerated from them by `RenderAllAnnotations` (AnnotationManagement.cs:106).
4. **Form values are plain dictionaries** (xaml.cs:107-109) and overlays are rebuilt by `RenderFormFields`
   (Forms.cs:49) on every render.
5. **Edits that change page structure go through a temp working copy**: `SaveTempAndReload`
   (TempReload.cs:72) saves `_doc` to `App.MakeTempFile("temp")`, reopens it, and points `_currentFile`
   at it. So `_doc` and `_currentFile` are always kept in sync and a session is fully described by
   (`_doc`, `_currentFile`, `_originalFile`, overlay dictionaries, rotation map, undo stacks, flags).
6. **No per-document static singletons.** A grep of static mutable fields in Services/*.cs and the root
   *.cs finds only app-wide state (Logger, ThemeManager, LocaleManager, PdfiumGate worker, App._sessionTemps
   App.xaml.cs:815, `_availableFamiliesCache` FileOps.cs:1305, `PageThumbnailVm._loadSem`
   PageThumbnailVm.cs:21). `SearchService` (Search.cs:183) is stateless: it re-reads `_currentFile` per query.

### A.1 Tier 1: document MODEL fields (must live in the tab session, never reset on switch) - 16 fields

| # | Field | Type | Declared | Notes |
|---|-------|------|----------|-------|
| 1 | `_doc` | `PdfDocument?` (PdfSharpCore) | xaml.cs:25 | 320 refs / 29 files. Live editable model. |
| 2 | `_currentFile` | `string?` | xaml.cs:26 | Working copy path (temp after any page op / decrypt / repair). Render source. 67 refs. |
| 3 | `_originalFile` | `string?` | xaml.cs:27 | User's real path (tab identity, Save target). "Untitled.pdf" for NewDocument (FileToolbar.cs:63). 36 refs. |
| 4 | `_openedProtected` | `bool` | xaml.cs:51 | Password/owner-restricted at open. Set AFTER FinishOpenFile (FileOps.cs:104, 132). |
| 5 | `_docHasFormFields` | `bool` | xaml.cs:55 | Drives AnnotationRenderPolicy.ForViewer. |
| 6 | `_annotations` | `Dictionary<int, List<PageAnnotation>>` (readonly) | xaml.cs:75 | Unsaved overlay annotations. 45 refs. |
| 7 | `_renderDims` | `Dictionary<int,(int w,int h)>` (readonly) | xaml.cs:76 | Canvas coordinate space per page; annotation coordinates are relative to it (continuous view writes canonical 2048 dims, ViewMode.cs:147-150). Keep per session so annotations stay consistent before first re-render. 41 refs. |
| 8 | `_pageRotations` | `Dictionary<int,int>` (readonly) | xaml.cs:80 | /Rotate values stripped from the working copy (TempReload.cs:89-95). Losing it = wrong orientation. |
| 9 | `_formTextValues` | `Dictionary<int,string>` | xaml.cs:107 | Typed form text. |
| 10 | `_formCheckValues` | `Dictionary<int,bool>` | xaml.cs:108 | |
| 11 | `_formRadioValues` | `Dictionary<string,string>` | xaml.cs:109 | |
| 12 | `_formAppearanceFailed` | `bool` | Forms.cs:615 | Save-time fallback flag. |
| 13 | `_undoStack` | `Stack<UndoEntry>` | xaml.cs:130 | Entries may carry full-document `byte[]` (DocBytes); budget 40 entries / 256 MB (xaml.cs:137-138) PER STACK. |
| 14 | `_redoStack` | `Stack<UndoEntry>` | xaml.cs:131 | Same budget. Note: FinishOpenFile clears `_undoStack` but NOT `_redoStack` (FileOps.cs:627) - an existing leak of redo history across documents. |
| 15 | `_isDirty` | `bool` | xaml.cs:263 | Tab dirty dot source. |
| 16 | `_blankCache` | `Dictionary<int, List<TextEntryPlaceholder.Candidate>>` | TextBox.cs:32-33 | Per-document PdfPig cache; can be dropped on switch (rebuildable) but cheapest to keep per session. |

`UndoEntry` itself (xaml.cs:128-129) is a record struct with `PageIdx`, `DocBytes`, `WasDirty`, `Annotation` - pure data, movable.

### A.2 Tier 2: per-document VIEW state (should travel with the tab so switching is "where I left it") - 6 fields + 5 UI-held values

| # | Field / value | Type | Declared / held in | Notes |
|---|---------------|------|--------------------|-------|
| 17 | `_zoomLevel` | `double` | xaml.cs:31 | Also persisted app-wide in SaveWindowSettings (Settings.cs:385). |
| 18 | `_fitMode` | `FitMode` | xaml.cs:37 | Persisted app-wide (Settings.cs:384). |
| 19 | `_viewMode` | `ViewMode` | xaml.cs:41 | Today GLOBAL: loaded from registry in ctor (xaml.cs:299-300), saved by SetViewMode (ViewMode.cs:33). Recommend per tab, new tabs inherit the global default. |
| 20 | `_allSearchRects` | `Dictionary<int, List<(l,b,r,t)>>` | xaml.cs:266 | Whole-document search hits. |
| 21 | `_searchResultPages` | `List<int>` | xaml.cs:267 | |
| 22 | `_searchPageCursor` | `int` | xaml.cs:268 | |
| UI | selected page | `PageList.SelectedIndex` | MainWindow.xaml:759 | Current tab state records it (Tabs.cs:43). |
| UI | scroll offsets | `PagePreviewPanel.HorizontalOffset/VerticalOffset` | MainWindow.xaml:944 | Not captured today. In Continuous view the page index plus an intra-page fraction is more robust than a raw offset (offset depends on zoom and slot heights). |
| UI | search query | `_searchBox.Text`, `_searchBar` visibility | xaml.cs:231-233 (controls built in Search.cs) | |
| UI | sidebar pane | `_sidebarShowingOutlines` (xaml.cs:215) | | Could stay window-wide; must fall back to Pages when the incoming doc has no outline (LoadOutlines Outline.cs:150-153 disables the tab). |
| UI | sidebar list scroll | `SidebarScrollViewer` (PageSelection.cs:30-33) | | Nice to have. |

The uncommitted `TabViewState(PageIndex, Zoom, Mode)` (Tabs.cs:31) already covers page/zoom/view mode, keyed by path.

### A.3 Tier 3: per-document DERIVED / TRANSIENT fields (reset or rebuilt on switch; never persisted) - 62 fields

Render pipeline (must be cancelled/stopped on switch - see A.6):
- `_lastRenderZoom` double xaml.cs:32; `_rerenderTimer` DispatcherTimer? xaml.cs:38; `_secondaryRenderCts` CTS? xaml.cs:39;
  `_continuousRenderCts` CTS? xaml.cs:68; `_thumbCts` CTS? FileOps.cs:805; `_zoomWheelRemainder` double xaml.cs:60;
  `_zoomRestoreGate` DeferredActionGate xaml.cs:65; `_suppressScrollToPage` bool xaml.cs:47.
- Continuous view layout: `_continuousTops` List<double> xaml.cs:69; `_continuousScrollTarget` int xaml.cs:70;
  `_continuousPageW` double xaml.cs:71; `_continuousCanvases` Dictionary<int,Canvas> xaml.cs:247; `_activeCanvas` Canvas xaml.cs:245.
- Overlays: `_linkOverlays` List<Canvas> xaml.cs:211; `_searchHighlights` List<Rect> xaml.cs:234; `_outlinesFitted` bool xaml.cs:216.

Interaction-in-progress (must be committed or cancelled BEFORE switching):
- Drawing: `_isDrawing` xaml.cs:148, `_drawStart` :149, `_activePreview` :150, `_activeInk` :151.
- Typewriter / inline edit: `_activeTextBox` TextBox? xaml.cs:152, `_reeditOriginal` TextAnnotation? :165. Commit via `CommitActiveTextBox()` (used at FileToolbar.cs:395).
- Selection: `_selectedAnnotation` :153, `_selectionBorder` :154, `_isSelecting` :225, `_selectStart` :226, `_selectRect` :227, `_selectedText` :228.
- Resize: `_isResizingSig` :170, `_resizeSigStart` :171, `_resizeSigStartScale` :172, `_resizeSigAnnot` :173, `_resizeHandles` :174, `_resizeCorner` :175, `_resizeAnchor` :176.
- Drag-move: `_isDraggingAnnot` :179, `_dragAnnotStart` :180, `_dragAnnotOrigPos` :188, `_dragAnnot` :189.
- Crop: `_cropCanvasRect` :192, `_cropPreviewRect` :193, `_cropPreviewRectBorder` :194, `_cropBrackets` :195, `_cropConfirmBar` :196,
  `_cropHandles` :199, `_activeCropHandleTag` :200, `_cropHandleDragStart` :201, `_cropRectAtHandleDrag` :202, `_cropPageIndex` :203,
  `_cropX1Box/_cropY1Box/_cropX2Box/_cropY2Box` :204 (4 fields), `_cropRangeBox` :205, `_updatingCropInputs` :206, `_cropBarDragging` :207, `_cropBarDragOffset` :208.
- Measure: `_measureLine` Line? Measure.cs:21, `_measureStart` Measure.cs:22.
- OCR: `_ocrRegionArmed` bool Tools.cs:845, `_ocrRegionPage` int Tools.cs:846, `_ocrCts` CTS? Tools.cs:717 (operation-scoped, see A.5).

### A.4 App/window-wide state (stays on MainWindow, shared by all tabs)

- Mode/tool: `_mode` xaml.cs:43, `_suppressModeEvents` :44, `_currentTool` :74 (reset to Select on switch if it is Crop/Measure/OCR-region, else keep).
- Tool settings: `_drawColor` :157, `_drawWidth` :158, `_drawOpacity` :159, `_highlightColor` :160, `_drawSettingsBar` :161, `_textFontSize` :164, `_textColor` :166, `_textSettingsBar` :167.
- Pan input: `_isPanning` :183, `_spaceHeld` :184, `_panStart` :185, `_panScrollH` :186, `_panScrollV` :187 (cancel on switch, but window-owned).
- Page-list drag: `_dragStartPoint` :28 (DragDrop.cs:52).
- Sidebar: `_sidebarCollapsed` :214, `_savedPagesWidth` :217, `_savedOutlinesWidth` :218, `_sidebarSv` PageSelection.cs:30.
- Search chrome: `_searchBar` :231, `_searchBox` :232, `_searchStatus` :233, `_searchService` Search.cs:183.
- Signatures: `_signatureStore` :237, `_pendingSignature` :238, `_signaturePopup` :239.
- Control refs (readonly, set in ctor xaml.cs:276-298): `_annotationCanvas` :242, `_pageContentGrid` :248, `_continuousPanel` :67, `_pageContentPanel` :222,
  `_toolSelectBtn`..`_toolImageBtn` :249-254, `_toolCropBtn` :197, `_toolLineBtn` :198, `_saveAsBtnRef` :255, `_closeFileBtnRef` :256, `_zoomBox` :257,
  `_portableBadge` :258, `_pageJumpBox` :259, `_pageTotalLabel` :260, `_sidebarToggleBtn` :219, `_sidebarBorder` :220, `_sidebarCol` :221.
- Wheel: `_wheelFlipGate` :57 (window input, fine shared). `_suppressLogToggleEvent` :66.
- Full screen: `_fullScreen`, `_fsRow0.._fsSplitterW`, `_fsSidebarMin`, `_fsPrevState`, `_fsPrevTopmost`, `_fsPrevResize`, `_fsPrevLeft/Top/W/H`, `_fsSidebarVis/_fsSplitterVis`, `_fsHintBorder` (FullScreen.cs:14-22).
- Timers/misc: `_fileSizeTimer` FileOps.cs:1216 (status shows the ACTIVE file size - re-run ShowFileSizeStatus on switch), `_toastTimer` FileOps.cs:1259, `_pendingUpdate` Update.cs:17.
- Statics: `SwatchColors`, `_swatchDimBorder`, `_drawBarBackground`, `_thumbBorderBrush` (DrawBar.cs:29-39), `TextFontSizes` (TextBar.cs:29), `_availableFamiliesCache` (FileOps.cs:1305).
- Tab infrastructure (to be replaced): `_openTabs` List<string> Tabs.cs:25, `_tabState` Dictionary Tabs.cs:33.
- Constants (ignored): ZoomMin/Max/Step xaml.cs:33-35, FormOverlayTag :110, FormOverlayZIndex :113, UndoMax* :137-138, Sidebar* Outline.cs:32-33, WM_* Settings.cs:198-203, 330-341, FPDF_REMOVE_SECURITY FileOps.cs:311, Key* Update.cs:13-15.

### A.5 State held in UI controls (per document, must be rebound on switch)

| Control | What it holds | Built by | On switch |
|---------|---------------|----------|-----------|
| `PageList.ItemsSource` (xaml:759) | `PageThumbnailVm[]`, each with a 128x256 `BitmapSource` (PageThumbnailVm.cs:23) and the working `filePath` (:34) | RefreshPageList FileOps.cs:807 | Either keep the array in the session (cheap, ~130 KB/page at 128x256 BGRA; 500-page doc ~65 MB, so cap it) and re-assign, or drop and regenerate. Note RefreshPageList carries old thumbnails forward (:829-840) - must NOT carry tab A's thumbs into tab B. |
| `PageImage.Source` (xaml:967) | Primary page WriteableBitmap up to 6144 px | RenderPage FileOps.cs:886 | Drop (null) and re-render. |
| `_pageContentPanel` children | Secondary tiles (Two-page/Grid) | RenderAdditionalPages FileOps.cs:1004, ClearSecondaryPages :974 | Clear + re-render. |
| `_continuousPanel` children (xaml:977) | One slot per page with Image + overlay Canvas | SetupContinuousView ViewMode.cs:117 | Clear + rebuild (it is a full-document layout; rebuilding is what OpenFile already does). |
| `_annotationCanvas` / `_continuousCanvases` children | Annotation visuals, form overlays (tag FormOverlayTag), link overlays, search highlights, selection border, resize handles, crop visuals | RenderAllAnnotations, RenderFormFields, RenderPageLinks | Clear + regenerate from model. |
| `OutlineTree.Items` (xaml:802) + `SidebarOutlinesTab.IsEnabled` | TreeViewItems built from `_doc.Outlines` | LoadOutlines Outline.cs:137 | Rebuild via LoadOutlines (cheap) or cache the item list per session. |
| `FileNameLabel` (xaml:485), `_pageTotalLabel`, `_pageJumpBox`, SaveAsBtn foreground (DirtyTracking.cs:32-36), status text, file-size status | Header/status | FinishOpenFile FileOps.cs:624, 641-643; MarkDirty | Refresh from session. |
| Search bar text + highlights | query, rects | Search.cs | Restore query text, redraw highlights from session's `_allSearchRects`. |
| `OcrProgressOverlay` (xaml:1477), flatten/print overlay (FileToolbar.cs:714) | Long-op UI | | Block switching while visible (see A.6). |

### A.6 Things that make snapshot/restore hard (hazards)

1. **Background render tasks write into shared UI.** `_thumbCts`, `_secondaryRenderCts`, `_continuousRenderCts`
   and `_rerenderTimer` all target the single set of controls. OpenFile already cancels two of them (FileOps.cs:46-50);
   a switch must cancel all four and stop the timer, and every render callback must also check that the
   session it started for is still active (capture `var s = _session;` and bail if `s != _session`), because
   cancellation is cooperative and a Dispatcher.Invoke can already be queued.
2. **Async user operations that write back after `await`** with no session identity check:
   - Compress `ToolsCompress_Click` Tools.cs:641-683 (no blocking overlay; `AdoptTransformedFile` at :673 after await).
   - OCR `ToolsOcr_Click` Tools.cs:722-750 (overlay OcrProgressOverlay but keyboard Ctrl+Tab still reaches the window; adopt at :748).
   - Redact/other at Tools.cs:1151-1157; Straighten.cs:103; Form OCR FormOcr.cs:64-83 writes `_formTextValues` and `MarkDirty` after await.
   - Flatten FileToolbar.cs:581-713 and Print :782+ reassign `_doc`/`_currentFile` before the await and use captured paths after.
   - Compare.cs:57, ExportImages.cs:89 (read-only results, lower risk).
   With tabs, any of these can land in the WRONG document. Fix: a window-level "busy" gate (disable tab strip + tab
   shortcuts while a long op runs) AND make `AdoptTransformedFile`/form-OCR write into the session captured at the
   start of the operation (session-targeted adoption).
3. **Undo memory multiplies.** Each tab has two stacks at up to 256 MB each (xaml.cs:137-138). Ten tabs could pin
   GBs of `DocBytes`. Needs a global budget across sessions (see D.4).
4. **Temp files are only deleted at process exit.** `App.MakeTempFile` registers into a static list
   (App.xaml.cs:815-831), `CleanupSessionTemps` deletes all at exit (:833). With tabs, closing a tab should
   delete that tab's temps (working copy, dec, repaired, undo, clean/burned) - needs a per-session temp list
   and an `App.ReleaseTempFile(path)` to unregister+delete.
5. **`_doc` reassignment is scattered** across 12 sites (FileOps OpenFile branches :72-207, repair :496-604,
   TempReload.cs:115-138, AnnotationManagement.cs:303-398 undo/redo, FileToolbar.cs:62, 420, 556, 608, 802,
   Tools.cs:42, 56). With a property shim (D) these keep working unchanged because they assign through the
   property into the active session.
6. **`FinishOpenFile` is used for both "new document" and "tool rewrote the current document"**
   (`AdoptTransformedFile` Tools.cs:53-60 calls it, which wipes annotations/undo and calls AddTab). With real
   tabs, "adopt" must NOT create a new tab; split FinishOpenFile into `ResetSessionForNewContent` +
   `BindActiveSessionToUi`.
7. **Global settings coupling**: `SetViewMode` persists to registry (ViewMode.cs:33); SaveWindowSettings
   persists `FitMode`, `ZoomLevel`, `LastFile` (Settings.cs:384-389 - and `LastFile` stores `_currentFile`,
   the working path, not `_originalFile`; after any page edit the temp is skipped by IsTransientPath so it is
   silently not updated).
8. **Several fields are `readonly` collections** (`_annotations`, `_renderDims`, `_pageRotations`, form dicts,
   undo/redo stacks, search collections). Capture/restore by copying contents is O(n) and error prone; swapping
   references requires dropping `readonly` - both handled by the property shim in D.
9. **No FileSystemWatcher** exists (grep: none), so nothing to detach. No file locks after open (A.0.2).

---------------------------------------------------------------------------------------------------

## B. Open / close / dirty / save paths today

### B.1 Open
- Entry points: `Open_Click` FileToolbar.cs:73-77 (single-select dialog, `Multiselect` not set);
  `DropZone_Drop` DragDrop.cs:35-43 (takes `files[0]` only; wired to both the empty-state DropZone xaml:897 and
  the document area PagePreviewPanel xaml:957); `OpenRecent` Recent.cs:41-47; command line in the ctor
  `Loaded` handler xaml.cs:328-370 (first existing file only, else `LastFile`); single-instance forward
  `HandleForwardedLaunch` SingleInstance.cs:18-49 via `SingleInstanceProtocol.PickLaunchTarget`
  Services/SingleInstance.cs:42-56 (first existing file only); `SwitchToTab` Tabs.cs:98-126; ScreenshotHarness.cs:61.
- **Existing data-loss bug**: `Open_Click`, `DropZone_Drop` (including a drop onto an open document) and
  `OpenRecent` call `OpenFile` with NO dirty check - `OpenFile` itself never checks `_isDirty`. Only
  `NewDocument` (FileToolbar.cs:44), `SwitchToTab` (Tabs.cs:113), `CloseFile` (DirtyTracking.cs:55),
  `HandleForwardedLaunch` (SingleInstance.cs:28) and `OnClosing` (WindowChrome.cs:66) prompt. Real tabs fix
  this naturally (every open goes to a new tab).
- `OpenFile(path)` FileOps.cs:42-224: cancels continuous/secondary renders (:46-50); copies network paths to a
  local temp (:56-65); fallback chain Modify (:72) -> encrypted strip via PDFium/Import (:76-90) ->
  owner-password ReadOnly (:94-112) -> user password + decrypted temp copy (:113-138) -> XRef: PDFium re-save
  (:139-163) then ReadOnly (:165-176) then repair prompt (:177-192) -> EOF: PDFium re-save / import / raster
  (:194-219). Every branch closes the previous `_doc` first ("if (_doc is not null) { _doc.Close(); _doc = null; }").
  **This is the main thing a tab-aware open must change: never close the previous document; build the new one into
  a fresh session.**
- Repair helpers: `TryRepairAndOpen` FileOps.cs:490-532, `RepairViaDocnetRasterize` :540-612 (both end in FinishOpenFile).
- `FinishOpenFile(displayPath, workingPath)` FileOps.cs:618-679: recent list, sets `_currentFile`/`_originalFile`,
  header label, clears `_annotations`, `_undoStack` (NOT `_redoStack`), `_renderDims`, blank cache, `_pageRotations`,
  `_openedProtected`, recomputes `_docHasFormFields`, clears form dicts and search state, `ClearSecondaryPages`,
  `ClearSelection`, `RefreshPageList`, `LoadOutlines`, shows the preview, `MarkDirty(false)`, selects page 0,
  bootstraps continuous view (:651-658), queues fit (:662-674), status + log, `AddTab(_originalFile)` (:678).
  It mixes three concerns: (a) reset model state for new content, (b) bind the model to the UI, (c) tab registration.
- NewDocument FileToolbar.cs:42-71 -> FinishOpenFile("Untitled.pdf", temp). `AddTab` skips non-existent paths
  (Tabs.cs:80-81), so an Untitled document gets no tab today.

### B.2 Page-structure edits and working copies
- `SaveTempAndReload(keepAnnotations)` TempReload.cs:72-173: `MarkDirty`, strip /Rotate into `_pageRotations`,
  save to `MakeTempFile("temp")`, reopen with xref recovery, set `_currentFile`, `RefreshPageList`, re-select,
  re-layout. Callers: ContextMenu.cs:119, Crop.cs:604/627, DragDrop.cs:92, FileToolbar.cs:104/337/355/373/385,
  Links.cs:186, Tools.cs:1008.
- `PushDocUndo` AnnotationManagement.cs:45-52 snapshots `_doc` bytes; Undo/Redo AnnotationManagement.cs:254-427
  reopen from a temp and reset overlays.
- Tools: `BuildWorkingSourceFile` Tools.cs:27-49, `AdoptTransformedFile` Tools.cs:53-60 (FinishOpenFile + MarkDirty).

### B.3 Dirty
- `MarkDirty(bool)` DirtyTracking.cs:29-37 sets `_isDirty` and recolors `SaveAsBtn`. Called from AddAnnotation
  (AnnotationManagement.cs:37), SaveTempAndReload, form edits, undo/redo, saves, opens.
  **Hook**: MarkDirty must also refresh the tab chip's dirty dot (active session only; background sessions never
  change dirty state because they cannot be edited).

### B.4 Save
- `SaveInPlace` FileToolbar.cs:389-459 -> writes to `_originalFile` (falls back to SaveAs if empty). With
  annotations: save clean temp, burn, save target, reopen clean temp as `_doc` (:405-423). On a post-save reload
  failure it calls `OpenFile(saveTarget)` (:453) - with tabs that must become "reload into the SAME session".
  Note: for NewDocument `_originalFile == "Untitled.pdf"` (not empty), so SaveInPlace would write a relative
  "Untitled.pdf" - worth routing to SaveAs when the session is Untitled.
- `SaveAs_Click` FileToolbar.cs:521-579 sets `_originalFile` = new path (:559, :567) and `FileNameLabel` but does
  NOT update `_openTabs` - today the tab keeps the old path (stale tab label, and a later switch reopens the old
  file). Tab identity must be the session object, not the path.
- `RemovePassword_Click` FileToolbar.cs:487-519, `SaveFlattened_Click` :581-713 (reassigns `_doc`), `Print_Click` :782+.

### B.5 Close
- `CloseFile()` DirtyTracking.cs:44-104: if >1 tab, routes to `CloseTab(_originalFile)` (:50-54); else dirty prompt
  (:55-61), `_doc.Close()`, clears model + UI (:62-100), `_openTabs.Clear()`, `RefreshTabStrip()`. Does not clear
  `_redoStack`, `_pageRotations`, `_openedProtected`, `_linkOverlays`, continuous canvases.
- `CloseTab(path)` Tabs.cs:131-165: background tabs "hold no unsaved state" (true today, false with live sessions).
- Window close: `OnClosing` WindowChrome.cs:64-79 prompts once for the ACTIVE `_isDirty` only, then SaveWindowSettings.
  `Closed` handler xaml.cs:312 closes `_doc` and calls `App.CleanupSessionTemps()`.
  **Hook**: must iterate every session; recommended UX is one dialog listing dirty tabs (Save all / Discard all /
  Cancel) or per-tab prompts activating each dirty tab in turn (upstream approach - see C).

### B.6 Where a session switch must hook in
1. **Before leaving** the active session: `CommitActiveTextBox()`; cancel crop/measure/OCR-region/drawing/drag/
   resize/selection (`ClearSelection` Selection.cs:224, `ClearTextSelection` :305, crop cancel in Crop.cs,
   `HideDrawSettings`/`HideTextSettings`/`HideSignaturePopup` as CloseFile does DirtyTracking.cs:80-83);
   cancel `_thumbCts`, `_secondaryRenderCts`, `_continuousRenderCts`, stop `_rerenderTimer`, invalidate
   `_zoomRestoreGate`; capture view state (page index, zoom, fit mode, view mode, scroll offsets, search query,
   sidebar pane) into the session; optionally keep `PageList.ItemsSource` array in the session.
2. **Swap** `_session = next` (with the property shim every model field now reads from `next`).
3. **Bind** the incoming session to the UI (the "(b)" half of FinishOpenFile): header label, `_pageTotalLabel`,
   `_pageJumpBox`, `MarkDirty(session.IsDirty)` (UI part only), thumbnails (re-assign cached array or
   RefreshPageList without carrying old thumbs), `LoadOutlines`, `_docHasFormFields`, search bar text +
   highlights, clear `_annotationCanvas`/`PageImage`/secondary tiles/continuous panel, then apply the view mode
   and re-render at the saved page/zoom/scroll (reuse `SetViewMode` + `SetupContinuousView(page)` or
   `RenderPage(page)`, then restore scroll at DispatcherPriority.Loaded like RestoreTabState Tabs.cs:52-65),
   `ShowFileSizeStatus`, window title/status.
4. Guard: refuse to switch while a long operation is running (busy gate), while a modal is up, or while
   `_isDrawing`/drag is in progress (mouse capture).

---------------------------------------------------------------------------------------------------

## C. Upstream KillerPDF v1.8.61 tab implementation

Source: clone at scratchpad/killerpdf, read with `git show v1.8.61:<path>`. "Tabs.cs" = Controls/Viewer/PdfViewer.Tabs.cs,
"TabStrip.cs" = PdfViewer.TabStrip.cs, "TabsApi.cs" = PdfViewer.TabsApi.cs. Upstream is net10 with its own engine and
a split view (two panes A/B, each a `PdfViewer` with its own tab list), so the code is not directly portable, but
the model and the bug history are.

### C.1 Session model: `PdfViewer.DocumentSession` (Tabs.cs:36-179)

`internal sealed class DocumentSession : INotifyPropertyChanged`, nested in PdfViewer. Each pane owns
`ObservableCollection<DocumentSession> _sessions` (Tabs.cs:184) and `DocumentSession? _active` (Tabs.cs:185).

- Identity: `Doc` PdfWorkingDocument? (:38, the whole file as `byte[]`), `CurrentFile` (:39, working/temp path),
  `OriginalFile` (:40, user path, Save target, tab title/tooltip), `DeferredPath` (:43, restored-but-not-loaded tab).
- View: `ZoomLevel` (:45), `LastRenderZoom` (:46), `Fit` (:47), `View` (:48), `GridColumns` (:49), `Tool` (:50, the
  active tool is per tab), `PageIndex` (:51), `IsDirty` (:52), `ProtectedSource` (:53), `ScrollH`/`ScrollV` (:54-55),
  `SearchPageCursor` (:56).
- Collections (swapped BY REFERENCE, not copied): `Annotations` (:58), `RenderDims` (:59), `RenderCache`
  ConcurrentDictionary<(page,bucket,rot),BitmapSource> (:64), `RenderCacheSize` (:69, #189), `PageRotations` (:70),
  six form dictionaries (:71-76), `UndoStack`/`RedoStack` (:77-78), `AllSearchRects` (:79), `SearchResultPages` (:80).
- Sidebar thumbnails per tab: `ThumbCache` PageThumbnailVm[]? (:85), `ThumbCacheFile` (:86), `ThumbCts` (:87),
  `ThumbCacheComplete` (:88). Moved into the session because one shared CTS per pane let a newly opened tab cancel
  another tab's thumbnail loader (comment :82-84).
- Presentation (notifying, bound by the XAML template): computed `Title` (:90-93), `TabLabel` = "• " prefix when
  dirty + title (:103, set in `RefreshTabLabel` :112-116), `TabTip` (:107), `IsActive` (:120), `IsFirst`/`IsLast`
  (:129/:136), retro-chrome flags (:142-154), `PaneFocused` (:160), `PaneDimmed` (:166), `IsStripVisible` (:174).

**Live vs captured.** The viewer still has "live" fields (`_doc`, `_annotations`, `_undoStack`, `_isDirty`,
declared in PdfViewer.Bridge.cs / Bridge2.cs, e.g. `_isDirty` Bridge2.cs:114), and MainWindow reaches them through
forwarding properties (MainWindow.xaml.cs:296 `_isDirty => ActiveViewer.IsDirtyRef`). So upstream is a HYBRID of
the capture/restore and shim ideas: `CaptureSessionState` (Tabs.cs:192-238) copies scalars by value and collections
by reference into the session (and writes a per-file "DocStates" registry entry, :235-237); `ApplySessionState`
(Tabs.cs:304-338) assigns them back, closes the active engine view (`CloseEngineDocumentSession` :311), clears
back/forward nav history (:333-334) and touches the render LRU (:337). Selection, text-edit handles, crop state,
the active textbox and the visual tile tree are not captured - they are rebuilt per switch.

Per-file view state also survives restarts: `SaveDocState`/`TryGetDocState` (Tabs.cs:245-301), one registry value of
`path|fit|zoom|view|page|scrollH|scrollV` lines capped at 40 (`DocStatesMax`), read back by FinishOpenFile
(Shell/FileOperations.cs:194-200).

### C.2 Lifecycle

- **Switch** `SwitchToTab` (Tabs.cs:742-775): return if already active; `Host?.FocusViewer(this)` (#161, :753);
  `CommitActiveTextBox()` + `CancelTransientForSwitch()` (:467-477: clear selection/text selection, close search bar,
  draw/text/signature popups); `CancelRenderWork()` (:482-497: cancel viewport-restore gate, stop rerender timer,
  cancel+dispose secondary/continuous/sharpen CTSs and `_thumbCts`); `CaptureSessionState(_active)`;
  `SetActiveSession(target)` (:735-739); `ApplySessionState(target)`; hide content `PageContentGrid.Opacity = 0`
  (:764-765); if deferred `MaterializeDeferred` (:798-814, OpenFile then capture) else `RenderActiveSession()`
  (:556-586: take a DeferredActionGate generation, snapshot ScrollH/ScrollV BEFORE the rebuild (#399), set label,
  clear annotation canvas, `MarkDirty(_isDirty)`, `BootstrapDocumentView(PageIndex, autoFit:false, restoreVerticalOffset)`,
  `SetTool(_active.Tool)`, queue a ContextIdle scroll restore guarded by `ReferenceEquals(_active, session)` + gate
  generation); `RebuildTabStrip()`; `FadeInDocContent()` (:780-794, 140 ms fade at ContextIdle, after the scroll restore).
- **Open** `OpenInNewTab` (Tabs.cs:690-718): capture active; `FindOpenSession` (:673-681, case-insensitive full path).
  If already open and CLEAN, switch there and toast "Already open"; a DIRTY duplicate opens as a second copy
  (later needs a save-conflict warning, `OtherPaneHasDirtyCopyOf` MainWindowTabStubs.cs:88-95). `BeginTabLoad`
  (:633-660) reuses the active tab if it is empty, else creates a session inheriting View/Fit, activates it and applies
  it (blanking live fields). `AbortTabLoad` (:663-670) removes it and restores the previous tab on failure. Async
  opens (decrypt/repair) finish in `FinalizeAsyncOpen` (FileOperations.cs:357-363).
- **Close** `CloseTab` (Tabs.cs:827-833) -> `CloseTabCore` (:835-902): re-entrancy flag `_closingTab` + membership
  check (#353); a non-active target is made live and rendered first (:848-856) so the user sees what they are asked
  about; dirty prompt YesNo "Close this file without saving?" (:863-869, NO Save option); re-validate index and
  active-ness after the modal (:871-872); `CancelRenderWork`, `_doc.Close()`, remove from `_sessions` and `_renderLru`,
  clear bitmap cache, `CompactLohSoon()` (:434-442). Last tab: remove "LastFile", add a blank session,
  `ShowEmptyState()` (:884-892). Otherwise activate `_sessions[Math.Min(idx, Count-1)]` (right neighbour, else left, :895).
- **Close others / all**: `CloseOtherTabs` (:819-823) calls CloseTab per tab (each dirty one prompts); `CloseAllTabs`
  Ctrl+Q (:906-935) one combined warning.
- **Window close** (Shell/WindowChrome.cs:659-726): `Viewer.CaptureActiveIfAny(); ViewerB.CaptureActiveIfAny();`
  (:669-670, comment calls the missing capture a data-loss bug); `anyDirty = _isDirty || AllSessions().Any(s => s.IsDirty)`
  (MainWindowTabStubs.cs:63-67) -> ONE YesNo prompt defaulting to No, no per-tab list, no Save. Then a quit prompt
  with "close my open tabs"/"remember" (#105). `SaveWindowSettings` (MainWindow.xaml.cs:554-639) persists `OpenTabs`/
  `ActiveTab` (and B-pane) as `|`-joined paths; startup (MainWindow.xaml.cs:486-521) restores them as DEFERRED
  sessions (`MakeDeferredSession` TabsApi.cs:247), materializing only the active one.
- **Save/Save As**: do not touch the strip. SaveInPlace (FileOperations.cs:874-949) writes `_originalFile`, reopens a
  clean temp, `MarkDirty(false)`; SaveAs (976-1073) sets `_originalFile`. Tab title/tooltip update on the next
  `RebuildTabStrip` after a capture.
- **Dirty dot**: `MarkDirty` (Shell/DirtyTracking.cs:27-54) only recolours the Save button; the tab's "• " prefix only
  changes on capture + rebuild, so the ACTIVE tab's dot lags until the next switch/open/close (an upstream bug; do not copy).

### C.3 Memory strategy

- Parsed document: kept for every loaded tab (`s.Doc`, whole file as byte[]).
- Engine session: only the active tab (closed in ApplySessionState :311, reopened lazily).
- Bitmaps per tab: LRU render cache `CacheRender` (Tabs.cs:371-398), cap 48 pages (`RenderCachePageCap` :355) and
  ~160 MB (`RenderCacheByteBudget` :361), floor 6 pages; evicts the page farthest from the one just cached; sizes are
  recorded on the inserting thread (#189: `PixelWidth` on an unfrozen bitmap from another thread threw).
- Bitmaps across tabs: only the 3 most-recently-used tabs keep caches (`RenderCacheTabCap` :346, `TouchRenderLru`
  :413-428); evicted tabs clear their cache and schedule `CompactLohSoon` (one-shot LOH compaction + GC.Collect at
  ApplicationIdle, #122).
- Invalidation: `InvalidateRenderCache(s)` (:445-449) on pixel/order edits; `FlushAllRenderCaches` (:403-410) on invert toggle.
- Thumbnails: kept per tab, not LRU-evicted. No tab-count cap; drag-drop asks above 50 files and warns above 30 tabs
  (Shell/ImportAndZip.cs:113-120, 154-158). Deferred (restored) tabs hold only a path.

### C.4 Tab strip UI

- XAML (PdfViewer.xaml:21-260): `ItemsControl x:Name="TabStrip"` bound to `_sessions` (TabStrip.cs:31) in a
  `UniformGrid Rows="1"`; min width 60; tooltip `TabTip`, label `TabLabel` with CharacterEllipsis; close button
  `Tag="{Binding}"` -> `CloseTab_Click` (TabStrip.cs:501-504); active tab ZIndex 1, bold, accent stripe via DataTriggers;
  band hidden unless the pane has more than one tab (TabStrip.cs:54-55).
- Overflow: NO scrolling (reasoning at TabStrip.cs:164-175). `ApplyTabWindow` (:206-251) shows as many tabs as fit at
  a 120 px floor (`TabFloorWidth`), reserves 26 px for a chevron, keeps a contiguous window containing the active tab;
  the chevron menu `TabOverflow_Click` (:280-299) lists hidden tabs (doubles `_` to avoid access keys). Resize re-runs
  it via `TabBarResized` with a re-entrancy guard (:259-272).
- Mouse: middle-click close (`Tab_MouseDown` :466-470); left-click switches on mouse UP (:660-664) so a press can start a
  drag; drag-reorder (`Tab_DragDown/Move/Up` :587-691) with capture, system drag threshold, `_sessions.Move` when the
  leading edge crosses a neighbour's midpoint, 140 ms neighbour slide and 120 ms settle animations; dropping over the
  other pane moves the tab (Shell/PaneDrag.cs:130-152, no reload).
- Context menu (`Tab_RightClick` :472-488): Open Containing Folder (`explorer /select,`, disabled if missing, #399),
  Close Tab (Ctrl+W), Close Other Tabs (Ctrl+Shift+W, disabled under 2 docs).
- Shortcuts (Shell/KeyboardShortcuts.cs): Ctrl+W (:317), Ctrl+Shift+W (:322), Ctrl+Q close all (:327), Ctrl+Tab /
  Ctrl+Shift+Tab (:337/:332) via `CycleTab` (Tabs.cs:721-729, skips empty tabs, wraps), Ctrl+O (:342), Ctrl+N new
  blank doc in a new tab (:425). NOT present: Ctrl+T and Ctrl+1..9 (Ctrl+1 is actual-size zoom, :452, the same
  conflict Scalpel has).
- Multi-file: Open dialog single-select (FileOperations.cs:462-467). Drag-drop `DropZone_Drop` (Shell/DragDrop.cs:70-89)
  -> `OnPathsDropped` (ImportAndZip.cs:76-144): folders expanded, zips extracted, more than one file prompts
  Merge / Separate tabs / Cancel. Command line only `args[1]` (MainWindow.xaml.cs:477-481).
- Single instance (App.xaml.cs:176-297): mutex + named pipe, forwards only the first non-flag arg, 3 s connect timeout;
  pipe server calls `DeliverExternalOpen` -> `RestoreAndActivate` + `OpenFromExternal` (Shell/ExternalOpen.cs:39-66),
  which stashes the path in `_pendingExternalPath` until `Loaded` if the window is not ready (`FlushPendingExternalOpen`).

### C.5 The four referenced fixes

- **#353** (1.8.4, 386d0de0): `ArgumentOutOfRangeException` in `CloseTab` from `CloseFile_Click`. The index was computed
  AFTER the modal dirty prompt; a repeated close (second click or Ctrl+W while the modal pumped messages) or a tab owned
  by the other pane gave idx = -1. Fix: `_closingTab` re-entrancy guard + membership check (Tabs.cs:825-833), re-checks
  in CloseTabCore (:838, :847) and after the prompt `if (idx < 0 || !ReferenceEquals(_active, s)) return;` (:871-872);
  route to the owning pane (MainWindowTabStubs.cs:17-22).
- **#378 / #379** (1.8.4, 3cd263ab, PR merge 7d083edb): in Grid view a tab switch or Explorer open left the PREVIOUS
  document's page 1 in the first tile. Cause: a `_syncingPageList` flag (from #301) made `PageList_SelectionChanged`
  return early before `RenderPage`, and that handler had been the only thing rendering the primary tile on bootstrap;
  the restored `_lastRenderZoom` also kept the re-sharpen timer from masking it. Fix: `BootstrapDocumentView` renders
  the primary tile itself: `RenderPage(_viewMode == ViewMode.Grid ? 0 : page)` (PdfViewer.Viewport.cs:1384).
  Scalpel has the same latent dependency: FinishOpenFile relies on `PageList.SelectedIndex = 0` firing
  `PageList_SelectionChanged` (FileOps.cs:647; PageSelection.cs:76), which is a no-op when the index is already 0.
- **#399** (1.8.5 e8a3b77c, 1.8.6 6546f1db, 1.8.5 2ab9aa6f): scrolling one tab moved others / scroll not restored /
  zoom not remembered / request for Open Containing Folder. Fixes: `PagePreviewPanel_ScrollChanged` writes offsets into
  `_active.ScrollH/ScrollV` filtered on `OriginalSource == PagePreviewPanel` (Viewport.cs:155-165); DocStates got
  scroll; FinishOpenFile restores saved fit; `BootstrapDocumentView` captures `expectedSession = _active`
  (Viewport.cs:1342) and every deferred callback (Loaded/Background/ContextIdle at :1377, :1391, :1440) checks
  `ReferenceEquals(_active, expectedSession)`. Residual bug: layout-driven ScrollChanged during the rebuild wrote the
  OUTGOING tab's offset into the INCOMING session before the restore read it; fixed by snapshotting sh/sv before
  `BootstrapDocumentView` (Tabs.cs:562-565). Plus Open Containing Folder (TabStrip.cs:476-479, 490-499).

### C.6 What ports to Scalpel

Port:
1. A `DocumentSession` class holding model + view state, collections owned by the session (upstream's by-reference
   swap). Every blank session gets fresh collection instances (upstream hit aliasing bugs: `EnsureInitialSession`
   Tabs.cs:458-463, `ApplyActiveSessionIfAny` TabsApi.cs:84-93).
2. The switch sequence: commit textbox -> cancel transient UI -> cancel ALL render work -> capture view state ->
   activate -> bind -> render explicitly (no reliance on `SelectionChanged`, #378) -> restore scroll at ContextIdle
   guarded by session identity + DeferredActionGate generation (#399) -> fade in.
3. Snapshot the restore scroll values BEFORE rebuilding the viewport; ignore ScrollChanged events whose OriginalSource
   is not PagePreviewPanel (Scalpel's `PagePreviewPanel_ScrollChanged` ViewMode.cs:90 does not filter today).
4. Close hardening from #353: re-entrancy flag, membership re-check after every modal, compute indexes after the prompt,
   show the tab being asked about before prompting.
5. Per-session thumbnail cache + per-session thumbnail CTS (Scalpel's single `_thumbCts` FileOps.cs:805 is exactly the
   shared-token bug upstream fixed).
6. Open dedup by full path. Recommendation for Scalpel: ALWAYS switch to the existing tab (clean or dirty) and toast,
   rather than opening a second copy - Scalpel has one pane, so a second copy only creates save conflicts.
7. Deferred/lazy sessions for restored tabs (path only until first activation).
8. Bitmaps only for the active tab (Scalpel has no render cache today, so dropping `PageImage.Source`, secondary tiles
   and the continuous panel on switch is the equivalent of a 1-tab LRU), plus `CompactLohSoon` after closing a tab.
9. Context menu items, middle-click, mouse-up switching, drag reorder with a threshold.
10. Pending-forward queue for single-instance launches that arrive before `Loaded`.

Improve on upstream (do not copy): live dirty dot (refresh the chip from `MarkDirty`), a Save option in the per-tab
close prompt (Save / Don't save / Cancel), a window-close dialog that lists dirty tabs, multi-select Open dialog, all
command-line/forwarded files opened as tabs, tab overflow via horizontal scroll plus a chevron list (Scalpel already
hosts the strip in a ScrollViewer, MainWindow.xaml:888-892).

---------------------------------------------------------------------------------------------------

## D. Recommended architecture for Scalpel

### D.1 Options weighed

**Option 1 - Capture/restore ("swap fields in and out").** Keep every field on MainWindow; `DocumentSession` is a bag
of the same fields; on switch copy MainWindow -> outgoing session, incoming session -> MainWindow.
- Pros: nearly zero diff in the ~35 partials; can start immediately.
- Cons: the 16 model fields include 9 `readonly` collections that must become non-readonly to swap by reference (or be
  copied element-wise, O(n); a naive Stack copy also reverses order). Two sources of truth: a background async
  continuation (A.6.2) that writes `_formTextValues` or calls `AdoptTransformedFile` after a switch writes into the
  WRONG document with no error. Every future field must be added to Capture AND Restore or it silently leaks across
  tabs (upstream needed several aliasing fixes). Window close must capture the active session first (upstream
  WindowChrome.cs:669-670 calls the omission a data-loss bug).

**Option 2 - Move per-document fields into `DocumentSession` and rewrite access as `_s.Doc`, `_s.Annotations`, ...**
- Pros: one source of truth; the compiler finds every use; async code can capture `var s = _s;`.
- Cons: about 755 reference edits across 29 files for the 22 persistent fields alone (`_doc` 320 refs/29 files,
  `_currentFile` 67/17, `_viewMode` 53/11, `_annotations` 45/13, `_renderDims` 41/19, `_zoomLevel` 39/7,
  `_originalFile` 36/9, ...). Large merge-conflict surface with the ~70-file uncommitted working tree and the ongoing
  upstream-port program; big-bang risk.

**Option 3 (RECOMMENDED) - Session object + forwarding-property shim.** Create `DocumentSession` owning the model
and view state (Option 2's data layout), but instead of rewriting 755 call sites, REPLACE each MainWindow field
declaration with a same-named private property that forwards to the active session:

```csharp
private DocumentSession _s = DocumentSession.CreateEmpty();     // the active tab
private PdfDocument? _doc { get => _s.Doc; set => _s.Doc = value; }
private string? _currentFile { get => _s.WorkingPath; set => _s.WorkingPath = value; }
private Dictionary<int, List<PageAnnotation>> _annotations => _s.Annotations;
private Stack<UndoEntry> _undoStack => _s.Undo;
private bool _isDirty { get => _s.IsDirty; set => _s.IsDirty = value; }
```

C# permits underscore-named properties, and a grep found no `ref`/`out` use of any of these fields, so every existing
call site compiles unchanged. Switching tabs becomes `_s = next` plus a UI bind; nothing is copied, so nothing in the
model can be forgotten. Later, partials can migrate to `_s.X` (or to a captured `session.X` in async code)
opportunistically, and the shim is deleted when the last reference goes.
- Pros: one source of truth from day one; small, mechanical, reviewable diff (about 25 declarations in
  MainWindow.xaml.cs plus Forms.cs:615, TextBox.cs:32-33, FileOps.cs:805); async code can pin its session; every
  stage is shippable.
- Cons: the shim hides that `_doc` is session-scoped (mitigate with a banner comment and a CLAUDE.md note);
  `UndoEntry`/`UndoKind`, `FitMode` and `ViewMode` are private nested types of MainWindow (xaml.cs:36, 40, 124-129),
  so either nest `DocumentSession` inside MainWindow (file `MainWindow.DocumentSession.cs`, simplest) or move those
  types to namespace level (needed if the session is to be linked into Scalpel.Tests).

Why not a viewer control per tab (N visual trees)? Scalpel's viewer is not a control - it is MainWindow code-behind
bound to fixed named elements (PageList, PageImage, ContinuousPanel, OutlineTree, AnnotationCanvas). Duplicating the
visual tree per tab would be the largest refactor and would keep N sets of rendered bitmaps alive. One visual tree
rebound to the active session is the right fit.

### D.2 DocumentSession contents (proposed)

- Identity/model: `Id` (tab identity - NOT the path), `Doc`, `WorkingPath` (`_currentFile`), `OriginalPath`
  (`_originalFile`; null for Untitled), `DisplayName`, `IsUntitled`, `OpenedProtected`, `HasFormFields`, `IsDirty`,
  `FormAppearanceFailed`.
- Collections: `Annotations`, `RenderDims`, `PageRotations`, `FormText`, `FormCheck`, `FormRadio`, `Undo`, `Redo`,
  `BlankCache`, `SearchRects`, `SearchPages`, `SearchCursor`.
- View: `ZoomLevel`, `FitMode`, `ViewMode`, `PageIndex`, `ScrollH`, `ScrollV` (plus continuous-view page + fraction),
  `SearchQuery`, `SidebarOnOutlines`.
- Caches: `Thumbs` (PageThumbnailVm[]?), `ThumbsForPath` (the working path they were built from), `ThumbCts`.
- Lifecycle: `TempFiles` (temps created for this session), `IsDeferred` + `DeferredPath` (restored tabs),
  `LastActivated` (LRU), `Busy` (long op in flight).
- `INotifyPropertyChanged` for `DisplayName`, `IsDirty`, `IsActive`, tooltip - drives the strip bindings.

### D.3 Behaviour decisions

- Tab identity is the session, not the path (fixes the SaveAs stale-tab bug in B.4 and allows Untitled tabs).
- Open (multi-select dialog, drop of N files, N command-line args, forwarded args, recent): for each path, if a session
  with the same normalized `OriginalPath` exists, activate it and toast "Already open"; else open into a NEW session.
  The current "silently replace the dirty document" behaviour (B.1) disappears. If the active session is an empty or
  unmodified Untitled placeholder, reuse it (upstream `BeginTabLoad`).
- `OpenFile` refactor: build the new document into a fresh session; on failure discard it and keep the previous one
  active (upstream `AbortTabLoad`). No branch may close another session's `_doc` (today every branch does
  `_doc.Close()` first, FileOps.cs:71, 100, 118, 145, 168, 200, 496, 601).
- `AdoptTransformedFile` (Tools.cs:53) and the post-save reload (FileToolbar.cs:453) replace content IN the same session.
- Close tab: if dirty, activate it, then Save / Don't save / Cancel (reuse `SaveInPlace`; abort the close if the save
  fails or Save As is cancelled). Re-entrancy flag + membership re-check after the modal (#353). Then cancel render
  work, `_doc.Close()`, delete the session's temps (new `App.ReleaseTempFiles`), drop caches, schedule LOH compaction,
  activate the right neighbour else the left; last tab -> empty state (DropZone + recent list).
- Window close (`OnClosing` WindowChrome.cs:64): collect dirty sessions; one -> the close-tab prompt; several -> one
  dialog listing them with Save all / Discard all / Cancel. Persist `OpenTabs` + `ActiveTab` (real paths only, skip
  Untitled/temp) alongside or instead of `LastFile` (Settings.cs:386-389, which today stores the working path by mistake).
- Startup: restore saved tabs as deferred sessions and materialize only the active one (upstream `MakeDeferredSession`).
  This is a behaviour change from "reopen last file" - confirm with the user.
- Busy gate: while any long op runs (compress, OCR, redact, flatten, print, form OCR, export images, compare,
  straighten) the strip and tab shortcuts are disabled; the op also captures `var s = _s;` at start and adopts its
  result into `s` (if `s` is no longer active, update `s` and mark it dirty without touching the UI).
- Shortcuts: Ctrl+T = new blank document tab (make Ctrl+N open its blank doc in a new tab too); Ctrl+W close tab
  (KeyboardShortcuts.cs:174); Ctrl+Shift+W close others (:169, already wired); Ctrl+Tab / Ctrl+Shift+Tab (:184-191);
  Ctrl+PageDown / Ctrl+PageUp as aliases. **Ctrl+1..9 conflicts** with zoom presets: Ctrl+0/Ctrl+1 = actual size
  (KeyboardShortcuts.cs:227), Ctrl+2 = fit width (:233), Ctrl+3 = fit page (:238). Choices: tab-jump on Alt+1..9, or
  move zoom presets to another chord. Needs a user decision; update the shortcut overlay (PageSelection.cs:121),
  docs/UI-REFERENCE.md and the E2E catalog.
- Strip UI (mockups first, per the CLAUDE.md design rule): ItemsControl bound to `ObservableCollection<DocumentSession>`
  (replacing the code-built chips, Tabs.cs:198-297); the existing horizontal ScrollViewer (MainWindow.xaml:888) plus a
  chevron "all tabs" list; dirty dot; middle-click close; mouse-up switch; drag reorder with threshold; context menu
  (Close, Close others, Close to the right, Open containing folder, Copy path). Decide whether the strip shows with
  one tab (Chrome style) or only with 2+ (today, MainWindow.xaml:884-886).

### D.4 Memory policy for background tabs

- Keep: `PdfDocument` model, overlay dictionaries, form values, rotations, render dims, search results, view state.
  These are the unsaved work and are small relative to bitmaps.
- Drop on deactivate: `PageImage.Source`, secondary tiles (`ClearSecondaryPages` FileOps.cs:974 already nulls image
  sources), continuous panel children and canvases, link/search/form overlays. These are the large WriteableBitmaps
  (primary up to 6144 px via `ViewerRenderResolution.Primary`, FileOps.cs:898), regenerated from `WorkingPath` on activation.
- Thumbnails: keep per session for the 3-5 most recently used tabs, drop older ones (upstream's LRU idea). Never carry
  thumbs from another session (RefreshPageList FileOps.cs:829-840 must only reuse the same session's array).
- Undo: add a GLOBAL byte budget across sessions (e.g. 512 MB total) on top of the per-stack 256 MB
  (`UndoHistoryBudget.PushBounded` Services/UndoHistoryBudget.cs:13); when exceeded, trim the oldest Document entries
  from the least-recently-used background session first. Keep it pure (Services/UndoBudgetCoordinator.cs) so it is
  unit-testable.
- After closing a tab or trimming: one-shot `GCSettings.LargeObjectHeapCompactionMode = CompactOnce; GC.Collect()` at
  ApplicationIdle, throttled (upstream `CompactLohSoon`, #122).
- No hard tab cap; confirm when more than about 20 files are dropped at once. Optional later: hibernate long-idle CLEAN
  sessions (close `_doc`, keep path + view state, reopen on activation like a deferred session). Never hibernate a
  dirty session.
- Temp files: per-session list, deleted on tab close; the exit sweep (App.xaml.cs:833) stays as the backstop.

### D.5 Risks

1. Async continuations writing into the wrong session (A.6.2) - busy gate plus session-captured adoption.
2. Deferred Dispatcher callbacks in the render path (RenderPage FileOps.cs:957, FinishOpenFile :658/:664,
   SetViewMode ViewMode.cs:52/60, SetupContinuousView :204, RenderContinuousPages :319, Zoom.cs:75/116/182,
   TempReload.cs:157/167, RestoreTabState Tabs.cs:56) landing
   after a switch (#378/#399 class) - capture the session and check `ReferenceEquals(_s, captured)` in each; use
   `DeferredActionGate` generations.
3. `PagePreviewPanel_ScrollChanged` (ViewMode.cs:90) writes `PageList.SelectedIndex` during rebuilds; filter on
   OriginalSource and suppress while binding.
4. Mouse capture or an in-progress drag/draw when a shortcut switches tabs - cancel the gesture or refuse the switch.
5. `PageList_SelectionChanged` as the de-facto render trigger (FileOps.cs:647-650, PageSelection.cs:76) - render
   explicitly in the bind path (upstream #378).
6. Memory growth with many heavy tabs (undo DocBytes) - D.4 global budget.
7. `SetViewMode` writes the registry (ViewMode.cs:33); a per-tab restore must not overwrite the user's default view mode.
8. Forwarded launches during a modal or before Loaded - queue and replay.
9. Existing single-document assumptions in the E2E harness (Scalpel.E2E/Catalog, Driver) - update; do not run the
   harness without asking the user.
10. Behaviour change surprises: Open no longer replaces the current document; startup restores several tabs.

### D.6 Staged migration (app shippable after every stage)

Stage 0 - Groundwork, no visible change.
- Fix latent bugs that tabs would amplify: FinishOpenFile and CloseFile also clear `_redoStack`; CloseFile also clears
  `_pageRotations`, `_openedProtected`, `_linkOverlays`; `LastFile` stores `_originalFile`; Open/drop/recent get a dirty
  prompt as an interim guard (B.1 data-loss bug).
- Make `UndoEntry`/`UndoKind`, `FitMode`, `ViewMode` reachable from a session class.
- Per-session temp tracking in App (`MakeTempFile(tag)` returning the path as today, plus an owner list and
  `ReleaseTempFiles`).

Stage 1 - DocumentSession + forwarding shim (still one document).
- Add MainWindow.DocumentSession.cs; replace the 22 persistent field declarations (A.1 + A.2) with forwarding
  properties. `_s` is the only session; FinishOpenFile/CloseFile create/replace it. Behaviour identical.

Stage 2 - Split FinishOpenFile; switch primitives (still one visible document).
- `ResetContentForSession(s)` (model reset), `BindSessionToUi(s)` (labels, thumbs, outline, view, explicit render,
  scroll restore with identity guard), `DeactivateSession()` (commit textbox, cancel transient UI, cancel all render
  work incl. the thumbnail CTS moved into the session). `AdoptTransformedFile` uses Reset + Bind and never adds a tab.
  Identity guards on every deferred render callback.

Stage 3 - Live multi-session tabs.
- `ObservableCollection<DocumentSession> _sessions`; OpenFile builds into a new session (abort restores the previous);
  dedup by path; `SwitchTo(s)`; `CloseSession(s)` with Save / Don't save / Cancel and #353 hardening; `OnClosing` over
  all sessions; busy gate + session-captured adoption; SaveAs updates the tab through the session. Delete `_openTabs`,
  `_tabState`, `RememberTabState`, `RestoreTabState` (Tabs.cs:25-66). Keep the current chip UI, bound to sessions.
  Add a What's New entry (Services/Changelog.cs).

Stage 4 - Multi-open.
- Open dialog `Multiselect = true` (FileToolbar.cs:75); `DropZone_Drop` opens every PDF (DragDrop.cs:40-41); the
  command-line loop opens every existing file (xaml.cs:339-347); `SingleInstanceProtocol` returns all files
  (Services/SingleInstance.cs:42) and SingleInstanceProtocolTests is extended; pending-forward queue before Loaded.

Stage 5 - Strip UI (after mockups are approved).
- ItemsControl template, dirty dot bound to `IsDirty`, middle-click, mouse-up switch, drag reorder, context menu,
  overflow chevron, shortcuts (Ctrl+T, Ctrl+W, Ctrl+Tab, tab-jump digits once the Ctrl+1..3 conflict is decided), new
  strings in all 9 locale files (LocaleParityTests), shortcut overlay, docs/UI-REFERENCE.md.

Stage 6 - Memory and persistence.
- Thumbnail LRU, global undo budget, LOH compaction, deferred restore of `OpenTabs` at startup, optional per-file
  view-state memory (upstream DocStates).

### D.7 Test strategy

Scalpel.Tests links source files directly (Scalpel.Tests/Scalpel.Tests.csproj, 77 `<Compile Include>` links, e.g.
TempSweep.cs :64, SingleInstance.cs :68, DeferredActionGate.cs :96, UndoHistoryBudget.cs :105) and has no WPF project
reference, so keep tab logic WPF-free in Services/ and link it:
- `TabListModel` (pure): ordering, activate, next-active-after-close rule (right neighbour else left), close others /
  to the right, reorder (move), dedup by normalized path, Ctrl+Tab cycling, digit mapping (9 = last tab), and the
  #353 scenarios (close requested twice; list changed while a modal was open).
- `UndoBudgetCoordinator` (pure): global byte budget, LRU trim order, newest entry always kept.
- `OpenTabsSetting` (pure): serialize/deserialize OpenTabs/ActiveTab; skip missing, temp and Untitled paths.
- `SingleInstanceProtocol.PickLaunchTargets` (all files, flags in any position): extend SingleInstanceProtocolTests.
- A PdfSharpCore test proving `PdfReader.Open(path, Modify)` holds no lock (rename/delete the source afterwards).
- A session-isolation test: two `DocumentSession` instances never share collection instances (reflection over
  reference-type fields) - guards the aliasing class of bugs upstream hit. Requires DocumentSession to be free of
  MainWindow-private types (see D.1 Option 3 cons).
- E2E (Scalpel.E2E; do not run without asking): add scenarios - open two files, annotate A, switch to B and back
  (annotation + undo survive), dirty dot, close a dirty tab prompt, window close with two dirty tabs, drop 3 files,
  Ctrl+Tab, SaveAs renames the tab.

### D.8 Field count summary

- Persistent per-document fields that must travel with a tab: **22** (16 model, A.1; 6 view, A.2), plus 5 UI-held
  values (selected page, scroll offsets, search query, sidebar pane, sidebar scroll).
- Derived/transient per-document fields to reset or rebuild on switch: **62** (A.3).
- Total document-scoped fields on MainWindow: **84**, versus about 70 app/window-wide fields and constants (A.4).

---------------------------------------------------------------------------------------------------

