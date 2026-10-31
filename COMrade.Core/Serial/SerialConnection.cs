using System.IO.Ports;
using PortHandshake = System.IO.Ports.Handshake;
using PortParity = System.IO.Ports.Parity;
using PortStopBits = System.IO.Ports.StopBits;

namespace COMrade.Core.Serial;

/// <summary>A serial port opened through System.IO.Ports.</summary>
/// <remarks>
/// <para>
/// Reads and writes use the blocking <see cref="SerialPort"/> methods on a worker thread, with a
/// short read timeout. The asynchronous stream API cannot be used: the CH340 driver aborts every
/// asynchronous read that waits for data, and CP210x and ESP32 drivers ignore its timeouts.
/// A blocking read still returns as soon as any byte arrives; the timeout only sets how often the
/// read loop checks for cancellation.
/// </para>
/// <para>
/// DTR is asserted when the port opens, as most terminals do: many USB CDC devices (RP2040,
/// Arduino) only send data once DTR is set. RTS stays off because on ESP32 native USB and on
/// boards with the classic auto-reset circuit, RTS alone holds the chip in reset.
/// </para>
/// </remarks>
public sealed class SerialConnection : ISerialConnection
{
    private const int ReadPollMilliseconds = 100;
    private const int ReadBufferSize = 4096;

    private readonly SerialPort _port;
    private readonly string _portName;
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];
    private SerialSettings _settings;

    private SerialConnection(SerialPort port, SerialSettings settings)
    {
        _port = port;
        _portName = port.PortName;
        _settings = settings;
    }

    /// <summary>Opens <paramref name="portName"/>; has the shape of <see cref="OpenSerialConnection"/>.</summary>
    public static Task<ISerialConnection> OpenAsync(string portName, SerialSettings settings, CancellationToken cancellationToken) =>
        Task.Run<ISerialConnection>(() => Open(portName, settings), cancellationToken);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        new(Task.Run(() => Read(buffer, cancellationToken), cancellationToken));

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
        new(Task.Run(() => Write(data), cancellationToken));

    public void ApplySettings(SerialSettings settings)
    {
        try
        {
            Configure(_port, settings);
            _settings = settings;
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            RestoreSettings();
            throw new SerialPortException(SerialPortError.InvalidSettings, $"{_portName}: the driver rejected the line settings.", ex);
        }
    }

    // Closing can block for a long time on some drivers after the device is unplugged,
    // so it runs on a worker thread and the session decides how long to wait.
    public ValueTask DisposeAsync() => new(Task.Run(() =>
    {
        try
        {
            _port.Dispose();
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            throw SerialPortErrors.ToException(ex, _portName, opening: false);
        }
    }));

    private int Read(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var length = Math.Min(buffer.Length, _readBuffer.Length);
        while (true)
        {
            // Cancelled reads return nothing rather than throw: closing the port is the normal
            // way a session ends, and an exception leaving this thread stops the debugger.
            if (cancellationToken.IsCancellationRequested)
                return 0;

            try
            {
                var count = _port.Read(_readBuffer, 0, length);
                if (count > 0)
                {
                    // The read returns as soon as the first byte arrives; take whatever else is
                    // already waiting so a line is delivered in one piece rather than two.
                    var waiting = Math.Min(_port.BytesToRead, length - count);
                    if (waiting > 0)
                        count += _port.Read(_readBuffer, count, waiting);

                    _readBuffer.AsSpan(0, count).CopyTo(buffer.Span);
                    return count;
                }
            }
            catch (TimeoutException)
            {
                // Nothing arrived during this poll; check for cancellation and wait again.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The session closed the port while this read was waiting.
                return 0;
            }
            catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
            {
                throw SerialPortErrors.ToException(ex, _portName, opening: false);
            }
        }
    }

    private void Write(ReadOnlyMemory<byte> data)
    {
        try
        {
            _port.Write(data.ToArray(), 0, data.Length);
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            throw SerialPortErrors.ToException(ex, _portName, opening: false);
        }
    }

    private static SerialConnection Open(string portName, SerialSettings settings)
    {
        var port = new SerialPort(portName) { DtrEnable = true, ReadTimeout = ReadPollMilliseconds };
        try
        {
            Configure(port, settings);
            port.Open();
            return new SerialConnection(port, settings);
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            port.Dispose();
            throw SerialPortErrors.ToException(ex, portName, opening: true);
        }
    }

    private void RestoreSettings()
    {
        try
        {
            Configure(_port, _settings);
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            // The port keeps whatever part of the new settings the driver accepted.
        }
    }

    private static void Configure(SerialPort port, SerialSettings settings)
    {
        port.BaudRate = settings.BaudRate;
        port.DataBits = settings.DataBits;
        port.Parity = settings.Parity switch
        {
            Parity.None => PortParity.None,
            Parity.Odd => PortParity.Odd,
            Parity.Even => PortParity.Even,
            Parity.Mark => PortParity.Mark,
            Parity.Space => PortParity.Space,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Parity, "Unknown parity."),
        };
        port.StopBits = settings.StopBits switch
        {
            StopBits.One => PortStopBits.One,
            StopBits.OnePointFive => PortStopBits.OnePointFive,
            StopBits.Two => PortStopBits.Two,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.StopBits, "Unknown stop bits."),
        };
        port.Handshake = settings.FlowControl switch
        {
            FlowControl.None => PortHandshake.None,
            FlowControl.RtsCts => PortHandshake.RequestToSend,
            FlowControl.XonXoff => PortHandshake.XOnXOff,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.FlowControl, "Unknown flow control."),
        };
    }
}
