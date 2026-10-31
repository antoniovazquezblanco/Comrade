namespace COMrade.Core.Serial;

/// <summary>An open serial port.</summary>
/// <remarks>
/// An interface because sessions run against two implementations: the real port and, in tests,
/// a simulated one that can fail to open, die mid-read or hang while closing.
/// Every member throws <see cref="SerialPortException"/> when the port fails.
/// </remarks>
public interface ISerialConnection : IAsyncDisposable
{
    /// <summary>
    /// Waits for incoming bytes and copies them into <paramref name="buffer"/>; returns how many, or
    /// may return 0 once <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    /// <summary>Changes the line settings of the open port; on failure the previous settings stay in effect.</summary>
    void ApplySettings(SerialSettings settings);
}
