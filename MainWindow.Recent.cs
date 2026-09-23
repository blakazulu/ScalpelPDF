using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Scalpel
{
    public partial class MainWindow
    {
        // View-model row for the empty-state recent list.
        private sealed record RecentItemVm(string Path, string FileName, string Dir);

        // Returns the recent files that still exist on disk (most-recent first).
        private List<string> ExistingRecentFiles() =>
            App.GetRecentFiles().Where(p => { try { return File.Exists(p); } catch { return false; } }).ToList();

        // Rebuilds the empty-state recent list; hides the header/list when there is nothing to show.
        private void PopulateRecentList()
        {
            if (RecentList is null || RecentHeader is null) return;
            var items = ExistingRecentFiles()
                .Select(p => new RecentItemVm(p, Path.GetFileName(p), TrimDir(Path.GetDirectoryName(p) ?? "")))
                .ToList();
            RecentList.ItemsSource = items;
            var vis = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentList.Visibility = vis;
            RecentHeader.Visibility = vis;
        }

        // Shortens a directory to its last segment for compact display ("...\Docs").
        private static string TrimDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? dir : name;
        }

        // Opens a recent file; if it has vanished, toasts and drops it from the list.
        private void OpenRecent(string path)
        {
            if (File.Exists(path)) { OpenInTab(path); return; }
            ShowToast(Loc("Str_Recent_NotFound"));
            App.RemoveRecentFile(path);
            PopulateRecentList();
        }

        private void RecentRow_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // do not also trigger the DropZone browse handler
            if (sender is FrameworkElement fe && fe.Tag is string path) OpenRecent(path);
        }

        private void RecentRemove_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement fe && fe.Tag is string path)
            {
                App.RemoveRecentFile(path);
                PopulateRecentList();
            }
        }

        // Rebuilds the dynamic "recent files" tail of the Open context menu on each open. The first
        // three items (Open / New / Close) are static; everything we add is tagged "recent" so it
        // can be cleared and rebuilt without disturbing them.
        private void OpenContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (OpenContextMenu is null) return;
            // Remove previously-added dynamic items.
            for (int i = OpenContextMenu.Items.Count - 1; i >= 0; i--)
                if (OpenContextMenu.Items[i] is FrameworkElement fe && (fe.Tag as string) == "recent")
                    OpenContextMenu.Items.RemoveAt(i);

            var files = ExistingRecentFiles();
            if (files.Count == 0) return;

            OpenContextMenu.Items.Add(new Separator { Tag = "recent" });
            foreach (var path in files)
            {
                var item = new MenuItem
                {
                    Header = System.IO.Path.GetFileName(path),
                    ToolTip = path,
                    Tag = "recent",
                };
                string captured = path;
                item.Click += (_, _) => OpenRecent(captured);
                OpenContextMenu.Items.Add(item);
            }
            OpenContextMenu.Items.Add(new Separator { Tag = "recent" });
            var clear = new MenuItem { Header = Loc("Str_Recent_Clear"), Tag = "recent" };
            clear.Click += (_, _) => { App.ClearRecentFiles(); PopulateRecentList(); };
            OpenContextMenu.Items.Add(clear);
        }
    }
}
