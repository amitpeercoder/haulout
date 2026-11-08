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
    IReadOnlyList<double>? Humidity = null,
    double SeaTempC = 8);

/// <summary>How the day's relative humidity walks (design note 010, second pass D). Placeholders.</summary>
public sealed record HumidityRules
{
    /// <summary>Day-to-day persistence of the AR(1) walk inside the month's band (0 to 1).</summary>
    public double Persistence { get; init; } = 0.8;
    /// <summary>Percent added on a wet day (the sum is held to 100).</summary>
    public double WetAddPercent { get; init; } = 8;
}

/// <summary>The sea-surface temperature on top of the month's curve: a slow average of the air's anomaly, with a gain, clamped. Placeholders.</summary>
public sealed record SeaTempRules
{
    public double AnomalyGain { get; init; } = 0.5;
    public double AnomalyClampC { get; init; } = 2.0;
    /// <summary>The length of the moving average of the air-temperature anomaly, in days (an exponential average with this time constant).</summary>
    public int AnomalyAverageDays { get; init; } = 30;
}

/// <summary>What the fog rule needs, and the words the report uses for it (design note 010, second pass D). All placeholders; UNVERIFIED.</summary>
public sealed record FogRules
{
    /// <summary>Sea fog forms when the dew point is at least the sea temperature minus this.</summary>
    public double MarginC { get; init; } = 1.0;
    /// <summary>The strongest wind force at which sea fog forms (and holds); the series has one force a day.</summary>
    public int MaxWindForce { get; init; } = 3;
    /// <summary>Sea fog lifts at the first hour from dawn when the hour's air temperature exceeds the dew point by more than this.</summary>
    public double LiftAboveDewC { get; init; } = 2.0;
    /// <summary>The hour the search for the lift starts at (about the coolest hour of the day: the night minimum is taken then).</summary>
    public int DawnHour { get; init; } = 5;
    /// <summary>Radiation fog in the harbour: a dry day with wind at most this force, the night minimum at or below the dew point.</summary>
    public int RadiationMaxWindForce { get; init; } = 1;
    /// <summary>The hour radiation fog is gone by.</summary>
    public int RadiationClearByHour { get; init; } = 10;
    public FogWords Words { get; init; } = new();
}

/// <summary>The weather report's fog words. Bands are on dew point minus sea temperature, in degrees.</summary>
public sealed record FogWords
{
    public IReadOnlyList<Haulout.Sim.Data.Band> Bands { get; init; } =
    [
        new(-3.0, "no fog in that"), new(-1.0, "fog unlikely"), new(null, "fog likely in the morning"),
    ];
    public string WindKeepsOff { get; init; } = "fog would form, but the wind will keep it off";
    public string Harbour { get; init; } = "no sea fog, but air near its dew point on a still night: mist in the harbour at dawn";
    public string HarbourPlan { get; init; } = "Mist in the harbour in the morning: without a plotter or radar you creep out at {knots} knots for the first hour.";
    public string OverWater { get; init; } = "Fog over the water.";
    public string OverHarbour { get; init; } = "Mist lies over the harbour.";
}

/// <summary>Twelve monthly curves, index 0 = January.</summary>
public sealed record Climate(
    string Placeholders, IReadOnlyList<MonthClimate> Months, HumidityRules? Humidity = null, SeaTempRules? Sea = null, FogRules? Fog = null)
{
    public HumidityRules HumidityRule => Humidity ?? new HumidityRules();
    public SeaTempRules SeaRule => Sea ?? new SeaTempRules();
    public FogRules FogRule => Fog ?? new FogRules();

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
            if (m.Humidity is { Count: not 2 }) throw new InvalidDataException("humidity must be [lo, hi].");
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
