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
