using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;

namespace Haulout.Sim;

/// <summary>
/// Where the player stands. Way: how you get there ("into the cockpit"), used in logs and exits.
/// Adjacent: ids of places one move away (symmetric). OnlyOnTheHard: not available while she is afloat.
/// </summary>
public sealed record Place(
    string Id, string Name, IReadOnlyList<string> Aliases, string Description, string Way,
    IReadOnlyList<string> Adjacent, bool OnlyOnTheHard, bool Exposed = false);

/// <summary>Something you take off to see or reach what is behind it. Removed at PlaceId.</summary>
public sealed record Cover(
    string Id, string Name, IReadOnlyList<string> Aliases, string PlaceId, bool Plural,
    int RemoveMinutes, int RefitMinutes, string RemoveText, string RefitText,
    bool StartsOff = false, string OffPhrase = "", IReadOnlyList<string>? StateWords = null,
    string RemoveLog = "", string RefitLog = "");

/// <summary>The true physical spot of a component, with the label the player sees.</summary>
public sealed record Position(string Id, string Label);

/// <summary>
/// From PlaceId, once CoverId (null: none) is off, a component can be seen, and perhaps touched.
/// Reachable implies Visible.
/// </summary>
public sealed record ReachEntry(string PlaceId, string? CoverId, bool Visible, bool Reachable);

public sealed class Boat
{
    private readonly Dictionary<string, Component> _byId = new();
    private readonly Dictionary<string, Place> _places = new();
    private readonly Dictionary<string, Cover> _covers = new();
    private readonly Dictionary<string, Position> _positions = new();

    public Boat(
        string name, IEnumerable<Place> places, IEnumerable<Cover> covers,
        IEnumerable<Position> positions, IEnumerable<Component> components,
        Economy? economy = null, SurveyData? survey = null, DiyQuality? diyQuality = null, Rates? rates = null,
        WaterRules? water = null, ElectricalRules? electrical = null, InsuranceRules? insurance = null,
        IEnumerable<ChandleryItem>? chandlery = null, TripRules? trips = null, SeaRules? seaRules = null, ForecastRules? forecastRules = null)
    {
        Chandlery = (chandlery ?? []).ToList();
        Trips = trips;
        SeaRules = seaRules ?? SeaRules.Default;
        ForecastRules = forecastRules ?? ForecastRules.Default;
        Rates = rates ?? Rates.Default;
        Water = water ?? WaterRules.Default;
        Electrical = electrical ?? ElectricalRules.Default;
        Insurance = insurance ?? InsuranceRules.Default;
        Economy = economy ?? new Economy(0, 0, 0, 0);
        Survey = survey;
        DiyQuality = diyQuality;
        Name = name;
        Places = places.ToList();
        Covers = covers.ToList();
        Positions = positions.ToList();
        Components = components.ToList();

        foreach (var p in Places)
            if (!_places.TryAdd(p.Id, p)) throw new ArgumentException($"Duplicate place id '{p.Id}'.");
        foreach (var p in Places)
            foreach (var a in p.Adjacent)
            {
                if (!_places.TryGetValue(a, out var other))
                    throw new ArgumentException($"Place '{p.Id}' is adjacent to unknown place '{a}'.");
                if (!other.Adjacent.Contains(p.Id))
                    throw new ArgumentException($"Place '{p.Id}' is adjacent to '{a}' but not the other way round.");
            }

        foreach (var c in Covers)
        {
            if (!_covers.TryAdd(c.Id, c)) throw new ArgumentException($"Duplicate cover id '{c.Id}'.");
            if (!_places.ContainsKey(c.PlaceId))
                throw new ArgumentException($"Cover '{c.Id}' is removed at unknown place '{c.PlaceId}'.");
        }

        foreach (var p in Positions)
            if (!_positions.TryAdd(p.Id, p)) throw new ArgumentException($"Duplicate position id '{p.Id}'.");

        foreach (var c in Components)
        {
            if (!_byId.TryAdd(c.Id, c))
                throw new ArgumentException($"Duplicate component id '{c.Id}'.");
            if (!_positions.ContainsKey(c.PositionId))
                throw new ArgumentException($"Component '{c.Id}' is at unknown position '{c.PositionId}'.");
            foreach (var r in c.Reach)
            {
                if (!_places.ContainsKey(r.PlaceId))
                    throw new ArgumentException($"Component '{c.Id}' has reach at unknown place '{r.PlaceId}'.");
                if (r.CoverId is not null && !_covers.ContainsKey(r.CoverId))
                    throw new ArgumentException($"Component '{c.Id}' has reach behind unknown cover '{r.CoverId}'.");
                if (r.Reachable && !r.Visible)
                    throw new ArgumentException($"Component '{c.Id}' is reachable but not visible from '{r.PlaceId}'.");
            }
            foreach (var f in c.Faults)
                if (f.ComponentId != c.Id)
                    throw new ArgumentException($"Fault '{f.Id}' names component '{f.ComponentId}' but is owned by '{c.Id}'.");
        }
    }

    public string Name { get; }
    public Economy Economy { get; }
    public Rates Rates { get; }
    public WaterRules Water { get; }
    public ElectricalRules Electrical { get; }
    public InsuranceRules Insurance { get; }
    /// <summary>What the chandlery sells, in every port. Empty: nothing for sale.</summary>
    public IReadOnlyList<ChandleryItem> Chandlery { get; }
    /// <summary>Ports and routes. Null: this boat has nowhere to go.</summary>
    public TripRules? Trips { get; }
    public SeaRules SeaRules { get; }
    public ForecastRules ForecastRules { get; }
    /// <summary>Null: this boat has no surveyor.</summary>
    public SurveyData? Survey { get; }
    /// <summary>Null: no poor-job rule.</summary>
    public DiyQuality? DiyQuality { get; }
    public IReadOnlyList<Component> Components { get; }
    public IReadOnlyList<Place> Places { get; }
    public IReadOnlyList<Cover> Covers { get; }
    public IReadOnlyList<Position> Positions { get; }

    public Component Get(string id) =>
        _byId.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"No component '{id}'.");

    public bool TryGet(string id, out Component c) => _byId.TryGetValue(id, out c!);

    public Place GetPlace(string id) =>
        _places.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"No place '{id}'.");

    public Cover GetCover(string id) =>
        _covers.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"No cover '{id}'.");

    public Position GetPosition(string id) =>
        _positions.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"No position '{id}'.");

    public IEnumerable<Fault> AllFaults => Components.SelectMany(c => c.Faults);
}
