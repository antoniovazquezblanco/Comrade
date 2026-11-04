using COMrade.Core.Sessions;

namespace COMrade.Core.Tests.Sessions;

public class ReceiveBufferTests
{
    [Fact]
    public void Offsets_count_every_byte_received()
    {
        var buffer = new ReceiveBuffer();

        buffer.Append("abc"u8);
        buffer.Append("de"u8);

        Assert.Equal(0, buffer.Start);
        Assert.Equal(5, buffer.End);
        Assert.Equal((2, "cde"), Read(buffer, 2, 100));
    }

    [Fact]
    public void Read_returns_at_most_the_bytes_asked_for()
    {
        var buffer = new ReceiveBuffer();
        buffer.Append("abcdef"u8);

        Assert.Equal((1, "bc"), Read(buffer, 1, 2));
        Assert.Equal((6, ""), Read(buffer, 6, 2));
    }

    [Fact]
    public void Oldest_bytes_are_dropped_beyond_the_limit_and_offsets_keep_counting()
    {
        var buffer = new ReceiveBuffer(limit: 4);

        buffer.Append("abc"u8);
        buffer.Append("def"u8);

        Assert.Equal(2, buffer.Start);
        Assert.Equal(6, buffer.End);
        Assert.Equal((2, "cdef"), Read(buffer, 0, 100));
    }

    [Fact]
    public void Bytes_wrap_around_the_buffer_in_order()
    {
        var buffer = new ReceiveBuffer(limit: 5);

        foreach (var chunk in new[] { "ab", "cd", "ef", "gh", "i" })
            buffer.Append(System.Text.Encoding.ASCII.GetBytes(chunk));

        Assert.Equal((4, "efghi"), Read(buffer, 4, 100));
        Assert.Equal((6, "ghi"), Read(buffer, 6, 100));
    }

    [Fact]
    public void A_chunk_larger_than_the_limit_keeps_its_end()
    {
        var buffer = new ReceiveBuffer(limit: 4);
        buffer.Append("xy"u8);

        buffer.Append("abcdefgh"u8);

        Assert.Equal(6, buffer.Start);
        Assert.Equal((6, "efgh"), Read(buffer, 0, 100));
    }

    [Fact]
    public void Growing_keeps_the_bytes_in_order()
    {
        var buffer = new ReceiveBuffer(limit: 64 * 1024);
        var expected = new List<byte>();

        for (var i = 0; i < 300; i++)
        {
            var chunk = Enumerable.Range(i, 37).Select(value => (byte)value).ToArray();
            buffer.Append(chunk);
            expected.AddRange(chunk);
        }

        Assert.Equal(expected, buffer.Read(0, int.MaxValue).Bytes);
    }

    [Fact]
    public async Task Waiting_ends_when_bytes_arrive_past_the_offset()
    {
        var buffer = new ReceiveBuffer();
        buffer.Append("a"u8);

        var waiting = buffer.WaitAsync(1, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);

        buffer.Append("b"u8);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Waiting_for_bytes_already_there_ends_at_once()
    {
        var buffer = new ReceiveBuffer();
        buffer.Append("ab"u8);

        await buffer.WaitAsync(1, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Waiting_can_be_cancelled()
    {
        var buffer = new ReceiveBuffer();
        using var cancellation = new CancellationTokenSource();

        var waiting = buffer.WaitAsync(0, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    private static (long Start, string Text) Read(ReceiveBuffer buffer, long offset, int maxBytes)
    {
        var (start, bytes) = buffer.Read(offset, maxBytes);
        return (start, System.Text.Encoding.ASCII.GetString(bytes));
    }
}
