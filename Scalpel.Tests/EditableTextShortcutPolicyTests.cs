using System.Windows.Input;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class EditableTextShortcutPolicyTests
{
    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.C)]
    [InlineData(Key.V)]
    [InlineData(Key.X)]
    [InlineData(Key.Z)]
    [InlineData(Key.Y)]
    [InlineData(Key.Left)]
    [InlineData(Key.Delete)]
    public void StandardControlTextGesturesStayInTextBox(Key key)
    {
        Assert.True(EditableTextShortcutPolicy.KeepInTextBox(
            key, ModifierKeys.Control));
    }

    [Theory]
    [InlineData(Key.S)]
    [InlineData(Key.F)]
    [InlineData(Key.P)]
    [InlineData(Key.O)]
    [InlineData(Key.W)]
    public void ApplicationControlShortcutsReachWindow(Key key)
    {
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            key, ModifierKeys.Control));
    }

    [Fact]
    public void OrdinaryTypingAndSelectionStayInTextBox()
    {
        Assert.True(EditableTextShortcutPolicy.KeepInTextBox(
            Key.S, ModifierKeys.None));
        Assert.True(EditableTextShortcutPolicy.KeepInTextBox(
            Key.Left, ModifierKeys.Shift));
    }

    [Fact]
    public void AltNavigationReachesWindow()
    {
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            Key.Left, ModifierKeys.Alt));
    }

    [Theory]
    [InlineData(Key.F1)]
    [InlineData(Key.F2)]
    [InlineData(Key.F3)]
    [InlineData(Key.F4)]
    [InlineData(Key.F5)]
    [InlineData(Key.F6)]
    [InlineData(Key.F7)]
    [InlineData(Key.F8)]
    [InlineData(Key.F9)]
    [InlineData(Key.F11)]
    [InlineData(Key.F12)]
    public void FunctionKeysReachWindowWithOrWithoutShift(Key key)
    {
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            key, ModifierKeys.None));
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            key, ModifierKeys.Shift));
    }

    [Fact]
    public void SystemF10ReachesWindowWithOrWithoutShift()
    {
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            Key.System, ModifierKeys.None, Key.F10));
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            Key.System, ModifierKeys.Shift, Key.F10));
    }
}
