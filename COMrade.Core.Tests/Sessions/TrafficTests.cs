using COMrade.Core.Sessions;

namespace COMrade.Core.Tests.Sessions;

public class TrafficTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly Traffic _traffic = new();

    [Fact]
    public void Runs_follow_the_sender_in_the_order_bytes_went()
    {
        _traffic.Append(TrafficSender.Device, "log"u8, At);
        _traffic.Append(TrafficSender.Device, "in: "u8, At.AddMilliseconds(10));
        _traffic.Append(TrafficSender.Comrade, "root\r"u8, At.AddMilliseconds(20));
        _traffic.Append(TrafficSender.Device, "# "u8, At.AddMilliseconds(30));

        var (start, runs) = _traffic.Read(0, 100);

        Assert.Equal(0, start);
        Assert.Equal(
            [Run(TrafficSender.Device, At, "login: "), Run(TrafficSender.Comrade, At.AddMilliseconds(20), "root\r"), Run(TrafficSender.Device, At.AddMilliseconds(30), "# ")],
            runs.Select(Plain));
        Assert.Equal(14, _traffic.End);
    }

    [Fact]
    public void A_pause_starts_a_run_with_its_own_time()
    {
        _traffic.Append(TrafficSender.Device, "a"u8, At);
        _traffic.Append(TrafficSender.Device, "b"u8, At + Traffic.Pause - TimeSpan.FromMilliseconds(1));
        _traffic.Append(TrafficSender.Device, "c"u8, At + Traffic.Pause * 2);

        var (_, runs) = _traffic.Read(0, 100);

        Assert.Equal([Run(TrafficSender.Device, At, "ab"), Run(TrafficSender.Device, At + Traffic.Pause * 2, "c")], runs.Select(Plain));
    }

    [Fact]
    public void A_read_from_the_middle_of_a_run_keeps_its_time_and_stops_at_max_bytes()
    {
        _traffic.Append(TrafficSender.Device, "login: "u8, At);
        _traffic.Append(TrafficSender.Keyboard, "root\r"u8, At.AddSeconds(2));

        var (start, runs) = _traffic.Read(3, 6);

        Assert.Equal(3, start);
        Assert.Equal([Run(TrafficSender.Device, At, "in: "), Run(TrafficSender.Keyboard, At.AddSeconds(2), "ro")], runs.Select(Plain));
    }

    [Fact]
    public void The_oldest_bytes_go_with_their_runs()
    {
        var traffic = new Traffic(limit: 8);
        traffic.Append(TrafficSender.Device, "abcdef"u8, At);
        traffic.Append(TrafficSender.Comrade, "123456"u8, At);

        var (start, runs) = traffic.Read(0, 100);

        Assert.Equal(4, traffic.Start);
        Assert.Equal(4, start);
        Assert.Equal([Run(TrafficSender.Device, At, "ef"), Run(TrafficSender.Comrade, At, "123456")], runs.Select(Plain));

        traffic.Append(TrafficSender.Keyboard, "ABCDEFGHIJ"u8, At);
        Assert.Equal([Run(TrafficSender.Keyboard, At, "CDEFGHIJ")], traffic.Read(0, 100).Runs.Select(Plain));
        Assert.Equal(22, traffic.End);
    }

    [Fact]
    public void Too_many_runs_let_the_oldest_go_with_their_bytes()
    {
        var traffic = new Traffic(maxRuns: 4);
        for (var i = 0; i < 6; i++)
            traffic.Append(i % 2 == 0 ? TrafficSender.Device : TrafficSender.Comrade, [(byte)('a' + i)], At);

        var (start, runs) = traffic.Read(0, 100);

        Assert.Equal(2, traffic.Start);
        Assert.Equal(2, start);
        Assert.Equal("cdef", string.Concat(runs.Select(run => (char)run.Bytes[0])));
        Assert.Equal([TrafficSender.Device, TrafficSender.Comrade, TrafficSender.Device, TrafficSender.Comrade], runs.Select(run => run.Sender));
    }

    [Fact]
    public void Every_offset_reads_back_the_runs_as_they_were_appended()
    {
        // Runs of 1 to 5 bytes from senders that take turns, and the bytes' senders one by one.
        var senders = new List<TrafficSender>();
        for (var run = 0; run < 50; run++)
        {
            var sender = (TrafficSender)(run % 3);
            var length = run % 5 + 1;
            _traffic.Append(sender, Enumerable.Repeat((byte)run, length).ToArray(), At.AddSeconds(run));
            senders.AddRange(Enumerable.Repeat(sender, length));
        }

        for (var offset = 0; offset < senders.Count; offset++)
        {
            foreach (var maxBytes in new[] { 1, 3, 7, 1000 })
            {
                var (start, runs) = _traffic.Read(offset, maxBytes);
                Assert.Equal(offset, start);

                var at = offset;
                foreach (var run in runs)
                {
                    Assert.NotEmpty(run.Bytes);
                    for (var i = 0; i < run.Bytes.Length; i++, at++)
                        Assert.Equal(senders[at], run.Sender);
                    Assert.Equal(At.AddSeconds(run.Bytes[0]), run.At);
                }

                Assert.Equal(Math.Min(offset + maxBytes, senders.Count), at);
            }
        }
    }

    [Fact]
    public void A_read_stops_at_the_runs_asked_for()
    {
        _traffic.Append(TrafficSender.Device, "a"u8, At);
        _traffic.Append(TrafficSender.Comrade, "bc"u8, At);
        _traffic.Append(TrafficSender.Device, "d"u8, At);

        var (start, runs) = _traffic.Read(0, 100, maxRuns: 2);

        Assert.Equal(0, start);
        Assert.Equal([Run(TrafficSender.Device, At, "a"), Run(TrafficSender.Comrade, At, "bc")], runs.Select(Plain));
    }

    [Fact]
    public void Nothing_is_kept_of_an_empty_append()
    {
        _traffic.Append(TrafficSender.Device, "a"u8, At);
        _traffic.Append(TrafficSender.Comrade, [], At);
        _traffic.Append(TrafficSender.Device, "b"u8, At);

        Assert.Equal([Run(TrafficSender.Device, At, "ab")], _traffic.Read(0, 100).Runs.Select(Plain));
    }

    [Fact]
    public async Task A_wait_ends_when_bytes_go_either_way()
    {
        var wait = _traffic.WaitAsync(0, TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        _traffic.Append(TrafficSender.Assistant, "x"u8, At);

        await wait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Each_byte_has_the_time_its_chunk_arrived()
    {
        _traffic.Append(TrafficSender.Device, "ab"u8, At);
        _traffic.Append(TrafficSender.Device, "cd"u8, At.AddMilliseconds(10));
        _traffic.Append(TrafficSender.Comrade, "e"u8, At.AddMilliseconds(10));
        _traffic.Append(TrafficSender.Device, "f"u8, At.AddMilliseconds(30));

        Assert.Equal(
            [At, At, At.AddMilliseconds(10), At.AddMilliseconds(10), At.AddMilliseconds(10), At.AddMilliseconds(30), null],
            Enumerable.Range(0, 7).Select(offset => _traffic.ArrivedAt(offset)));
    }

    [Fact]
    public void Bytes_whose_chunk_time_went_have_their_runs_time()
    {
        var traffic = new Traffic(maxRuns: 4);
        for (var i = 0; i < 6; i++)
            traffic.Append(TrafficSender.Device, "x"u8, At.AddMilliseconds(10 * i));

        // Two chunk times made room for newer ones; the run they are in keeps its own.
        Assert.Equal([At, At, At.AddMilliseconds(20), At.AddMilliseconds(50)], new long[] { 0, 1, 2, 5 }.Select(offset => traffic.ArrivedAt(offset)));
    }

    [Fact]
    public void Bytes_no_longer_kept_have_no_time()
    {
        var traffic = new Traffic(limit: 4);
        traffic.Append(TrafficSender.Device, "abcdef"u8, At);

        Assert.Equal([null, At], new long[] { 1, 2 }.Select(offset => traffic.ArrivedAt(offset)));
    }

    private static (TrafficSender, DateTimeOffset, string) Run(TrafficSender sender, DateTimeOffset at, string text) => (sender, at, text);

    private static (TrafficSender, DateTimeOffset, string) Plain(TrafficRun run) =>
        (run.Sender, run.At, System.Text.Encoding.ASCII.GetString(run.Bytes));
}
