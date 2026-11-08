using Haulout.Sim.Faults;

namespace Haulout.Sim.Data;

/// <summary>The money and the calendar of the spring. All placeholders from the boat's data.</summary>
public sealed record Economy(int StartingBalanceNok, int LaunchDeadlineDay, int EmergencyLiftNok, int LaunchMinutes, int EmergencyLiftDays = 0);

public enum SurveySeverity { Note, Attention, BeforeLaunch }

/// <summary>Cause: the surveyor names the fault. Investigate: the surveyor only points at the part. None: nothing wrong to find.</summary>
public enum SurveyFinding { Cause, Investigate, None }

/// <summary>Component and Fault are ids. Fault null: no fault behind the line.</summary>
public sealed record SurveyItemData(string Component, SurveySeverity Severity, string? Fault, SurveyFinding Finding, string Text);

public sealed record SurveyData(
    int NokPerFoot, int LengthFeet, int Days, string Surveyor,
    IReadOnlyList<string> Opening, IReadOnlyList<string> Closing, IReadOnlyList<SurveyItemData> Items)
{
    public int FeeNok => NokPerFoot * LengthFeet;
}

/// <summary>What poor DIY work turns into, per subsystem.</summary>
public sealed record PoorJobTemplate(
    string Name, string Reveal, string Consequence, int CostLaterNok, DiyRoute Diy, YardRoute Yard);

public sealed record DiyQuality(
    int StartSkill, int Threshold, double PoorJobChance, IReadOnlyDictionary<Components.Subsystem, PoorJobTemplate> PoorJobs);

/// <summary>Weather and cover exposure weights for the progression rate. PLACEHOLDERS for Amit.</summary>
public sealed record ExposureRates(double RainWeight, double RainCapMm, double FrostWeight, double PerOpenCover);

public sealed record UseRates(double PerEngineHour);

/// <summary>Covers: ids of covers that count as "left off" when the player is away.</summary>
public sealed record NeglectRates(
    double PerKnownOpenFault, double BatteryLow, double PerCoverLeftOff, double PerDayAwayBeyond3, IReadOnlyList<string> Covers);

/// <summary>When the weather says no: WindForce from which exposed places and the launch are refused; RainMm from which a needs-dry job is refused. PLACEHOLDERS.</summary>
public sealed record WeatherGates(int WindForce, double RainMm);

/// <summary>
/// The numbers behind every fault's progression rate (design note 006). The spread multiplies a fault's base rate,
/// drawn once per fault as lo + r * (hi - lo). The default (no weights, spread 0.6 to 1.6) keeps hand-built boats working.
/// </summary>
public sealed record Rates(
    double Threshold, double SpreadLo, double SpreadHi, ExposureRates Exposure, UseRates Use, NeglectRates Neglect,
    WeatherGates? Gates = null)
{
    public static Rates Default { get; } = new(
        1.0, 0.6, 1.6, new ExposureRates(0, 1, 0, 0), new UseRates(0), new NeglectRates(0, 0, 0, 0, []));

    public WeatherGates WeatherGates => Gates ?? new WeatherGates(7, 0.5);
}

/// <summary>A level word: the bilge holds less than UnderLitres (null: anything more). PLACEHOLDERS.</summary>
public sealed record LevelWord(double? UnderLitres, string Text);

/// <summary>
/// The water model's numbers (design note 006). SoleLitres: above it the interior is soaked. SinkLitres: afloat, she sinks.
/// The manual pump moves ManualPumpLitresPerMinute for ManualPumpMinutes. All PLACEHOLDERS for Amit.
/// </summary>
public sealed record WaterRules(
    double StartLitres, double SoleLitres, double SinkLitres, double RainThroughOpenHatchLitresPerMm,
    double ManualPumpLitresPerMinute, int ManualPumpMinutes, string SoleText, IReadOnlyList<LevelWord> LevelWords)
{
    public static WaterRules Default { get; } = new(0, 120, 1500, 0.5, 10, 15, "Water stands over the sole.",
    [
        new(1, "dry, a damp sheen"), new(5, "a finger of water"), new(30, "a few inches, sloshing"),
        new(120, "deep, up to the floorboards"), new(null, "over the sole"),
    ]);

    /// <summary>The bilge in words, from the bands.</summary>
    public string Words(double litres) =>
        (LevelWords.FirstOrDefault(w => w.UnderLitres is { } u && litres < u) ?? LevelWords[^1]).Text;
}

/// <summary>
/// SelfDischargePerDay: the share of a full charge a battery loses a day. PumpMinStateOfCharge: below it the bilge pump cannot run.
/// CrankDraw: the share a start attempt takes. ChargePerHour: what the alternator puts back per hour of running at its full
/// chargeFactor. RunDrawPerHour: what the running boat takes per hour. All PLACEHOLDERS.
/// </summary>
public sealed record ElectricalRules(
    double SelfDischargePerDay, double PumpMinStateOfCharge, double CrankDraw = 0.01, double ChargePerHour = 0.10, double RunDrawPerHour = 0.03)
{
    public static ElectricalRules Default { get; } = new(0.002, 0.15);
}

/// <summary>What the insurer pays for a write-off: PayoutFraction of ValueNok, nothing if voided. PLACEHOLDERS.</summary>
public sealed record InsuranceRules(int ValueNok, double PayoutFraction)
{
    public static InsuranceRules Default { get; } = new(180000, 0.6);
    public int PayoutNok => (int)Math.Round(ValueNok * PayoutFraction);
}

/// <summary>
/// One thing the chandlery sells. Tool: bought once and kept (the remote start button). Otherwise a spare that stacks.
/// ForComponent: the component id a spare is used to replace. Fuses and hose have none (no component takes them yet).
/// </summary>
public sealed record ChandleryItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public int PriceNok { get; init; }
    public int Minutes { get; init; } = 15;
    public bool Tool { get; init; }
    public string? ForComponent { get; init; }
    public string? LogText { get; init; }
}

/// <summary>A place she can lie. Yard: the yard (repairs, survey, shore power, the crane) is here. ShopName: where Buy happens.</summary>
public sealed record Port
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public bool Yard { get; init; }
    public string ShopName { get; init; } = "the chandlery";
    public IReadOnlyList<string> ArrivalLines { get; init; } = [];
}

/// <summary>A place to get out of the weather, AtNm along the route from its From port.</summary>
public sealed record Shelter
{
    public string Name { get; init; } = "";
    public double AtNm { get; init; }
}

/// <summary>A passage from one port to another. The way back is the same route reversed (see <see cref="Reversed"/>).</summary>
public sealed record Route
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public double DistanceNm { get; init; }
    public int CourseDeg { get; init; }
    public IReadOnlyList<Shelter> Shelter { get; init; } = [];

    /// <summary>The way back: course + 180, shelters at distance - atNm (nearest the new start first).</summary>
    public Route Reversed() => this with
    {
        From = To, To = From, CourseDeg = (CourseDeg + 180) % 360,
        Shelter = Shelter.Select(s => new Shelter { Name = s.Name, AtNm = DistanceNm - s.AtNm }).OrderBy(s => s.AtNm).ToList(),
    };
}

/// <summary>What a tow costs: money, hours aboard on the way, hours lost on top. PLACEHOLDERS.</summary>
public sealed record TowRules
{
    public int Nok { get; init; } = 6000;
    public int Hours { get; init; } = 6;
    public int LostHours { get; init; } = 18;
}

/// <summary>The ports and routes of the slice. Home: the port id she starts at.</summary>
public sealed record TripRules(string Home, IReadOnlyList<Port> Ports, IReadOnlyList<Route> Routes, TowRules Tow)
{
    public Port? FindPort(string id) => Ports.FirstOrDefault(p => p.Id == id);

    /// <summary>Routes out of a port: the data routes from it, and each route to it, reversed.</summary>
    public IEnumerable<Route> RoutesFrom(string portId) =>
        Routes.Where(r => r.From == portId).Concat(Routes.Where(r => r.To == portId).Select(r => r.Reversed()));
}

/// <summary>Speed, sea state and the load of a passage (design note 007). All PLACEHOLDERS for Amit.</summary>
public sealed record SeaRules
{
    public IReadOnlyList<int> StateByForce { get; init; } = [0, 1, 1, 2, 3, 4, 5, 6, 7, 8];
    public IReadOnlyList<string> StateWords { get; init; } =
        ["flat calm", "a ripple", "small wavelets", "a short chop", "moderate waves", "rough water, whitecaps everywhere", "a rough sea", "a very rough sea", "a high sea"];
    public double MotorKnots { get; init; } = 5;
    public int SailMinForce { get; init; } = 3;
    public int SailMaxForce { get; init; } = 5;
    public IReadOnlyList<double> SailKnots { get; init; } = [0, 0, 0, 4.0, 5.0, 5.5];
    public int NoGoDeg { get; init; } = 45;
    public double HeaveToDriftKnots { get; init; } = 0.5;
    public double AnchorWithinNm { get; init; } = 1;
    public double TowKnots { get; init; } = 3;
    public double SlopPerSeaState { get; init; } = 0.5;
    public int SprayFromSeaState { get; init; } = 2;
    public double SprayLitresPerHourPerSeaState { get; init; } = 2;
    public int StirFromSeaState { get; init; } = 3;
    public int StirHoursToBite { get; init; } = 2;
    public double ImpellerShedPerLoadHour { get; init; } = 0.3;
    public int OverheatStopMinutes { get; init; } = 90;
    public double UnderWayDrawPerHour { get; init; } = 0.05;
    public int DepartMinutes { get; init; } = 10;
    public int HeaveToMinutes { get; init; } = 5;
    public int AnchorMinutes { get; init; } = 15;
    public int TurnBackMinutes { get; init; } = 2;

    public static SeaRules Default { get; } = new();
}

/// <summary>How far ahead the forecast sees and how often it is wrong, by lead in days (index 0 = today). PLACEHOLDERS.</summary>
public sealed record ForecastRules
{
    public int MaxLeadDays { get; init; } = 3;
    public IReadOnlyList<double> ForceErrorChance { get; init; } = [0, 0.3, 0.5, 0.7];
    public IReadOnlyList<double> RainFlipChance { get; init; } = [0, 0.1, 0.2, 0.3];

    public static ForecastRules Default { get; } = new();
}
