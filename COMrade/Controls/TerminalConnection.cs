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
    private readonly StringBuilder _pending = new();
    private bool _started;

    public event EventHandler<TerminalOutputEventArgs>? TerminalOutput;

    /// <summary>Text typed or pasted in the terminal.</summary>
    public event EventHandler<string>? InputReceived;

    /// <summary>
    /// Shows bytes in the terminal; a UTF-8 character split across calls is kept until complete.
    /// Output written before the control has started (e.g. the history of a tab that moved to
    /// another window) is kept and shown when it starts; the control would otherwise drop it.
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            var chars = new char[_decoder.GetCharCount(data, flush: false)];
            var count = _decoder.GetChars(data, chars, flush: false);
            if (count == 0)
                return;

            if (!_started)
            {
                _pending.Append(chars, 0, count);
                return;
            }

            // Raised while holding the lock, so output keeps its order with what Start sends.
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(new string(chars, 0, count)));
        }
    }

    /// <summary>Called by the control once its native terminal exists and it listens to the output.</summary>
    public void Start()
    {
        lock (_lock)
        {
            _started = true;
            if (_pending.Length == 0)
                return;
            var pending = _pending.ToString();
            _pending.Clear();
            TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(pending));
        }
    }

    public void WriteInput(string data) => InputReceived?.Invoke(this, data);

    public void Resize(uint rows, uint columns)
    {
    }

    public void Close()
    {
    }
}
