namespace Haulout.Sim.Components;

/// <summary>A small part that can be pulled back to see underneath (a boot, a cap).</summary>
public sealed record RemovablePart(
    string Name, IReadOnlyList<string> Aliases, string UncoveredText, string? UncoverText = null, string? RefitText = null);

/// <summary>
/// The verbs a component offers, by what kind of part it is (design note 002, finding 2). A verb is never offered because of a fault:
/// two parts of one kind offer the same verbs whatever state they are in.
/// </summary>
public sealed record ComponentKind(string Id, IReadOnlySet<string> Verbs);

/// <summary>The names of the verbs a kind can declare.</summary>
public static class Verb
{
    public const string Inspect = "inspect";
    public const string Uncover = "uncover";
    public const string Measure = "measure";
    public const string Clean = "clean";
    public const string Replace = "replace";
    public const string Charge = "charge";
    public const string CheckFilterBowl = "check-filter-bowl";
    public const string CheckRawWaterFlow = "check-raw-water-flow";

    public static readonly IReadOnlyList<string> All =
        [Inspect, Uncover, Measure, Clean, Replace, Charge, CheckFilterBowl, CheckRawWaterFlow];
}

public sealed record ComponentSpec(
    Subsystem Subsystem,
    string PositionId,
    IReadOnlyList<ReachEntry> Reach,
    IReadOnlyList<string> Aliases,
    string Description,
    double? SeriesOhms,
    ComponentKind? Kind,
    int ReplaceCostNok,
    int ReplaceMinutes,
    IReadOnlyDictionary<string, double> Properties,
    RemovablePart? RemovablePart = null,
    bool BelowWaterline = false,
    string? ReplaceText = null,
    bool NeedsDry = false)
{
    public static ComponentSpec Default { get; } = new(
        Subsystem.Engine, "", [], [], "", null, null, 0, 0, new Dictionary<string, double>());
}
