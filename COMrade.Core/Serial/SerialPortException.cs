namespace COMrade.Core.Serial;

/// <summary>A serial port operation failed for the reason in <see cref="Error"/>.</summary>
public sealed class SerialPortException(SerialPortError error, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public SerialPortError Error { get; } = error;
}
