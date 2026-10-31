using System.Text;

namespace COMrade.Core.Logging;

/// <summary>
/// Removes colour, cursor and other terminal control sequences from text, keeping what a reader
/// sees: printable text, tabs and line breaks. Sequences split across reads are handled.
/// </summary>
public sealed class AnsiStripper
{
    private const char Escape = '\u001b';
    private const char Bell = '\u0007';

    private State _state;

    private enum State
    {
        Text,
        Escape,
        ControlSequence,
        OperatingSystemCommand,
        OperatingSystemCommandEscape,
    }

    /// <summary>Appends the readable part of <paramref name="text"/> to <paramref name="output"/>.</summary>
    public void Strip(ReadOnlySpan<char> text, StringBuilder output)
    {
        foreach (var c in text)
        {
            switch (_state)
            {
                case State.Text when c == Escape:
                    _state = State.Escape;
                    break;
                case State.Text when c is '\t' or '\n' or '\r' || !char.IsControl(c):
                    output.Append(c);
                    break;
                case State.Text:
                    // Other control characters (bell, backspace...) do not belong in a text file.
                    break;
                case State.Escape when c == '[':
                    _state = State.ControlSequence;
                    break;
                case State.Escape when c == ']':
                    _state = State.OperatingSystemCommand;
                    break;
                case State.Escape:
                    // Two-character sequences such as ESC 7 or ESC M.
                    _state = State.Text;
                    break;
                case State.ControlSequence when c is >= '@' and <= '~':
                    // The final character of ESC [ ... m, ESC [ 2 J and the like.
                    _state = State.Text;
                    break;
                case State.ControlSequence:
                    break;
                case State.OperatingSystemCommand when c == Bell:
                    _state = State.Text;
                    break;
                case State.OperatingSystemCommand when c == Escape:
                    _state = State.OperatingSystemCommandEscape;
                    break;
                case State.OperatingSystemCommand:
                    break;
                case State.OperatingSystemCommandEscape:
                    // ESC \ ends a window title or similar command.
                    _state = c == '\\' ? State.Text : State.OperatingSystemCommand;
                    break;
            }
        }
    }
}
