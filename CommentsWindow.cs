using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scalpel.Services;

namespace Scalpel
{
    /// <summary>
    /// Reads and edits the comments a PDF carries.
    ///
    /// <para>Reviewers leave notes in a PDF and the person acting on them usually needs to answer
    /// back, correct a typo, or clear a note that has been dealt with. Doing that used to mean
    /// opening the file in another PDF reader; this window keeps it in Scalpel.</para>
    ///
    /// <para>Nothing is written until Save is pressed, and even then the edits land in the open
    /// document rather than on disk - the user still saves the file the usual way, so an edit here
    /// is as undoable as any other change.</para>
    /// </summary>
    internal sealed class CommentsWindow : Window
    {
        /// <summary>One comment plus whatever the user has done to it in this session.</summary>
        private sealed class Entry
        {
            public Entry(PdfComment comment)
            {
                Comment = comment;
                Text = comment.Contents;
                Author = comment.Author;
            }

            public PdfComment Comment { get; }
            public string Text { get; set; }
            public string Author { get; set; }
            public bool Deleted { get; set; }

            public bool TextChanged => !string.Equals(Text, Comment.Contents, StringComparison.Ordinal);
            public bool AuthorChanged => !string.Equals(Author, Comment.Author, StringComparison.Ordinal);
            public bool Changed => TextChanged || AuthorChanged;

            /// <summary>The one-line summary shown in the list.</summary>
            public string Label
            {
                get
                {
                    string first = (Text ?? string.Empty)
                        .Replace("\r", " ").Replace("\n", " ").Trim();
                    if (first.Length > 60) first = first.Substring(0, 60) + "...";
                    if (first.Length == 0) first = "(no text)";

                    string who = string.IsNullOrWhiteSpace(Author) ? Comment.Kind : Author;
                    string mark = Deleted ? "[deleted] " : Changed ? "[edited] " : "";
                    return $"{mark}p{Comment.PageNumber}  {who}: {first}";
                }
            }
        }

        private readonly List<Entry> _entries;
        private readonly ListBox _list = new();
        private readonly TextBox _author = new();
        private readonly TextBox _text = new();
        private readonly Button _deleteBtn = new();
        private readonly Func<string, string> _loc;
        private bool _loading;

        /// <summary>The edits the user accepted, empty unless the dialog returned true.</summary>
        public IReadOnlyList<(PdfComment Comment, string Text, string Author, bool Deleted)> Result
        { get; private set; } = [];

        public CommentsWindow(Window owner, IReadOnlyList<PdfComment> comments, Func<string, string> loc)
        {
            _loc = loc;
            _entries = comments.Select(c => new Entry(c)).ToList();

            Owner = owner;
            Title = loc("Str_Tool_Comments");
            Width = 720;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("BgModal");
            FontFamily = (FontFamily)Application.Current.FindResource("FontUI");

            var fgPrimary = (Brush)Application.Current.FindResource("TextPrimary");
            var fgDim = (Brush)Application.Current.FindResource("TextSecondary");
            var bgControl = (Brush)Application.Current.FindResource("BgControl");
            var border = (Brush)Application.Current.FindResource("BorderDim");

            var root = new DockPanel { Margin = new Thickness(14) };

            var hint = new TextBlock
            {
                Text = loc("Str_Cmt_Hint"),
                Foreground = fgDim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            DockPanel.SetDock(hint, Dock.Top);
            root.Children.Add(hint);

            // Buttons along the bottom.
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var cancel = new Button
            {
                Content = loc("Str_Ocr_Progress_Cancel"),
                Style = (Style)Application.Current.FindResource("StudioToolButton"),
                MinWidth = 96,
                Margin = new Thickness(0, 0, 8, 0),
            };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            var save = new Button
            {
                Content = loc("Str_Cmt_Save"),
                Style = (Style)Application.Current.FindResource("StudioPrimaryButton"),
                MinWidth = 120,
            };
            save.Click += (_, _) => Accept();
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Left: the list of comments. Right: the selected one, editable.
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _list.Background = bgControl;
            _list.Foreground = fgPrimary;
            _list.BorderBrush = border;
            _list.BorderThickness = new Thickness(1);
            _list.SelectionChanged += (_, _) => LoadSelected();
            Grid.SetColumn(_list, 0);
            grid.Children.Add(_list);

            var right = new DockPanel();
            Grid.SetColumn(right, 2);

            var authorLabel = new TextBlock
            {
                Text = loc("Str_Cmt_Author"), Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4),
            };
            DockPanel.SetDock(authorLabel, Dock.Top);
            right.Children.Add(authorLabel);

            _author.Background = bgControl;
            _author.Foreground = fgPrimary;
            _author.BorderBrush = border;
            _author.BorderThickness = new Thickness(1);
            _author.Padding = new Thickness(6, 4, 6, 4);
            _author.Margin = new Thickness(0, 0, 0, 10);
            _author.TextChanged += (_, _) => CaptureEdit();
            DockPanel.SetDock(_author, Dock.Top);
            right.Children.Add(_author);

            var textLabel = new TextBlock
            {
                Text = loc("Str_Cmt_Text"), Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4),
            };
            DockPanel.SetDock(textLabel, Dock.Top);
            right.Children.Add(textLabel);

            _deleteBtn.Content = loc("Str_Cmt_Delete");
            _deleteBtn.Style = (Style)Application.Current.FindResource("StudioDangerButton");
            _deleteBtn.HorizontalAlignment = HorizontalAlignment.Left;
            _deleteBtn.MinWidth = 140;
            _deleteBtn.Margin = new Thickness(0, 10, 0, 0);
            _deleteBtn.Click += (_, _) => ToggleDelete();
            DockPanel.SetDock(_deleteBtn, Dock.Bottom);
            right.Children.Add(_deleteBtn);

            _text.Background = bgControl;
            _text.Foreground = fgPrimary;
            _text.BorderBrush = border;
            _text.BorderThickness = new Thickness(1);
            _text.Padding = new Thickness(6, 4, 6, 4);
            _text.AcceptsReturn = true;
            _text.TextWrapping = TextWrapping.Wrap;
            _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _text.TextChanged += (_, _) => CaptureEdit();
            right.Children.Add(_text);

            grid.Children.Add(right);
            root.Children.Add(grid);
            Content = root;

            RefreshList();
            if (_entries.Count > 0) _list.SelectedIndex = 0;
        }

        private Entry? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _entries.Count
            ? _entries[_list.SelectedIndex] : null;

        /// <summary>Repaints the list labels, keeping the current selection.</summary>
        private void RefreshList()
        {
            int keep = _list.SelectedIndex;
            _loading = true;
            _list.Items.Clear();
            foreach (var e in _entries) _list.Items.Add(e.Label);
            if (keep >= 0 && keep < _list.Items.Count) _list.SelectedIndex = keep;
            _loading = false;
        }

        private void LoadSelected()
        {
            var entry = Selected;
            _loading = true;
            _author.Text = entry?.Author ?? string.Empty;
            _text.Text = entry?.Text ?? string.Empty;
            _loading = false;

            bool has = entry is not null;
            _author.IsEnabled = has && !(entry!.Deleted);
            _text.IsEnabled = has && !(entry!.Deleted);
            _deleteBtn.IsEnabled = has;
            _deleteBtn.Content = entry?.Deleted == true ? _loc("Str_Cmt_Restore") : _loc("Str_Cmt_Delete");
        }

        /// <summary>Copies the edit boxes into the selected entry as the user types.</summary>
        private void CaptureEdit()
        {
            if (_loading) return;
            var entry = Selected;
            if (entry is null || entry.Deleted) return;

            entry.Text = _text.Text;
            entry.Author = _author.Text;

            // Keep the list label in step without stealing focus from the box being typed in.
            int index = _list.SelectedIndex;
            if (index >= 0 && index < _list.Items.Count) _list.Items[index] = entry.Label;
        }

        private void ToggleDelete()
        {
            var entry = Selected;
            if (entry is null) return;
            entry.Deleted = !entry.Deleted;
            int index = _list.SelectedIndex;
            if (index >= 0 && index < _list.Items.Count) _list.Items[index] = entry.Label;
            LoadSelected();
        }

        private void Accept()
        {
            Result = _entries
                .Where(e => e.Deleted || e.Changed)
                .Select(e => (e.Comment, e.Text, e.Author, e.Deleted))
                .ToList();
            DialogResult = true;
            Close();
        }
    }
}
