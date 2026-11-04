using COMrade.Core.Serial;

namespace COMrade.IntegrationTests;

public class SerialConnectionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opening_a_port_that_does_not_exist_reports_not_found()
    {
        var exception = await Assert.ThrowsAsync<SerialPortException>(
            () => SerialConnection.OpenAsync("COM250", SerialSettings.Default, ControlLines.Default, Cancellation));

        Assert.Equal(SerialPortError.NotFound, exception.Error);
    }

    [Fact]
    public async Task Real_port_opens_and_closes()
    {
        var connection = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation);

        await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
    }

    [Fact]
    public async Task Opening_a_port_that_is_already_open_reports_in_use()
    {
        await using var first = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation);

        var exception = await Assert.ThrowsAsync<SerialPortException>(
            () => SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation));

        Assert.Equal(SerialPortError.InUse, exception.Error);
    }

    [Fact]
    public async Task Closing_the_port_ends_a_waiting_read_quietly()
    {
        var connection = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation);
        using var session = new CancellationTokenSource();
        var read = connection.ReadAsync(new byte[256], session.Token).AsTask();
        await Task.Delay(300, Cancellation);

        // As a session does on Close port: cancel the read, then close.
        await session.CancelAsync();
        await connection.DisposeAsync();

        Assert.Equal(0, await read.WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
    }

    [Fact]
    public async Task Port_reopens_after_being_closed()
    {
        var first = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation);
        await first.DisposeAsync();

        await using var second = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation);
    }

    [Fact]
    public async Task Settings_change_on_an_open_port()
    {
        await using var connection = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation);

        connection.ApplySettings(SerialSettings.Default with { BaudRate = 9600, Parity = Parity.Even });
        connection.ApplySettings(SerialSettings.Default with { BaudRate = 921600 });
    }

    [Fact]
    public async Task Rejected_settings_leave_the_port_usable()
    {
        await using var connection = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, ControlLines.Default, Cancellation);

        var exception = Assert.Throws<SerialPortException>(
            () => connection.ApplySettings(SerialSettings.Default with { BaudRate = 0 }));

        Assert.Equal(SerialPortError.InvalidSettings, exception.Error);
        connection.ApplySettings(SerialSettings.Default with { BaudRate = 57600 });
    }

    [Theory]
    [InlineData(9600)]
    [InlineData(115200)]
    [InlineData(921600)]
    public async Task Data_sent_on_a_loopback_port_comes_back_intact(int baudRate)
    {
        await using var connection = await SerialConnection.OpenAsync(
            TestPorts.Loopback, SerialSettings.Default with { BaudRate = baudRate }, ControlLines.Default, Cancellation);
        var sent = new byte[baudRate >= 115200 ? 8192 : 1024];
        Random.Shared.NextBytes(sent);

        await connection.WriteAsync(sent, Cancellation);
        var received = await ReadExactlyAsync(connection, sent.Length, TimeSpan.FromSeconds(10));

        Assert.Equal(sent, received);
    }

    [Fact]
    public async Task Discarding_input_drops_what_arrived_but_was_not_read()
    {
        await using var connection = await SerialConnection.OpenAsync(TestPorts.Loopback, SerialSettings.Default, ControlLines.Default, Cancellation);
        await connection.WriteAsync("dropped"u8.ToArray(), Cancellation);
        await Task.Delay(200, Cancellation);

        connection.DiscardInput();
        await connection.WriteAsync("kept"u8.ToArray(), Cancellation);

        Assert.Equal("kept"u8.ToArray(), await ReadExactlyAsync(connection, 4, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Detection_on_a_loopback_port_finds_the_speed_it_hears_text_at()
    {
        await using var connection = await SerialConnection.OpenAsync(TestPorts.Loopback, SerialSettings.Default, ControlLines.Default, Cancellation);
        var speed = 0;

        // The port talks to itself, so text sent comes back at any speed: send it at one speed only.
        var detection = await BaudRateDetector.DetectAsync(
            connection,
            SerialSettings.Default,
            wake: () => speed == 57600 ? "U-Boot 2020.01 (Jan 06 2020)\r\n=> "u8.ToArray() : ReadOnlyMemory<byte>.Empty,
            new SynchronousProgress(baudRate => speed = baudRate),
            TimeProvider.System,
            Cancellation);

        Assert.Equal(57600, detection.BaudRate);
        Assert.Equal(57600, speed);
    }

    private static async Task<byte[]> ReadExactlyAsync(ISerialConnection connection, int length, TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        deadline.CancelAfter(timeout);

        var received = new byte[length];
        var total = 0;
        while (total < length)
            total += await connection.ReadAsync(received.AsMemory(total), deadline.Token);
        return received;
    }

    // Reported on the detection's thread, before it sends and listens at the speed.
    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
