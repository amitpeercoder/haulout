namespace Haulout.Sim.Faults;

public enum FaultEffectKind { SeriesOhms, StateOfCharge, FuelRestriction, RawWaterFlowFactor, OpenCircuit, NoFuel, None, Property }

/// <summary>How a property effect combines with the value so far: replace it, add to it, scale it, or cap it from above.</summary>
public enum PropertyOp { Set, Add, Multiply, Min }

/// <summary>
/// What a fault does to the boat. The older kinds are read where they matter (the engine inputs). Kind <see cref="FaultEffectKind.Property"/>
/// is the general form (design note 006): Property, Op and Value change a named property of Component (default: the fault's own component);
/// <see cref="Systems.Properties.Effective"/> applies them.
/// </summary>
public sealed record FaultEffect(
    FaultEffectKind Kind, double Value, string? Property = null, PropertyOp? Op = null, string? Component = null);

/// <summary>
/// A dormant fault wakes, once, when the named system value reaches AtLeast (for example "water.awashHours").
/// Spread (design note 008): the threshold is AtLeast times a seeded factor of this fault, so no two faults wake at the same count.
/// Repeats: once the woken fault is Fixed the component gets a dormant copy of it, armed from the values of that moment, so next winter
/// can do it again. Cause: the sentence told when the player finds it ("{n}" is the count the value had passed, "{s}" a plural s).
/// </summary>
public sealed record SpawnRule(string When, double AtLeast, bool Spread = false, bool Repeats = false, string? Cause = null);

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
/// It happens when the fault's progression crosses the threshold (see <see cref="ProgressionSpec"/>).
/// OnLaunch: escalates the moment she is launched. Text: what the player reads when it happens.
/// </summary>
public sealed record Escalation(string To, bool OnLaunch, string Text);

/// <summary>What a known fault means on a trip, in plain words, and the chandlery item (id) that answers it, if any.</summary>
public sealed record AtSeaSpec(string Text, string? Spare = null);

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
        IReadOnlyDictionary<string, double>? properties = null,
        ProgressionSpec? progression = null,
        IEnumerable<FaultEffect>? effects = null,
        IEnumerable<SpawnRule>? spawn = null,
        string? arrivalSign = null,
        AtSeaSpec? atSea = null,
        string? failsWhen = null,
        string? symptom = null)
    {
        AtSea = atSea;
        FailsWhen = failsWhen;
        Symptom = symptom;
        ArrivalSign = arrivalSign;
        Id = id;
        Name = name;
        ComponentId = componentId;
        var extra = (effects ?? []).ToList();
        Effect = effect.Kind == FaultEffectKind.None && extra.Count > 0 ? extra[0] : effect;
        Effects = (effect.Kind == FaultEffectKind.None ? extra : extra.Prepend(effect)).ToList();
        Spawn = (spawn ?? []).ToList();
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
        Progression = progression;
    }

    public string Id { get; }
    /// <summary>
    /// Design note 010, second pass G: an instrument's fault that does not stop it all the time. "rain": it stops working on a day of rain and
    /// works on a dry one. Null: the fault stops it always. The truth of the day decides, so the intermittence can be reasoned about.
    /// </summary>
    public string? FailsWhen { get; }
    /// <summary>What the player sees of it while it is stopping the instrument (the screen blank, the radar empty); told on a look and in the hour's report. Null: nothing.</summary>
    public string? Symptom { get; }
    /// <summary>The consequence at sea, for the trip plan; null: nothing to say. Never shown unless the fault is known.</summary>
    public AtSeaSpec? AtSea { get; }
    /// <summary>What a mechanic calls it.</summary>
    public string Name { get; }
    public string ComponentId { get; }
    /// <summary>The legacy single effect, or the first of <see cref="Effects"/> when there is no legacy one.</summary>
    public FaultEffect Effect { get; }
    /// <summary>Every effect, the legacy one first (when it is not None), then the listed ones in data order.</summary>
    public IReadOnlyList<FaultEffect> Effects { get; }
    /// <summary>Wakes the fault (dormant only) when any of these system values has been reached. Empty: it never spawns.</summary>
    public IReadOnlyList<SpawnRule> Spawn { get; }
    /// <summary>What the player perceives on coming back aboard after this fault woke while they were away (a hose seen through a gap); null: nothing until they look.</summary>
    public string? ArrivalSign { get; }
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
    public ProgressionSpec? Progression { get; }

    /// <summary>Accumulated progression toward escalation; escalates at the boat's threshold.</summary>
    public double Progress { get; internal set; }
    /// <summary>Drawn once when the fault became active, from the world random; null if it has no progression.</summary>
    public double? Spread { get; internal set; }
    public FaultHistory History { get; } = new();

    public FaultState State { get; private set; } = FaultState.Latent;
    /// <summary>The day the player deferred it (set while Deferred).</summary>
    public int? DeferredDay { get; private set; }
    /// <summary>The player has put it off at least once, even if they took it back or it has since escalated.</summary>
    public bool EverDeferred { get; private set; }
    /// <summary>A spawn rule woke it. Wakes only once, even if the fault is later fixed.</summary>
    public bool Spawned { get; private set; }
    /// <summary>The day the fault became active: the game's start day, or the day a successor or a poor job woke. Null while dormant.</summary>
    public int? ActiveSinceDay { get; private set; }

    /// <summary>The value of each spawn rule's system value when this fault was armed (a re-armed copy); subtracted before the threshold. Empty: 0.</summary>
    public IReadOnlyDictionary<string, double> SpawnBaseline { get; private set; } = new Dictionary<string, double>();
    /// <summary>A repeating fault that was fixed has already put its dormant copy on the component.</summary>
    internal bool Rearmed { get; set; }
    /// <summary>One of its spawn rules asks for a fresh copy once it is fixed.</summary>
    internal bool Repeats => Spawn.Any(r => r.Repeats);

    /// <summary>
    /// A dormant copy for next time: same name, reveals, routes and effects under a new id, armed from <paramref name="baseline"/>.
    /// </summary>
    internal Fault CloneDormant(string id, IReadOnlyDictionary<string, double> baseline) => new(
        id, Name, ComponentId, new FaultEffect(FaultEffectKind.None, 0), Reveals, FixedBy, present: true, OnHardOnly, Harmless, dormant: true,
        Consequence, CostLaterNok, LaterNote, Diy, Yard, Escalation, Properties, Progression, Effects, Spawn, ArrivalSign, AtSea, FailsWhen, Symptom)
    {
        SpawnBaseline = baseline,
    };

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
        EverDeferred = true;
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

    /// <summary>A system value crossed the fault's spawn threshold: it is live now, Latent, and never spawns again.</summary>
    internal void SpawnOn(int day)
    {
        Spawned = true;
        Wake(day);
    }

    /// <summary>A fault present from the start, or added during play, starts ageing on this day.</summary>
    internal void ActivateOn(int day)
    {
        if (!Dormant) ActiveSinceDay ??= day;
    }

    public FaultReveal? RevealFor(PlayerAction a, bool uncovered = false) =>
        Reveals.FirstOrDefault(r => r.Action == a && (!r.NeedsUncovered || uncovered));
}
