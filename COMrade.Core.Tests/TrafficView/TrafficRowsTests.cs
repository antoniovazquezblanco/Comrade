using System.Text;
using COMrade.Core.Sessions;
using COMrade.Core.TrafficView;

namespace COMrade.Core.Tests.TrafficView;

public class TrafficRowsTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly Traffic _traffic = new();
    private readonly TrafficRows _rows;
    private DateTimeOffset _now = At;

    public TrafficRowsTests() => _rows = new TrafficRows(_traffic);

    [Fact]
    public void A_line_feed_ends_a_row()
    {
        Append(TrafficSender.Device, "boot\r\nok\r\n> ");

        Assert.True(_rows.Update());

        Assert.Equal([@"boot\r\n", @"ok\r\n", "> "], Shown());
    }

    [Fact]
    public void Each_sender_gets_rows_of_its_own()
    {
        Append(TrafficSender.Device, "login: ");
        Append(TrafficSender.Keyboard, "root\r");
        Append(TrafficSender.Device, "\r\n# ");

        _rows.Update();

        Assert.Equal(
            [(TrafficSender.Device, "login: "), (TrafficSender.Keyboard, @"root\r"), (TrafficSender.Device, @"\r\n"), (TrafficSender.Device, "# ")],
            Rows().Select(row => (row.Sender, Text(row))));
    }

    [Fact]
    public void A_pause_ends_a_row()
    {
        Append(TrafficSender.Device, "Hit any key");
        _now += Traffic.Pause;
        Append(TrafficSender.Device, "...");

        _rows.Update();

        Assert.Equal(["Hit any key", "..."], Shown());
    }

    [Fact]
    public void A_row_that_does_not_fit_wraps_and_its_rest_has_no_time()
    {
        _rows.Columns = 4;
        Append(TrafficSender.Device, "abcdefghij\nk");

        _rows.Update();

        Assert.Equal([("abcd", false), ("efgh", true), (@"ij\n", true), ("k", false)], Rows().Select(row => (Text(row), row.Continues)));
    }

    [Fact]
    public void Escapes_take_their_width_and_characters_are_not_split()
    {
        _rows.Columns = 4;
        Append(TrafficSender.Device, [(byte)'a', 0x01, (byte)'b', (byte)'c', 0xC3, 0xA9, (byte)'d', (byte)'e']);

        _rows.Update();

        Assert.Equal(["a", @"\x01", "bcéd", "e"], Shown());
    }

    [Fact]
    public void The_last_row_grows_with_what_arrives()
    {
        Append(TrafficSender.Device, "U-Bo");
        _rows.Update();
        Append(TrafficSender.Device, "ot\r\n=> ");

        Assert.True(_rows.Update());

        Assert.Equal([@"U-Boot\r\n", "=> "], Shown());
        Assert.False(_rows.Update());
    }

    [Fact]
    public void A_character_split_between_chunks_shows_whole()
    {
        Append(TrafficSender.Device, [(byte)'c', (byte)'a', (byte)'f', 0xC3]);
        _rows.Update();
        Assert.Equal([@"caf\xc3"], Shown());

        Append(TrafficSender.Device, [0xA9]);
        _rows.Update();

        Assert.Equal(["café"], Shown());
    }

    [Fact]
    public void A_character_split_where_the_row_is_full_wraps_as_it_would_whole()
    {
        _rows.Columns = 4;
        Append(TrafficSender.Device, [(byte)'a', (byte)'b', (byte)'c', 0xC3]);
        _rows.Update();

        Append(TrafficSender.Device, [0xA9, (byte)'d']);
        _rows.Update();

        Assert.Equal(["abcé", "d"], Shown());
    }

    [Fact]
    public void Laid_out_as_it_arrives_or_at_once_the_rows_are_the_same()
    {
        var random = new Random(7);
        string[] pieces = ["ok", "\r\n", "é", "\x1b[32m", "long line that wraps", "\\", " "];
        _rows.Columns = 7;
        for (var i = 0; i < 400; i++)
        {
            var sender = random.Next(5) == 0 ? TrafficSender.Keyboard : TrafficSender.Device;
            var bytes = Encoding.UTF8.GetBytes(pieces[random.Next(pieces.Length)]);

            // Characters split between chunks too.
            var split = random.Next(bytes.Length + 1);
            Append(sender, bytes[..split]);
            if (random.Next(2) == 0)
                _rows.Update();
            _now += TimeSpan.FromMilliseconds(random.Next(3) == 0 ? 1500 : 10);
            Append(sender, bytes[split..]);
            if (random.Next(3) == 0)
                _rows.Update();
        }

        _rows.Update();
        var atOnce = new TrafficRows(_traffic) { Columns = 7 };
        atOnce.Update();

        Assert.Equal(Rows(atOnce), Rows());
        Assert.Equal(_traffic.End, Rows().Last().End);
    }

    [Fact]
    public void What_was_sent_can_be_left_out()
    {
        Append(TrafficSender.Device, "> ");
        Append(TrafficSender.Assistant, "reset\r");
        Append(TrafficSender.Device, "\r\nresetting\r\n");
        _rows.Update();

        _rows.ShowSent = false;
        Assert.Equal(["> ", @"\r\n", @"resetting\r\n"], Shown());

        _rows.ShowSent = true;
        Assert.Equal(["> ", @"reset\r", @"\r\n", @"resetting\r\n"], Shown());
    }

    [Fact]
    public void After_clear_only_new_traffic_shows()
    {
        Append(TrafficSender.Device, "old\r\n");
        _rows.Update();

        _rows.Clear();
        Assert.Equal(0, _rows.Count);
        Append(TrafficSender.Device, "new");
        _rows.Update();

        Assert.Equal(["new"], Shown());
    }

    [Fact]
    public void Rows_go_with_the_traffic_no_longer_kept()
    {
        var traffic = new Traffic(limit: 16);
        var rows = new TrafficRows(traffic);
        traffic.Append(TrafficSender.Device, "one\ntwo\nthree\n"u8, At);
        rows.Update();

        traffic.Append(TrafficSender.Device, "fourth\n"u8, At);
        Assert.True(rows.Update());

        // "one\n" and the "t" of "two\n" went.
        Assert.Equal([@"wo\n", @"three\n", @"fourth\n"], Shown(rows, traffic));
    }

    [Fact]
    public void A_row_is_found_by_any_of_its_bytes()
    {
        Append(TrafficSender.Device, "ab\ncd\nef");
        _rows.Update();

        Assert.Equal([0, 0, 0, 1, 1, 1, 2, 2, 3], Enumerable.Range(0, 9).Select(offset => _rows.IndexOf(offset)));
    }

    private void Append(TrafficSender sender, string text) => Append(sender, Encoding.UTF8.GetBytes(text));

    private void Append(TrafficSender sender, byte[] bytes) => _traffic.Append(sender, bytes, _now);

    private string[] Shown() => Shown(_rows, _traffic);

    private static string[] Shown(TrafficRows rows, Traffic traffic) => [.. Rows(rows).Select(row => Text(row, traffic))];

    private TrafficRow[] Rows() => Rows(_rows);

    private static TrafficRow[] Rows(TrafficRows rows) => [.. Enumerable.Range(0, rows.Count).Select(index => rows[index])];

    private string Text(TrafficRow row) => Text(row, _traffic);

    private static string Text(TrafficRow row, Traffic traffic)
    {
        var (start, runs) = traffic.Read(row.Offset, row.Length);
        Assert.Equal(row.Offset, start);
        return TrafficText.Format(Assert.Single(runs).Bytes);
    }
}
