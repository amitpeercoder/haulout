namespace Haulout.Sim.Components;

public sealed record ComponentSpec(
    Subsystem Subsystem,
    string Location,
    IReadOnlyList<string> Aliases,
    string Description,
    double? SeriesOhms,
    bool Cleanable,
    int ReplaceCostNok,
    int ReplaceMinutes,
    IReadOnlyDictionary<string, double> Properties)
{
    public static ComponentSpec Default { get; } = new(
        Subsystem.Engine, "", [], "", null, false, 0, 0, new Dictionary<string, double>());
}
