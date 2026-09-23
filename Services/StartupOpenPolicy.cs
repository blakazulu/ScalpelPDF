namespace Scalpel.Services
{
    /// <summary>What the main window opens once it has loaded.</summary>
    public enum StartupOpen
    {
        /// <summary>Open the files named on the command line.</summary>
        CommandLine,
        /// <summary>Restore the tabs open at the last exit (or the older single "last file").</summary>
        Restore,
        /// <summary>Open nothing: a document is already open or queued.</summary>
        Nothing,
    }

    /// <summary>
    /// Startup open rules for document tabs (R18). WPF-free so the precedence is unit-tested.
    /// </summary>
    public static class StartupOpenPolicy
    {
        /// <summary>
        /// Command-line files always open. Otherwise the last session is restored, unless a
        /// document is already open or queued when the window loads - a launch forwarded from a
        /// second Scalpel process can arrive before <c>Loaded</c>, and like a command-line file it
        /// takes precedence over the restore (which would otherwise have to share, or overwrite,
        /// the session that already holds it).
        /// </summary>
        /// <param name="openInProgress">
        /// True while a forwarded open is still in flight when <c>Loaded</c> runs - e.g. a
        /// password/repair prompt, or the "more than 20 files" confirmation in
        /// <c>OpenManyInTabs</c>, is showing a modal dialog. A modal pumps a nested message loop,
        /// so <c>Loaded</c> can run *inside it*, before the forwarded open has written anything to
        /// the session or to the pending-opens queue: at that instant
        /// <paramref name="documentAlreadyOpen"/> is still false and <paramref name="queuedOpens"/>
        /// is still 0, so without this flag the policy would wrongly say "restore" and
        /// <c>TryRestoreOpenTabs</c> would stamp a restored path onto the very session the
        /// forwarded open is still populating.
        /// </param>
        public static StartupOpen Decide(int commandLineFiles, bool documentAlreadyOpen, int queuedOpens, bool openInProgress = false)
        {
            if (commandLineFiles > 0) return StartupOpen.CommandLine;
            if (documentAlreadyOpen || queuedOpens > 0 || openInProgress) return StartupOpen.Nothing;
            return StartupOpen.Restore;
        }

        /// <summary>
        /// True only for the empty start state - no document, not a deferred (restored, not yet
        /// loaded) tab, nothing unsaved - which an open, New or restore fills instead of adding a
        /// second tab next to it.
        /// </summary>
        public static bool IsReusablePlaceholder(bool hasDocument, bool isDeferred, bool isDirty) =>
            !hasDocument && !isDeferred && !isDirty;
    }
}
