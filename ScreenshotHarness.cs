#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        private const int ShotW = 1920, ShotH = 1080;

        private sealed record Shot(string FileName, AppMode Mode, Theme Theme, Accent Accent, bool Seed);

        private static readonly IReadOnlyList<Shot> ShotRecipe =
        [
            new Shot("01-view-dark.png",    AppMode.View,  Theme.Dark,         Accent.Amber, false),
            new Shot("02-edit-light.png",   AppMode.Edit,  Theme.Light,        Accent.Amber, false),
            new Shot("03-pages-dark.png",   AppMode.Pages, Theme.Dark,         Accent.Cyan,  false),
            new Shot("04-sign-dark.png",    AppMode.Sign,  Theme.Dark,         Accent.Amber, false),
            new Shot("05-highcontrast.png", AppMode.View,  Theme.HighContrast, Accent.Amber, false),
            new Shot("06-view-green.png",   AppMode.View,  Theme.Dark,         Accent.Green, false),
        ];

        /// <summary>
        /// Dev-only: renders the store screenshot set, then exits. Triggered by `/shoot`.
        /// Never compiled into release builds (whole file is #if DEBUG).
        /// </summary>
        internal async void RunScreenshotHarness()
        {
            try
            {
                string outDir = LocateScreenshotsDir();
                Directory.CreateDirectory(outDir);
                string sample = SampleDocument.Generate(
                    Path.Combine(Path.GetTempPath(), "scalpel_sample_shot.pdf"));

                // Render off-screen at a fixed size -- monitor size / DPI are irrelevant.
                // Do NOT set ShowInTaskbar = false after the window is shown -- WPF forces a
                // native HWND recreation which can disrupt rendering and the dispatcher pump.
                WindowState = WindowState.Normal;
                Left = -10000; Top = -10000;
                Width = ShotW; Height = ShotH;

                foreach (var shot in ShotRecipe)
                {
                    ThemeManager.ApplyTheme(shot.Theme);
                    ThemeManager.ApplyAccent(shot.Accent);
                    OpenFile(sample);
                    SetMode(shot.Mode);
                    if (shot.Seed) SeedShotAnnotations(shot.Mode);   // no-op unless Task 5 lands
                    await SettleAsync();
                    CaptureContent(Path.Combine(outDir, shot.FileName));
                }

                try { File.Delete(sample); } catch { }
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("screenshot harness failed: " + ex); } catch { }
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        // Until Task 5, seeding is a no-op so shots 2 & 4 show the mode toolbar over the doc.
        partial void SeedShotAnnotations(AppMode mode);

        private async Task SettleAsync()
        {
            UpdateLayout();
            // Fix: avoid ContextIdle/ApplicationIdle -- the DispatcherTimer (_rerenderTimer)
            // and other Background-priority work keeps the queue busy, so idle-priority
            // continuations can starve forever. Instead:
            //   1. Drain through Background priority (fires after all pending renders,
            //      matching FinishOpenFile's own scheduling priority).
            //   2. Yield for 600 ms so async page-bitmap decode/display completes.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(600);
            // One more background drain to pick up any layout triggered by the bitmaps arriving.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(200);
        }

        private void CaptureContent(string path)
        {
            var root = (FrameworkElement)Content;
            root.Measure(new Size(ShotW, ShotH));
            root.Arrange(new Rect(0, 0, ShotW, ShotH));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(ShotW, ShotH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        private static string LocateScreenshotsDir()
        {
            // Walk up from the exe dir (bin\Debug\net48) to the repo root that holds 'screenshots'.
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && dir is not null; i++)
            {
                string candidate = Path.Combine(dir, "screenshots");
                if (Directory.Exists(candidate)) return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
        }
    }
}
#endif
