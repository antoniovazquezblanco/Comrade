using System.Text;
using COMrade.Core.Logging;

namespace COMrade.Core.Tests.Logging;

public class AnsiStripperTests
{
    private static string Strip(params string[] reads)
    {
        var stripper = new AnsiStripper();
        var output = new StringBuilder();
        foreach (var read in reads)
            stripper.Strip(read, output);
        return output.ToString();
    }

    [Fact]
    public void Colour_codes_are_removed()
    {
        Assert.Equal("ok: ready\r\n", Strip("\e[32mok\e[0m: ready\r\n"));
    }

    [Fact]
    public void Cursor_and_screen_codes_are_removed()
    {
        Assert.Equal("menu", Strip("\e[2J\e[H\e[?25lmenu\e[?25h\e[10;5H"));
    }

    [Fact]
    public void Window_titles_are_removed_with_either_ending()
    {
        Assert.Equal("ab", Strip("\e]0;router\aa\e]2;title\e\\b"));
    }

    [Fact]
    public void Codes_split_across_reads_are_removed()
    {
        Assert.Equal("red text", Strip("\e", "[3", "1mred", " text\e[", "0m"));
    }

    [Fact]
    public void Text_tabs_and_line_breaks_are_kept_other_controls_dropped()
    {
        Assert.Equal("a\tb\r\nÁngulo ✓", Strip("a\tb\a\b\r\nÁngulo ✓"));
    }
}
