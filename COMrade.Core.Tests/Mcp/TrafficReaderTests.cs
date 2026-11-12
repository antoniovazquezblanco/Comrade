using COMrade.Core.Mcp;
using COMrade.Core.Sessions;

namespace COMrade.Core.Tests.Mcp;

public class TrafficReaderTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly Traffic _traffic = new(limit: 16);

    [Fact]
    public void Starts_with_the_backlog_and_reads_the_rest_in_order()
    {
        _traffic.Append(TrafficSender.Device, "boot"u8, At);
        var reader = new TrafficReader(_traffic, backlog: 2);
        _traffic.Append(TrafficSender.Keyboard, "x"u8, At);

        var (runs, missed) = reader.Read(100, 100);

        Assert.Equal(0, missed);
        Assert.Equal(["ot", "x"], runs.Select(reader.Text));
        Assert.Equal(0, reader.Unread);
    }

    [Fact]
    public void Says_what_it_missed_and_decodes_afresh_after()
    {
        var reader = new TrafficReader(_traffic, backlog: 0);
        _traffic.Append(TrafficSender.Device, [(byte)'a', 0xC3], At);
        var (runs, _) = reader.Read(100, 100);
        Assert.Equal("a", reader.Text(runs[0]));

        // The rest of the character left the buffer, so the byte after the gap is no part of it.
        _traffic.Append(TrafficSender.Device, new byte[20], At);
        _traffic.Append(TrafficSender.Device, [0xA9, .. Enumerable.Repeat((byte)'b', 15)], At);
        (runs, var missed) = reader.Read(100, 100);

        Assert.Equal(20, missed);
        Assert.Equal("\xFFFD" + new string('b', 15), string.Concat(runs.Select(reader.Text)));
    }

    [Fact]
    public void A_character_split_between_two_reads_comes_out_whole()
    {
        var reader = new TrafficReader(_traffic, backlog: 0);
        _traffic.Append(TrafficSender.Device, [(byte)'c', 0xC3], At);
        var first = reader.Text(reader.Read(100, 100).Runs.Single());
        _traffic.Append(TrafficSender.Device, [0xA9], At);
        var second = reader.Text(reader.Read(100, 100).Runs.Single());

        Assert.Equal(("c", "\xE9"), (first, second));
    }

    [Fact]
    public void Text_after_hex_starts_afresh()
    {
        var reader = new TrafficReader(_traffic, backlog: 0);
        _traffic.Append(TrafficSender.Device, [(byte)'c', 0xC3], At);
        Assert.Equal("c", reader.Text(reader.Read(100, 100).Runs.Single()));
        _traffic.Append(TrafficSender.Device, "A"u8, At);
        Assert.Equal("41", reader.Hex(reader.Read(100, 100).Runs.Single()));

        _traffic.Append(TrafficSender.Device, [0xA9, (byte)'d'], At);

        Assert.Equal("\xFFFD" + "d", reader.Text(reader.Read(100, 100).Runs.Single()));
    }

    [Fact]
    public void Skipping_to_the_latest_says_how_much()
    {
        _traffic.Append(TrafficSender.Device, "0123456789"u8, At);
        var reader = new TrafficReader(_traffic, backlog: 100);

        Assert.Equal(7, reader.SkipToLatest(3));
        Assert.Equal("789", reader.Text(reader.Read(100, 100).Runs.Single()));
        Assert.Equal(0, reader.SkipToLatest(3));
    }
}
