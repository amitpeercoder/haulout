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
    /// <summary>false: the yard chandlery does not keep it on the shelf; it is ordered and arrives in days (design note 010).</summary>
    public bool Stocked { get; init; } = true;
    /// <summary>The id of the upgrade component this item fits (design note 010, step 3); null: an ordinary spare or tool.</summary>
    public string? Upgrade { get; init; }
}

/// <summary>A point on the chart: nautical miles east and north of the home port, which is (0, 0). PLACEHOLDER (design note 009).</summary>
public sealed record ChartPoint(double EastNm, double NorthNm);

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
    /// <summary>Where the port is on the chart (design note 009). Every port in a route needs one.</summary>
    public ChartPoint? Chart { get; init; }
    /// <summary>She lies to her own anchor here: no mooring, no harbour (design note 009). PLACEHOLDER.</summary>
    public bool Anchorage { get; init; }

    public bool Sells(ChandleryItem item) => Shop && (Stock is null || Stock.Contains(item.Id));
    public int PriceOf(ChandleryItem item) => (int)Math.Round(item.PriceNok * PriceFactor, MidpointRounding.AwayFromZero);
    public IReadOnlyList<string> ArrivalLines { get; init; } = [];
    /// <summary>The arrival lines of an anchorage when the anchor chain is seized (design note 010): she lies to a line ashore instead. Null: the same lines.</summary>
    public IReadOnlyList<string>? ArrivalLinesAnchorJammed { get; init; }
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
    public int Nok { get; init; }
    public int Hours { get; init; } = 6;
    public int LostHours { get; init; } = 18;
}

/// <summary>The ports and routes of the slice. Home: the port id she starts at.</summary>
public sealed record TripRules(string Home, IReadOnlyList<Port> Ports, IReadOnlyList<Route> Routes, TowRules Tow)
{
    public Port? FindPort(string id) => Ports.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// The point <paramref name="nm"/> along the straight line from the route's From port to its To port (clamped to the route). The only
    /// geometry in the sim (design note 009). Both ports need a chart position (the loader checks).
    /// </summary>
    public ChartPoint PointAlong(Route route, double nm)
    {
        var a = FindPort(route.From)?.Chart ?? new ChartPoint(0, 0);
        var b = FindPort(route.To)?.Chart ?? a;
        if (route.DistanceNm <= 0) return a;
        double f = Math.Clamp(nm, 0, route.DistanceNm) / route.DistanceNm; // the rounded distance, so the end is exactly the To port
        return new ChartPoint(a.EastNm + (b.EastNm - a.EastNm) * f, a.NorthNm + (b.NorthNm - a.NorthNm) * f);
    }

    /// <summary>Routes out of a port: the data routes from it, and each route to it, reversed.</summary>
    public IEnumerable<Route> RoutesFrom(string portId) =>
        Routes.Where(r => r.From == portId).Concat(Routes.Where(r => r.To == portId).Select(r => r.Reversed()));

    /// <summary>
    /// The legs of the shortest way (by nautical miles) from one port to another through the routes of the data, each leg as sailed from its own
    /// start. Empty when there is none (or the ports are the same). Ties go to the port listed first, so the answer never changes from run to run.
    /// </summary>
    public IReadOnlyList<Route> PathBetween(string fromPortId, string toPortId)
    {
        if (fromPortId == toPortId) return [];
        var best = new Dictionary<string, double> { [fromPortId] = 0 };
        var via = new Dictionary<string, Route>();
        var open = new List<string> { fromPortId };
        var done = new HashSet<string>();
        while (open.Count > 0)
        {
            string at = open.OrderBy(id => best[id]).ThenBy(id => Ports.ToList().FindIndex(p => p.Id == id)).First();
            open.Remove(at);
            if (!done.Add(at)) continue;
            if (at == toPortId) break;
            foreach (var leg in RoutesFrom(at))
            {
                double nm = best[at] + leg.DistanceNm;
                if (done.Contains(leg.To) || best.TryGetValue(leg.To, out var known) && known <= nm) continue;
                best[leg.To] = nm;
                via[leg.To] = leg;
                if (!open.Contains(leg.To)) open.Add(leg.To);
            }
        }
        if (!via.ContainsKey(toPortId)) return [];
        var legs = new List<Route>();
        for (string id = toPortId; id != fromPortId; id = via[id].From) legs.Insert(0, via[id]);
        return legs;
    }
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
    /// <summary>
    /// Wear (1 minus condition) at which the impeller's vanes show a set to the eye with the pump cover off, and at which the flow at the exhaust
    /// reads weaker; both before it sheds (design note 010, second pass I). A boat that names no threshold shows no wear symptom.
    /// </summary>
    public double ImpellerSetFromWear { get; init; } = double.MaxValue;
    public double ImpellerWeakFlowFromWear { get; init; } = double.MaxValue;
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
    public int LiftNok { get; init; }
    public int StorageNok { get; init; }
    public int ChangeFeeNok { get; init; }
    public int WashNok { get; init; }
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
    public int PartedRepairNok { get; init; }
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
    public int DieselNokPerLitre { get; init; }
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

/// <summary>A standing cost that comes due on the calendar whether or not the player is there (design note 010).</summary>
public sealed record BillRule
{
    public string Id { get; init; } = "";
    /// <summary>"Berth and mooring at Skjærvik, the quarter": the invoice in words, told with the amount.</summary>
    public string Text { get; init; } = "";
    /// <summary>How the week line names it: "the berth invoice".</summary>
    public string Short { get; init; } = "";
    public int Nok { get; init; }
    /// <summary>Falls due at 00:00 on every absolute day from <see cref="FromDay"/> whose day of year is in this list.</summary>
    public IReadOnlyList<int> DaysOfYear { get; init; } = [];
    public int FromDay { get; init; }
}

/// <summary>
/// The week and the money (design note 010, step 1). The defaults are off where they move money or time: no pay, no bills, no vacation
/// limit, no overdraft, so a boat built without data behaves as before. All numbers in the shipped data are PLACEHOLDERS for Amit.
/// </summary>
public sealed record CalendarRules
{
    /// <summary>Aboard on a weekday up to this many minutes is an evening after work, free. int.MaxValue: the days are never charged.</summary>
    public int EveningMinutes { get; init; } = int.MaxValue;
    public int VacationDaysPerYear { get; init; } = int.MaxValue;
    public int UnpaidLeaveNok { get; init; }
    /// <summary>Lands at 00:00 on the first day of every month (day of year divisible by 30).</summary>
    public int PayNok { get; init; }
    public int OverdraftLimitNok { get; init; }
    public double InterestPerMonth { get; init; }
    public IReadOnlyList<BillRule> Bills { get; init; } = [];
    /// <summary>How far ahead the Calendar query looks, in days.</summary>
    public int AheadDays { get; init; } = 42;
    /// <summary>The calendar of wants (design note 010, step 2). Default: none.</summary>
    public IReadOnlyList<EventRule> Events { get; init; } = [];

    /// <summary>Weekdays aboard are charged (a vacation day or unpaid leave).</summary>
    public bool ChargesDays => VacationDaysPerYear < int.MaxValue;

    public static CalendarRules Default { get; } = new();
}

/// <summary>Parts that are not on the chandlery's shelf are ordered (design note 010). Lead time in whole days, inclusive. Default: 0 to 0, nothing is ever out of stock.</summary>
public sealed record OrderRules
{
    public IReadOnlyList<int> LeadDays { get; init; } = [0, 0];
    /// <summary>The port id the yard chandlery delivers to.</summary>
    public string DeliverTo { get; init; } = "";
    public int PhoneMinutes { get; init; } = 5;

    public int LeadLo => LeadDays.Count >= 2 ? LeadDays[0] : 0;
    public int LeadHi => LeadDays.Count >= 2 ? Math.Max(LeadDays[0], LeadDays[1]) : 0;
    /// <summary>Orders exist at all: a part flagged not stocked is only out of stock when they do.</summary>
    public bool Enabled => LeadHi > 0;

    public static OrderRules Default { get; } = new();
}

/// <summary>
/// Fuel (design note 010, step 2). The default burns nothing, so a boat built without data runs as before. All numbers in the shipped data
/// are PLACEHOLDERS for Amit; the burn is UNVERIFIED against a real 20 hp diesel (design note 010, second pass C).
/// </summary>
public sealed record FuelRules
{
    /// <summary>The engine's rating in horsepower. Zero: the boat burns nothing.</summary>
    public double RatedHp { get; init; }
    /// <summary>The hull's speed in knots (about 1.34 times the root of the waterline length in feet): the load is the cube of the speed over it.</summary>
    public double HullSpeedKn { get; init; } = 6;
    /// <summary>Litres of diesel per horsepower-hour at full load.</summary>
    public double LitresPerHpHour { get; init; } = 0.2;
    /// <summary>The load under way is clamped to this range.</summary>
    public double LoadMin { get; init; } = 0.15;
    public double LoadMax { get; init; } = 1.0;
    /// <summary>The load with the engine running and the boat going nowhere (in port, hove-to, on the yard hose, charging at the mooring).</summary>
    public double IdleLoad { get; init; } = 0.15;
    /// <summary>The burn rises by this fraction at the heaviest fouling, in proportion to the growth.</summary>
    public double FoulingMaxAdd { get; init; }
    /// <summary>The burn rises by this fraction while the alternator charges a battery below <see cref="ChargingBelowStateOfCharge"/>.</summary>
    public double ChargingAdd { get; init; }
    public double ChargingBelowStateOfCharge { get; init; } = 0.7;
    /// <summary>A present fault (by id) that makes the engine drink more: the fraction it adds.</summary>
    public IReadOnlyDictionary<string, double> FaultAdds { get; init; } = new Dictionary<string, double>();
    /// <summary>The short phrase for a cause of extra burn: a fault id, or "charging".</summary>
    public IReadOnlyDictionary<string, string> CauseWords { get; init; } = new Dictionary<string, string>();
    /// <summary>The phrase for the fouling, by the growth the player saw (or the truth's, in the log).</summary>
    public IReadOnlyList<Band> FoulingWords { get; init; } = [];
    /// <summary>One multiplier's add in words ("a tenth", "a third"), by the fraction it adds.</summary>
    public IReadOnlyList<Band> AddWords { get; init; } = [];
    /// <summary>The gauge reads to this many litres.</summary>
    public int GaugeStepLitres { get; init; } = 5;
    /// <summary>Below this the tank is empty: the fuel supply is fully restricted.</summary>
    public double EmptyBelowLitres { get; init; } = 0.5;
    /// <summary>The gauge in words, by the fraction of a full tank.</summary>
    public IReadOnlyList<Band> GaugeWords { get; init; } = [];

    public bool Burns => RatedHp > 0;

    public static FuelRules Default { get; } = new();
}

/// <summary>
/// Night and fog at sea (design note 010, step 2). The default has no fog cap. All numbers in the shipped data are PLACEHOLDERS;
/// the harbourmaster's fog rule is UNVERIFIED. What a boat running dark risks is a close call (<see cref="CloseCallRules"/>, cause "night").
/// </summary>
public sealed record NightRules
{
    /// <summary>An hour is dark when it is more than half the day's length from this hour.</summary>
    public double SolarNoonHour { get; init; } = 13;
    /// <summary>Knots in fog without a working chartplotter or radar. 0: fog does not slow her and the harbour does not hold her.</summary>
    public double FogKnots { get; init; }
    /// <summary>Knots in fog with a working chartplotter only (radar: no cap).</summary>
    public double FogKnotsWithPlotter { get; init; }

    public bool FogMatters => FogKnots > 0;

    public static NightRules Default { get; } = new();
}

/// <summary>
/// Close calls under way (design note 010, second pass D; step E adds the cause "night"): per cause a chance per whole hour, the share of
/// close calls a ferry reports, the narrations (a ferry's or not, picked by the roll) and the line that tells she was reported. A cause may
/// bring the harbourmaster's letter when a ferry reports it (design note 010, second pass E). The default has none. All numbers in the shipped
/// data are PLACEHOLDERS; the chances, and what ferries and harbourmasters do, are UNVERIFIED.
/// </summary>
public sealed record CloseCallRules
{
    /// <summary>What the harbourmaster's letter bills (money; set from economy.json, never the boat file).</summary>
    public int LetterNok { get; init; }
    /// <summary>The fictional ferries that report (the roll picks one for the letter).</summary>
    public IReadOnlyList<string> Ferries { get; init; } = [];
    /// <summary>The ledger line of the letter's bill.</summary>
    public string LetterBillText { get; init; } = "";
    /// <summary>The alert that tells the letter has come: {ferry}, {day}, {hour}, {nok} (each letter's text differs, so none is folded into another).</summary>
    public string LetterAlert { get; init; } = "";
    public IReadOnlyDictionary<string, CloseCallCause> Causes { get; init; } = new Dictionary<string, CloseCallCause>();

    public static CloseCallRules Default { get; } = new();
}

/// <summary>
/// How the plotter and the radar wear (design note 010, second pass G). Each is a count of exposure that starts when the instrument is fitted;
/// a dormant fault wakes when the count reaches its spawn rule's threshold times the house's seeded spread. All numbers PLACEHOLDERS, UNVERIFIED.
/// </summary>
public sealed record InstrumentRules
{
    /// <summary>The plug behind the binnacle: wetness a full day of rain (the exposure rain term, so a winter cover keeps it off) adds, and what an hour of spray at sea adds.</summary>
    public PlotterPlugRules PlotterPlug { get; init; } = new();
    /// <summary>The scanner cable on the mast: chafe an hour adds in a wind of force FromForce, FromForce + 1 ..., times UnderWayFactor when she is at sea.</summary>
    public ScannerCableRules ScannerCable { get; init; } = new();

    public static InstrumentRules Default { get; } = new();
}

public sealed record PlotterPlugRules
{
    public double RainPerDay { get; init; }
    public double SprayPerHourAtSea { get; init; }
    public int SprayFromForce { get; init; } = 99;
}

public sealed record ScannerCableRules
{
    public int FromForce { get; init; } = 99;
    public IReadOnlyList<double> PerHourByForce { get; init; } = [];
    public double UnderWayFactor { get; init; } = 1;
}

/// <summary>One cause of close calls: the chance for each whole hour under way, the share a ferry reports, and the words.</summary>
public sealed record CloseCallCause
{
    public double ChancePerHour { get; init; }
    public double ReportChance { get; init; }
    public IReadOnlyList<CloseCallNarration> Narrations { get; init; } = [];
    public string ReportLine { get; init; } = "";
    /// <summary>A reported close call of this cause brings the harbourmaster's letter.</summary>
    public bool Letter { get; init; }
    /// <summary>The component whose present faults the letter shows the owner (the lights: the letter says she was unlit). Empty: none.</summary>
    public string LetterDiscovers { get; init; } = "";
    /// <summary>The letter, with {ferry}, {day}, {hour} and {nok}.</summary>
    public string LetterText { get; init; } = "";
    /// <summary>What the passage plan says when the player knows the lights have a problem: finishes "...you know the navigation lights have a problem: ".</summary>
    public string PlanLine { get; init; } = "";
}

/// <summary>A close call in words; Ferry says it was a ferry (the narrations of a reported close call are the ferry ones).</summary>
public sealed record CloseCallNarration(bool Ferry, string Text);

/// <summary>
/// What an event of the calendar of wants asks of the boat (design note 010, step 2). Kind: fitted (an upgrade, fitted and without a present
/// fault), working (a component, and any others named, without a present fault), anyWorking (any one of several alternatives aboard and without a present fault), dryBunks (the cushions fitted and no water over the sole in the last Days days),
/// warm (met when the component is fitted, or the night's low is at least BelowC degrees). Text is the requirement in the player's words.
/// </summary>
public sealed record RequirementRule
{
    public string Kind { get; init; } = "";
    /// <summary>The component it is about. For kind anyWorking, empty.</summary>
    public string Component { get; init; } = "";
    /// <summary>
    /// Kind working: more components that must all work as well. Kind anyWorking: the alternatives, each a component id or several joined by
    /// '+' that must all be aboard and work; the requirement holds when any alternative does ("a heads that works": the old heads with its
    /// valve, or the new heads).
    /// </summary>
    public IReadOnlyList<string> Components { get; init; } = [];
    /// <summary>The key of the failed line (and of the view); empty: the component.</summary>
    public string Id { get; init; } = "";
    /// <summary>The key of this requirement's failed line.</summary>
    public string Key => Id.Length > 0 ? Id : Component;
    public string Text { get; init; } = "";
    public int Days { get; init; }
    public double BelowC { get; init; }
}

/// <summary>
/// A day of an event's window (0 is the first) and an hour of that day: the moment by which she must be there (design note 010, second pass H).
/// </summary>
public sealed record EventMoment
{
    public int Day { get; init; }
    public int Hour { get; init; }
}

/// <summary>
/// Being there early (the club cruise on the Friday evening): by this moment the event is made with extra lines, and the logbook and the season
/// page say she made it. Lines are told with the made lines; Log is the logbook's line; Short is the season page's words.
/// </summary>
public sealed record EventEarly
{
    public int Day { get; init; }
    public int Hour { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = [];
    public string Log { get; init; } = "";
    public string Short { get; init; } = "";
}

/// <summary>
/// One beat of a week the player picks (the friend's week; design note 010, second pass H). Kind: arrival (at the event's port, at Hour of the first day:
/// the requirements named in Requires are judged; failing one ends the week), trip (a passage to Port made in the week with the guest aboard) or night
/// (at Hour of the last day, afloat at any port: a cold night needs what Requires asks). Requires names requirements of the event by key.
/// </summary>
public sealed record BeatRule
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    /// <summary>The beat in the player's words ("A day trip to Hestholmen").</summary>
    public string Text { get; init; } = "";
    /// <summary>What it asks, for a beat with no requirement of its own (the trip).</summary>
    public string Ask { get; init; } = "";
    public int Hour { get; init; }
    public string Port { get; init; } = "";
    public IReadOnlyList<string> Requires { get; init; } = [];
    /// <summary>Told when the beat opens (the arrival tells what the friend wants).</summary>
    public IReadOnlyList<string> AnnounceLines { get; init; } = [];
    public IReadOnlyList<string> MadeLines { get; init; } = [];
    /// <summary>The night beat when the night is not cold.</summary>
    public IReadOnlyList<string> PassLines { get; init; } = [];
    public IReadOnlyDictionary<string, string> FailedLines { get; init; } = new Dictionary<string, string>();
    public string MissedText { get; init; } = "";
    /// <summary>Told when a wait is cut at the beat's hour.</summary>
    public string ComeBackLine { get; init; } = "";
    /// <summary>Told when a wait is cut on the morning of the beat's day (the night).</summary>
    public string MorningLine { get; init; } = "";
}

/// <summary>How the week went, by outcome (good, made, thin): the closing line, and the short words the season page uses.</summary>
public sealed record WeekOutcome
{
    public string Line { get; init; } = "";
    public string Short { get; init; } = "";
}

/// <summary>The window in which the player picks the week of an event, and how much notice it needs.</summary>
public sealed record EventPick
{
    public int FromDayOfYear { get; init; }
    public int ToDayOfYear { get; init; }
    public int NoticeDays { get; init; } = 1;
}

/// <summary>
/// One event of the calendar of wants (design note 010, step 2): a window, a place, requirements and a story. The window of a fixed event
/// in year y starts on the first day on or after y x 360 + FromDayOfYear whose weekday is Weekday (none: that day) and lasts Days days.
/// Overnight: she must be there at the midnight that ends the first day. Pick: the player picks the week (the friend's week).
/// </summary>
public sealed record EventRule
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Port { get; init; } = "";
    public int FromDayOfYear { get; init; }
    public string? Weekday { get; init; }
    public int Days { get; init; } = 1;
    public bool Overnight { get; init; }
    public EventPick? Pick { get; init; }
    public IReadOnlyList<RequirementRule> Requires { get; init; } = [];
    public IReadOnlyList<string> MadeLines { get; init; } = [];
    /// <summary>What shows when a requirement fails, by the component id of the requirement.</summary>
    public IReadOnlyDictionary<string, string> FailedLines { get; init; } = new Dictionary<string, string>();
    public string MissedText { get; init; } = "";
    /// <summary>The moment after which she is no longer judged (the club cruise: the Saturday evening). Null: the whole window.</summary>
    public EventMoment? MadeBy { get; init; }
    public EventEarly? Early { get; init; }
    /// <summary>The beats of a picked week, in order; empty: one judgement on the first day.</summary>
    public IReadOnlyList<BeatRule> Beats { get; init; } = [];
    public IReadOnlyDictionary<string, WeekOutcome> WeekOutcomes { get; init; } = new Dictionary<string, WeekOutcome>();
    /// <summary>Told when the player leaves the guest ashore (a wait during the week).</summary>
    public string WalkLine { get; init; } = "";
    /// <summary>Told when the guest is asked: what the week costs.</summary>
    public string InviteCostLine { get; init; } = "";
}

/// <summary>
/// A part the boat can be given (design note 010, step 3): a full component that is not aboard until it is fitted, and how it is fitted.
/// The chandlery item that carries it names it in <see cref="ChandleryItem.Upgrade"/>. Every number PLACEHOLDER for Amit.
/// </summary>
public sealed record UpgradeRule
{
    /// <summary>The component it becomes. Built by the loader like any component; kept out of the boat's components until it is fitted.</summary>
    public required Haulout.Sim.Components.Component Component { get; init; }
    /// <summary>The place the player must stand to fit it.</summary>
    public string PlaceId { get; init; } = "";
    /// <summary>The player's own work.</summary>
    public int InstallMinutes { get; init; }
    /// <summary>The yard's labour. Null (with <see cref="YardDays"/>): the yard does not do this job.</summary>
    public int? YardNok { get; init; }
    public int? YardDays { get; init; }
    /// <summary>What the broker adds for it, at condition 1.</summary>
    public int ValueNok { get; init; }
    /// <summary>What it makes possible, in the player's words.</summary>
    public string Opens { get; init; } = "";
    /// <summary>The narrative of fitting it.</summary>
    public string InstallText { get; init; } = "";
    /// <summary>The components aboard from the start that fitting it takes off the boat (the old heads and its valve): gone, with their faults.</summary>
    public IReadOnlyList<string> Replaces { get; init; } = [];
    /// <summary>Components that come aboard with it (design note 010, second pass G: the radar's scanner cable): fitted together, never an upgrade of their own.</summary>
    public IReadOnlyList<Haulout.Sim.Components.Component> Also { get; init; } = [];
}

/// <summary>
/// Reputation at the yard and the harbour (design note 010, step 4), read from counters the game already keeps and never shown as a number.
/// The default weighs nothing and moves nothing, so a boat built without data behaves as before. All numbers in the shipped data are PLACEHOLDERS.
/// </summary>
public sealed record ReputationRules
{
    /// <summary>One point for every this many standing bills paid within the bank's limit. 0: bills do not count.</summary>
    public int OnTimeBillsPerPoint { get; init; }
    /// <summary>Points for a haulout on the booked day, a tow, a parted mooring, a forfeited slot, a bill that left the balance past the limit.</summary>
    public int SlotKept { get; init; }
    public int Tow { get; init; }
    public int MooringParted { get; init; }
    public int SlotMissed { get; init; }
    public int BillPastLimit { get; init; }
    /// <summary>Points for a ferry's report after a close call (design note 010, second pass D).</summary>
    public int FerryReport { get; init; }
    public int Min { get; init; } = -5;
    public int Max { get; init; } = 5;
    /// <summary>Each point takes this share off the tow (a negative point adds it).</summary>
    public double TowFactorPerPoint { get; init; }
    /// <summary>Each point brings the yard's first free slot this many days nearer (a negative point pushes it later).</summary>
    public int SlotDaysPerPoint { get; init; }
    /// <summary>The sentence for a score: the first band whose Under exceeds it.</summary>
    public IReadOnlyList<Band> Words { get; init; } = [];

    public static ReputationRules Default { get; } = new();
}

/// <summary>One thing that makes the engine drink more: what it is called (a short phrase from data) and the fraction it adds.</summary>
public sealed record FuelAdd(string Cause, double Add);

/// <summary>The burn in litres an hour, and the named multipliers it was made of.</summary>
public sealed record FuelBurn(double LitresPerHour, IReadOnlyList<FuelAdd> Adds)
{
    /// <summary>The load: the cube of the speed over the hull's speed, clamped; the idle load when she is not under way (null speed).</summary>
    public static double Load(FuelRules rules, double? speedKn) =>
        speedKn is not { } s ? rules.IdleLoad : Math.Clamp(Math.Pow(Math.Max(0, s) / rules.HullSpeedKn, 3), rules.LoadMin, rules.LoadMax);

    /// <summary>
    /// ratedHp x litresPerHpHour x load x the product of (1 + add) over the adds. The one function behind both the burn (every true add) and the
    /// plan (only what the player knows).
    /// </summary>
    public static FuelBurn Of(FuelRules rules, double? speedKn, IReadOnlyList<FuelAdd> adds)
    {
        double rate = rules.RatedHp * rules.LitresPerHpHour * Load(rules, speedKn);
        foreach (var a in adds) rate *= 1 + a.Add;
        return new FuelBurn(rate, adds);
    }

    /// <summary>"heavy fouling adds a third; a clogged fuel filter adds a tenth".</summary>
    public static string Words(FuelRules rules, IReadOnlyList<FuelAdd> adds) =>
        string.Join("; ", adds.Select(a => $"{a.Cause} adds {Band.Pick(rules.AddWords, a.Add)}"));
}
