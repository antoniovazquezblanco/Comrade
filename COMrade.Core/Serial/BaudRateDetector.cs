using System.Buffers;
using System.Text;

namespace COMrade.Core.Serial;

/// <summary>
/// Finds the speed a device talks at by listening to it at one common speed after another. At its
/// own speed a device's output reads as text; at any other it comes out as bytes text seldom holds,
/// such as 0x00, 0x80 and 0xFF. Only the speed changes: data bits, parity and stop bits stay.
/// </summary>
/// <remarks>
/// The device has to send text meanwhile: a boot log, or the prompt it prints again for Enter.
/// What is heard at a speed adds up over its listens, so a short prompt heard a few times is enough.
/// </remarks>
public static class BaudRateDetector
{
    /// <summary>
    /// The speeds tried after the port's own, most common first. 74880 is the boot ROM of ESP8266
    /// boards and 1500000 the console of Rockchip boards.
    /// </summary>
    public static IReadOnlyList<int> BaudRates { get; } =
        [115200, 9600, 57600, 38400, 19200, 230400, 460800, 921600, 1500000, 74880, 1000000, 2000000, 500000, 250000, 4800, 2400, 1200];

    /// <summary>How long one listen at a speed lasts.</summary>
    internal static readonly TimeSpan ListenTime = TimeSpan.FromMilliseconds(250);

    /// <summary>How long detection goes on without finding the speed.</summary>
    internal static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>Bytes a speed needs before it is chosen: fewer could read as text by chance.</summary>
    internal const int MinBytes = 16;

    /// <summary>Share of text a speed needs, which allows a stray byte such as the glitch of a device powering up.</summary>
    internal const double MinTextShare = 0.9;

    // Enough to judge a speed: a device that never pauses must not keep detection at one speed.
    private const int MaxListenBytes = 256;

    // A speed whose output reads as text but is still too short is listened to again, up to this
    // many listens in a row, before moving on.
    private const int MaxListensInARow = 4;

    // What is kept of the output heard at a speed, to show once it is chosen: about a screen.
    private const int MaxKeptBytes = 4096;

    private const int ReadBufferSize = 4096;

    /// <summary>
    /// Detects the speed of the device on <paramref name="connection"/>, open with
    /// <paramref name="settings"/>, trying their speed first. The connection is left at the last
    /// speed tried: the caller sets the one it keeps.
    /// </summary>
    /// <param name="wake">
    /// Bytes to send before each listen, such as Enter to make a prompt show again; empty for none.
    /// Asked each time, so the answer may change while detection runs.
    /// </param>
    /// <param name="progress">Told each speed as listening at it starts.</param>
    /// <returns><see cref="BaudRateDetection.Stopped"/> when <paramref name="cancellationToken"/> is cancelled.</returns>
    /// <exception cref="SerialPortException">The port failed.</exception>
    public static async Task<BaudRateDetection> DetectAsync(
        ISerialConnection connection,
        SerialSettings settings,
        Func<ReadOnlyMemory<byte>>? wake,
        IProgress<int>? progress,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        List<int> baudRates = [settings.BaudRate, .. BaudRates.Where(baudRate => baudRate != settings.BaudRate)];
        var heard = new Dictionary<int, Heard>();
        using var timeLimit = new CancellationTokenSource(TimeLimit, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeLimit.Token);
        try
        {
            while (baudRates.Count > 0)
            {
                foreach (var baudRate in baudRates.ToList())
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (!TrySetBaudRate(connection, settings with { BaudRate = baudRate }))
                    {
                        baudRates.Remove(baudRate);
                        continue;
                    }

                    progress?.Report(baudRate);
                    if (!heard.TryGetValue(baudRate, out var output))
                        heard[baudRate] = output = new Heard();

                    for (var listens = 0; listens < MaxListensInARow; listens++)
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        if (wake?.Invoke() is { IsEmpty: false } bytes)
                            await connection.WriteAsync(bytes, stop.Token);

                        var count = await ListenAsync(connection, output, time, stop.Token);
                        if (output.IsText)
                            return new BaudRateDetection(BaudRateDetectionOutcome.Detected, baudRate, output.Kept);
                        if (count == 0 || !output.MayBeText)
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }

        if (cancellationToken.IsCancellationRequested)
            return BaudRateDetection.Stopped;

        var outcome = heard.Values.Any(output => output.Count >= MinBytes)
            ? BaudRateDetectionOutcome.Unreadable
            : BaudRateDetectionOutcome.Silent;
        return new BaudRateDetection(outcome, null, []);
    }

    /// <summary>
    /// How many bytes of <paramref name="data"/> are text: printable ASCII, tab, line breaks,
    /// backspace, escape (colour codes) and well-formed UTF-8.
    /// </summary>
    internal static int CountText(ReadOnlySpan<byte> data)
    {
        var text = 0;
        while (!data.IsEmpty)
        {
            var length = 1;
            if (data[0] < 0x80)
            {
                if (data[0] is >= 0x20 and < 0x7F or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\b' or 0x1B)
                    text++;
            }
            else if (Rune.DecodeFromUtf8(data, out _, out var consumed) is OperationStatus.Done)
            {
                text += consumed;
                length = consumed;
            }

            data = data[length..];
        }

        return text;
    }

    // False when the driver refuses the speed: not every adapter does every speed.
    private static bool TrySetBaudRate(ISerialConnection connection, SerialSettings settings)
    {
        try
        {
            connection.ApplySettings(settings);
        }
        catch (SerialPortException ex) when (ex.Error is SerialPortError.InvalidSettings)
        {
            return false;
        }

        // What arrived at the previous speed would count against this one.
        connection.DiscardInput();
        return true;
    }

    // Reads for one listen time, or until enough has arrived; returns how many bytes.
    private static async Task<int> ListenAsync(ISerialConnection connection, Heard output, TimeProvider time, CancellationToken stop)
    {
        using var timeUp = new CancellationTokenSource(ListenTime, time);
        using var listen = CancellationTokenSource.CreateLinkedTokenSource(stop, timeUp.Token);
        var buffer = new byte[ReadBufferSize];
        var total = 0;
        while (total < MaxListenBytes)
        {
            int count;
            try
            {
                count = await connection.ReadAsync(buffer, listen.Token);
            }
            catch (OperationCanceledException) when (listen.IsCancellationRequested)
            {
                break;
            }

            if (count == 0)
                break;

            output.Add(buffer.AsSpan(0, count));
            total += count;
        }

        return total;
    }

    // What was heard at one speed, over all its listens.
    private sealed class Heard
    {
        private readonly List<byte> _kept = [];
        private int _text;

        public int Count { get; private set; }

        public byte[] Kept => [.. _kept];

        /// <summary>Enough was heard, and it reads as text.</summary>
        public bool IsText => Count >= MinBytes && MayBeText;

        /// <summary>What was heard reads as text, but may be too short to tell.</summary>
        public bool MayBeText => Count > 0 && _text >= Count * MinTextShare;

        public void Add(ReadOnlySpan<byte> data)
        {
            Count += data.Length;
            _text += CountText(data);
            _kept.AddRange(data[..Math.Min(data.Length, MaxKeptBytes - _kept.Count)]);
        }
    }
}
