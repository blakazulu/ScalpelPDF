using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Scalpel.Services
{
    /// <summary>
    /// Naming and sweep-selection rules for Scalpel's per-session temp PDFs (WPF-free, pure).
    ///
    /// Every temp file carries the PID of the instance that created it
    /// (<c>scalpel_p{pid}_{tag}_{guid}.pdf</c>). The startup sweep must only delete files whose
    /// owner is no longer running: Scalpel has several windows open at once when the user
    /// double-clicks a second PDF, and a sweep that blindly removed every <c>scalpel_*.pdf</c>
    /// destroyed the OTHER window's working copy (decrypted / repaired / network-copied
    /// documents all live in temp), which surfaced as "could not open file" mid-session.
    /// </summary>
    public static class TempSweep
    {
        public const string Pattern = "scalpel_*.pdf";

        private static readonly Regex OwnerRx = new(@"^scalpel_p(\d+)_", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string MakeName(int pid, string tag, Guid id) => $"scalpel_p{pid}_{tag}_{id:N}.pdf";

        /// <summary>PID embedded in a temp file name, or null for legacy names without one.</summary>
        public static int? OwnerPid(string pathOrName)
        {
            try
            {
                var m = OwnerRx.Match(Path.GetFileName(pathOrName));
                return m.Success && int.TryParse(m.Groups[1].Value, out int pid) ? pid : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// The subset of <paramref name="files"/> that is safe to delete: legacy (unowned) files,
        /// files owned by a process that is no longer alive, and files carrying our own PID
        /// (we cannot have created any before startup, so those are PID-reuse leftovers).
        /// </summary>
        public static IEnumerable<string> Sweepable(IEnumerable<string> files, int selfPid, Func<int, bool> isProcessAlive)
        {
            foreach (var f in files)
            {
                int? owner = OwnerPid(f);
                if (owner is null || owner == selfPid) { yield return f; continue; }
                bool alive;
                try { alive = isProcessAlive(owner.Value); } catch { alive = false; }
                if (!alive) yield return f;
            }
        }
    }
}
