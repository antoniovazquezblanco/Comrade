namespace COMrade.Core.Terminal;

/// <summary>Sizes in whole character cells, for snapping the window to the terminal's grid.</summary>
public static class CharacterGrid
{
    /// <summary>
    /// The whole-pixel cell size that fits <paramref name="count"/> cells, and no more, in
    /// <paramref name="available"/> pixels; null when no size or more than one size does.
    /// </summary>
    /// <remarks>The terminal fits as many cells as it can but does not say how big they are.</remarks>
    public static int? InferCellSize(int available, int count)
    {
        if (available <= 0 || count <= 0)
            return null;

        // count = floor(available / cell)  ⇔  available / (count + 1) < cell ≤ available / count.
        var largest = available / count;
        var smallest = available / (count + 1) + 1;
        return smallest == largest ? largest : null;
    }

    /// <summary>
    /// The window size closest to <paramref name="proposed"/> that leaves a whole number of cells, at
    /// least one, after <paramref name="chrome"/>, and that is not below <paramref name="minimum"/>.
    /// </summary>
    public static int Snap(int proposed, int chrome, int cell, int minimum = 0)
    {
        var cells = Math.Max(1, (int)Math.Round((proposed - chrome) / (double)cell, MidpointRounding.AwayFromZero));
        while (chrome + cells * cell < minimum)
            cells++;
        return chrome + cells * cell;
    }
}
