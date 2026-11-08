namespace Haulout.Sim.Components;

/// <summary>A small part that can be pulled back to see underneath (a boot, a cap).</summary>
public sealed record RemovablePart(
    string Name, IReadOnlyList<string> Aliases, string UncoveredText, string? UncoverText = null, string? RefitText = null);

public sealed record ComponentSpec(
    Subsystem Subsystem,
    string PositionId,
    IReadOnlyList<ReachEntry> Reach,
    IReadOnlyList<string> Aliases,
    string Description,
    double? SeriesOhms,
    bool Cleanable,
    int ReplaceCostNok,
    int ReplaceMinutes,
    IReadOnlyDictionary<string, double> Properties,
    RemovablePart? RemovablePart = null,
    bool BelowWaterline = false,
    string? ReplaceText = null,
    bool NeedsDry = false)
{
    public static ComponentSpec Default { get; } = new(
        Subsystem.Engine, "", [], [], "", null, false, 0, 0, new Dictionary<string, double>());
}
