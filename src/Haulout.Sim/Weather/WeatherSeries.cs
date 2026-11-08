using Haulout.Sim.Time;

namespace Haulout.Sim.Weather;

/// <summary>
/// The season's weather, one record per day, generated once from a seed. Not a simulator:
/// persistence (smoothed temperature, wet/dry spells, steady wind) over monthly curves.
/// </summary>
public sealed class WeatherSeries
{
    /// <summary>Three years of days. Generation is sequential, so days 0 to 720 are the same for a seed whatever this is.</summary>
    public const int LastDay = 1080;

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
        // Humidity has its own stream, two draws a day (one normal), so the main stream (and the golden hash of the series) never moves.
        var humRng = new SeededRandom(seed * 9187 + 211);
        var humidity = climate.HumidityRule;
        var seaRule = climate.SeaRule;
        var fogRule = climate.FogRule;
        double humAnomaly = 0, seaAnomalyAverage = 0;
        double persistence = Math.Clamp(humidity.Persistence, 0, 0.999);
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

            double roundedTemp = Math.Round(temp, 1), roundedNight = Math.Round(night, 1);

            // Humidity: a unit AR(1) walk scaled to the month's band, held in the band, raised on a wet day (and never past 100).
            humAnomaly = persistence * humAnomaly + Math.Sqrt(1 - persistence * persistence) * Normal(humRng);
            double humMid = (c.HumidityLo + c.HumidityHi) / 2, humHalf = (c.HumidityHi - c.HumidityLo) / 2;
            double hum = Math.Clamp(humMid + humAnomaly * humHalf, c.HumidityLo, c.HumidityHi);
            if (wet) hum += humidity.WetAddPercent;
            hum = Math.Round(Math.Min(100, hum), 1);

            // The dew point is the air mass's: the Magnus value at the day's mean air temperature, constant through the day.
            double dew = Math.Round(DewPoint((roundedTemp + roundedNight) / 2, hum), 1);

            // The sea: the month's curve plus a slow average of the air's anomaly, clamped to the season's range. No draws.
            seaAnomalyAverage += (tempAnomaly - seaAnomalyAverage) / Math.Max(1, seaRule.AnomalyAverageDays);
            double sea = Math.Round(c.SeaTemp + SeaAnomaly(seaAnomalyAverage, seaRule), 1);

            var weather = new WeatherDay(day, force, Directions[dir], rain, roundedTemp, roundedNight, Math.Round(c.Daylight, 2), 0,
                hum, dew, sea);
            days[day] = ApplyFog(weather, fogRule);
        }
        return new WeatherSeries(days);
    }

    private readonly record struct Curve(
        double TempLo, double TempHi, double NightDrop, double RainChance, double RainAfterRain,
        double RainMean, double WindMean, double GaleChance, double Daylight, double HumidityLo, double HumidityHi, double SeaTemp);

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
            L(a.RainMeanMm, b.RainMeanMm), L(a.WindMeanForce, b.WindMeanForce), L(a.GaleChance, b.GaleChance),
            L(a.DaylightHours, b.DaylightHours), L(Hum(a, 0), Hum(b, 0)), L(Hum(a, 1), Hum(b, 1)), L(a.SeaTempC, b.SeaTempC));
    }

    private static double Hum(MonthClimate m, int i) => m.Humidity is { Count: 2 } h ? h[i] : (i == 0 ? 60 : 80);

    /// <summary>The dew point in degrees C by the Magnus formula (a = 17.62, b = 243.12 C) from the air temperature and the relative humidity in percent.</summary>
    public static double DewPoint(double airC, double humidityPercent)
    {
        const double a = 17.62, b = 243.12;
        double gamma = Math.Log(Math.Max(humidityPercent, 1) / 100.0) + a * airC / (b + airC);
        return b * gamma / (a - gamma);
    }

    /// <summary>The sea's departure from the month's curve: gain times the slow average of the air's anomaly, clamped to plus or minus the clamp.</summary>
    public static double SeaAnomaly(double airAnomalyAverageC, SeaTempRules rule) =>
        Math.Clamp(rule.AnomalyGain * airAnomalyAverageC, -rule.AnomalyClampC, rule.AnomalyClampC);

    /// <summary>
    /// The day's fog from its numbers. Sea (advection) fog: the dew point at or above the sea temperature less the margin and a wind of at most
    /// the fog's force: fog from the start of the day, lifting at the first hour from dawn when the hour's air temperature is more than the lift
    /// above the dew point (24 when none is; if the night minimum is already that warm it never forms). Radiation fog in the harbour: a dry day
    /// with wind at most the radiation force and a night minimum at or below the dew point, until the clear-by hour. Sea fog wins.
    /// </summary>
    public static WeatherDay ApplyFog(WeatherDay d, FogRules rule)
    {
        if (d.WindForce <= rule.MaxWindForce && d.DewPointC >= d.SeaTempC - rule.MarginC)
        {
            int lift = 24;
            for (int h = rule.DawnHour; h < 24; h++)
                if (d.AirTempAt(h) > d.DewPointC + rule.LiftAboveDewC) { lift = h; break; }
            if (lift > rule.DawnHour) return d with { FogStartHour = 0, FogUntilHour = lift, Kind = FogKind.Sea };
        }
        if (d.RainMm <= 0 && d.WindForce <= rule.RadiationMaxWindForce && d.NightC <= d.DewPointC)
            return d with { FogStartHour = 0, FogUntilHour = rule.RadiationClearByHour, Kind = FogKind.Harbour };
        return d;
    }

    /// <summary>Standard normal via Box-Muller.</summary>
    private static double Normal(IRandom rng)
    {
        var u1 = 1 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
