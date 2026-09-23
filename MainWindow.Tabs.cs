using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Document tabs
        //
        // Every open PDF is its own live DocumentSession (MainWindow.DocumentSession.cs): its
        // document, unsaved annotations, undo/redo, dirty flag and place in the document. `_s` is
        // the session shown in the window; `_tabs` is the ordered list of all of them. Switching
        // tabs never reopens a file: it is DeactivateSession() on the outgoing session, `_s = next`,
        // then BindSessionToUi(next). A fresh app with no document holds one empty placeholder
        // session, which the first open (or New) fills instead of adding a second tab.
        // ============================================================

        private readonly TabListModel<DocumentSession> _tabs = new();

        /// <summary>
        /// One undo/redo memory budget shared across every open tab, on top of each tab's own
        /// per-stack cap (<c>UndoMaxBytes</c> in MainWindow.xaml.cs). A document-level undo entry
        /// holds a whole copy of the PDF, so N tabs each at their own cap could still pin N times
        /// that in RAM; this keeps the total bounded regardless of tab count.
        /// </summary>
        private const long UndoMaxBytesAllTabs = 512L * 1024 * 1024;

        /// <summary>
        /// Trims undo/redo history across every tab down to <see cref="UndoMaxBytesAllTabs"/>,
        /// oldest entries of the least-recently-activated tab first. The active tab (<c>_s</c>) is
        /// always treated as most recent, so its newest undo entry is never the one dropped. Call
        /// after every document-undo push.
        /// </summary>
        private void TrimUndoAcrossTabs()
        {
            var byAge = _tabs.Items.OrderBy(t => ReferenceEquals(t, _s) ? DateTime.MaxValue : t.LastActivated).ToList();
            var stacks = byAge.SelectMany(t => new[] { t.Redo, t.Undo }).ToList();
            UndoBudgetCoordinator.TrimAcross(stacks, UndoEntrySize, UndoMaxBytesAllTabs);
        }

        /// <summary>
        /// Counts long-running document operations (compress, OCR, redact, straighten, form OCR,
        /// flatten, print, export images, compare) that finish after an await. Tab switching,
        /// closing and the window close are all refused with the <c>Str_Tab_Busy</c> toast while
        /// it is busy, so a result can never land in a document other than the one it started on.
        /// </summary>
        private readonly LongOperationGate _longOps = new();

        // ============================================================
        // Session primitives: a tab switch is DeactivateSession() on the outgoing document,
        // `_s = next`, then BindSessionToUi(_s, newContent: false). Opening a file (or adopting a
        // tool's output) is ResetSessionContent + BindSessionToUi(_s, newContent: true).
        // ============================================================

        /// <summary>
        /// Bumped on every tab switch and every open. A dispatcher callback queued for an earlier
        /// generation belongs to a document that is no longer shown and must do nothing
        /// (upstream #378/#379/#399).
        /// </summary>
        private int _sessionGeneration;
        private bool IsStale(int generation) => generation != _sessionGeneration;

        /// <summary>
        /// True while BindSessionToUi is rebuilding the view, until its deferred scroll restore has
        /// run. PagePreviewPanel_ScrollChanged must not turn the intermediate scroll offsets of the
        /// rebuild into a page selection, or it overwrites the incoming session's page.
        /// </summary>
        private bool _bindingSession;

        /// <summary>
        /// Clears the per-document model of <paramref name="s"/> for new content (a fresh open or a
        /// tool's output replacing the pages). Touches no UI. Keeps the session's view preferences
        /// (zoom, fit, view mode) so a new document opens the way the user last looked at one.
        /// </summary>
        private void ResetSessionContent(DocumentSession s)
        {
            s.Annotations.Clear();
            s.Undo.Clear();
            s.Redo.Clear();
            s.RenderDims.Clear();
            s.BlankCache = [];
            // Per-document state: without this reset a page rotation or the "was protected" flag
            // from the previous file leaks into the newly opened one.
            s.PageRotations.Clear();
            s.FormText.Clear();
            s.FormCheck.Clear();
            s.FormRadio.Clear();
            s.SearchRects.Clear();
            s.SearchPages.Clear();
            s.SearchCursor = -1;
            s.OpenedProtected = false;
            s.IsDirty = false;
            // A deferred (startup-restored, not-yet-loaded) tab stops being deferred the moment it
            // actually gets content, successful or not - ForgetPaths clears it on the failure path.
            s.DeferredPath = null;
            s.PageIndex = 0;
            s.ScrollH = s.ScrollV = 0;
            // The old thumbnails are another document's pages now; they must never seed the list.
            try { s.ThumbCts?.Cancel(); } catch { }
            s.Thumbs = null;
            s.ThumbsForPath = null;
        }

        /// <summary>
        /// Shows session <paramref name="s"/> (which must be the active session <c>_s</c>) in the
        /// window: labels, dirty colour, thumbnails, outline, view mode, an explicit render of
        /// <c>s.PageIndex</c>, then the scroll position.
        /// </summary>
        /// <param name="newContent">True when the session's pages were just (re)loaded (an open,
        /// New, a tool's output): the view is fitted as on open. False for a tab switch: the
        /// session's own zoom is applied and its scroll position restored instead.</param>
        private void BindSessionToUi(DocumentSession s, bool newContent)
        {
            // Everything queued for what was shown before is stale from here on.
            _sessionGeneration++;
            int gen = _sessionGeneration;
            _bindingSession = true;
            double savedZoom = s.ZoomLevel;
            // Captured before anything re-fits: SetupContinuousView always applies a fit
            // (FitToPage when the mode is None), which overwrites the session's own fit mode.
            FitMode savedFit = s.FitMode;

            FileNameLabel.Text = s.DisplayName;
            ClearSecondaryPages();
            ClearSelection();

            // A returning tab keeps its own zoom. The scale transform is shared by every tab, so
            // it still holds the outgoing tab's zoom: apply this session's before anything renders.
            // (Only the transform: ApplyZoom would also queue a re-render of the list's OLD index.)
            if (!newContent)
            {
                try
                {
                    if (_pageContentGrid.LayoutTransform is ScaleTransform st)
                    {
                        st.ScaleX = _zoomLevel;
                        st.ScaleY = _zoomLevel;
                    }
                }
                catch { }
            }

            // Thumbnails: a session that already holds a complete set for its current working file
            // shows it at once; anything else (a fresh open, an interrupted load) is (re)loaded.
            bool thumbsReady = s.Thumbs is { } t && _doc is not null && t.Length == _doc.PageCount
                && Scalpel.Services.DocumentPath.Same(s.ThumbsForPath, s.WorkingPath)
                && Array.TrueForAll(t, x => x.Thumbnail is not null);
            if (thumbsReady) PageList.ItemsSource = s.Thumbs;
            else RefreshPageList();

            LoadOutlines();
            DropZone.Visibility = Visibility.Collapsed;
            PagePreviewPanel.Visibility = Visibility.Visible;
            if (_closeFileBtnRef != null) _closeFileBtnRef.IsEnabled = true;
            _pageJumpBox.IsEnabled = true;
            _pageTotalLabel.Text = $"/ {_doc!.PageCount}";
            MarkDirty(s.IsDirty);
            UpdateViewModeButtons();

            if (_doc!.PageCount > 0)
            {
                int target = Math.Min(Math.Max(0, s.PageIndex), _doc.PageCount - 1);
                int before = PageList.SelectedIndex;
                PageList.SelectedIndex = target;
                if (_viewMode == ViewMode.Continuous)
                {
                    // SelectionChanged returns early in Continuous (no RenderPage call), so the
                    // panels have to be bootstrapped here.
                    _pageContentPanel.Visibility = Visibility.Collapsed;
                    _continuousPanel.Visibility  = Visibility.Visible;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                    {
                        if (IsStale(gen)) return;
                        SetupContinuousView(target);
                        // A returning tab the user zoomed by hand (Ctrl+wheel, Ctrl+=, the zoom
                        // box) keeps that zoom: SetupContinuousView just re-fitted it. SetZoom
                        // also puts the fit mode back to None. Its page scroll is queued at
                        // Loaded, so it runs after this and lands at the restored zoom.
                        if (!newContent && savedFit == FitMode.None && Math.Abs(_zoomLevel - savedZoom) > 1e-6)
                            SetZoom(savedZoom);
                    });
                }
                else
                {
                    _pageContentPanel.Visibility = Visibility.Visible;
                    _continuousPanel.Visibility  = Visibility.Collapsed;
                    // Do not rely on SelectionChanged to paint the page (upstream #378): when the
                    // index did not change it never fires and the previous document's bitmap
                    // stays up. When it did change, the handler has just rendered it (with zoom,
                    // jump box and search highlights), so a second render would only cost time.
                    if (before == target)
                    {
                        RenderPage(_viewMode == ViewMode.Grid ? 0 : target);
                        _pageJumpBox.Text = (target + 1).ToString();
                    }
                }

                if (newContent)
                {
                    // Auto-fit to width once the first page has rendered and layout has settled.
                    // DispatcherPriority.Background is lower than Loaded, so this fires after
                    // all pending RenderPage / RefreshPageView callbacks have completed.
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                        (Action)(() =>
                        {
                            if (IsStale(gen)) return;
                            // Grid opens to its 3-across default; other modes fit to width. Background
                            // runs after every Loaded callback, so this is the final word on open and
                            // must be view-aware or it collapses the grid back to a single page.
                            if (_viewMode == ViewMode.Grid)
                                SetZoom(GridZoomForN(Math.Min(_doc?.PageCount ?? 1, 3)));
                            else
                                FitToWidth();  // Single, Two-Page, and Continuous open fit-to-width
                        }));
                }
                else
                {
                    try { SyncZoomBox(); } catch { }
                    // Grid's first render queues its 3-across default zoom (PageList_SelectionChanged);
                    // a returning tab puts its own grid zoom back after that.
                    if (_viewMode == ViewMode.Grid)
                    {
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, (Action)(() =>
                        {
                            if (IsStale(gen)) return;
                            if (Math.Abs(_zoomLevel - savedZoom) > 1e-6) SetZoom(savedZoom);
                        }));
                    }
                }
            }

            // Scroll back to where the user was. Offsets are snapshotted by DeactivateSession
            // before anything is rebuilt (upstream #399). ContextIdle runs after the Loaded
            // rebuild and the Background fit. A fresh document (0,0) is already at the top, so
            // nothing is scrolled that could fight an early user scroll; the callback still runs
            // to end the binding window for PagePreviewPanel_ScrollChanged.
            double h = s.ScrollH, v = s.ScrollV;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, (Action)(() =>
            {
                if (IsStale(gen)) return;   // a newer bind owns the flag now
                try
                {
                    // Raw offsets are only meaningful at the zoom they were taken at. When the
                    // zoom differs (a fit mode re-fitted to a resized window), the page-based
                    // scroll done by the rebuild already put the right page in view.
                    if ((h != 0 || v != 0) && Math.Abs(_zoomLevel - savedZoom) < 1e-4)
                    {
                        PagePreviewPanel.ScrollToHorizontalOffset(h);
                        PagePreviewPanel.ScrollToVerticalOffset(v);
                    }
                }
                catch { }
                // Let the restore's own ScrollChanged pass before page tracking resumes.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, (Action)(() =>
                {
                    if (!IsStale(gen)) _bindingSession = false;
                }));
            }));
            SetStatus(string.Format(Loc("Str_Opened"), s.DisplayName, _doc.PageCount));
        }

        /// <summary>
        /// Leaves the active document: commits the text being typed, dismisses transient UI,
        /// cancels every render still in flight and records where the user was into <c>_s</c>.
        /// Called before the active session changes (a tab switch, opening into a new tab).
        /// </summary>
        private void DeactivateSession()
        {
            try { CommitActiveTextBox(); } catch { }
            try { ClearSelection(); ClearTextSelection(); } catch { }
            try { HideDrawSettings(); HideTextSettings(); HideSignaturePopup(); } catch { }
            try { HideCropConfirmBar(); } catch { }
            try { ClearMeasureOverlay(); _activeCanvas?.ReleaseMouseCapture(); } catch { }
            try { _thumbCts?.Cancel(); } catch { }
            try { _secondaryRenderCts?.Cancel(); } catch { }
            try { _continuousRenderCts?.Cancel(); } catch { }
            try { _rerenderTimer?.Stop(); } catch { }
            try
            {
                // Only a session that is actually on screen has a place to remember; the empty
                // placeholder must not pick up the drop zone's offsets.
                if (_s.Doc is not null)
                {
                    _s.PageIndex = Math.Max(0, PageList.SelectedIndex);
                    _s.ScrollH = PagePreviewPanel.HorizontalOffset;   // snapshot BEFORE any rebuild (upstream #399)
                    _s.ScrollV = PagePreviewPanel.VerticalOffset;
                    _s.SearchQuery = _searchBox?.Text;
                }
                _s.LastActivated = DateTime.UtcNow;
            }
            catch { }

            // Drop this tab's big bitmaps now that it is leaving the screen: with many tabs open,
            // every background one keeping its full-resolution page image(s) pins a lot of memory
            // for pixels nobody can see. BindSessionToUi always rebuilds PageImage (via RenderPage),
            // the secondary/grid tiles (RenderAdditionalPages) and the continuous panel
            // (SetupContinuousView) from scratch on every bind, so nothing here is lost - it is
            // simply re-rendered the next time this tab is shown (R15).
            try
            {
                PageImage.Source = null;
                ClearSecondaryPages();
                _continuousPanel.Children.Clear();
            }
            catch { }

            _sessionGeneration++;
        }

        private static bool PathEq(string? a, string? b) =>
            string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        // ============================================================
        // Switch / open / close
        // ============================================================

        /// <summary>Shows <paramref name="target"/>. A live session is not reloaded; a deferred one
        /// (startup-restored, not yet opened) is loaded now, into this same tab (R19).</summary>
        private void SwitchTo(DocumentSession target)
        {
            try
            {
                if (ReferenceEquals(target, _s)) return;
                if (_tabs.IndexOf(target) < 0) return;
                if (_longOps.IsBusy) { ShowToast(Loc("Str_Tab_Busy")); return; }
                var previous = _s;
                DeactivateSession();
                _s = target;
                _tabs.Activate(target);
                target.LastActivated = DateTime.UtcNow;
                TrimBackgroundThumbnails();

                if (target.Doc is null && target.DeferredPath is not null)
                {
                    // MaterializeDeferred binds the UI itself on success (via OpenFile's normal
                    // FinishOpenFile path); on failure the tab is dropped and the view falls back
                    // to whichever tab the user was on before this switch (R19).
                    if (!MaterializeDeferred(target))
                    {
                        DropFailedSession(target);
                        ActivateFallback(_tabs.IndexOf(previous) >= 0 ? previous : null, target);
                        return;
                    }
                }
                else if (target.Doc is not null)
                {
                    BindSessionToUi(target, newContent: false);
                }
                else
                {
                    ShowEmptyState();
                }
                RefreshTabStrip();
            }
            catch { /* never crash on a tab switch */ }
        }

        /// <summary>
        /// Loads the file behind a deferred tab - one restored at startup whose document was never
        /// opened, because it was not the tab active when Scalpel last closed (Task 12) - into that
        /// SAME session, through the normal open pipeline. Must be called with <paramref name="s"/>
        /// already the active session (<c>_s</c>): <c>OpenFile</c> always writes into <c>_s</c>.
        /// A successful open binds the UI itself (FinishOpenFile); the caller must not bind again.
        /// A failed open shows a toast and leaves <paramref name="s"/> with no document, for the
        /// caller to remove instead of leaving an empty tab stuck in the strip (R19).
        /// </summary>
        private bool MaterializeDeferred(DocumentSession s)
        {
            if (s.DeferredPath is null) return s.Doc is not null;
            if (_longOps.IsBusy) { ShowToast(Loc("Str_Tab_Busy")); return false; }
            string path = s.DeferredPath;
            BeginTabOp();
            try
            {
                OpenFile(path);
                if (s.Doc is null)
                {
                    ShowToast(string.Format(Loc("Str_Tab_DeferredFailed"), System.IO.Path.GetFileName(path)));
                    return false;
                }
                return true;
            }
            finally { EndTabOp(); }
        }

        /// <summary>Removes a session that never got (or lost) its document - a deferred tab
        /// whose file failed to load, an open or New that failed - and frees its temp files.</summary>
        private void DropFailedSession(DocumentSession s)
        {
            try { s.Doc?.Close(); } catch { }
            s.Doc = null;
            ForgetPaths(s);
            try { App.ReleaseTempFiles(s.Id); } catch { }
            _tabs.Remove(s);
        }

        /// <summary>
        /// The one way to pick a new active tab after the active one went away (closed, failed to
        /// load, rolled back), so the active session is never left deferred (Doc == null with a
        /// DeferredPath, which no click could ever load: SwitchTo no-ops on the active tab).
        /// Activates <paramref name="candidate"/> (or, when it is null or gone, the tab the list
        /// now has active). A deferred candidate is materialized; if its file fails to load it is
        /// dropped with the MaterializeDeferred toast and its neighbour is tried next. With no tab
        /// left, a fresh empty placeholder shaped like <paramref name="template"/> is added. A
        /// live candidate is bound as a tab switch (newContent: false). Refreshes the strip.
        /// The caller must already have left the previously shown session (DeactivateSession) or
        /// removed it.
        /// </summary>
        private void ActivateFallback(DocumentSession? candidate, DocumentSession template)
        {
            var c = candidate is not null && _tabs.IndexOf(candidate) >= 0
                ? candidate
                : (_tabs.Active is { } a && _tabs.IndexOf(a) >= 0 ? a : _tabs.Items.FirstOrDefault());
            while (c is not null)
            {
                _s = c;
                _tabs.Activate(c);
                c.LastActivated = DateTime.UtcNow;
                if (c.Doc is null && c.DeferredPath is not null)
                {
                    if (MaterializeDeferred(c)) break;   // binds the UI itself (FinishOpenFile)
                    DropFailedSession(c);                 // c was active: the list activates a neighbour
                    c = _tabs.Active;
                    continue;
                }
                if (c.Doc is not null) BindSessionToUi(c, newContent: false);
                else ShowEmptyState();
                break;
            }
            if (c is null)
            {
                _s = DocumentSession.CreateLike(template);
                _tabs.Add(_s);
                ShowEmptyState();
            }
            RefreshTabStrip();
        }

        /// <summary>Background tabs kept with a full thumbnail set, most recent first.</summary>
        private const int MaxBackgroundTabsWithThumbs = 4;

        /// <summary>
        /// Frees the sidebar thumbnail bitmaps of every background tab beyond the
        /// <see cref="MaxBackgroundTabsWithThumbs"/> most recently activated ones - with many tabs
        /// open, a whole document's worth of page thumbnails per idle tab adds up. A tab whose
        /// thumbnails were dropped this way regenerates them the moment it is shown again:
        /// <c>BindSessionToUi</c>'s <c>thumbsReady</c> check sees <c>Thumbs == null</c> and falls
        /// back to <c>RefreshPageList()</c> exactly as a freshly opened tab would (R16).
        /// </summary>
        private void TrimBackgroundThumbnails()
        {
            var background = _tabs.Items.Where(t => !ReferenceEquals(t, _s))
                                         .OrderByDescending(t => t.LastActivated)
                                         .Skip(MaxBackgroundTabsWithThumbs);
            foreach (var t in background)
            {
                if (t.Thumbs is null) continue;
                try { t.ThumbCts?.Cancel(); } catch { }
                t.Thumbs = null;
                t.ThumbsForPath = null;
            }
        }

        // Opens that arrive while another open or a close prompt is running (a launch forwarded
        // from a second Scalpel process is dispatched inside the modal's message loop). Running
        // them there would swap `_s` under the operation in progress, so they wait their turn.
        // The same holds while any modal dialog is up (Save As, a Tools dialog): the code that
        // opened it acts on `_s` once it closes.
        private int _tabOpDepth;
        private readonly List<string> _pendingOpens = [];
        private System.Windows.Threading.DispatcherTimer? _pendingOpenTimer;

        // A multi-open batch (Open dialog multi-select, several files dropped, several files on
        // the command line or forwarded from a second launch) whose files landed in _pendingOpens
        // because a tab op/modal was running. Held here until SchedulePendingOpens actually drains
        // the queue, so "land on the first file" and /edit never act on a half-opened batch.
        private readonly List<(List<string> Paths, bool Edit)> _pendingLandings = [];

        private void BeginTabOp() => _tabOpDepth++;

        private void EndTabOp()
        {
            if (--_tabOpDepth > 0 || _pendingOpens.Count == 0) return;
            SchedulePendingOpens();
        }

        // A long operation (compress, OCR, redact, straighten, form OCR, flatten, print, export
        // images, compare) blocks an open exactly like a tab op/modal does: the pending-open timer
        // must not try to drain the queue while one is running, or it would reopen a file into the
        // document the operation is still working on.
        private bool CanOpenNow(int depth) =>
            depth == 0 && !_longOps.IsBusy && !System.Windows.Interop.ComponentDispatcher.IsThreadModal;

        /// <summary>Set while <see cref="OpenManyInTabs"/> is looping over a batch, so
        /// <see cref="OpenInTab"/> queuing each file while a long operation is busy does not show
        /// the busy toast once per file; the caller shows it once instead.</summary>
        private bool _suppressBusyToast;

        private void SchedulePendingOpens()
        {
            try
            {
                if (_pendingOpenTimer is null)
                {
                    _pendingOpenTimer = new System.Windows.Threading.DispatcherTimer(
                        System.Windows.Threading.DispatcherPriority.Background, Dispatcher)
                        { Interval = TimeSpan.FromMilliseconds(250) };
                    _pendingOpenTimer.Tick += (_, _) =>
                    {
                        if (!CanOpenNow(_tabOpDepth)) return;          // still busy: try again later
                        _pendingOpenTimer!.Stop();
                        var queued = _pendingOpens.ToList();
                        _pendingOpens.Clear();
                        foreach (var p in queued) OpenInTab(p);

                        // Now that the queue actually drained, any multi-open batch that was
                        // waiting on it can land on its first file and apply /edit.
                        if (_pendingLandings.Count > 0)
                        {
                            var landings = _pendingLandings.ToList();
                            _pendingLandings.Clear();
                            foreach (var landing in landings) ApplyMultiOpenLanding(landing.Paths, landing.Edit);
                        }
                    };
                }
                if (!_pendingOpenTimer.IsEnabled) _pendingOpenTimer.Start();
            }
            catch { }
        }

        /// <summary>
        /// Startup tab restore (Task 12, R18): reopens the tabs open when Scalpel last closed, read
        /// from the <c>OpenTabs</c> setting written by <see cref="SaveOpenTabsSetting"/>. Only the
        /// tab that was active loads its document now (through <see cref="MaterializeDeferred"/>);
        /// every other restored path becomes a deferred tab that loads on first activation. Called
        /// from the <c>Loaded</c> handler only when there is no command-line file and no document
        /// already open or queued (R18 gives those precedence; see StartupOpenPolicy). Returns
        /// false when there is nothing to restore, so the caller falls back to
        /// the older single "last file" setting.
        /// </summary>
        private bool TryRestoreOpenTabs()
        {
            try
            {
                string? wire = App.GetSetting("OpenTabs");
                var (paths, activeIndex) = Scalpel.Services.OpenTabsSetting.Parse(wire, System.IO.File.Exists);
                if (paths.Count == 0) return false;

                // The ctor's empty placeholder session becomes the first restored tab instead of
                // sitting alongside it as an extra empty one - but only while it really is the
                // empty start state. The Loaded handler already skips the restore when a document
                // was opened (or queued) first, e.g. a launch forwarded before Loaded ran
                // (StartupOpenPolicy); this check makes sure a restored path is still never stamped
                // onto a session that holds some other document. Otherwise every restored tab is
                // added as a new deferred tab after the existing ones and the shown tab stays.
                var host = _s;
                bool reuse = Scalpel.Services.StartupOpenPolicy.IsReusablePlaceholder(
                    host.Doc is not null, host.DeferredPath is not null, host.IsDirty);
                DocumentSession? activeSession = null;
                var restored = new List<DocumentSession>(paths.Count);
                for (int i = 0; i < paths.Count; i++)
                {
                    // Never two tabs for one file: a duplicate in the setting, or a file that is
                    // already open (or already restored), is skipped.
                    var dup = _tabs.FindByPath(paths[i], t => t.Doc is null ? t.DeferredPath : t.OriginalPath);
                    if (dup is not null)
                    {
                        if (i == activeIndex) activeSession = dup;
                        continue;
                    }
                    bool useHost = reuse && restored.Count == 0;
                    var s = useHost ? host : DocumentSession.CreateLike(host);
                    s.OriginalPath = paths[i];
                    s.DeferredPath = paths[i];
                    if (!useHost) _tabs.Add(s, activate: false);
                    restored.Add(s);
                    if (i == activeIndex) activeSession = s;
                }
                if (restored.Count == 0) return !reuse;

                if (!reuse)
                {
                    // Something else is shown already: it stays the active tab.
                    RefreshTabStrip();
                    return true;
                }

                // Only the tab the user was on loads now (Task 12); ActivateFallback falls through
                // to its neighbours if that file fails to load, and back to an empty placeholder
                // if none does.
                ActivateFallback(activeSession ?? restored[0], host);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Opens <paramref name="path"/> in its own tab. A file that is already open is switched
        /// to instead. The empty start state (a placeholder session with no document) is filled
        /// rather than kept as an extra tab. If every open fallback fails, the new tab is dropped
        /// and the document the user was on comes back untouched.
        /// </summary>
        private void OpenInTab(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_longOps.IsBusy)
            {
                // Queued, not dropped (R13): the file opens as soon as the operation ends instead
                // of being lost. A multi-open batch shows this toast once via _suppressBusyToast,
                // not once per file.
                if (!_suppressBusyToast) ShowToast(Loc("Str_Tab_Busy"));
                _pendingOpens.Add(path);
                SchedulePendingOpens();
                return;
            }
            if (!CanOpenNow(_tabOpDepth))
            {
                _pendingOpens.Add(path);
                SchedulePendingOpens();
                return;
            }

            // A session with no document is not "open" (a failed open can leave a path behind) -
            // except a deferred (startup-restored, not yet loaded) tab, which IS this file; switching
            // to it materializes it instead of opening a second tab for the same path.
            var existing = _tabs.FindByPath(path, t => t.Doc is null ? t.DeferredPath : t.OriginalPath);
            if (existing is not null)
            {
                SwitchTo(existing);
                ShowToast(string.Format(Loc("Str_Tab_AlreadyOpen"), System.IO.Path.GetFileName(path)));
                return;
            }

            BeginTabOp();
            try
            {
                var previous = _s;
                // The empty start state only - never a deferred (startup-restored) tab, which
                // already has a real file behind it even though it has no document loaded yet.
                bool reusePlaceholder = Scalpel.Services.StartupOpenPolicy.IsReusablePlaceholder(
                    previous.Doc is not null, previous.DeferredPath is not null, previous.IsDirty);
                var fresh = reusePlaceholder ? previous : DocumentSession.CreateLike(previous);
                if (!reusePlaceholder)
                {
                    DeactivateSession();
                    _s = fresh;
                    _tabs.Add(fresh);
                }

                OpenFile(path);                       // the fallback chain writes into `fresh` only

                // A failed open must not leave a path on a document-less session: the placeholder
                // would then claim the file is "already open" and block opening it again.
                if (fresh.Doc is null) ForgetPaths(fresh);

                if (fresh.Doc is null && !reusePlaceholder)
                {
                    // Every fallback failed (or a password / repair prompt was declined): drop the
                    // empty tab (and any temp file the attempt made) and go back to the document
                    // the user was on (upstream AbortTabLoad).
                    DropFailedSession(fresh);
                    ActivateFallback(previous, fresh);
                }
                else if (fresh.Doc is null)
                {
                    ShowEmptyState();   // the placeholder stays empty; clear anything half-bound
                }
                RefreshTabStrip();
            }
            catch { /* the open path reports its own failures */ }
            finally { EndTabOp(); }
        }

        /// <summary>Files above this count in one batch (Open dialog multi-select, a drop, a
        /// command line, or a forwarded launch) ask for confirmation before opening them all.</summary>
        private const int ManyFilesConfirmThreshold = 20;

        /// <summary>
        /// Opens every PDF in <paramref name="paths"/> in its own tab (non-PDF entries, including
        /// folders dropped alongside files, are silently ignored). Lands on the first file once it
        /// is actually open, like Explorer's own multi-open order, and applies Edit mode once
        /// (<paramref name="edit"/>) to whichever tab ends up active. A file that arrives while a
        /// tab op or modal is running queues through the normal <see cref="OpenInTab"/> mechanism;
        /// the landing is deferred until that queue actually drains (see <see cref="_pendingLandings"/>).
        /// </summary>
        private void OpenManyInTabs(IEnumerable<string> paths, bool edit = false)
        {
            var list = paths.Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count == 0) return;
            if (list.Count > ManyFilesConfirmThreshold)
            {
                var res = ScalpelDialog.Show(this, string.Format(Loc("Str_Tab_OpenMany"), list.Count),
                    "Scalpel", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;
            }

            // A long operation blocks every open in the batch; show the busy toast once here
            // instead of letting OpenInTab show it once per file (R13).
            if (_longOps.IsBusy) ShowToast(Loc("Str_Tab_Busy"));
            _suppressBusyToast = true;
            try { foreach (var p in list) OpenInTab(p); }
            finally { _suppressBusyToast = false; }

            // Any path still sitting in _pendingOpens was queued (a tab op/modal, or a long
            // operation, was running) and will open later when the pending-open timer drains it;
            // defer landing until then so it never acts on a half-opened batch. Otherwise every
            // file is open now: land at once.
            if (list.Exists(p => _pendingOpens.Contains(p)))
                _pendingLandings.Add((list, edit));
            else
                ApplyMultiOpenLanding(list, edit);
        }

        /// <summary>Switches to the first file of a multi-open batch, but only if it actually
        /// ended up open, and applies Edit mode once to whichever tab is active afterward.</summary>
        private void ApplyMultiOpenLanding(List<string> list, bool edit)
        {
            if (list.Count == 0) return;
            var first = _tabs.FindByPath(list[0], t => t.Doc is null ? null : t.OriginalPath);
            if (first is not null) SwitchTo(first);        // land on the first file, like Explorer's order
            if (edit && _doc is not null && list.Exists(p => Scalpel.Services.DocumentPath.Same(_originalFile, p)))
                SetMode(AppMode.Edit);
        }

        /// <summary>Clears the file identity of a session left without a document.</summary>
        private static void ForgetPaths(DocumentSession s)
        {
            s.OriginalPath = null;
            s.WorkingPath = null;
            s.DeferredPath = null;
        }

        private bool _closingTab;

        /// <summary>
        /// Closes one tab, asking Save / Don't save / Cancel first when it has unsaved changes.
        /// </summary>
        /// <returns>false when the user cancelled (the tab stays open).</returns>
        private bool CloseSession(DocumentSession s)
        {
            if (_closingTab) return false;                          // upstream #353: repeated request
            if (_tabs.IndexOf(s) < 0) return true;                  // already gone
            if (_longOps.IsBusy) { ShowToast(Loc("Str_Tab_Busy")); return false; }
            _closingTab = true;
            BeginTabOp();
            var origin = _s;                                        // the tab the user was looking at
            try
            {
                // Text still being typed on the active tab counts as a change to ask about.
                if (ReferenceEquals(s, _s)) { try { CommitActiveTextBox(); } catch { } }
                if (s.IsDirty)
                {
                    SwitchTo(s);                                   // show the document being asked about
                    // SwitchTo swallows failures and can refuse: never ask about a tab that is not
                    // the one shown, since Save acts on `_s`.
                    if (!ReferenceEquals(_s, s)) return false;
                    var res = ScalpelDialog.Show(this,
                        string.Format(Loc("Str_Tab_SavePrompt"), s.DisplayName),
                        Loc("Str_Dlg_AppTitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (res == MessageBoxResult.Cancel || res == MessageBoxResult.None)
                    {
                        if (!ReferenceEquals(origin, s)) SwitchTo(origin);
                        return false;
                    }
                    if (_tabs.IndexOf(s) < 0) return true;          // re-check after the modal
                    if (res == MessageBoxResult.Yes)
                    {
                        if (!ReferenceEquals(_s, s)) SwitchTo(s);   // Save acts on the active session
                        if (!ReferenceEquals(_s, s)) return false;
                        SaveInPlace();
                        if (s.IsDirty) return false;                // save failed or Save As cancelled
                    }
                }

                bool wasActive = ReferenceEquals(s, _s);
                if (wasActive) DeactivateSession();
                try { s.ThumbCts?.Cancel(); } catch { }
                try { s.Doc?.Close(); } catch { }
                s.Doc = null;
                s.Thumbs = null;                                    // never shown again; free the bitmaps
                s.ThumbsForPath = null;
                // The tab's document is closed now, so its own working/undo/redo/burn temp files
                // (R14) are safe to delete immediately rather than waiting for the exit sweep.
                try { App.ReleaseTempFiles(s.Id); } catch { }
                var next = _tabs.Remove(s);

                // Closing a background tab (via its prompt we switched to it) returns the user to
                // the tab they were on, not to a neighbour of the closed one.
                if (!ReferenceEquals(origin, s) && _tabs.IndexOf(origin) >= 0)
                    next = origin;

                // The neighbour may be a deferred (restored, not yet loaded) tab: ActivateFallback
                // loads it (or drops it and tries the next) rather than leaving it active and empty.
                if (wasActive || next is null) ActivateFallback(next, s);
                else RefreshTabStrip();
                ScheduleLohCompaction();
                return true;
            }
            catch { return false; }
            finally
            {
                _closingTab = false;
                EndTabOp();
            }
        }

        /// <summary>True while an LOH compaction pass is already queued, so closing several tabs
        /// in a row (CloseMany) schedules one pass instead of piling up a GC.Collect per tab.</summary>
        private bool _lohCompactionQueued;

        /// <summary>
        /// A closed tab's undo/redo document snapshots and page bitmaps were large objects (85 KB+
        /// each lands on the Large Object Heap), which the .NET Framework GC does not compact by
        /// default - closing tabs over a session can fragment it until the process holds far more
        /// reserved memory than any single open tab needs. One <c>CompactOnce</c> pass folds that
        /// back down; queued at ApplicationIdle so it never runs on the UI thread's own time.
        /// </summary>
        private void ScheduleLohCompaction()
        {
            if (_lohCompactionQueued) return;
            _lohCompactionQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, (Action)(() =>
            {
                _lohCompactionQueued = false;
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                }
                catch { }
            }));
        }

        /// <summary>Closes each tab in turn; stops at the first one the user cancels.</summary>
        private void CloseMany(IReadOnlyList<DocumentSession> victims)
        {
            foreach (var v in victims) if (!CloseSession(v)) break;
        }

        /// <summary>Ctrl+Shift+W: closes every tab except the one being viewed.</summary>
        private void CloseOtherTabs() => CloseOtherTabs(_s);

        /// <summary>Closes every tab except <paramref name="keep"/> (the tab menu's "Close other
        /// tabs"), which is shown first so it is the tab left standing.</summary>
        private void CloseOtherTabs(DocumentSession keep)
        {
            try
            {
                if (_tabs.Count <= 1 || _tabs.IndexOf(keep) < 0) return;
                if (!ReferenceEquals(keep, _s))
                {
                    SwitchTo(keep);
                    if (!ReferenceEquals(keep, _s)) return;   // refused (busy)
                }
                CloseMany(_tabs.OthersThan(keep));
            }
            catch { }
        }

        private void CloseTabsToRight() => CloseTabsToRight(_s);

        /// <summary>Closes every tab after <paramref name="of"/> in strip order.</summary>
        private void CloseTabsToRight(DocumentSession of)
        {
            try
            {
                var victims = _tabs.RightOf(of);
                // Viewing one of the tabs about to close: show the tab that stays first, so each
                // close does not activate (and load) the next victim on the way.
                if (victims.Any(v => ReferenceEquals(v, _s)))
                {
                    SwitchTo(of);
                    if (!ReferenceEquals(of, _s)) return;   // refused (busy) or failed to load
                    victims = _tabs.RightOf(of);
                }
                CloseMany(victims);
            }
            catch { }
        }

        /// <summary>Ctrl+Tab / Ctrl+Shift+Tab (and Ctrl+PageDown / Ctrl+PageUp).</summary>
        private void CycleTab(bool forward)
        {
            try
            {
                if (_tabs.Count <= 1) return;
                var t = _tabs.Cycle(forward);
                if (t is not null) SwitchTo(t);
            }
            catch { }
        }

        // ============================================================
        // Tab strip: "compact chips" under the ribbon band (option C of
        // design-mockups/tabs/tab-strip-options.html). Always shown. Each open document is a 26 px
        // pill; the active one is accent-filled. The empty start state (one placeholder session
        // with no document) shows no chip, only the + button.
        //
        // Chips are rebuilt from _tabs on every refresh: a handful of Borders is cheap, and it
        // keeps the strip trivially in step with the model (names, dirty dots, order, active).
        // ============================================================

        /// <summary>More than this many tabs shows the chevron that lists every tab.</summary>
        private const int TabAllTabsMenuThreshold = 6;

        /// <summary>The parts of one chip whose look depends on hover.</summary>
        private sealed class TabChipParts
        {
            public DocumentSession Session = null!;
            public bool Active;
            public Button Close = null!;
            public System.Windows.Shapes.Ellipse Dot = null!;
        }

        // Drag-to-reorder state. A press on a chip captures the mouse; only once it has moved
        // SystemParameters.MinimumHorizontalDragDistance does it become a drag. A release that
        // never became a drag is a click (switch).
        private Border? _dragChip;
        private DocumentSession? _dragSession;
        private Point _dragStart;
        private double _dragGrabX;
        private bool _dragging;

        // What the strip last scrolled into view, so a refresh that only changes a name or a dirty
        // dot does not yank the strip back while the user has scrolled it to look at other tabs.
        private DocumentSession? _tabScrolledFor;
        private int _tabScrolledCount = -1;

        private void RefreshTabStrip()
        {
            try
            {
                if (TabStrip is null || TabStripHost is null) return;
                if (_dragging) return;   // the drag's own end refreshes; never pull the chip being dragged

                // A pressed-but-not-dragged chip is about to be replaced: forget it cleanly.
                if (_dragChip is not null) EndTabDrag();

                TabStrip.Children.Clear();

                // A deferred (startup-restored, not yet loaded) tab shows as a normal chip labelled
                // with its file name - OriginalPath is set for it just like a loaded tab - so the
                // strip looks the same as before the app closed; ApplyChipHover already keeps its
                // dot hidden because a never-loaded session is never dirty.
                var shown = _tabs.Items.Where(t => t.Doc is not null || t.DeferredPath is not null).ToList();
                var fontUI = (FontFamily)Application.Current.FindResource("FontUI");
                var fontIcon = (FontFamily)Application.Current.FindResource("FontIcon");

                Border? activeChip = null;
                foreach (var s in shown)
                {
                    bool active = ReferenceEquals(s, _s);
                    var chip = BuildTabChip(s, active, fontUI, fontIcon);
                    if (active) activeChip = chip;
                    TabStrip.Children.Add(chip);
                }

                if (TabMoreBtn is not null)
                    TabMoreBtn.Visibility = shown.Count > TabAllTabsMenuThreshold ? Visibility.Visible : Visibility.Collapsed;
                UpdateTabScrollLimit();

                // Scroll the active chip into view when the active tab or the tab count changed.
                if (activeChip is not null
                    && (!ReferenceEquals(_tabScrolledFor, _s) || _tabScrolledCount != shown.Count))
                {
                    _tabScrolledFor = _s;
                    _tabScrolledCount = shown.Count;
                    var target = activeChip;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
                    {
                        try { if (target.IsLoaded) target.BringIntoView(); } catch { }
                    }));
                }
            }
            catch { /* a broken strip must never block editing */ }
        }

        private Border BuildTabChip(DocumentSession session, bool active, FontFamily fontUI, FontFamily fontIcon)
        {
            var chip = new Border
            {
                Style = (Style)Application.Current.FindResource("DocumentTab"),
                ToolTip = session.OriginalPath ?? session.DisplayName
            };
            if (active)
            {
                // Local values beat the style's hover trigger, so the active chip keeps its fill.
                chip.SetResourceReference(Border.BackgroundProperty, "AccentDim");
                chip.SetResourceReference(Border.BorderBrushProperty, "AccentBorder");
            }
            string fg = active ? "AccentText" : "TextPrimary";

            var row = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };

            // The close slot: an 18 px round X, with the unsaved dot drawn in the same place.
            var slot = new Grid { Width = 18, Height = 18, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
            var closeBtn = new Button
            {
                Style = (Style)Application.Current.FindResource("DocumentTabClose"),
                Content = Application.Current.FindResource("Ico_X"),
                FontFamily = fontIcon,
                ToolTip = Loc("Str_TT_CloseTab")
            };
            closeBtn.SetResourceReference(Control.ForegroundProperty, fg);
            closeBtn.Click += (_, e) =>
            {
                e.Handled = true;
                CloseSession(session);
            };
            slot.Children.Add(closeBtn);
            slot.Children.Add(dot);
            DockPanel.SetDock(slot, Dock.Right);
            row.Children.Add(slot);

            var label = new TextBlock
            {
                Text = session.DisplayName,
                FontFamily = fontUI,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 180,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, fg);
            // The chip Border has no automation peer, so this label is how UI Automation (the E2E
            // harness, screen readers) finds a tab: its Name is the file name, in strip order.
            System.Windows.Automation.AutomationProperties.SetAutomationId(label, "TabChipLabel");
            row.Children.Add(label);

            chip.Child = row;

            var parts = new TabChipParts { Session = session, Active = active, Close = closeBtn, Dot = dot };
            chip.Tag = parts;
            ApplyChipHover(parts, false);

            chip.MouseEnter += (_, _) => ApplyChipHover(parts, true);
            chip.MouseLeave += (_, _) => ApplyChipHover(parts, false);
            chip.MouseLeftButtonDown += TabChip_MouseLeftButtonDown;
            chip.MouseMove += TabChip_MouseMove;
            chip.MouseLeftButtonUp += TabChip_MouseLeftButtonUp;
            chip.LostMouseCapture += TabChip_LostMouseCapture;
            chip.MouseDown += (_, e) =>
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Middle) e.Handled = true;
            };
            chip.MouseUp += (_, e) =>
            {
                if (e.ChangedButton != System.Windows.Input.MouseButton.Middle) return;
                e.Handled = true;
                CloseSession(session);
            };
            chip.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowTabContextMenu(session, chip);
            };
            return chip;
        }

        /// <summary>
        /// Close slot rules: an unsaved tab shows the accent dot, replaced by the X on hover; a
        /// saved tab shows the X on hover and always on the active chip.
        /// </summary>
        private static void ApplyChipHover(TabChipParts p, bool hover)
        {
            try
            {
                bool dirty = p.Session.IsDirty;
                p.Dot.Visibility = dirty && !hover ? Visibility.Visible : Visibility.Collapsed;
                p.Close.Opacity = hover || (p.Active && !dirty) ? 1 : 0;
            }
            catch { }
        }

        // ---------- click / drag ----------

        private void TabChip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not Border chip || chip.Tag is not TabChipParts p) return;
                e.Handled = true;
                _dragChip = chip;
                _dragSession = p.Session;
                _dragStart = e.GetPosition(TabStrip);
                _dragGrabX = e.GetPosition(chip).X;
                _dragging = false;
                chip.CaptureMouse();
            }
            catch { }
        }

        private void TabChip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;
                if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                var pos = e.GetPosition(TabStrip);
                if (!_dragging)
                {
                    if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
                    _dragging = true;
                    _dragChip.Opacity = 0.55;
                    _dragChip.RenderTransform = new TranslateTransform();
                    _dragChip.ToolTip = null;   // a tooltip popping up mid-drag only gets in the way
                }
                DragTabTo(pos.X);
            }
            catch { }
        }

        /// <summary>
        /// Moves the dragged chip so its left edge follows the pointer, and swaps it past each
        /// neighbour whose midpoint its centre crosses (the model via _tabs.Move, the strip by
        /// moving the neighbour so the captured chip never leaves the visual tree). Coordinates are
        /// the strip's own layout space, which WPF keeps logical under RightToLeft, so the same
        /// arithmetic serves Hebrew and Arabic.
        /// </summary>
        private void DragTabTo(double pointerX)
        {
            var chip = _dragChip;
            if (chip is null || _dragSession is null) return;
            var kids = TabStrip.Children;
            double left = pointerX - _dragGrabX;
            double center = left + chip.ActualWidth / 2;

            for (int guard = 0; guard < 64; guard++)
            {
                int idx = kids.IndexOf(chip);
                if (idx < 0) return;
                Border? neighbour = null;
                int neighbourIdx = -1;
                if (idx + 1 < kids.Count && kids[idx + 1] is Border next && center > MidOf(next))
                {
                    neighbour = next; neighbourIdx = idx + 1;
                }
                else if (idx > 0 && kids[idx - 1] is Border prev && center < MidOf(prev))
                {
                    neighbour = prev; neighbourIdx = idx - 1;
                }
                if (neighbour is null || neighbour.Tag is not TabChipParts np) break;

                int from = _tabs.IndexOf(_dragSession), to = _tabs.IndexOf(np.Session);
                if (from < 0 || to < 0) break;
                _tabs.Move(from, to);
                kids.RemoveAt(neighbourIdx);
                kids.Insert(idx, neighbour);
                TabStrip.UpdateLayout();
            }

            if (chip.RenderTransform is TranslateTransform tt)
                tt.X = left - LayoutInformation.GetLayoutSlot(chip).X;
        }

        private static double MidOf(FrameworkElement e) =>
            LayoutInformation.GetLayoutSlot(e).X + e.ActualWidth / 2;

        private void TabChip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;
                e.Handled = true;
                bool wasDragging = _dragging;
                var session = _dragSession;
                EndTabDrag();
                if (wasDragging) RefreshTabStrip();
                else if (session is not null) SwitchTo(session);   // a click: switch on release
            }
            catch { }
        }

        private void TabChip_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Capture taken away mid-press (Alt+Tab, a dialog): drop the drag, keep the order so far.
            if (_dragChip is null || !ReferenceEquals(sender, _dragChip)) return;
            bool wasDragging = _dragging;
            EndTabDrag();
            if (wasDragging) RefreshTabStrip();
        }

        private void EndTabDrag()
        {
            var chip = _dragChip;
            _dragChip = null;          // cleared first: ReleaseMouseCapture re-enters LostMouseCapture
            _dragSession = null;
            _dragging = false;
            if (chip is null) return;
            try
            {
                chip.Opacity = 1;
                chip.RenderTransform = Transform.Identity;
                if (chip.IsMouseCaptured) chip.ReleaseMouseCapture();
            }
            catch { }
        }

        // ---------- strip chrome ----------

        /// <summary>
        /// The chip row scrolls inside its own width and the + button sits right after the last
        /// chip, so the scroller is capped at what is left of the row once + (and the chevron,
        /// when shown) have their room.
        /// </summary>
        private void UpdateTabScrollLimit()
        {
            try
            {
                if (TabScroll is null || TabStripHost is null || TabAddBtn is null) return;
                double avail = TabStripHost.ActualWidth - TabStripHost.Padding.Left - TabStripHost.Padding.Right
                               - TabAddBtn.Width - TabAddBtn.Margin.Left - TabAddBtn.Margin.Right;
                if (TabMoreBtn is not null && TabMoreBtn.Visibility == Visibility.Visible)
                    avail -= TabMoreBtn.Width + TabMoreBtn.Margin.Left + TabMoreBtn.Margin.Right;
                TabScroll.MaxWidth = Math.Max(0, avail);
            }
            catch { }
        }

        private void TabStripHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTabScrollLimit();

        /// <summary>The scrollbar is hidden, so the wheel scrolls the chips sideways.</summary>
        private void TabScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (TabScroll.ScrollableWidth <= 0) return;
                TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset - e.Delta / 2.0);
                e.Handled = true;
            }
            catch { }
        }

        private void TabAddBtn_Click(object sender, RoutedEventArgs e) => NewDocument();

        /// <summary>The chevron (7+ tabs): every tab, with a check on the one being viewed.</summary>
        private void TabMoreBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menu = new ContextMenu
                {
                    Style = (Style)Application.Current.FindResource("ScrollingContextMenu"),
                    PlacementTarget = TabMoreBtn,
                    Placement = PlacementMode.Bottom,
                    FlowDirection = FlowDirection
                };
                foreach (var s in _tabs.Items.Where(t => t.Doc is not null || t.DeferredPath is not null))
                {
                    var target = s;
                    // A TextBlock header, not a string: a file name's underscores must not turn
                    // into access keys.
                    var name = new TextBlock
                    {
                        Text = s.DisplayName,
                        MaxWidth = 320,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var check = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse("M 0,5 L 3.5,8.5 L 10,1.5"),
                        StrokeThickness = 1.6,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Width = 11,
                        Height = 10,
                        Margin = new Thickness(16, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = ReferenceEquals(s, _s) ? Visibility.Visible : Visibility.Hidden
                    };
                    check.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
                    var header = new DockPanel();
                    DockPanel.SetDock(check, Dock.Right);
                    header.Children.Add(check);
                    header.Children.Add(name);

                    var item = new MenuItem { Header = header, ToolTip = s.OriginalPath ?? s.DisplayName };
                    item.Click += (_, _) => SwitchTo(target);
                    menu.Items.Add(item);
                }
                menu.IsOpen = true;
            }
            catch { }
        }

        // ---------- context menu ----------

        private void ShowTabContextMenu(DocumentSession s, FrameworkElement target)
        {
            try
            {
                if (_tabs.IndexOf(s) < 0) return;
                var menu = new ContextMenu
                {
                    PlacementTarget = target,
                    Placement = PlacementMode.MousePoint,
                    FlowDirection = FlowDirection
                };

                var close = MakeMenuItem(Loc("Str_Tab_Close"), (_, _) => CloseSession(s), "Ctrl+W");
                // Stable automation ids (locale-independent) for the E2E harness.
                System.Windows.Automation.AutomationProperties.SetAutomationId(close, "TabMenuClose");
                menu.Items.Add(close);

                var others = MakeMenuItem(Loc("Str_Tab_CloseOthers"), (_, _) => CloseOtherTabs(s), "Ctrl+Shift+W");
                others.IsEnabled = _tabs.Count > 1;
                System.Windows.Automation.AutomationProperties.SetAutomationId(others, "TabMenuCloseOthers");
                menu.Items.Add(others);

                int idx = _tabs.IndexOf(s);
                var right = MakeMenuItem(Loc("Str_Tab_CloseRight"), (_, _) => CloseTabsToRight(s));
                right.IsEnabled = idx >= 0 && idx < _tabs.Count - 1;
                System.Windows.Automation.AutomationProperties.SetAutomationId(right, "TabMenuCloseRight");
                menu.Items.Add(right);

                menu.Items.Add(new Separator());

                string? path = s.OriginalPath;
                bool exists = false;
                try { exists = !string.IsNullOrEmpty(path) && File.Exists(path); } catch { }
                var folder = MakeMenuItem(Loc("Str_Tab_OpenFolder"), (_, _) => OpenContainingFolder(path));
                folder.IsEnabled = exists;
                System.Windows.Automation.AutomationProperties.SetAutomationId(folder, "TabMenuOpenFolder");
                menu.Items.Add(folder);

                var copy = MakeMenuItem(Loc("Str_Tab_CopyPath"), async (_, _) =>
                {
                    try { if (!string.IsNullOrEmpty(path)) await SetClipboardTextAsync(path); } catch { }
                });
                copy.IsEnabled = !string.IsNullOrEmpty(path);
                System.Windows.Automation.AutomationProperties.SetAutomationId(copy, "TabMenuCopyPath");
                menu.Items.Add(copy);

                menu.IsOpen = true;
            }
            catch { }
        }

        /// <summary>Opens Explorer on the file's folder with the file selected.</summary>
        private static void OpenContainingFolder(string? path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch { }
        }
    }
}
