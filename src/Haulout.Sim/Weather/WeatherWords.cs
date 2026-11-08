using System.Globalization;

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

    public static string Describe(WeatherDay d)
    {
        var sky = d.RainMm <= 0
            ? (d.WindForce <= 3 && d.TempC >= 8 ? "Bright" : "Dry")
            : "Grey";
        var parts = new List<string>
        {
            sky,
            Temperature(d),
        };
        if (d.RainMm > 0) parts.Add(RainWord(d));
        parts.Add(WindPhrase(d));
        if (d.NightC < 0) parts.Add("frost overnight");
        return string.Join(", ", parts) + ".";
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
