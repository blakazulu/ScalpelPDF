using System;
using System.Collections.Generic;
using PdfSharpCore.Pdf;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Document session: everything that belongs to ONE open document (one tab).
        //
        // The fields below USED to be MainWindow fields. They are now same-named properties that
        // forward to the ACTIVE session `_s`, so existing code (`_doc`, `_annotations`, `_isDirty`,
        // ...) keeps compiling and always talks to the tab being shown. Switching tabs is `_s = next`
        // plus a UI rebind; nothing is copied, so nothing can be forgotten.
        //
        // Async code that must keep writing to the document it started on captures `var s = _s;`
        // and uses `s.X` after an await, never the forwarding property.
        // ============================================================

        private sealed class DocumentSession
        {
            public Guid Id { get; } = Guid.NewGuid();

            // Model
            public PdfDocument? Doc;
            public string? WorkingPath;        // was _currentFile: temp/working copy PDFium renders
            public string? OriginalPath;       // was _originalFile: user's real file path; survives temp swaps from crop/rotate, used by Save

            /// <summary>Set on a tab restored at startup (Task 12: reopen open tabs) whose document
            /// has not been loaded yet - a background tab from the last session, not the one that
            /// was active. <see cref="OriginalPath"/> is set to the same path so the tab shows a
            /// real name and tooltip while empty; MaterializeDeferred (MainWindow.Tabs.cs) loads the
            /// file into this same session and clears this field the first time the tab is
            /// activated. Never set together with <see cref="Doc"/> being non-null.</summary>
            public string? DeferredPath;

            /// <summary>True when the open document needed a password or carried owner restrictions.
            /// Scalpel decrypts to a working copy at open time, so every save writes an unprotected
            /// PDF; this makes that visible instead of silent.</summary>
            public bool OpenedProtected;

            /// <summary>True when the open document has fillable form fields. The viewer draws those
            /// as live controls, so the page raster must NOT also bake them in or the two show through
            /// each other as ghost text.</summary>
            public bool HasFormFields;

            /// <summary>Set when a field's appearance stream could not be generated, so the save can
            /// fall back to asking the viewer to build appearances itself.</summary>
            public bool FormAppearanceFailed;

            public bool IsDirty;
            public readonly Dictionary<int, List<PageAnnotation>> Annotations = [];
            public readonly Dictionary<int, (int w, int h)> RenderDims = [];

            // Stores the PDF /Rotate value for each page.  The temp file used by Docnet has
            // rotation stripped to zero so FPDF_GetPageWidth/Height returns MediaBox dims and
            // the content isn't clipped; RotateBitmap is applied at render time instead.
            public readonly Dictionary<int, int> PageRotations = [];

            // Form filling — text/check keyed by widget object number; radio keyed by field name
            public readonly Dictionary<int, string> FormText = [];
            public readonly Dictionary<int, bool> FormCheck = [];
            public readonly Dictionary<string, string> FormRadio = [];

            // Undo stack — each entry is either an annotation removal or a full document snapshot.
            // Redo needs the annotation object itself: an Annotation undo only records "remove the
            // last annotation on page N", which cannot be reversed without the removed instance.
            public readonly Stack<UndoEntry> Undo = new();
            public readonly Stack<UndoEntry> Redo = new();

            // Printed blanks ("Name: ......" on a flattened form) are just text runs of dots or
            // underscores, so they are found once per page and cached; a text click never pays for a
            // PdfPig parse twice.
            public Dictionary<int, List<Scalpel.Services.TextEntryPlaceholder.Candidate>> BlankCache = [];

            // Whole-document search results (PDF-space rects per page)
            public readonly Dictionary<int, List<(double left, double bottom, double right, double top)>> SearchRects = [];
            public readonly List<int> SearchPages = [];
            public int SearchCursor = -1;

            // View (restored when the tab is shown again)
            public double ZoomLevel = 1.0;
            public FitMode FitMode = FitMode.None;
            public ViewMode ViewMode = ViewMode.Continuous;
            public int PageIndex = 0;
            public double ScrollH = 0, ScrollV = 0;
            public string? SearchQuery = null;
            public DateTime LastActivated = DateTime.UtcNow;

            // Sidebar thumbnails. Kept per session so returning to a tab shows its own pages at
            // once, and so a reload never seeds the list with another document's images.
            public PageThumbnailVm[]? Thumbs;
            public string? ThumbsForPath;      // the working file Thumbs were rendered from
            public System.Threading.CancellationTokenSource? ThumbCts;

            public string DisplayName =>
                string.IsNullOrEmpty(OriginalPath) ? "Untitled.pdf" : System.IO.Path.GetFileName(OriginalPath);

            /// <summary>A fresh session that inherits the view preferences of <paramref name="template"/>.</summary>
            public static DocumentSession CreateLike(DocumentSession? template) => new()
            {
                ZoomLevel = template?.ZoomLevel ?? 1.0,
                FitMode = template?.FitMode ?? FitMode.None,
                ViewMode = template?.ViewMode ?? ViewMode.Continuous,
            };
        }

        /// <summary>The session shown in the window. Never null.</summary>
        private DocumentSession _s = new();

        // ---- forwarding shim (one line per former field) ----
        private PdfDocument? _doc { get => _s.Doc; set => _s.Doc = value; }
        private string? _currentFile { get => _s.WorkingPath; set => _s.WorkingPath = value; }
        private string? _originalFile { get => _s.OriginalPath; set => _s.OriginalPath = value; }
        private bool _openedProtected { get => _s.OpenedProtected; set => _s.OpenedProtected = value; }
        private bool _docHasFormFields { get => _s.HasFormFields; set => _s.HasFormFields = value; }
        private bool _formAppearanceFailed { get => _s.FormAppearanceFailed; set => _s.FormAppearanceFailed = value; }
        private bool _isDirty { get => _s.IsDirty; set => _s.IsDirty = value; }
        private Dictionary<int, List<PageAnnotation>> _annotations => _s.Annotations;
        private Dictionary<int, (int w, int h)> _renderDims => _s.RenderDims;
        private Dictionary<int, int> _pageRotations => _s.PageRotations;
        private Dictionary<int, string> _formTextValues => _s.FormText;
        private Dictionary<int, bool> _formCheckValues => _s.FormCheck;
        private Dictionary<string, string> _formRadioValues => _s.FormRadio;
        private Stack<UndoEntry> _undoStack => _s.Undo;
        private Stack<UndoEntry> _redoStack => _s.Redo;
        private Dictionary<int, List<Scalpel.Services.TextEntryPlaceholder.Candidate>> _blankCache
        { get => _s.BlankCache; set => _s.BlankCache = value; }
        private Dictionary<int, List<(double left, double bottom, double right, double top)>> _allSearchRects => _s.SearchRects;
        private List<int> _searchResultPages => _s.SearchPages;
        private int _searchPageCursor { get => _s.SearchCursor; set => _s.SearchCursor = value; }
        private double _zoomLevel { get => _s.ZoomLevel; set => _s.ZoomLevel = value; }
        private FitMode _fitMode { get => _s.FitMode; set => _s.FitMode = value; }
        private ViewMode _viewMode { get => _s.ViewMode; set => _s.ViewMode = value; }
        private System.Threading.CancellationTokenSource? _thumbCts { get => _s.ThumbCts; set => _s.ThumbCts = value; }
    }
}
