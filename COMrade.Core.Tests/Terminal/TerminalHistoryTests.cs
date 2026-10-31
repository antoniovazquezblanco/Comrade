using System.Text;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class TerminalHistoryTests
{
    private static string Text(IEnumerable<byte[]> chunks) => Encoding.UTF8.GetString([.. chunks.SelectMany(chunk => chunk)]);

    [Fact]
    public void Output_reaches_the_attached_terminal()
    {
        var history = new TerminalHistory();
        var shown = new List<byte[]>();
        history.Attach(shown.Add);

        history.Write("boot\r\n"u8);

        Assert.Equal("boot\r\n", Text(shown));
    }

    [Fact]
    public void A_new_terminal_first_shows_what_was_shown_before()
    {
        var history = new TerminalHistory();
        history.Attach(_ => { });
        history.Write("first line\r\n"u8);
        history.Detach();
        history.Write("while moving\r\n"u8);

        var shown = new List<byte[]>();
        history.Attach(shown.Add);
        history.Write("after\r\n"u8);

        Assert.Equal("first line\r\nwhile moving\r\nafter\r\n", Text(shown));
    }

    [Fact]
    public void Clear_forgets_what_was_shown()
    {
        var history = new TerminalHistory();
        history.Write("old\r\n"u8);

        history.Clear();
        var shown = new List<byte[]>();
        history.Attach(shown.Add);

        Assert.Empty(shown);
    }

    [Fact]
    public void Old_output_is_dropped_beyond_the_limit_from_a_line_start()
    {
        var history = new TerminalHistory(limit: 16);
        history.Write("line one\r\nline two\r\nline three\r\n"u8);

        var shown = new List<byte[]>();
        history.Attach(shown.Add);

        Assert.Equal("line three\r\n", Text(shown));
    }

    [Fact]
    public async Task Output_written_while_a_terminal_attaches_is_shown_exactly_once()
    {
        var history = new TerminalHistory();
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
                history.Write([(byte)'x']);
        }, TestContext.Current.CancellationToken);

        var shown = new List<byte[]>();
        history.Attach(chunk =>
        {
            lock (shown)
                shown.Add(chunk);
        });
        await writer;

        Assert.Equal(2000, shown.Sum(chunk => chunk.Length));
    }
}
