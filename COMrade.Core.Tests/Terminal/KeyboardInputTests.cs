using System.Text;
using COMrade.Core.Serial;
using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class KeyboardInputTests
{
    [Theory]
    [InlineData(LineEnding.Cr, "ls\r")]
    [InlineData(LineEnding.Lf, "ls\n")]
    [InlineData(LineEnding.CrLf, "ls\r\n")]
    [InlineData(LineEnding.None, "ls\r")]
    public void Enter_sends_the_configured_line_ending(LineEnding ending, string expected)
    {
        Assert.Equal(expected, Encoding.UTF8.GetString(KeyboardInput.Encode("ls\r", ending)));
    }

    [Fact]
    public void Pasted_lines_each_end_with_the_configured_line_ending()
    {
        Assert.Equal("a\r\nb\r\n", Encoding.UTF8.GetString(KeyboardInput.Encode("a\rb\r", LineEnding.CrLf)));
    }

    [Fact]
    public void Control_characters_and_utf8_are_sent_as_is()
    {
        Assert.Equal([0x03], KeyboardInput.Encode("\x03", LineEnding.CrLf));
        Assert.Equal(Encoding.UTF8.GetBytes("ñ€"), KeyboardInput.Encode("ñ€", LineEnding.Cr));
    }

    [Fact]
    public void Local_echo_starts_a_new_line_on_enter()
    {
        Assert.Equal("ls\r\n", Encoding.UTF8.GetString(KeyboardInput.Echo("ls\r")));
    }

    [Fact]
    public void Local_echo_erases_on_backspace()
    {
        Assert.Equal("ab\b \b", Encoding.UTF8.GetString(KeyboardInput.Echo("ab\x7f")));
    }
}
