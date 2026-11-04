using COMrade.Core.Mcp;
using COMrade.Core.Sessions;

namespace COMrade.Core.Tests.Mcp;

public class PortReaderTests
{
    private readonly ReceiveBuffer _buffer = new(limit: 64);

    [Fact]
    public void A_new_reader_starts_with_the_backlog_of_earlier_output()
    {
        _buffer.Append("0123456789"u8);

        var reader = new PortReader(_buffer, backlog: 4);

        Assert.Equal(6, reader.Position);
        Assert.Equal(4, reader.Unread);
        Assert.Equal("6789", reader.Text(reader.Read(100).Bytes));
        Assert.Equal(0, reader.Unread);
    }

    [Fact]
    public void Reads_continue_where_the_last_one_ended()
    {
        var reader = new PortReader(_buffer, backlog: 0);
        _buffer.Append("hello "u8);

        Assert.Equal("hel", reader.Text(reader.Read(3).Bytes));
        _buffer.Append("world"u8);
        Assert.Equal("lo world", reader.Text(reader.Read(100).Bytes));
    }

    [Fact]
    public void Bytes_dropped_before_they_were_read_are_reported_as_missed()
    {
        var reader = new PortReader(_buffer, backlog: 0);

        _buffer.Append(new byte[100]);
        var (bytes, missed) = reader.Read(100);

        Assert.Equal(64, bytes.Length);
        Assert.Equal(36, missed);
    }

    [Fact]
    public void Text_has_no_colour_codes_and_keeps_characters_split_between_reads()
    {
        var reader = new PortReader(_buffer, backlog: 0);
        byte[] coloured = [.. "\e[32mOK\e[0m caf"u8, 0xC3];

        _buffer.Append(coloured);
        var first = reader.Text(reader.Read(100).Bytes);
        _buffer.Append([0xA9, (byte)'\r', (byte)'\n']);
        var second = reader.Text(reader.Read(100).Bytes);

        Assert.Equal("OK caf", first);
        Assert.Equal("é\r\n", second);
    }

    [Fact]
    public void A_colour_code_split_between_reads_is_removed()
    {
        var reader = new PortReader(_buffer, backlog: 0);

        _buffer.Append("a\e[3"u8);
        var first = reader.Text(reader.Read(100).Bytes);
        _buffer.Append("1mb"u8);
        var second = reader.Text(reader.Read(100).Bytes);

        Assert.Equal("ab", first + second);
    }

    [Fact]
    public void Skipping_to_the_end_marks_everything_read()
    {
        _buffer.Append("old"u8);
        var reader = new PortReader(_buffer, backlog: 64);

        Assert.Equal(3, reader.SkipToEnd());
        Assert.Equal(0, reader.Unread);
        Assert.Equal(0, reader.SkipToEnd());
    }

    [Fact]
    public void Skipping_to_the_latest_keeps_only_the_newest_bytes_unread()
    {
        var reader = new PortReader(_buffer, backlog: 0);
        _buffer.Append("0123456789"u8);

        Assert.Equal(7, reader.SkipToLatest(3));
        Assert.Equal("789", reader.Text(reader.Read(100).Bytes));
        Assert.Equal(0, reader.SkipToLatest(3));
    }

    [Fact]
    public void Hex_shows_each_byte()
    {
        var reader = new PortReader(_buffer, backlog: 0);

        Assert.Equal("48 69 0D 0A FF", reader.Hex([0x48, 0x69, 0x0D, 0x0A, 0xFF]));
    }
}
