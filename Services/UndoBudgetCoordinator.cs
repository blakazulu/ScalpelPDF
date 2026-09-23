using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>
    /// One memory budget for the undo history of every open tab. Each document undo holds a
    /// whole copy of the PDF, so ten tabs at the per-tab cap would pin gigabytes. Trims oldest
    /// entries from the least recently used tab first; the newest entry of the last stack (the
    /// active tab) is always kept.
    /// </summary>
    public static class UndoBudgetCoordinator
    {
        public static long TrimAcross<T>(IReadOnlyList<Stack<T>> stacksLeastRecentFirst,
            Func<T, long> sizeOf, long maxTotalBytes)
        {
            long total = stacksLeastRecentFirst.Sum(s => s.Sum(e => Math.Max(0, sizeOf(e))));
            for (int i = 0; i < stacksLeastRecentFirst.Count && total > maxTotalBytes; i++)
            {
                var stack = stacksLeastRecentFirst[i];
                bool keepNewest = i == stacksLeastRecentFirst.Count - 1;
                var newestFirst = stack.ToList();
                while (total > maxTotalBytes && newestFirst.Count > (keepNewest ? 1 : 0))
                {
                    total -= Math.Max(0, sizeOf(newestFirst[newestFirst.Count - 1]));
                    newestFirst.RemoveAt(newestFirst.Count - 1);
                }
                stack.Clear();
                for (int k = newestFirst.Count - 1; k >= 0; k--) stack.Push(newestFirst[k]);
            }
            return total;
        }
    }
}
