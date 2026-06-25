using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        private const string KeyUpdateEnabled = "UpdateCheckEnabled";
        private const string KeyUpdateLastCheck = "LastUpdateCheck";
        private const string KeyUpdateDismissed = "UpdateDismissedVersion";

        private UpdateInfo? _pendingUpdate;

        /// <summary>One-time opt-in prompt the first time the setting is unset. Stores the choice.</summary>
        private void EnsureUpdateOptIn()
        {
            try
            {
                if (App.GetSetting(KeyUpdateEnabled) != null) return; // already answered
                var res = ScalpelDialog.Show(this,
                    Loc("Str_Update_OptIn_Body"),
                    Loc("Str_Update_OptIn_Title"),
                    MessageBoxButton.YesNo);
                App.SetSetting(KeyUpdateEnabled, res == MessageBoxResult.Yes ? "1" : "0");
            }
            catch { /* never block startup on the opt-in */ }
        }

        /// <summary>If enabled and due, fetch version.json and show the overlay for a newer version.</summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                bool enabled = App.GetSetting(KeyUpdateEnabled) == "1";
                DateTime? last = null;
                if (DateTime.TryParse(App.GetSetting(KeyUpdateLastCheck), out var parsed))
                    last = parsed.ToUniversalTime();
                if (!UpdateService.ShouldCheckNow(enabled, last, DateTime.UtcNow)) return;

                var info = await UpdateService.CheckAsync(UpdateService.VersionJsonUrl).ConfigureAwait(true);
                App.SetSetting(KeyUpdateLastCheck, DateTime.UtcNow.ToString("o")); // after every attempt
                if (info is null) return;

                var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                              ?? new Version(0, 0, 0);
                if (!UpdateService.IsNewer(info.Version, current)) return;
                if (App.GetSetting(KeyUpdateDismissed) == info.Version) return; // dismissed this version

                ShowUpdateOverlay(info, current);
            }
            catch { /* offline / any failure: silent */ }
        }

        private void ShowUpdateOverlay(UpdateInfo info, Version current)
        {
            _pendingUpdate = info;
            UpdateBodyText.Text =
                $"{Loc("Str_Update_Body_Prefix")}{info.Version}{Loc("Str_Update_Body_Suffix")} " +
                $"(v{current.Major}.{current.Minor}.{Math.Max(0, current.Build)})";

            UpdateNotesPanel.Children.Clear();
            foreach (var note in info.Notes)
            {
                UpdateNotesPanel.Children.Add(new TextBlock
                {
                    Text = "• " + note,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                });
            }
            UpdateOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateGet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_pendingUpdate is { } info)
                {
                    string url = UpdateService.ResolveUrl(info, App.IsPackaged());
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch { /* ignore launch failure */ }
            UpdateOverlay.Visibility = Visibility.Collapsed;
        }

        private void UpdateLater_Click(object sender, RoutedEventArgs e)
        {
            try { if (_pendingUpdate is { } info) App.SetSetting(KeyUpdateDismissed, info.Version); }
            catch { }
            UpdateOverlay.Visibility = Visibility.Collapsed;
        }

        private void UpdateOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => UpdateOverlay.Visibility = Visibility.Collapsed;

        private void UpdateOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => e.Handled = true;

        /// <summary>Reflects the stored setting onto the toggle; call when opening Settings.</summary>
        private void SyncUpdateToggle()
        {
            if (UpdateCheckToggle != null)
                UpdateCheckToggle.IsChecked = App.GetSetting(KeyUpdateEnabled) == "1";
        }

        private void UpdateCheckToggle_Click(object sender, RoutedEventArgs e)
        {
            App.SetSetting(KeyUpdateEnabled, UpdateCheckToggle.IsChecked == true ? "1" : "0");
        }
    }
}
