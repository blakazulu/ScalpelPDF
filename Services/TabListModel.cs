using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>
    /// The ordered list of open document tabs and which one is active. WPF-free so the ordering
    /// rules (who becomes active after a close, cycling, number jumps) are unit-tested. Identity
    /// is the tab object, never its path, so Save As and untitled documents need no special case.
    /// </summary>
    public sealed class TabListModel<T> where T : class
    {
        private readonly List<T> _items = [];

        public IReadOnlyList<T> Items => _items;
        public T? Active { get; private set; }
        public int Count => _items.Count;

        public int IndexOf(T item) => _items.FindIndex(i => ReferenceEquals(i, item));

        public void Add(T item, bool activate = true)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (IndexOf(item) < 0) _items.Add(item);
            if (activate || Active is null) Active = item;
        }

        public bool Activate(T item)
        {
            if (IndexOf(item) < 0) return false;
            Active = item;
            return true;
        }

        /// <summary>
        /// Removes <paramref name="item"/> and returns the tab that is active afterwards: the right
        /// neighbour of a closed active tab, else its left neighbour, else null. Removing a tab that
        /// is not in the list (a stale or repeated close request) changes nothing.
        /// </summary>
        public T? Remove(T item)
        {
            int idx = IndexOf(item);
            if (idx < 0) return Active;
            _items.RemoveAt(idx);
            if (!ReferenceEquals(Active, item)) return Active;
            Active = _items.Count == 0 ? null : _items[Math.Min(idx, _items.Count - 1)];
            return Active;
        }

        public void Move(int from, int to)
        {
            if (from < 0 || from >= _items.Count) return;
            to = Math.Max(0, Math.Min(_items.Count - 1, to));
            if (from == to) return;
            var item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, item);
        }

        /// <summary>The tab Ctrl+Tab (forward) or Ctrl+Shift+Tab would go to. Does not activate it.</summary>
        public T? Cycle(bool forward)
        {
            if (_items.Count == 0) return null;
            int idx = Active is null ? 0 : Math.Max(0, IndexOf(Active));
            int next = forward ? (idx + 1) % _items.Count : (idx - 1 + _items.Count) % _items.Count;
            return _items[next];
        }

        /// <summary>1..8 pick that tab; 9 picks the last tab (Chrome's rule). Anything else is null.</summary>
        public T? ByNumber(int n)
        {
            if (n < 1 || n > 9 || _items.Count == 0) return null;
            if (n == 9) return _items[_items.Count - 1];
            return n - 1 < _items.Count ? _items[n - 1] : null;
        }

        public IReadOnlyList<T> OthersThan(T keep) =>
            _items.Where(i => !ReferenceEquals(i, keep)).ToList();

        public IReadOnlyList<T> RightOf(T item)
        {
            int idx = IndexOf(item);
            return idx < 0 ? [] : _items.Skip(idx + 1).ToList();
        }

        public T? FindByPath(string path, Func<T, string?> pathOf) =>
            _items.FirstOrDefault(i => DocumentPath.Same(pathOf(i), path));
    }

    public static class TabCloseOrder
    {
        /// <summary>Dirty tabs in the order the window-close flow asks about them: the tab being
        /// looked at first, then the rest left to right.</summary>
        public static IReadOnlyList<T> DirtyFirstActive<T>(IReadOnlyList<T> tabs, T? active, Func<T, bool> isDirty)
            where T : class
        {
            var result = new List<T>();
            if (active is not null && isDirty(active)) result.Add(active);
            foreach (var t in tabs)
                if (!ReferenceEquals(t, active) && isDirty(t)) result.Add(t);
            return result;
        }
    }
}
