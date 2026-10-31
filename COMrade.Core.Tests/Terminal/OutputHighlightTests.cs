using System.Text;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class OutputHighlightTests
{
    // XTPUSHSGR and the highlight, black on yellow; the highlight again; XTPOPSGR.
    private const string On = "\e[#{\e[38;2;0;0;0;48;2;249;241;165m";
    private const string Again = "\e[38;2;0;0;0;48;2;249;241;165m";
    private const string Off = "\e[#}";

    // The selected match: black on orange.
    private const string OnSelected = "\e[#{\e[38;2;0;0;0;48;2;255;150;50m";

    private static string Mark(string output, string query, bool matchCase = false, (int Line, int Column)? selected = null) =>
        Encoding.UTF8.GetString(OutputHighlight.Mark(Encoding.UTF8.GetBytes(output), query, matchCase, selected));

    [Fact]
    public void Matches_are_marked_and_the_rest_is_kept()
    {
        Assert.Equal($"u-{On}boot{Off}> {On}boot{Off}\r\n", Mark("u-boot> boot\r\n", "boot"));
    }

    [Fact]
    public void Case_is_ignored_unless_asked()
    {
        Assert.Equal($"{On}Error{Off} {On}error{Off}", Mark("Error error", "error"));
        Assert.Equal($"Error {On}error{Off}", Mark("Error error", "error", matchCase: true));
    }

    [Fact]
    public void Colours_around_a_match_are_kept()
    {
        // The match ends with XTPOPSGR, which brings the green back.
        Assert.Equal($"\e[32mstatus {On}alive{Off} ok\e[0m", Mark("\e[32mstatus alive ok\e[0m", "alive"));
    }

    [Fact]
    public void A_match_split_by_colour_codes_is_marked_whole()
    {
        // The highlight starts again after each code, and the codes are applied again after the
        // match, so the text after it keeps the colour they set.
        Assert.Equal($"\e[32m{On}run\e[0m\e[33m{Again}ning{Off}\e[0m\e[33m now\e[0m", Mark("\e[32mrun\e[0m\e[33mning now\e[0m", "running"));
    }

    [Fact]
    public void Overlapping_matches_are_marked_once()
    {
        Assert.Equal($"{On}aaaa{Off}b", Mark("aaaab", "aa"));
    }

    [Fact]
    public void Matches_do_not_reach_across_lines()
    {
        Assert.Equal($"{On}ab{Off}\r\n{On}ab{Off}", Mark("ab\r\nab", "ab"));
        Assert.Equal("a\r\nb", Mark("a\r\nb", "ab"));
    }

    [Fact]
    public void Text_beyond_ascii_is_marked()
    {
        Assert.Equal($"señal {On}débil{Off} €", Mark("señal débil €", "DÉBIL"));
    }

    [Fact]
    public void The_selected_match_has_a_colour_of_its_own()
    {
        Assert.Equal($"{On}boot{Off} {OnSelected}boot{Off}\r\n{On}boot{Off}", Mark("boot boot\r\nboot", "boot", selected: (0, 5)));
        Assert.Equal($"{On}boot{Off} {On}boot{Off}\r\n{OnSelected}boot{Off}", Mark("boot boot\r\nboot", "boot", selected: (1, 0)));
    }

    [Fact]
    public void Nothing_to_find_leaves_the_output_as_it_was()
    {
        Assert.Equal("\e[1mboot\e[0m\r\n", Mark("\e[1mboot\e[0m\r\n", ""));
        Assert.Equal("\e[1mboot\e[0m\r\n", Mark("\e[1mboot\e[0m\r\n", "kernel"));
    }
}
