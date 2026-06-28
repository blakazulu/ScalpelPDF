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
        private PdfDocument? _doc;
        private string? _currentFile;
        private string? _originalFile;  // user's real file path; survives temp swaps from crop/rotate, used by Save
        private Point _dragStartPoint;

        // Zoom
        private double _zoomLevel = 1.0;
        private double _lastRenderZoom = 1.0;
        private const double ZoomMin = 0.05;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 0.15;
        private enum FitMode { None, Width, Page }
        private FitMode _fitMode = FitMode.None;
        private System.Windows.Threading.DispatcherTimer? _rerenderTimer;
        private System.Threading.CancellationTokenSource? _secondaryRenderCts;
        private enum ViewMode { Single, Continuous, TwoPage, Grid }
        private ViewMode _viewMode = ViewMode.Continuous;
        private enum AppMode { View, Edit, Pages, Sign }
        private AppMode _mode = AppMode.View;
        private bool _suppressModeEvents;
        private bool _suppressLogToggleEvent;
        private readonly StackPanel _continuousPanel = null!;
        private System.Threading.CancellationTokenSource? _continuousRenderCts;
        private readonly List<double> _continuousTops = [];
        private int _continuousScrollTarget = -1;  // re-scroll here once its true height is known
        private double _continuousPageW;

        // Editing
        private EditTool _currentTool = EditTool.Select;
        private readonly Dictionary<int, List<PageAnnotation>> _annotations = [];
        private readonly Dictionary<int, (int w, int h)> _renderDims = [];
        // Stores the PDF /Rotate value for each page.  The temp file used by Docnet has
        // rotation stripped to zero so FPDF_GetPageWidth/Height returns MediaBox dims and
        // the content isn't clipped; RotateBitmap is applied at render time instead.
        private readonly Dictionary<int, int> _pageRotations = [];

        // Form filling — text/check keyed by widget object number; radio keyed by field name
        private readonly Dictionary<int, string>    _formTextValues  = [];
        private readonly Dictionary<int, bool>      _formCheckValues = [];
        private readonly Dictionary<string, string> _formRadioValues = [];
        private const string FormOverlayTag = "FormFieldOverlay";

        // Undo stack — each entry is either an annotation removal or a full document snapshot.
        private enum UndoKind { Annotation, Document }
        private readonly record struct UndoEntry(UndoKind Kind, int PageIdx = -1, byte[]? DocBytes = null, bool WasDirty = false);
        private readonly Stack<UndoEntry> _undoStack = new();
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
        private Border? _signaturePopup;

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

        // Dirty / unsaved-change tracking
        private bool _isDirty = false;

        // Whole-document search results (PDF-space rects per page)
        private readonly Dictionary<int, List<(double left, double bottom, double right, double top)>> _allSearchRects = [];
        private readonly List<int> _searchResultPages = [];
        private int _searchPageCursor = -1;

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
            ApplyGrainTexture();
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += (_, _) => { _doc?.Close(); App.CleanupSessionTemps(); };

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

                var args = Environment.GetCommandLineArgs();
                // Find the first argument that is an existing file (skipping arg[0] = exe path
                // and flags like /edit), so flag-vs-path order doesn't matter. The "Edit with
                // Scalpel PDF" context-menu verb launches us as: <exe> /edit "<file>".
                string? fileArg = null;
                bool editMode = false;
                for (int i = 1; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "/edit", StringComparison.OrdinalIgnoreCase))
                        editMode = true;
                    else if (fileArg is null && System.IO.File.Exists(args[i]))
                        fileArg = args[i];
                }
                if (fileArg is not null)
                {
                    OpenFile(fileArg);
                    // Jump straight to Edit mode for the "Edit with Scalpel PDF" verb — but only
                    // once a document actually loaded (OpenFile runs synchronously; _doc is null
                    // on failure or a declined repair prompt).
                    if (editMode && _doc is not null)
                        SetMode(AppMode.Edit);
                }
                else
                {
                    // Reopen the last file if no file argument was provided
                    var lastFile = App.GetSetting("LastFile");
                    if (!string.IsNullOrEmpty(lastFile) && System.IO.File.Exists(lastFile))
                    {
                        OpenFile(lastFile!);
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

        // ============================================================
        // Inline text editing (double-click)
        // ============================================================

        private void EditTextAtPosition(Point canvasPos, int pageIdx)
        {
            if (_currentFile is null || !_renderDims.ContainsKey(pageIdx)) return;

            // Commit any existing edit first
            if (_activeTextBox is not null)
            {
                CommitActiveTextBox();
                return;
            }

            // Re-edit an already-committed TextEditAnnotation without re-reading the PDF.
            // Without this check, a second double-click would read the original file, produce
            // a duplicate whiteout+text layer, and cause the "overlapping quasi-duplicates" bug.
            if (_annotations.TryGetValue(pageIdx, out var existingPage))
            {
                var existingEdit = existingPage.OfType<TextEditAnnotation>()
                    .FirstOrDefault(a => a.OriginalBounds.Contains(canvasPos));
                if (existingEdit is not null)
                {
                    var reb = existingEdit.OriginalBounds;
                    var retb = new TextBox
                    {
                        Text = existingEdit.NewContent,
                        Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                        Foreground = Brushes.Black,
                        BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                        BorderThickness = new Thickness(2),
                        FontFamily = new FontFamily(existingEdit.FontName),
                        FontSize = Math.Max(existingEdit.FontSize, 10),
                        FontWeight = ToWeight(existingEdit.IsBold),
                        FontStyle = ToStyle(existingEdit.IsItalic),
                        FlowDirection = Scalpel.Services.BidiReorder.ContainsRtl(existingEdit.NewContent)
                            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        MinWidth = Math.Max(reb.Width + 20, 100),
                        // Height from the font size so the box fits the text at any size
                        // (see EditTextAtPosition new-edit path for the rationale).
                        Height = Math.Max(Math.Max(existingEdit.FontSize, 10) * 1.35 + 6, 24),
                        Padding = new Thickness(2, 0, 2, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        AcceptsReturn = false,
                        Tag = new TextEditContext
                        {
                            PageIndex = pageIdx,
                            OriginalText = existingEdit.OriginalContent,
                            CanvasBounds = reb,
                            Position = existingEdit.Position,
                            FontSize = existingEdit.FontSize,
                            FontName = existingEdit.FontName,
                            IsBold = existingEdit.IsBold,
                            IsItalic = existingEdit.IsItalic,
                            // Carry the embedded font forward so re-edits re-gate against the new text
                            // (bytes are fetched from the resolver at commit via this key).
                            EmbeddedFamilyKey = existingEdit.ExactFontFamily,
                            ExistingAnnotation = existingEdit
                        }
                    };
                    Canvas.SetLeft(retb, reb.X);
                    Canvas.SetTop(retb, reb.Y);
                    _activeCanvas.Children.Add(retb);
                    _activeTextBox = retb;
                    var rewo = new Rectangle
                    {
                        Fill = Brushes.White,
                        Width = reb.Width + 4,
                        Height = reb.Height + 4,
                        IsHitTestVisible = false,
                        Tag = "EditWhiteout"
                    };
                    Canvas.SetLeft(rewo, reb.X - 2);
                    Canvas.SetTop(rewo, reb.Y - 2);
                    _activeCanvas.Children.Insert(_activeCanvas.Children.IndexOf(retb), rewo);
                    retb.KeyDown += EditTextBox_KeyDown;
                    retb.Loaded += (s, ev) => { retb.Focus(); Keyboard.Focus(retb); retb.SelectAll(); retb.LostFocus += EditTextBox_LostFocus; };
                    SetStatus("Re-editing text — Enter to save, Escape to cancel");
                    return;
                }
            }

            // Re-edit a user-placed text annotation: lift it into an editable box
            // pre-filled with its content, size (shown in points), and color.
            if (_annotations.TryGetValue(pageIdx, out var placedPage))
            {
                var placed = placedPage.OfType<TextAnnotation>()
                    .LastOrDefault(a => HitTestAnnotation(a, canvasPos, out _));
                if (placed is not null)
                {
                    var pcol = placed.GetColor();
                    _textColor = pcol;
                    double syp = 1.0;
                    if (_doc is not null && _renderDims.TryGetValue(pageIdx, out var prd) && prd.h > 0)
                        syp = _doc.Pages[pageIdx].Height.Point / prd.h;
                    _textFontSize = Math.Max(1, Math.Round(placed.FontSize * syp));

                    _reeditOriginal = placed;
                    placedPage.Remove(placed);
                    RenderAllAnnotations(pageIdx);

                    var ptb = new TextBox
                    {
                        Text = placed.Content,
                        Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                        Foreground = new SolidColorBrush(pcol),
                        BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                        BorderThickness = new Thickness(1),
                        FontFamily = (FontFamily)FindResource("FontUI"),
                        FontSize = placed.FontSize,
                        MinWidth = 120,
                        MinHeight = 24,
                        Padding = new Thickness(2),
                        AcceptsReturn = true,
                        Tag = pageIdx
                    };
                    Canvas.SetLeft(ptb, placed.Position.X);
                    Canvas.SetTop(ptb, placed.Position.Y);
                    _activeCanvas.Children.Add(ptb);
                    _activeTextBox = ptb;
                    ptb.KeyDown += TextBox_KeyDown;
                    ptb.Loaded += (s, ev) => { ptb.Focus(); Keyboard.Focus(ptb); ptb.SelectAll(); ptb.LostFocus += TextBox_LostFocus; };
                    ShowTextSettings();
                    SetStatus("Editing text — change size/color above, Enter to save");
                    return;
                }
            }

            try
            {
                var (renderW, renderH) = _renderDims[pageIdx];

                using var pigDoc = PdfPigDoc.Open(_currentFile);
                if (pageIdx >= pigDoc.NumberOfPages) return;
                var page = pigDoc.GetPage(pageIdx + 1);

                double pdfW = page.Width;
                double pdfH = page.Height;
                double sxInv = (double)renderW / pdfW; // pdf->canvas
                double syInv = (double)renderH / pdfH;

                // Convert all words to canvas coordinates upfront
                var canvasWords = page.GetWords().Select(w =>
                {
                    double cx = w.BoundingBox.Left * sxInv;
                    double cy = renderH - (w.BoundingBox.Top * syInv);
                    double cw = (w.BoundingBox.Right - w.BoundingBox.Left) * sxInv;
                    double ch = (w.BoundingBox.Top - w.BoundingBox.Bottom) * syInv;
                    return new { Word = w, Rect = new Rect(cx, cy, cw, ch) };
                }).ToList();

                if (canvasWords.Count == 0) { SetStatus("No selectable text — this page may be a scanned image"); return; }

                // Find words on the same line as the click (Y overlap with tolerance)
                var clickY = canvasPos.Y;
                var lineWords = canvasWords
                    .Where(cw => clickY >= cw.Rect.Top - 3 && clickY <= cw.Rect.Bottom + 3)
                    .OrderBy(cw => cw.Rect.Left)  // strictly left-to-right
                    .ToList();

                if (lineWords.Count == 0)
                {
                    // Try nearest line within 20px
                    var nearest = canvasWords
                        .OrderBy(cw => Math.Abs((cw.Rect.Top + cw.Rect.Bottom) / 2 - clickY))
                        .First();
                    double nearMidY = (nearest.Rect.Top + nearest.Rect.Bottom) / 2;
                    lineWords = [..canvasWords
                        .Where(cw => Math.Abs((cw.Rect.Top + cw.Rect.Bottom) / 2 - nearMidY) < 5)
                        .OrderBy(cw => cw.Rect.Left)];
                }

                if (lineWords.Count == 0)
                {
                    SetStatus("No text line found at this position");
                    return;
                }

                // Compute bounding box in canvas space
                double cLeft = lineWords.Min(w => w.Rect.Left);
                double cTop = lineWords.Min(w => w.Rect.Top);
                double cRight = lineWords.Max(w => w.Rect.Right);
                double cBottom = lineWords.Max(w => w.Rect.Bottom);
                double cWidth = cRight - cLeft;
                double cHeight = cBottom - cTop;

                // Reconstruct the line in LOGICAL order. PdfPig returns words left-to-right, so a
                // Hebrew/Arabic line's words would join reversed; JoinWordsLogical walks RTL lines
                // right-to-left so the edit box (and the burned-in edit) reads correctly.
                string lineText = Scalpel.Services.BidiReorder.JoinWordsLogical(
                    [.. lineWords.Select(w => (w.Word.Text, w.Rect.Left))]);

                // Get actual font info from PdfPig letter data
                double canvasFontSize = cHeight * 0.75; // fallback
                string fontName = "Segoe UI"; // fallback
                bool isBold = false, isItalic = false;
                byte[]? embeddedBytes = null;   // the document's own font, when not installed
                string? embeddedKey = null;
                string fontDisplay = "";
                var firstWord = lineWords.First().Word;
                try
                {
                    if (firstWord.Letters.Count > 0)
                    {
                        var letter = firstWord.Letters[0];
                        // PointSize is the size as actually rendered (it accounts for text-matrix
                        // scaling); letter.FontSize is only the raw `Tf` size, which is often 1pt
                        // for matrix-scaled big text and would yield a tiny edit box. Prefer
                        // PointSize, fall back to FontSize, then to the measured glyph height.
                        double pdfFontPts = letter.PointSize > 0 ? letter.PointSize
                                          : letter.FontSize > 0 ? letter.FontSize
                                          : 0;
                        canvasFontSize = pdfFontPts > 0 ? pdfFontPts * syInv : cHeight * 0.9;

                        // Resolve raw PdfPig font name -> family + style + availability.
                        string? rawFont = null;
                        try { rawFont = letter.FontName; } catch { }
                        if (string.IsNullOrEmpty(rawFont))
                        {
                            try { rawFont = firstWord.FontName; } catch { }
                        }
                        var resolved = Scalpel.Services.FontResolver.Resolve(rawFont, AvailableFontFamilies());
                        fontName = resolved.FamilyName;
                        isBold = resolved.IsBold;
                        isItalic = resolved.IsItalic;
                        fontDisplay = resolved.DisplayName;
                        if (!resolved.IsInstalled)
                        {
                            // The font isn't installed. Try to use the DOCUMENT'S OWN embedded font so
                            // the edit looks identical. Usable only when the embedded program carries a
                            // Unicode cmap covering the line (subset CID fonts usually don't — then we
                            // fall back to a substitute and tell the user to install the font).
                            byte[]? emb = _currentFile is null ? null
                                : Scalpel.Services.EmbeddedFontExtractor.TryExtract(_currentFile, rawFont ?? resolved.DisplayName, out _);
                            if (emb is { Length: > 0 } && Scalpel.Services.TrueTypeCmap.CoversAllText(emb, lineText))
                            {
                                embeddedBytes = emb;
                                embeddedKey = "__emb_" + Scalpel.Services.EmbeddedFontExtractor.Normalize(resolved.DisplayName) + "_" + emb.Length;
                                Scalpel.Services.PdfFontResolver.Instance.RegisterBundledFont(embeddedKey, emb, isBold, isItalic);
                                // Exact font available — no toast.
                            }
                            else
                            {
                                ShowToast(string.Format(Loc("Str_FontMissing_Body"), resolved.DisplayName), resolved.DisplayName);
                            }
                        }
                    }
                }
                catch { /* use fallbacks */ }

                // Show editable TextBox over the line
                var tb = new TextBox
                {
                    Text = lineText,
                    Background = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)),
                    Foreground = Brushes.Black,
                    BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                    BorderThickness = new Thickness(2),
                    FontFamily = new FontFamily(fontName),
                    FontWeight = ToWeight(isBold),
                    FontStyle = ToStyle(isItalic),
                    FontSize = Math.Max(canvasFontSize, 10),
                    // Hebrew/Arabic lines read right-to-left: base the box direction on the text
                    // so the caret, alignment and typing behave naturally while editing.
                    FlowDirection = Scalpel.Services.BidiReorder.ContainsRtl(lineText)
                        ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                    MinWidth = Math.Max(cWidth + 20, 100),
                    // Fit the box height to the FONT (line height ~1.35em + borders), not the
                    // measured glyph bbox + a constant — the constant under-sizes big text and
                    // clips it. This tracks the selected text's height at any size.
                    Height = Math.Max(Math.Max(canvasFontSize, 10) * 1.35 + 6, 24),
                    Padding = new Thickness(2, 0, 2, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    AcceptsReturn = false,
                    Tag = new TextEditContext
                    {
                        PageIndex = pageIdx,
                        OriginalText = lineText,
                        CanvasBounds = new Rect(cLeft, cTop, cWidth, cHeight),
                        Position = new Point(cLeft, cTop),
                        FontSize = Math.Max(canvasFontSize, 10),
                        FontName = fontName,
                        IsBold = isBold,
                        IsItalic = isItalic,
                        EmbeddedFontBytes = embeddedBytes,
                        EmbeddedFamilyKey = embeddedKey,
                        FontDisplay = fontDisplay,
                    }
                };
                Canvas.SetLeft(tb, cLeft);
                Canvas.SetTop(tb, cTop);
                _activeCanvas.Children.Add(tb);
                _activeTextBox = tb;

                if (Scalpel.Services.BidiReorder.ContainsRtl(lineText))
                {
                    tb.FlowDirection = FlowDirection.RightToLeft;
                    int rtlProbe = Scalpel.Services.ArabicShaper.ContainsArabic(lineText) ? 0x0628 : 0x05D0;
                    if (!FontCovers(fontName, isBold, isItalic, rtlProbe))
                        tb.FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew, Noto Sans Arabic");
                }

                // Show white-out behind the edit box so original text is hidden
                var whiteout = new Rectangle
                {
                    Fill = Brushes.White,
                    Width = cWidth + 4,
                    Height = cHeight + 4,
                    IsHitTestVisible = false,
                    Tag = "EditWhiteout"
                };
                Canvas.SetLeft(whiteout, cLeft - 2);
                Canvas.SetTop(whiteout, cTop - 2);
                int tbIdx = _activeCanvas.Children.IndexOf(tb);
                _activeCanvas.Children.Insert(tbIdx, whiteout);

                tb.KeyDown += EditTextBox_KeyDown;
                tb.Loaded += (s, ev) =>
                {
                    tb.Focus();
                    Keyboard.Focus(tb);
                    tb.SelectAll();
                    tb.LostFocus += EditTextBox_LostFocus;
                };

                SetStatus("Editing text - Enter to save, Escape to cancel");
            }
            catch (Exception ex)
            {
                SetStatus($"Text edit error: {ex.Message}");
            }
        }

        /// <summary>Context data attached to an inline text edit TextBox via Tag.</summary>
        private class TextEditContext
        {
            public int PageIndex { get; set; }
            public string OriginalText { get; set; } = "";
            public Rect CanvasBounds { get; set; }
            public Point Position { get; set; }
            public double FontSize { get; set; }
            public string FontName { get; set; } = "Segoe UI";
            public bool IsBold { get; set; }
            public bool IsItalic { get; set; }
            /// <summary>The document's own embedded font bytes (extracted when the original font isn't
            /// installed), and the resolver key it was registered under. Used to redraw the edit in the
            /// exact font when it covers the typed text; null when unavailable/unusable.</summary>
            public byte[]? EmbeddedFontBytes { get; set; }
            public string? EmbeddedFamilyKey { get; set; }
            public string FontDisplay { get; set; } = "";
            /// <summary>Non-null when re-editing an already-committed annotation; update in place instead of adding a new one.</summary>
            public TextEditAnnotation? ExistingAnnotation { get; set; }
        }

        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CommitTextEdit();
                e.Handled = true;
            }
        }

        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_activeTextBox is not null && _activeTextBox.Tag is TextEditContext)
            {
                Dispatcher.BeginInvoke(new Action(CommitTextEdit),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void CancelTextEdit()
        {
            if (_activeTextBox is null) return;
            var tb = _activeTextBox;
            _activeTextBox = null;
            _activeCanvas.Children.Remove(tb);
            // Remove the whiteout rectangle
            var whiteout = _activeCanvas.Children.OfType<Rectangle>()
                .FirstOrDefault(r => r.Tag is string s && s == "EditWhiteout");
            if (whiteout is not null)
                _activeCanvas.Children.Remove(whiteout);
            SetStatus("Text edit cancelled");
        }

        private void CommitTextEdit()
        {
            if (_activeTextBox is null || _activeTextBox.Tag is not TextEditContext ctx) return;
            var tb = _activeTextBox;
            _activeTextBox = null;
            string newText = tb.Text.Trim();
            _activeCanvas.Children.Remove(tb);

            // Remove the whiteout rectangle
            var whiteout = _activeCanvas.Children.OfType<Rectangle>()
                .FirstOrDefault(r => r.Tag is string s && s == "EditWhiteout");
            if (whiteout is not null)
                _activeCanvas.Children.Remove(whiteout);

            if (string.IsNullOrEmpty(newText) || newText == ctx.OriginalText)
            {
                SetStatus(newText == ctx.OriginalText ? "No changes made" : "Text edit cancelled (empty)");
                return;
            }

            // If we have the document's own embedded font, use it for the EDIT only when it covers
            // every character the user actually typed (a subset font can't render brand-new glyphs).
            // Otherwise fall back to the substitute font and warn that the original isn't installed.
            byte[]? embBytes = ctx.EmbeddedFontBytes;
            if (embBytes is null && ctx.EmbeddedFamilyKey is not null)
                Scalpel.Services.PdfFontResolver.Instance.TryGetExactFontBytes(ctx.EmbeddedFamilyKey, ctx.IsBold, ctx.IsItalic, out embBytes);
            string? exactFamily = (ctx.EmbeddedFamilyKey is not null && embBytes is { Length: > 0 }
                                   && Scalpel.Services.TrueTypeCmap.CoversAllText(embBytes, newText))
                ? ctx.EmbeddedFamilyKey : null;
            // Had an exact font for the original text, but the new text adds glyphs it lacks → substitute + warn.
            if (exactFamily is null && ctx.EmbeddedFamilyKey is not null && !string.IsNullOrEmpty(ctx.FontDisplay))
                ShowToast(string.Format(Loc("Str_FontMissing_Body"), ctx.FontDisplay), ctx.FontDisplay);

            if (ctx.ExistingAnnotation is not null)
            {
                // Update the existing annotation in place — avoids duplicate whiteout layers
                ctx.ExistingAnnotation.NewContent = newText;
                ctx.ExistingAnnotation.ExactFontFamily = exactFamily;
            }
            else
            {
                var edit = new TextEditAnnotation
                {
                    PageIndex = ctx.PageIndex,
                    OriginalBounds = ctx.CanvasBounds,
                    Position = ctx.Position,
                    NewContent = newText,
                    OriginalContent = ctx.OriginalText,
                    FontSize = ctx.FontSize,
                    FontName = ctx.FontName,
                    IsBold = ctx.IsBold,
                    IsItalic = ctx.IsItalic,
                    ExactFontFamily = exactFamily,
                };
                AddAnnotation(edit);
            }
            RenderAllAnnotations(ctx.PageIndex);
            SetStatus($"Text edited: \"{ctx.OriginalText}\" -> \"{newText}\"");
        }

        // ============================================================
        // Text box handling
        // ============================================================

        private void PlaceTextBox(Point pos, int pageIdx)
        {
            // _textFontSize is a point size; convert to the page's canvas (render-dim) units so
            // it renders and exports as real points. DrawAnnotationsOnDocument multiplies by
            // sy = page.Height.Point / renderH, so dividing by sy here makes "14" export as 14pt.
            double fontCanvas = _textFontSize;
            if (_doc is not null && _renderDims.TryGetValue(pageIdx, out var rdims) && rdims.h > 0)
            {
                double sy = _doc.Pages[pageIdx].Height.Point / rdims.h;
                if (sy > 0) fontCanvas = _textFontSize / sy;
            }
            var tb = new TextBox
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                Foreground = new SolidColorBrush(_textColor),
                BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew, Noto Sans Arabic"),
                FontSize = fontCanvas,
                MinWidth = 120,
                MinHeight = 24,
                Padding = new Thickness(2),
                AcceptsReturn = true,
                Tag = pageIdx
            };
            Canvas.SetLeft(tb, pos.X);
            Canvas.SetTop(tb, pos.Y);
            _activeCanvas.Children.Add(tb);
            _activeTextBox = tb;
            tb.TextChanged += (s, e) =>
            {
                tb.FlowDirection = Scalpel.Services.BidiReorder.ContainsRtl(tb.Text)
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
            };
            tb.KeyDown += TextBox_KeyDown;
            // Defer focus until the TextBox is actually rendered
            tb.Loaded += (s, e) =>
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.LostFocus += TextBox_LostFocus;
            };
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_activeTextBox is not null)
                {
                    _activeCanvas.Children.Remove(_activeTextBox);
                    _activeTextBox = null;
                }
                if (_reeditOriginal is not null)
                {
                    int rp = _reeditOriginal.PageIndex;
                    if (!_annotations.TryGetValue(rp, out var rlist)) { rlist = []; _annotations[rp] = rlist; }
                    rlist.Add(_reeditOriginal);
                    _reeditOriginal = null;
                    RenderAllAnnotations(rp);
                }
                if (_currentTool != EditTool.Text) HideTextSettings();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                CommitActiveTextBox();
                e.Handled = true;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Only commit if the TextBox actually has content
            if (_activeTextBox is not null && !string.IsNullOrWhiteSpace(_activeTextBox.Text))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Keep the edit box open if focus moved into the size/color bar so the
                    // user can restyle (the Size ComboBox takes focus; color swatches do not).
                    if (_textSettingsBar is not null && Keyboard.FocusedElement is DependencyObject fe
                        && IsDescendantOf(fe, _textSettingsBar))
                        return;
                    CommitActiveTextBox();
                }),
                System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void CommitActiveTextBox()
        {
            if (_activeTextBox is null) return;
            // If it's an inline text edit, use the dedicated commit path
            if (_activeTextBox.Tag is TextEditContext)
            {
                CommitTextEdit();
                return;
            }
            var tb = _activeTextBox;
            _activeTextBox = null;
            _reeditOriginal = null;   // committing replaces any annotation being re-edited

            string content = tb.Text.Trim();
            int pageIdx = tb.Tag is int idx ? idx : PageList.SelectedIndex;
            double x = Canvas.GetLeft(tb);
            double y = Canvas.GetTop(tb);

            _activeCanvas.Children.Remove(tb);

            if (!string.IsNullOrEmpty(content))
            {
                var ta = new TextAnnotation
                {
                    PageIndex = pageIdx,
                    Position = new Point(x, y),
                    Content = content,
                    FontSize = tb.FontSize
                };
                ta.SetColor(tb.Foreground is SolidColorBrush scb ? scb.Color : Colors.Black);
                AddAnnotation(ta);
                RenderTextAnnotation(ta);
            }
            if (_currentTool != EditTool.Text) HideTextSettings();
        }

        // ============================================================
        // Keyboard shortcuts
        // ============================================================

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Don't intercept keys when typing in any TextBox (typewriter tool or form field)
            if (e.OriginalSource is TextBox) return;
            if (_activeTextBox is not null && _activeTextBox.IsFocused) return;

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedText();
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SelectAllText();
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleSearchBar();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _currentTool == EditTool.Crop && _cropConfirmBar is not null)
            {
                ApplyCrop([PageList.SelectedIndex]);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _currentTool == EditTool.Crop && _cropConfirmBar is not null)
            {
                HideCropConfirmBar();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ShortcutOverlay.Visibility == Visibility.Visible)
            {
                ShortcutOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _searchBar is not null && _searchBar.Visibility == Visibility.Visible)
            {
                CloseSearchBar();
                e.Handled = true;
            }
            else if (e.Key == Key.OemQuestion && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShortcutOverlay.Visibility = ShortcutOverlay.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                e.Handled = true;
            }
            else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Print_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _selectedAnnotation is not null)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Undo_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                SaveAs_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveInPlace();
                e.Handled = true;
            }
            else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CloseFile();
                e.Handled = true;
            }
            else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Open_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                NewDocument();
                e.Handled = true;
            }
            else if ((e.Key == Key.Left || e.Key == Key.Up) && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_doc is not null && PageList.SelectedIndex > 0)
                {
                    PageList.SelectedIndex--;
                    e.Handled = true;
                }
            }
            else if ((e.Key == Key.Right || e.Key == Key.Down) && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_doc is not null && PageList.SelectedIndex < _doc.PageCount - 1)
                {
                    PageList.SelectedIndex++;
                    e.Handled = true;
                }
            }
            else if ((e.Key == Key.OemPlus || e.Key == Key.Add) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewMode == ViewMode.Grid) GridZoomStep(false); else SetZoom(_zoomLevel + ZoomStep);
                e.Handled = true;
            }
            else if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_viewMode == ViewMode.Grid) GridZoomStep(true); else SetZoom(_zoomLevel - ZoomStep);
                e.Handled = true;
            }
            else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SetZoom(1.0);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // No overlay active — ESC exits the app
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Space && !_spaceHeld)
            {
                _spaceHeld = true;
                PagePreviewPanel.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            base.OnPreviewKeyUp(e);
            if (e.Key == Key.Space && _spaceHeld)
            {
                _spaceHeld = false;
                if (!_isPanning)
                    PagePreviewPanel.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        // ============================================================
        // Annotation management
        // ============================================================

        private void AddAnnotation(PageAnnotation annotation)
        {
            if (!_annotations.ContainsKey(annotation.PageIndex))
                _annotations[annotation.PageIndex] = [];
            _annotations[annotation.PageIndex].Add(annotation);
            _undoStack.Push(new UndoEntry(UndoKind.Annotation, annotation.PageIndex, WasDirty: _isDirty));
            MarkDirty();
        }

        /// <summary>
        /// Saves the current in-memory document bytes onto the undo stack so that
        /// document-level operations (crop, delete page, merge, reorder) can be undone.
        /// Must be called BEFORE modifying _doc.
        /// </summary>
        private void PushDocUndo()
        {
            if (_doc is null) return;
            using var ms = new System.IO.MemoryStream();
            _doc.Save(ms);
            _undoStack.Push(new UndoEntry(UndoKind.Document, DocBytes: ms.ToArray(), WasDirty: _isDirty));
        }

        private void RenderTextAnnotation(TextAnnotation ta)
        {
            var tb = new TextBlock
            {
                Text = ta.Content,
                Foreground = new SolidColorBrush(ta.GetColor()),
                FontFamily = (FontFamily)FindResource("FontUI"),
                FontSize = ta.FontSize,
                Padding = new Thickness(2)
            };
            if (Scalpel.Services.BidiReorder.ContainsRtl(ta.Content))
            {
                tb.FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew, Noto Sans Arabic");
                tb.FlowDirection = FlowDirection.RightToLeft;
            }
            Canvas.SetLeft(tb, ta.Position.X);
            Canvas.SetTop(tb, ta.Position.Y);
            _activeCanvas.Children.Add(tb);
        }

        private void RenderAllAnnotations(int pageIndex)
        {
            // Resolve this page's annotation surface from the unified per-page overlay map, which
            // every multi-page view populates; fall back to the single-page canvas. View-mode
            // independent on purpose so the tools behave identically in all four modes.
            _activeCanvas = _continuousCanvases.TryGetValue(pageIndex, out var pageCanvas)
                ? pageCanvas : _annotationCanvas;
            _activeCanvas.Children.Clear();

            if (_annotations.TryGetValue(pageIndex, out var annotList))
            foreach (var annot in annotList)
            {
                switch (annot)
                {
                    case TextAnnotation ta:
                        RenderTextAnnotation(ta);
                        break;
                    case HighlightAnnotation ha:
                        var rect = new Rectangle
                        {
                            Fill = new SolidColorBrush(ha.GetColor()),
                            Width = ha.Bounds.Width,
                            Height = ha.Bounds.Height
                        };
                        Canvas.SetLeft(rect, ha.Bounds.X);
                        Canvas.SetTop(rect, ha.Bounds.Y);
                        _activeCanvas.Children.Add(rect);
                        break;
                    case InkAnnotation ia:
                        if (ia.Points.Count < 2) continue;
                        var poly = new Polyline
                        {
                            Stroke = new SolidColorBrush(ia.GetColor()),
                            StrokeThickness = ia.StrokeWidth,
                            StrokeLineJoin = PenLineJoin.Round,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round
                        };
                        foreach (var pt in ia.Points) poly.Points.Add(pt);
                        _activeCanvas.Children.Add(poly);
                        break;
                    case TextEditAnnotation tea:
                        // White-out original text
                        var wo = new Rectangle
                        {
                            Fill = Brushes.White,
                            Width = tea.OriginalBounds.Width + 4,
                            Height = tea.OriginalBounds.Height + 4,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(wo, tea.OriginalBounds.X - 2);
                        Canvas.SetTop(wo, tea.OriginalBounds.Y - 2);
                        _activeCanvas.Children.Add(wo);
                        // Draw replacement text
                        var etb = new TextBlock
                        {
                            Text = tea.NewContent,
                            Foreground = Brushes.Black,
                            FontFamily = new FontFamily(tea.FontName),
                            FontSize = tea.FontSize,
                            FontWeight = ToWeight(tea.IsBold),
                            FontStyle = ToStyle(tea.IsItalic),
                            Padding = new Thickness(0)
                        };
                        Canvas.SetLeft(etb, tea.Position.X);
                        Canvas.SetTop(etb, tea.Position.Y);
                        _activeCanvas.Children.Add(etb);
                        break;

                    case SignatureAnnotation sa:
                        if (sa.ImageData is not null)
                        {
                            // Image-based signature
                            try
                            {
                                var imgBytes = Convert.FromBase64String(sa.ImageData);
                                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                                bmp.BeginInit();
                                bmp.StreamSource = new System.IO.MemoryStream(imgBytes);
                                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                var imgCtrl = new System.Windows.Controls.Image
                                {
                                    Source = bmp,
                                    Width = sa.SourceWidth * sa.Scale,
                                    Height = sa.SourceHeight * sa.Scale,
                                    Stretch = System.Windows.Media.Stretch.Uniform,
                                    IsHitTestVisible = false
                                };
                                Canvas.SetLeft(imgCtrl, sa.Position.X);
                                Canvas.SetTop(imgCtrl, sa.Position.Y);
                                _activeCanvas.Children.Add(imgCtrl);
                            }
                            catch { /* skip broken image */ }
                        }
                        else
                        {
                            foreach (var stroke in sa.Strokes)
                            {
                                if (stroke.Count < 2) continue;
                                var sigPoly = new Polyline
                                {
                                    Stroke = Brushes.Black,
                                    StrokeThickness = 2 * sa.Scale,
                                    StrokeLineJoin = PenLineJoin.Round,
                                    StrokeStartLineCap = PenLineCap.Round,
                                    StrokeEndLineCap = PenLineCap.Round
                                };
                                foreach (var pt in stroke)
                                    sigPoly.Points.Add(new Point(
                                        sa.Position.X + pt.X * sa.Scale,
                                        sa.Position.Y + pt.Y * sa.Scale));
                                _activeCanvas.Children.Add(sigPoly);
                            }
                        }
                        break;

                    case ImageAnnotation ia:
                        try
                        {
                            var iaBytes = Convert.FromBase64String(ia.ImageData);
                            var iaBmp = new System.Windows.Media.Imaging.BitmapImage();
                            iaBmp.BeginInit();
                            iaBmp.StreamSource = new System.IO.MemoryStream(iaBytes);
                            iaBmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            iaBmp.EndInit();
                            var iaCtrl = new System.Windows.Controls.Image
                            {
                                Source = iaBmp,
                                Width = ia.SourceWidth * ia.Scale,
                                Height = ia.SourceHeight * ia.Scale,
                                Stretch = System.Windows.Media.Stretch.Uniform,
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(iaCtrl, ia.Position.X);
                            Canvas.SetTop(iaCtrl, ia.Position.Y);
                            _activeCanvas.Children.Add(iaCtrl);
                        }
                        catch { /* skip broken image */ }
                        break;
                }
            }

            // Re-add form field overlays — RenderAllAnnotations clears the canvas so they must be restored.
            if (_renderDims.TryGetValue(pageIndex, out var dims))
                RenderFormFields(pageIndex, dims.w, dims.h);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count == 0)
            {
                SetStatus("Nothing to undo");
                return;
            }

            var entry = _undoStack.Pop();

            if (entry.Kind == UndoKind.Annotation)
            {
                int pageIdx = entry.PageIdx;
                if (_annotations.ContainsKey(pageIdx) && _annotations[pageIdx].Count > 0)
                    _annotations[pageIdx].RemoveAt(_annotations[pageIdx].Count - 1);
                ClearSelection();
                RenderAllAnnotations(pageIdx);
                MarkDirty(entry.WasDirty);
                SetStatus("Undid last annotation");
            }
            else // Document snapshot
            {
                if (entry.DocBytes is null) return;
                int selectedIdx = PageList.SelectedIndex;
                var tempPath = App.MakeTempFile("undo");
                System.IO.File.WriteAllBytes(tempPath, entry.DocBytes);
                _doc?.Close();
                // PdfSharpCore can write a snapshot whose xref offset points at the xref table,
                // producing "Unexpected token 'xref'" on reopen. Repair via Import (preserves
                // rotations) then PDFium, mirroring the save/reload path, instead of crashing.
                try
                {
                    _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                }
                catch (Exception undoOpenEx) when (IsXRefException(undoOpenEx))
                {
                    var fixedPath = App.MakeTempFile("undofixed");
                    if (!TryImportRepairToPath(tempPath, fixedPath)
                        && !TryPdfiumSaveWithZeroRotations(tempPath, fixedPath))
                        throw;
                    tempPath = fixedPath;
                    _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                }
                _currentFile = tempPath;
                _annotations.Clear();
                _renderDims.Clear();
                ClearSelection();
                MarkDirty(entry.WasDirty);
                RefreshPageList();
                if (selectedIdx >= 0 && selectedIdx < PageList.Items.Count)
                    PageList.SelectedIndex = selectedIdx;
                else if (PageList.Items.Count > 0)
                    PageList.SelectedIndex = 0;
                // Re-render the current view so the main page(s) reflect the restored document.
                // RefreshPageList only updates the sidebar, and re-selecting the same page does not
                // fire SelectionChanged, so grid/two-page tiles would otherwise stay stale.
                int reIdx = PageList.SelectedIndex;
                if (_viewMode == ViewMode.Continuous)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                        (Action)(() => SetupContinuousView(reIdx)));
                else
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
                    {
                        RenderPage(_viewMode == ViewMode.Grid ? 0 : reIdx);
                        ReapplyGridOrFit();
                    }));
                SetStatus("Undid document change");
            }
        }

        private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
        {
            int pageIdx = PageList.SelectedIndex;
            if (pageIdx < 0) return;
            if (_annotations.ContainsKey(pageIdx) && _annotations[pageIdx].Count > 0)
            {
                _annotations[pageIdx].Clear();
                MarkDirty();
            }
            ClearSelection();
            _annotationCanvas.Children.Clear();
            SetStatus("Cleared annotations on this page");
        }

        // ============================================================
        // Dirty / unsaved-change tracking
        // ============================================================

        private void MarkDirty(bool dirty = true)
        {
            _isDirty = dirty;
            if (_saveAsBtnRef != null)
            {
                _saveAsBtnRef.Foreground = dirty
                    ? new SolidColorBrush(Color.FromRgb(0xff, 0xa5, 0x00)) // orange = unsaved
                    : (SolidColorBrush)FindResource("Accent");
            }
        }

        // ============================================================
        // Close file (Ctrl+W) — returns to drop-zone state
        // ============================================================

        private void CloseFile()
        {
            if (_doc is null) return;
            if (_isDirty)
            {
                var res = ScalpelDialog.Show(this,
                    Loc("Str_Dlg_UnsavedClose"),
                    "Scalpel", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }
            _doc.Close();
            _doc = null;
            _currentFile = null;
            _activeTextBox = null;   // cancel any in-progress typewriter edit before canvas clear
            _annotations.Clear();
            _undoStack.Clear();
            _renderDims.Clear();
            _formTextValues.Clear();
            _formCheckValues.Clear();
            _formRadioValues.Clear();
            _allSearchRects.Clear();
            _searchResultPages.Clear();
            _searchPageCursor = -1;
            _thumbCts?.Cancel();
            PageList.ItemsSource = null;
            if (FindName("PageImage") is System.Windows.Controls.Image img) img.Source = null;
            _annotationCanvas.Children.Clear();
            FileNameLabel.Text = "";
            DropZone.Visibility = Visibility.Visible;
            PagePreviewPanel.Visibility = Visibility.Collapsed;
            CloseSearchBar();
            HideDrawSettings();
            HideTextSettings();
            HideSignaturePopup();
            SetTool(EditTool.Select);
            if (_closeFileBtnRef != null) _closeFileBtnRef.IsEnabled = false;
            _pageJumpBox.IsEnabled = false;
            _continuousRenderCts?.Cancel();
            _continuousPanel.Children.Clear();
            _continuousTops.Clear();
            _pageJumpBox.Text = "";
            _pageTotalLabel.Text = "/ –";
            OutlineTree.Items.Clear();
            SidebarOutlinesTab.IsEnabled = false;
            if (_sidebarShowingOutlines) SwitchSidebarToPagesTab();
            MarkDirty(false);
            SetStatus("Ready");
        }

        private void CloseFile_Click(object sender, RoutedEventArgs e) => CloseFile();

        // ============================================================
        // File toolbar handlers
        // ============================================================

        // Generic "open the ContextMenu attached to this button" handler — used by the
        // Open split-button chevron (Open / New / Close File).
        private void FileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu is not null)
            { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; }
        }

        // Toolbar wrapper — rotates CW (90°) for the Pages mode panel button.
        private void RotatePagesToolbar_Click(object sender, RoutedEventArgs e) => RotatePages_Click(90);

        private void New_Click(object sender, RoutedEventArgs e) => NewDocument();

        private void NewDocument()
        {
            if (_isDirty)
            {
                var res = ScalpelDialog.Show(this,
                    "You have unsaved changes. Discard them and create a new document?",
                    "Scalpel", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }

            try
            {
                var newDoc = new PdfDocument();
                newDoc.AddPage(); // one blank A4 page

                var tempPath = App.MakeTempFile("new");
                newDoc.Save(tempPath);
                newDoc.Close();

                _doc?.Close();
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                FinishOpenFile("Untitled.pdf", tempPath);
                SetStatus("New blank document");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Could not create new document:\n{ex.Message}",
                    "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "PDF files|*.pdf", Title = "Open PDF" };
            if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
        }

        private void Merge_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var doc = _doc;
            var dlg = new OpenFileDialog { Filter = "PDF files|*.pdf", Title = "Select PDF to merge", Multiselect = true };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                foreach (var file in dlg.FileNames)
                {
                    int pageOffset = doc.PageCount;

                    // Open twice: Import mode for AddPage, ReadOnly for catalog access.
                    using var srcRead = PdfReader.Open(file, PdfDocumentOpenMode.ReadOnly);
                    var namedDestMap = BuildNamedDestMap(srcRead);

                    using var src = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                    for (int i = 0; i < src.PageCount; i++)
                        doc.AddPage(src.Pages[i]);

                    // Rewrite named-destination links in the newly added pages so they
                    // resolve correctly after the catalog is not imported.
                    if (namedDestMap.Count > 0)
                        RewriteNamedDestLinks(doc, pageOffset, namedDestMap);
                }
                SaveTempAndReload();
                SetStatus($"Merged {dlg.FileNames.Length} file(s) - {_doc?.PageCount} total pages");
                Scalpel.Services.Logger.Info("File", "merge.success", "PDFs merged", new { added = dlg.FileNames.Length });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "merge.fail", "Merge failed", ex);
                ScalpelDialog.Show(this, $"Merge failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Builds a map of named destination string → 0-based page index from a source document's
        /// /Dests dictionary and /Names /Dests name tree.
        /// </summary>
        private Dictionary<string, int> BuildNamedDestMap(PdfDocument src)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var catalog = src.Internals.Catalog;

                // Legacy flat /Dests dictionary
                var destsDict = catalog.Elements.GetDictionary("/Dests");
                if (destsDict != null)
                {
                    foreach (var key in destsDict.Elements.Keys)
                    {
                        PdfItem? val = DerefItem(destsDict.Elements[key] ?? new PdfInteger(-1));
                        int? idx = ResolveDestPageIndexInDoc(src, val);
                        if (idx.HasValue) map[key.TrimStart('/')] = idx.Value;
                    }
                }

                // Modern /Names /Dests name tree
                var namesDict = catalog.Elements.GetDictionary("/Names");
                var destTree  = namesDict?.Elements.GetDictionary("/Dests");
                if (destTree != null)
                    WalkNameTree(src, destTree, map);
            }
            catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "BuildNamedDestMap", "BuildNamedDestMap failed", ex); }
            return map;
        }

        private void WalkNameTree(PdfDocument src, PdfDictionary node, Dictionary<string, int> map)
        {
            var namesArr = node.Elements.GetArray("/Names");
            if (namesArr != null)
            {
                for (int i = 0; i + 1 < namesArr.Elements.Count; i += 2)
                {
                    var keyItem = namesArr.Elements[i];
                    string key  = keyItem is PdfString ks ? ks.Value : keyItem?.ToString()?.TrimStart('/') ?? "";
                    if (string.IsNullOrEmpty(key)) continue;
                    PdfItem? val = DerefItem(namesArr.Elements[i + 1]);
                    int? idx = ResolveDestPageIndexInDoc(src, val);
                    if (idx.HasValue) map[key] = idx.Value;
                }
            }

            var kids = node.Elements.GetArray("/Kids");
            if (kids != null)
            {
                for (int i = 0; i < kids.Elements.Count; i++)
                {
                    if (DerefItem(kids.Elements[i]) is PdfDictionary kid)
                        WalkNameTree(src, kid, map);
                }
            }
        }

        /// <summary>
        /// Resolves a destination value (PdfArray or PdfDictionary with /D) to a page index
        /// within the given source document by matching the page object number.
        /// </summary>
        private static int? ResolveDestPageIndexInDoc(PdfDocument src, PdfItem? val)
        {
            PdfArray? arr = val as PdfArray;
            if (arr is null && val is PdfDictionary vd)
                arr = vd.Elements.GetArray("/D");
            if (arr is null || arr.Elements.Count == 0) return null;

            var first = arr.Elements[0];
            int objNum = GetObjectNumber(first);
            if (objNum > 0)
            {
                for (int i = 0; i < src.PageCount; i++)
                {
                    var pgRef = src.Pages[i].Reference;
                    if (pgRef != null && pgRef.ObjectNumber == objNum) return i;
                }
            }
            else if (first is PdfInteger pi && pi.Value >= 0 && pi.Value < src.PageCount)
            {
                return pi.Value;
            }
            return null;
        }

        /// <summary>
        /// Walks all link annotations in pages [pageOffset, doc.PageCount) and rewrites any
        /// named-destination /D values to explicit [pageRef /Fit] arrays using the merged
        /// document's page references. This is needed because PdfSharpCore's import does not
        /// copy the source document's /Names /Dests catalog entries.
        /// </summary>
        private static void RewriteNamedDestLinks(PdfDocument doc, int pageOffset,
            Dictionary<string, int> namedDestMap)
        {
            for (int pi = pageOffset; pi < doc.PageCount; pi++)
            {
                try
                {
                    var page    = doc.Pages[pi];
                    var annotsArr = page.Elements.GetArray("/Annots");
                    if (annotsArr is null) continue;

                    for (int ai = 0; ai < annotsArr.Elements.Count; ai++)
                    {
                        PdfItem? elem = annotsArr.Elements[ai];
                        PdfDictionary? ann = elem as PdfDictionary
                            ?? (DerefItemStatic(elem) as PdfDictionary);
                        if (ann is null) continue;

                        var subtype = ann.Elements["/Subtype"]?.ToString() ?? "";
                        if (!subtype.Contains("Link")) continue;

                        // Check /A /D (GoTo action)
                        var actionDict = ann.Elements.GetDictionary("/A");
                        if (actionDict != null)
                        {
                            var s = actionDict.Elements["/S"]?.ToString() ?? "";
                            if (s.Contains("GoTo"))
                            {
                                var destItem = actionDict.Elements["/D"];
                                string? name = ExtractDestName(destItem);
                                if (name != null && namedDestMap.TryGetValue(name, out int srcIdx))
                                {
                                    int targetIdx = pageOffset + srcIdx;
                                    if (targetIdx < doc.PageCount)
                                        actionDict.Elements["/D"] = MakeExplicitDest(doc, targetIdx);
                                }
                            }
                        }
                        else
                        {
                            // Bare /Dest on annotation
                            var destItem = ann.Elements["/Dest"];
                            string? name = ExtractDestName(destItem);
                            if (name != null && namedDestMap.TryGetValue(name, out int srcIdx))
                            {
                                int targetIdx = pageOffset + srcIdx;
                                if (targetIdx < doc.PageCount)
                                    ann.Elements["/Dest"] = MakeExplicitDest(doc, targetIdx);
                            }
                        }
                    }
                }
                catch (Exception ex) { Scalpel.Services.Logger.Error("Error", "RewriteNamedDestLinks", "RewriteNamedDestLinks failed", ex); }
            }
        }

        private static string? ExtractDestName(PdfItem? item)
        {
            if (item is null) return null;
            if (item is PdfString ps) return ps.Value;
            if (item is PdfName   pn) return pn.Value.TrimStart('/');
            return null;
        }

        private static PdfArray MakeExplicitDest(PdfDocument doc, int pageIndex)
        {
            var arr = new PdfArray(doc);
            arr.Elements.Add(doc.Pages[pageIndex].Reference);
            arr.Elements.Add(new PdfName("/Fit"));
            return arr;
        }

        // Static version of DerefItem for use in static helpers.
        private static PdfItem DerefItemStatic(PdfItem item)
        {
            var valueProp = item.GetType().GetProperty("Value",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (valueProp?.GetValue(item) is PdfObject resolved) return resolved;
            return item;
        }

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var currentFile = _currentFile;
            var selected = PageList.SelectedItems;
            if (selected.Count == 0) { ScalpelDialog.Show(this, "Select pages to extract."); return; }
            var dlg = new SaveFileDialog { Filter = "PDF files|*.pdf", Title = "Save extracted pages as",
                                           CheckFileExists = false, CheckPathExists = true };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var indices = new List<int>();
                foreach (PageThumbnailVm vm in selected) indices.Add(vm.PageIndex);
                using var importDoc = PdfReader.Open(currentFile, PdfDocumentOpenMode.Import);
                var newDoc = new PdfDocument();
                foreach (var idx in indices.OrderBy(i => i))
                    newDoc.AddPage(importDoc.Pages[idx]);
                newDoc.Save(dlg.FileName);
                SetStatus(string.Format(Loc("Str_Extracted"), indices.Count, System.IO.Path.GetFileName(dlg.FileName)));
                Scalpel.Services.Logger.Info("File", "extract.success", "Pages extracted", new { count = indices.Count });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "extract.fail", "Extract failed", ex);
                ScalpelDialog.Show(this, $"Split failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var doc = _doc;
            var selected = PageList.SelectedItems;
            if (selected.Count == 0) { ScalpelDialog.Show(this, "Select pages to delete."); return; }
            var result = ScalpelDialog.Show(this, $"Delete {selected.Count} {(selected.Count == 1 ? "page" : "pages")}?", "Scalpel",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                var indices = new List<int>();
                foreach (PageThumbnailVm vm in selected) indices.Add(vm.PageIndex);
                foreach (var idx in indices.OrderByDescending(i => i))
                    doc.Pages.RemoveAt(idx);
                SaveTempAndReload();
                SetStatus(string.Format(Loc("Str_Deleted"), indices.Count, _doc?.PageCount));
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Delete failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertBlankPage_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            var doc = _doc;
            int insertAfter = PageList.SelectedIndex >= 0 ? PageList.SelectedIndex : doc.PageCount - 1;
            try
            {
                var blank = new PdfPage { Width = XUnit.FromPoint(595), Height = XUnit.FromPoint(842) };
                doc.Pages.Insert(insertAfter + 1, blank);
                SaveTempAndReload();
                PageList.SelectedIndex = insertAfter + 1;
                SetStatus($"Inserted blank page at position {insertAfter + 2}");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Insert failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || PageList.SelectedIndex <= 0) return;
            var doc = _doc;
            int idx = PageList.SelectedIndex;
            var page = doc.Pages[idx];
            doc.Pages.RemoveAt(idx);
            doc.Pages.Insert(idx - 1, page);
            SaveTempAndReload();
            PageList.SelectedIndex = idx - 1;
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || PageList.SelectedIndex < 0 || PageList.SelectedIndex >= _doc.PageCount - 1) return;
            var doc = _doc;
            int idx = PageList.SelectedIndex;
            var page = doc.Pages[idx];
            doc.Pages.RemoveAt(idx);
            doc.Pages.Insert(idx + 1, page);
            SaveTempAndReload();
            PageList.SelectedIndex = idx + 1;
        }

        private void SaveInPlace()
        {
            if (_doc is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            // Save back to the user's real file. After a page edit (crop/rotate) _currentFile is a
            // temp working copy, so the real path is kept in _originalFile. If there is no real path
            // (e.g. a repaired temp-backed open), fall back to Save As.
            if (string.IsNullOrEmpty(_originalFile)) { SaveAs_Click(this, new RoutedEventArgs()); return; }
            CommitActiveTextBox();
            string saveTarget = _originalFile!;
            try
            {
                bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
                WriteFormValuesToDocument();
                // Always strip link annotation borders regardless of user annotation count
                // so mailto/URI links don't appear as strikethrough lines in other viewers.
                StripLinkAnnotationBorders(_doc);

                if (hasAnnotations)
                {
                    // Save a clean copy of the doc (without burned annotations), burn
                    // annotations into the real file, then restore the in-memory doc
                    // from the clean copy so future saves don't double-burn.
                    var tempClean = App.MakeTempFile("clean");
                    _doc.Save(tempClean);
                    DrawAnnotationsOnDocument();
                    _doc.Save(saveTarget);
                    _doc.Close();
                    _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                    _currentFile = tempClean;
                }
                else
                {
                    _doc.Save(saveTarget);
                }

                MarkDirty(false);
                SetStatus($"Saved - {System.IO.Path.GetFileName(saveTarget)}");
            }
            catch (Exception ex)
            {
                ScalpelDialog.Show(this, $"Save failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Toolbar Save (the main half of the split button): saves straight back to the
        // original file. "Save As" lives in the chevron dropdown beside it. SaveInPlace
        // handles the empty-doc and no-real-path (repaired temp-backed open -> Save As)
        // cases, and this matches the Ctrl+S shortcut behaviour.
        private void Save_Click(object sender, RoutedEventArgs e) => SaveInPlace();

        // Chevron beside Save: drops the Save As menu (same pattern as FileMenu_Click).
        private void SaveMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu is not null)
            { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            CommitActiveTextBox();
            var dlg = new SaveFileDialog { Filter = "PDF files|*.pdf", Title = "Save PDF as",
                                           CheckFileExists = false, CheckPathExists = true };
            string? seed = _originalFile ?? _currentFile;
            if (!string.IsNullOrEmpty(seed))
            {
                dlg.FileName = System.IO.Path.GetFileName(seed);
                var seedDir = System.IO.Path.GetDirectoryName(_originalFile ?? "");
                if (!string.IsNullOrEmpty(seedDir) && System.IO.Directory.Exists(seedDir))
                    dlg.InitialDirectory = seedDir;
            }
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
                WriteFormValuesToDocument();
                // Always strip link annotation borders regardless of user annotation count.
                StripLinkAnnotationBorders(_doc);

                if (hasAnnotations)
                {
                    var tempClean = App.MakeTempFile("clean");
                    _doc.Save(tempClean);
                    DrawAnnotationsOnDocument();
                    _doc.Save(dlg.FileName);
                    _doc.Close();
                    _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                    _currentFile = tempClean;
                    _originalFile = dlg.FileName;
                    FileNameLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
                    MarkDirty(false);
                    SetStatus($"Saved with annotations to {System.IO.Path.GetFileName(dlg.FileName)}");
                }
                else
                {
                    _doc.Save(dlg.FileName);
                    _originalFile = dlg.FileName;
                    FileNameLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
                    MarkDirty(false);
                    SetStatus($"Saved to {System.IO.Path.GetFileName(dlg.FileName)}");
                }
                Scalpel.Services.Logger.Info("File", "save.success", "PDF saved", new { path = dlg.FileName });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "save.fail", "Save failed", ex);
                ScalpelDialog.Show(this, $"Save failed:\n{ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveFlattened_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            CommitActiveTextBox();
            var dlg = new SaveFileDialog { Filter = "PDF files|*.pdf", Title = "Save Flattened PDF",
                                           CheckFileExists = false, CheckPathExists = true };
            if (dlg.ShowDialog(this) != true) return;

            // Burn any pending annotations into a temp source for rasterization
            // (must happen on UI thread before we go async)
            string sourcePath;
            bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
            if (hasAnnotations)
            {
                var tempClean  = App.MakeTempFile("clean");
                var tempBurned = App.MakeTempFile("burned");
                _doc.Save(tempClean);
                DrawAnnotationsOnDocument();
                _doc.Save(tempBurned);
                _doc.Close();
                _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                _currentFile = tempClean;
                sourcePath = tempBurned;
            }
            else
            {
                var temp = App.MakeTempFile("src");
                _doc.Save(temp);
                sourcePath = temp;
            }

            int pageCount = _doc.PageCount;

            // Snapshot per-page dimensions (CropBox-aware) before going off-thread
            var pageDims = new (double widthPt, double heightPt)[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                var p = _doc.Pages[i];
                pageDims[i] = (p.Width.Point, p.Height.Point);
            }

            // Show a progress overlay so the user knows we're working
            var overlay = ShowFlattenProgress(pageCount);
            string outputPath = dlg.FileName;

            try
            {
                // Rasterize on a background thread — keeps the UI responsive
                await Task.Run(() =>
                {
                    // Rasterize pages across CPU cores. Docnet/PDFium is not thread-safe, so the
                    // pdfium render is serialized behind a lock; the PNG encode (GDI+) runs in
                    // parallel. Pages are assembled into the PDF afterwards, in order.
                    var pngPages = new byte[pageCount][];
                    var docGate  = new object();
                    int done     = 0;
                    var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) };
                    Parallel.For(0, pageCount, po, i =>
                    {
                        // Per-page pixel dimensions at 150 DPI, sized to the CropBox
                        int pw = Math.Max(1, (int)(pageDims[i].widthPt  * 150 / 72.0));
                        int ph = Math.Max(1, (int)(pageDims[i].heightPt * 150 / 72.0));
                        // PageDimensions requires dimOne <= dimTwo (short-edge, long-edge)
                        int dimMin = Math.Min(pw, ph);
                        int dimMax = Math.Max(pw, ph);

                        byte[] bgra; int rw, rh;
                        lock (docGate)
                        {
                            using var pageDocReader = DocLib.Instance.GetDocReader(sourcePath, new PageDimensions(dimMin, dimMax));
                            using var pr = pageDocReader.GetPageReader(i);
                            bgra = pr.GetImage();
                            rw   = pr.GetPageWidth();
                            rh   = pr.GetPageHeight();
                        }
                        // Encode BGRA to PNG (GDI+) outside the lock so it parallelizes.
                        pngPages[i] = RenderToPng(bgra, rw, rh);

                        int n = System.Threading.Interlocked.Increment(ref done);
                        Dispatcher.BeginInvoke(new Action(() => UpdateFlattenProgress(overlay, n, pageCount)));
                    });

                    // Assemble the output PDF in page order (PdfSharp is single-threaded).
                    var outDoc = new PdfDocument();
                    try
                    {
                        for (int i = 0; i < pageCount; i++)
                        {
                            var newPage = outDoc.AddPage();
                            newPage.Width  = XUnit.FromPoint(pageDims[i].widthPt);
                            newPage.Height = XUnit.FromPoint(pageDims[i].heightPt);
                            using var xi  = XImage.FromStream(() => new MemoryStream(pngPages[i]));
                            using var gfx = XGraphics.FromPdfPage(newPage);
                            gfx.DrawImage(xi, 0, 0, newPage.Width.Point, newPage.Height.Point);
                        }
                        outDoc.Save(outputPath);
                    }
                    finally
                    {
                        outDoc.Dispose();
                    }
                });

                MarkDirty(false);
                SetStatus($"Flattened PDF saved to {System.IO.Path.GetFileName(outputPath)}");
                Scalpel.Services.Logger.Info("File", "flatten.success", "PDF flattened", new { path = outputPath, pages = pageCount });
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("File", "flatten.fail", "Flatten failed", ex);
                try { ScalpelDialog.Show(this, $"Flatten failed:\n{ex.GetType().Name}: {ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error); }
                catch { /* dialog failed; overlay still removed in finally */ }
            }
            finally
            {
                try { HideFlattenProgress(overlay); } catch { /* ensure overlay never leaks */ }
            }
        }

        // ---- flatten progress overlay helpers ----

        private Border ShowFlattenProgress(int pageCount, string verb = "Flattening")
        {
            var progressText = new TextBlock
            {
                Text       = $"{verb} page 0 of {pageCount}...",
                Foreground = Brushes.White,
                FontSize   = 14,
                Tag        = verb   // stored so UpdateFlattenProgress can read it
            };
            var panel = new StackPanel
            {
                Orientation         = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            panel.Children.Add(progressText);

            var overlay = new Border
            {
                Background        = new SolidColorBrush(Color.FromArgb(200, 0x1a, 0x1a, 0x1a)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment   = VerticalAlignment.Stretch,
                Child             = panel,
                Tag               = "FlattenOverlay"
            };
            Panel.SetZIndex(overlay, 999);

            // Attach to the root grid
            if (Content is Grid rootGrid)
                rootGrid.Children.Add(overlay);

            return overlay;
        }

        private static void UpdateFlattenProgress(Border overlay, int current, int total)
        {
            if (overlay.Child is StackPanel panel)
                foreach (var child in panel.Children)
                    if (child is TextBlock tb && tb.Tag is string verb)
                        tb.Text = $"{verb} page {current} of {total}...";
        }

        private void HideFlattenProgress(Border overlay)
        {
            if (Content is Grid rootGrid)
                rootGrid.Children.Remove(overlay);
        }

        /// <summary>
        /// Encodes raw BGRA pixel data from pdfium to PNG without touching the UI thread.
        /// GDI+ Format32bppArgb is BGRA in memory — matches pdfium output exactly.
        /// </summary>
        private static byte[] RenderToPng(byte[] bgra, int width, int height)
        {
            var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                using var bmp = new System.Drawing.Bitmap(
                    width, height, width * 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                    pin.AddrOfPinnedObject());
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
            finally { pin.Free(); }
        }

        private async void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null || _currentFile is null) { ScalpelDialog.Show(this, "Open a PDF first."); return; }
            CommitActiveTextBox();

            // Burn pending annotations into a temp copy on the UI thread before going off-thread
            bool hasAnnotations = _annotations.Values.Any(list => list.Count > 0);
            string printPath;
            string? tempFlattened = null;
            if (hasAnnotations)
            {
                var tempClean = App.MakeTempFile("clean");
                _doc.Save(tempClean);
                DrawAnnotationsOnDocument();
                printPath = App.MakeTempFile("print");
                _doc.Save(printPath);
                tempFlattened = printPath;
                _doc.Close();
                _doc = PdfReader.Open(tempClean, PdfDocumentOpenMode.Modify);
                _currentFile = tempClean;
            }
            else
            {
                printPath = _currentFile;
            }

            int pageCount = _doc.PageCount;

            // Rasterize every page, then hand them to our own preview window. WPF's OS
            // PrintDialog cannot show a preview ("This app doesn't support print preview"),
            // so Scalpel renders the preview and drives printing itself.
            var overlay = ShowFlattenProgress(pageCount, "Preparing");
            bool overlayHidden = false;
            byte[][]? pngPages = null;
            int[]?    rasterW  = null;
            int[]?    rasterH  = null;

            try
            {
                // Rasterize all pages on background thread — keeps UI responsive
                await Task.Run(() =>
                {
                    pngPages = new byte[pageCount][];
                    rasterW  = new int[pageCount];
                    rasterH  = new int[pageCount];
                    using var docReader = DocLib.Instance.GetDocReader(printPath, new PageDimensions(1536, 1536));
                    for (int i = 0; i < pageCount; i++)
                    {
                        using var pr = docReader.GetPageReader(i);
                        int w = pr.GetPageWidth();
                        int h = pr.GetPageHeight();
                        pngPages[i] = RenderToPng(pr.GetImage(), w, h);
                        rasterW[i]  = w;
                        rasterH[i]  = h;
                        int captured = i;
                        Dispatcher.Invoke(() => UpdateFlattenProgress(overlay, captured + 1, pageCount));
                    }
                });

                // Decode to frozen BitmapSources on the UI thread for the preview window.
                var sources = new BitmapSource[pageCount];
                for (int i = 0; i < pageCount; i++)
                {
                    using var ms = new MemoryStream(pngPages![i]);
                    var src = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    src.Freeze();
                    sources[i] = src;
                }

                HideFlattenProgress(overlay);
                overlayHidden = true;

                var preview = new PrintPreviewWindow(this, sources, rasterW!, rasterH!);
                if (preview.ShowDialog() == true)
                {
                    SetStatus(string.Format(Loc("Str_Printed"), preview.PrintedPageCount));
                    Scalpel.Services.Logger.Info("Print", "print.success", "Document printed", new { pages = preview.PrintedPageCount });
                }
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Print", "print.fail", "Print failed", ex);
                try { ScalpelDialog.Show(this, $"Print failed:\n{ex.GetType().Name}: {ex.Message}", "Scalpel", MessageBoxButton.OK, MessageBoxImage.Error); }
                catch { }
            }
            finally
            {
                if (!overlayHidden) try { HideFlattenProgress(overlay); } catch { }
                if (tempFlattened != null) try { System.IO.File.Delete(tempFlattened); } catch { }
            }
        }

        // ============================================================
        // Save annotations to PDF
        // ============================================================

        /// <summary>True if <paramref name="family"/> (exact face) maps <paramref name="codepoint"/>.</summary>
        private static bool FontCovers(string family, bool bold, bool italic, int codepoint)
        {
            if (Scalpel.Services.PdfFontResolver.Instance.TryGetExactFontBytes(family, bold, italic, out var bytes))
                return Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, codepoint);
            return false;
        }

        /// <summary>Pick a bundled face covering the script of <paramref name="text"/>:
        /// Arabic → Noto Sans Arabic, Hebrew → Noto Sans Hebrew, Cyrillic → Noto Sans,
        /// otherwise the candidate (Latin). Falls back to the candidate if a bundled face
        /// isn't registered, so a missing font never blanks the text.</summary>
        private static string PickFace(string text, string candidate, bool bold, bool italic)
        {
            foreach (char c in text)
            {
                // Arabic base (0600-06FF) or presentation forms A/B (FB50-FDFF, FE70-FEFF)
                if ((c >= '؀' && c <= 'ۿ') || (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿'))
                    return FontCovers(candidate, bold, italic, 0x0628) ? candidate : "Noto Sans Arabic";
                // Hebrew (0590-05FF) or Hebrew presentation forms (FB1D-FB4F)
                if ((c >= '֐' && c <= '׿') || (c >= 'יִ' && c <= 'ﭏ'))
                    return FontCovers(candidate, bold, italic, 0x05D0) ? candidate : "Noto Sans Hebrew";
                // Cyrillic (0400-04FF)
                if (c >= 'Ѐ' && c <= 'ӿ')
                    return FontCovers(candidate, bold, italic, 0x0410) ? candidate : "Noto Sans";
            }
            return candidate;
        }

        /// <summary>Draw one line of text, handling RTL: reorder to visual order, pick a
        /// Hebrew-capable font (the candidate if it covers Hebrew, else bundled Noto), and
        /// right-align to <paramref name="rightX"/> when it exceeds <paramref name="leftX"/>
        /// (edits with known bounds); otherwise left-align at leftX. LTR text is unchanged.</summary>
        private static void DrawTextRun(XGraphics gfx, string text, string candidateFamily,
            double fontSizePx, XFontStyle style, XBrush brush,
            double leftX, double rightX, double baselineY, bool forceCandidate = false)
        {
            bool bold = style == XFontStyle.Bold || style == XFontStyle.BoldItalic;
            bool italic = style == XFontStyle.Italic || style == XFontStyle.BoldItalic;

            if (!Scalpel.Services.BidiReorder.ContainsRtl(text))
            {
                // LTR (incl. Cyrillic): pick a covering face so Russian doesn't render as boxes.
                // forceCandidate (an extracted embedded font already verified to cover the text)
                // bypasses the script-substitution heuristic so the exact font is used.
                string ltrFace = forceCandidate ? candidateFamily : PickFace(text, candidateFamily, bold, italic);
                gfx.DrawString(text, new XFont(ltrFace, fontSizePx, style), brush, leftX, baselineY);
                return;
            }

            // RTL: shape Arabic (cursive joining) BEFORE reordering, then reverse to visual order.
            string shaped = Scalpel.Services.ArabicShaper.ContainsArabic(text)
                ? Scalpel.Services.ArabicShaper.Shape(text)
                : text;
            string family = forceCandidate ? candidateFamily : PickFace(shaped, candidateFamily, bold, italic);
            var font = new XFont(family, fontSizePx, style);
            string visual = Scalpel.Services.BidiReorder.ToVisual(shaped);
            double width = gfx.MeasureString(visual, font).Width;
            double x = rightX > leftX ? rightX - width : leftX;
            gfx.DrawString(visual, font, brush, x, baselineY);
        }

        private void DrawAnnotationsOnDocument()
        {
            if (_doc is null) return;

            // Strip link annotation borders so they don't render as colored rectangles
            // (e.g. strikethrough-like lines) in other PDF viewers.
            StripLinkAnnotationBorders(_doc);

            foreach (var kvp in _annotations)
            {
                int pageIdx = kvp.Key;
                var annots = kvp.Value;
                if (annots.Count == 0 || pageIdx >= _doc.PageCount) continue;
                if (!_renderDims.ContainsKey(pageIdx)) continue;

                var page = _doc.Pages[pageIdx];
                var (renderW, renderH) = _renderDims[pageIdx];
                double sx = page.Width.Point / renderW;
                double sy = page.Height.Point / renderH;

                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                foreach (var annot in annots)
                {
                    switch (annot)
                    {
                        case TextAnnotation ta:
                            var lines = ta.Content.Split('\n');
                            double lineH = ta.FontSize * sy * 1.2;
                            double ty = ta.Position.Y * sy + ta.FontSize * sy;
                            var taColor = ta.GetColor();
                            var taBrush = new XSolidBrush(XColor.FromArgb(taColor.A, taColor.R, taColor.G, taColor.B));
                            double taLeft = ta.Position.X * sx;
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrEmpty(line))
                                    DrawTextRun(gfx, line, "Geist", ta.FontSize * sy, XFontStyle.Regular,
                                        taBrush, taLeft, taLeft, ty); // rightX==leftX → left-anchored
                                ty += lineH;
                            }
                            break;

                        case HighlightAnnotation ha:
                            var hc = ha.GetColor();
                            var hBrush = new XSolidBrush(XColor.FromArgb(hc.A, hc.R, hc.G, hc.B));
                            gfx.DrawRectangle(hBrush,
                                ha.Bounds.X * sx, ha.Bounds.Y * sy,
                                ha.Bounds.Width * sx, ha.Bounds.Height * sy);
                            break;

                        case InkAnnotation ia:
                            if (ia.Points.Count < 2) break;
                            var ic = ia.GetColor();
                            var pen = new XPen(XColor.FromArgb(ic.A, ic.R, ic.G, ic.B), ia.StrokeWidth * sx)
                            {
                                LineJoin = XLineJoin.Round,
                                LineCap = XLineCap.Round
                            };
                            for (int i = 0; i < ia.Points.Count - 1; i++)
                            {
                                gfx.DrawLine(pen,
                                    ia.Points[i].X * sx, ia.Points[i].Y * sy,
                                    ia.Points[i + 1].X * sx, ia.Points[i + 1].Y * sy);
                            }
                            break;

                        case TextEditAnnotation tea:
                            // White-out original text area
                            var whiteRect = new XSolidBrush(XColors.White);
                            gfx.DrawRectangle(whiteRect,
                                (tea.OriginalBounds.X - 2) * sx, (tea.OriginalBounds.Y - 2) * sy,
                                (tea.OriginalBounds.Width + 4) * sx, (tea.OriginalBounds.Height + 4) * sy);
                            var editStyle = tea.IsBold && tea.IsItalic ? XFontStyle.BoldItalic
                                          : tea.IsBold ? XFontStyle.Bold
                                          : tea.IsItalic ? XFontStyle.Italic
                                          : XFontStyle.Regular;
                            double etyB = tea.Position.Y * sy + tea.FontSize * sy;
                            double eLeft = tea.OriginalBounds.X * sx;
                            double eRight = (tea.OriginalBounds.X + tea.OriginalBounds.Width) * sx;
                            // Use the document's own embedded font when we have it (exact match),
                            // forcing it past the substitution heuristic; else the resolved family.
                            string editCandidate = tea.ExactFontFamily ?? tea.FontName;
                            DrawTextRun(gfx, tea.NewContent, editCandidate, tea.FontSize * sy, editStyle,
                                XBrushes.Black, eLeft, eRight, etyB, forceCandidate: tea.ExactFontFamily is not null);
                            break;

                        case SignatureAnnotation sa:
                            if (sa.ImageData is not null)
                            {
                                try
                                {
                                    var imgBytes = Convert.FromBase64String(sa.ImageData);
                                    var xImg = XImage.FromStream(() => new System.IO.MemoryStream(imgBytes));
                                    double imgX = sa.Position.X * sx;
                                    double imgY = sa.Position.Y * sy;
                                    double imgW = sa.SourceWidth * sa.Scale * sx;
                                    double imgH = sa.SourceHeight * sa.Scale * sy;
                                    gfx.DrawImage(xImg, imgX, imgY, imgW, imgH);
                                }
                                catch { /* skip broken image */ }
                            }
                            else
                            {
                                var sigPen = new XPen(XColors.Black, 2 * sa.Scale * sx)
                                {
                                    LineJoin = XLineJoin.Round,
                                    LineCap = XLineCap.Round
                                };
                                foreach (var stroke in sa.Strokes)
                                {
                                    for (int i = 0; i < stroke.Count - 1; i++)
                                    {
                                        double x1 = (sa.Position.X + stroke[i].X * sa.Scale) * sx;
                                        double y1 = (sa.Position.Y + stroke[i].Y * sa.Scale) * sy;
                                        double x2 = (sa.Position.X + stroke[i + 1].X * sa.Scale) * sx;
                                        double y2 = (sa.Position.Y + stroke[i + 1].Y * sa.Scale) * sy;
                                        gfx.DrawLine(sigPen, x1, y1, x2, y2);
                                    }
                                }
                            }
                            break;

                        case ImageAnnotation ia:
                            try
                            {
                                var iaBytes = Convert.FromBase64String(ia.ImageData);
                                var xia = XImage.FromStream(() => new System.IO.MemoryStream(iaBytes));
                                double iaX = ia.Position.X * sx;
                                double iaY = ia.Position.Y * sy;
                                double iaW = ia.SourceWidth * ia.Scale * sx;
                                double iaH = ia.SourceHeight * ia.Scale * sy;
                                gfx.DrawImage(xia, iaX, iaY, iaW, iaH);
                            }
                            catch { /* skip broken image */ }
                            break;
                    }
                }
            }
        }

        // ============================================================
        // Bitmap rotation helper
        // ============================================================

        /// <summary>
        /// Rotates a raw BGRA (4 bytes/pixel) bitmap clockwise by degrees.
        /// Used because Docnet's FPDF_RenderPageBitmapWithMatrix uses a pure-scaling
        /// matrix, so PDFium renders the page in its MediaBox orientation (no rotation).
        /// We strip /Rotate from the temp file so content is never clipped, then rotate
        /// the pixel buffer here to match the intended visual orientation.
        /// </summary>
        internal static (byte[] bytes, int w, int h) RotateBitmapStatic(byte[] src, int w, int h, int degrees)
            => RotateBitmap(src, w, h, degrees);

        private static (byte[] bytes, int w, int h) RotateBitmap(byte[] src, int w, int h, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0) return (src, w, h);
            int newW = (degrees == 90 || degrees == 270) ? h : w;
            int newH = (degrees == 90 || degrees == 270) ? w : h;
            byte[] dst = new byte[newW * newH * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcIdx = (y * w + x) * 4;
                    int dstX, dstY;
                    switch (degrees)
                    {
                        case 90:  dstX = h - 1 - y; dstY = x;         break; // CW
                        case 180: dstX = w - 1 - x; dstY = h - 1 - y; break;
                        default:  dstX = y;          dstY = w - 1 - x; break; // 270 CW
                    }
                    int dstIdx = (dstY * newW + dstX) * 4;
                    dst[dstIdx]     = src[srcIdx];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
            return (dst, newW, newH);
        }

        // ============================================================
        // Temp save/reload
        // ============================================================

        private void SaveTempAndReload(bool keepAnnotations = false)
        {
            if (_doc is null || _currentFile is null) return;
            // Overlay annotations are unsaved, still-editable user work. Callers that don't change
            // page identity (crop) pass keepAnnotations:true so annotations on other pages survive
            // the reload and stay selectable/movable; they are re-rendered after the doc reopens.
            if (!keepAnnotations) _annotations.Clear();
            _renderDims.Clear();
            ClearSelection();
            MarkDirty();
            var doc = _doc;
            int selectedIdx = PageList.SelectedIndex;

            // Capture page rotations, then strip them from the document before saving.
            // Docnet uses FPDF_GetPageWidth/Height (MediaBox, no rotation) to size the bitmap,
            // then renders with PDFium's page CTM which *does* include /Rotate.  For 90°/270°
            // the rendered landscape content overflows the portrait-sized bitmap and gets clipped.
            // Stripping /Rotate to 0 before saving means Docnet renders clean unrotated content
            // that fits the bitmap; RotateBitmap is applied in each render path instead.
            _pageRotations.Clear();
            for (int i = 0; i < doc.PageCount; i++)
            {
                int rot = ((doc.Pages[i].Rotate % 360) + 360) % 360;
                _pageRotations[i] = rot;
                doc.Pages[i].Rotate = 0;
            }

            var tempPath = App.MakeTempFile("temp");
            try
            {
                doc.Save(tempPath);
                doc.Close();
            }
            catch (Exception saveEx) when (IsXRefException(saveEx))
            {
                // PdfSharpCore fails to re-save encrypted PDFs (e.g. owner-restricted RC4 files)
                // because it encounters cross-reference tokens while serialising dirty objects.
                // Primary fallback: use PDFium (already initialised for the page preview) to
                // load the source, strip all /Rotate values, remove encryption, and save.
                // Secondary fallback: PdfSharpCore Import mode (works on some non-encrypted xref
                // issues but fails on encrypted files; kept as a last resort).
                doc.Close();
                _doc = null;
                if (!TryPdfiumSaveWithZeroRotations(_currentFile!, tempPath) &&
                    !TryImportRepairToPath(_currentFile!, tempPath, stripRotations: true))
                    throw; // re-throw original if both fallbacks fail
            }
            // PdfSharpCore sometimes saves a file where one object's xref offset points at the
            // xref table itself (object N offset = xref table position). When PdfSharp then tries
            // to re-open that file in Modify mode it seeks to the xref table, reads the keyword
            // "xref" as a token in an object context, and throws "Unexpected token 'xref'".
            // Fix: catch the reopen failure, pipe the saved file through PDFium (which has
            // robust error recovery and will rewrite a correct xref), then retry the open.
            try
            {
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
            }
            catch (Exception openEx) when (IsXRefException(openEx))
            {
                var fixedPath = App.MakeTempFile("fixed");
                if (!TryPdfiumSaveWithZeroRotations(tempPath, fixedPath))
                    throw; // PDFium also failed — re-throw original reopen error
                tempPath = fixedPath;
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
            }
            _currentFile = tempPath;

            // Restore rotations in the reopened in-memory doc so saves, form fields,
            // and all other operations see the correct rotation values.
            foreach (var kv in _pageRotations)
                _doc.Pages[kv.Key].Rotate = kv.Value;

            RefreshPageList();
            if (selectedIdx >= 0 && selectedIdx < PageList.Items.Count)
                PageList.SelectedIndex = selectedIdx;
            else if (PageList.Items.Count > 0)
                PageList.SelectedIndex = 0;

            // In Continuous view the strip caches one rendered slot per page. After a
            // page-modifying reload (e.g. crop) it must be rebuilt so the main view reflects the
            // new pages; the slot-sizing in RenderContinuousPages makes cropped pages fit cleanly.
            if (_viewMode == ViewMode.Continuous)
            {
                int contIdx = PageList.SelectedIndex;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    (Action)(() => SetupContinuousView(contIdx)));
                return;
            }

            // Refit synchronously so the first rendered frame uses the correct zoom.
            PagePreviewPanel.ScrollToHorizontalOffset(0);
            ReapplyGridOrFit();

            // Deferred refit after layout settles for accurate ActualWidth.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
            {
                PagePreviewPanel.ScrollToHorizontalOffset(0);
                ReapplyGridOrFit();
            }));
        }

        // ============================================================
        // Zoom
        // ============================================================

        private void PagePreview_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (_viewMode == ViewMode.Grid) { GridZoomStep(e.Delta < 0); return; }

                // Capture cursor position and scroll offsets BEFORE zoom changes so we can
                // compute the new offsets that keep the point under the cursor stationary.
                Point cursorInViewport = e.GetPosition(PagePreviewPanel);
                double oldZoom = _zoomLevel;
                double oldHOff = PagePreviewPanel.HorizontalOffset;
                double oldVOff = PagePreviewPanel.VerticalOffset;

                SetZoom(e.Delta > 0 ? _zoomLevel + ZoomStep : _zoomLevel - ZoomStep);

                // After layout settles, reposition the scroll so the cursor point stays fixed.
                // Formula: newOffset = (oldOffset + cursorPos) * (newZoom / oldZoom) - cursorPos
                double ratio   = _zoomLevel / oldZoom;
                double newHOff = (oldHOff + cursorInViewport.X) * ratio - cursorInViewport.X;
                double newVOff = (oldVOff + cursorInViewport.Y) * ratio - cursorInViewport.Y;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
                {
                    PagePreviewPanel.ScrollToHorizontalOffset(Math.Max(0, newHOff));
                    PagePreviewPanel.ScrollToVerticalOffset(Math.Max(0, newVOff));
                }));
                return;
            }

            // Regular scroll: let the ScrollViewer handle it normally.
            // At scroll boundaries, fall through to page navigation so the user
            // can reach adjacent pages without touching the sidebar.
            if (PagePreviewPanel.ScrollableHeight <= 0)
            {
                // No scrollable content — navigate pages directly.
                e.Handled = true;
                NavigatePageByWheel(e.Delta);
                return;
            }

            bool atTop    = PagePreviewPanel.VerticalOffset <= 0;
            bool atBottom = PagePreviewPanel.VerticalOffset >= PagePreviewPanel.ScrollableHeight - 1;
            // In Continuous view the whole document is one scroll; don't hop pages at the
            // boundary - just let it stop at the top/bottom.
            if (_viewMode != ViewMode.Continuous && ((atTop && e.Delta > 0) || (atBottom && e.Delta < 0)))
            {
                e.Handled = true;
                NavigatePageByWheel(e.Delta);
            }
            // Otherwise let the ScrollViewer scroll naturally.
        }

        private void NavigatePageByWheel(int delta)
        {
            if (_doc is null) return;
            int cur = PageList.SelectedIndex;
            if (delta > 0 && cur > 0)
                PageList.SelectedIndex = cur - 1;
            else if (delta < 0 && cur < _doc.PageCount - 1)
                PageList.SelectedIndex = cur + 1;
        }

        private void ApplyZoom()
        {
            if (_pageContentGrid.LayoutTransform is ScaleTransform st)
            {
                st.ScaleX = _zoomLevel;
                st.ScaleY = _zoomLevel;
            }
            SyncZoomBox();   // keep the toolbar box in step (FitToWidth/FitToPage don't call SetZoom)
            // Recalculate how many pages fit after zoom changes.
            // Use RefreshPageView so link overlays are re-added after RenderAdditionalPages
            // calls ClearSecondaryPages (which wipes them).
            int applyIdx = PageList.SelectedIndex;
            if (applyIdx >= 0)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => RefreshPageView(applyIdx));

            // If the user has zoomed in far enough that the current bitmap would be
            // upscaled by more than 20%, queue a deferred re-render at higher resolution.
            // The timer debounces rapid Ctrl+scroll so we re-render only once per gesture.
            if (applyIdx >= 0 && _zoomLevel > _lastRenderZoom * 1.20 && _doc is not null)
            {
                if (_rerenderTimer is null)
                {
                    _rerenderTimer = new System.Windows.Threading.DispatcherTimer
                        { Interval = TimeSpan.FromMilliseconds(400) };
                    _rerenderTimer.Tick += (_, _) =>
                    {
                        _rerenderTimer!.Stop();
                        if (_doc is not null && PageList.SelectedIndex >= 0)
                            RenderPage(PageList.SelectedIndex);
                    };
                }
                _rerenderTimer.Stop();
                _rerenderTimer.Start();
            }
        }

        private void ResetZoom() => SetZoom(1.0);

        // Grid zoom snaps to "fit N pages across the viewport", so zooming steps through clean
        // columns (1, 2, 3, ... per row) instead of arbitrary percentages. N rises as you zoom out
        // and keeps going for larger documents until the page size hits the zoom floor.
        private double GridZoomForN(int n)
        {
            if (n < 1) n = 1;
            double rdW = _annotationCanvas.Width > 0 ? _annotationCanvas.Width : 1583;
            double vw  = PagePreviewPanel.ActualWidth;   // SAME width + slot the layout uses
            if (vw <= 0 || rdW <= 0) return _zoomLevel;
            // RenderAdditionalPages lays out pages in slots of (rdW + 12) within (ActualWidth - 24);
            // invert that so "fit n" produces exactly n columns with no gap.
            return (vw - 24.0) / (n * (rdW + 12.0));
        }

        private void GridZoomStep(bool zoomOut)
        {
            double rdW = _annotationCanvas.Width > 0 ? _annotationCanvas.Width : 1583;
            double vw  = PagePreviewPanel.ActualWidth;
            if (vw <= 0 || rdW <= 0) { SetZoom(zoomOut ? _zoomLevel - ZoomStep : _zoomLevel + ZoomStep); return; }
            // Current columns, computed the SAME way RenderAdditionalPages computes pagesPerRow.
            int curN = Math.Max(1, (int)Math.Floor((vw - 24.0) / (_zoomLevel * (rdW + 12.0))));
            int newN = Math.Max(1, zoomOut ? curN + 1 : curN - 1);
            // If the column count is already at the limit the clamped zoom is unchanged, so
            // skip the re-render entirely - otherwise every Ctrl+Scroll reloads all tiles
            // without changing anything.
            double target = Math.Max(ZoomMin, Math.Min(ZoomMax, GridZoomForN(newN)));
            if (Math.Abs(target - _zoomLevel) < 1e-4) return;
            SetZoom(target);   // already clamped to [ZoomMin, ZoomMax]
        }

        /// <summary>
        /// Central zoom-change entry point for buttons, keyboard shortcuts, and the dropdown.
        /// Clamps to [ZoomMin, ZoomMax], applies the scale, syncs the combo box, and updates
        /// the status bar. Does NOT apply a fit mode — call FitToWidth / FitToPage for that.
        /// </summary>
        // The internal _zoomLevel scales each page's layout box. In Continuous mode that box is
        // the page's natural DIP width, so _zoomLevel already reads as true zoom (1.0 = 100%).
        // In Single/Two-Page/Grid the box is the render-dimension bitmap (~2x natural width), so
        // the raw _zoomLevel reads about half the real size. DisplayZoomFactor converts to true
        // zoom for everything shown to (or typed by) the user; the internal value is unchanged.
        private double DisplayZoomFactor()
        {
            if (_viewMode == ViewMode.Continuous || _doc is null) return 1.0;
            int idx = _viewMode == ViewMode.Grid ? 0 : Math.Max(0, PageList.SelectedIndex);
            if (idx < 0 || idx >= _doc.PageCount) return 1.0;
            if (!_renderDims.TryGetValue(idx, out var d) || d.w <= 0) return 1.0;
            double wpt = _doc.Pages[idx].Width.Point, hpt = _doc.Pages[idx].Height.Point;
            if (_pageRotations.TryGetValue(idx, out int r) && (r == 90 || r == 270)) wpt = hpt;
            double naturalW = wpt * 96.0 / 72.0;
            if (naturalW <= 0) return 1.0;
            return d.w / naturalW;
        }
        private double DisplayZoomPct() => _zoomLevel * DisplayZoomFactor() * 100.0;

        private void SetZoom(double level)
        {
            _fitMode   = FitMode.None;
            _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, level));
            ApplyZoom();
            SyncZoomBox();
            if (_doc != null && PageList.SelectedIndex >= 0)
                SetStatus($"Page {PageList.SelectedIndex + 1} of {_doc.PageCount} - {DisplayZoomPct():F0}%");
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)  { if (_viewMode == ViewMode.Grid) GridZoomStep(false); else SetZoom(_zoomLevel + ZoomStep); }
        private void ZoomOut_Click(object sender, RoutedEventArgs e) { if (_viewMode == ViewMode.Grid) GridZoomStep(true);  else SetZoom(_zoomLevel - ZoomStep); }

        private void SyncZoomBox()
        {
            if (_zoomBox is null) return;
            _zoomBox.SelectionChanged -= ZoomBox_SelectionChanged;

            // When a fit mode is active, show the "Fit Width"/"Fit Page" entry rather than a raw
            // percentage so the box matches the status bar.
            string? fitTag = _fitMode == FitMode.Width ? "fitwidth"
                           : _fitMode == FitMode.Page  ? "fitpage"
                           : null;
            if (fitTag != null)
            {
                foreach (ComboBoxItem item in _zoomBox.Items)
                {
                    if (item.Tag?.ToString() == fitTag)
                    {
                        _zoomBox.SelectedItem = item;
                        _zoomBox.SelectionChanged += ZoomBox_SelectionChanged;
                        return;
                    }
                }
            }

            string target = $"{DisplayZoomPct():F0}%";
            foreach (ComboBoxItem item in _zoomBox.Items)
            {
                if (item.Content?.ToString() == target)
                {
                    _zoomBox.SelectedItem = item;
                    _zoomBox.SelectionChanged += ZoomBox_SelectionChanged;
                    return;
                }
            }
            // No preset match - clear dropdown selection and show free-form percentage
            _zoomBox.SelectedItem = null;
            _zoomBox.Text = target;
            _zoomBox.SelectionChanged += ZoomBox_SelectionChanged;
        }

        private void ZoomBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_zoomBox?.SelectedItem is not ComboBoxItem item) return;
            string? tag = item.Tag?.ToString();
            if (tag is null) return;

            if (tag == "fitwidth") { FitToWidth(); return; }
            if (tag == "fitpage")  { FitToPage();  return; }

            if (double.TryParse(tag, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double z))
            {
                _fitMode = FitMode.None;
                // Preset tags are true zoom (1.0 = 100%); convert to the internal render-dim scale.
                double zf = DisplayZoomFactor(); if (zf <= 0) zf = 1.0;
                _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, z / zf));
                ApplyZoom();
                if (PageList.SelectedIndex >= 0 && _doc != null)
                    SetStatus($"Page {PageList.SelectedIndex + 1} of {_doc.PageCount} - {DisplayZoomPct():F0}%");
            }
        }

        private void FitToWidth()
        {
            double viewW = PagePreviewPanel.ActualWidth - 40;
            if (viewW <= 0) return;

            // Continuous mode: pages are laid out at _continuousPageW (natural DIPs width)
            // and scaled by the ScaleTransform on PageContentGrid. PageImage is hidden, so
            // we cannot use its Source as a guard; use _continuousPageW directly instead.
            if (_viewMode == ViewMode.Continuous)
            {
                if (_continuousPageW <= 0) return;
                _fitMode   = FitMode.Width;
                _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, viewW / _continuousPageW));
                ApplyZoom();
                int ci = PageList.SelectedIndex;
                if (ci >= 0 && _doc != null)
                    SetStatus(string.Format(Loc("Str_FitWidth"), ci + 1, _doc.PageCount, $"{DisplayZoomPct():F0}"));
                return;
            }

            if (PageImage.Source is null) return;
            // Use _renderDims rather than PageImage.ActualWidth - the latter can be stale
            // (reporting the previous page's layout size) if WPF layout hasn't fully settled.
            // _renderDims is set synchronously inside RenderPage so it always matches the
            // current page. dipW is zoom-stable: scaledMax scales with zoom while RenderPage
            // divides by zoomFactor, so the two cancel out. Use dipW directly.
            int idx = PageList.SelectedIndex;
            double dipW = (idx >= 0 && _renderDims.TryGetValue(idx, out var dimsW))
                ? dimsW.w
                : (PageImage.ActualWidth > 0 ? PageImage.ActualWidth : 1);
            if (dipW <= 0) return;
            // Two Page mode shows two pages side by side — each page gets roughly half
            // the viewport width (minus a small gap between pages).
            double slotW = _viewMode == ViewMode.TwoPage ? (viewW - 12) / 2 : viewW;
            _fitMode = FitMode.Width;
            _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax, slotW / dipW));
            ApplyZoom();
            if (idx >= 0 && _doc != null)
                SetStatus(string.Format(Loc("Str_FitWidth"), idx + 1, _doc.PageCount, $"{DisplayZoomPct():F0}"));
        }

        private void FitToPage()
        {
            double viewW = PagePreviewPanel.ActualWidth  - 40;
            double viewH = PagePreviewPanel.ActualHeight - 40;
            if (viewW <= 0 || viewH <= 0) return;

            // Continuous mode: derive the current page's natural height from its PDF aspect
            // ratio and _continuousPageW, then fit both axes.
            if (_viewMode == ViewMode.Continuous)
            {
                if (_continuousPageW <= 0 || _doc is null) return;
                int ci = PageList.SelectedIndex;
                if (ci < 0) return;
                var pdfPage = _doc.Pages[ci];
                double ratio = Math.Max(0.1, pdfPage.Height.Point / Math.Max(1.0, pdfPage.Width.Point));
                double dipH  = _continuousPageW * ratio;
                _fitMode   = FitMode.Page;
                _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax,
                    Math.Min(viewW / _continuousPageW, viewH / dipH)));
                ApplyZoom();
                SetStatus(string.Format(Loc("Str_FitPage"), ci + 1, _doc.PageCount, $"{DisplayZoomPct():F0}"));
                return;
            }

            if (PageImage.Source is null) return;
            int idx = PageList.SelectedIndex;
            // dipW/dipH are zoom-stable (see FitToWidth comment). Use them directly.
            double dipW = (idx >= 0 && _renderDims.TryGetValue(idx, out var dimsP))
                ? dimsP.w
                : (PageImage.ActualWidth > 0 ? PageImage.ActualWidth : 1);
            double dipH2 = (idx >= 0 && _renderDims.TryGetValue(idx, out var dimsP2))
                ? dimsP2.h
                : (PageImage.ActualHeight > 0 ? PageImage.ActualHeight : 1);
            if (dipW <= 0 || dipH2 <= 0) return;
            double slotW2 = _viewMode == ViewMode.TwoPage ? (viewW - 12) / 2 : viewW;
            _fitMode = FitMode.Page;
            _zoomLevel = Math.Max(ZoomMin, Math.Min(ZoomMax,
                Math.Min(slotW2 / dipW, viewH / dipH2)));
            ApplyZoom();
            SetStatus(string.Format(Loc("Str_FitPage"), idx + 1, _doc!.PageCount, $"{DisplayZoomPct():F0}"));
        }

        // Re-fit the main view after a reload. Grid keeps its column-fit (FitToWidth alone would
        // yank it out into a single-page Fit Width view); other modes honor the fit mode.
        private void ReapplyGridOrFit()
        {
            if (_viewMode == ViewMode.Grid)
            {
                double rdW = _annotationCanvas.Width > 0 ? _annotationCanvas.Width : 1583;
                double vw  = PagePreviewPanel.ActualWidth;
                if (vw > 0 && rdW > 0)
                {
                    int curN = Math.Max(1, (int)Math.Round((vw - 24.0) / (Math.Max(0.01, _zoomLevel) * (rdW + 12.0))));
                    SetZoom(GridZoomForN(curN));
                }
                else ApplyZoom();
                return;
            }
            if (_fitMode == FitMode.Page) FitToPage();
            else FitToWidth();
        }

        private void PagePreviewPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_cropPreviewRect is not null || _cropConfirmBar is not null) return;
            // Grid keeps its zoom on resize. Re-zooming here rebuilds the tiles, which can toggle a
            // scrollbar, change the viewport size again, and feed back into this handler - an
            // infinite layout loop that freezes the app. The grid already fits on open and on
            // mode switch, so do nothing here for grid.
            if (_viewMode == ViewMode.Grid) return;
            if (_fitMode == FitMode.Width) FitToWidth();
            else if (_fitMode == FitMode.Page) FitToPage();
        }

        private void PagePreviewPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            bool spaceDown = Keyboard.IsKeyDown(Key.Space);
            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && spaceDown))
            {
                _isPanning  = true;
                _panStart   = e.GetPosition(PagePreviewPanel);
                _panScrollH = PagePreviewPanel.HorizontalOffset;
                _panScrollV = PagePreviewPanel.VerticalOffset;
                PagePreviewPanel.CaptureMouse();
                PagePreviewPanel.Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
            // Crop: allow starting the selection OUTSIDE the page. On-page clicks are handled by
            // the page overlay; here we catch clicks in the margins, route them to the nearest page
            // overlay, and clamp the start point to the page edge so the rect stays on the page.
            else if (e.ChangedButton == MouseButton.Left && !spaceDown
                     && _currentTool == EditTool.Crop && _doc is not null)
            {
                // Resolve the page surface for an off-page crop start in any view mode. On-page
                // clicks are left to the page canvas/overlay; we only handle margin clicks here.
                Canvas? target = null;
                if (_viewMode == ViewMode.Continuous)
                {
                    if (!(e.OriginalSource is DependencyObject osc && IsWithinPageOverlay(osc)))
                    {
                        int pg = NearestContinuousPage(e.GetPosition(_continuousPanel).Y);
                        if (pg >= 0) _continuousCanvases.TryGetValue(pg, out target);
                    }
                }
                else
                {
                    // Single / Two-Page / Grid. An on-page click is handled by that page's own
                    // surface: the primary page uses _annotationCanvas, secondary/grid tiles use
                    // their per-page overlay. Only a genuine margin click (on neither) is routed
                    // here, and we fall back to the primary page for it.
                    bool onPrimary = e.OriginalSource is DependencyObject oss && IsDescendantOf(oss, _annotationCanvas);
                    bool onTile    = e.OriginalSource is DependencyObject ost && IsWithinPageOverlay(ost);
                    if (!onPrimary && !onTile)
                        target = _annotationCanvas;
                }
                if (target is not null && target.Width > 0 && target.Height > 0)
                {
                    _activeCanvas = target;
                    var p = e.GetPosition(target);
                    p.X = Math.Max(0, Math.Min(target.Width, p.X));
                    p.Y = Math.Max(0, Math.Min(target.Height, p.Y));
                    StartCropDraw(p);
                    e.Handled = true;
                }
            }
        }

        // Begin a crop selection on the active overlay at pos (render-dim coords).
        private void StartCropDraw(Point pos)
        {
            _cropPageIndex = _activeCanvas.Tag is int cpi ? cpi : (_viewMode == ViewMode.Grid ? 0 : PageList.SelectedIndex);
            ClearSelection();
            HideCropConfirmBar();
            _isDrawing = true;
            _drawStart = pos;
            _cropPreviewRect = new Rectangle
            {
                Stroke          = Brushes.White,
                StrokeThickness = 1.5,
                StrokeDashArray = [5, 3],
                Fill            = AccentBrush(55),
                Width = 0, Height = 0,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 3, Opacity = 0.7 },
            };
            Canvas.SetLeft(_cropPreviewRect, pos.X);
            Canvas.SetTop(_cropPreviewRect, pos.Y);
            Panel.SetZIndex(_cropPreviewRect, 1);
            _activeCanvas.Children.Add(_cropPreviewRect);
            _activePreview = _cropPreviewRect;
            _activeCanvas.CaptureMouse();
        }

        private bool IsWithinPageOverlay(DependencyObject node)
        {
            var cur = node;
            while (cur != null)
            {
                if (cur is Canvas c && _continuousCanvases.ContainsValue(c)) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private int NearestContinuousPage(double yInPanel)
        {
            int best = -1; double bestDist = double.MaxValue;
            for (int i = 0; i < _continuousTops.Count && i < _continuousPanel.Children.Count; i++)
            {
                double top = _continuousTops[i];
                double h = ((FrameworkElement)_continuousPanel.Children[i]).Height;
                if (double.IsNaN(h)) h = 0;
                double bottom = top + h;
                double dist = yInPanel < top ? top - yInPanel : (yInPanel > bottom ? yInPanel - bottom : 0);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        private void PagePreviewPanel_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;
            var pos = e.GetPosition(PagePreviewPanel);
            PagePreviewPanel.ScrollToHorizontalOffset(_panScrollH - (pos.X - _panStart.X));
            PagePreviewPanel.ScrollToVerticalOffset  (_panScrollV - (pos.Y - _panStart.Y));
            e.Handled = true;
        }

        private void PagePreviewPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning) return;
            if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Left) return;
            _isPanning = false;
            PagePreviewPanel.ReleaseMouseCapture();
            PagePreviewPanel.Cursor = _spaceHeld ? Cursors.Hand : Cursors.Arrow;
            e.Handled = true;
        }

        // ============================================================
        // Drag/drop: file open
        // ============================================================

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Length > 0 && files[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    OpenFile(files[0]);
            }
        }

        private void DropZone_Click(object sender, MouseButtonEventArgs e) => Open_Click(sender, e);

        // ============================================================
        // Drag/drop: page reorder
        // ============================================================

        private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            _dragStartPoint = e.GetPosition(null);

        private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (PageList.SelectedIndex >= 0)
                    DragDrop.DoDragDrop(PageList, PageList.SelectedIndex, DragDropEffects.Move);
            }
        }

        private void PageList_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(int)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void PageList_Drop(object sender, DragEventArgs e)
        {
            if (_doc is null || !e.Data.GetDataPresent(typeof(int))) return;
            var doc = _doc;
            int fromIdx = (int)e.Data.GetData(typeof(int))!;
            var pos = e.GetPosition(PageList);
            int toIdx = PageList.Items.Count - 1;
            for (int i = 0; i < PageList.Items.Count; i++)
            {
                if (PageList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                {
                    var itemPos = item.TranslatePoint(new Point(0, item.ActualHeight / 2), PageList);
                    if (pos.Y < itemPos.Y) { toIdx = i; break; }
                }
            }
            if (fromIdx == toIdx) return;
            var page = doc.Pages[fromIdx];
            doc.Pages.RemoveAt(fromIdx);
            if (toIdx > fromIdx) toIdx--;
            doc.Pages.Insert(toIdx, page);
            SaveTempAndReload();
            PageList.SelectedIndex = toIdx;
        }

        // ============================================================
        // Page selection handler
        // ============================================================

        // Lazy accessor — resolves PageList's internal ScrollViewer on first use.
        private ScrollViewer? _sidebarSv;
        private ScrollViewer? SidebarScrollViewer
            => _sidebarSv ??= FindDescendant<ScrollViewer>(PageList);

        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T hit) return hit;
                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void PageList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            SidebarScrollViewer?.ScrollToVerticalOffset(
                SidebarScrollViewer.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private void PageJumpBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _doc is null) return;
            e.Handled = true;
            if (int.TryParse(_pageJumpBox.Text, out int pg))
            {
                int idx = Math.Max(0, Math.Min(_doc.PageCount - 1, pg - 1));
                PageList.SelectedIndex = idx;
            }
            else
            {
                // Restore current page number if input was invalid
                _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
            }
            Keyboard.ClearFocus();
        }

        private void PageJumpBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _pageJumpBox.SelectAll();
        }

        private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageList.SelectedIndex >= 0)
            {
                CommitActiveTextBox();
                ClearSelection();
                ClearTextSelection();
                if (_viewMode == ViewMode.Continuous)
                {
                    _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
                    ScrollContinuousToPage(PageList.SelectedIndex);
                    return;
                }
                if (_viewMode == ViewMode.Grid)
                {
                    // Grid is a stable overview: selecting a page highlights it but must NOT
                    // re-anchor the grid. It still needs an initial render (open / first display)
                    // when no tiles exist yet; later selections only update the highlight.
                    _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
                    if (_pageContentPanel.Children.Count <= 1)
                    {
                        PagePreviewPanel.ScrollToTop();
                        PagePreviewPanel.ScrollToHorizontalOffset(0);
                        RenderPage(0);   // grid primary is always page 0
                        // Default the grid to a clean 3-columns-across fit. Deferred to Loaded so the
                        // viewport width is valid (it can still be 0 mid-open, which would fall back
                        // to a carried-over zoom and show a single large page).
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                            (Action)(() => SetZoom(GridZoomForN(Math.Min(_doc?.PageCount ?? 1, 3)))));
                    }
                    return;
                }
                PagePreviewPanel.ScrollToTop();
                PagePreviewPanel.ScrollToHorizontalOffset(0);
                RenderPage(PageList.SelectedIndex);
                ApplyZoom();
                // Update page jump box
                _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
                // Re-highlight search results on this page if a search is active
                if (_searchBar is not null && _searchBar.Visibility == Visibility.Visible
                    && _allSearchRects.Count > 0)
                    HighlightSearchResultsOnCurrentPage();
            }
        }

        private void ShortcutHelp_Click(object sender, RoutedEventArgs e)
        {
            ShortcutOverlay.Visibility = ShortcutOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShortcutOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Click on the dim backdrop closes the overlay.
            ShortcutOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShortcutOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Stop the click from bubbling up to the backdrop handler.
            e.Handled = true;
        }

        private void ShortcutOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            ShortcutOverlay.Visibility = Visibility.Collapsed;
        }

        // ── About overlay ───────────────────────────────────────────────

        private void AboutTab_Click(object sender, RoutedEventArgs e) => ShowAboutOverlay();

        private void ShowAboutOverlay()
        {
            // Populate dynamic values (SHA256 is slow; run on background thread)
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                               .GetName().Version?.ToString(3) ?? "?";
            var (sigValid, sigSubject, sigThumbprint) = App.GetExeSignerInfo();

            AboutPublisherBlock.Text   = sigValid ? sigSubject : "(not signed or chain failed)";
            AboutThumbprintBlock.Text  = string.IsNullOrEmpty(sigThumbprint) ? "(none)" : sigThumbprint;
            AboutSha256Block.Text      = Loc("Str_About_Computing");

            // Logo block
            AboutLogoBlock.Inlines.Clear();
            var logoHl = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Scalpel"))
            {
                Foreground      = (System.Windows.Media.Brush)FindResource("Accent"),
                TextDecorations = null
            };
            logoHl.Click += (_, _) =>
                Process.Start(new ProcessStartInfo("https://github.com/blakazulu/ScalpelPDF") { UseShellExecute = true });
            AboutLogoBlock.Inlines.Add(logoHl);

            // Tagline block
            AboutTaglineBlock.Inlines.Clear();
            AboutTaglineBlock.Inlines.Add(new System.Windows.Documents.Run("A fast, free PDF toolkit for Windows.")
            {
                Foreground = (System.Windows.Media.Brush)FindResource("TextDim")
            });

            // Version block (clickable - opens GitHub release)
            AboutVersionBlock.Inlines.Clear();
            var verHl = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run($"v{version}"))
            {
                Foreground      = (System.Windows.Media.Brush)FindResource("Accent"),
                TextDecorations = null
            };
            verHl.Click += (_, _) =>
                Process.Start(new ProcessStartInfo(
                    $"https://github.com/blakazulu/ScalpelPDF/releases/tag/v{version}")
                { UseShellExecute = true });
            AboutVersionBlock.Inlines.Add(verHl);

            AboutOverlay.Visibility = Visibility.Visible;

            // Compute SHA256 off the UI thread
            System.Threading.Tasks.Task.Run(() =>
            {
                var sha256 = App.GetExeSha256();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    (Action)(() => AboutSha256Block.Text = sha256));
            });
        }

        private void AboutOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AboutOverlay.Visibility = Visibility.Collapsed;
        }

        private void AboutOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void AboutOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            AboutOverlay.Visibility = Visibility.Collapsed;
        }

        // ── What's New (changelog) ─────────────────────────────────────────
        private void WhatsNewTab_Click(object sender, RoutedEventArgs e) => ShowWhatsNewOverlay();

        private void ShowWhatsNewOverlay()
        {
            WhatsNewList.Children.Clear();
            var accent = (System.Windows.Media.Brush)FindResource("Accent");
            var dim    = (System.Windows.Media.Brush)FindResource("TextDim");
            var body   = (System.Windows.Media.Brush)FindResource("TextPrimary");
            var uiFont = (FontFamily)FindResource("FontUI");

            bool first = true;
            foreach (var rel in Scalpel.Services.Changelog.Releases)
            {
                // Version + date header
                var header = new TextBlock
                {
                    FontFamily = uiFont, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = accent,
                    Margin = new Thickness(0, first ? 0 : 18, 0, 2),
                };
                header.Inlines.Add(new System.Windows.Documents.Run($"Version {rel.Version}"));
                header.Inlines.Add(new System.Windows.Documents.Run($"   ·   {rel.Date}")
                {
                    Foreground = dim,
                    FontWeight = FontWeights.Normal,
                });
                WhatsNewList.Children.Add(header);
                first = false;

                // Bulleted changes
                foreach (var change in rel.Changes)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                    var bullet = new TextBlock
                    {
                        Text = "•", FontFamily = uiFont, FontSize = 12, Foreground = accent,
                        Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                    };
                    DockPanel.SetDock(bullet, Dock.Left);
                    var text = new TextBlock
                    {
                        Text = change, FontFamily = uiFont, FontSize = 12, Foreground = body,
                        TextWrapping = TextWrapping.Wrap,
                    };
                    row.Children.Add(bullet);
                    row.Children.Add(text);
                    WhatsNewList.Children.Add(row);
                }
            }

            WhatsNewOverlay.Visibility = Visibility.Visible;
        }

        private void WhatsNewOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WhatsNewOverlay.Visibility = Visibility.Collapsed;
        }

        private void WhatsNewOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void WhatsNewOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            WhatsNewOverlay.Visibility = Visibility.Collapsed;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        // ============================================================
        // View Mode
        // ============================================================

        private void SetViewMode(ViewMode mode)
        {
            if (_viewMode == mode) return;
            _viewMode = mode;
            App.SetSetting("ViewMode", mode.ToString());

            bool isContinuous = mode == ViewMode.Continuous;
            _pageContentPanel.Visibility = isContinuous ? Visibility.Collapsed : Visibility.Visible;
            _continuousPanel.Visibility  = isContinuous ? Visibility.Visible   : Visibility.Collapsed;

            if (!isContinuous)
            {
                _continuousRenderCts?.Cancel();
                _continuousPanel.Children.Clear();
                _continuousTops.Clear();
                _continuousCanvases.Clear();
            }

            UpdateViewModeButtons();
            if (_doc is null) return;
            int idx = PageList.SelectedIndex;
            if (mode == ViewMode.Continuous)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => SetupContinuousView(idx));
            }
            else
            {
                _secondaryRenderCts?.Cancel();
                ClearSecondaryPages();
                _pageContentPanel.Width = double.NaN;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    RenderPage(mode == ViewMode.Grid ? 0 : idx);
                    // Grid: apply a clean column-fit zoom (continuous's zoom is far too large for a
                    // grid, and a non-column zoom leaves a gap). SetZoom -> ApplyZoom defers the
                    // single tile render, so return here instead of calling RefreshPageView again
                    // (a second render would duplicate tiles).
                    if (mode == ViewMode.Grid)
                    {
                        SetZoom(GridZoomForN(Math.Min(_doc!.PageCount, 3)));
                        return;
                    }
                    // Switching into Single or Two-Page fits the whole page so it isn't left at an
                    // awkward carried-over zoom from another mode.
                    if      (mode == ViewMode.Single || mode == ViewMode.TwoPage) FitToPage();
                    else if (_fitMode == FitMode.Width) FitToWidth();
                    else if (_fitMode == FitMode.Page)  FitToPage();
                    else                                ApplyZoom();
                    RefreshPageView(idx);
                });
            }
        }

        private void ScrollContinuousToPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _continuousTops.Count) return;
            double target = _continuousTops[pageIndex] * _zoomLevel;
            PagePreviewPanel.ScrollToVerticalOffset(target);
        }

        private void PagePreviewPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_viewMode != ViewMode.Continuous || _continuousTops.Count == 0) return;

            double viewportCenter = (PagePreviewPanel.VerticalOffset + PagePreviewPanel.ViewportHeight * 0.5)
                                    / Math.Max(0.01, _zoomLevel);
            int nearest = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < _continuousTops.Count; i++)
            {
                if (i >= _continuousPanel.Children.Count) break;
                var slot = (FrameworkElement)_continuousPanel.Children[i];
                double center = _continuousTops[i] + slot.Height * 0.5;
                double dist   = Math.Abs(center - viewportCenter);
                if (dist < minDist) { minDist = dist; nearest = i; }
            }

            if (PageList.SelectedIndex != nearest)
            {
                _pageJumpBox.Text = (nearest + 1).ToString();
                // Update sidebar thumbnail without triggering a full page render
                PageList.SelectionChanged -= PageList_SelectionChanged;
                PageList.SelectedIndex = nearest;
                PageList.SelectionChanged += PageList_SelectionChanged;
            }
        }

        private void SetupContinuousView(int initialPage)
        {
            if (_doc is null) return;
            _continuousRenderCts?.Cancel();
            _continuousPanel.Children.Clear();
            _continuousTops.Clear();
            _continuousCanvases.Clear();

            // Use the PDF's natural page width in WPF DIPs (96 DIP/inch, 72 pt/inch).
            // This is zoom-independent, which is critical: FitToWidth computes
            //   zoom = viewportW / _continuousPageW
            // and if _continuousPageW were derived from the current zoom level the two
            // would cancel and FitToWidth would always return approximately the old zoom.
            var refPage = _doc.Pages[0];
            _continuousPageW = Math.Max(200.0, refPage.Width.Point * (96.0 / 72.0));

            double y = 0;
            for (int i = 0; i < _doc.PageCount; i++)
            {
                _continuousTops.Add(y);
                var pdfPage = _doc.Pages[i];
                double pw = pdfPage.Width.Point, ph = pdfPage.Height.Point;
                if (_pageRotations.TryGetValue(i, out int prot) && (prot == 90 || prot == 270))
                    (pw, ph) = (ph, pw);
                double ratio = Math.Max(0.1, ph / Math.Max(1, pw));
                double slotH = _continuousPageW * ratio;

                // Canonical render-dim space (matches single-page RenderPage: longest side -> 2048)
                // so annotation coordinates are identical in both view modes.
                double maxDim = Math.Max(pw, ph);
                int rdW = Math.Max(1, (int)Math.Round(2048.0 * pw / maxDim));
                int rdH = Math.Max(1, (int)Math.Round(2048.0 * ph / maxDim));
                _renderDims[i] = (rdW, rdH);

                // Per-page annotation overlay: sized in render-dim space, scaled to the slot.
                double slotScale = _continuousPageW / rdW;
                var overlay = new Canvas
                {
                    Width           = rdW,
                    Height          = rdH,
                    Background       = Brushes.Transparent,
                    ClipToBounds     = true,
                    Tag              = i,
                    LayoutTransform  = new System.Windows.Media.ScaleTransform(slotScale, slotScale)
                };
                overlay.PreviewMouseLeftButtonDown += Canvas_MouseLeftButtonDown;
                overlay.MouseMove                  += Canvas_MouseMove;
                overlay.PreviewMouseLeftButtonUp   += Canvas_MouseLeftButtonUp;
                _continuousCanvases[i] = overlay;

                var pageImg = new Image { Stretch = Stretch.None, Width = _continuousPageW, Height = slotH };
                RenderOptions.SetBitmapScalingMode(pageImg, BitmapScalingMode.HighQuality);

                var slotGrid = new Grid();
                slotGrid.Children.Add(pageImg);
                slotGrid.Children.Add(overlay);

                var placeholder = new Border
                {
                    Width      = _continuousPageW,
                    Height     = slotH,
                    Margin     = new Thickness(0, 0, 0, 12),
                    Background = Application.Current.TryFindResource("Background") as SolidColorBrush
                                 ?? new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    Tag = i,
                    Child = slotGrid
                };
                int capturedI = i;
                placeholder.PreviewMouseLeftButtonDown += (_, _) => PageList.SelectedIndex = capturedI;
                _continuousPanel.Children.Add(placeholder);
                y += slotH + 12;
            }

            // Re-apply fit mode now that _continuousPageW is known; default to fit-page (one whole
            // page in view) unless the user explicitly chose fit-width.
            if (_fitMode == FitMode.Width) FitToWidth(); else FitToPage();

            _continuousScrollTarget = initialPage;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                () => ScrollContinuousToPage(initialPage));

            _ = RenderContinuousPages();
        }

        private async System.Threading.Tasks.Task RenderContinuousPages()
        {
            if (_doc is null || _currentFile is null) return;
            _continuousRenderCts?.Cancel();
            _continuousRenderCts = new System.Threading.CancellationTokenSource();
            var cts = _continuousRenderCts;

            string currentFile = _currentFile;
            int pageCount      = _doc.PageCount;
            double targetW     = _continuousPageW;
            int renderW        = Math.Max(800, Math.Min(2048, (int)(targetW * 2)));

            // Capture per-page rotations on the UI thread before going async
            var rotations = new Dictionary<int, int>(_pageRotations);

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var docReader = DocLib.Instance.GetDocReader(
                        currentFile, new PageDimensions(renderW, renderW * 2));

                    for (int i = 0; i < pageCount; i++)
                    {
                        if (cts.IsCancellationRequested) return;
                        using var pr = docReader.GetPageReader(i);
                        int w = pr.GetPageWidth();
                        int h = pr.GetPageHeight();
                        var raw = pr.GetImage();
                        if (w <= 0 || h <= 0 || raw is null) continue;
                        if (rotations.TryGetValue(i, out int rot) && rot != 0)
                            (raw, w, h) = RotateBitmap(raw, w, h, rot);

                        int fi = i, fw = w, fh = h;
                        byte[] bytes = raw;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (cts.IsCancellationRequested || _viewMode != ViewMode.Continuous) return;
                            if (fi >= _continuousPanel.Children.Count) return;

                            var slot = (Border)_continuousPanel.Children[fi];
                            double dipW = slot.Width;
                            double dipH = dipW * fh / fw;
                            double dpiX = 96.0 * fw / dipW;
                            double dpiY = 96.0 * fh / dipH;

                            var bmp = new WriteableBitmap(fw, fh, dpiX, dpiY, PixelFormats.Bgra32, null);
                            bmp.WritePixels(new Int32Rect(0, 0, fw, fh), bytes, fw * 4, 0);
                            bmp.Freeze();

                            if (slot.Child is Grid slotGrid && slotGrid.Children.Count > 0
                                && slotGrid.Children[0] is Image pageImg)
                            {
                                pageImg.Source  = bmp;
                                pageImg.Width   = dipW;
                                pageImg.Height  = dipH;
                                slot.Background = Brushes.White;

                                // Size the slot and overlay from the ACTUAL rendered page so a
                                // cropped page (which renders shorter than its MediaBox estimate)
                                // fills its slot with no white bars. Mirrors single-page view.
                                slot.Height = dipH;
                                double maxF = Math.Max(fw, fh);
                                int rdW = Math.Max(1, (int)Math.Round(2048.0 * fw / maxF));
                                int rdH = Math.Max(1, (int)Math.Round(2048.0 * fh / maxF));
                                _renderDims[fi] = (rdW, rdH);
                                if (slotGrid.Children.Count > 1 && slotGrid.Children[1] is Canvas ov)
                                {
                                    ov.Width  = rdW;
                                    ov.Height = rdH;
                                    ov.LayoutTransform =
                                        new System.Windows.Media.ScaleTransform(dipW / rdW, dipW / rdW);
                                }

                                // Slot heights are now exact; recompute scroll offsets from them.
                                double yy = 0;
                                for (int k = 0; k < _continuousPanel.Children.Count && k < _continuousTops.Count; k++)
                                {
                                    _continuousTops[k] = yy;
                                    double hk = ((FrameworkElement)_continuousPanel.Children[k]).Height;
                                    if (double.IsNaN(hk)) hk = 0;
                                    yy += hk + 12;
                                }

                                // Pages render in order, so when the target page is reached every
                                // page above it has its final height; re-scroll so a crop lands you
                                // back on the same page instead of drifting to the next one.
                                if (_continuousScrollTarget >= 0 && fi >= _continuousScrollTarget)
                                {
                                    int tgt = _continuousScrollTarget;
                                    _continuousScrollTarget = -1;
                                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                                        (Action)(() => ScrollContinuousToPage(tgt)));
                                }

                                RenderAllAnnotations(fi);
                            }
                        });
                    }
                }
                catch { /* render cancelled or doc closed */ }
            }, cts.Token);
        }
    }

    // ============================================================
    // Themed dialog — replaces MessageBox for dark-UI consistency
    // ============================================================
    internal static class ScalpelDialog
    {
        // Pulls the current theme brush at call time so dialogs respect light/dark/HC themes.
        private static SolidColorBrush R(string key)
            => (SolidColorBrush)Application.Current.Resources[key];

#pragma warning disable IDE0060 // image intentionally kept for API parity with MessageBox; not yet rendered
        public static MessageBoxResult Show(
            Window? owner,
            string message,
            string title = "Scalpel",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None)
#pragma warning restore IDE0060
        {
            var result = MessageBoxResult.OK;

            var win = new Window
            {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize
            };

            var fontUI = (FontFamily)Application.Current.FindResource("FontUI");
            win.FontFamily = fontUI;

            var outerBorder = new Border
            {
                Background      = R("BgModal"),
                BorderBrush     = R("BorderDim"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(12),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4, Direction = 270
                }
            };

            var root = new StackPanel();

            // Title bar
            var titleBar = new Border
            {
                Background   = R("BgPanel"),
                Padding      = new Thickness(16, 10, 16, 10),
                CornerRadius = new CornerRadius(11, 11, 0, 0)
            };
            titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };
            titleBar.Child = new TextBlock
            {
                Text       = title,
                Foreground = R("TextPrimary"),
                FontWeight = FontWeights.SemiBold,
                FontSize   = (double)Application.Current.FindResource("FsDialogTitle"),
                FontFamily = fontUI
            };
            root.Children.Add(titleBar);

            // Message
            var msgBorder = new Border
            {
                Padding = new Thickness(20, 16, 20, 8),
                Child = new TextBlock
                {
                    Text         = message,
                    Foreground   = R("TextPrimary"),
                    FontFamily   = fontUI,
                    FontSize     = (double)Application.Current.FindResource("FsBody"),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            root.Children.Add(msgBorder);

            // Buttons — use Studio styles. Primary/confirm = StudioPrimaryButton,
            // secondary/cancel = StudioToolButton. ScalpelDialog cannot distinguish
            // destructive from non-destructive callers, so destructive confirm
            // buttons remain StudioPrimaryButton (noted in Task 10 report).
            var btnPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button MakeBtn(string label, MessageBoxResult res, bool primary = false)
            {
                var styleKey = primary ? "StudioPrimaryButton" : "StudioToolButton";
                var btn = new Button
                {
                    Content = label,
                    Style   = (Style)Application.Current.FindResource(styleKey),
                    Width   = 80,
                    Margin  = new Thickness(8, 0, 0, 0)
                };
                btn.Click += (_, _2) => { result = res; win.Close(); };
                return btn;
            }

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    btnPanel.Children.Add(MakeBtn("OK", MessageBoxResult.OK, primary: true));
                    break;
                case MessageBoxButton.OKCancel:
                    btnPanel.Children.Add(MakeBtn("Cancel", MessageBoxResult.Cancel));
                    btnPanel.Children.Add(MakeBtn("OK",     MessageBoxResult.OK,     primary: true));
                    break;
                case MessageBoxButton.YesNo:
                    btnPanel.Children.Add(MakeBtn("No",  MessageBoxResult.No));
                    btnPanel.Children.Add(MakeBtn("Yes", MessageBoxResult.Yes, primary: true));
                    break;
                case MessageBoxButton.YesNoCancel:
                    btnPanel.Children.Add(MakeBtn("Cancel", MessageBoxResult.Cancel));
                    btnPanel.Children.Add(MakeBtn("No",     MessageBoxResult.No));
                    btnPanel.Children.Add(MakeBtn("Yes",    MessageBoxResult.Yes,    primary: true));
                    break;
            }

            root.Children.Add(new Border
            {
                Padding = new Thickness(16, 8, 16, 16),
                Child   = btnPanel
            });

            outerBorder.Child = root;

            win.Content = outerBorder;
            win.ShowDialog();
            return result;
        }
    }
}
