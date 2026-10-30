namespace COMrade.Core.Serial;

/// <summary>Why a serial port could not be opened or stopped working.</summary>
public enum SerialPortError
{
    /// <summary>Another program has the port open.</summary>
    InUse,

    /// <summary>Windows refused access to the port.</summary>
    AccessDenied,

    /// <summary>The port does not exist (the device is not connected).</summary>
    NotFound,

    /// <summary>The device was unplugged or stopped responding while the port was open.</summary>
    DeviceRemoved,

    /// <summary>The driver rejected the line settings.</summary>
    InvalidSettings,

    /// <summary>Any other read, write or driver failure.</summary>
    IoFailure,
}
