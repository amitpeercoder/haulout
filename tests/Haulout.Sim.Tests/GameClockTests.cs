using Haulout.Sim.Time;

namespace Haulout.Sim.Tests;

public class GameClockTests
{
    [Theory]
    [InlineData(0, Season.Winter)]      // January
    [InlineData(60, Season.Spring)]     // March
    [InlineData(150, Season.Summer)]    // June
    [InlineData(270, Season.Autumn)]    // October
    [InlineData(330, Season.Winter)]    // December
    public void Season_follows_month(int day, Season expected) =>
        Assert.Equal(expected, new GameClock(day).Season);

    [Fact]
    public void Year_rolls_over_after_360_days() =>
        Assert.Equal(1, new GameClock(0).AddDays(360).Year);
}
