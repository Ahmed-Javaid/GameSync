using GameSync.UI.Controls;

namespace GameSync.Core.Tests;

/// <summary>
/// The owner, 18 Oct 2026, of first run's Your games, its cards at the top and the lower half of the window empty: "use
/// equal block spacing if there is alot of empty space". A step's blocks share the room left equally.
/// </summary>
public class SpreadTests
{
    [Fact]
    public void ONB_07_blocks_share_the_room_left_equally_above_between_and_below()
    {
        // Two blocks, 300 high in all and 16 apart, given 600: 284 left over, a third of it above, between and below.
        var (top, gap) = GsSpread.Gaps(600, 300, 2, 16, 160);
        Assert.Equal(284 / 3.0, top, 6);
        Assert.Equal(16 + (284 / 3.0), gap, 6);
        Assert.Equal(top, 600 - (top + 300 + gap), 6);
    }

    [Fact]
    public void ONB_07_a_tall_window_grows_each_gap_by_the_most_allowed_and_splits_the_rest_above_and_below()
    {
        var (top, gap) = GsSpread.Gaps(1600, 300, 2, 16, 160);
        Assert.Equal(16 + 160, gap, 6);
        Assert.Equal(top, 1600 - (top + 300 + gap), 6);
    }

    [Fact]
    public void ONB_07_blocks_taller_than_the_room_stack_at_their_spacing()
    {
        Assert.Equal((0d, 16d), GsSpread.Gaps(400, 500, 3, 16, 160));
        Assert.Equal((0d, 16d), GsSpread.Gaps(double.PositiveInfinity, 500, 3, 16, 160));
        Assert.Equal((0d, 16d), GsSpread.Gaps(400, 0, 0, 16, 160));
    }
}
