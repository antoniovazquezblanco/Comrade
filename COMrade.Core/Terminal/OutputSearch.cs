using System.Text;
using COMrade.Core.Logging;

namespace COMrade.Core.Terminal;

/// <summary>
/// Finds text in what a terminal has shown. Windows Terminal's control cannot search, so COMrade
/// searches its own copy of the output (<see cref="TerminalHistory"/>), as the reader sees it:
/// without colour and cursor codes.
/// </summary>
public static class OutputSearch
{
    /// <summary>Enough to be useful in a list; a longer list says to refine the search.</summary>
    public const int MaximumMatches = 1000;

    /// <summary>The lines of <paramref name="output"/> (UTF-8 terminal output) as a reader sees them.</summary>
    public static IReadOnlyList<string> Lines(ReadOnlySpan<byte> output)
    {
        var text = new StringBuilder();
        new AnsiStripper().Strip(Encoding.UTF8.GetString(output), text);
        return [.. text.ToString().Split('\n').Select(line => line.TrimEnd('\r'))];
    }

    /// <summary>
    /// Every occurrence of <paramref name="query"/> in <paramref name="lines"/> from line
    /// <paramref name="from"/> (counted from 0) on, oldest first: the newest
    /// <see cref="MaximumMatches"/> when there are more, as the latest output is usually what is
    /// looked for. Line numbers count from 1 at the first of <paramref name="lines"/>.
    /// </summary>
    public static IReadOnlyList<SearchMatch> Find(IReadOnlyList<string> lines, string query, bool matchCase, int from = 0)
    {
        var matches = new Queue<SearchMatch>();
        if (string.IsNullOrEmpty(query))
            return [];

        for (var line = from; line < lines.Count; line++)
        {
            foreach (var column in Columns(lines[line], query, matchCase))
            {
                if (matches.Count == MaximumMatches)
                    matches.Dequeue();
                matches.Enqueue(new SearchMatch(line + 1, column, query.Length, lines[line]));
            }
        }

        return [.. matches];
    }

    /// <summary>Where <paramref name="query"/> starts in <paramref name="line"/>, overlapping occurrences included.</summary>
    internal static IEnumerable<int> Columns(string line, string query, bool matchCase)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var column = line.IndexOf(query, comparison); column >= 0; column = line.IndexOf(query, column + 1, comparison))
            yield return column;
    }
}
