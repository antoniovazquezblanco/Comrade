using System.Text;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class ReceivedLineEndingsTests
{
    [Fact]
    public void Bare_lf_starts_a_new_line_by_default()
    {
        var converter = new ReceivedLineEndings(TerminalSettings.Default);

        Assert.Equal("one\r\ntwo\r\n", Convert(converter, "one\ntwo\n"));
    }

    [Fact]
    public void Cr_lf_is_left_alone()
    {
        var converter = new ReceivedLineEndings(new TerminalSettings(NewLineOnLf: true, NewLineOnCr: true));

        Assert.Equal("one\r\ntwo\r\n", Convert(converter, "one\r\ntwo\r\n"));
    }

    [Fact]
    public void Cr_lf_split_across_reads_is_not_doubled()
    {
        var converter = new ReceivedLineEndings(new TerminalSettings(NewLineOnLf: true, NewLineOnCr: true));

        var output = Convert(converter, "one\r") + Convert(converter, "\ntwo");

        Assert.Equal("one\r\ntwo", output);
    }

    [Fact]
    public void Bare_cr_starts_a_new_line_when_enabled()
    {
        var converter = new ReceivedLineEndings(new TerminalSettings(NewLineOnCr: true));

        Assert.Equal("one\r\ntwo\r\n", Convert(converter, "one\rtwo\r\n"));
    }

    [Fact]
    public void Bare_cr_at_the_end_of_a_read_gets_its_new_line_when_more_arrives()
    {
        var converter = new ReceivedLineEndings(new TerminalSettings(NewLineOnCr: true));

        Assert.Equal("one\r", Convert(converter, "one\r"));
        Assert.Equal("\ntwo", Convert(converter, "two"));
    }

    [Fact]
    public void Consecutive_crs_each_start_a_new_line_when_enabled()
    {
        var converter = new ReceivedLineEndings(new TerminalSettings(NewLineOnCr: true));

        Assert.Equal("a\r\n\r\nb", Convert(converter, "a\r\rb"));
    }

    [Fact]
    public void Bare_cr_only_returns_to_the_line_start_by_default()
    {
        var converter = new ReceivedLineEndings(TerminalSettings.Default);

        Assert.Equal("progress 10%\rprogress 20%", Convert(converter, "progress 10%\rprogress 20%"));
    }

    [Fact]
    public void Nothing_changes_with_both_options_off()
    {
        var converter = new ReceivedLineEndings(new TerminalSettings(NewLineOnLf: false, NewLineOnCr: false));

        Assert.Equal("a\nb\rc", Convert(converter, "a\nb\rc"));
    }

    [Fact]
    public void Settings_changed_while_running_apply_to_the_next_read()
    {
        var converter = new ReceivedLineEndings(new TerminalSettings(NewLineOnLf: false));
        Assert.Equal("a\n", Convert(converter, "a\n"));

        converter.Settings = TerminalSettings.Default;

        Assert.Equal("b\r\n", Convert(converter, "b\n"));
    }

    [Fact]
    public void Escape_sequences_and_utf8_pass_through_untouched()
    {
        var converter = new ReceivedLineEndings(TerminalSettings.Default);

        Assert.Equal("\x1b[32mÁngulo ✓\x1b[0m\r\n", Convert(converter, "\x1b[32mÁngulo ✓\x1b[0m\n"));
    }

    private static string Convert(ReceivedLineEndings converter, string text) =>
        Encoding.UTF8.GetString(converter.Convert(Encoding.UTF8.GetBytes(text)));
}
