using COMrade.Core.Ports;
using COMrade.Core.Serial;

namespace COMrade.Core.Sessions;

/// <summary>
/// One device tab: opens and closes its serial port, moves data, and reopens the port automatically
/// when the device is unplugged and comes back, even under a different COM number.
/// </summary>
public sealed class PortSession : IAsyncDisposable
{
    /// <summary>How long to wait for a driver to close a port before abandoning the handle.</summary>
    internal static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Wait before each reopen attempt once the device is back. Drivers refuse to open the port
    /// for a moment after the device appears (seen on CH340, CP210x and ESP32 USB alike), so even
    /// the first attempt waits a little.
    /// </summary>
    internal static readonly TimeSpan[] ReopenDelays =
        [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private const int ReadBufferSize = 4096;

    private readonly PortList _ports;
    private readonly OpenSerialConnection _openConnection;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _deviceEventsLock = new();
    private Task _deviceEvents = Task.CompletedTask;
    private ISerialConnection? _connection;
    private CancellationTokenSource? _readCancellation;
    private long _bytesReceived;
    private long _bytesSent;
    private volatile bool _isDevicePresent;

    public PortSession(
        SerialPortInfo port,
        SerialSettings settings,
        PortList ports,
        OpenSerialConnection openConnection,
        TimeProvider? time = null)
    {
        Port = port;
        Settings = settings;
        _ports = ports;
        _openConnection = openConnection;
        _time = time ?? TimeProvider.System;

        _ports.PortArrived += OnPortArrived;
        _ports.PortRemoved += OnPortRemoved;
        _isDevicePresent = _ports.Ports.Any(present => present.DeviceId == port.DeviceId);
    }

    /// <summary>The state changed; read <see cref="State"/> and <see cref="LastError"/>.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The device came back as a different port (e.g. a new COM number); read <see cref="Port"/>.</summary>
    public event EventHandler? PortChanged;

    /// <summary>Bytes arrived from the device. Raised on a background thread.</summary>
    public event EventHandler<byte[]>? DataReceived;

    /// <summary>An input line of the open port changed; read <see cref="ModemLines"/>. Raised on a background thread.</summary>
    public event EventHandler? ModemLinesChanged;

    /// <summary>The port this session talks to; follows the device when its COM number changes.</summary>
    public SerialPortInfo Port { get; private set; }

    public SerialSettings Settings { get; private set; }

    public bool ReopenAutomatically { get; set; } = true;

    /// <summary>DTR and RTS as chosen for this session; set again each time the port opens.</summary>
    public ControlLines ControlLines { get; private set; } = ControlLines.Default;

    /// <summary>The input lines of the open port; none while it is not open.</summary>
    public ModemLines ModemLines => _connection?.ModemLines ?? ModemLines.None;

    public PortSessionState State { get; private set; } = PortSessionState.Closed;

    /// <summary>Why the last open or the open port failed; null after a successful open or a close.</summary>
    public SerialPortError? LastError { get; private set; }

    public bool IsDevicePresent => _isDevicePresent;

    /// <summary>When the port was opened; null while it is not open.</summary>
    public DateTimeOffset? OpenedAt { get; private set; }

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>Completes when the device arrivals and removals reported so far have been handled.</summary>
    internal Task DeviceEventsHandled
    {
        get
        {
            lock (_deviceEventsLock)
                return _deviceEvents;
        }
    }

    /// <summary>Opens the port. Also used for "Try again" after an error and "Reconnect now" while waiting.</summary>
    public async Task OpenAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State is PortSessionState.Open)
                return;

            var error = await ConnectAsync();
            if (error is not null)
                SetState(IsDeviceGone(error.Value) && ReopenAutomatically ? PortSessionState.Waiting : PortSessionState.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the port. A closed session does not reopen when the device comes back.</summary>
    public async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DisconnectAsync();
            LastError = null;
            SetState(PortSessionState.Closed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sends bytes to the device; returns false when the port is not open or the write failed.</summary>
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data)
    {
        var connection = _connection;
        if (connection is null)
            return false;

        try
        {
            await connection.WriteAsync(data, CancellationToken.None);
        }
        catch (SerialPortException ex)
        {
            await ConnectionLostAsync(connection, ex.Error);
            return false;
        }

        Interlocked.Add(ref _bytesSent, data.Length);
        return true;
    }

    /// <summary>
    /// Sets DTR and RTS, on the open port if there is one, and keeps them for the next time it opens.
    /// Returns false when the open port failed to set them.
    /// </summary>
    public async Task<bool> SetControlLinesAsync(ControlLines lines)
    {
        ISerialConnection? connection;
        SerialPortError error;
        await _gate.WaitAsync();
        try
        {
            ControlLines = lines;
            connection = _connection;
            if (connection is null)
                return true;

            try
            {
                connection.SetControlLines(lines);
                return true;
            }
            catch (SerialPortException ex)
            {
                error = ex.Error;
            }
        }
        finally
        {
            _gate.Release();
        }

        await ConnectionLostAsync(connection, error);
        return false;
    }

    /// <summary>Sends a break of <paramref name="duration"/>; returns false when the port is not open or it failed.</summary>
    public async Task<bool> SendBreakAsync(TimeSpan duration)
    {
        var connection = _connection;
        if (connection is null)
            return false;

        try
        {
            await connection.SendBreakAsync(duration, CancellationToken.None);
            return true;
        }
        catch (SerialPortException ex)
        {
            await ConnectionLostAsync(connection, ex.Error);
            return false;
        }
    }

    /// <summary>
    /// Changes the line settings, on the open port if there is one. Returns false, keeping the
    /// previous settings, when the driver rejects them.
    /// </summary>
    public async Task<bool> ApplySettingsAsync(SerialSettings settings)
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                try
                {
                    _connection.ApplySettings(settings);
                }
                catch (SerialPortException ex)
                {
                    LastError = ex.Error;
                    return false;
                }
            }

            Settings = settings;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ports.PortArrived -= OnPortArrived;
        _ports.PortRemoved -= OnPortRemoved;
        await CloseAsync();
    }

    // Must be called holding the gate. Leaves the session Open on success; on failure returns
    // the error and leaves choosing the next state to the caller.
    private async Task<SerialPortError?> ConnectAsync()
    {
        SetState(PortSessionState.Opening);

        ISerialConnection connection;
        try
        {
            connection = await _openConnection(Port.PortName, Settings, ControlLines, CancellationToken.None);
        }
        catch (SerialPortException ex)
        {
            LastError = ex.Error;
            return ex.Error;
        }

        _connection = connection;
        connection.ModemLinesChanged += OnModemLinesChanged;
        _readCancellation = new CancellationTokenSource();
        LastError = null;
        OpenedAt = _time.GetUtcNow();
        SetState(PortSessionState.Open);

        var cancellation = _readCancellation.Token;
        _ = Task.Run(() => ReadLoopAsync(connection, cancellation));
        return null;
    }

    // Must be called holding the gate.
    private async Task DisconnectAsync()
    {
        var connection = _connection;
        if (connection is null)
            return;

        _connection = null;
        connection.ModemLinesChanged -= OnModemLinesChanged;
        OpenedAt = null;
        _readCancellation?.Cancel();
        _readCancellation = null;

        try
        {
            await connection.DisposeAsync().AsTask().WaitAsync(CloseTimeout, _time);
        }
        catch (TimeoutException)
        {
            // Some drivers never finish closing a port whose device vanished; abandon the handle.
        }
        catch (SerialPortException)
        {
            // Closing a port that has already failed is expected to fail as well.
        }
    }

    private async Task ReadLoopAsync(ISerialConnection connection, CancellationToken cancellation)
    {
        var buffer = new byte[ReadBufferSize];
        try
        {
            while (true)
            {
                var count = await connection.ReadAsync(buffer, cancellation);
                if (cancellation.IsCancellationRequested)
                    return;

                Interlocked.Add(ref _bytesReceived, count);
                DataReceived?.Invoke(this, buffer[..count]);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (SerialPortException ex) when (!cancellation.IsCancellationRequested)
        {
            await ConnectionLostAsync(connection, ex.Error);
        }
        catch (SerialPortException)
        {
            // The session closed the port while this read was waiting.
        }
    }

    private async Task ConnectionLostAsync(ISerialConnection connection, SerialPortError error)
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection != connection)
                return;

            await DisconnectAsync();
            LastError = error;
            SetState(IsDeviceGone(error) && ReopenAutomatically ? PortSessionState.Waiting : PortSessionState.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnModemLinesChanged(object? sender, EventArgs e) => ModemLinesChanged?.Invoke(this, EventArgs.Empty);

    private bool IsDeviceGone(SerialPortError error) =>
        error is SerialPortError.DeviceRemoved or SerialPortError.NotFound || !_isDevicePresent;

    // Port list events arrive on the watcher thread while the list is locked, so the work is
    // queued and handled in arrival order on a background thread.
    private void OnPortRemoved(object? sender, SerialPortInfo port)
    {
        if (!string.Equals(port.DeviceId, Port.DeviceId, StringComparison.OrdinalIgnoreCase))
            return;

        _isDevicePresent = false;
        QueueDeviceEvent(DeviceRemovedAsync);
    }

    private void OnPortArrived(object? sender, SerialPortInfo port)
    {
        if (!IsThisDevice(port))
            return;

        _isDevicePresent = true;
        QueueDeviceEvent(() => DeviceArrivedAsync(port));
    }

    // Exact identity, or the moved-socket fallback: an adapter without a serial number of its own is
    // known only by its USB socket, so after it disappears the only adapter of the same model
    // without a serial number is taken to be it, plugged into another socket.
    private bool IsThisDevice(SerialPortInfo port)
    {
        var identity = Port.Identity;
        if (port.Identity.IsSameDeviceAs(identity))
            return true;

        return !_isDevicePresent
            && identity.IsSameModelWithoutSerial(port.Identity)
            && _ports.Ports.Count(present => identity.IsSameModelWithoutSerial(present.Identity)) == 1;
    }

    private void QueueDeviceEvent(Func<Task> handle)
    {
        lock (_deviceEventsLock)
            _deviceEvents = _deviceEvents.ContinueWith(_ => handle(), TaskScheduler.Default).Unwrap();
    }

    private async Task DeviceRemovedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State is PortSessionState.Open)
            {
                await DisconnectAsync();
                LastError = SerialPortError.DeviceRemoved;
                SetState(ReopenAutomatically ? PortSessionState.Waiting : PortSessionState.Error);
            }
            else if (State is PortSessionState.Error && ReopenAutomatically)
            {
                // The failure that came just before was the device going away.
                SetState(PortSessionState.Waiting);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DeviceArrivedAsync(SerialPortInfo port)
    {
        await _gate.WaitAsync();
        try
        {
            if (port != Port)
            {
                Port = port;
                PortChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _gate.Release();
        }

        await ReopenAsync();
    }

    private async Task ReopenAsync()
    {
        for (var attempt = 0; attempt < ReopenDelays.Length; attempt++)
        {
            if (ReopenDelays[attempt] > TimeSpan.Zero)
                await Task.Delay(ReopenDelays[attempt], _time);

            await _gate.WaitAsync();
            try
            {
                if (State is not PortSessionState.Waiting || !_isDevicePresent)
                    return;

                var error = await ConnectAsync();
                if (error is null)
                    return;

                var gaveUp = attempt == ReopenDelays.Length - 1 && _isDevicePresent;
                SetState(gaveUp ? PortSessionState.Error : PortSessionState.Waiting);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private void SetState(PortSessionState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
