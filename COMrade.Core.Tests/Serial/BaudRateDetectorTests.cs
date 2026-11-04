using COMrade.Core.Serial;
using COMrade.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace COMrade.Core.Tests.Serial;

public class BaudRateDetectorTests
{
    private static readonly byte[] BootLog = "U-Boot 2020.01 (Jan 06 2020 - 10:00:00 +0000)\r\n\r\nDRAM:  128 MiB\r\n"u8.ToArray();
    private static readonly byte[] Enter = "\r"u8.ToArray();

    private readonly FakeTimeProvider _time = new();
    private readonly FakeSerialConnection _port = new("COM5", SerialSettings.Default, ControlLines.Default, hangOnClose: false);
    private readonly RecordingProgress<int> _tried = new();

    [Fact]
    public void Text_counts_with_colour_codes_and_utf8()
    {
        var text = "ok\r\n\e[32mgreen\e[0m\tÁngulo ✓\b"u8;

        Assert.Equal(text.Length, BaudRateDetector.CountText(text));
    }

    [Fact]
    public void What_a_wrong_speed_reads_does_not_count_as_text()
    {
        Assert.Equal(0, BaudRateDetector.CountText([0x00, 0xFE, 0x80, 0xF8, 0xFF, 0xC0, 0x80, 0xE0, 0x80, 0x80]));
        Assert.Equal(2, BaudRateDetector.CountText([(byte)'o', 0x00, (byte)'k', 0xC3]));
    }

    [Fact]
    public async Task Device_at_the_ports_own_speed_is_found_without_trying_another()
    {
        _port.DeviceBaudRate = 115200;
        _port.Talk(BootLog, times: 10);

        var detection = await DetectAsync();

        Assert.Equal(BaudRateDetectionOutcome.Detected, detection.Outcome);
        Assert.Equal(115200, detection.BaudRate);
        Assert.Equal([115200], _tried.Values);
    }

    [Fact]
    public async Task Device_at_another_speed_is_found_and_what_it_printed_kept()
    {
        _port.DeviceBaudRate = 9600;
        _port.Talk(BootLog, times: 20);

        var detection = await DetectAsync();

        Assert.Equal(9600, detection.BaudRate);
        Assert.Equal([115200, 9600], _tried.Values);
        Assert.Equal(BootLog, detection.Output[..BootLog.Length]);
        Assert.Equal(detection.Output.Length, BaudRateDetector.CountText(detection.Output));
    }

    [Fact]
    public async Task Port_speed_is_tried_first()
    {
        _port.DeviceBaudRate = 57600;
        _port.Talk(BootLog, times: 20);

        var detection = await DetectAsync(SerialSettings.Default with { BaudRate = 57600 });

        Assert.Equal(57600, detection.BaudRate);
        Assert.Equal([57600], _tried.Values);
    }

    [Fact]
    public async Task Stray_byte_at_the_right_speed_is_forgiven()
    {
        _port.DeviceBaudRate = 115200;
        _port.Talk([0xFF, .. BootLog], times: 1);

        var detection = await DetectWhileTimePassesAsync();

        Assert.Equal(115200, detection.BaudRate);
    }

    [Fact]
    public async Task Speeds_the_driver_refuses_are_skipped()
    {
        _port.DeviceBaudRate = 38400;
        _port.UnsupportedBaudRates.UnionWith([9600, 57600]);
        _port.Talk(BootLog, times: 20);

        var detection = await DetectAsync();

        Assert.Equal(38400, detection.BaudRate);
        Assert.Equal([115200, 38400], _tried.Values);
    }

    [Fact]
    public async Task Short_prompt_printed_for_each_enter_adds_up()
    {
        _port.DeviceBaudRate = 57600;
        _port.Prompt = "\r\n=> "u8.ToArray();

        var detection = await DetectWhileTimePassesAsync(wake: () => Enter);

        Assert.Equal(57600, detection.BaudRate);
        Assert.Equal([115200, 9600, 57600], _tried.Values);
        Assert.Equal("\r\n=> \r\n=> \r\n=> \r\n=> "u8.ToArray(), detection.Output);
    }

    [Fact]
    public async Task Without_enter_a_device_at_a_prompt_is_silent()
    {
        _port.DeviceBaudRate = 57600;
        _port.Prompt = "\r\n=> "u8.ToArray();

        var detection = await DetectWhileTimePassesAsync();

        Assert.Equal(BaudRateDetectionOutcome.Silent, detection.Outcome);
        Assert.Null(detection.BaudRate);
        Assert.Empty(_port.Written);
    }

    [Fact]
    public async Task Output_that_reads_as_text_at_no_speed_is_unreadable()
    {
        // A speed detection does not try, such as one a device got wrong.
        _port.DeviceBaudRate = 12345;
        _port.Talk(BootLog, times: 200);

        var detection = await DetectWhileTimePassesAsync();

        Assert.Equal(BaudRateDetectionOutcome.Unreadable, detection.Outcome);
        Assert.Equal(BaudRateDetector.BaudRates.Count, _tried.Values.Distinct().Count());
    }

    [Fact]
    public async Task Cancelling_stops_the_detection()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var detect = BaudRateDetector.DetectAsync(_port, SerialSettings.Default, wake: null, _tried, _time, cancellation.Token);

        await cancellation.CancelAsync();

        Assert.Equal(BaudRateDetection.Stopped, await detect.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failing_port_ends_the_detection_with_its_error()
    {
        var detect = DetectAsync();

        _port.Fail(SerialPortError.DeviceRemoved);

        var exception = await Assert.ThrowsAsync<SerialPortException>(() => detect.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(SerialPortError.DeviceRemoved, exception.Error);
    }

    private Task<BaudRateDetection> DetectAsync(SerialSettings? settings = null, Func<ReadOnlyMemory<byte>>? wake = null) =>
        BaudRateDetector.DetectAsync(_port, settings ?? SerialSettings.Default, wake, _tried, _time, TestContext.Current.CancellationToken);

    // Listens end on the fake clock when the device is quiet: keep time moving until it is done.
    private async Task<BaudRateDetection> DetectWhileTimePassesAsync(Func<ReadOnlyMemory<byte>>? wake = null)
    {
        var detect = Task.Run(() => DetectAsync(wake: wake), TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!detect.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Detection did not end within 10 seconds.");
            _time.Advance(BaudRateDetector.ListenTime);
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        return await detect;
    }
}
