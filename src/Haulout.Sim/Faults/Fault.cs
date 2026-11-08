namespace Haulout.Sim.Faults;

public enum FaultEffectKind { SeriesOhms, StateOfCharge, FuelRestriction, RawWaterFlowFactor, OpenCircuit, NoFuel, None }

public sealed record FaultEffect(FaultEffectKind Kind, double Value);

public enum PlayerAction { Look, Inspect, Measure, Start, Clean, Replace, Charge, CheckFilterBowl, CheckRawWaterFlow }

/// <summary>What the player learns from an action. Discovers=false is a hint only.</summary>
public sealed record FaultReveal(PlayerAction Action, string Text, bool Discovers, bool NeedsUncovered = false);

/// <summary>Where a fault stands. Discovered stays true on the fault itself after it is fixed (history).</summary>
public enum FaultState { Latent, Symptomatic, Discovered, Deferred, Fixed }

/// <summary>Doing it yourself: the plan, parts money, your time.</summary>
public sealed record DiyRoute(string How, int PartsNok, int Minutes);

/// <summary>Paying the yard: their plan, their price, their lead time in days.</summary>
public sealed record YardRoute(string How, int Nok, int LeadDays);

/// <summary>
/// What a fault turns into. To: id of a (dormant) fault on the same component.
/// AfterDays: escalates that many days after the fault became active (whatever its state); null: never on the clock.
/// OnLaunch: escalates the moment she is launched. Text: what the player reads when it happens.
/// </summary>
public sealed record Escalation(string To, int? AfterDays, bool OnLaunch, string Text);

public sealed class Fault
{
    public Fault(
        string id,
        string name,
        string componentId,
        FaultEffect effect,
        IEnumerable<FaultReveal>? reveals = null,
        IEnumerable<PlayerAction>? fixedBy = null,
        bool present = true,
        bool onHardOnly = false,
        bool harmless = false,
        bool dormant = false,
        string consequence = "",
        int costLaterNok = 0,
        string? laterNote = null,
        DiyRoute? diy = null,
        YardRoute? yard = null,
        Escalation? escalation = null,
        IReadOnlyDictionary<string, double>? properties = null)
    {
        Id = id;
        Name = name;
        ComponentId = componentId;
        Effect = effect;
        Reveals = (reveals ?? []).ToList();
        FixedBy = (fixedBy ?? []).ToList();
        OnHardOnly = onHardOnly;
        Harmless = harmless;
        Dormant = dormant || !present;
        Consequence = consequence;
        CostLaterNok = costLaterNok;
        LaterNote = laterNote;
        Diy = diy;
        Yard = yard;
        Escalation = escalation;
        Properties = properties ?? new Dictionary<string, double>();
    }

    public string Id { get; }
    /// <summary>What a mechanic calls it.</summary>
    public string Name { get; }
    public string ComponentId { get; }
    public FaultEffect Effect { get; }
    public IReadOnlyList<FaultReveal> Reveals { get; }
    public IReadOnlyList<PlayerAction> FixedBy { get; }
    public bool OnHardOnly { get; }
    /// <summary>A red herring: when found it is ruled out at once.</summary>
    public bool Harmless { get; }
    /// <summary>A successor fault: inert until its predecessor escalates.</summary>
    public bool Dormant { get; private set; }
    public string Consequence { get; }
    public int CostLaterNok { get; }
    public string? LaterNote { get; }
    public DiyRoute? Diy { get; }
    public YardRoute? Yard { get; }
    public Escalation? Escalation { get; }
    public IReadOnlyDictionary<string, double> Properties { get; }

    public FaultState State { get; private set; } = FaultState.Latent;
    /// <summary>The day the player deferred it (set while Deferred).</summary>
    public int? DeferredDay { get; private set; }
    /// <summary>The day the fault became active: the game's start day, or the day a successor or a poor job woke. Null while dormant.</summary>
    public int? ActiveSinceDay { get; private set; }

    /// <summary>It turned into its successor; it no longer counts as present, nor as fixed.</summary>
    internal bool Escalated { get; private set; }

    /// <summary>Not dormant, not escalated, not fixed.</summary>
    public bool Present => !Dormant && !Escalated && State != FaultState.Fixed;

    /// <summary>True once the player has found it; stays true after it is fixed.</summary>
    public bool Discovered { get; private set; }

    public void Discover()
    {
        if (Dormant) return;
        Discovered = true;
        if (State is FaultState.Latent or FaultState.Symptomatic) State = FaultState.Discovered;
    }

    /// <summary>A hint was seen: Latent becomes Symptomatic.</summary>
    public void MarkSymptomatic()
    {
        if (Present && State == FaultState.Latent) State = FaultState.Symptomatic;
    }

    /// <summary>The fault is gone. Discovered stays as is: the player still knows it happened. Does nothing to a fault that is not present.</summary>
    public void Clear()
    {
        if (!Present) return;
        State = FaultState.Fixed;
        DeferredDay = null;
    }

    public void Defer(int day)
    {
        if (State != FaultState.Discovered) return;
        State = FaultState.Deferred;
        DeferredDay = day;
    }

    public void Undefer()
    {
        if (State != FaultState.Deferred) return;
        State = FaultState.Discovered;
        DeferredDay = null;
    }

    internal void MarkEscalated() => Escalated = true;

    /// <summary>The predecessor escalated: this one is live now, and Latent.</summary>
    internal void Wake(int day)
    {
        Dormant = false;
        ActiveSinceDay = day;
    }

    /// <summary>A fault present from the start, or added during play, starts ageing on this day.</summary>
    internal void ActivateOn(int day)
    {
        if (!Dormant) ActiveSinceDay ??= day;
    }

    public FaultReveal? RevealFor(PlayerAction a, bool uncovered = false) =>
        Reveals.FirstOrDefault(r => r.Action == a && (!r.NeedsUncovered || uncovered));
}
