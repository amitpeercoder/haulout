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

        var components = dto.Components.Select(c =>
        {
            var spec = new ComponentSpec(
                c.Subsystem, c.Position,
                (c.Reach ?? []).Select(r => new ReachEntry(r.Place, r.Cover, r.Visible, r.Reachable)).ToList(),
                c.Aliases ?? [], c.Description ?? "",
                c.SeriesOhms, c.Cleanable, c.ReplaceCostNok, c.ReplaceMinutes,
                new Dictionary<string, double>(c.Properties ?? new()),
                c.RemovablePart is { } rp ? new RemovablePart(rp.Name, rp.Aliases ?? [], rp.UncoveredText ?? "", rp.UncoverText, rp.RefitText) : null,
                c.BelowWaterline, c.ReplaceText, c.NeedsDry);
            var wear = WearRate.CalendarYears(c.WearYears > 0 ? c.WearYears : 10, c.WearPerUseHour);
            var faults = (c.Faults ?? []).Select(f => new Fault(
                f.Id, f.Name, c.Id, f.Effect ?? new FaultEffect(FaultEffectKind.None, 0),
                f.Reveals ?? [], f.FixedBy ?? [], f.Present ?? true,
                f.OnHardOnly, f.Harmless, f.Dormant, f.Consequence ?? "", f.CostLaterNok, f.LaterNote,
                f.Diy is { } d ? new DiyRoute(d.How, d.PartsNok, d.Minutes) : null,
                f.Yard is { } y ? new YardRoute(y.How, y.Nok, y.LeadDays) : null,
                f.Escalation is { } e ? new Escalation(e.To, e.OnLaunch, e.Text ?? "") : null,
                f.Properties is null ? null : new Dictionary<string, double>(f.Properties),
                f.Progression is { } pr ? new ProgressionSpec(pr.BaseRatePerDay, pr.Factors ?? [], pr.ExposedBy ?? [], pr.RainOnlyWhenOff ?? []) : null,
                f.Effects, ParseSpawn(f.Spawn), f.ArrivalSign?.Text));
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
                        new DiyRoute(kv.Value.Diy.How, kv.Value.Diy.PartsNok, kv.Value.Diy.Minutes),
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
                w.LevelWords is { Count: > 0 } words ? words.Select(x => new LevelWord(x.UnderLitres, x.Text)).ToList() : WaterRules.Default.LevelWords)
            : null;
        var electrical = dto.Electrical is { } el ? new ElectricalRules(el.SelfDischargePerDay, el.PumpMinStateOfCharge, el.CrankDraw, el.ChargePerHour, el.RunDrawPerHour) : null;
        var insurance = dto.Insurance is { } ins ? new InsuranceRules(ins.ValueNok, ins.PayoutFraction) : null;

        return new Boat(dto.Name, places, covers, positions, components, economy, survey, quality, rates, water, electrical, insurance);
    }

    /// <summary>"spawn": one rule or a list of rules; any one crossing wakes the fault.</summary>
    private static List<SpawnRule> ParseSpawn(JsonElement? spawn)
    {
        if (spawn is not { } el || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return [];
        var items = el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().ToList() : [el];
        return items.Select(i => new SpawnRule(
            i.GetProperty("when").GetString() ?? throw new InvalidOperationException("A spawn rule needs 'when'."),
            i.GetProperty("atLeast").GetDouble())).ToList();
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
        public int PartsNok { get; set; }
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
        public int ReplaceCostNok { get; set; }
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
    }

    private sealed class ArrivalSignDto
    {
        public string Text { get; set; } = "";
    }
}
