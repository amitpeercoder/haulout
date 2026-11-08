using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haulout.Sim.Components;
using Haulout.Sim.Faults;

namespace Haulout.Sim.Data;

public static class BoatLoader
{
    private const string Sloop28Resource = "Haulout.Sim.Data.sloop28.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Boat LoadSloop28()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream(Sloop28Resource)
            ?? throw new InvalidOperationException($"Embedded resource '{Sloop28Resource}' not found.");
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

    public static Boat Load(string json)
    {
        var dto = JsonSerializer.Deserialize<BoatDto>(json, Options)
            ?? throw new InvalidOperationException("Boat JSON is empty.");

        var places = dto.Places.Select(p => new Place(
            p.Id, p.Name, p.Aliases ?? [], p.Description ?? "", p.Way ?? "", p.Adjacent ?? [], p.OnlyOnTheHard, p.Exposed));
        var covers = dto.Covers.Select(c => new Cover(
            c.Id, c.Name, c.Aliases ?? [], c.Place, c.Plural, c.RemoveMinutes, c.RefitMinutes,
            c.RemoveText ?? "", c.RefitText ?? "", c.StartsOff, c.OffPhrase ?? "", c.StateWords is { Count: 2 } ? c.StateWords : null,
            c.RemoveLog ?? "", c.RefitLog ?? ""));
        var positions = dto.Positions.Select(p => new Position(p.Id, p.Label));

        var chandlery = dto.Chandlery ?? [];
        var components = dto.Components.Select(c =>
        {
            // Decision 007/1: a part the chandlery sells costs what the chandlery charges. The price lives on the shop item;
            // the component's replace price and the parts of a DIY fix by replacing it are taken from it.
            var part = chandlery.FirstOrDefault(i => !i.Tool && i.ForComponent == c.Id);
            int replaceCostNok = PartPrice(part, c.ReplaceCostNok, $"component '{c.Id}' replaceCostNok");
            var spec = new ComponentSpec(
                c.Subsystem, c.Position,
                (c.Reach ?? []).Select(r => new ReachEntry(r.Place, r.Cover, r.Visible, r.Reachable)).ToList(),
                c.Aliases ?? [], c.Description ?? "",
                c.SeriesOhms, c.Cleanable, replaceCostNok, c.ReplaceMinutes,
                new Dictionary<string, double>(c.Properties ?? new()),
                c.RemovablePart is { } rp ? new RemovablePart(rp.Name, rp.Aliases ?? [], rp.UncoveredText ?? "", rp.UncoverText, rp.RefitText) : null,
                c.BelowWaterline, c.ReplaceText, c.NeedsDry);
            var wear = WearRate.CalendarYears(c.WearYears > 0 ? c.WearYears : 10, c.WearPerUseHour);
            var faults = (c.Faults ?? []).Select(f => new Fault(
                f.Id, f.Name, c.Id, f.Effect ?? new FaultEffect(FaultEffectKind.None, 0),
                f.Reveals ?? [], f.FixedBy ?? [], f.Present ?? true,
                f.OnHardOnly, f.Harmless, f.Dormant, f.Consequence ?? "", f.CostLaterNok, f.LaterNote,
                f.Diy is { } d
                    ? new DiyRoute(d.How,
                        (f.FixedBy ?? []).Contains(PlayerAction.Replace)
                            ? PartPrice(part, d.PartsNok, $"fault '{f.Id}' diy partsNok")
                            : d.PartsNok ?? 0,
                        d.Minutes)
                    : null,
                f.Yard is { } y ? new YardRoute(y.How, y.Nok, y.LeadDays) : null,
                f.Escalation is { } e ? new Escalation(e.To, e.OnLaunch, e.Text ?? "") : null,
                f.Properties is null ? null : new Dictionary<string, double>(f.Properties),
                f.Progression is { } pr ? new ProgressionSpec(pr.BaseRatePerDay, pr.Factors ?? [], pr.ExposedBy ?? [], pr.RainOnlyWhenOff ?? []) : null,
                f.Effects, ParseSpawn(f.Spawn), f.ArrivalSign?.Text,
                f.AtSea is { } asp ? new AtSeaSpec(asp.Text, asp.Spare) : null));
            return new Component(c.Id, c.Name, c.Condition, wear, spec, faults);
        });

        var economy = dto.Economy is { } ec
            ? new Economy(ec.StartingBalanceNok, ec.LaunchDeadlineDay, ec.EmergencyLiftNok, ec.LaunchMinutes, ec.EmergencyLiftDays)
            : null;
        var survey = dto.Survey is { } sv
            ? new SurveyData(
                sv.NokPerFoot, sv.LengthFeet, sv.Days, sv.Surveyor, sv.Opening ?? [], sv.Closing ?? [],
                (sv.Items ?? []).Select(i => new SurveyItemData(i.Component, i.Severity, i.Fault, i.Finding, i.Text)).ToList())
            : null;
        var quality = dto.DiyQuality is { } dq
            ? new DiyQuality(
                dq.StartSkill, dq.Threshold, dq.PoorJobChance,
                (dq.PoorJobs ?? new()).ToDictionary(
                    kv => kv.Key,
                    kv => new PoorJobTemplate(
                        kv.Value.Name, kv.Value.Reveal, kv.Value.Consequence, kv.Value.CostLaterNok,
                        new DiyRoute(kv.Value.Diy.How, kv.Value.Diy.PartsNok ?? 0, kv.Value.Diy.Minutes),
                        new YardRoute(kv.Value.Yard.How, kv.Value.Yard.Nok, kv.Value.Yard.LeadDays)))
                    .ToDictionary(kv => Enum.Parse<Subsystem>(kv.Key, ignoreCase: true), kv => kv.Value))
            : null;

        var rates = dto.Rates is { } rt
            ? new Rates(
                rt.Threshold, rt.Spread is { Count: 2 } sp ? sp[0] : 0.6, rt.Spread is { Count: 2 } sp2 ? sp2[1] : 1.6,
                new ExposureRates(rt.Exposure?.RainWeight ?? 0, rt.Exposure?.RainCapMm ?? 1, rt.Exposure?.FrostWeight ?? 0, rt.Exposure?.PerOpenCover ?? 0),
                new UseRates(rt.Use?.PerEngineHour ?? 0),
                new NeglectRates(
                    rt.Neglect?.PerKnownOpenFault ?? 0, rt.Neglect?.BatteryLow ?? 0, rt.Neglect?.PerCoverLeftOff ?? 0,
                    rt.Neglect?.PerDayAwayBeyond3 ?? 0, rt.Neglect?.Covers ?? []),
                new WeatherGates(rt.WeatherGates?.WindForce ?? 7, rt.WeatherGates?.RainMm ?? 0.5))
            : null;

        var water = dto.Water is { } w
            ? new WaterRules(
                w.StartLitres, w.SoleLitres, w.SinkLitres, w.RainThroughOpenHatchLitresPerMm,
                w.ManualPumpLitresPerMinute, w.ManualPumpMinutes, w.SoleText ?? WaterRules.Default.SoleText,
                w.LevelWords is { Count: > 0 } words ? words.Select(x => new LevelWord(x.UnderLitres, x.Text)).ToList() : WaterRules.Default.LevelWords,
                w.HarbourmasterLitres ?? w.SoleLitres, w.HarbourmasterText ?? WaterDefaults.HarbourmasterText)
            : null;
        var electrical = dto.Electrical is { } el ? new ElectricalRules(el.SelfDischargePerDay, el.PumpMinStateOfCharge, el.CrankDraw, el.ChargePerHour, el.RunDrawPerHour) : null;
        var insurance = dto.Insurance is { } ins ? new InsuranceRules(ins.ValueNok, ins.PayoutFraction) : null;

        foreach (var port in dto.Trips?.Ports ?? [])
            foreach (var id in port.Stock ?? [])
                if (!chandlery.Any(i => i.Id == id))
                    throw new InvalidOperationException($"Port '{port.Id}' stocks '{id}', which is not in the chandlery.");

        var trips = dto.Trips is { } tr
            ? new TripRules(
                tr.Home, tr.Ports ?? [], DeriveRoutes(tr.Ports ?? [], tr.Routes ?? []), tr.Tow ?? new TowRules())
            : null;

        return new Boat(
            dto.Name, places, covers, positions, components, economy, survey, quality, rates, water, electrical, insurance,
            chandlery, trips, dto.Sea, dto.Forecast, dto.Yard, dto.Fouling, dto.Mooring, dto.Layup, dto.Sale);
    }

    /// <summary>
    /// Routes as written in the data carry no distance or course: both are derived from the two ports' chart positions (design note 009),
    /// distance to 0.1 nm and course to the whole degree, 0 to 359. A route whose ports lack a position, a leftover distance or course,
    /// two ports at one point, or a shelter outside the route are errors.
    /// </summary>
    private static List<Route> DeriveRoutes(List<Port> ports, List<RouteDto> routes)
    {
        for (int i = 0; i < ports.Count; i++)
            for (int j = i + 1; j < ports.Count; j++)
                if (ports[i].Chart is { } a && ports[j].Chart is { } b && a == b)
                    throw new InvalidOperationException($"Ports '{ports[i].Id}' and '{ports[j].Id}' are at the same point on the chart.");

        var result = new List<Route>();
        foreach (var r in routes)
        {
            if (r.DistanceNm is not null || r.CourseDeg is not null)
                throw new InvalidOperationException($"Route {r.From} → {r.To} carries distanceNm or courseDeg: they are derived from the ports' chart positions.");
            var from = ports.FirstOrDefault(p => p.Id == r.From) ?? throw new InvalidOperationException($"Route {r.From} → {r.To}: no port '{r.From}'.");
            var to = ports.FirstOrDefault(p => p.Id == r.To) ?? throw new InvalidOperationException($"Route {r.From} → {r.To}: no port '{r.To}'.");
            foreach (var end in new[] { from, to })
                if (end.Chart is null)
                    throw new InvalidOperationException($"Route {r.From} → {r.To}: port {end.Id} has no chart position.");
            double dx = to.Chart!.EastNm - from.Chart!.EastNm, dy = to.Chart.NorthNm - from.Chart.NorthNm;
            double distance = Math.Round(Math.Sqrt(dx * dx + dy * dy), 1, MidpointRounding.AwayFromZero);
            int course = (((int)Math.Round(Math.Atan2(dx, dy) * 180 / Math.PI, MidpointRounding.AwayFromZero) % 360) + 360) % 360;
            var shelter = r.Shelter ?? [];
            foreach (var s in shelter)
                if (s.AtNm <= 0 || s.AtNm >= distance)
                    throw new InvalidOperationException($"Route {r.From} → {r.To}: shelter '{s.Name}' at {s.AtNm} nm is not inside the route (0 to {distance} nm).");
            result.Add(new Route { From = r.From, To = r.To, DistanceNm = distance, CourseDeg = course, Shelter = shelter });
        }
        return result;
    }

    /// <summary>
    /// The price of a part the chandlery sells: the shop item's. A number written in the data as well must agree with it,
    /// so two prices for one part cannot drift apart again. No shop item: the data's number (0 when absent).
    /// </summary>
    private static int PartPrice(ChandleryItem? part, int? dataNok, string what)
    {
        if (part is null) return dataNok ?? 0;
        if (dataNok is { } n && n != 0 && n != part.PriceNok)
            throw new InvalidOperationException(
                $"The chandlery sells the {part.Name} at {part.PriceNok} NOK but {what} says {n}. Remove the number: the chandlery price is used.");
        return part.PriceNok;
    }

    /// <summary>"spawn": one rule or a list of rules; any one crossing wakes the fault.</summary>
    private static List<SpawnRule> ParseSpawn(JsonElement? spawn)
    {
        if (spawn is not { } el || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return [];
        var items = el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().ToList() : [el];
        return items.Select(i => new SpawnRule(
            i.GetProperty("when").GetString() ?? throw new InvalidOperationException("A spawn rule needs 'when'."),
            i.GetProperty("atLeast").GetDouble(),
            i.TryGetProperty("spread", out var spread) && spread.ValueKind == JsonValueKind.True,
            i.TryGetProperty("repeats", out var repeats) && repeats.ValueKind == JsonValueKind.True,
            i.TryGetProperty("cause", out var cause) && cause.ValueKind == JsonValueKind.String ? cause.GetString() : null)).ToList();
    }

    private sealed class BoatDto
    {
        public string Name { get; set; } = "";
        public List<PlaceDto> Places { get; set; } = [];
        public List<CoverDto> Covers { get; set; } = [];
        public List<PositionDto> Positions { get; set; } = [];
        public List<ComponentDto> Components { get; set; } = [];
        public EconomyDto? Economy { get; set; }
        public SurveyDto? Survey { get; set; }
        public DiyQualityDto? DiyQuality { get; set; }
        public RatesDto? Rates { get; set; }
        public WaterDto? Water { get; set; }
        public ElectricalDto? Electrical { get; set; }
        public InsuranceDto? Insurance { get; set; }
        public List<ChandleryItem>? Chandlery { get; set; }
        public TripsDto? Trips { get; set; }
        public SeaRules? Sea { get; set; }
        public ForecastRules? Forecast { get; set; }
        public YardRules? Yard { get; set; }
        public FoulingRules? Fouling { get; set; }
        public MooringRules? Mooring { get; set; }
        public LayupRules? Layup { get; set; }
        public SaleRules? Sale { get; set; }
    }

    private sealed class RouteDto
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        /// <summary>Present only to be rejected: derived from the ports' positions.</summary>
        public double? DistanceNm { get; set; }
        public int? CourseDeg { get; set; }
        public List<Shelter>? Shelter { get; set; }
    }

    private sealed class TripsDto
    {
        public string Home { get; set; } = "";
        public List<Port>? Ports { get; set; }
        public List<RouteDto>? Routes { get; set; }
        public TowRules? Tow { get; set; }
    }

    private sealed class LevelWordDto
    {
        public double? UnderLitres { get; set; }
        public string Text { get; set; } = "";
    }

    private sealed class WaterDto
    {
        public double StartLitres { get; set; }
        public double SoleLitres { get; set; } = 120;
        public double SinkLitres { get; set; } = 1500;
        public double RainThroughOpenHatchLitresPerMm { get; set; }
        public double ManualPumpLitresPerMinute { get; set; } = 10;
        public int ManualPumpMinutes { get; set; } = 15;
        public string? SoleText { get; set; }
        public double? HarbourmasterLitres { get; set; }
        public string? HarbourmasterText { get; set; }
        public List<LevelWordDto>? LevelWords { get; set; }
    }

    private sealed class ElectricalDto
    {
        public double SelfDischargePerDay { get; set; }
        public double PumpMinStateOfCharge { get; set; } = 0.15;
        public double CrankDraw { get; set; } = 0.01;
        public double ChargePerHour { get; set; } = 0.10;
        public double RunDrawPerHour { get; set; } = 0.03;
    }

    private sealed class InsuranceDto
    {
        public int ValueNok { get; set; }
        public double PayoutFraction { get; set; }
    }

    private sealed class RatesDto
    {
        public double Threshold { get; set; } = 1.0;
        public List<double>? Spread { get; set; }
        public ExposureDto? Exposure { get; set; }
        public UseDto? Use { get; set; }
        public NeglectDto? Neglect { get; set; }
        public WeatherGatesDto? WeatherGates { get; set; }
    }

    private sealed class ExposureDto
    {
        public double RainWeight { get; set; }
        public double RainCapMm { get; set; } = 1;
        public double FrostWeight { get; set; }
        public double PerOpenCover { get; set; }
    }

    private sealed class WeatherGatesDto
    {
        public int WindForce { get; set; } = 7;
        public double RainMm { get; set; } = 0.5;
    }

    private sealed class UseDto
    {
        public double PerEngineHour { get; set; }
    }

    private sealed class NeglectDto
    {
        public double PerKnownOpenFault { get; set; }
        public double BatteryLow { get; set; }
        public double PerCoverLeftOff { get; set; }
        public double PerDayAwayBeyond3 { get; set; }
        public List<string>? Covers { get; set; }
    }

    private sealed class ProgressionDto
    {
        public double BaseRatePerDay { get; set; }
        public List<RateFactor>? Factors { get; set; }
        public List<string>? ExposedBy { get; set; }
        public List<string>? RainOnlyWhenOff { get; set; }
    }

    private sealed class EconomyDto
    {
        public int StartingBalanceNok { get; set; }
        public int LaunchDeadlineDay { get; set; }
        public int EmergencyLiftNok { get; set; }
        public int LaunchMinutes { get; set; }
        public int EmergencyLiftDays { get; set; }
    }

    private sealed class SurveyItemDto
    {
        public string Component { get; set; } = "";
        public SurveySeverity Severity { get; set; }
        public string? Fault { get; set; }
        public SurveyFinding Finding { get; set; }
        public string Text { get; set; } = "";
    }

    private sealed class SurveyDto
    {
        public int NokPerFoot { get; set; }
        public int LengthFeet { get; set; }
        public int Days { get; set; }
        public string Surveyor { get; set; } = "";
        public List<string>? Opening { get; set; }
        public List<string>? Closing { get; set; }
        public List<SurveyItemDto>? Items { get; set; }
    }

    private sealed class DiyDto
    {
        public string How { get; set; } = "";
        /// <summary>Optional when the fix replaces a part the chandlery sells: the chandlery price is used.</summary>
        public int? PartsNok { get; set; }
        public int Minutes { get; set; }
    }

    private sealed class YardDto
    {
        public string How { get; set; } = "";
        public int Nok { get; set; }
        public int LeadDays { get; set; }
    }

    private sealed class EscalationDto
    {
        public string To { get; set; } = "";
        public bool OnLaunch { get; set; }
        public string? Text { get; set; }
    }

    private sealed class PoorJobDto
    {
        public string Name { get; set; } = "";
        public string Reveal { get; set; } = "";
        public string Consequence { get; set; } = "";
        public int CostLaterNok { get; set; }
        public DiyDto Diy { get; set; } = new();
        public YardDto Yard { get; set; } = new();
    }

    private sealed class DiyQualityDto
    {
        public int StartSkill { get; set; }
        public int Threshold { get; set; }
        public double PoorJobChance { get; set; }
        public Dictionary<string, PoorJobDto>? PoorJobs { get; set; }
    }

    private sealed class PlaceDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public string? Description { get; set; }
        public string? Way { get; set; }
        public List<string>? Adjacent { get; set; }
        public bool OnlyOnTheHard { get; set; }
        public bool Exposed { get; set; }
    }

    private sealed class CoverDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public string Place { get; set; } = "";
        public bool Plural { get; set; }
        public int RemoveMinutes { get; set; }
        public int RefitMinutes { get; set; }
        public string? RemoveText { get; set; }
        public string? RefitText { get; set; }
        public bool StartsOff { get; set; }
        public string? OffPhrase { get; set; }
        public List<string>? StateWords { get; set; }
        public string? RemoveLog { get; set; }
        public string? RefitLog { get; set; }
    }

    private sealed class PositionDto
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private sealed class ReachDto
    {
        public string Place { get; set; } = "";
        public string? Cover { get; set; }
        public bool Visible { get; set; }
        public bool Reachable { get; set; }
    }

    private sealed class ComponentDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Subsystem Subsystem { get; set; }
        public string Position { get; set; } = "";
        public List<ReachDto>? Reach { get; set; }
        public List<string>? Aliases { get; set; }
        public string? Description { get; set; }
        public double Condition { get; set; }
        public double WearYears { get; set; }
        public double WearPerUseHour { get; set; }
        public double? SeriesOhms { get; set; }
        /// <summary>Authoring note, ignored by the sim.</summary>
        public string? Note { get; set; }
        public bool Cleanable { get; set; }
        /// <summary>Omitted when the chandlery sells the part (decision 007/1): the shop item's price is used.</summary>
        public int? ReplaceCostNok { get; set; }
        public int ReplaceMinutes { get; set; }
        public Dictionary<string, double>? Properties { get; set; }
        public List<FaultDto>? Faults { get; set; }
        public RemovablePartDto? RemovablePart { get; set; }
        public bool BelowWaterline { get; set; }
        public string? ReplaceText { get; set; }
        public bool NeedsDry { get; set; }
    }

    private sealed class RemovablePartDto
    {
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public string? UncoveredText { get; set; }
        public string? UncoverText { get; set; }
        public string? RefitText { get; set; }
    }

    private sealed class FaultDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public FaultEffect? Effect { get; set; }
        public List<FaultEffect>? Effects { get; set; }
        public JsonElement? Spawn { get; set; }
        public List<FaultReveal>? Reveals { get; set; }
        public List<PlayerAction>? FixedBy { get; set; }
        public bool? Present { get; set; }
        public bool OnHardOnly { get; set; }
        public bool Harmless { get; set; }
        public bool Dormant { get; set; }
        public string? Consequence { get; set; }
        public int CostLaterNok { get; set; }
        public string? LaterNote { get; set; }
        public DiyDto? Diy { get; set; }
        public YardDto? Yard { get; set; }
        public EscalationDto? Escalation { get; set; }
        public Dictionary<string, double>? Properties { get; set; }
        public ProgressionDto? Progression { get; set; }
        /// <summary>Free text for the data's readers (for example "UNVERIFIED"); ignored by the loader.</summary>
        public string? Note { get; set; }
        public ArrivalSignDto? ArrivalSign { get; set; }
        public AtSeaDto? AtSea { get; set; }
    }

    private sealed class AtSeaDto
    {
        public string Text { get; set; } = "";
        public string? Spare { get; set; }
    }

    private sealed class ArrivalSignDto
    {
        public string Text { get; set; } = "";
    }
}
