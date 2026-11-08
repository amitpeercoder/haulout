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
            p.Id, p.Name, p.Aliases ?? [], p.Description ?? "", p.Way ?? "", p.Adjacent ?? [], p.OnlyOnTheHard));
        var covers = dto.Covers.Select(c => new Cover(
            c.Id, c.Name, c.Aliases ?? [], c.Place, c.Plural, c.RemoveMinutes, c.RefitMinutes,
            c.RemoveText ?? "", c.RefitText ?? ""));
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
                c.BelowWaterline, c.ReplaceText);
            var wear = WearRate.CalendarYears(c.WearYears > 0 ? c.WearYears : 10, c.WearPerUseHour);
            var faults = (c.Faults ?? []).Select(f => new Fault(
                f.Id, f.Name, c.Id, f.Effect,
                f.Reveals ?? [], f.FixedBy ?? [], f.Present ?? true,
                f.OnHardOnly, f.Harmless, f.Dormant, f.Consequence ?? "", f.CostLaterNok, f.LaterNote,
                f.Diy is { } d ? new DiyRoute(d.How, d.PartsNok, d.Minutes) : null,
                f.Yard is { } y ? new YardRoute(y.How, y.Nok, y.LeadDays) : null,
                f.Escalation is { } e ? new Escalation(e.To, e.AfterDays, e.OnLaunch, e.Text ?? "") : null,
                f.Properties is null ? null : new Dictionary<string, double>(f.Properties)));
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

        return new Boat(dto.Name, places, covers, positions, components, economy, survey, quality);
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
        public int? AfterDays { get; set; }
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
        public FaultEffect Effect { get; set; } = new(FaultEffectKind.SeriesOhms, 0);
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
    }
}
