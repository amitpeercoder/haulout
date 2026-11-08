using System.Text.Json.Serialization;

namespace Haulout.Sim.Play;

/// <summary>Id is for in-process use only (tests, history); it is never serialised, so no adapter can leak it.</summary>
public sealed record KnownFault([property: JsonIgnore] string Id, string Name, string ComponentName);

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
[JsonDerivedType(typeof(LaunchView), "launch-view")]
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

/// <summary>The closing card of the pre-launch slice. DeferredCount: problems the player deferred; OpenCount: discovered problems still open (deferred included).</summary>
public sealed record LaunchView(int Day, int DeadlineDay, int DeferredCount, int OpenCount, IReadOnlyList<string> Lines) : Observation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BriefingView), "briefing-view")]
[JsonDerivedType(typeof(StatusView), "status-view")]
[JsonDerivedType(typeof(LogView), "log-view")]
[JsonDerivedType(typeof(CommandList), "command-list")]
[JsonDerivedType(typeof(AboardView), "aboard-view")]
[JsonDerivedType(typeof(ComponentActionsView), "component-actions-view")]
[JsonDerivedType(typeof(SurveyReportView), "survey-report-view")]
[JsonDerivedType(typeof(ReadinessView), "readiness-view")]
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
    string Place,
    int BalanceNok = 0,
    int LaunchDeadlineDay = 0,
    bool Launched = false) : QueryResult;

/// <summary>CostNok is what the entry cost the player (a receipt), 0 when it cost nothing.</summary>
/// <summary>Document: set when the entry is a document the player can read again (e.g. "survey report").</summary>
public sealed record LogEntry(int Day, string TimeOfDay, string Text, int CostNok = 0, string? Document = null);

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
/// Flagged: a surveyor's "recommend investigation" points at it and the cause is not yet found.
public sealed record FoundComponent(string Name, string Position, bool KnownProblem, bool VisibleHere, bool ReachableHere, bool Flagged = false);

/// <summary>Place: the name of the place the player is in. No ids anywhere.</summary>
public sealed record AboardView(
    string Place, IReadOnlyList<AboardPlace> Places, IReadOnlyList<AboardCover> Covers, IReadOnlyList<FoundComponent> Components) : QueryResult;

/// <summary>
/// One verb on a component. Verb is the Command it maps to: "inspect" | "uncover" | "measure" | "clean" | "replace" | "charge"
/// | "check-filter-bowl" | "check-raw-water-flow" | "defer" | "undefer" | "yard-fix". UnavailableReason is set when Available is false.
/// Target: for the problem verbs (defer, undefer, yard-fix), the problem's name to pass as the command's argument; null otherwise.
/// </summary>
public sealed record ActionOption(string Verb, string Label, bool Available, string? UnavailableReason, int Minutes, int CostNok, string? Target = null);

/// <summary>Found is false (and Options empty) when the player has not found anything by that name.</summary>
public sealed record ComponentActionsView(string Component, bool Found, string? ComponentName, IReadOnlyList<ActionOption> Options) : QueryResult;

/// <summary>Severity in the surveyor's words: "Note" | "Attention" | "Before launch".</summary>
public sealed record SurveyItemView(int Number, string Component, string Severity, string Text);

/// <summary>
/// The survey report as a letter. Ordered false: no survey yet (other fields empty). Day: when it was written.
/// No ids, no fault names beyond what the surveyor writes.
/// </summary>
public sealed record SurveyReportView(
    bool Ordered, int Day, string Surveyor, string BoatName, int FeeNok,
    IReadOnlyList<string> Opening, IReadOnlyList<SurveyItemView> Items, IReadOnlyList<string> Closing) : QueryResult;

/// <summary>One way to fix a problem. Route: "DIY" | "Yard". How: plain words. Minutes: your time (DIY); LeadDays: the yard's.
/// Available false with a reason, e.g. afloat for below-waterline work, or not enough money.</summary>
public sealed record FixRoute(string Route, string How, int CostNok, int Minutes, int LeadDays, bool Available, string? UnavailableReason);

/// <summary>
/// A discovered problem that is still open. State: "Discovered" | "Deferred". CostNowNok: cheapest route now.
/// CostLaterNok: the likely bill if it is left until after launch; LaterNote e.g. "needs an emergency lift".
/// OnHardOnly: can only be fixed with her out of the water. Consequence: one line in plain words.
/// </summary>
public sealed record ReadinessItem(
    string Problem, string Component, string State, int CostNowNok, int CostLaterNok, string? LaterNote,
    bool OnHardOnly, string Consequence, IReadOnlyList<FixRoute> Routes);

/// <summary>A surveyor's pointer the player has not run down yet: the part and the surveyor's words. Not a fault.</summary>
public sealed record InvestigateItem(string Component, string Note);

/// <summary>
/// The launch picture. Only discovered problems, ever. Fixed: names of discovered problems that are now fixed or ruled out (history).
/// DaysToDeadline: negative once past it.
/// </summary>
public sealed record ReadinessView(
    int Day, int LaunchDeadlineDay, int DaysToDeadline, int BalanceNok, bool Launched,
    IReadOnlyList<ReadinessItem> Open, IReadOnlyList<InvestigateItem> ToInvestigate, IReadOnlyList<string> Fixed) : QueryResult;
