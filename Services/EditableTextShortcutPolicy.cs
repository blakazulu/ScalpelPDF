using System.Windows.Input;

namespace Scalpel.Services
{
    /// <summary>Separates text-editing gestures from window-level application shortcuts.</summary>
    public static class EditableTextShortcutPolicy
    {
        /// <summary>True when a keystroke received while a text box has focus should stay in the
        /// text box (typing, selection, clipboard, caret movement) rather than bubble up to the
        /// window's shortcut handling. <paramref name="systemKey"/> resolves <see cref="Key.System"/>
        /// (the way WPF reports F10 and Alt combinations).</summary>
        public static bool KeepInTextBox(Key key, ModifierKeys modifiers,
            Key systemKey = Key.None)
        {
            Key effectiveKey = key == Key.System ? systemKey : key;
            if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
                return false;
            if (effectiveKey is >= Key.F1 and <= Key.F24)
                return false;
            if ((modifiers & ModifierKeys.Control) == 0)
                return true;

            return effectiveKey is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y
                or Key.Back or Key.Delete or Key.Insert
                or Key.Left or Key.Right or Key.Up or Key.Down
                or Key.Home or Key.End or Key.Space;
        }
    }
}
