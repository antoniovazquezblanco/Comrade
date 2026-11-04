namespace COMrade.Core.Serial;

/// <summary>The result of a speed detection, see <see cref="BaudRateDetector"/>.</summary>
/// <param name="BaudRate">The device's speed; null unless detected.</param>
/// <param name="Output">What the device sent at that speed while it was listened to; empty unless detected.</param>
public sealed record BaudRateDetection(BaudRateDetectionOutcome Outcome, int? BaudRate, byte[] Output)
{
    public static BaudRateDetection Stopped { get; } = new(BaudRateDetectionOutcome.Stopped, null, []);
}
