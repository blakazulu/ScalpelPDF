namespace Scalpel.Services
{
    /// <summary>
    /// Generation counter for work that is queued to run later (a dispatcher callback, a
    /// scroll restore after layout). Each <see cref="Begin"/> hands out a token; the queued
    /// action checks <see cref="IsCurrent"/> before acting so a newer request or a
    /// <see cref="Cancel"/> silently supersedes anything still pending.
    /// </summary>
    public sealed class DeferredActionGate
    {
        private int _generation;

        /// <summary>Starts a new deferred action and returns its token.</summary>
        public int Begin() => unchecked(++_generation);

        /// <summary>Invalidates every token handed out so far.</summary>
        public void Cancel()
        {
            unchecked { _generation++; }
        }

        /// <summary>True when <paramref name="generation"/> is the most recent token.</summary>
        public bool IsCurrent(int generation) => generation == _generation;
    }
}
