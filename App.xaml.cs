using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Scalpel.Services;
using Microsoft.Win32;

namespace Scalpel
{
    public partial class App : Application
    {
        // ============================================================
        // Paths
        // ============================================================

        private static readonly string AppName   = "Scalpel";

        // ============================================================
        // Shell interop
        // ============================================================

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST       = 0x0000;

        // ============================================================
        // Packaging identity (MSIX / Microsoft Store)
        // ============================================================
        //
        // When Scalpel runs from an MSIX package (Store install or sideload), the
        // package — not the app — owns install, uninstall, file associations, and
        // shortcuts. All of the self-installer machinery below is therefore disabled
        // in packaged mode; see IsPackaged() callers.

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        private static readonly Lazy<bool> _isPackaged = new(() =>
        {
            try
            {
                int len = 0;
                // GetCurrentPackageFullName is only present on Windows 8+; P/Invoke
                // resolves lazily, so a missing export throws here and we treat it as
                // unpackaged. A return of APPMODEL_ERROR_NO_PACKAGE means no package.
                int rc = GetCurrentPackageFullName(ref len, null);
                return rc != APPMODEL_ERROR_NO_PACKAGE;
            }
            catch { return false; }
        });

        /// <summary>
        /// True when running from an MSIX package (Microsoft Store or sideload).
        /// In this mode the self-installer, portable badge, file-association
        /// registration, and self-uninstall are all suppressed — the package
        /// manifest and the OS handle those concerns.
        /// </summary>
        internal static bool IsPackaged() => _isPackaged.Value;

        // ============================================================
        // Startup
        // ============================================================

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException                    += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException      += OnDomainException;
            TaskScheduler.UnobservedTaskException           += OnUnobservedTaskException;

            base.OnStartup(e);

            if (!CheckPdfiumIntegrity()) { Shutdown(2); return; }

            // Handle uninstall flag (called by Add/Remove Programs). Check the full raw
            // command line (not just e.Args[0]) so it works regardless of how the shell
            // passes the argument. Ignored in packaged mode — the Store/OS removes MSIX
            // packages, and the desktop self-uninstall would target a non-existent dir.
            // Detect "running as the uninstaller": launched as uninstall.exe (the argument-less
            // uninstaller) OR with a /uninstall flag. The command-line exe path (arg[0]) is the
            // reliable source for a copied exe; MainModule is a secondary fallback.
            string arg0    = Environment.GetCommandLineArgs().FirstOrDefault() ?? "";
            string mainMod = "";
            try { mainMod = Process.GetCurrentProcess().MainModule?.FileName ?? ""; } catch { }
            bool runningAsUninstaller =
                string.Equals(System.IO.Path.GetFileName(arg0),    "uninstall.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(System.IO.Path.GetFileName(mainMod), "uninstall.exe", StringComparison.OrdinalIgnoreCase);
            bool uninstallRequested = runningAsUninstaller
                || Environment.GetCommandLineArgs()
                    .Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));
            // uninstall.exe is ONLY ever created by the per-user installer — a real MSIX
            // build never self-installs it. So when we are the dedicated uninstaller, always
            // honor it, even though IsPackaged() can falsely report true: Windows' Settings
            // app is itself packaged and the child uninstall.exe inherits its package context.
            // The legacy /uninstall-flag path stays gated by !IsPackaged() for real MSIX builds.
            if (uninstallRequested && (runningAsUninstaller || !IsPackaged()))
            {
                InstallerUI.RunUninstallFlow(
                    Scalpel.Services.Installer.WipeAllData,
                    Scalpel.Services.Installer.WriteDeferredDirWipeScript,
                    () =>
                    {
                        var bat = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scalpel_uninstall.bat");
                        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
                        {
                            WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = true,
                        });
                    });
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnLastWindowClose;
            CleanupStaleTemps();

            // Logging is on by default; "0" disables it.
            bool loggingEnabled = GetSetting("LoggingEnabled") != "0";
            Scalpel.Services.Logger.Init(enabled: loggingEnabled);
            RegisterGlobalClickLogging();
            var ver = typeof(App).Assembly.GetName().Version?.ToString() ?? "?";
            Scalpel.Services.Logger.Info("App", "app.start", $"Scalpel {ver} starting",
                new { packaged = IsPackaged() });

            ThemeManager.Initialize();
            LocaleManager.Initialize();
            RegisterPdfFonts();
            new MainWindow().Show();
        }

        /// <summary>Register bundled fonts with PdfSharpCore and install our resolver
        /// (once). Lets saved PDFs embed both system and bundled (Geist) fonts.</summary>
        private static void RegisterPdfFonts()
        {
            try
            {
                if (PdfSharpCore.Fonts.GlobalFontSettings.FontResolver is not null) return;
                foreach (var (file, bold) in new[]
                {
                    ("Geist-Regular.ttf", false),
                    ("Geist-SemiBold.ttf", true),
                })
                {
                    try
                    {
                        var uri = new Uri($"pack://application:,,,/Resources/Fonts/{file}");
                        var info = GetResourceStream(uri);
                        if (info?.Stream is null) continue;
                        using var ms = new System.IO.MemoryStream();
                        info.Stream.CopyTo(ms);
                        Scalpel.Services.PdfFontResolver.Instance
                            .RegisterBundledFont("Geist", ms.ToArray(), bold, italic: false);
                    }
                    catch { /* skip a missing/locked font resource */ }
                }
                PdfSharpCore.Fonts.GlobalFontSettings.FontResolver =
                    Scalpel.Services.PdfFontResolver.Instance;
            }
            catch { /* never block startup over font setup */ }
        }

        // ============================================================
        // Global click logging  (one handler for every button/menu click)
        // ============================================================

        private static bool _clickLoggingRegistered;

        private static void RegisterGlobalClickLogging()
        {
            if (_clickLoggingRegistered) return;
            _clickLoggingRegistered = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
                new RoutedEventHandler(OnAnyControlClicked), handledEventsToo: true);
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent,
                new RoutedEventHandler(OnAnyControlClicked), handledEventsToo: true);
        }

        private static void OnAnyControlClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var src = (e.OriginalSource as FrameworkElement) ?? sender as FrameworkElement;
                // Skip ScrollBar repeat-buttons / thumbs — they auto-fire on the hot path and are not meaningful clicks.
                if (src is System.Windows.Controls.Primitives.RepeatButton ||
                    src is System.Windows.Controls.Primitives.Thumb) return;
                string name = !string.IsNullOrEmpty(src?.Name) ? src!.Name : src?.GetType().Name ?? "?";
                string? label = (src as ContentControl)?.Content as string
                                ?? (src as ContentControl)?.Content?.ToString();
                Scalpel.Services.Logger.Info("UI", "click", name,
                    new { label, type = src?.GetType().Name });
            }
            catch { }
        }

        // ============================================================
        // Crash handling
        // ============================================================
        //
        // NOTE: AccessViolationException is not catchable on .NET 4.8 without
        // [HandleProcessCorruptedStateExceptions], which we deliberately omit.

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Scalpel.Services.Logger.Error("Error", "crash.dispatcher", e.Exception.Message, e.Exception);
            Scalpel.Services.Logger.Flush();
            var logPath = CrashReporter.Capture(e.Exception, "Dispatcher");
            bool cont   = ShowCrashDialog(e.Exception, logPath, isFatal: false);
            e.Handled   = true; // always handle; we manage the exit ourselves
            if (!cont)
            {
                CleanupSessionTemps();
                Shutdown(1);
            }
        }

        private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception
                     ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error");
            Scalpel.Services.Logger.Error("Error", "crash.appdomain", ex.Message, ex);
            Scalpel.Services.Logger.Flush();
            var logPath = CrashReporter.Capture(ex, "AppDomain");

            try
            {
                if (Dispatcher != null && !Dispatcher.HasShutdownStarted)
                    Dispatcher.Invoke(() => ShowCrashDialog(ex, logPath, isFatal: true));
                else
                    ShowCrashDialog(ex, logPath, isFatal: true);
            }
            catch { /* at least the log was written */ }

            CleanupSessionTemps();
            // CLR will terminate the process after this handler returns.
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved(); // prevent process teardown
            Scalpel.Services.Logger.Error("Error", "crash.task", e.Exception.Message, e.Exception);
            Scalpel.Services.Logger.Flush();
            var logPath = CrashReporter.Capture(e.Exception, "TaskScheduler");

            try
            {
                if (Dispatcher != null && !Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(new Action(
                        () => ShowCrashDialog(e.Exception, logPath, isFatal: false)));
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Dark-themed crash report dialog. Returns true if the user chose Continue.
        /// Must be called on the UI thread.
        /// </summary>
        private static bool ShowCrashDialog(Exception ex, string logPath, bool isFatal)
        {
            bool shouldContinue = false;

            // ── Palette ─────────────────────────────────────────────
            var bg       = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
            var dimBg    = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
            var codeBg   = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12));
            var red      = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
            var green    = new SolidColorBrush(Color.FromRgb(0x1e, 0xa5, 0x4c));
            var greenHov = new SolidColorBrush(Color.FromRgb(0x27, 0xc8, 0x60));
            var dimText  = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
            var midText  = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa));
            var redHov   = new SolidColorBrush(Color.FromRgb(0xc4, 0x2b, 0x1c));
            var grayBtn  = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            var grayHov  = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            var quitNorm = new SolidColorBrush(Color.FromRgb(0x5a, 0x10, 0x10));
            var quitHov  = new SolidColorBrush(Color.FromRgb(0xc4, 0x2b, 0x1c));

            var win = new Window
            {
                Title                 = "Scalpel — Unexpected Error",
                Width                 = 680,
                Height                = 520,
                MinWidth              = 480,
                MinHeight             = 360,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode            = ResizeMode.CanResize,
                WindowStyle           = WindowStyle.None,
                Background            = bg,
                ShowInTaskbar         = true
            };

            // ── Layout ──────────────────────────────────────────────
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ── Title bar ───────────────────────────────────────────
            var titleBar = new DockPanel { Background = dimBg };
            Grid.SetRow(titleBar, 0);
            titleBar.MouseLeftButtonDown += (_, ea) =>
            {
                if (ea.ButtonState == MouseButtonState.Pressed) win.DragMove();
            };

            var xBtn = MakeTitleBarCloseButton(dimText, redHov);
            xBtn.Click += (_, _) => { shouldContinue = false; win.Close(); };
            DockPanel.SetDock(xBtn, Dock.Right);
            titleBar.Children.Add(xBtn);
            titleBar.Children.Add(new TextBlock
            {
                Text              = "Scalpel — Unexpected Error",
                Foreground        = dimText,
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(12, 0, 0, 0)
            });
            root.Children.Add(titleBar);

            // ── Error header ─────────────────────────────────────────
            var headerPanel = new StackPanel
            {
                Background = dimBg,
                Margin     = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(headerPanel, 1);

            var headerInner = new StackPanel { Margin = new Thickness(20, 14, 20, 14) };

            var typeRow = new StackPanel { Orientation = Orientation.Horizontal };
            typeRow.Children.Add(new TextBlock
            {
                Text              = "⚠  ",
                Foreground        = red,
                FontSize          = 18,
                VerticalAlignment = VerticalAlignment.Center
            });
            typeRow.Children.Add(new TextBlock
            {
                Text              = ex.GetType().Name,
                Foreground        = red,
                FontSize          = 16,
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            headerInner.Children.Add(typeRow);

            headerInner.Children.Add(new TextBlock
            {
                Text         = ex.Message,
                Foreground   = Brushes.White,
                FontSize     = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 6, 0, 0)
            });
            headerInner.Children.Add(new TextBlock
            {
                Text       = $"Log: {logPath}",
                Foreground = dimText,
                FontSize   = 11,
                Margin     = new Thickness(0, 6, 0, 0)
            });

            headerPanel.Children.Add(headerInner);
            root.Children.Add(headerPanel);

            // ── Stack trace ──────────────────────────────────────────
            var traceBox = new TextBox
            {
                Text                          = FormatExceptionChain(ex),
                Background                    = codeBg,
                Foreground                    = midText,
                FontFamily                    = new FontFamily("Consolas,Courier New"),
                FontSize                      = 11,
                IsReadOnly                    = true,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping                  = TextWrapping.NoWrap,
                BorderThickness               = new Thickness(0),
                Padding                       = new Thickness(12, 8, 12, 8),
                Margin                        = new Thickness(0, 1, 0, 0)
            };
            Grid.SetRow(traceBox, 2);
            root.Children.Add(traceBox);

            // ── Button bar ───────────────────────────────────────────
            var btnBorder = new Border
            {
                Background = dimBg,
                Padding    = new Thickness(16, 10, 16, 10)
            };
            Grid.SetRow(btnBorder, 3);

            var btnPanel = new DockPanel();

            // Left: utility buttons
            var leftBtns = new StackPanel { Orientation = Orientation.Horizontal };

            var copyBtn = MakeCrashButton("Copy Report", grayBtn, grayHov, Brushes.White, 100);
            copyBtn.Click += (_, _) =>
            {
                try { Clipboard.SetText(BuildFullCrashReport(ex)); } catch { }
            };
            leftBtns.Children.Add(copyBtn);

            var logsBtn = MakeCrashButton("Open Logs", grayBtn, grayHov, Brushes.White, 88);
            logsBtn.Margin = new Thickness(8, 0, 0, 0);
            logsBtn.Click += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(CrashReporter.LogDir);
                    Process.Start(new ProcessStartInfo(CrashReporter.LogDir) { UseShellExecute = true });
                }
                catch { }
            };
            leftBtns.Children.Add(logsBtn);

            var githubBtn = MakeCrashButton("Report on GitHub", grayBtn, grayHov,
                new SolidColorBrush(Color.FromRgb(0x60, 0xc0, 0xff)), 128);
            githubBtn.Margin = new Thickness(8, 0, 0, 0);
            githubBtn.Click += (_, _) =>
            {
                try
                {
                    var ver    = Assembly.GetExecutingAssembly().GetName().Version;
                    var msgLen = Math.Min(80, ex.Message.Length);
                    var title  = Uri.EscapeDataString(
                        $"Crash: {ex.GetType().Name}: {ex.Message[..msgLen]}");
                    var stack  = ex.StackTrace?.Length > 800
                        ? ex.StackTrace[..800] + "\n... (truncated)"
                        : ex.StackTrace ?? "(no stack trace)";
                    var body = Uri.EscapeDataString(
                        $"**Version:** {ver?.ToString(3)}\n" +
                        $"**OS:** {Environment.OSVersion}\n" +
                        $"**Exception:** `{ex.GetType().FullName}`\n" +
                        $"**Message:** {ex.Message}\n\n" +
                        $"```\n{stack}\n```\n\n" +
                        $"_Log folder: `{CrashReporter.LogDir}`_");
                    Process.Start(new ProcessStartInfo(
                        $"https://github.com/blakazulu/ScalpelPDF/issues/new?title={title}&body={body}")
                        { UseShellExecute = true });
                }
                catch { }
            };
            leftBtns.Children.Add(githubBtn);

            DockPanel.SetDock(leftBtns, Dock.Left);
            btnPanel.Children.Add(leftBtns);

            // Right: Continue / Quit
            var rightBtns = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var contBtn = MakeCrashButton("Continue", green, greenHov,
                new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)), 88);
            contBtn.IsEnabled  = !isFatal;
            contBtn.FontWeight = isFatal ? FontWeights.Normal : FontWeights.SemiBold;
            contBtn.Margin     = new Thickness(0, 0, 8, 0);
            contBtn.Click += (_, _) => { shouldContinue = true; win.Close(); };

            var quitBtnCtrl = MakeCrashButton("Quit", quitNorm, quitHov, Brushes.White, 72);
            quitBtnCtrl.FontWeight = isFatal ? FontWeights.SemiBold : FontWeights.Normal;
            quitBtnCtrl.Click += (_, _) => { shouldContinue = false; win.Close(); };

            rightBtns.Children.Add(contBtn);
            rightBtns.Children.Add(quitBtnCtrl);

            DockPanel.SetDock(rightBtns, Dock.Right);
            btnPanel.Children.Add(rightBtns);

            btnBorder.Child = btnPanel;
            root.Children.Add(btnBorder);

            win.Content = root;
            win.ShowDialog();
            return shouldContinue;
        }

        private static Button MakeTitleBarCloseButton(SolidColorBrush fg, SolidColorBrush hoverBg)
        {
            var t  = new ControlTemplate(typeof(Button));
            var b  = new FrameworkElementFactory(typeof(Border));
            b.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            b.AppendChild(cp);
            t.VisualTree = b;

            var s    = new Style(typeof(Button));
            s.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
            s.Setters.Add(new Setter(Button.ForegroundProperty, fg));
            s.Setters.Add(new Setter(Button.TemplateProperty,   t));
            var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trig.Setters.Add(new Setter(Button.BackgroundProperty, hoverBg));
            trig.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            s.Triggers.Add(trig);

            return new Button
            {
                Content                  = "",
                FontFamily               = new FontFamily("Segoe MDL2 Assets"),
                FontSize                 = 11,
                Width                    = 46,
                BorderThickness          = new Thickness(0),
                VerticalAlignment        = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor                   = Cursors.Arrow,
                Style                    = s
            };
        }

        private static Button MakeCrashButton(string label,
            SolidColorBrush normal, SolidColorBrush hover, SolidColorBrush fg,
            double width = 88)
            => new()
            {
                Content = label,
                Width   = width,
                Height  = 28,
                Style   = MakeLauncherButtonStyle(normal, hover, fg)
            };

        private static string FormatExceptionChain(Exception ex)
        {
            var sb    = new StringBuilder();
            var inner = ex;
            var depth = 0;
            while (inner != null && depth < 5)
            {
                if (depth > 0) { sb.AppendLine(); sb.AppendLine("=== Inner Exception ==="); }
                sb.AppendLine($"{inner.GetType().FullName}: {inner.Message}");
                sb.AppendLine(inner.StackTrace ?? "(no stack trace)");
                inner = inner.InnerException;
                depth++;
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildFullCrashReport(Exception ex)
        {
            var sb  = new StringBuilder();
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            sb.AppendLine($"Scalpel v{ver?.ToString(3)}");
            sb.AppendLine($"Time : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"OS   : {Environment.OSVersion}");
            sb.AppendLine();
            sb.Append(FormatExceptionChain(ex));
            return sb.ToString();
        }

        // ============================================================
        // App exit
        // ============================================================

        protected override void OnExit(ExitEventArgs e)
        {
            Scalpel.Services.Logger.Info("App", "app.exit", "Shutting down");
            Scalpel.Services.Logger.Shutdown();
            base.OnExit(e);
        }

        // ============================================================
        // Public surface used by MainWindow (portable badge / install)
        // ============================================================

        /// <summary>
        /// True when running from outside the installed location (i.e. portable mode).
        /// </summary>
        internal static bool IsPortable()
        {
            // A Store/MSIX build is never "portable": the package owns install state,
            // so the portable badge and the in-app installer must stay hidden.
            if (IsPackaged()) return false;

            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            return !string.Equals(currentExe, Installer.InstallExe, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Installs Scalpel, offers to set as default PDF handler, then relaunches
        /// from the installed location. Returns false if installation failed or was
        /// already installed from this path.
        /// </summary>
        internal static void InstallAndRelaunch(string? fileToOpen, bool wantDesktop)
        {
            // Packaged (Store/MSIX) builds never self-install; the package owns
            // install state. This path is already unreachable because IsPortable()
            // hides the badge, but guard defensively.
            if (IsPackaged()) return;

            DoInstall(wantDesktop);

            if (!IsDefaultPdfHandler())
            {
                var res = ScalpelDialog.Show(null,
                    "Make Scalpel your default PDF viewer?\n\n" +
                    "Windows only lets you change this yourself. Click \"Yes\" to open " +
                    "Default Apps settings, then:\n\n" +
                    "    1.  Search for  .pdf  (or scroll to it)\n" +
                    "    2.  Click the app currently shown (e.g. your browser)\n" +
                    "    3.  Pick Scalpel, then choose Set default\n\n" +
                    "Open Default Apps settings now?",
                    "Set Scalpel as default", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("ms-settings:defaultapps")
                            { UseShellExecute = true });
                    }
                    catch
                    {
                        // Fallback for older shells without the ms-settings: URI.
                        try { Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.DefaultPrograms")
                            { UseShellExecute = true }); } catch { }
                    }
                }
            }

            // Guard: if the install didn't actually produce the EXE, don't blindly relaunch
            // (that throws a fatal Win32Exception). Surface it and stay put instead.
            if (!File.Exists(Installer.InstallExe))
            {
                MessageBox.Show(
                    "Installation did not complete: the installed copy was not created. " +
                    "Scalpel will keep running from its current location.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var psi = new ProcessStartInfo(Installer.InstallExe) { UseShellExecute = true };
            if (fileToOpen != null)
                psi.Arguments = $"\"{fileToOpen}\"";
            Process.Start(psi);
            Application.Current.Shutdown();
        }

        // ============================================================
        // Registry helpers
        // ============================================================

        // ============================================================
        // Temp file tracking
        // ============================================================

        /// <summary>
        /// User-private temp directory for session working files (encrypted PDFs, etc.).
        /// %LOCALAPPDATA% is user-private and not indexed by Windows Search.
        /// </summary>
        internal static readonly string TempDir = Path.Combine(Installer.DataDir, "Temp");

        private static readonly List<string> _sessionTemps = [];

        /// <summary>
        /// Creates a tracked temp path of the form scalpel_&lt;tag&gt;_&lt;guid&gt;.pdf
        /// under %LOCALAPPDATA%\Scalpel\Temp\.
        /// All registered paths are deleted when CleanupSessionTemps() is called.
        /// </summary>
        internal static string MakeTempFile(string tag)
        {
            try { Directory.CreateDirectory(TempDir); } catch { }
            var path = Path.Combine(TempDir, $"scalpel_{tag}_{Guid.NewGuid():N}.pdf");
            lock (_sessionTemps) _sessionTemps.Add(path);
            return path;
        }

        /// <summary>Deletes all temp files registered this session (best-effort).</summary>
        internal static void CleanupSessionTemps()
        {
            lock (_sessionTemps)
            {
                foreach (var f in _sessionTemps)
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
                _sessionTemps.Clear();
            }
        }

        /// <summary>
        /// Deletes scalpel_*.pdf files left over from previous crashed sessions.
        /// Sweeps both the current TempDir and the legacy %TEMP% location.
        /// Locked files (still open by another instance) are silently skipped.
        /// </summary>
        internal static void CleanupStaleTemps()
        {
            // Current location
            try
            {
                if (Directory.Exists(TempDir))
                    foreach (var f in Directory.GetFiles(TempDir, "scalpel_*.pdf"))
                        try { File.Delete(f); } catch { }
            }
            catch { }

            // Legacy %TEMP% location — sweep once for users upgrading from older builds
            try
            {
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), "scalpel_*.pdf"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        internal static string? GetSetting(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Scalpel\Settings");
                return key?.GetValue(name) as string;
            }
            catch { return null; }
        }

        internal static void SetSetting(string name, string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Scalpel\Settings");
                key.SetValue(name, value);
            }
            catch { /* best-effort */ }
        }

        private static bool IsInstalled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Scalpel");
            if (key is null) return false;
            return key.GetValue("Installed") is int i && i == 1;
        }

        private static bool IsDefaultPdfHandler()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\FileAssociations\.pdf\UserChoice");
            return key?.GetValue("ProgId") is string progId &&
                   progId.Equals("Scalpel.pdf", StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // Launcher dialog
        // ============================================================

        /// <summary>
        /// Builds a button Style with a custom ControlTemplate so hover colours
        /// actually render (WPF's default template ignores Background changes on hover).
        /// </summary>
        private static Style MakeLauncherButtonStyle(
            SolidColorBrush normal, SolidColorBrush hover, SolidColorBrush fg)
        {
            var template = new ControlTemplate(typeof(Button));
            var border   = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty,              new Thickness(0, 6, 0, 6));
            border.AppendChild(cp);
            template.VisualTree = border;

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty,  normal));
            style.Setters.Add(new Setter(Button.ForegroundProperty,  fg));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.TemplateProperty,    template));
            style.Setters.Add(new Setter(Button.CursorProperty,      Cursors.Hand));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, hover));
            style.Triggers.Add(hoverTrigger);

            return style;
        }


        // ============================================================
        // Security — Authenticode verification + pdfium integrity
        // ============================================================

        // ── WinVerifyTrust P/Invoke ──────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_FILE_INFO
        {
            public uint   cbStruct;
            public IntPtr pcwszFilePath;   // LPCWSTR
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint   cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint   dwUIChoice;          // 2 = WTD_UI_NONE
            public uint   fdwRevocationChecks; // 0 = WTD_REVOKE_NONE
            public uint   dwUnionChoice;       // 1 = WTD_CHOICE_FILE
            public IntPtr pUnion;              // → WINTRUST_FILE_INFO
            public uint   dwStateAction;       // 0 = WTD_STATEACTION_IGNORE
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint   dwProvFlags;         // 0 = allow network fetch of intermediates
            public uint   dwUIContext;
            public IntPtr pSignatureSettings;
        }

        private static readonly Guid WTD_VERIFY_GENERIC =
            new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false,
                   CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(
            IntPtr hwnd, ref Guid pgActionID, IntPtr pWVTData);

        // ── Public helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Calls WinVerifyTrust to validate an Authenticode signature.
        /// Returns (Valid, SubjectCN, Thumbprint).
        /// Valid=false for unsigned, expired (past grace), or tampered files.
        /// </summary>
        internal static (bool Valid, string Subject, string Thumbprint)
            VerifyAuthenticode(string filePath)
        {
            var subject    = "(not signed)";
            var thumbprint = string.Empty;

            // Try to read cert info regardless of signature validity
            try
            {
                var raw  = X509Certificate.CreateFromSignedFile(filePath);
                var cert = new X509Certificate2(raw);
                subject    = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
                thumbprint = cert.Thumbprint ?? string.Empty;
            }
            catch { /* unsigned or unreadable */ }

            // Full chain + revocation check via WinVerifyTrust
            var pathPtr      = Marshal.StringToHGlobalUni(filePath);
            var fileInfoPtr  = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            var dataPtr      = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            try
            {
                Marshal.StructureToPtr(new WINTRUST_FILE_INFO
                {
                    cbStruct      = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = pathPtr
                }, fileInfoPtr, false);

                Marshal.StructureToPtr(new WINTRUST_DATA
                {
                    cbStruct      = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice    = 2,  // WTD_UI_NONE
                    dwUnionChoice = 1,  // WTD_CHOICE_FILE
                    pUnion        = fileInfoPtr,
                    dwProvFlags   = 0   // allow network fetch of intermediate certs
                }, dataPtr, false);

                var actionId = WTD_VERIFY_GENERIC;
                uint hr = WinVerifyTrust(IntPtr.Zero, ref actionId, dataPtr);
                return (hr == 0, subject, thumbprint);
            }
            finally
            {
                Marshal.FreeHGlobal(dataPtr);
                Marshal.FreeHGlobal(fileInfoPtr);
                Marshal.FreeHGlobal(pathPtr);
            }
        }

        /// <summary>
        /// Convenience wrapper: verify the currently running EXE.
        /// </summary>
        internal static (bool Valid, string Subject, string Thumbprint) GetExeSignerInfo()
        {
            try
            {
                return VerifyAuthenticode(Process.GetCurrentProcess().MainModule!.FileName);
            }
            catch
            {
                return (false, "(not signed)", string.Empty);
            }
        }

        /// <summary>SHA256 hex of the currently running EXE (for the About dialog).</summary>
        internal static string GetExeSha256()
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule!.FileName;
                using var sha = SHA256.Create();
                using var fs  = File.OpenRead(path);
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            }
            catch { return "(unavailable)"; }
        }

        // ── pdfium.dll integrity check ───────────────────────────────────────

        /// <summary>
        /// Finds the Costura-embedded pdfium resource, decompresses it in-memory,
        /// and compares its SHA256 to BuildInfo.PdfiumSha256.
        /// Returns false (and shows a message box) only on a confirmed mismatch.
        /// Fails-open if the check cannot complete (dev builds, missing resource, I/O error).
        /// </summary>
        private static bool CheckPdfiumIntegrity()
        {
            if (string.Equals(BuildInfo.PdfiumSha256, BuildInfo.PdfiumSha256Disabled, StringComparison.Ordinal))
                return true; // disabled for this build (dev / SkipSign)

            var asm = Assembly.GetExecutingAssembly();
            var resourceName = Array.Find(asm.GetManifestResourceNames(),
                n => n.IndexOf("pdfium", StringComparison.OrdinalIgnoreCase) >= 0
                     && n.EndsWith(".compressed", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
                return true; // not bundled via Costura (dev build running from bin/)

            try
            {
                string actual;
                using (var rs      = asm.GetManifestResourceStream(resourceName)!)
                using (var deflate = new DeflateStream(rs, CompressionMode.Decompress))
                using (var sha     = SHA256.Create())
                    actual = BitConverter.ToString(sha.ComputeHash(deflate)).Replace("-", "");

                if (!string.Equals(actual, BuildInfo.PdfiumSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "Security check failed: pdfium.dll integrity verification failed.\n\n" +
                        $"Expected: {BuildInfo.PdfiumSha256}\n" +
                        $"Actual  : {actual}\n\n" +
                        "The bundled PDF engine may have been tampered with. Scalpel will exit.",
                        $"{AppName} — Security", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                return true;
            }
            catch
            {
                return true; // fail-open: only block on confirmed mismatch
            }
        }

        // ============================================================
        // Installation
        // ============================================================

        private static void DoInstall(bool wantDesktop)
        {
            string src = Process.GetCurrentProcess().MainModule!.FileName;

            // ── Trust gate: refuse to install an unsigned or wrong-publisher EXE ──
            var (valid, _, _) = VerifyAuthenticode(src);
            if (!valid)
            {
                MessageBox.Show(
                    "Installation refused: the running EXE does not carry a valid Authenticode " +
                    "signature.\n\nOnly signed builds of Scalpel can be installed.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // ── Downgrade guard ────────────────────────────────────────────────────
            if (File.Exists(Installer.InstallExe))
            {
                var runVer  = FileVersionInfo.GetVersionInfo(src).FileVersion ?? "";
                var instVer = FileVersionInfo.GetVersionInfo(Installer.InstallExe).FileVersion ?? "";
                if (string.Compare(runVer, instVer, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    var res = MessageBox.Show(
                        $"You are about to install an older version ({runVer}) " +
                        $"over the currently installed version ({instVer}).\n\n" +
                        "Downgrade anyway?",
                        AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (res != MessageBoxResult.Yes) return;
                }
            }

            try
            {
                // Copy EXE to install location, plus a same-binary copy named uninstall.exe
                // (argument-less uninstaller — see Installer.UninstallExe).
                Directory.CreateDirectory(Installer.InstallDir);
                File.Copy(src, Installer.InstallExe, overwrite: true);
                try { File.Copy(src, Installer.UninstallExe, overwrite: true); } catch { }

                // Shortcuts
                Directory.CreateDirectory(Installer.StartMenuDir);
                CreateShortcut(Installer.StartMenuLnk, Installer.InstallExe);
                // Dedicated uninstall shortcut — a .lnk reliably preserves its arguments,
                // unlike the ARP UninstallString which Windows' Settings uninstall can strip.
                CreateShortcut(Installer.UninstallLnk, Installer.InstallExe, "/uninstall");
                if (wantDesktop)
                    CreateShortcut(Installer.DesktopLnk, Installer.InstallExe);

                // Installed marker
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Scalpel"))
                {
                    key.SetValue("Installed",    1);
                    key.SetValue("InstallPath",  Installer.InstallExe);
                    key.SetValue("Version",
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "");
                }

                // Add/Remove Programs entry
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Scalpel"))
                {
                    key.SetValue("DisplayName",          AppName);
                    key.SetValue("DisplayVersion",
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "");
                    key.SetValue("Publisher",            "Liraz Amir");
                    key.SetValue("InstallLocation",      Installer.InstallDir);
                    key.SetValue("DisplayIcon",          $"{Installer.InstallExe},0");
                    // Point at the argument-less uninstall.exe — Windows 11's Settings uninstall
                    // strips arguments from UninstallString, so "Scalpel.exe /uninstall" would just
                    // launch the app. A dedicated exe (no args) can't be mis-parsed.
                    key.SetValue("UninstallString",      $"\"{Installer.UninstallExe}\"");
                    // No QuietUninstallString — our uninstall is interactive (shows a confirm dialog).
                    key.SetValue("NoModify",             1);
                    key.SetValue("NoRepair",             1);
                }

                // Register as PDF file handler (per-user — no admin needed)
                RegisterFileHandler();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation failed:\n{ex.Message}", AppName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void RegisterFileHandler()
        {
            // ProgID definition
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Scalpel.pdf"))
                k.SetValue("", "PDF Document");

            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\Scalpel.pdf\DefaultIcon"))
                k.SetValue("", $"{Installer.InstallExe},0");

            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\Scalpel.pdf\shell\open\command"))
                k.SetValue("", $"\"{Installer.InstallExe}\" \"%1\"");

            // Application registration — REQUIRED for Scalpel to appear in the shell's
            // "Open with" / "Choose another app" list (and therefore be selectable as the
            // default). Without Applications\Scalpel.exe + SupportedTypes, the OpenWithProgids
            // hint alone is unreliable on Windows 10/11.
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\Applications\Scalpel.exe"))
                k.SetValue("FriendlyAppName", "Scalpel");
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\Applications\Scalpel.exe\DefaultIcon"))
                k.SetValue("", $"{Installer.InstallExe},0");
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\Applications\Scalpel.exe\shell\open\command"))
                k.SetValue("", $"\"{Installer.InstallExe}\" \"%1\"");
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\Applications\Scalpel.exe\SupportedTypes"))
                k.SetValue(".pdf", "");

            // Associate .pdf extension — adds Scalpel to the "Open with" list
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\.pdf\OpenWithProgids"))
                k.SetValue("Scalpel.pdf", new byte[0], RegistryValueKind.None);

            // RegisteredApplications capability (used by Default Programs UI)
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Scalpel\Capabilities"))
            {
                k.SetValue("ApplicationName",        AppName);
                k.SetValue("ApplicationDescription", "Lightweight PDF viewer and editor");
            }
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Scalpel\Capabilities\FileAssociations"))
                k.SetValue(".pdf", "Scalpel.pdf");

            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                k.SetValue(AppName, @"Software\Scalpel\Capabilities");

            // "Edit with Scalpel PDF" context-menu verb for ALL .pdf files, independent of the
            // default handler. Per-user (HKCU), no admin. On Windows 11 it appears under
            // "Show more options"; on Windows 10 on the main context menu. The verb subkey is
            // namespaced "Scalpel.edit" (not the bare "edit") to avoid colliding with a built-in
            // edit verb. Removed on uninstall via Installer.OwnedRegistryKeys.
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit"))
            {
                k.SetValue("", "Edit with Scalpel PDF");
                k.SetValue("Icon", $"{Installer.InstallExe},0");
            }
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\SystemFileAssociations\.pdf\shell\Scalpel.edit\command"))
                k.SetValue("", $"\"{Installer.InstallExe}\" /edit \"%1\"");

            // Tell the shell file associations have changed
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        private static void CreateShortcut(string lnkPath, string targetPath, string? arguments = null)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null) return;
                dynamic shell    = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                shortcut.TargetPath       = targetPath;
                if (!string.IsNullOrEmpty(arguments))
                    shortcut.Arguments = arguments;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Save();
            }
            catch { /* best-effort */ }
        }

    }
}
