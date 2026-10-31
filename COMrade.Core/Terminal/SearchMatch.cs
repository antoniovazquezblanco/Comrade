namespace COMrade.Core.Terminal;

/// <summary>A place where the searched text appears in a terminal's output.</summary>
/// <param name="Line">Line number, from 1 at the first line searched.</param>
/// <param name="Column">Position in the line, from 0.</param>
/// <param name="Length">Characters found, as many as were searched for.</param>
/// <param name="Text">The whole line, without colour codes.</param>
public sealed record SearchMatch(int Line, int Column, int Length, string Text);
