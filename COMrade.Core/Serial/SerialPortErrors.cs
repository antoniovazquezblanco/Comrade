namespace COMrade.Core.Serial;

/// <summary>Translates the exceptions System.IO.Ports throws into <see cref="SerialPortError"/>s.</summary>
internal static class SerialPortErrors
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidHandle = 6;
    private const int ErrorBadCommand = 22;
    private const int ErrorGenFailure = 31;
    private const int ErrorSharingViolation = 32;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNoSuchDevice = 433;
    private const int ErrorOperationAborted = 995;
    private const int ErrorDeviceNotConnected = 1167;
    private const int ErrorDeviceRemoved = 1617;

    /// <summary>True for the exception types serial port operations fail with (not cancellation).</summary>
    public static bool IsPortFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or TimeoutException;

    public static SerialPortException ToException(Exception exception, string portName, bool opening)
    {
        var error = Classify(exception, opening);
        return new SerialPortException(error, $"{portName}: {Describe(error)}", exception);
    }

    /// <param name="opening">True while opening the port, false once it is open.</param>
    public static SerialPortError Classify(Exception exception, bool opening) => exception switch
    {
        // Windows reports a port held by another program as "access denied". Once the port is
        // open, the same error means the handle stopped working because the device went away.
        UnauthorizedAccessException => opening ? SerialPortError.InUse : SerialPortError.DeviceRemoved,
        ArgumentException => SerialPortError.InvalidSettings,
        IOException io => Win32Error(io) switch
        {
            ErrorFileNotFound or ErrorPathNotFound => SerialPortError.NotFound,
            ErrorAccessDenied or ErrorSharingViolation => opening ? SerialPortError.InUse : SerialPortError.DeviceRemoved,
            ErrorInvalidParameter => SerialPortError.InvalidSettings,
            ErrorGenFailure when opening => SerialPortError.IoFailure,
            ErrorGenFailure or ErrorInvalidHandle or ErrorBadCommand or ErrorNoSuchDevice
                or ErrorOperationAborted or ErrorDeviceNotConnected or ErrorDeviceRemoved => SerialPortError.DeviceRemoved,
            _ => SerialPortError.IoFailure,
        },
        _ => SerialPortError.IoFailure,
    };

    private static int Win32Error(IOException exception) =>
        (exception.HResult & 0xFFFF0000) == 0x80070000 ? exception.HResult & 0xFFFF : -1;

    private static string Describe(SerialPortError error) => error switch
    {
        SerialPortError.InUse => "the port is in use by another program, or access was denied.",
        SerialPortError.AccessDenied => "access to the port was denied.",
        SerialPortError.NotFound => "the port does not exist; the device is not connected.",
        SerialPortError.DeviceRemoved => "the device was disconnected.",
        SerialPortError.InvalidSettings => "the driver rejected the line settings.",
        _ => "the port stopped working.",
    };
}
