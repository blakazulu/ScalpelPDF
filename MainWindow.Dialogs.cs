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
