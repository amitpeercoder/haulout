using System.Text.Json.Serialization;

namespace Haulout.Sim.Play;

public sealed record KnownFault(string Id, string Name, string ComponentName);

public sealed record CompartmentSummary(string Id, string Name);

public sealed record SeenComponent(string Id, string Name);

public sealed record CommandResult(
    bool Accepted,
    string Action,
    IReadOnlyList<string> Narrative,
    int MinutesTaken,
    int CostNok,
    Observation? Observation,
    IReadOnlyList<KnownFault> NewlyDiscovered,
    bool EngineRunning,
    bool Finished);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CompartmentList), "compartment-list")]
[JsonDerivedType(typeof(CompartmentView), "compartment-view")]
[JsonDerivedType(typeof(InspectionView), "inspection-view")]
[JsonDerivedType(typeof(MeterReading), "meter-reading")]
[JsonDerivedType(typeof(StartAttemptView), "start-attempt-view")]
[JsonDerivedType(typeof(FilterBowlView), "filter-bowl-view")]
[JsonDerivedType(typeof(RawWaterFlowView), "raw-water-flow-view")]
[JsonDerivedType(typeof(RepairView), "repair-view")]
public abstract record Observation;

public sealed record CompartmentList(IReadOnlyList<CompartmentSummary> Compartments) : Observation;

public sealed record CompartmentView(string Id, string Name, string Description, IReadOnlyList<SeenComponent> Components) : Observation;

public sealed record InspectionView(string ComponentName, string Text) : Observation;

/// <summary>
/// Kind: "volts-resting" | "volts-cranking" | "volts-drop" | "volts-at-starter" | "ohms".
/// CrankingVolts is only set on the start battery's reading: the terminal voltage while cranking.
/// </summary>
public sealed record MeterReading(string ComponentName, string Kind, double? Value, string Unit, string Text, double? CrankingVolts = null) : Observation;

public sealed record StartAttemptView(string Outcome, string Description) : Observation;

public sealed record FilterBowlView(string Text) : Observation;

public sealed record RawWaterFlowView(string Text) : Observation;

public sealed record RepairView(string ComponentName, string Text) : Observation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BriefingView), "briefing-view")]
[JsonDerivedType(typeof(StatusView), "status-view")]
[JsonDerivedType(typeof(LogView), "log-view")]
[JsonDerivedType(typeof(CompartmentsView), "compartments-view")]
[JsonDerivedType(typeof(CommandList), "command-list")]
public abstract record QueryResult;

public sealed record BriefingView(IReadOnlyList<string> Lines) : QueryResult;

public sealed record StatusView(
    int Day,
    string TimeOfDay,
    int MinutesSpent,
    int MoneySpentNok,
    string? LastStartOutcome,
    bool EngineRunning,
    bool Finished,
    IReadOnlyList<KnownFault> DiscoveredFaults) : QueryResult;

public sealed record LogEntry(int Day, string TimeOfDay, string Text);

public sealed record LogView(IReadOnlyList<LogEntry> Entries) : QueryResult;

/// <summary>The Compartments query answer (the CompartmentList observation is a separate type).</summary>
public sealed record CompartmentsView(IReadOnlyList<CompartmentSummary> Compartments) : QueryResult;

public sealed record CommandInfo(string Verb, string? Argument, string Description);

public sealed record CommandList(IReadOnlyList<CommandInfo> Commands) : QueryResult;
