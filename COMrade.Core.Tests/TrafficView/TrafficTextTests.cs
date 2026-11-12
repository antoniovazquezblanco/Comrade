using System.Text;
using COMrade.Core.Sessions;
using COMrade.Core.TrafficView;

namespace COMrade.Core.Tests.TrafficView;

public class TrafficTextTests
{
    [Fact]
    public void Printable_text_shows_as_it_is()
    {
        Assert.Equal("AT+CGMR=1 ~{}", TrafficText.Format("AT+CGMR=1 ~{}"u8));
    }

    [Fact]
    public void Control_characters_show_as_the_escapes_the_send_bar_takes()
    {
        Assert.Equal(@"\r\n\t\e\\", TrafficText.Format("\r\n\t\e\\"u8));
        Assert.Equal(@"\x00\x03\x1a\x7f", TrafficText.Format([0x00, 0x03, 0x1A, 0x7F]));
    }

    [Fact]
    public void Utf8_text_shows_as_characters_a_column_each()
    {
        var bytes = Encoding.UTF8.GetBytes("café ≤ 😀");

        Assert.Equal("café ≤ 😀", TrafficText.Format(bytes));
        Assert.Equal(new TrafficUnit(2, 1, IsEscape: false), TrafficText.Next(bytes.AsSpan(3)));
        Assert.Equal(new TrafficUnit(4, 1, IsEscape: false), TrafficText.Next(bytes.AsSpan(bytes.Length - 4)));
    }

    [Fact]
    public void Bytes_that_are_not_utf8_show_one_escape_each()
    {
        Assert.Equal(@"a\xffb\x80\xc3(", TrafficText.Format([(byte)'a', 0xFF, (byte)'b', 0x80, 0xC3, (byte)'(']));
    }

    [Theory]
    [InlineData("\u202E", @"\xe2\x80\xae")]
    [InlineData("\u200B", @"\xe2\x80\x8b")]
    [InlineData("\u0085", @"\xc2\x85")]
    [InlineData("\u2028", @"\xe2\x80\xa8")]
    public void Characters_that_change_how_the_text_around_them_shows_are_escaped(string character, string shown)
    {
        Assert.Equal("a" + shown + "b", TrafficText.Format(Encoding.UTF8.GetBytes("a" + character + "b")));
    }

    [Fact]
    public void The_start_of_a_character_whose_rest_is_missing_shows_its_bytes_together()
    {
        Assert.Equal(new TrafficUnit(2, 8, IsEscape: true, IsIncomplete: true), TrafficText.Next([0xE2, 0x80]));
        Assert.Equal(@"ok\xe2\x80", TrafficText.Format([(byte)'o', (byte)'k', 0xE2, 0x80]));
    }

    [Fact]
    public void Widths_add_up_to_what_shows()
    {
        byte[] bytes = [.. "té\r\n\\x"u8, 0x00, 0xFF, 0xC3, 0xA9, 0x1B, 0xE2, 0x80];

        var width = 0;
        for (var index = 0; index < bytes.Length;)
        {
            var unit = TrafficText.Next(bytes.AsSpan(index));
            width += unit.Width;
            index += unit.Length;
        }

        Assert.Equal(TrafficText.Format(bytes).Length, width);
    }

    [Fact]
    public void Pieces_tell_escapes_from_text()
    {
        Assert.Equal(
            [("OK", false), (@"\r\n", true), ("é>", false), (@"\x03", true)],
            TrafficText.Pieces([.. "OK\r\n"u8, 0xC3, 0xA9, (byte)'>', 0x03]));
        Assert.Empty(TrafficText.Pieces([]));
    }

    [Fact]
    public void Hex_shows_each_byte_with_a_gap_every_eight_when_grouped()
    {
        byte[] bytes = [.. Enumerable.Range(0x41, 17).Select(value => (byte)value)];

        Assert.Equal("41 54 0d", TrafficText.Hex("AT\r"u8));
        Assert.Equal("41 42 43 44 45 46 47 48  49 4a 4b 4c 4d 4e 4f 50  51", TrafficText.Hex(bytes, grouped: true));
        Assert.Equal("", TrafficText.Hex([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(32)]
    public void The_width_of_grouped_hex_is_what_it_takes(int count)
    {
        Assert.Equal(TrafficText.Hex(new byte[count], grouped: true).Length, TrafficText.HexWidth(count));
    }

    [Fact]
    public void The_device_receives_and_the_console_and_mcp_send()
    {
        TrafficSender[] senders = [TrafficSender.Device, TrafficSender.Keyboard, TrafficSender.Assistant, TrafficSender.Comrade];

        Assert.Equal(
            [("RX", "device"), ("TX", "console"), ("TX", "mcp"), ("TX", "console")],
            senders.Select(sender => (TrafficText.Direction(sender), TrafficText.From(sender))));
    }
}
