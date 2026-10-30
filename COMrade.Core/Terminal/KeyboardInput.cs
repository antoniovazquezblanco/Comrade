using System.Text;
using COMrade.Core.Serial;

namespace COMrade.Core.Terminal;

/// <summary>Converts text typed or pasted in the terminal into what is sent and what is echoed.</summary>
public static class KeyboardInput
{
    private const char Enter = '\r';
    private const char Delete = '\x7f';

    /// <summary>Bytes to send: UTF-8, with Enter (CR) replaced by the send line ending.</summary>
    /// <remarks>
    /// <see cref="LineEnding.None"/> still sends CR for Enter: pressing Enter in an interactive
    /// terminal must send something.
    /// </remarks>
    public static byte[] Encode(string input, LineEnding sendLineEnding)
    {
        var enter = sendLineEnding switch
        {
            LineEnding.Lf => "\n",
            LineEnding.CrLf => "\r\n",
            _ => "\r",
        };

        return Encoding.UTF8.GetBytes(enter == "\r" ? input : input.Replace("\r", enter, StringComparison.Ordinal));
    }

    /// <summary>What local echo shows: Enter starts a new line and Backspace erases the last character.</summary>
    public static byte[] Echo(string input)
    {
        var echo = new StringBuilder(input.Length + 4);
        foreach (var character in input)
        {
            switch (character)
            {
                case Enter:
                    echo.Append("\r\n");
                    break;
                case Delete or '\b':
                    echo.Append("\b \b");
                    break;
                default:
                    echo.Append(character);
                    break;
            }
        }

        return Encoding.UTF8.GetBytes(echo.ToString());
    }
}
