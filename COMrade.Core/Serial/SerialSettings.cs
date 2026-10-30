namespace COMrade.Core.Serial;

/// <summary>Line settings for a serial port.</summary>
public sealed record SerialSettings(
    int BaudRate,
    int DataBits,
    Parity Parity,
    StopBits StopBits,
    FlowControl FlowControl,
    LineEnding SendLineEnding)
{
    /// <summary>Settings used for a device COMrade has not seen before.</summary>
    public static SerialSettings Default { get; } =
        new(115200, 8, Parity.None, StopBits.One, FlowControl.None, LineEnding.Cr);

    /// <summary>Compact frame notation, such as "8N1" or "7E1.5".</summary>
    public string FrameNotation => $"{DataBits}{ParityLetter(Parity)}{StopBitsText(StopBits)}";

    private static char ParityLetter(Parity parity) => parity switch
    {
        Parity.None => 'N',
        Parity.Odd => 'O',
        Parity.Even => 'E',
        Parity.Mark => 'M',
        Parity.Space => 'S',
        _ => throw new ArgumentOutOfRangeException(nameof(parity), parity, null),
    };

    private static string StopBitsText(StopBits stopBits) => stopBits switch
    {
        StopBits.One => "1",
        StopBits.OnePointFive => "1.5",
        StopBits.Two => "2",
        _ => throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, null),
    };
}
