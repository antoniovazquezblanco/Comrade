using System;
using System.Text;
using System.Threading;
using Microsoft.Terminal.Wpf;

namespace COMrade.Controls;

/// <summary>
/// Data source for Windows Terminal's control: output comes from the device instead of a shell
/// process, and typed text is handed back to <see cref="TerminalView"/>.
/// </summary>
internal sealed class TerminalConnection : ITerminalConnection
{
    private readonly Lock _lock = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

    /// <summary>Text typed or pasted in the terminal.</summary>
    public event EventHandler<string>? InputReceived;

    /// <summary>Shows bytes in the terminal; a UTF-8 character split across calls is kept until complete.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        string text;
        lock (_lock)
        {
            var chars = new char[_decoder.GetCharCount(data, flush: false)];
            var count = _decoder.GetChars(data, chars, flush: false);
            if (count == 0)
                return;
            text = new string(chars, 0, count);
        }

        TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(text));
    }

    public void Start()
    {
    }

    public void WriteInput(string data) => InputReceived?.Invoke(this, data);

    public void Resize(uint rows, uint columns)
    {
    }

    public void Close()
    {
    }
}
