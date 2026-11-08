using Haulout.Sim.Faults;

namespace Haulout.Sim.Components;

/// <summary>
/// A physical part of the boat with a condition that degrades.
/// Condition is 0..1; 1 is new. What condition means for symptoms is the
/// job of each system's rules, not of this class.
/// </summary>
public sealed class Component
{
    private readonly List<Fault> _faults;

    public Component(string id, string name, double condition, WearRate wear)
        : this(id, name, condition, wear, ComponentSpec.Default, null)
    {
    }

    public Component(string id, string name, double condition, WearRate wear, ComponentSpec spec, IEnumerable<Fault>? faults)
    {
        if (condition is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(condition));
        Id = id;
        Name = name;
        Condition = condition;
        Wear = wear;
        Spec = spec;
        _faults = (faults ?? []).ToList();
        State = new Dictionary<string, double>(spec.Properties);
    }

    public string Id { get; }
    public string Name { get; }
    public double Condition { get; private set; }
    public WearRate Wear { get; }
    public ComponentSpec Spec { get; }

    public Subsystem Subsystem => Spec.Subsystem;
    public string PositionId => Spec.PositionId;
    public IReadOnlyList<ReachEntry> Reach => Spec.Reach;
    public IReadOnlyList<string> Aliases => Spec.Aliases;
    public string Description => Spec.Description;
    public double? SeriesOhms => Spec.SeriesOhms;
    public ComponentKind? Kind => Spec.Kind;
    /// <summary>Whether this kind of part offers the verb (one source of truth for what can be done to it).</summary>
    public bool HasVerb(string verb) => Spec.Kind?.Verbs.Contains(verb) ?? verb == Verb.Inspect;
    public int ReplaceCostNok => Spec.ReplaceCostNok;
    public int ReplaceMinutes => Spec.ReplaceMinutes;
    public IReadOnlyDictionary<string, double> Properties => Spec.Properties;
    public RemovablePart? RemovablePart => Spec.RemovablePart;
    public bool BelowWaterline => Spec.BelowWaterline;
    /// <summary>The narrative of Replace; null: the default "You fit a new {name}."</summary>
    public string? ReplaceText => Spec.ReplaceText;

    /// <summary>
    /// The live values of the component's properties (a battery's charge, a pump's cycle count). Starts as a copy of
    /// <see cref="Properties"/>; the systems write it; <see cref="Replace"/> puts it back to the spec.
    /// </summary>
    public Dictionary<string, double> State { get; }

    public IReadOnlyList<Fault> Faults => _faults;
    public IEnumerable<Fault> ActiveFaults => _faults.Where(f => f.Present);

    /// <summary>Adds a fault that arises during play (poor work of the player's own).</summary>
    internal void AddFault(Fault fault)
    {
        if (fault.ComponentId != Id) throw new ArgumentException($"Fault '{fault.Id}' belongs to '{fault.ComponentId}', not '{Id}'.", nameof(fault));
        _faults.Add(fault);
    }

    /// <summary>Advance wear by elapsed calendar days and engine/use hours.</summary>
    public void Age(int days, double useHours)
    {
        var loss = days * Wear.PerDay + useHours * Wear.PerUseHour;
        Condition = Math.Max(0, Condition - loss);
    }

    public void Replace()
    {
        foreach (var fault in _faults.Where(f => f.Present).ToList()) fault.Clear();
        Condition = 1;
        State.Clear();
        foreach (var (key, value) in Spec.Properties) State[key] = value;
    }
}

/// <summary>Condition lost per calendar day (UV, salt, time) and per hour of use.</summary>
public readonly record struct WearRate(double PerDay, double PerUseHour)
{
    /// <summary>Lasts roughly <paramref name="years"/> of calendar time if never used.</summary>
    public static WearRate CalendarYears(double years, double perUseHour = 0) =>
        new(1.0 / (years * 360), perUseHour);
}
