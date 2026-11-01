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
/// The control lines are set before the port opens, so a device that resets on a DTR or RTS edge
/// sees no pulse when it opens with them off. See <see cref="ControlLines"/> for the defaults.
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
    private ControlLines _lines;

    private SerialConnection(SerialPort port, SerialSettings settings, ControlLines lines)
    {
        _port = port;
        _portName = port.PortName;
        _settings = settings;
        _lines = lines;
        _port.PinChanged += OnPinChanged;
    }

    public event EventHandler? ModemLinesChanged;

    public ModemLines ModemLines
    {
        get
        {
            try
            {
                return (_port.CtsHolding ? ModemLines.Cts : 0)
                    | (_port.DsrHolding ? ModemLines.Dsr : 0)
                    | (_port.CDHolding ? ModemLines.CarrierDetect : 0);
            }
            catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
            {
                // The port is failing; the read loop reports it.
                return ModemLines.None;
            }
        }
    }

    /// <summary>Opens <paramref name="portName"/>; has the shape of <see cref="OpenSerialConnection"/>.</summary>
    public static Task<ISerialConnection> OpenAsync(string portName, SerialSettings settings, ControlLines lines, CancellationToken cancellationToken) =>
        Task.Run<ISerialConnection>(() => Open(portName, settings, lines), cancellationToken);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        new(Task.Run(() => Read(buffer, cancellationToken), cancellationToken));

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
        new(Task.Run(() => Write(data), cancellationToken));

    public void ApplySettings(SerialSettings settings)
    {
        try
        {
            Configure(_port, settings);
            SetLines(_port, settings, _lines);
            _settings = settings;
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            RestoreSettings();
            throw new SerialPortException(SerialPortError.InvalidSettings, $"{_portName}: the driver rejected the line settings.", ex);
        }
    }

    public void SetControlLines(ControlLines lines)
    {
        try
        {
            SetLines(_port, _settings, lines);
            _lines = lines;
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            throw SerialPortErrors.ToException(ex, _portName, opening: false);
        }
    }

    public Task SendBreakAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        try
        {
            _port.BreakState = true;
            try
            {
                await Task.Delay(duration, cancellationToken);
            }
            finally
            {
                _port.BreakState = false;
            }
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            throw SerialPortErrors.ToException(ex, _portName, opening: false);
        }
    }, cancellationToken);

    // Closing can block for a long time on some drivers after the device is unplugged,
    // so it runs on a worker thread and the session decides how long to wait.
    public ValueTask DisposeAsync() => new(Task.Run(() =>
    {
        try
        {
            _port.PinChanged -= OnPinChanged;
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

    private static SerialConnection Open(string portName, SerialSettings settings, ControlLines lines)
    {
        var port = new SerialPort(portName) { ReadTimeout = ReadPollMilliseconds };
        try
        {
            Configure(port, settings);
            SetLines(port, settings, lines);
            port.Open();
            return new SerialConnection(port, settings, lines);
        }
        catch (Exception ex) when (SerialPortErrors.IsPortFailure(ex))
        {
            port.Dispose();
            throw SerialPortErrors.ToException(ex, portName, opening: true);
        }
    }

    // Ring is not reported, so its changes are ignored.
    private void OnPinChanged(object sender, SerialPinChangedEventArgs e)
    {
        if (e.EventType is not SerialPinChange.Ring)
            ModemLinesChanged?.Invoke(this, EventArgs.Empty);
    }

    // RTS cannot be set while RTS/CTS flow control drives it.
    private static void SetLines(SerialPort port, SerialSettings settings, ControlLines lines)
    {
        port.DtrEnable = lines.Dtr;
        if (settings.FlowControl is not FlowControl.RtsCts)
            port.RtsEnable = lines.Rts;
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
