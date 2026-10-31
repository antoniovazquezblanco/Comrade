using COMrade.Core.Terminal;

namespace COMrade.ViewModels;

/// <summary>A match in the find panel's list: its line, with the text found set apart.</summary>
/// <remarks>
/// The list shows the parts side by side, and a TextBlock leaves the spaces it ends with out of its
/// width: the spaces a part ends with start the next part instead.
/// </remarks>
public sealed record FindResult(SearchMatch Match)
{
    // Enough of the line before the match to read it in context: a long line starts later.
    private const int Context = 24;

    public int Line => Match.Line;

    /// <summary>Its line and column, where another search finds the same match.</summary>
    public (int Line, int Column) Position => (Line, Match.Column);

    public string Before => TextBefore.TrimEnd(' ');

    public string Found => TrailingSpaces(TextBefore) + TextFound.TrimEnd(' ');

    public string After => TrailingSpaces(TextFound) + Match.Text[(Match.Column + Match.Length)..];

    /// <summary>What screen readers say for the result in the list.</summary>
    public override string ToString() => $"Line {Line}: {Match.Text}";

    private string TextBefore => Match.Column <= Context
        ? Match.Text[..Match.Column]
        : "…" + Match.Text[(Match.Column - Context)..Match.Column];

    private string TextFound => Match.Text.Substring(Match.Column, Match.Length);

    private static string TrailingSpaces(string text) => new(' ', text.Length - text.TrimEnd(' ').Length);
}
