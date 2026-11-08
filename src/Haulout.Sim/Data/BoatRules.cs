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
    double ManualPumpLitresPerMinute, int ManualPumpMinutes, string SoleText, IReadOnlyList<LevelWord> LevelWords,
    double HarbourmasterLitres = 120, string HarbourmasterText = WaterDefaults.HarbourmasterText)
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

/// <summary>A place she can lie. Yard: the yard (repairs, survey, shore power, the crane) is here. ShopName: where Buy happens; Stock and PriceFactor: what it sells and at what markup.</summary>
public sealed record Port
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public bool Yard { get; init; }
    public string ShopName { get; init; } = "the chandlery";
    /// <summary>The chandlery ids the shop here sells; null: all of them. PLACEHOLDER stock for Amit.</summary>
    public IReadOnlyList<string>? Stock { get; init; }
    /// <summary>What the shop here charges against the chandlery price (1.3 = 30 % markup). PLACEHOLDER.</summary>
    public double PriceFactor { get; init; } = 1.0;

    /// <summary>False: no shop at all (an anchorage). Nothing can be bought here and no spare can be had. PLACEHOLDER (design note 008).</summary>
    public bool Shop { get; init; } = true;
    /// <summary>Diesel is for sale here (design note 008, used by the winter tasks). PLACEHOLDER.</summary>
    public bool Fuel { get; init; }

    public bool Sells(ChandleryItem item) => Shop && (Stock is null || Stock.Contains(item.Id));
    public int PriceOf(ChandleryItem item) => (int)Math.Round(item.PriceNok * PriceFactor, MidpointRounding.AwayFromZero);
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

/// <summary>A word band: the first band whose Under exceeds the value wins; a null Under is anything above the rest.</summary>
public sealed record Band(double? Under, string Text)
{
    /// <summary>The text of the band the value falls in; "" when there are no bands.</summary>
    public static string Pick(IReadOnlyList<Band> bands, double value)
    {
        foreach (var b in bands)
            if (b.Under is { } u && value < u) return b.Text;
        return bands.Count == 0 ? "" : bands[^1].Text;
    }
}

/// <summary>
/// The yard's calendar and prices (design note 008). Days of year are 0-based within a 360-day year. All PLACEHOLDERS for Amit;
/// the haulout season is UNVERIFIED.
/// </summary>
public sealed record YardRules
{
    public int WindowFromDayOfYear { get; init; } = 255;
    public int WindowToDayOfYear { get; init; } = 300;
    /// <summary>From this day of year every week of waiting pushes the first free slot later by a seeded 0 to DriftMaxDaysPerWeek days.</summary>
    public int DriftFromDayOfYear { get; init; } = 240;
    public int DriftMaxDaysPerWeek { get; init; } = 7;
    public int MinLeadDays { get; init; } = 1;
    public int LiftNok { get; init; } = 7000;
    public int StorageNok { get; init; } = 9000;
    public int ChangeFeeNok { get; init; } = 500;
    public int WashNok { get; init; } = 1200;
    public int WashMinutes { get; init; } = 120;
    public int HauloutMinutes { get; init; } = 120;
    /// <summary>A lift on or after this day of year is the autumn lift: storage is billed and she is laid up.</summary>
    public int AutumnFromDayOfYear { get; init; } = 240;

    public static YardRules Default { get; } = new();
}

/// <summary>
/// Growth on the hull below the waterline (design note 008). The growth rate and the knots lost are PLACEHOLDERS, UNVERIFIED against real boats.
/// </summary>
public sealed record FoulingRules
{
    public double BaseGrowthPerDay { get; init; } = 0.008;
    public int WaterLagDays { get; init; } = 20;
    public double GrowthFromC { get; init; } = 6;
    public double FullGrowthC { get; init; } = 15;
    public double PaintProtection { get; init; } = 0.9;
    public IReadOnlyList<double> Spread { get; init; } = [0.6, 1.6];
    public double MaxKnotsLost { get; init; } = 1.2;
    public double LoadPerGrowth { get; init; } = 0.5;
    public int CleanMinutes { get; init; } = 240;
    public int ScrubMinutes { get; init; } = 120;
    public double ScrubFraction { get; init; } = 0.4;
    public double PaintNeedsCleanBelow { get; init; } = 0.1;
    public double ShowFrom { get; init; } = 0.05;
    /// <summary>Antifouling in a condition under this reads as thin paint in the lift's cause line.</summary>
    public double PaintThinBelow { get; init; } = 0.5;
    /// <summary>A look at the waterline compares with the last look when the words are the same but the growth moved by more than this.</summary>
    public double CompareStep { get; init; } = 0.04;
    public IReadOnlyList<Band> WaterlineWords { get; init; } = [];
    public IReadOnlyList<Band> HullWords { get; init; } = [];

    public static FoulingRules Default { get; } = new();
}

/// <summary>Chafe on the mooring lines on a gale day afloat in port, and what happens when they part (design note 008). PLACEHOLDERS, UNVERIFIED.</summary>
public sealed record MooringRules
{
    public double StartChafe { get; init; } = 0.2;
    public int FromForce { get; init; } = 7;
    /// <summary>Index 0 is force FromForce; stronger gales than the list use its last entry.</summary>
    public IReadOnlyList<double> ChafeByForce { get; init; } = [0.25, 0.45, 0.7];
    public IReadOnlyList<double> Spread { get; init; } = [0.6, 1.6];
    public double PartAt { get; init; } = 1.0;
    public double WarnFrom { get; init; } = 0.5;
    public int PartedRepairNok { get; init; } = 4000;
    public IReadOnlyList<Band> ChafeWords { get; init; } = [];

    public static MooringRules Default { get; } = new();
}

/// <summary>
/// The winter layup on the hard (design note 008, step 2): the tasks' minutes and prices, and the rates of the four winter accumulators.
/// All PLACEHOLDERS for Amit; the domain claims are UNVERIFIED.
/// </summary>
public sealed record LayupRules
{
    /// <summary>From this day of the year to the yard's autumn day it is the season: waiting for spring is refused unless she is laid up.</summary>
    public int SeasonFromDayOfYear { get; init; } = 60;
    public int WinteriseMinutes { get; init; } = 90;
    public int AntifreezeNok { get; init; } = 250;
    public int FillMinutes { get; init; } = 30;
    public int DieselNokPerLitre { get; init; } = 22;
    /// <summary>A tank at or above this fraction of its capacity breathes no damp air worth the name.</summary>
    public double FullAt { get; init; } = 0.9;
    public int BatteryMinutes { get; init; } = 30;
    public int BatteryRoomNok { get; init; } = 400;
    public int ChargerMinutes { get; init; } = 20;
    public int ChargerNok { get; init; } = 600;
    public int CoverMinutes { get; init; } = 120;
    public int CoverNok { get; init; } = 2500;
    /// <summary>What a battery left aboard on the hard loses in a day on top of its self-discharge.</summary>
    public double DrawPerDay { get; init; } = 0.006;
    public double FrostHoursPerDegree { get; init; } = 3;
    public double MaxFrostHoursPerNight { get; init; } = 14;
    public double CondensationBelowNightC { get; init; } = 5;
    public double FreezeBelowCharge { get; init; } = 0.25;
    public double SnowBelowC { get; init; } = 1;
    public int SpringDayOfYear { get; init; } = 75;

    public static LayupRules Default { get; } = new();
}

/// <summary>
/// The sale (design note 008, step 3): the valuation, the broker, the buyers. All numbers are PLACEHOLDERS for Amit; the valuation
/// practice itself (what a buyer deducts, how a logbook and the season move a price) is UNVERIFIED against real brokers.
/// </summary>
public sealed record SaleRules
{
    public int BaseNok { get; init; } = 180000;
    /// <summary>Years of engine hours nobody wrote down, before the player owned her.</summary>
    public int UndocumentedYearsAtPurchase { get; init; } = 4;
    public int PerUndocumentedYearNok { get; init; } = 3000;
    public int PerEntryNok { get; init; } = 600;
    public int EntriesCap { get; init; } = 12;
    /// <summary>Added in March to June.</summary>
    public double SpringBonus { get; init; } = 0.04;
    /// <summary>Taken off from September to February.</summary>
    public double AutumnDiscount { get; init; } = 0.04;
    public double FloorFraction { get; init; } = 0.3;
    /// <summary>Growth on the hull from which a buyer's surveyor counts the wash as a finding.</summary>
    public double FoulingFindFrom { get; init; } = 0.2;
    public double FindChance { get; init; } = 0.85;
    public double ViewingChancePerDay { get; init; } = 0.2;
    public double AskSensitivity { get; init; } = 3;
    public IReadOnlyDictionary<string, double> SeasonViewingFactor { get; init; } =
        new Dictionary<string, double> { ["spring"] = 1.3, ["summer"] = 1.0, ["autumn"] = 0.6, ["winter"] = 0.6 };
    public IReadOnlyList<double> OfferSpread { get; init; } = [0.88, 1.02];
    public int OfferHoldsDays { get; init; } = 7;
    public int ListMinutes { get; init; } = 30;
    public string Broker { get; init; } = "Holm & Datter, yacht brokers";
    /// <summary>Replacing one of these (or winterising the engine) is an engine service entry in the logbook.</summary>
    public IReadOnlyList<string> ServiceComponents { get; init; } =
        ["engine-oil", "oil-filter", "impeller", "primary-filter", "secondary-filter", "alternator-belt"];

    public static SaleRules Default { get; } = new();
}

/// <summary>Text the water rules fall back to when the boat's data says none.</summary>
public static class WaterDefaults
{
    public const string HarbourmasterText = "The harbourmaster rang: she's sitting low on her mooring. Worth getting down there.";
}
