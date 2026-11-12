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
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Lock _deviceEventsLock = new();
    private Task _deviceEvents = Task.CompletedTask;
    private ISerialConnection? _connection;
    private CancellationTokenSource? _readCancellation;
    private Task _readLoop = Task.CompletedTask;
    private bool _isDetecting;
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

    /// <summary>The raw bytes received, across reopens, for AI assistants.</summary>
    public ReceiveBuffer Received { get; } = new();

    /// <summary>What went over the port both ways, across reopens, marked with who sent it, for AI assistants.</summary>
    public Traffic Traffic { get; } = new();

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

    /// <summary>
    /// Sends bytes to the device, waiting between bytes and lines as the settings say; returns
    /// false when the port is not open, closes while a paced send is under way, or the write failed.
    /// </summary>
    /// <remarks>One send at a time, so a paced send is not interleaved with the next one.</remarks>
    /// <param name="from">Who sends, for <see cref="Traffic"/>, where the bytes go as they go out, before the device can answer them.</param>
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, TrafficSender from)
    {
        var connection = _connection;
        if (connection is null)
            return false;

        await _sendGate.WaitAsync();
        try
        {
            var settings = Settings;
            if (!settings.IsPaced)
            {
                Record(from, data.Span);
                await connection.WriteAsync(data, CancellationToken.None);
                Interlocked.Add(ref _bytesSent, data.Length);
                return true;
            }

            for (var index = 0; index < data.Length; index++)
            {
                if (_connection != connection)
                    return false;

                Record(from, data.Span.Slice(index, 1));
                await connection.WriteAsync(data.Slice(index, 1), CancellationToken.None);
                Interlocked.Increment(ref _bytesSent);

                var delay = EndsLine(data.Span, index) ? settings.LineDelayMilliseconds : settings.CharacterDelayMilliseconds;
                // Also after the last byte, so the next send keeps the pace.
                if (delay > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), _time);
            }

            return true;
        }
        catch (SerialPortException ex)
        {
            await ConnectionLostAsync(connection, ex.Error);
            return false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void Record(TrafficSender from, ReadOnlySpan<byte> data) => Traffic.Append(from, data, _time.GetUtcNow());

    // A line ends with LF, or with a CR that no LF follows.
    private static bool EndsLine(ReadOnlySpan<byte> data, int index) =>
        data[index] == (byte)'\n' || (data[index] == (byte)'\r' && (index + 1 == data.Length || data[index + 1] != (byte)'\n'));

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
    /// Finds the speed the device talks at (see <see cref="BaudRateDetector"/>) and keeps it on the
    /// open port; what the device sent at that speed is then reported as received. Meanwhile the
    /// detection reads the port, so nothing is reported, and settings applied wait for it to end.
    /// </summary>
    /// <param name="wake">Bytes to send at each speed, see <see cref="BaudRateDetector.DetectAsync"/>; counted as sent.</param>
    /// <returns>
    /// <see cref="BaudRateDetection.Stopped"/> when the port is not open, closes or fails, or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </returns>
    public async Task<BaudRateDetection> DetectBaudRateAsync(
        Func<ReadOnlyMemory<byte>>? wake = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ISerialConnection connection;
        Task readLoop;
        CancellationToken lifetime;
        await _gate.WaitAsync();
        try
        {
            if (_connection is null || _readCancellation is null || _isDetecting)
                return BaudRateDetection.Stopped;

            // The read loop stops and the detection reads in its place. Closing the port cancels
            // the new token, as it would have cancelled the read loop's.
            connection = _connection;
            readLoop = _readLoop;
            var stopReading = _readCancellation;
            _readCancellation = new CancellationTokenSource();
            lifetime = _readCancellation.Token;
            stopReading.Cancel();
            _isDetecting = true;
        }
        finally
        {
            _gate.Release();
        }

        var detection = BaudRateDetection.Stopped;
        SerialPortError? failure = null;
        try
        {
            // Outside the gate: a read loop that has just failed takes it to report the lost connection.
            await readLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            // Off the caller's thread: each change of speed is a driver call that can take a moment.
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
            var settings = Settings;
            detection = await Task.Run(
                () => BaudRateDetector.DetectAsync(connection, settings, CountAsSent(wake), progress, _time, stop.Token),
                CancellationToken.None);
        }
        catch (SerialPortException ex) when (!lifetime.IsCancellationRequested)
        {
            failure = ex.Error;
        }
        catch (SerialPortException)
        {
            // The session closed the port while the detection was reading.
        }
        finally
        {
            // Also after an unexpected error, so that the port does not stay without a read loop.
            detection = await ResumeReadingAsync(connection, failure is null ? detection : null);
        }

        if (failure is { } error)
            await ConnectionLostAsync(connection, error);
        return detection;
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
            // While detecting, the detection sets the port's speed; these settings apply when it ends.
            if (_connection is not null && !_isDetecting)
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

        StartReading(connection, _readCancellation.Token);
        return null;
    }

    // The read loop takes over from the detection again, at the detected speed. Without a detection
    // (the port failed) or once the port closed, it does not, and the detection counts as stopped.
    private async Task<BaudRateDetection> ResumeReadingAsync(ISerialConnection connection, BaudRateDetection? detection)
    {
        await _gate.WaitAsync();
        try
        {
            _isDetecting = false;
            if (detection is null || _connection != connection || _readCancellation is not { } cancellation)
                return BaudRateDetection.Stopped;

            detection = KeepBaudRate(connection, detection);
            StartReading(connection, cancellation.Token, detection.Output);
            return detection;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Must be called holding the gate. The port goes to the detected speed, or back to the
    // session's settings, which may have changed during the detection.
    private BaudRateDetection KeepBaudRate(ISerialConnection connection, BaudRateDetection detection)
    {
        var settings = detection.BaudRate is { } baudRate ? Settings with { BaudRate = baudRate } : Settings;
        try
        {
            connection.ApplySettings(settings);
            Settings = settings;
            return detection;
        }
        catch (SerialPortException ex)
        {
            LastError = ex.Error;
            return BaudRateDetection.Stopped;
        }
    }

    // Bytes the detection sends count as sent, like typed ones, and as COMrade's own.
    private Func<ReadOnlyMemory<byte>>? CountAsSent(Func<ReadOnlyMemory<byte>>? wake) => wake is null ? null : () =>
    {
        var bytes = wake();
        Record(TrafficSender.Comrade, bytes.Span);
        Interlocked.Add(ref _bytesSent, bytes.Length);
        return bytes;
    };

    // Must be called holding the gate. Reports first, when given, before anything read.
    private void StartReading(ISerialConnection connection, CancellationToken cancellation, byte[]? first = null) =>
        _readLoop = Task.Run(() => ReadLoopAsync(connection, first, cancellation));

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

    private async Task ReadLoopAsync(ISerialConnection connection, byte[]? first, CancellationToken cancellation)
    {
        if (first is { Length: > 0 })
            Report(first);

        var buffer = new byte[ReadBufferSize];
        try
        {
            while (true)
            {
                var count = await connection.ReadAsync(buffer, cancellation);
                if (cancellation.IsCancellationRequested)
                    return;

                Report(buffer[..count]);
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

    private void Report(byte[] data)
    {
        // The traffic first: whoever sees the bytes in Received finds them there too.
        Interlocked.Add(ref _bytesReceived, data.Length);
        Record(TrafficSender.Device, data);
        Received.Append(data);
        DataReceived?.Invoke(this, data);
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
