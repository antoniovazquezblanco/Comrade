namespace COMrade.Core.Serial;

/// <summary>How a speed detection ended, see <see cref="BaudRateDetector"/>.</summary>
public enum BaudRateDetectionOutcome
{
    /// <summary>The device's output read as text at <see cref="BaudRateDetection.BaudRate"/>.</summary>
    Detected,

    /// <summary>The device sent nothing, or too little at any speed to tell.</summary>
    Silent,

    /// <summary>The device sent enough, but its output read as text at no speed.</summary>
    Unreadable,

    /// <summary>Stopped before the end: cancelled, or the port closed or failed.</summary>
    Stopped,
}
