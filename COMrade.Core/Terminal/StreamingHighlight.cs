using System.Text;
using COMrade.Core.Logging;

namespace COMrade.Core.Terminal;

/// <summary>
/// Marks the matches of a search in output as it arrives, read by read, as
/// <see cref="OutputHighlight.Mark"/> marks output kept. Text that could begin a match waits for the
/// next read, or for <see cref="Flush"/>, so a match split between two reads is marked whole.
/// </summary>
/// <remarks>Not thread-safe: reads are marked one at a time, in order.</remarks>
public sealed class StreamingHighlight(string query, bool matchCase)
{
    private const char Escape = '\u001b';

    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    // Output not shown yet: text that could begin a match and what follows it, or the start of a
    // code whose rest is to come. It starts outside any code, so it can be read again from there.
    private string _held = string.Empty;

    /// <summary>Whether output waits for more, to be shown by <see cref="Flush"/> if none comes.</summary>
    public bool IsHolding => _held.Length > 0;

    /// <summary>
    /// What to show for <paramref name="data"/>, the next read of output: all that cannot be part of
    /// a match still to come, marked.
    /// </summary>
    public byte[] Mark(ReadOnlySpan<byte> data)
    {
        var chars = new char[_decoder.GetCharCount(data, flush: false)];
        _decoder.GetChars(data, chars, flush: false);
        var raw = _held + new string(chars);

        var text = new StringBuilder(raw.Length);
        var sources = new List<int>(raw.Length);
        var stripper = new AnsiStripper();
        stripper.Strip(raw, text, sources);

        // A code still open at the end waits for its rest; the text before it can be searched.
        var complete = stripper.InSequence ? raw.LastIndexOf(Escape) : raw.Length;
        var visible = sources.Count;
        while (visible > 0 && sources[visible - 1] >= complete)
            visible--;
        var searched = text.ToString(0, visible);
        var spans = OutputHighlight.Spans(searched, query, matchCase, selected: null);

        // Text at the end that could begin a match waits, unless a match goes on into it.
        var wait = searched.Length - PrefixAtEnd(searched);
        foreach (var span in spans)
        {
            if (span.Start < wait && span.End > wait)
                wait = span.End;
        }

        var cut = wait < searched.Length ? sources[wait] : complete;
        var marked = new StringBuilder(cut + spans.Count * 64);
        OutputHighlight.AppendMarked(marked, raw, sources, spans.Where(span => span.End <= wait), cut);
        _held = raw[cut..];
        return Encoding.UTF8.GetBytes(marked.ToString());
    }

    /// <summary>
    /// The text held for a match that did not come, as it is: when no output followed for a moment.
    /// A code still open stays held, as the rest of it is on its way.
    /// </summary>
    public byte[] Flush()
    {
        var stripper = new AnsiStripper();
        stripper.Strip(_held, new StringBuilder());
        var cut = stripper.InSequence ? _held.LastIndexOf(Escape) : _held.Length;

        var shown = _held[..cut];
        _held = _held[cut..];
        return Encoding.UTF8.GetBytes(shown);
    }

    // The length of the longest end of the last line of text that begins the query, the whole
    // query excepted.
    private int PrefixAtEnd(string text)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var lineStart = text.LastIndexOf('\n') + 1;
        for (var length = Math.Min(query.Length - 1, text.Length - lineStart); length > 0; length--)
        {
            if (string.Compare(text, text.Length - length, query, 0, length, comparison) == 0)
                return length;
        }

        return 0;
    }
}
