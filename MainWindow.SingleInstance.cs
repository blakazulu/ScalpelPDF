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
                var (file, edit) = SingleInstanceProtocol.PickLaunchTarget(args, File.Exists);
                Logger.Info("App", "instance.forwarded", "Launch forwarded from a second instance",
                    new { file, edit, argc = args.Length });

                if (file is not null && !PathEq(file, _originalFile))
                {
                    // Same guard SwitchToTab applies: never silently discard unsaved edits.
                    if (_isDirty)
                    {
                        BringToFront();
                        var res = ScalpelDialog.Show(this, Loc("Str_Dlg_UnsavedClose"), "Scalpel",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (res != MessageBoxResult.Yes) return;
                    }
                    OpenFile(file); // FinishOpenFile adds the tab and marks it active
                    if (edit && _doc is not null) SetMode(AppMode.Edit);
                }
                else if (file is not null && edit && _doc is not null)
                {
                    SetMode(AppMode.Edit);
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
