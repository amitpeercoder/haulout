using Haulout.Sim.Components;

namespace Haulout.Sim.Tests;

public class ComponentTests
{
    [Fact]
    public void Impeller_wears_with_engine_hours_more_than_calendar()
    {
        // An impeller is good for ~2 years or ~200 engine hours.
        var impeller = new Component("impeller", "Raw-water impeller", 1.0,
            WearRate.CalendarYears(2, perUseHour: 1.0 / 200));

        impeller.Age(days: 30, useHours: 0);
        var calendarOnly = impeller.Condition;

        impeller.Age(days: 0, useHours: 50);
        var afterUse = impeller.Condition;

        Assert.InRange(calendarOnly, 0.95, 0.96);
        Assert.InRange(afterUse, 0.70, 0.71);
    }

    [Fact]
    public void Condition_never_goes_below_zero()
    {
        var c = new Component("belt", "Alternator belt", 0.1, WearRate.CalendarYears(1));
        c.Age(days: 3600, useHours: 0);
        Assert.Equal(0, c.Condition);
    }

    [Fact]
    public void Replace_restores_to_new()
    {
        var c = new Component("belt", "Alternator belt", 0.3, WearRate.CalendarYears(1));
        c.Replace();
        Assert.Equal(1, c.Condition);
    }

    [Fact]
    public void Seeded_random_is_replayable()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);
        Assert.Equal(a.NextDouble(), b.NextDouble());
    }
}
