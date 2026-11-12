using System.Globalization;

namespace COMrade.Core.TrafficView;

/// <summary>The columns of the analysis view, in the order they show.</summary>
[Flags]
public enum AnalysisColumns
{
    None = 0,
    Time = 1,
    Direction = 2,
    From = 4,
    Hex = 8,
    Text = 16,
    All = Time | Direction | From | Hex | Text,
}

/// <summary>
/// How the columns shown share a row of the analysis view: Time, Dir and From take what their
/// values need; a row holds the most bytes whose hex fits, 32, 16 or 8, with a column of text for
/// each when the text shows too; the text takes the rest. Hex or Text always shows.
/// </summary>
public sealed record TrafficLayout(AnalysisColumns Shown, int RowBytes, int TextColumns)
{
    public const int TimeWidth = 12;
    public const int DirectionWidth = 3;
    public const int FromWidth = 7;
    public const int MinimumTextColumns = 8;

    private static readonly int[] RowByteChoices = [32, 16, 8];

    /// <summary>The columns of <paramref name="shown"/> in a row <paramref name="columns"/> wide; Text when neither Hex nor Text is asked for.</summary>
    public static TrafficLayout Fit(AnalysisColumns shown, int columns)
    {
        shown = Contents(shown);
        var room = Math.Max(0, columns - Width(shown, AnalysisColumns.Time, TimeWidth) - Width(shown, AnalysisColumns.Direction, DirectionWidth) - Width(shown, AnalysisColumns.From, FromWidth));
        var text = shown.HasFlag(AnalysisColumns.Text);
        if (!shown.HasFlag(AnalysisColumns.Hex))
            return new(shown, int.MaxValue, Math.Max(MinimumTextColumns, room));

        var bytes = Array.Find(RowByteChoices, count => TrafficText.HexWidth(count) + (text ? 1 + count : 0) <= room);
        if (bytes == 0)
            bytes = RowByteChoices[^1];
        return new(shown, bytes, text ? Math.Max(MinimumTextColumns, room - TrafficText.HexWidth(bytes) - 1) : int.MaxValue);
    }

    /// <summary>
    /// <paramref name="shown"/> with <paramref name="column"/> shown or hidden; unchanged when that
    /// would hide both Hex and Text, which leaves nothing of the bytes.
    /// </summary>
    public static AnalysisColumns Show(AnalysisColumns shown, AnalysisColumns column, bool show)
    {
        var changed = show ? shown | column : shown & ~column;
        return (changed & (AnalysisColumns.Hex | AnalysisColumns.Text)) == 0 ? shown : changed;
    }

    /// <summary>The names of the columns shown, each over its column.</summary>
    public string Header =>
        (Column(AnalysisColumns.Time, "Time", TimeWidth)
        + Column(AnalysisColumns.Direction, "Dir", DirectionWidth)
        + Column(AnalysisColumns.From, "From", FromWidth)
        + (Shown.HasFlag(AnalysisColumns.Hex) ? "Hex".PadRight(TrafficText.HexWidth(RowBytes)) + " " : string.Empty)
        + Column(AnalysisColumns.Text, "Text", 0)).TrimEnd();

    /// <summary>The time column: when the row's first byte arrived, local; blank for none.</summary>
    public string Time(DateTimeOffset? at) => Shown.HasFlag(AnalysisColumns.Time)
        ? (at?.ToLocalTime().ToString(TrafficText.TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty).PadRight(TimeWidth) + " "
        : string.Empty;

    /// <summary>The Dir and From columns; blank for the rest of a wrapped row.</summary>
    public string Who(TrafficRow row) =>
        Column(AnalysisColumns.Direction, row.Continues ? string.Empty : TrafficText.Direction(row.Sender), DirectionWidth)
        + Column(AnalysisColumns.From, row.Continues ? string.Empty : TrafficText.From(row.Sender), FromWidth);

    /// <summary>The Hex column: the bytes, as wide as a full row's.</summary>
    public string Hex(ReadOnlySpan<byte> bytes) => Shown.HasFlag(AnalysisColumns.Hex)
        ? TrafficText.Hex(bytes, grouped: true).PadRight(TrafficText.HexWidth(RowBytes)) + " "
        : string.Empty;

    /// <summary>A row as shown, for copying it.</summary>
    public string Format(DateTimeOffset? at, TrafficRow row, ReadOnlySpan<byte> bytes)
    {
        var line = Time(at) + Who(row) + Hex(bytes);
        return Shown.HasFlag(AnalysisColumns.Text) ? line + TrafficText.Format(bytes) : line.TrimEnd();
    }

    private static AnalysisColumns Contents(AnalysisColumns shown) =>
        (shown & (AnalysisColumns.Hex | AnalysisColumns.Text)) == 0 ? (shown | AnalysisColumns.Text) & AnalysisColumns.All : shown & AnalysisColumns.All;

    private static int Width(AnalysisColumns shown, AnalysisColumns column, int width) => shown.HasFlag(column) ? width + 1 : 0;

    private string Column(AnalysisColumns column, string value, int width) =>
        Shown.HasFlag(column) ? value.PadRight(width) + " " : string.Empty;
}
