using COMrade.Core.Terminal;

namespace COMrade.Core.Tests.Terminal;

public class CharacterGridTests
{
    // Cascadia Mono 11 at 100 %: 9 × 17 pixel cells, measured on the real control.
    [Theory]
    [InlineData(1072, 119, 9)]
    [InlineData(603, 35, 17)]
    [InlineData(1071, 119, 9)]
    public void Cell_size_is_inferred_from_the_cells_that_fit(int available, int count, int expected)
    {
        Assert.Equal(expected, CharacterGrid.InferCellSize(available, count));
    }

    [Fact]
    public void Cell_size_is_unknown_when_several_sizes_fit()
    {
        // Two rows fit in 40 pixels with cells of 14 to 20 pixels.
        Assert.Null(CharacterGrid.InferCellSize(40, 2));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 0)]
    public void Cell_size_is_unknown_without_cells(int available, int count)
    {
        Assert.Null(CharacterGrid.InferCellSize(available, count));
    }

    [Theory]
    [InlineData(1100, 1103)] // 32 + 119 × 9.
    [InlineData(1099, 1103)]
    [InlineData(1098, 1094)] // 32 + 118 × 9.
    [InlineData(1094, 1094)]
    public void Window_snaps_to_the_nearest_whole_cell(int proposed, int expected)
    {
        Assert.Equal(expected, CharacterGrid.Snap(proposed, chrome: 32, cell: 9));
    }

    [Fact]
    public void Snapping_never_goes_below_the_minimum_size()
    {
        Assert.Equal(32 + 52 * 9, CharacterGrid.Snap(400, chrome: 32, cell: 9, minimum: 500));
    }

    [Fact]
    public void At_least_one_cell_is_kept()
    {
        Assert.Equal(41, CharacterGrid.Snap(10, chrome: 32, cell: 9));
    }
}
