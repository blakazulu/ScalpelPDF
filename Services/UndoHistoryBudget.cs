using System;
using System.Collections.Generic;

namespace Scalpel.Services
{
    /// <summary>Keeps newest-first undo histories within both a depth and memory budget.</summary>
    public static class UndoHistoryBudget
    {
        /// <summary>Pushes <paramref name="entry"/> and then trims the oldest entries until the
        /// stack holds at most <paramref name="maximumEntries"/> items totalling at most
        /// <paramref name="maximumBytes"/> (as reported by <paramref name="sizeOf"/>). The newest
        /// entry is always kept, even when it alone exceeds the byte budget.</summary>
        public static void PushBounded<T>(Stack<T> history, T entry,
            Func<T, long> sizeOf, int maximumEntries, long maximumBytes)
        {
            if (history is null) throw new ArgumentNullException(nameof(history));
            if (sizeOf is null) throw new ArgumentNullException(nameof(sizeOf));
            if (maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));

            history.Push(entry);
            T[] newestFirst = [.. history];
            var retained = new List<T>(Math.Min(newestFirst.Length, maximumEntries));
            long retainedBytes = 0;
            foreach (T candidate in newestFirst)
            {
                long candidateBytes = Math.Max(0, sizeOf(candidate));
                if (retained.Count > 0 &&
                    (retained.Count >= maximumEntries || candidateBytes > maximumBytes - retainedBytes))
                    break;
                retained.Add(candidate);
                retainedBytes = Math.Min(maximumBytes, retainedBytes + candidateBytes);
            }

            history.Clear();
            for (int index = retained.Count - 1; index >= 0; index--)
                history.Push(retained[index]);
        }
    }
}
