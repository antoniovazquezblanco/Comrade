using System.Text;
using System.Text.RegularExpressions;
using COMrade.Core.Logging;

namespace COMrade.Core.Terminal;

/// <summary>
/// Marks search matches in terminal output, for the terminal to show them highlighted. The marks
/// are colour codes around each match: the terminal cannot highlight text itself, so the output is
/// shown again with them (see <see cref="TerminalHistory.Replay"/>).
/// </summary>
public static partial class OutputHighlight
{
    // Black on yellow, and on orange for the selected match, as finders highlight; in exact colours,
    // as the schemes' own yellows differ (Solarized's bright yellow is grey). XTPUSHSGR keeps the
    // colours the match had and XTPOPSGR brings them back after it, so the output around a match
    // looks as it did.
    private const string Push = "\e[#{";
    private const string Highlight = "\e[38;2;0;0;0;48;2;249;241;165m";
    private const string SelectedHighlight = "\e[38;2;0;0;0;48;2;255;150;50m";
    private const string End = "\e[#}";

    /// <summary>
    /// <paramref name="output"/>, UTF-8 terminal output, with every occurrence of
    /// <paramref name="query"/> marked: the matches <see cref="OutputSearch"/> finds, without its
    /// maximum. The match at <paramref name="selected"/>, a line of the output from 0 and a column,
    /// is marked apart.
    /// </summary>
    public static byte[] Mark(ReadOnlySpan<byte> output, string query, bool matchCase, (int Line, int Column)? selected = null)
    {
        var raw = Encoding.UTF8.GetString(output);
        var text = new StringBuilder(raw.Length);
        var sources = new List<int>(raw.Length);
        new AnsiStripper().Strip(raw, text, sources);

        var spans = Spans(text.ToString(), query, matchCase, selected);
        if (spans.Count == 0)
            return output.ToArray();

        var marked = new StringBuilder(raw.Length + spans.Count * (Push.Length + Highlight.Length + End.Length));
        AppendMarked(marked, raw, sources, spans, raw.Length);
        return Encoding.UTF8.GetBytes(marked.ToString());
    }

    /// <summary>
    /// Appends <paramref name="raw"/> up to <paramref name="end"/> to <paramref name="marked"/>, with
    /// <paramref name="spans"/> marked: spans of its text without codes, which
    /// <paramref name="sources"/> maps to positions in <paramref name="raw"/> (see
    /// <see cref="AnsiStripper.Strip"/>).
    /// </summary>
    internal static void AppendMarked(StringBuilder marked, string raw, List<int> sources, IEnumerable<(int Start, int End, bool IsSelected)> spans, int end)
    {
        var copied = 0;
        foreach (var (start, spanEnd, isSelected) in spans)
        {
            var highlight = isSelected ? SelectedHighlight : Highlight;
            var rawStart = sources[start];
            marked.Append(raw, copied, rawStart - copied).Append(Push).Append(highlight);

            // Colour codes inside a match end its highlight: it starts again after each, and they
            // are applied again after the match, so the text after it has the colour they set.
            var inside = new StringBuilder();
            var position = rawStart;
            for (var i = start; i < spanEnd; i++)
            {
                if (sources[i] > position)
                {
                    var codes = raw[position..sources[i]];
                    marked.Append(codes).Append(highlight);
                    inside.Append(codes);
                }
                marked.Append(raw[sources[i]]);
                position = sources[i] + 1;
            }

            marked.Append(End);
            foreach (Match colour in ColourCode().Matches(inside.ToString()))
                marked.Append(colour.Value);
            copied = position;
        }

        marked.Append(raw, copied, end - copied);
    }

    // The matches as spans of the text without colour codes, line by line as OutputSearch finds
    // them, with overlapping ones joined; a span is selected when a match in it is.
    internal static List<(int Start, int End, bool IsSelected)> Spans(string text, string query, bool matchCase, (int Line, int Column)? selected)
    {
        var spans = new List<(int Start, int End, bool IsSelected)>();
        if (string.IsNullOrEmpty(query))
            return spans;

        var line = 0;
        for (var lineStart = 0; lineStart <= text.Length; line++)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = text.Length;

            foreach (var column in OutputSearch.Columns(text[lineStart..lineEnd].TrimEnd('\r'), query, matchCase))
            {
                var (start, end, isSelected) = (lineStart + column, lineStart + column + query.Length, selected == (line, column));
                if (spans.Count > 0 && start <= spans[^1].End)
                    spans[^1] = (spans[^1].Start, Math.Max(end, spans[^1].End), spans[^1].IsSelected || isSelected);
                else
                    spans.Add((start, end, isSelected));
            }

            lineStart = lineEnd + 1;
        }

        return spans;
    }

    [GeneratedRegex(@"\e\[[0-9;:]*m")]
    private static partial Regex ColourCode();
}
