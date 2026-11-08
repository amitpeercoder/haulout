using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests;

public class WeatherTests
{
    private static IEnumerable<WeatherDay> Month(WeatherSeries s, int month, int year = 0) =>
        Enumerable.Range(year * 360 + month * 30, 30).Select(s.For);

    private static IEnumerable<WeatherSeries> Seeds() => Enumerable.Range(1, 20).Select(x => WeatherSeries.Generate(x));

    [Fact]
    public void Same_seed_gives_identical_days()
    {
        var a = WeatherSeries.Generate(4);
        var b = WeatherSeries.Generate(4);
        Assert.Equal(WeatherSeries.LastDay + 1, a.Count);
        for (var d = 0; d <= 720; d++) Assert.Equal(a[d], b[d]);
    }

    [Fact]
    public void Different_seeds_differ()
    {
        var a = WeatherSeries.Generate(1);
        var b = WeatherSeries.Generate(2);
        Assert.Contains(Enumerable.Range(0, 721), d => a[d] != b[d]);
    }

    [Fact]
    public void Embedded_climate_has_twelve_months_and_a_placeholder_note()
    {
        Assert.Equal(12, Climate.Default.Months.Count);
        Assert.Contains("placeholder", Climate.Default.Placeholders);
    }

    [Fact]
    public void March_is_cold_wet_and_sometimes_gales()
    {
        var march = Seeds().SelectMany(s => Month(s, 2)).ToList();
        var meanTemp = march.Average(d => d.TempC);
        var rainFraction = march.Count(WeatherWords.IsRaining) / (double)march.Count;
        Assert.InRange(meanTemp, 2, 8);
        Assert.True(rainFraction > 0.4, $"rain fraction {rainFraction}");
        Assert.Contains(march, d => d.WindForce >= 7);
        Assert.Contains(march, d => d.NightC < 0);
    }

    [Fact]
    public void July_is_warmer_than_March_and_winter_colder_than_summer()
    {
        var all = Seeds().ToList();
        double Mean(int month) => all.SelectMany(s => Month(s, month)).Average(d => d.TempC);
        Assert.True(Mean(6) > Mean(2) + 5);
        Assert.True(Mean(0) < Mean(6) - 10);
        Assert.True(Mean(11) < Mean(7));
    }

    [Fact]
    public void Every_day_is_in_range_and_directions_are_words()
    {
        var valid = new[] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
        foreach (var s in Seeds())
            for (var d = 0; d <= 720; d++)
            {
                var day = s[d];
                Assert.Equal(d, day.Day);
                Assert.InRange(day.WindForce, 0, 9);
                Assert.True(day.RainMm >= 0);
                Assert.Contains(day.WindFrom, valid);
            }
    }

    [Fact]
    public void Temperature_does_not_jump_across_month_ends()
    {
        foreach (var s in Seeds())
            for (var d = 1; d <= 720; d++)
                Assert.True(Math.Abs(s[d].TempC - s[d - 1].TempC) < 9, $"day {d}");
    }

    [Fact]
    public void Lookups_clamp_to_the_range()
    {
        var s = WeatherSeries.Generate(3);
        Assert.Equal(s[0], s.For(-5));
        Assert.Equal(s[WeatherSeries.LastDay], s.For(5000));
    }

    [Fact]
    public void Fair_never_rains_nor_blows()
    {
        var fair = WeatherSeries.Fair();
        for (var d = 0; d <= 720; d++)
        {
            Assert.Equal(new WeatherDay(d, 3, "southwest", 0, 6, 2), fair[d]);
            Assert.False(WeatherWords.IsRaining(fair[d]));
            Assert.False(WeatherWords.IsGale(fair[d]));
        }
    }

    [Fact]
    public void Constant_sets_the_day_on_each_record()
    {
        var s = WeatherSeries.Constant(new WeatherDay(0, 8, "west", 12, 3, -1));
        Assert.Equal(40, s[40].Day);
        Assert.Equal(8, s[40].WindForce);
    }

    [Fact]
    public void Describe_reads_in_the_games_voice()
    {
        Assert.Equal("Grey, 5 °C, rain on and off, a fresh breeze from the southwest.",
            WeatherWords.Describe(new WeatherDay(0, 5, "southwest", 4, 5, 2)));
        Assert.Equal("Bright, 12 °C, a gentle breeze from the west.",
            WeatherWords.Describe(new WeatherDay(0, 3, "west", 0, 12, 6)));
        Assert.Equal("Dry, 3 °C, a strong breeze from the north, frost overnight.",
            WeatherWords.Describe(new WeatherDay(0, 6, "north", 0, 3, -2)));
        Assert.Equal("Grey, 6 °C, heavy rain, a gale from the west.",
            WeatherWords.Describe(new WeatherDay(0, 8, "west", 14, 6, 3)));
        Assert.Equal("Grey, 7 °C, drizzle, no wind.",
            WeatherWords.Describe(new WeatherDay(0, 0, "north", 1, 7, 3)));
    }

    private static WeatherDay D(int force, double rain) => new(0, force, "west", rain, 5, 2);

    [Fact]
    public void Summarise_names_the_runs()
    {
        var rain = D(4, 5);
        var dry = D(3, 0);
        var gale = D(8, 6);
        Assert.Equal("Six days of rain, then two dry ones.",
            WeatherWords.Summarise([rain, rain, rain, rain, rain, rain, dry, dry]));
        Assert.Equal("Three dry days, then a gale, then four days of rain.",
            WeatherWords.Summarise([dry, dry, dry, gale, rain, rain, rain, rain]));
        Assert.Equal("A dry day.", WeatherWords.Summarise([dry]));
        Assert.Equal("A day of rain.", WeatherWords.Summarise([rain]));
        Assert.Equal("A gale.", WeatherWords.Summarise([gale]));
        Assert.Equal("A mixed week: three days of rain, three dry.",
            WeatherWords.Summarise([rain, dry, rain, dry, rain, dry]));
    }

    [Fact]
    public void IsRaining_and_IsGale_use_their_thresholds()
    {
        Assert.True(WeatherWords.IsRaining(D(3, 0.5)));
        Assert.False(WeatherWords.IsRaining(D(3, 0.4)));
        Assert.True(WeatherWords.IsGale(D(7, 0)));
        Assert.False(WeatherWords.IsGale(D(6, 0)));
        Assert.False(WeatherWords.IsGale(D(7, 0), 8));
    }
}
