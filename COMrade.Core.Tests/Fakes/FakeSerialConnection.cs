using System.Threading.Channels;
using COMrade.Core.Serial;

namespace COMrade.Core.Tests.Fakes;

/// <summary>Simulated open serial port: the test feeds received bytes, failures and close behaviour.</summary>
internal sealed class FakeSerialConnection(string portName, SerialSettings settings, bool hangOnClose) : ISerialConnection
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte> _written = [];

    public string PortName { get; } = portName;

    public SerialSettings Settings { get; private set; } = settings;

    public bool RejectSettings { get; set; }

    public bool FailWrites { get; set; }

    public bool IsDisposed { get; private set; }

    public byte[] Written
    {
        get
        {
            lock (_written)
                return [.. _written];
        }
    }

    /// <summary>Simulates bytes arriving from the device.</summary>
    public void Receive(params byte[] data) => _incoming.Writer.TryWrite(data);

    /// <summary>Simulates the port failing while a read is waiting.</summary>
    public void Fail(SerialPortError error) =>
        _incoming.Writer.TryComplete(new SerialPortException(error, $"{PortName}: {error}"));

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            var data = await _incoming.Reader.ReadAsync(cancellationToken);
            data.CopyTo(buffer);
            return data.Length;
        }
        catch (ChannelClosedException ex)
        {
            throw ex.InnerException as SerialPortException
                ?? new SerialPortException(SerialPortError.IoFailure, $"{PortName} was closed.");
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (FailWrites)
            throw new SerialPortException(SerialPortError.DeviceRemoved, $"{PortName}: write failed");

        lock (_written)
            _written.AddRange(data.ToArray());
        return ValueTask.CompletedTask;
    }

    public void ApplySettings(SerialSettings settings)
    {
        if (RejectSettings)
            throw new SerialPortException(SerialPortError.InvalidSettings, $"{PortName}: settings rejected");
        Settings = settings;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        _incoming.Writer.TryComplete();
        return hangOnClose ? new ValueTask(new TaskCompletionSource().Task) : ValueTask.CompletedTask;
    }
}
