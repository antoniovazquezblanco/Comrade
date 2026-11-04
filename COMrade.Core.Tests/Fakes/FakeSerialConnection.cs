using System.Threading.Channels;
using COMrade.Core.Serial;

namespace COMrade.Core.Tests.Fakes;

/// <summary>
/// Simulated open serial port: the test feeds received bytes, failures and close behaviour, and
/// can simulate the device on the other end talking at a speed of its own.
/// </summary>
internal sealed class FakeSerialConnection(string portName, SerialSettings settings, ControlLines lines, bool hangOnClose) : ISerialConnection
{
    // What text sent at one speed looks like read at another: bytes text never holds.
    private static readonly byte[] Garbage = [0x00, 0xFE, 0x80, 0xF8];

    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly Queue<byte[]> _talk = new();
    private readonly List<byte> _written = [];

    public string PortName { get; } = portName;

    public SerialSettings Settings { get; private set; } = settings;

    /// <summary>DTR and RTS as last set, starting with those the port opened with.</summary>
    public ControlLines Lines { get; private set; } = lines;

    public List<TimeSpan> Breaks { get; } = [];

    public event EventHandler? ModemLinesChanged;

    public ModemLines ModemLines { get; private set; }

    public bool RejectSettings { get; set; }

    /// <summary>Speeds the driver refuses.</summary>
    public HashSet<int> UnsupportedBaudRates { get; } = [];

    /// <summary>The speed the device talks at: read at any other, what it sends comes out as garbage. Null for any speed.</summary>
    public int? DeviceBaudRate { get; set; }

    /// <summary>What the device answers to Enter received at its speed, such as its prompt.</summary>
    public byte[]? Prompt { get; set; }

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

    /// <summary>
    /// Simulates the device printing <paramref name="text"/> <paramref name="times"/> times without
    /// a pause, as in a boot log: each read returns it once, however long since the last read.
    /// </summary>
    public void Talk(byte[] text, int times)
    {
        lock (_talk)
        {
            for (var i = 0; i < times; i++)
                _talk.Enqueue(text);
        }
    }

    /// <summary>Simulates the device changing its input lines.</summary>
    public void ChangeModemLines(ModemLines lines)
    {
        ModemLines = lines;
        ModemLinesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Simulates the port failing while a read is waiting.</summary>
    public void Fail(SerialPortError error) =>
        _incoming.Writer.TryComplete(new SerialPortException(error, $"{PortName}: {error}"));

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            byte[]? data;
            lock (_talk)
                _talk.TryDequeue(out data);

            data = AsHeard(data ?? await _incoming.Reader.ReadAsync(cancellationToken));
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

        if (Prompt is { } prompt && IsAtDeviceSpeed && data.Span.IndexOfAny((byte)'\r', (byte)'\n') >= 0)
            Receive(prompt);
        return ValueTask.CompletedTask;
    }

    public void ApplySettings(SerialSettings settings)
    {
        if (RejectSettings || UnsupportedBaudRates.Contains(settings.BaudRate))
            throw new SerialPortException(SerialPortError.InvalidSettings, $"{PortName}: settings rejected");
        Settings = settings;
    }

    public void DiscardInput()
    {
        while (_incoming.Reader.TryRead(out _))
        {
        }
    }

    public void SetControlLines(ControlLines lines)
    {
        if (FailWrites)
            throw new SerialPortException(SerialPortError.DeviceRemoved, $"{PortName}: setting the lines failed");
        Lines = lines;
    }

    public Task SendBreakAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (FailWrites)
            throw new SerialPortException(SerialPortError.DeviceRemoved, $"{PortName}: break failed");
        Breaks.Add(duration);
        return Task.CompletedTask;
    }

    private bool IsAtDeviceSpeed => DeviceBaudRate is null || DeviceBaudRate == Settings.BaudRate;

    private byte[] AsHeard(byte[] sent) =>
        IsAtDeviceSpeed ? sent : [.. sent.Select((_, i) => Garbage[i % Garbage.Length])];

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        _incoming.Writer.TryComplete();
        return hangOnClose ? new ValueTask(new TaskCompletionSource().Task) : ValueTask.CompletedTask;
    }
}
