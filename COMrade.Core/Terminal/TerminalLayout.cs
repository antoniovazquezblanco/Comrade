using System.Globalization;

namespace COMrade.Core.Terminal;

/// <summary>
/// Where lines of output (as <see cref="OutputSearch.Lines"/> gives them) land among a terminal's
/// rows: lines longer than the terminal is wide wrap onto more rows, and a terminal keeps only so
/// many rows, dropping the oldest. Output that moves the cursor about, as full-screen programs do,
/// does not follow lines, and lands elsewhere.
/// </summary>
public static class TerminalLayout
{
    private const int TabWidth = 8;

    /// <summary>The rows <paramref name="line"/> takes in a terminal <paramref name="columns"/> characters wide.</summary>
    public static int Rows(string line, int columns)
    {
        var width = 0;
        var column = 0;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\r')
            {
                column = 0;
                continue;
            }

            if (c == '\t')
                column = (column / TabWidth + 1) * TabWidth;
            else if (char.IsHighSurrogate(c) && i + 1 < line.Length)
                column += Width(char.ConvertToUtf32(c, line[++i]));
            else
                column += Width(c);
            width = Math.Max(width, column);
        }

        return Math.Max(1, (width + columns - 1) / columns);
    }

    /// <summary>
    /// The row of line <paramref name="index"/> in a terminal <paramref name="columns"/> wide that
    /// shows all of <paramref name="lines"/> in <paramref name="terminalRows"/> rows, the last line
    /// at the bottom; null when the terminal no longer keeps that line.
    /// </summary>
    public static int? RowOf(IReadOnlyList<string> lines, int index, int columns, int terminalRows)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, lines.Count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);

        var before = 0;
        var total = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == index)
                before = total;
            total += Rows(lines[i], columns);
        }

        var row = before - Math.Max(0, total - terminalRows);
        return row >= 0 ? row : null;
    }

    // Columns a character takes: none for marks that combine with the one before, two for the wide
    // characters of East Asian scripts and emoji.
    private static int Width(int codePoint)
    {
        if (codePoint < 0x20)
            return 0;
        if (codePoint < 0x300)
            return 1;

        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return 0;

        return codePoint is >= 0x1100 and <= 0x115F
            or >= 0x2E80 and <= 0x303E
            or >= 0x3041 and <= 0x33FF
            or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xA000 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F
            or >= 0x1F900 and <= 0x1F9FF
            or >= 0x20000 and <= 0x3FFFD
            ? 2
            : 1;
    }
}
