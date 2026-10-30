using COMrade.Core.Serial;

namespace COMrade.Core.Tests.Fakes;

/// <summary>Opens <see cref="FakeSerialConnection"/>s; the test decides which opens fail.</summary>
internal sealed class FakeSerialDriver
{
    private readonly Lock _lock = new();
    private readonly Queue<SerialPortError> _openFailures = new();
    private readonly List<FakeSerialConnection> _connections = [];

    /// <summary>New connections never finish closing, like some drivers after the device vanished.</summary>
    public bool HangOnClose { get; set; }

    public IReadOnlyList<FakeSerialConnection> Connections
    {
        get
        {
            lock (_lock)
                return [.. _connections];
        }
    }

    public FakeSerialConnection Current => Connections[^1];

    public void FailNextOpen(SerialPortError error, int times = 1)
    {
        lock (_lock)
        {
            for (var i = 0; i < times; i++)
                _openFailures.Enqueue(error);
        }
    }

    public Task<ISerialConnection> OpenAsync(string portName, SerialSettings settings, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_openFailures.TryDequeue(out var error))
                return Task.FromException<ISerialConnection>(new SerialPortException(error, $"{portName}: {error}"));

            var connection = new FakeSerialConnection(portName, settings, HangOnClose);
            _connections.Add(connection);
            return Task.FromResult<ISerialConnection>(connection);
        }
    }
}
