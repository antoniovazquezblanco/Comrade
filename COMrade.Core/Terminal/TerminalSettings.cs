namespace COMrade.Core.Terminal;

/// <summary>How the terminal treats what is typed and what arrives.</summary>
/// <param name="LocalEcho">Show typed text in the terminal, for devices that do not echo it back.</param>
/// <param name="NewLineOnLf">Start a new line on a bare LF (devices that end lines with LF only).</param>
/// <param name="NewLineOnCr">Start a new line on a bare CR (devices that end lines with CR only).</param>
public sealed record TerminalSettings(bool LocalEcho = false, bool NewLineOnLf = true, bool NewLineOnCr = false)
{
    public static TerminalSettings Default { get; } = new();
}
