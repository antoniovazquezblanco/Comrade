using System.Buffers;
using System.Globalization;
using System.Text;
using COMrade.Core.Sessions;

namespace COMrade.Core.TrafficView;

/// <summary>
/// Bytes that went over a port as the analysis view shows them: printable characters as they are,
/// the rest as escapes, <c>\r \n \t \e</c>, <c>\xHH</c> for any other byte and <c>\\</c> for a
/// backslash, as the send bar takes them back. Text is UTF-8; bytes that are not, and characters
/// that would change how the text around them shows (controls, format characters such as U+202E,
/// line and paragraph separators, unassigned ones), show as <c>\xHH</c>, one per byte.
/// </summary>
public static class TrafficText
{
    /// <summary>How a row's time shows, in local time.</summary>
    public const string TimeFormat = "HH:mm:ss.fff";

    /// <summary>
    /// The first character of <paramref name="bytes"/> as shown: how many bytes it takes, how many
    /// columns it shows in, and whether it is an escape. <paramref name="bytes"/> must not be empty.
    /// </summary>
    public static TrafficUnit Next(ReadOnlySpan<byte> bytes)
    {
        var first = bytes[0];
        if (first < 0x80)
        {
            return first switch
            {
                (byte)'\\' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0x1B => new(1, 2, IsEscape: true),
                >= 0x20 and < 0x7F => new(1, 1, IsEscape: false),
                _ => new(1, 4, IsEscape: true),
            };
        }

        return Rune.DecodeFromUtf8(bytes, out var rune, out var consumed) switch
        {
            OperationStatus.Done when Shows(rune) => new(consumed, 1, IsEscape: false),

            // The rest of the character may still come: its bytes go together, so they show together.
            OperationStatus.NeedMoreData => new(bytes.Length, 4 * bytes.Length, IsEscape: true, IsIncomplete: true),
            _ => new(1, 4, IsEscape: true),
        };
    }

    /// <summary>The bytes as shown.</summary>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        for (var index = 0; index < bytes.Length;)
            index += Append(text, bytes[index..]);
        return text.ToString();
    }

    /// <summary>The bytes as shown, in pieces that are all escapes or all not, for colouring them apart.</summary>
    public static IReadOnlyList<(string Text, bool IsEscape)> Pieces(ReadOnlySpan<byte> bytes)
    {
        var pieces = new List<(string, bool)>();
        var text = new StringBuilder();
        var escapes = false;
        for (var index = 0; index < bytes.Length;)
        {
            var unit = Next(bytes[index..]);
            if (unit.IsEscape != escapes && text.Length > 0)
            {
                pieces.Add((text.ToString(), escapes));
                text.Clear();
            }

            escapes = unit.IsEscape;
            index += Append(text, bytes[index..]);
        }

        if (text.Length > 0)
            pieces.Add((text.ToString(), escapes));
        return pieces;
    }

    /// <summary>RX for what the device sent, TX for what was sent to it.</summary>
    public static string Direction(TrafficSender sender) => sender is TrafficSender.Device ? "RX" : "TX";

    /// <summary>
    /// Who sent it: the device; the console, the user in COMrade's terminal, or COMrade on their
    /// behalf, such as Enter at each speed while it detects the device's; or MCP, an AI assistant.
    /// </summary>
    public static string From(TrafficSender sender) => sender switch
    {
        TrafficSender.Device => "device",
        TrafficSender.Keyboard or TrafficSender.Comrade => "console",
        TrafficSender.Assistant => "mcp",
        _ => throw new ArgumentOutOfRangeException(nameof(sender), sender, null),
    };

    // Appends the first character of bytes as shown; returns how many bytes it took.
    private static int Append(StringBuilder text, ReadOnlySpan<byte> bytes)
    {
        var unit = Next(bytes);
        if (!unit.IsEscape)
        {
            text.Append(Encoding.UTF8.GetString(bytes[..unit.Length]));
            return unit.Length;
        }

        foreach (var value in bytes[..unit.Length])
        {
            text.Append(value switch
            {
                (byte)'\\' => @"\\",
                (byte)'\r' => @"\r",
                (byte)'\n' => @"\n",
                (byte)'\t' => @"\t",
                0x1B => @"\e",
                _ => @"\x" + value.ToString("x2", CultureInfo.InvariantCulture),
            });
        }

        return unit.Length;
    }

    private static bool Shows(Rune rune) => Rune.GetUnicodeCategory(rune) is not (
        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator or UnicodeCategory.OtherNotAssigned);
}

/// <summary>A character as the analysis view shows it, see <see cref="TrafficText.Next"/>.</summary>
/// <param name="Length">Bytes it takes.</param>
/// <param name="Width">Columns it shows in.</param>
/// <param name="IsEscape">Shown as escapes.</param>
/// <param name="IsIncomplete">The start of a character whose other bytes are not there (yet).</param>
public readonly record struct TrafficUnit(int Length, int Width, bool IsEscape, bool IsIncomplete = false);
