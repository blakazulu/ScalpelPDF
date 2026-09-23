using System;
using System.IO;
using System.Windows;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Single-instance: launches forwarded from a second Scalpel process
        // (file-association double-click, "Edit with Scalpel PDF", Store tile)
        // land here instead of spawning another window. See Services/SingleInstance.cs.
        // ============================================================

        /// <summary>Runs on the UI thread with the forwarded process's command-line args.</summary>
        internal void HandleForwardedLaunch(string[] args)
        {
            try
            {
                var (files, edit) = SingleInstanceProtocol.PickLaunchTargets(args, File.Exists);
                Logger.Info("App", "instance.forwarded", "Launch forwarded from a second instance",
                    new { files = files.Count, edit, argc = args.Length });

                if (files.Count > 0)
                {
                    // Opens each in its own tab (or switches to one already open), so no document
                    // being edited is ever replaced and nothing needs a discard prompt. A file that
                    // arrives while a modal is up on this window (e.g. a Save prompt) queues through
                    // the normal pending-open mechanism; OpenManyInTabs lands on the first file and
                    // applies /edit once the whole batch has actually opened, even then.
                    BringToFront();
                    OpenManyInTabs(files, edit);
                }

                BringToFront();
            }
            catch (Exception ex)
            {
                Logger.Error("App", "instance.forward.fail", "Could not apply forwarded launch", ex);
            }
        }

        private void BringToFront()
        {
            try
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (!IsVisible) Show();
                Activate();
                // Windows only honors Activate() when we hold foreground rights (the forwarding
                // process granted them via AllowSetForegroundWindow); the Topmost flick is the
                // standard fallback that still raises the window when it does not.
                Topmost = true;
                Topmost = false;
                Focus();
            }
            catch { }
        }
    }
}
