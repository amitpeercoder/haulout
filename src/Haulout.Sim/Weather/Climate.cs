using System.Reflection;
using System.Text.Json;

namespace Haulout.Sim.Weather;

/// <summary>One month's curves. All values are placeholders for Amit.</summary>
public sealed record MonthClimate(
    IReadOnlyList<double> TempC,
    double NightDropC,
    double RainChance,
    double RainAfterRainChance,
    double RainMeanMm,
    double WindMeanForce,
    double GaleChance,
    IReadOnlyList<string> Prevailing,
    double DaylightHours = 12,
    double FogChance = 0);

/// <summary>Twelve monthly curves, index 0 = January.</summary>
public sealed record Climate(
    string Placeholders, IReadOnlyList<MonthClimate> Months, double FogAllDayFraction = 0.25, IReadOnlyList<int>? FogLiftsHour = null)
{
    /// <summary>The whole hours the morning fog may lift at, lo and hi (design note 010).</summary>
    public (int Lo, int Hi) FogLifts => FogLiftsHour is { Count: >= 2 } l ? (l[0], Math.Max(l[0], l[1])) : (9, 13);

    private const string ResourceName = "Haulout.Sim.Data.weather.json";
    private static readonly Lazy<Climate> Embedded = new(() => Load(ReadEmbedded()));

    public static Climate Default => Embedded.Value;

    public static Climate Load(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var climate = JsonSerializer.Deserialize<Climate>(json, options)
            ?? throw new InvalidDataException("weather.json is empty.");
        if (climate.Months.Count != 12)
            throw new InvalidDataException($"weather.json needs 12 months, found {climate.Months.Count}.");
        foreach (var m in climate.Months)
        {
            if (m.TempC.Count != 2) throw new InvalidDataException("tempC must be [lo, hi].");
            if (m.Prevailing.Count == 0) throw new InvalidDataException("prevailing must not be empty.");
        }
        return climate;
    }

    private static string ReadEmbedded()
    {
        using var stream = typeof(Climate).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
