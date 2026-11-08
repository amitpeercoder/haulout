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
    private readonly List<Component> _components;
    /// <summary>Every component there has been, in the order it came aboard, including those an upgrade has taken off the boat.</summary>
    private readonly List<Component> _everAboard;
    private readonly List<Component> _retired = [];
    private readonly Dictionary<string, UpgradeRule> _upgrades = new();

    public Boat(
        string name, IEnumerable<Place> places, IEnumerable<Cover> covers,
        IEnumerable<Position> positions, IEnumerable<Component> components,
        Economy? economy = null, SurveyData? survey = null, DiyQuality? diyQuality = null, Rates? rates = null,
        WaterRules? water = null, ElectricalRules? electrical = null, InsuranceRules? insurance = null,
        IEnumerable<ChandleryItem>? chandlery = null, TripRules? trips = null, SeaRules? seaRules = null, ForecastRules? forecastRules = null,
        YardRules? yard = null, FoulingRules? fouling = null, MooringRules? mooring = null, LayupRules? layup = null,
        SaleRules? sale = null, CalendarRules? calendar = null, OrderRules? orders = null, FuelRules? fuel = null, NightRules? night = null,
        IEnumerable<UpgradeRule>? upgrades = null, ReputationRules? reputation = null, Currency? currency = null, CloseCallRules? closeCalls = null, InstrumentRules? instruments = null)
    {
        Currency = currency ?? Currency.Default;
        Reputation = reputation ?? ReputationRules.Default;
        Fuel = fuel ?? FuelRules.Default;
        Night = night ?? NightRules.Default;
        CloseCalls = closeCalls ?? CloseCallRules.Default;
        Instruments = instruments ?? InstrumentRules.Default;
        Calendar = calendar ?? CalendarRules.Default;
        Orders = orders ?? OrderRules.Default;
        Sale = sale ?? SaleRules.Default;
        Layup = layup ?? LayupRules.Default;
        Yard = yard ?? YardRules.Default;
        Fouling = fouling ?? FoulingRules.Default;
        Mooring = mooring ?? MooringRules.Default;
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
        _components = components.ToList();
        _everAboard = _components.ToList();

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

        foreach (var c in _components)
        {
            if (!_byId.TryAdd(c.Id, c))
                throw new ArgumentException($"Duplicate component id '{c.Id}'.");
            Validate(c);
        }

        // Upgrades are components that are not aboard yet (design note 010, step 3): checked like any other, kept apart until fitted.
        foreach (var u in upgrades ?? [])
        {
            var c = u.Component;
            if (_byId.ContainsKey(c.Id) || !_upgrades.TryAdd(c.Id, u))
                throw new ArgumentException($"Duplicate component id '{c.Id}'.");
            Validate(c);
            foreach (var extra in u.Also)
            {
                if (_byId.ContainsKey(extra.Id) || _upgrades.ContainsKey(extra.Id) || _upgrades.Values.Any(o => o != u && o.Also.Any(a => a.Id == extra.Id)) || extra.Id == c.Id)
                    throw new ArgumentException($"Duplicate component id '{extra.Id}'.");
                Validate(extra);
            }
            if (!_places.ContainsKey(u.PlaceId))
                throw new ArgumentException($"Upgrade '{c.Id}' is fitted at unknown place '{u.PlaceId}'.");
            if ((u.YardNok is null) != (u.YardDays is null))
                throw new ArgumentException($"Upgrade '{c.Id}' needs both yardNok and yardDays, or neither.");
            foreach (var old in u.Replaces)
                if (!_byId.ContainsKey(old))
                    throw new ArgumentException($"Upgrade '{c.Id}' replaces '{old}', which is not a component of the boat.");
        }
    }

    private void Validate(Component c)
    {
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

    public string Name { get; }
    /// <summary>How money is written (display only; one abstract unit). From economy.json.</summary>
    public Currency Currency { get; }
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
    public YardRules Yard { get; }
    public FoulingRules Fouling { get; }
    public MooringRules Mooring { get; }
    public LayupRules Layup { get; }
    public SaleRules Sale { get; }
    public CalendarRules Calendar { get; }
    public OrderRules Orders { get; }
    public FuelRules Fuel { get; }
    public NightRules Night { get; }
    public CloseCallRules CloseCalls { get; }
    public InstrumentRules Instruments { get; }
    public ReputationRules Reputation { get; }
    /// <summary>Null: this boat has no surveyor.</summary>
    public SurveyData? Survey { get; }
    /// <summary>Null: no poor-job rule.</summary>
    public DiyQuality? DiyQuality { get; }
    /// <summary>What is aboard. Grows at the end when an upgrade is fitted; nothing before the fit sees an upgrade.</summary>
    public IReadOnlyList<Component> Components => _components;
    /// <summary>The upgrades the boat can be given, fitted or not, in data order.</summary>
    public IReadOnlyCollection<UpgradeRule> Upgrades => _upgrades.Values;
    public IReadOnlyList<Place> Places { get; }
    public IReadOnlyList<Cover> Covers { get; }
    public IReadOnlyList<Position> Positions { get; }

    public Component Get(string id) =>
        _byId.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"No component '{id}'.");

    /// <summary>The upgrade has been fitted (it is among the components).</summary>
    public bool IsFitted(string id) => _upgrades.ContainsKey(id) && _byId.ContainsKey(id);

    public bool TryGetUpgrade(string id, out UpgradeRule upgrade) => _upgrades.TryGetValue(id, out upgrade!);

    /// <summary>
    /// Appends the upgrade to the end of the components and registers it by id; the components it replaces leave the boat (they are kept in
    /// <see cref="Retired"/>). Throws if it is unknown or fitted already.
    /// </summary>
    internal void Fit(string id)
    {
        if (!_upgrades.TryGetValue(id, out var u)) throw new KeyNotFoundException($"No upgrade '{id}'.");
        if (_byId.ContainsKey(id)) throw new InvalidOperationException($"Upgrade '{id}' is fitted already.");
        foreach (var old in u.Replaces)
            if (_byId.Remove(old, out var gone))
            {
                _components.Remove(gone);
                _retired.Add(gone);
            }
        foreach (var part in u.Also.Prepend(u.Component))
        {
            _components.Add(part);
            _everAboard.Add(part);
            _byId[part.Id] = part;
        }
    }

    public bool TryGet(string id, out Component c) => _byId.TryGetValue(id, out c!);

    public Place GetPlace(string id) =>
        _places.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"No place '{id}'.");

    public Cover GetCover(string id) =>
        _covers.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"No cover '{id}'.");

    public Position GetPosition(string id) =>
        _positions.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"No position '{id}'.");

    public IEnumerable<Fault> AllFaults => Components.SelectMany(c => c.Faults);

    /// <summary>The components an upgrade has taken off the boat (the old heads when the new ones went in). Not aboard; their faults went with them.</summary>
    public IReadOnlyList<Component> Retired => _retired;

    /// <summary>
    /// Every fault in the order the components first came aboard, retired ones included (a retired one is Fixed). A fit does not move the
    /// index of any fault, so a seeded roll indexed by fault stays the same when an upgrade replaces a part.
    /// </summary>
    public IEnumerable<Fault> AllFaultsEver => _everAboard.SelectMany(c => c.Faults);
}
