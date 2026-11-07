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

public sealed record StartAttemptView(string Outcome, string Description, bool LabouredCrank) : Observation;

public sealed record FilterBowlView(string Text) : Observation;

public sealed record RawWaterFlowView(string Text) : Observation;

public sealed record RepairView(string ComponentName, string Text) : Observation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BriefingView), "briefing-view")]
[JsonDerivedType(typeof(StatusView), "status-view")]
[JsonDerivedType(typeof(LogView), "log-view")]
[JsonDerivedType(typeof(CompartmentsView), "compartments-view")]
[JsonDerivedType(typeof(CommandList), "command-list")]
[JsonDerivedType(typeof(AboardView), "aboard-view")]
[JsonDerivedType(typeof(ComponentActionsView), "component-actions-view")]
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
    IReadOnlyList<KnownFault> DiscoveredFaults,
    IReadOnlyList<string> Tools) : QueryResult;

public sealed record LogEntry(int Day, string TimeOfDay, string Text);

public sealed record LogView(IReadOnlyList<LogEntry> Entries) : QueryResult;

/// <summary>The Compartments query answer (the CompartmentList observation is a separate type).</summary>
public sealed record CompartmentsView(IReadOnlyList<CompartmentSummary> Compartments) : QueryResult;

public sealed record CommandInfo(string Verb, string? Argument, string Description);

public sealed record CommandList(IReadOnlyList<CommandInfo> Commands) : QueryResult;

/// <summary>A component the player has found. HasKnownFault: a discovered fault is still present on it.</summary>
public sealed record FoundComponent(string Id, string Name, bool HasKnownFault);

/// <summary>Opened: the player has opened it, or it is in plain view and they have looked around.</summary>
public sealed record AboardCompartment(string Id, string Name, bool Opened, IReadOnlyList<FoundComponent> Components);

public sealed record AboardView(IReadOnlyList<AboardCompartment> Compartments) : QueryResult;

/// <summary>
/// One verb on a component. Verb is the Command it maps to: "inspect" | "measure" | "clean" | "replace" | "charge"
/// | "check-filter-bowl" | "check-raw-water-flow". UnavailableReason is set when Available is false.
/// </summary>
public sealed record ActionOption(string Verb, string Label, bool Available, string? UnavailableReason, int Minutes, int CostNok);

/// <summary>Found is false (and Options empty) when the player has not found anything by that name.</summary>
public sealed record ComponentActionsView(string Component, bool Found, string? ComponentName, IReadOnlyList<ActionOption> Options) : QueryResult;
