namespace COMrade.Core.Terminal;

/// <summary>
/// Turns bare CR and bare LF in received data into CR+LF, according to <see cref="TerminalSettings"/>,
/// so lines from devices that end them with only one of the two still start on a new line.
/// </summary>
/// <remarks>
/// Keeps the last byte of each chunk, so a CR+LF split across two reads is not doubled. With
/// <see cref="TerminalSettings.NewLineOnCr"/>, the new line for a CR at the very end of a chunk is
/// added when the next byte arrives, because only then is it known not to be followed by LF.
/// </remarks>
public sealed class ReceivedLineEndings(TerminalSettings settings)
{
    private const byte Cr = (byte)'\r';
    private const byte Lf = (byte)'\n';

    private bool _afterCr;

    /// <summary>Can be changed while data is flowing; applies from the next chunk.</summary>
    public TerminalSettings Settings { get; set; } = settings;

    public byte[] Convert(ReadOnlySpan<byte> data)
    {
        var settings = Settings;
        if (!settings.NewLineOnLf && !settings.NewLineOnCr)
        {
            if (!data.IsEmpty)
                _afterCr = data[^1] == Cr;
            return data.ToArray();
        }

        var output = new List<byte>(data.Length + 8);
        foreach (var value in data)
        {
            if (_afterCr && value != Lf && settings.NewLineOnCr)
                output.Add(Lf);
            if (value == Lf && !_afterCr && settings.NewLineOnLf)
                output.Add(Cr);

            output.Add(value);
            _afterCr = value == Cr;
        }

        return [.. output];
    }
}
