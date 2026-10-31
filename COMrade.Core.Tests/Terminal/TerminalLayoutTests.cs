using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class TerminalLayoutTests
{
    [Theory]
    [InlineData("", 1)]
    [InlineData("0123456789", 1)]
    [InlineData("0123456789a", 2)]
    [InlineData("0123456789012345678901", 3)]
    public void Long_lines_wrap_onto_more_rows(string line, int rows)
    {
        Assert.Equal(rows, TerminalLayout.Rows(line, columns: 10));
    }

    [Fact]
    public void Tabs_reach_the_next_multiple_of_eight()
    {
        Assert.Equal(1, TerminalLayout.Rows("a\tb", columns: 9));
        Assert.Equal(2, TerminalLayout.Rows("a\tb", columns: 8));
    }

    [Fact]
    public void A_carriage_return_writes_over_the_start_of_the_line()
    {
        // Progress shown in place: 12 characters, then 3 over them.
        Assert.Equal(2, TerminalLayout.Rows("progress 10%\r50%", columns: 10));
        Assert.Equal(1, TerminalLayout.Rows("10%\r50%", columns: 10));
    }

    [Fact]
    public void Wide_characters_take_two_columns()
    {
        Assert.Equal(1, TerminalLayout.Rows("日本語ok", columns: 8));
        Assert.Equal(2, TerminalLayout.Rows("日本語ok", columns: 7));
        Assert.Equal(1, TerminalLayout.Rows("señal", columns: 5));
    }

    [Fact]
    public void A_line_is_on_the_row_after_the_rows_before_it()
    {
        IReadOnlyList<string> lines = ["one", "a line that wraps", "three", ""];

        Assert.Equal(0, TerminalLayout.RowOf(lines, 0, columns: 10, terminalRows: 25));
        Assert.Equal(1, TerminalLayout.RowOf(lines, 1, columns: 10, terminalRows: 25));
        Assert.Equal(3, TerminalLayout.RowOf(lines, 2, columns: 10, terminalRows: 25));
    }

    [Fact]
    public void Rows_the_terminal_no_longer_keeps_move_the_rest_up()
    {
        // 6 rows of output in a terminal that keeps 4: the first 2 are gone.
        IReadOnlyList<string> lines = ["one", "two", "three", "four", "five", ""];

        Assert.Null(TerminalLayout.RowOf(lines, 1, columns: 10, terminalRows: 4));
        Assert.Equal(0, TerminalLayout.RowOf(lines, 2, columns: 10, terminalRows: 4));
        Assert.Equal(2, TerminalLayout.RowOf(lines, 4, columns: 10, terminalRows: 4));
    }
}
