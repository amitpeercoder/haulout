using Haulout.Sim.Time;

namespace Haulout.Sim.Weather;

/// <summary>
/// The season's weather, one record per day, generated once from a seed. Not a simulator:
/// persistence (smoothed temperature, wet/dry spells, steady wind) over monthly curves.
/// </summary>
public sealed class WeatherSeries
{
    public const int LastDay = 720;

    internal static readonly string[] Directions =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    private readonly WeatherDay[] _days;

    private WeatherSeries(WeatherDay[] days) => _days = days;

    public int Count => _days.Length;

    public WeatherDay this[int day] => _days[Math.Clamp(day, 0, _days.Length - 1)];

    public WeatherDay For(int day) => this[day];

    public static WeatherSeries Fair() => Constant(new WeatherDay(0, 3, "southwest", 0, 6, 2));

    public static WeatherSeries Constant(WeatherDay template) =>
        new(Enumerable.Range(0, LastDay + 1).Select(d => template with { Day = d }).ToArray());

    /// <summary>A hand-made series (tests, scenarios): the day number is set on whatever the function returns.</summary>
    public static WeatherSeries From(Func<int, WeatherDay> day) =>
        new(Enumerable.Range(0, LastDay + 1).Select(d => day(d) with { Day = d }).ToArray());

    public static WeatherSeries Generate(int seed, Climate? climate = null)
    {
        climate ??= Climate.Default;
        var rng = new SeededRandom(seed * 31 + 7);
        var days = new WeatherDay[LastDay + 1];

        double tempAnomaly = 0, windAnomaly = 0;
        var wet = false;
        var gale = false;
        var dir = 5; // southwest

        for (var day = 0; day <= LastDay; day++)
        {
            var c = Blend(climate, day);

            // Temperature: an AR(1) walk around the month's midpoint, scaled to its half-range.
            var half = (c.TempHi - c.TempLo) / 2;
            tempAnomaly = 0.75 * tempAnomaly + 0.6 * half * Normal(rng) * 0.5;
            tempAnomaly = Math.Clamp(tempAnomaly, -1.3 * half, 1.3 * half);
            var temp = (c.TempLo + c.TempHi) / 2 + tempAnomaly;
            var night = temp - c.NightDrop + 1.2 * Normal(rng);

            // Rain: two-state Markov chain.
            wet = rng.NextDouble() < (wet ? c.RainAfterRain : c.RainChance);
            var rain = 0.0;
            if (wet) rain = Math.Round(Math.Max(0.6, -c.RainMean * Math.Log(1 - rng.NextDouble())), 1);

            // Wind: steady anomaly around the mean; gales are their own event and tend to linger a day.
            windAnomaly = 0.6 * windAnomaly + 0.9 * Normal(rng);
            var galeChance = gale ? Math.Max(c.GaleChance, 0.25) : c.GaleChance;
            gale = rng.NextDouble() < galeChance;
            int force;
            if (gale) force = rng.NextDouble() < 0.7 ? 8 : 9;
            else force = Math.Clamp((int)Math.Round(c.WindMean + windAnomaly), 0, 7);

            // Direction: usually yesterday's, otherwise mostly the prevailing set.
            if (rng.NextDouble() > 0.55)
            {
                if (rng.NextDouble() < 0.8)
                {
                    var pick = climate.Months[new GameClock(day).Month].Prevailing;
                    var idx = Array.IndexOf(Directions, pick[(int)(rng.NextDouble() * pick.Count)]);
                    dir = idx >= 0 ? idx : dir;
                }
                else dir = (int)(rng.NextDouble() * 8);
            }

            days[day] = new WeatherDay(day, force, Directions[dir], rain, Math.Round(temp, 1), Math.Round(night, 1));
        }
        return new WeatherSeries(days);
    }

    private readonly record struct Curve(
        double TempLo, double TempHi, double NightDrop, double RainChance, double RainAfterRain,
        double RainMean, double WindMean, double GaleChance);

    /// <summary>Interpolates between month centres so no parameter jumps at a month boundary.</summary>
    private static Curve Blend(Climate climate, int day)
    {
        var clock = new GameClock(day);
        var pos = (clock.DayOfYear + 0.5) / GameClock.DaysPerMonth - 0.5; // month-centre coordinates
        var i = (int)Math.Floor(pos);
        var f = pos - i;
        var a = climate.Months[(i + 12) % 12];
        var b = climate.Months[(i + 13) % 12];
        double L(double x, double y) => x + (y - x) * f;
        return new Curve(
            L(a.TempC[0], b.TempC[0]), L(a.TempC[1], b.TempC[1]), L(a.NightDropC, b.NightDropC),
            L(a.RainChance, b.RainChance), L(a.RainAfterRainChance, b.RainAfterRainChance),
            L(a.RainMeanMm, b.RainMeanMm), L(a.WindMeanForce, b.WindMeanForce), L(a.GaleChance, b.GaleChance));
    }

    /// <summary>Standard normal via Box-Muller.</summary>
    private static double Normal(IRandom rng)
    {
        var u1 = 1 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
