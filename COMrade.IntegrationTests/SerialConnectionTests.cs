using COMrade.Core.Serial;

namespace COMrade.IntegrationTests;

public class SerialConnectionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opening_a_port_that_does_not_exist_reports_not_found()
    {
        var exception = await Assert.ThrowsAsync<SerialPortException>(
            () => SerialConnection.OpenAsync("COM250", SerialSettings.Default, Cancellation));

        Assert.Equal(SerialPortError.NotFound, exception.Error);
    }

    [Fact]
    public async Task Real_port_opens_and_closes()
    {
        var connection = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, Cancellation);

        await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
    }

    [Fact]
    public async Task Opening_a_port_that_is_already_open_reports_in_use()
    {
        await using var first = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, Cancellation);

        var exception = await Assert.ThrowsAsync<SerialPortException>(
            () => SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, Cancellation));

        Assert.Equal(SerialPortError.InUse, exception.Error);
    }

    [Fact]
    public async Task Port_reopens_after_being_closed()
    {
        var first = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, Cancellation);
        await first.DisposeAsync();

        await using var second = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, Cancellation);
    }

    [Fact]
    public async Task Settings_change_on_an_open_port()
    {
        await using var connection = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, Cancellation);

        connection.ApplySettings(SerialSettings.Default with { BaudRate = 9600, Parity = Parity.Even });
        connection.ApplySettings(SerialSettings.Default with { BaudRate = 921600 });
    }

    [Fact]
    public async Task Rejected_settings_leave_the_port_usable()
    {
        await using var connection = await SerialConnection.OpenAsync(TestPorts.Any, SerialSettings.Default, Cancellation);

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
            TestPorts.Loopback, SerialSettings.Default with { BaudRate = baudRate }, Cancellation);
        var sent = new byte[baudRate >= 115200 ? 8192 : 1024];
        Random.Shared.NextBytes(sent);

        await connection.WriteAsync(sent, Cancellation);
        var received = await ReadExactlyAsync(connection, sent.Length, TimeSpan.FromSeconds(10));

        Assert.Equal(sent, received);
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
}
