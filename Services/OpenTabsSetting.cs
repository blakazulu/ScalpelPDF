using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>Registry value for the tabs open at exit: "activeIndex|path|path|...".</summary>
    public static class OpenTabsSetting
    {
        public static string Serialize(IReadOnlyList<string> paths, int activeIndex) =>
            string.Join("|", new[] { activeIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) }.Concat(paths));

        public static (List<string> paths, int activeIndex) Parse(string? value, Func<string, bool> keep)
        {
            var none = (new List<string>(), -1);
            if (string.IsNullOrEmpty(value)) return none;
            var parts = value!.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int active)) return none;

            var kept = new List<string>();
            int newActive = -1;
            for (int i = 1; i < parts.Length; i++)
            {
                bool ok = false;
                try { ok = parts[i].Length > 0 && keep(parts[i]); } catch { }
                if (!ok) continue;
                if (i - 1 == active) newActive = kept.Count;
                kept.Add(parts[i]);
            }
            if (kept.Count == 0) return none;
            return (kept, newActive < 0 ? 0 : newActive);
        }
    }
}
