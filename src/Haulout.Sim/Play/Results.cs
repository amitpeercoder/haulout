using System.Text.Json.Serialization;

namespace Haulout.Sim.Play;

public sealed record KnownFault(string Id, string Name, string ComponentName);

/// <summary>A cover as the player sees it at a place. Removed: it is off.</summary>
public sealed record CoverState(string Name, bool Removed);

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
[JsonDerivedType(typeof(PlaceView), "place-view")]
[JsonDerivedType(typeof(CoverView), "cover-view")]
[JsonDerivedType(typeof(InspectionView), "inspection-view")]
[JsonDerivedType(typeof(MeterReading), "meter-reading")]
[JsonDerivedType(typeof(StartAttemptView), "start-attempt-view")]
[JsonDerivedType(typeof(FilterBowlView), "filter-bowl-view")]
[JsonDerivedType(typeof(RawWaterFlowView), "raw-water-flow-view")]
[JsonDerivedType(typeof(RepairView), "repair-view")]
public abstract record Observation;

/// <summary>
/// What the player perceives at a place (Look and GoTo). Visible and Reachable are component names, given the covers.
/// Exits are the names of adjacent places that can be gone to now.
/// </summary>
public sealed record PlaceView(
    string Place, string Description, IReadOnlyList<CoverState> Covers,
    IReadOnlyList<string> Visible, IReadOnlyList<string> Reachable, IReadOnlyList<string> Exits) : Observation;

/// <summary>Remove or Refit. NowVisible: component names that came into view by removing it (empty on refit).</summary>
public sealed record CoverView(string Cover, string Place, bool Removed, IReadOnlyList<string> NowVisible) : Observation;

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
    IReadOnlyList<string> Tools,
    string Place) : QueryResult;

/// <summary>CostNok is what the entry cost the player (a receipt), 0 when it cost nothing.</summary>
public sealed record LogEntry(int Day, string TimeOfDay, string Text, int CostNok = 0);

public sealed record LogView(IReadOnlyList<LogEntry> Entries) : QueryResult;

public sealed record CommandInfo(string Verb, string? Argument, string Description);

public sealed record CommandList(IReadOnlyList<CommandInfo> Commands) : QueryResult;

/// <summary>
/// A place aboard. Here: the player is in it. Visited: the player has been there (the start place counts once looked at).
/// Available: it can be gone to at all (beside the hull only while she is on the hard). Adjacent: names of neighbouring places.
/// </summary>
public sealed record AboardPlace(string Name, bool Here, bool Visited, bool Available, IReadOnlyList<string> Adjacent);

/// <summary>A cover, the name of the place it is removed from, and whether it is off.</summary>
public sealed record AboardCover(string Name, string Place, bool Removed);

/// <summary>
/// A component the player has found. Position: its true spot, as a label ("Under the quarter berth").
/// KnownProblem: a discovered problem is still present on it. VisibleHere / ReachableHere: from the current place, given the covers.
/// </summary>
public sealed record FoundComponent(string Name, string Position, bool KnownProblem, bool VisibleHere, bool ReachableHere);

/// <summary>Place: the name of the place the player is in. No ids anywhere.</summary>
public sealed record AboardView(
    string Place, IReadOnlyList<AboardPlace> Places, IReadOnlyList<AboardCover> Covers, IReadOnlyList<FoundComponent> Components) : QueryResult;

/// <summary>
/// One verb on a component. Verb is the Command it maps to: "inspect" | "uncover" | "measure" | "clean" | "replace" | "charge"
/// | "check-filter-bowl" | "check-raw-water-flow". UnavailableReason is set when Available is false.
/// </summary>
public sealed record ActionOption(string Verb, string Label, bool Available, string? UnavailableReason, int Minutes, int CostNok);

/// <summary>Found is false (and Options empty) when the player has not found anything by that name.</summary>
public sealed record ComponentActionsView(string Component, bool Found, string? ComponentName, IReadOnlyList<ActionOption> Options) : QueryResult;
