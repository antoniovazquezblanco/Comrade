using System.Text;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class StreamingHighlightTests
{
    private const string On = "\e[#{\e[38;2;0;0;0;48;2;249;241;165m";
    private const string Off = "\e[#}";

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void A_match_in_the_output_is_marked()
    {
        var marks = new StreamingHighlight("error", matchCase: false);

        Assert.Equal($"mmc0: {On}error{Off} -110\r\n", Text(marks.Mark("mmc0: error -110\r\n"u8)));
    }

    [Fact]
    public void Text_that_cannot_begin_a_match_is_shown_at_once()
    {
        var marks = new StreamingHighlight("error", matchCase: false);

        Assert.Equal("boot> ", Text(marks.Mark("boot> "u8)));
        Assert.False(marks.IsHolding);
    }

    [Fact]
    public void A_match_split_between_two_reads_is_marked_whole()
    {
        var marks = new StreamingHighlight("error", matchCase: false);

        Assert.Equal("mmc0: ", Text(marks.Mark("mmc0: err"u8)));
        Assert.True(marks.IsHolding);
        Assert.Equal($"{On}error{Off} -110", Text(marks.Mark("or -110"u8)));
    }

    [Fact]
    public void Text_held_for_a_match_that_does_not_come_is_shown_when_flushed()
    {
        var marks = new StreamingHighlight("error", matchCase: false);

        Assert.Equal("login: ", Text(marks.Mark("login: er"u8)));
        Assert.Equal("er", Text(marks.Flush()));
        Assert.False(marks.IsHolding);
        Assert.Equal("ror", Text(marks.Mark("ror"u8)));
    }

    [Fact]
    public void A_line_break_ends_what_could_begin_a_match()
    {
        var marks = new StreamingHighlight("error", matchCase: false);

        Assert.Equal("err\r\n", Text(marks.Mark("err\r\n"u8)));
        Assert.Equal("or", Text(marks.Mark("or"u8)));
    }

    [Theory]
    [InlineData("error", false)]
    [InlineData("ERROR", true)]
    [InlineData("señal", false)]
    [InlineData("running", false)]
    [InlineData("€", false)]
    public void Output_read_in_any_two_parts_is_marked_as_if_read_at_once(string query, bool matchCase)
    {
        // Splits fall inside characters of more than one byte and inside colour codes too.
        var output = Encoding.UTF8.GetBytes(
            "[  0.18] mmc0: \e[31merror\e[0m -110\r\nERROR: wlan0, señal débil 9,99 €\r\n" +
            "Status: \e[32mrun\e[0m\e[33mning\e[0m\r\n\e]0;title\aError again\r\nprompt> ");
        var whole = Text(OutputHighlight.Mark(output, query, matchCase));

        for (var split = 0; split <= output.Length; split++)
        {
            var marks = new StreamingHighlight(query, matchCase);
            var shown = Text([.. marks.Mark(output.AsSpan(0, split)), .. marks.Mark(output.AsSpan(split)), .. marks.Flush()]);

            Assert.True(whole == shown, $"Split at byte {split}:\n{whole}\n{shown}");
        }
    }

    [Fact]
    public void Output_read_byte_by_byte_is_marked_as_if_read_at_once()
    {
        var output = Encoding.UTF8.GetBytes("a \e[1merror\e[0m, señal: ERROR\r\nerr\r\nerror");
        var whole = Text(OutputHighlight.Mark(output, "error", matchCase: false));

        var marks = new StreamingHighlight("error", matchCase: false);
        var shown = new List<byte>();
        foreach (var b in output)
            shown.AddRange(marks.Mark([b]));
        shown.AddRange(marks.Flush());

        Assert.Equal(whole, Text([.. shown]));
    }
}
