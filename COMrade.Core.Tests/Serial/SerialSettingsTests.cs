using COMrade.Core.Serial;

namespace COMrade.Core.Tests.Serial;

public class SerialSettingsTests
{
    [Fact]
    public void Default_is_115200_8N1_without_flow_control_sending_CR()
    {
        var settings = SerialSettings.Default;

        Assert.Equal(115200, settings.BaudRate);
        Assert.Equal("8N1", settings.FrameNotation);
        Assert.Equal(FlowControl.None, settings.FlowControl);
        Assert.Equal(LineEnding.Cr, settings.SendLineEnding);
    }

    [Theory]
    [InlineData(8, Parity.None, StopBits.One, "8N1")]
    [InlineData(7, Parity.Even, StopBits.One, "7E1")]
    [InlineData(8, Parity.Odd, StopBits.Two, "8O2")]
    [InlineData(5, Parity.Mark, StopBits.OnePointFive, "5M1.5")]
    [InlineData(6, Parity.Space, StopBits.Two, "6S2")]
    public void FrameNotation_combines_data_bits_parity_and_stop_bits(
        int dataBits, Parity parity, StopBits stopBits, string expected)
    {
        var settings = SerialSettings.Default with { DataBits = dataBits, Parity = parity, StopBits = stopBits };

        Assert.Equal(expected, settings.FrameNotation);
    }
}
