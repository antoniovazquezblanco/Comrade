using COMrade.Core.Serial;

namespace COMrade.Core.Tests.Serial;

public class SerialPortErrorsTests
{
    [Fact]
    public void Missing_port_is_not_found()
    {
        Assert.Equal(SerialPortError.NotFound, Classify(new IOException("COM99", Win32(2)), opening: true));
        Assert.Equal(SerialPortError.NotFound, Classify(new IOException("COM99", Win32(3)), opening: true));
    }

    [Fact]
    public void Access_denied_while_opening_means_another_program_has_the_port()
    {
        Assert.Equal(SerialPortError.InUse, Classify(new UnauthorizedAccessException(), opening: true));
        Assert.Equal(SerialPortError.InUse, Classify(new IOException("busy", Win32(32)), opening: true));
    }

    [Fact]
    public void Access_denied_on_an_open_port_means_the_device_went_away()
    {
        Assert.Equal(SerialPortError.DeviceRemoved, Classify(new UnauthorizedAccessException(), opening: false));
    }

    [Theory]
    [InlineData(6)]    // ERROR_INVALID_HANDLE
    [InlineData(22)]   // ERROR_BAD_COMMAND
    [InlineData(31)]   // ERROR_GEN_FAILURE
    [InlineData(433)]  // ERROR_NO_SUCH_DEVICE
    [InlineData(995)]  // ERROR_OPERATION_ABORTED
    [InlineData(1167)] // ERROR_DEVICE_NOT_CONNECTED
    [InlineData(1617)] // ERROR_DEVICE_REMOVED
    public void Errors_typical_of_an_unplugged_device_mean_device_removed(int win32Error)
    {
        Assert.Equal(SerialPortError.DeviceRemoved, Classify(new IOException("gone", Win32(win32Error)), opening: false));
    }

    [Fact]
    public void Device_not_functioning_while_opening_is_an_io_failure()
    {
        Assert.Equal(SerialPortError.IoFailure, Classify(new IOException("not functioning", Win32(31)), opening: true));
    }

    [Fact]
    public void Rejected_parameters_are_invalid_settings()
    {
        Assert.Equal(SerialPortError.InvalidSettings, Classify(new ArgumentOutOfRangeException("BaudRate"), opening: false));
        Assert.Equal(SerialPortError.InvalidSettings, Classify(new IOException("bad", Win32(87)), opening: false));
    }

    [Fact]
    public void Anything_else_is_an_io_failure()
    {
        Assert.Equal(SerialPortError.IoFailure, Classify(new IOException("strange"), opening: false));
        Assert.Equal(SerialPortError.IoFailure, Classify(new TimeoutException(), opening: false));
    }

    [Fact]
    public void Cancellation_is_not_treated_as_a_port_failure()
    {
        Assert.False(SerialPortErrors.IsPortFailure(new OperationCanceledException()));
        Assert.True(SerialPortErrors.IsPortFailure(new IOException()));
        Assert.True(SerialPortErrors.IsPortFailure(new ObjectDisposedException("port")));
    }

    [Fact]
    public void Exception_names_the_port_and_keeps_the_original_error()
    {
        var original = new UnauthorizedAccessException();

        var exception = SerialPortErrors.ToException(original, "COM5", opening: true);

        Assert.Equal(SerialPortError.InUse, exception.Error);
        Assert.StartsWith("COM5:", exception.Message);
        Assert.Same(original, exception.InnerException);
    }

    private static SerialPortError Classify(Exception exception, bool opening) =>
        SerialPortErrors.Classify(exception, opening);

    private static int Win32(int error) => unchecked((int)0x80070000) | error;
}
