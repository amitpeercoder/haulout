using System.Globalization;
using Haulout.Sim.Data;

namespace Haulout.Sim.Weather;

/// <summary>Weather in the game's voice. Pure.</summary>
public static class WeatherWords
{
    public const double RainThresholdMm = 0.5;

    private static readonly string[] Beaufort =
    [
        "calm", "light air", "light breeze", "gentle breeze", "moderate breeze",
        "fresh breeze", "strong breeze", "near gale", "gale", "strong gale",
    ];

    private static readonly string[] Numbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty",
    ];

    public static bool IsRaining(WeatherDay d) => d.RainMm >= RainThresholdMm;

    public static bool IsGale(WeatherDay d, int atLeastForce = 7) => d.WindForce >= atLeastForce;

    /// <summary>Fog lies on the water or in the harbour at this hour of the day (0 to 24, fractional): it has started and not yet lifted.</summary>
    public static bool IsFoggy(WeatherDay d, double hourOfDay) => hourOfDay >= d.FogStartHour && hourOfDay < d.FogUntilHour;

    /// <summary>
    /// Fog that counts for a boat at this hour, <paramref name="minutesOut"/> after she cast off (null: she is in port). Sea fog counts at sea;
    /// harbour fog (radiation fog) lies in port only, so under way it counts in the first hour of a passage and no later.
    /// </summary>
    public static bool IsFoggyUnderway(WeatherDay d, double hourOfDay, double minutesOut) =>
        IsFoggy(d, hourOfDay) && (!d.HarbourOnly || minutesOut < 60);

    /// <summary>The fog of the day in words, for a forecast: "morning fog", "fog all day" or "harbour mist"; empty when there is none.</summary>
    public static string FogWord(WeatherDay d) =>
        d.FogUntilHour <= d.FogStartHour ? "" : d.HarbourOnly ? "harbour mist" : d.FogUntilHour >= 24 ? "fog all day" : "morning fog";

    /// <summary>
    /// What a sailor reads off the dew point and the sea: "Dew point 11, sea 10: fog likely in the morning." (the band of dew point minus sea
    /// temperature is in data; a wind above the fog's limit keeps it off), then, on a still dry night that will bring it, the harbour's mist.
    /// Empty for a day without the numbers.
    /// </summary>
    public static string SkyLine(WeatherDay d, FogRules? rules = null, bool numbersOnly = false)
    {
        if (!d.HasSky) return "";
        var fog = rules ?? Climate.Default.FogRule;
        string numbers = $"Dew point {Round(d.DewPointC)}, sea {Round(d.SeaTempC)}";
        if (numbersOnly) return numbers + ".";
        if (d.HarbourOnly) return $"{numbers}: {fog.Words.Harbour}.";
        string verdict = Band.Pick(fog.Words.Bands, d.DewPointC - d.SeaTempC);
        if (d.DewPointC - d.SeaTempC >= -fog.MarginC && d.WindForce > fog.MaxWindForce) verdict = fog.Words.WindKeepsOff;
        return $"{numbers}: {verdict}.";
    }

    private static string Round(double c) => (Math.Round(c, MidpointRounding.AwayFromZero) + 0.0).ToString("0", CultureInfo.InvariantCulture); // + 0.0: no "-0"

    /// <summary>Is the hour dark? More than half the day's length from solar noon (a day of 24 hours is never dark).</summary>
    public static bool IsDark(WeatherDay d, double hourOfDay, double solarNoonHour) =>
        d.DaylightHours < 24 && Math.Abs(hourOfDay - solarNoonHour) > d.DaylightHours / 2;

    /// <param name="hourOfDay">The hour it is now, when it matters: the fog is told only while it has not lifted. Null: the day alone, no fog line.</param>
    /// <param name="minutesOut">Minutes since she cast off when she is under way (null: in port): harbour fog is told only in port and in the first hour out.</param>
    /// <param name="withSky">Add what the dew point and the sea say about fog (the weather report and the forecast do; the log's header and the chart do not).
    /// With an hour: while fog lies, the numbers and the fog; once it has lifted, the numbers alone; before, the prediction.</param>
    public static string Describe(WeatherDay d, double? hourOfDay = null, bool withSky = false, double? minutesOut = null)
    {
        var skyWord = d.RainMm <= 0
            ? (d.WindForce <= 3 && d.TempC >= 8 ? "Bright" : "Dry")
            : "Grey";
        var parts = new List<string>
        {
            skyWord,
            Temperature(d),
        };
        if (d.RainMm > 0) parts.Add(RainWord(d));
        parts.Add(WindPhrase(d));
        if (d.NightC < 0) parts.Add("frost overnight");
        var text = string.Join(", ", parts) + ".";
        bool foggyNow = hourOfDay is { } now && (minutesOut is { } m ? IsFoggyUnderway(d, now, m) : IsFoggy(d, now));
        if (withSky && d.HasSky)
        {
            bool lifted = hourOfDay is { } hh && d.Kind != FogKind.None && (hh >= d.FogUntilHour || (d.HarbourOnly && minutesOut >= 60));
            text += " " + SkyLine(d, null, numbersOnly: foggyNow || lifted);
        }
        if (!foggyNow) return text;
        var words = Climate.Default.FogRule.Words;
        return text + " " + (d.HarbourOnly ? words.OverHarbour : words.OverWater);
    }

    /// <summary>"drizzle", "rain on and off" or "heavy rain"; empty when it is dry.</summary>
    public static string RainWord(WeatherDay d) =>
        d.RainMm <= 0 ? "" : d.RainMm < 2 ? "drizzle" : d.RainMm < 8 ? "rain on and off" : "heavy rain";

    /// <summary>The wind in words: "a near gale from the west", or "no wind".</summary>
    public static string WindPhrase(WeatherDay d)
    {
        var force = Math.Clamp(d.WindForce, 0, 9);
        return force == 0 ? "no wind" : $"{Article(Beaufort[force])} {Beaufort[force]} from the {d.WindFrom}";
    }

    /// <summary>"6 °C" (rounded, invariant).</summary>
    public static string Temperature(WeatherDay d) =>
        $"{Math.Round(d.TempC, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)} °C";

    private static string Article(string word) => word is "ice" ? "an" : word.StartsWith('a') || word.StartsWith('e') ? "an" : "a";

    private enum Kind { Rain, Dry, Gale }

    public static string Summarise(IReadOnlyList<WeatherDay> days)
    {
        if (days.Count == 0) return "";
        var runs = new List<(Kind Kind, int Length)>();
        foreach (var d in days)
        {
            var kind = IsGale(d, 8) ? Kind.Gale : IsRaining(d) ? Kind.Rain : Kind.Dry;
            if (runs.Count > 0 && runs[^1].Kind == kind) runs[^1] = (kind, runs[^1].Length + 1);
            else runs.Add((kind, 1));
        }

        if (runs.Count > 4)
        {
            int Count(Kind k) => runs.Where(r => r.Kind == k).Sum(r => r.Length);
            var text = $"A mixed week: {Number(Count(Kind.Rain))} {Plural(Count(Kind.Rain), "day")} of rain, {Number(Count(Kind.Dry))} dry";
            if (Count(Kind.Gale) > 0) text += $", {Number(Count(Kind.Gale))} of gale";
            return text + ".";
        }

        var phrases = runs.Select((r, i) => Phrase(r.Kind, r.Length, first: i == 0)).ToList();
        var sentence = string.Join(", then ", phrases);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..] + ".";
    }

    private static string Phrase(Kind kind, int n, bool first) => kind switch
    {
        Kind.Rain => n == 1 ? "a day of rain" : $"{Number(n)} days of rain",
        Kind.Dry => n == 1 ? "a dry day" : first ? $"{Number(n)} dry days" : $"{Number(n)} dry ones",
        _ => n == 1 ? "a gale" : $"{Number(n)} days of gale",
    };

    private static string Plural(int n, string word) => n == 1 ? word : word + "s";

    private static string Number(int n) => n >= 0 && n < Numbers.Length ? Numbers[n] : n.ToString(CultureInfo.InvariantCulture);
}
