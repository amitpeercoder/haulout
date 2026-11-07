namespace Haulout.Sim.Faults;

public enum FaultEffectKind { SeriesOhms, StateOfCharge, FuelRestriction, RawWaterFlowFactor, OpenCircuit, NoFuel }

public sealed record FaultEffect(FaultEffectKind Kind, double Value);

public enum PlayerAction { Look, Inspect, Measure, Start, Clean, Replace, Charge, CheckFilterBowl, CheckRawWaterFlow }

/// <summary>What the player learns from an action. Discovers=false is a hint only.</summary>
public sealed record FaultReveal(PlayerAction Action, string Text, bool Discovers, bool NeedsUncovered = false);

public sealed class Fault
{
    public Fault(
        string id,
        string name,
        string componentId,
        FaultEffect effect,
        IEnumerable<FaultReveal>? reveals = null,
        IEnumerable<PlayerAction>? fixedBy = null,
        bool present = true)
    {
        Id = id;
        Name = name;
        ComponentId = componentId;
        Effect = effect;
        Reveals = (reveals ?? []).ToList();
        FixedBy = (fixedBy ?? []).ToList();
        Present = present;
    }

    public string Id { get; }
    /// <summary>What a mechanic calls it.</summary>
    public string Name { get; }
    public string ComponentId { get; }
    public FaultEffect Effect { get; }
    public IReadOnlyList<FaultReveal> Reveals { get; }
    public IReadOnlyList<PlayerAction> FixedBy { get; }
    public bool Present { get; private set; }
    public bool Discovered { get; private set; }

    public void Discover() => Discovered = true;

    /// <summary>The fault is gone. Discovered stays as is: the player still knows it happened.</summary>
    public void Clear() => Present = false;

    public FaultReveal? RevealFor(PlayerAction a, bool uncovered = false) =>
        Reveals.FirstOrDefault(r => r.Action == a && (!r.NeedsUncovered || uncovered));
}
