using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Scalpel
{
    public partial class MainWindow : Window
    {
        private Point _dragStartPoint;

        // Zoom
        private double _lastRenderZoom = 1.0;
        private const double ZoomMin = 0.05;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 0.15;
        private enum FitMode { None, Width, Page }
        private System.Windows.Threading.DispatcherTimer? _rerenderTimer;
        private System.Threading.CancellationTokenSource? _secondaryRenderCts;
        private enum ViewMode { Single, Continuous, TwoPage, Grid }
        private enum AppMode { View, Edit, Pages, Sign }
        private AppMode _mode = AppMode.View;
        private bool _suppressModeEvents;
        /// <summary>Set while a click inside the document updates the current page, so the
        /// selection change does not scroll that page back to the top of the viewport.</summary>
        private bool _suppressScrollToPage;
        /// <summary>Keeps trackpad momentum from fanning through pages at a page edge.</summary>
        private readonly Scalpel.Services.WheelPageFlipGate _wheelFlipGate = new();
        /// <summary>Fractional wheel notches carried between Ctrl+wheel zoom events so a
        /// precision touchpad zooms proportionally instead of a notch at a time.</summary>
        private double _zoomWheelRemainder;

        // Restoring the scroll offset after a zoom is queued for after layout. Zooming again
        // before that runs queues a second restore, and the stale one lands last and throws the
        // view back to where the earlier gesture wanted it. Only the newest restore may act.
        private readonly Scalpel.Services.DeferredActionGate _zoomRestoreGate = new();
        private bool _suppressLogToggleEvent;
        private readonly StackPanel _continuousPanel = null!;
        private System.Threading.CancellationTokenSource? _continuousRenderCts;
        private readonly List<double> _continuousTops = [];
        private int _continuousScrollTarget = -1;  // re-scroll here once its true height is known
        private double _continuousPageW;

        // Editing
        private EditTool _currentTool = EditTool.Select;

        /// <summary>
        /// The visual rotation of a page, in degrees.
        /// <para>
        /// Scalpel keeps rotation OUT of the working document: <see cref="SaveTempAndReload"/>
        /// records each page's /Rotate in <see cref="_pageRotations"/> and writes 0 into the temp
        /// file, because Docnet sizes its bitmap from the unrotated MediaBox and rotated content
        /// would overflow it. The map is therefore only populated after a page operation - a
        /// freshly opened document still carries its real /Rotate - so anything that needs "how
        /// is this page actually oriented" must consult both, which is what this does.
        /// </para>
        /// </summary>
        internal int RotationOf(int pageIndex)
        {
            if (_pageRotations.TryGetValue(pageIndex, out int mapped))
                return ((mapped % 360) + 360) % 360;
            try
            {
                if (_doc is not null && pageIndex >= 0 && pageIndex < _doc.PageCount)
                    return ((_doc.Pages[pageIndex].Rotate % 360) + 360) % 360;
            }
            catch { }
            return 0;
        }

        private const string FormOverlayTag = "FormFieldOverlay";
        /// <summary>Form overlays sit below the annotation layer so an annotation placed over
        /// a field stays visible. Negative keeps them under everything added at the default 0.</summary>
        private const int FormOverlayZIndex = -1;

        /// <summary>
        /// Clamps a computed WPF Width/Height to something the layout system accepts. A malformed
        /// PDF (a zero-size stored signature canvas, a field rectangle of infinite height) would
        /// otherwise reach a WPF size property and take the whole viewer down with
        /// "'Infinity' is not a valid value for property 'Height'".
        /// </summary>
        internal static double SafeSize(double value, double fallback)
            => double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;

        // Undo stack — each entry is either an annotation removal or a full document snapshot.
        private enum UndoKind { Annotation, Document }
        // Redo needs the annotation object itself: an Annotation undo only records "remove the
        // last annotation on page N", which cannot be reversed without the removed instance.
        private readonly record struct UndoEntry(UndoKind Kind, int PageIdx = -1, byte[]? DocBytes = null,
                                                 bool WasDirty = false, PageAnnotation? Annotation = null);

        // A document-level undo stores a whole copy of the PDF, so an unbounded history costs
        // roughly (file size x number of page operations) in RAM - hundreds of megabytes on a big
        // scanned document. Both stacks are capped by depth AND by bytes; the newest entry is
        // always kept even when it alone is over budget.
        private const int UndoMaxEntries = 40;
        private const long UndoMaxBytes = 256L * 1024 * 1024;

        /// <summary>Approximate retained size of an undo entry, for the memory budget.</summary>
        private static long UndoEntrySize(UndoEntry e) => e.DocBytes?.LongLength ?? 4096;

        private void PushUndoEntry(UndoEntry entry) => Scalpel.Services.UndoHistoryBudget.PushBounded(
            _undoStack, entry, UndoEntrySize, UndoMaxEntries, UndoMaxBytes);

        private void PushRedoEntry(UndoEntry entry) => Scalpel.Services.UndoHistoryBudget.PushBounded(
            _redoStack, entry, UndoEntrySize, UndoMaxEntries, UndoMaxBytes);
        private bool _isDrawing;
        private Point _drawStart;
        private UIElement? _activePreview;
        private InkAnnotation? _activeInk;
        private TextBox? _activeTextBox;
        private PageAnnotation? _selectedAnnotation;
        private Border? _selectionBorder;

        // Draw/Highlight settings
        private Color _drawColor = Colors.Red;
        private double _drawWidth = 3;
        private byte _drawOpacity = 255;
        private Color _highlightColor = Color.FromArgb(80, 255, 255, 0);
        private Border? _drawSettingsBar;

        // Text (typewriter) tool settings
        private double _textFontSize = 24;
        private TextAnnotation? _reeditOriginal;  // placed-text annotation currently being re-edited
        private Color _textColor = Colors.Black;
        private Border? _textSettingsBar;

        // Signature / image resize
        private bool _isResizingSig;
        private Point _resizeSigStart;
        private double _resizeSigStartScale;
        private PlacedAnnotation? _resizeSigAnnot;
        private readonly List<Rectangle> _resizeHandles = [];   // 4 corner handles for placed annotations
        private string _resizeCorner = "SE";                    // which corner is being dragged
        private Point _resizeAnchor;                            // opposite corner, held fixed during resize

        // Placed annotation drag-to-move
        private bool _isDraggingAnnot;
        private Point _dragAnnotStart;

        // Middle-mouse / spacebar pan
        private bool _isPanning;
        private bool _spaceHeld;
        private Point _panStart;
        private double _panScrollH;
        private double _panScrollV;
        private Point _dragAnnotOrigPos;
        private PageAnnotation? _dragAnnot;   // placed image/signature OR typewriter text

        // Crop tool
        private Rect _cropCanvasRect;
        private Rectangle? _cropPreviewRect;
        private Rectangle? _cropPreviewRectBorder;  // unused after refactor; kept to avoid null-ref in cleanup
        private readonly List<System.Windows.Shapes.Path> _cropBrackets = []; // L-bracket corner visuals
        private Border? _cropConfirmBar;
        private readonly Button _toolCropBtn = null!;
        private readonly Button _toolLineBtn = null!;
        private readonly List<Rectangle> _cropHandles = [];
        private string? _activeCropHandleTag; // "NW" | "NE" | "SE" | "SW"
        private Point _cropHandleDragStart;
        private Rect _cropRectAtHandleDrag;
        private int _cropPageIndex = -1;   // page the crop rect was drawn on (grid/two-page aware)
        private TextBox? _cropX1Box, _cropY1Box, _cropX2Box, _cropY2Box;
        private TextBox? _cropRangeBox;
        private bool     _updatingCropInputs;
        private bool     _cropBarDragging;
        private Point    _cropBarDragOffset;

        // PDF link overlays (rendered on top of the annotation canvas)
        private readonly List<Canvas> _linkOverlays = [];

        // Sidebar + multi-page view
        private bool _sidebarCollapsed;
        private bool   _sidebarShowingOutlines;
        private bool   _outlinesFitted     = false;
        private double _savedPagesWidth    = 180;
        private double _savedOutlinesWidth = 300;
        private readonly Button _sidebarToggleBtn = null!;
        private readonly Border _sidebarBorder = null!;
        private readonly ColumnDefinition _sidebarCol = null!;
        private readonly WrapPanel _pageContentPanel = null!;

        // Text selection
        private bool _isSelecting;
        private Point _selectStart;
        private Rectangle? _selectRect;
        private string? _selectedText;

        // Search
        private Border? _searchBar;
        private TextBox? _searchBox;
        private TextBlock? _searchStatus;
        private readonly List<Rect> _searchHighlights = [];

        // Signatures
        private readonly SignatureStore _signatureStore = new();
        private SavedSignature? _pendingSignature;
        private System.Windows.Controls.Primitives.Popup? _signaturePopup;

        // Manual element refs (XAML codegen doesn't resolve these)
        private readonly Canvas _annotationCanvas = null!;
        // Active annotation surface. Single view: always _annotationCanvas. Continuous view:
        // set on mouse-down to the clicked page's overlay. Shared handlers target this.
        private Canvas _activeCanvas = null!;
        // Per-page overlay canvases for Continuous view, keyed by page index.
        private readonly Dictionary<int, Canvas> _continuousCanvases = [];
        private readonly Grid _pageContentGrid = null!;
        private readonly Button _toolSelectBtn = null!;
        private readonly Button _toolTextBtn = null!;
        private readonly Button _toolHighlightBtn = null!;
        private readonly Button _toolDrawBtn = null!;
        private readonly Button _toolSignatureBtn = null!;
        private readonly Button _toolImageBtn = null!;
        private readonly Button _saveAsBtnRef = null!;
        private readonly MenuItem _closeFileBtnRef = null!;
        private readonly ComboBox _zoomBox = null!;
        private readonly StackPanel _portableBadge = null!;
        private readonly TextBox _pageJumpBox = null!;
        private readonly TextBlock _pageTotalLabel = null!;

        public MainWindow()
        {
            InitializeComponent();
            // LocaleManager.Initialize ran before this window existed, so mirror RTL now for he/ar.
            this.FlowDirection = Scalpel.Services.LocaleManager.IsRtlLocale(Scalpel.Services.LocaleManager.Current)
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) VersionLabel.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
            _annotationCanvas = (Canvas)FindName("AnnotationCanvas")!;
            _activeCanvas = _annotationCanvas;
            _pageContentGrid = (Grid)FindName("PageContentGrid")!;
            _toolSelectBtn = (Button)FindName("ToolSelectBtn")!;
            _toolTextBtn = (Button)FindName("ToolTextBtn")!;
            _toolHighlightBtn = (Button)FindName("ToolHighlightBtn")!;
            _toolDrawBtn = (Button)FindName("ToolDrawBtn")!;
            _toolSignatureBtn = (Button)FindName("ToolSignatureBtn")!;
            _toolImageBtn = (Button)FindName("ToolImageBtn")!;
            _toolCropBtn = (Button)FindName("ToolCropBtn")!;
            _toolLineBtn = (Button)FindName("ToolLineBtn")!;
            _sidebarToggleBtn = (Button)FindName("SidebarToggleBtn")!;
            _sidebarBorder = (Border)FindName("SidebarBorder")!;
            _sidebarCol = (ColumnDefinition)FindName("SidebarCol")!;
            _pageContentPanel = (WrapPanel)FindName("PageContentPanel")!;
            _saveAsBtnRef = (Button)FindName("SaveAsBtn")!;
            _closeFileBtnRef = (MenuItem)FindName("CloseFileMenuItem")!;
            _zoomBox = (ComboBox)FindName("ZoomBox")!;
            _portableBadge = (StackPanel)FindName("PortableBadge")!;
            _pageJumpBox = (TextBox)FindName("PageJumpBox")!;
            _pageTotalLabel = (TextBlock)FindName("PageTotalLabel")!;
            _continuousPanel = (StackPanel)FindName("ContinuousPanel")!;
            PagePreviewPanel.ScrollChanged += PagePreviewPanel_ScrollChanged;
            if (Enum.TryParse<ViewMode>(App.GetSetting("ViewMode"), out var savedVm))
                _viewMode = savedVm;
            OutlineTree.SelectedItemChanged += OutlineTree_SelectedItemChanged;
            LoadSignatures();
            BuildContextMenu();
            SetTool(EditTool.Select);
            SetMode(AppMode.View);
            UpdateViewModeButtons();
            PopulateRecentList();
            ApplyGrainTexture();
            SourceInitialized += MainWindow_SourceInitialized;
            _tabs.Add(_s);   // the first session: an empty placeholder the first open fills
            Closed += (_, _) =>
            {
                foreach (var t in _tabs.Items) { try { t.Doc?.Close(); } catch { } }
                App.CleanupSessionTemps();
            };

            // Open a file passed via command-line / file association (e.g. double-clicking a .pdf)
            // Also show the portable badge when running outside the install location.
            ContentRendered += (_, _) => Services.ThemeManager.RefreshIcons();
            Services.ThemeManager.ThemeChanged += OnThemeChanged;

            Loaded += (_, _) =>
            {
#if DEBUG
                // Dev-only screenshot capture: `Scalpel.exe /shoot` renders the store
                // screenshot set and exits. Compiled out of release builds entirely.
                if (Environment.GetCommandLineArgs()
                        .Any(a => string.Equals(a, "/shoot", StringComparison.OrdinalIgnoreCase)))
                {
                    RunScreenshotHarness();
                    return;
                }
#endif
                RestoreWindowSettings();

                // Every argument that is an existing file (skipping arg[0] = exe path and flags
                // like /edit) opens as its own tab, in order; flag-vs-path order doesn't matter.
                // The "Edit with Scalpel PDF" context-menu verb launches us as: <exe> /edit "<file>".
                var args = Environment.GetCommandLineArgs();
                var (cmdFiles, editMode) = SingleInstanceProtocol.PickLaunchTargets(
                    args.Skip(1), System.IO.File.Exists);
                // A launch forwarded from a second Scalpel process is dispatched as soon as this
                // window exists, so it can open (or queue) a file before Loaded runs. Like a
                // command-line file it wins (R18): the tab restore is skipped entirely rather than
                // restoring around it (StartupOpenPolicy).
                bool alreadyOpen = _tabs.Items.Any(t => t.Doc is not null || t.DeferredPath is not null || t.IsDirty);
                // A forwarded open can still be mid-flight here: a password/repair prompt, or the
                // >20-files confirmation in OpenManyInTabs, runs a modal (nested message loop) that
                // pumps this very Loaded handler before the open has written a document, a deferred
                // path, or a pending-open entry anywhere - so alreadyOpen/_pendingOpens alone would
                // miss it and TryRestoreOpenTabs would stamp a restored path onto the session the
                // forwarded open is still populating. _tabOpDepth catches an open already inside its
                // BeginTabOp/EndTabOp span (e.g. a password/repair prompt); IsThreadModal also
                // catches OpenManyInTabs's own confirmation dialog, which shows before BeginTabOp is
                // reached.
                bool openInProgress = _tabOpDepth > 0 || System.Windows.Interop.ComponentDispatcher.IsThreadModal;
                var startup = StartupOpenPolicy.Decide(cmdFiles.Count, alreadyOpen, _pendingOpens.Count, openInProgress);
                if (startup == StartupOpen.CommandLine)
                {
                    // Jump straight to Edit mode for the "Edit with Scalpel PDF" verb — but only
                    // once a document actually loaded (OpenFile runs synchronously; _doc is null
                    // on failure or a declined repair prompt). OpenManyInTabs applies this once,
                    // to whichever tab ends up active, once the whole batch has actually opened.
                    OpenManyInTabs(cmdFiles, editMode);
                }
                else if (startup == StartupOpen.Restore && !TryRestoreOpenTabs())
                {
                    // No command-line files and no OpenTabs to restore (R18): fall back to the
                    // pre-tabs single "last file" behaviour.
                    var lastFile = App.GetSetting("LastFile");
                    if (!string.IsNullOrEmpty(lastFile) && System.IO.File.Exists(lastFile))
                    {
                        OpenInTab(lastFile!);
                        // If the reopen didn't actually load a document (open failed, or the
                        // user declined the repair prompt), forget it — otherwise the same
                        // damaged file would re-prompt on every subsequent launch.
                        if (_doc is null)
                            App.SetSetting("LastFile", "");
                    }
                }

                if (App.IsPortable())
                    _portableBadge.Visibility = Visibility.Visible;
            };

            Loaded += async (_, _) =>
            {
                EnsureUpdateOptIn();
                await CheckForUpdatesAsync();
            };
        }

        // ============================================================
        // Maximize-respects-taskbar fix (WindowStyle=None needs WM_GETMINMAXINFO)
        // ============================================================

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            ThemeManager.ApplyDwm(hwnd);
        }

    }

}
