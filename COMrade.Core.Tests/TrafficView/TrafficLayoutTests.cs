using COMrade.Core.Sessions;
using COMrade.Core.TrafficView;

namespace COMrade.Core.Tests.TrafficView;

public class TrafficLayoutTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 9, 30, 15, 250, TimeSpan.Zero);
    private static readonly string Local = At.ToLocalTime().ToString("HH:mm:ss.fff");

    private static readonly TrafficRow Sent = new(0, 3, TrafficSender.Keyboard, Continues: false);

    [Fact]
    public void With_every_column_a_row_holds_the_most_bytes_whose_hex_and_text_fit()
    {
        // 25 columns go to Time, Dir and From; 16 bytes take 48 of hex, a space and 16 of text.
        Assert.Equal(new TrafficLayout(AnalysisColumns.All, 16, 105 - 49), TrafficLayout.Fit(AnalysisColumns.All, 130));
        Assert.Equal(8, TrafficLayout.Fit(AnalysisColumns.All, 80).RowBytes);
        Assert.Equal(32, TrafficLayout.Fit(AnalysisColumns.All, 25 + 98 + 1 + 32).RowBytes);
    }

    [Fact]
    public void Hidden_columns_leave_their_room_to_the_rest()
    {
        var bytesOnly = AnalysisColumns.Hex | AnalysisColumns.Text;

        // Without Time, Dir and From, 32 bytes fit where 16 did: 98 of hex, a space, 32 of text.
        Assert.Equal(16, TrafficLayout.Fit(AnalysisColumns.All, 140).RowBytes);
        Assert.Equal(new TrafficLayout(bytesOnly, 32, 140 - 99), TrafficLayout.Fit(bytesOnly, 140));
    }

    [Fact]
    public void Text_alone_has_no_bytes_limit_and_hex_alone_no_text_limit()
    {
        var text = AnalysisColumns.All & ~AnalysisColumns.Hex;
        var hex = AnalysisColumns.All & ~AnalysisColumns.Text;

        Assert.Equal(new TrafficLayout(text, int.MaxValue, 105), TrafficLayout.Fit(text, 130));
        Assert.Equal(new TrafficLayout(hex, 32, int.MaxValue), TrafficLayout.Fit(hex, 130));
    }

    [Fact]
    public void A_narrow_view_still_has_a_row_of_8_bytes_and_some_text()
    {
        Assert.Equal(new TrafficLayout(AnalysisColumns.All, 8, TrafficLayout.MinimumTextColumns), TrafficLayout.Fit(AnalysisColumns.All, 30));
    }

    [Fact]
    public void Without_hex_or_text_the_text_shows()
    {
        Assert.Equal(AnalysisColumns.Time | AnalysisColumns.Text, TrafficLayout.Fit(AnalysisColumns.Time, 130).Shown);
    }

    [Fact]
    public void Hex_and_text_cannot_both_be_hidden()
    {
        var hex = AnalysisColumns.All & ~AnalysisColumns.Text;

        Assert.Equal(hex, TrafficLayout.Show(hex, AnalysisColumns.Hex, show: false));
        Assert.Equal(hex & ~AnalysisColumns.Time, TrafficLayout.Show(hex, AnalysisColumns.Time, show: false));
        Assert.Equal(AnalysisColumns.All, TrafficLayout.Show(hex, AnalysisColumns.Text, show: true));
    }

    [Fact]
    public void The_header_names_the_columns_shown_over_them()
    {
        var layout = TrafficLayout.Fit(AnalysisColumns.All, 80);

        Assert.Equal("Time         Dir From    Hex                     Text", layout.Header);
        Assert.Equal("Dir Text", TrafficLayout.Fit(AnalysisColumns.Direction | AnalysisColumns.Text, 80).Header);
        Assert.Equal("From    Hex", TrafficLayout.Fit(AnalysisColumns.From | AnalysisColumns.Hex, 80).Header);
    }

    [Fact]
    public void A_row_shows_the_columns_shown()
    {
        var all = TrafficLayout.Fit(AnalysisColumns.All, 80);
        var hex = TrafficLayout.Fit(AnalysisColumns.Direction | AnalysisColumns.Hex, 80);

        Assert.Equal($"{Local} TX  console 41 54 0d                AT\\r", all.Format(At, Sent, "AT\r"u8));
        Assert.Equal("TX  41 54 0d", hex.Format(At, Sent, "AT\r"u8));
    }

    [Fact]
    public void The_rest_of_a_wrapped_row_shows_only_its_bytes()
    {
        var layout = TrafficLayout.Fit(AnalysisColumns.All, 80);

        Assert.Equal(new string(' ', 25) + "65 66" + new string(' ', 19) + "ef", layout.Format(null, Sent with { Continues = true }, "ef"u8));
    }
}
