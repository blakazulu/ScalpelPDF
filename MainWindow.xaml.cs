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
