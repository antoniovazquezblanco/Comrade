using System.Text;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class OutputSearchTests
{
    private static IReadOnlyList<string> Lines(string output) => OutputSearch.Lines(Encoding.UTF8.GetBytes(output));

    [Fact]
    public void Lines_are_as_the_reader_sees_them()
    {
        Assert.Equal(["¡hola!", "ERROR: boot failed", ""], Lines("\e[1m¡hola!\e[0m\r\n\e[31mERROR\e[0m: boot failed\r\n"));
    }

    [Fact]
    public void Matches_are_listed_oldest_first_with_line_and_column()
    {
        var matches = OutputSearch.Find(["boot", "u-boot> boot", "done"], "boot", matchCase: false);

        Assert.Equal([(1, 0), (2, 2), (2, 8)], matches.Select(match => (match.Line, match.Column)));
        Assert.Equal("u-boot> boot", matches[1].Text);
        Assert.All(matches, match => Assert.Equal(4, match.Length));
    }

    [Fact]
    public void Case_is_ignored_unless_asked()
    {
        IReadOnlyList<string> lines = ["Error", "error", "ERROR"];

        Assert.Equal(3, OutputSearch.Find(lines, "error", matchCase: false).Count);
        Assert.Single(OutputSearch.Find(lines, "error", matchCase: true));
    }

    [Fact]
    public void Overlapping_occurrences_are_all_found()
    {
        Assert.Equal(3, OutputSearch.Find(["aaaa"], "aa", matchCase: true).Count);
    }

    [Fact]
    public void An_empty_search_finds_nothing()
    {
        Assert.Empty(OutputSearch.Find(["anything"], "", matchCase: false));
    }

    [Fact]
    public void Matches_stop_at_the_maximum_keeping_the_newest()
    {
        var lines = Enumerable.Repeat("x x x", 500).ToList();

        var matches = OutputSearch.Find(lines, "x", matchCase: true);

        Assert.Equal(OutputSearch.MaximumMatches, matches.Count);
        Assert.Equal((500, 4), (matches[^1].Line, matches[^1].Column));
        Assert.True(matches[0].Line > 1);
    }

    [Fact]
    public void A_search_can_start_at_a_later_line()
    {
        var matches = OutputSearch.Find(["boot", "u-boot> boot", "boot"], "boot", matchCase: false, from: 1);

        Assert.Equal([(2, 2), (2, 8), (3, 0)], matches.Select(match => (match.Line, match.Column)));
    }
}
