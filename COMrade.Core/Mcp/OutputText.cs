using System.Globalization;
using System.Text;
using COMrade.Core.Logging;

namespace COMrade.Core.Mcp;

/// <summary>
/// Text from what one side sends, piece by piece: UTF-8, without colour and cursor codes when asked,
/// carrying a character or a code split between two pieces over to the next.
/// </summary>
/// <param name="strip">Leave out colour and cursor codes and other control characters but tab, CR and LF.</param>
internal sealed class OutputText(bool strip = true)
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private AnsiStripper _stripper = new();

    /// <summary>For example "48 69 0D 0A".</summary>
    public static string Hex(byte[] bytes) =>
        string.Join(' ', bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        var count = _decoder.GetChars(bytes, chars, flush: false);
        if (!strip)
            return new string(chars, 0, count);

        var text = new StringBuilder(count);
        _stripper.Strip(chars.AsSpan(0, count), text);
        return text.ToString();
    }
}
