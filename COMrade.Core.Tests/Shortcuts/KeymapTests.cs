using COMrade.Core.Shortcuts;
using Windows.System;

namespace COMrade.Core.Tests.Shortcuts;

public class KeymapTests
{
    private static readonly KeyChord CtrlShiftT = new(VirtualKey.T, Shift: true);
    private static readonly KeyChord CtrlShiftW = new(VirtualKey.W, Shift: true);
    private static readonly KeyChord CtrlShiftQ = new(VirtualKey.Q, Shift: true);

    [Fact]
    public void Every_action_has_its_own_default_keys()
    {
        Assert.Equal(Keymap.Actions.Count, Keymap.Defaults.Count);
        Assert.Equal(Keymap.Defaults.Count, Keymap.Defaults.Values.Distinct().Count());
    }

    [Theory]
    [InlineData(ShortcutAction.NewTab, VirtualKey.T, true)]
    [InlineData(ShortcutAction.CloseTab, VirtualKey.W, true)]
    [InlineData(ShortcutAction.NextTab, VirtualKey.Tab, false)]
    [InlineData(ShortcutAction.PreviousTab, VirtualKey.Tab, true)]
    [InlineData(ShortcutAction.OpenOrClosePort, VirtualKey.O, true)]
    [InlineData(ShortcutAction.SendBreak, VirtualKey.B, true)]
    [InlineData(ShortcutAction.Copy, VirtualKey.C, true)]
    [InlineData(ShortcutAction.Paste, VirtualKey.V, true)]
    [InlineData(ShortcutAction.Find, VirtualKey.F, true)]
    [InlineData(ShortcutAction.ClearTerminal, VirtualKey.K, true)]
    [InlineData(ShortcutAction.StartOrStopLogging, VirtualKey.L, true)]
    [InlineData(ShortcutAction.ScrollToTop, VirtualKey.Home, true)]
    [InlineData(ShortcutAction.ScrollPageUp, VirtualKey.PageUp, true)]
    [InlineData(ShortcutAction.ScrollPageDown, VirtualKey.PageDown, true)]
    [InlineData(ShortcutAction.ScrollToBottom, VirtualKey.End, true)]
    [InlineData(ShortcutAction.OpenSettings, KeyChord.Comma, false)]
    public void Default_keys(ShortcutAction action, VirtualKey key, bool shift)
    {
        Assert.Equal(action, Keymap.Default.ActionFor(new KeyChord(key, shift)));
        Assert.Equal(new KeyChord(key, shift), Keymap.Default.KeysFor(action));
    }

    [Fact]
    public void The_defaults_are_no_change()
    {
        Assert.False(Keymap.Default.HasChanges);
        Assert.Null(Keymap.Default.Changes);
    }

    [Fact]
    public void Keys_of_no_shortcut_run_nothing()
    {
        // Ctrl+C reaches the device as 0x03.
        Assert.Null(Keymap.Default.ActionFor(new KeyChord(VirtualKey.C)));
    }

    [Fact]
    public void New_keys_replace_the_old_ones()
    {
        var keymap = Keymap.Default.With(ShortcutAction.CloseTab, CtrlShiftQ);

        Assert.Equal(CtrlShiftQ, keymap.KeysFor(ShortcutAction.CloseTab));
        Assert.Equal(ShortcutAction.CloseTab, keymap.ActionFor(CtrlShiftQ));
        Assert.Null(keymap.ActionFor(CtrlShiftW));
        Assert.True(keymap.IsChanged(ShortcutAction.CloseTab));
    }

    [Fact]
    public void Keys_of_another_shortcut_leave_it_without_keys()
    {
        var keymap = Keymap.Default.With(ShortcutAction.CloseTab, CtrlShiftT);

        Assert.Equal(ShortcutAction.CloseTab, keymap.ActionFor(CtrlShiftT));
        Assert.Null(keymap.KeysFor(ShortcutAction.NewTab));
        Assert.True(keymap.IsChanged(ShortcutAction.NewTab));
    }

    [Fact]
    public void A_shortcut_can_have_no_keys()
    {
        var keymap = Keymap.Default.With(ShortcutAction.Copy, null);

        Assert.Null(keymap.KeysFor(ShortcutAction.Copy));
        Assert.Null(keymap.ActionFor(new KeyChord(VirtualKey.C, Shift: true)));
    }

    [Fact]
    public void Reset_takes_the_default_keys_back()
    {
        var keymap = Keymap.Default
            .With(ShortcutAction.CloseTab, CtrlShiftT)
            .Reset(ShortcutAction.NewTab);

        Assert.Equal(CtrlShiftT, keymap.KeysFor(ShortcutAction.NewTab));
        Assert.Null(keymap.KeysFor(ShortcutAction.CloseTab));
    }

    [Fact]
    public void Default_keys_set_again_are_no_change()
    {
        var keymap = Keymap.Default
            .With(ShortcutAction.CloseTab, CtrlShiftQ)
            .With(ShortcutAction.CloseTab, CtrlShiftW);

        Assert.False(keymap.HasChanges);
        Assert.Null(keymap.Changes);
    }

    [Fact]
    public void Changes_are_kept_as_text_and_read_back()
    {
        var keymap = Keymap.Default
            .With(ShortcutAction.CloseTab, CtrlShiftQ)
            .With(ShortcutAction.Copy, null);

        Assert.Equal(new Dictionary<string, string?> { ["CloseTab"] = "Ctrl+Shift+Q", ["Copy"] = null }, keymap.Changes);
        var read = new Keymap(keymap.Changes);
        Assert.All(Keymap.Actions, action => Assert.Equal(keymap.KeysFor(action), read.KeysFor(action)));
    }

    [Fact]
    public void Changed_keys_win_over_default_ones()
    {
        // As in a file edited by hand: Ctrl+Shift+T is still New tab's default.
        var keymap = new Keymap(new Dictionary<string, string?> { ["CloseTab"] = "Ctrl+Shift+T" });

        Assert.Equal(ShortcutAction.CloseTab, keymap.ActionFor(CtrlShiftT));
        Assert.Null(keymap.KeysFor(ShortcutAction.NewTab));
    }

    [Fact]
    public void Unknown_actions_and_unreadable_keys_keep_the_defaults()
    {
        // As in a file from another version of COMrade.
        var keymap = new Keymap(new Dictionary<string, string?>
        {
            ["SplitPane"] = "Ctrl+Shift+D",
            ["CloseTab"] = "Ctrl+Shift+NoSuchKey",
            ["copy"] = "ctrl+shift+k",
        });

        Assert.Null(keymap.ActionFor(new KeyChord(VirtualKey.D, Shift: true)));
        Assert.Equal(CtrlShiftW, keymap.KeysFor(ShortcutAction.CloseTab));
        Assert.Equal(new KeyChord(VirtualKey.K, Shift: true), keymap.KeysFor(ShortcutAction.Copy));
    }
}
