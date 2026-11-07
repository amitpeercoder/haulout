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

        var compartments = dto.Compartments.Select(c =>
            new Compartment(c.Id, c.Name, c.Aliases ?? [], c.Description ?? ""));

        var components = dto.Components.Select(c =>
        {
            var spec = new ComponentSpec(
                c.Subsystem, c.Location, c.Aliases ?? [], c.Description ?? "",
                c.SeriesOhms, c.Cleanable, c.ReplaceCostNok, c.ReplaceMinutes,
                new Dictionary<string, double>(c.Properties ?? new()));
            var wear = WearRate.CalendarYears(c.WearYears > 0 ? c.WearYears : 10, c.WearPerUseHour);
            var faults = (c.Faults ?? []).Select(f => new Fault(
                f.Id, f.Name, c.Id, f.Effect,
                f.Reveals ?? [], f.FixedBy ?? [], f.Present ?? true));
            return new Component(c.Id, c.Name, c.Condition, wear, spec, faults);
        });

        return new Boat(dto.Name, compartments, components);
    }

    private sealed class BoatDto
    {
        public string Name { get; set; } = "";
        public List<CompartmentDto> Compartments { get; set; } = [];
        public List<ComponentDto> Components { get; set; } = [];
    }

    private sealed class CompartmentDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public string? Description { get; set; }
    }

    private sealed class ComponentDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Subsystem Subsystem { get; set; }
        public string Location { get; set; } = "";
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
    }

    private sealed class FaultDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public FaultEffect Effect { get; set; } = new(FaultEffectKind.SeriesOhms, 0);
        public List<FaultReveal>? Reveals { get; set; }
        public List<PlayerAction>? FixedBy { get; set; }
        public bool? Present { get; set; }
    }
}
