using COMrade.Core.Shortcuts;
using Windows.System;

namespace COMrade.Core.Tests.Shortcuts;

public class KeyChordTests
{
    [Theory]
    [InlineData(VirtualKey.T, true, "Ctrl+Shift+T")]
    [InlineData(VirtualKey.Tab, false, "Ctrl+Tab")]
    [InlineData(VirtualKey.End, true, "Ctrl+Shift+End")]
    [InlineData(VirtualKey.Number5, false, "Ctrl+5")]
    [InlineData(VirtualKey.F12, false, "Ctrl+F12")]
    [InlineData(KeyChord.Comma, false, "Ctrl+,")]
    [InlineData(KeyChord.Plus, true, "Ctrl+Shift++")]
    public void Keys_read_as_they_are_written(VirtualKey key, bool shift, string text)
    {
        var chord = new KeyChord(key, shift);

        Assert.Equal(text, chord.ToString());
        Assert.Equal(chord, KeyChord.Parse(text));
    }

    [Fact]
    public void Reading_ignores_case_and_spaces()
    {
        Assert.Equal(new KeyChord(VirtualKey.PageDown, Shift: true), KeyChord.Parse(" ctrl+shift+pagedown "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("Shift+T")]
    [InlineData("Alt+T")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift+")]
    [InlineData("Ctrl+Alt+T")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+NoSuchKey")]
    public void Text_without_ctrl_and_a_known_key_is_no_shortcut(string? text)
    {
        Assert.Null(KeyChord.Parse(text));
    }

    [Fact]
    public void Letters_digits_function_and_navigation_keys_can_be_shortcuts()
    {
        Assert.NotNull(KeyChord.From(VirtualKey.Q, shift: false));
        Assert.NotNull(KeyChord.From(VirtualKey.Number0, shift: true));
        Assert.NotNull(KeyChord.From(VirtualKey.F24, shift: false));
        Assert.NotNull(KeyChord.From(VirtualKey.Left, shift: true));
    }

    [Theory]
    [InlineData(VirtualKey.Control)]
    [InlineData(VirtualKey.Shift)]
    [InlineData(VirtualKey.Escape)]
    [InlineData(VirtualKey.NumberPad1)]
    // ; on a US keyboard, ñ on a Spanish one: the same key is another character on each layout.
    [InlineData((VirtualKey)0xBA)]
    public void Modifiers_escape_and_keys_that_depend_on_the_layout_cannot_be_shortcuts(VirtualKey key)
    {
        Assert.Null(KeyChord.From(key, shift: false));
    }
}
