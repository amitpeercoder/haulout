using System.Text.Json.Serialization;

namespace Haulout.Sim.Play;

/// <summary>Id is for in-process use only (tests, history); it is never serialised, so no adapter can leak it.</summary>
/// <summary>A problem the player has found. Fixed: it has since been put right (it stays on the list as history, marked).</summary>
public sealed record KnownFault([property: JsonIgnore] string Id, string Name, string ComponentName, bool Fixed = false);

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
    bool Finished,
    IReadOnlyList<Alert> Alerts);

/// <summary>Something the boat has told the player: a lamp, a smell, a counter. Text is the player's own perception, never a fault name.</summary>
public sealed record Alert(int Day, string TimeOfDay, string Text);

/// <summary>The unread alerts, handed over once (the query marks them read).</summary>
public sealed record NoticesView(IReadOnlyList<Alert> Alerts) : QueryResult;

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
[JsonDerivedType(typeof(LossView), "loss-view")]
[JsonDerivedType(typeof(LegView), "leg-view")]
[JsonDerivedType(typeof(TripResultView), "trip-result-view")]
[JsonDerivedType(typeof(HauloutView), "haulout-view")]
[JsonDerivedType(typeof(ListingView), "listing-view")]
[JsonDerivedType(typeof(ClosingView), "closing-view")]
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
public sealed record MeterReading(string ComponentName, string Kind, double? Value, string Unit, string Text, double? CrankingVolts = null,
    CircuitGap? Gap = null) : Observation;

/// <summary>
/// What the player can add up once the starter has been measured while cranking (002 finding 4): GapVolts is the battery's
/// cranking voltage minus the starter's, from the same model; AccountedVolts is the sum of the drops the player has
/// measured today (as shown to them). It names no part.
/// </summary>
public sealed record CircuitGap(double GapVolts, double AccountedVolts)
{
    /// <summary>A sound chain loses about 1.2 V between the posts and the starter while cranking; up to this is ordinary, not a gap.</summary>
    public const double OrdinaryLossVolts = 2.0;

    /// <summary>Unaccounted volts at or below this read as "your readings account for it".</summary>
    public const double AccountedForToleranceVolts = 0.5;

    public double UnaccountedVolts => GapVolts - AccountedVolts;
    public bool Ordinary => GapVolts <= OrdinaryLossVolts;
    public bool AccountedFor => !Ordinary && UnaccountedVolts <= AccountedForToleranceVolts;
}

public sealed record StartAttemptView(string Outcome, string Description, bool LabouredCrank) : Observation;

public sealed record FilterBowlView(string Text) : Observation;

public sealed record RawWaterFlowView(string Text) : Observation;

public sealed record RepairView(string ComponentName, string Text) : Observation;

/// <summary>The closing card of the pre-launch slice. DeferredCount: problems the player deferred; OpenCount: discovered problems still open (deferred included).</summary>
public sealed record LaunchView(int Day, int DeadlineDay, int DeferredCount, int OpenCount, IReadOnlyList<string> Lines) : Observation;

/// <summary>
/// The closing card when she sinks. Lines: the card in the game's voice. ValueNok: what the insurer values her at;
/// PayoutNok: what they pay (0 when Voided, with VoidReason in plain words).
/// </summary>
public sealed record LossView(
    int Day, IReadOnlyList<string> Lines, int ValueNok, int PayoutNok, bool Voided, string? VoidReason) : Observation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BriefingView), "briefing-view")]
[JsonDerivedType(typeof(StatusView), "status-view")]
[JsonDerivedType(typeof(LogView), "log-view")]
[JsonDerivedType(typeof(CommandList), "command-list")]
[JsonDerivedType(typeof(AboardView), "aboard-view")]
[JsonDerivedType(typeof(ComponentActionsView), "component-actions-view")]
[JsonDerivedType(typeof(SurveyReportView), "survey-report-view")]
[JsonDerivedType(typeof(ReadinessView), "readiness-view")]
[JsonDerivedType(typeof(WeatherView), "weather-view")]
[JsonDerivedType(typeof(NoticesView), "notices-view")]
[JsonDerivedType(typeof(InventoryView), "inventory-view")]
[JsonDerivedType(typeof(TripPlanView), "trip-plan-view")]
[JsonDerivedType(typeof(TripView), "trip-view")]
[JsonDerivedType(typeof(SeasonView), "season-view")]
[JsonDerivedType(typeof(SaleView), "sale-view")]
[JsonDerivedType(typeof(ChartView), "chart-view")]
[JsonDerivedType(typeof(CalendarView), "calendar-view")]
[JsonDerivedType(typeof(UpgradesView), "upgrades-view")]
[JsonDerivedType(typeof(SeasonPageView), "season-page-view")]
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
    bool Launched = false,
    bool Afloat = false,
    string? Weather = null,
    bool Sunk = false,
    IReadOnlyList<Alert>? Notices = null,
    string? Port = null,
    bool AtSea = false,
    string? Season = null,
    bool Sold = false,
    string? SeasonLead = null,
    string? Week = null,
    int VacationDaysLeft = 0,
    string? Next = null,
    string? Fuel = null,
    string? Tired = null) : QueryResult;

/// <summary>CostNok is what the entry cost the player (a receipt), 0 when it cost nothing.</summary>
/// <summary>Document: set when the entry is a document the player can read again (e.g. "survey report").</summary>
public sealed record LogEntry(int Day, string TimeOfDay, string Text, int CostNok = 0, string? Document = null, bool Alert = false);

/// <summary>Weather: today's text for the header. Day: today (0 when unknown).</summary>
public sealed record LogView(IReadOnlyList<LogEntry> Entries, string? Weather = null, int Day = 0) : QueryResult;

/// <summary>Today's weather: the words and the numbers behind them. Wind from a compass word.</summary>
public sealed record WeatherView(int Day, string Text, int WindForce, string WindFrom, double RainMm, double TempC) : QueryResult;

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
/// Available false with a reason, e.g. afloat for below-waterline work, or not enough money. PartNote (DIY, a part the yard does not keep on the
/// shelf): where the part stands, "not on the yard's shelf: order it first, 3 to 10 days", "on order, due day N", "waiting for you at the yard"
/// or "the new part is aboard"; null for a part off the shelf.</summary>
public sealed record FixRoute(string Route, string How, int CostNok, int Minutes, int LeadDays, bool Available, string? UnavailableReason, string? PartNote = null);

/// <summary>
/// A discovered problem that is still open. State: "Discovered" | "Deferred". CostNowNok: cheapest route now.
/// CostLaterNok: the likely bill if it is left until after launch; LaterNote e.g. "needs an emergency lift".
/// LikelyMinDays / LikelyMaxDays: the range in which a known problem will probably get worse (null: no estimate, or it never does).
/// OnHardOnly: can only be fixed with her out of the water. Consequence: one line in plain words.
/// </summary>
public sealed record ReadinessItem(
    string Problem, string Component, string State, int CostNowNok, int CostLaterNok, string? LaterNote,
    bool OnHardOnly, string Consequence, IReadOnlyList<FixRoute> Routes,
    int? LikelyMinDays = null, int? LikelyMaxDays = null, bool EscalatesOnLaunch = false);

/// <summary>A surveyor's pointer the player has not run down yet: the part and the surveyor's words. Not a fault.</summary>
public sealed record InvestigateItem(string Component, string Note);

/// <summary>
/// The launch picture. Only discovered problems, ever. Fixed: names of discovered problems that are now fixed or ruled out (history).
/// DaysToDeadline: negative once past it. SeasonLead: once she has been launched, the season's business in a sentence (the booked slot, the
/// winter list); it replaces the launch deadline in the views. Null before the first launch and after a Keep, when the deadline is what is next.
/// </summary>
public sealed record ReadinessView(
    int Day, int LaunchDeadlineDay, int DaysToDeadline, int BalanceNok, bool Launched,
    IReadOnlyList<ReadinessItem> Open, IReadOnlyList<InvestigateItem> ToInvestigate, IReadOnlyList<string> Fixed, bool Afloat = false, int EmergencyLiftNok = 0, string? SeasonLead = null) : QueryResult;

/// <summary>One thing aboard. Tool: kept for good (the remote start button); otherwise a spare, Count of them.</summary>
public sealed record InventoryLine(string Name, int Count, bool Tool);

/// <summary>One thing the chandlery sells, with its price and the walking time.</summary>
public sealed record ShopLine(string Name, int PriceNok, int Minutes);

/// <summary>Aboard: what you carry. Shop: the chandlery in this port (null at sea, where ForSale is empty).</summary>
public sealed record InventoryView(IReadOnlyList<InventoryLine> Aboard, string? Shop, IReadOnlyList<ShopLine> ForSale) : QueryResult;

/// <summary>One day of the forecast. Known false beyond the forecast's reach ("No forecast that far out."): the numbers are then empty.</summary>
/// <summary>Fog: the day has fog in the morning or all day (a morning you see coming; the forecast's noise does not touch it). Design note 010.</summary>
public sealed record ForecastDay(int Day, bool Known, string Text, int WindForce, string WindFrom, bool Rain, bool WindServes, bool Fog = false);

/// <summary>A known problem and what it means at sea. Spare: the chandlery item that answers it (null: none); SpareAboard: you have one.</summary>
public sealed record TripRisk(string Problem, string Component, string AtSea, string? Spare, bool SpareAboard);

/// <summary>
/// The plan for a trip. Blockers: plain words for why Depart would be refused now (empty: you could go).
/// SparesMissing: the spares the risks name that are not aboard. SailHours null: the wind will not serve, or no forecast.
/// Design note 010: Fuel is the gauge in words against what the passage needs; VacationDays the weekdays the passage will charge; Warnings are
/// things that are risks, not walls (fuel, the dark, fog, vacation days), only ever what the player knows; AtDestination the events and the
/// anchorage rule at the other end. Legs: when the way runs through another port, its legs (null for a direct route); the distance, hours, fuel,
/// daylight and vacation days then cover the whole way, and a passage sails the first leg.
/// </summary>
public sealed record TripPlanView(
    string From, string To, double DistanceNm, int CourseDeg, int DepartDay, double MotorHours, double? SailHours, string Estimate,
    IReadOnlyList<ForecastDay> Forecast, IReadOnlyList<TripRisk> Risks, IReadOnlyList<string> SparesAboard,
    IReadOnlyList<string> SparesMissing, IReadOnlyList<string> Shelter, int BalanceNok, int TowNok,
    IReadOnlyList<string> Blockers,
    string? Fuel = null, int VacationDays = 0, IReadOnlyList<string>? Warnings = null, IReadOnlyList<string>? AtDestination = null,
    IReadOnlyList<PlanLeg>? Legs = null) : QueryResult;

/// <summary>One leg of a way through another port (design note 010, second pass I). The plan's distance, hours, fuel, daylight and vacation days cover all of them; a passage is the first leg.</summary>
public sealed record PlanLeg(string From, string To, double DistanceNm, int CourseDeg);

/// <summary>
/// Where she is on the passage. At a port: AtSea false, Port set, the rest empty. At sea: From and To are port names, PositionNm is the
/// net distance made good from From (drift already taken off), RemainingNm what is left to To, Mode in the game's words ("under engine",
/// "under sail", "hove-to", "at anchor", "drifting", "under tow"), Hours the hour in progress since she cast off. CanAnchor: shelter is within
/// reach; NearestShelter the nearest place to anchor, in words. WindServes: the wind would carry her under sail on this course now.
/// Destinations: in port, one line per route out ("Hestholmen, 14 nm"); null at sea.
/// </summary>
public sealed record TripView(
    bool AtSea, string? Port, string? From, string? To, double PositionNm, double RemainingNm, string? Mode, int Hours,
    bool CanAnchor, string? NearestShelter, bool WindServes, string Text, IReadOnlyList<string>? Destinations = null) : QueryResult;

/// <summary>The observation after an hour (or part of one) at sea: Continue, Motor, Sail. Text is the hour line, also in the narrative and the logbook.</summary>
public sealed record LegView(
    int Hour, string Mode, double PositionNm, double RemainingNm, int WindForce, string WindFrom, int SeaState, string Text) : Observation;

/// <summary>
/// The card at the end of a passage: arrival, arrival back home after turning back, or a tow. Destination is the port she ended at.
/// Failures are what the boat told you on the way (alerts); Fixes what you put right at sea; Log the logbook entries of the passage.
/// </summary>
public sealed record TripResultView(
    string Destination, bool Towed, bool TurnedBack, int Minutes, int MoneySpentNok,
    IReadOnlyList<string> Failures, IReadOnlyList<string> Fixes, IReadOnlyList<LogEntry> Log, IReadOnlyList<string> Lines) : Observation;

/// <summary>
/// The lift (design note 008): HaulOut, and the autumn emergency lift. HullText is what the hull looks like out of the water, with
/// its cause in words. Lines: the card in the game's voice. No ids.
/// </summary>
public sealed record HauloutView(int Day, int LiftNok, int StorageNok, string HullText, IReadOnlyList<string> Lines) : Observation;

/// <summary>One piece of winter layup work (filled by the layup round; empty until then). Command is the console words.</summary>
public sealed record LayupTask(string Name, bool Done, int Minutes, int CostNok, string Command, string Risk);

/// <summary>
/// The season and the yard's calendar. Text is the season line (also in the status). Day numbers are absolute game days.
/// BookedDay: the slot you hold. FirstFreeDay null: the yard is full for the autumn, or has finished lifting for the year.
/// SlotsFromDay and SlotsToDay: this year's lifting window. HauledOut: she was launched and is out of the water again.
/// </summary>
public sealed record SeasonView(
    int Day, int Year, int DayOfYear, string SeasonWord, string Text, bool Afloat, bool LaidUp, int LaunchDeadlineDay,
    int? BookedDay, int? FirstFreeDay, int SlotsFromDay, int SlotsToDay, int LiftNok, int StorageNok, int ChangeFeeNok,
    int EmergencyLiftNok, bool HauledOut, IReadOnlyList<LayupTask> Tasks, bool Listed, int? AskNok, int OpenOffers, bool Kept,
    IReadOnlyList<SeasonAction>? Actions = null, IReadOnlyList<UpgradeView>? Upgrades = null, string? Reputation = null) : QueryResult;

/// <summary>
/// A season action and whether the facade would accept it right now (the same refusal the command gives, free and unlogged, when it is not).
/// Verb is the command's wire name; Cost is what it would bill now.
/// </summary>
public sealed record SeasonAction(string Verb, string Label, bool Available, string? UnavailableReason, int Minutes, int CostNok);

/// <summary>One line of the broker's letter: what it is, and what it does to the value (deductions are negative).</summary>
public sealed record ValuationLine(string Text, int Nok);

/// <summary>
/// The broker's valuation letter. The lines add up to <see cref="ValueNok"/>. Only the problems the player knows are open are in it:
/// the buyer's surveyor will find the rest for themselves.
/// </summary>
public sealed record ValuationLetter(
    int Day, string Broker, IReadOnlyList<ValuationLine> Lines, int ValueNok, int? AskNok, IReadOnlyList<string> Text);

/// <summary>A buyer's viewing: what their surveyor found (with the yard's price), what they value her at and what they offer.</summary>
public sealed record ViewingReport(
    int Number, int Day, IReadOnlyList<string> Findings, int ValueNok, int OfferNok, int HoldsUntilDay, bool Open);

public sealed record SaleView(
    bool Listed, int? AskNok, ValuationLetter? Letter, IReadOnlyList<ViewingReport> Viewings, bool Sold) : QueryResult;

/// <summary>What ListForSale hands back: the letter.</summary>
public sealed record ListingView(ValuationLetter Letter) : Observation;

/// <summary>The end of the game: facts only. The lines are also the narrative of the accepting command.</summary>
public sealed record ClosingView(
    int Day, int PriceNok, int SpentNok, int BalanceNok, int Days, int HoursWorked, int Trips, int Tows, IReadOnlyList<string> Lines) : Observation;

// ------------------------------------------------------------------ the chart (design note 009)

/// <summary>
/// The chart. Positions are nautical miles east and north of the home port, to 0.1 nm; bearings whole degrees. Here: where she is, in a
/// sentence. Track: the passage under way (null in port); EarlierTracks: this season's finished passages, oldest first. No ids.
/// </summary>
public sealed record ChartView(
    string Here, IReadOnlyList<ChartPort> Ports, IReadOnlyList<ChartRoute> Routes, ChartBoat Boat,
    ChartTrack? Track, IReadOnlyList<ChartTrack> EarlierTracks, ChartWind Wind, ChartPlan? Plan) : QueryResult;

/// <summary>A port with its marks. DistanceNm and BearingDeg are from the boat's position now; Reachable: a route out of the port she lies at (false at sea). Via: a port with no route from here but one through other ports, the ports passed through in words ("Hestholmen"), RouteNm the length of that way.</summary>
public sealed record ChartPort(
    string Name, double EastNm, double NorthNm, bool Yard, bool Shop, bool Fuel, bool Anchorage, bool IsHere,
    bool Reachable, double DistanceNm, int BearingDeg, string? Via = null, double RouteNm = 0);

/// <summary>A route as written in the data, once (the way back is the same line). Distance and course are derived from the ports' positions.</summary>
public sealed record ChartRoute(string From, string To, double DistanceNm, int CourseDeg, IReadOnlyList<ChartShelter> Shelters);

/// <summary>A place to get out of the weather, AtNm along the route from its first port, and where that is on the chart.</summary>
public sealed record ChartShelter(string Name, double AtNm, double EastNm, double NorthNm);

/// <summary>Where she is. At sea From/To are port names and PositionNm/RemainingNm the distance made good and left; HeadingDeg the route's course (0 in port).</summary>
public sealed record ChartBoat(
    bool AtSea, string? Port, double EastNm, double NorthNm, string? From, string? To, double PositionNm,
    double RemainingNm, string? Mode, int HeadingDeg, bool Sunk);

/// <summary>A passage on the chart. Outcome: "under way", "arrived", "turned back", "towed" or "sank". Day: the day she left.</summary>
public sealed record ChartTrack(string From, string To, int Day, string Outcome, IReadOnlyList<ChartFix> Fixes);

/// <summary>One hourly fix (hour 0: the departure). Events: what the logbook recorded in that hour.</summary>
public sealed record ChartFix(int Hour, double EastNm, double NorthNm, string Mode, IReadOnlyList<string> Events);

/// <summary>Today's real weather as the status shows it.</summary>
public sealed record ChartWind(int Force, string From, bool Rain, string Text);

/// <summary>The trip the adapter asked about, from the plan: the estimate under engine and sail, and what would stop her leaving.</summary>
public sealed record ChartPlan(
    string From, string To, double DistanceNm, int CourseDeg, IReadOnlyList<ChartShelter> Shelters,
    double MotorHours, double? SailHours, string Estimate, IReadOnlyList<string> Blockers);

// ------------------------------------------------------------------ the week and the money (design note 010)

/// <summary>One line of the money ledger. Nok is signed: income positive, everything else negative. Kind: income, spending, bill, interest or unpaid.</summary>
public sealed record LedgerEntry(int Day, string Kind, string Text, int Nok);

/// <summary>Something coming up: Kind is payday, bill, delivery, slot or event (the first day of its window). Nok is signed.</summary>
public sealed record CalendarEntry(int Day, string Weekday, string Kind, string Text, int Nok);

/// <summary>A part on order. Status: "on its way" or "waiting at the yard". Delivered parts drop out.</summary>
public sealed record OrderView(string Part, int OrderedDay, int DueDay, string Status);

/// <summary>The totals since the season began (the start of the game, or the last Keep), all positive, and the last twelve entries, newest first.</summary>
public sealed record MoneyView(
    int BalanceNok, int IncomeNok, int SpendingNok, int BillsNok, int InterestNok, int UnpaidNok, IReadOnlyList<LedgerEntry> Recent);

/// <summary>
/// The week. EveningMinutesLeft is 0 at weekends and once the day is charged; NextPaydayDay is -1 without pay.
/// Without a calendar in the boat's data the vacation fields are 0 and the lists empty.
/// </summary>
public sealed record CalendarView(
    int Day, string Weekday, bool Weekend, string Week, int EveningMinutesLeft, int VacationDaysLeft, int VacationDaysPerYear,
    int VacationDaysUsed, int UnpaidDays, int BalanceNok, int OverdraftLimitNok, int PayNok, int NextPaydayDay,
    IReadOnlyList<CalendarEntry> Ahead, IReadOnlyList<OrderView> Orders, MoneyView Money,
    IReadOnlyList<EventView>? Events = null, string? Reputation = null, int PaydaysSeen = 0, int EventsMade = 0, int EventsMissed = 0) : QueryResult;

/// <summary>
/// One thing an event asks of the boat, in the player's words. Met: true or false when the player can tell, null when it depends on weather
/// or on something not yet known. Note: why, in words (never an id, never a fault the player has not found).
/// </summary>
public sealed record Requirement(string Text, bool? Met, string Note);

/// <summary>
/// An event of the calendar of wants (design note 010). When: "Saturday, day 152". Status: "ahead", "on now", "made", "failed: &lt;requirement&gt;" or
/// "missed". Story: the first line of the story once made, else empty. ToDay is the last day of the window. Going: the player marked it (plan event),
/// or the friend was asked for it: a wait stops on the morning it starts; an event not marked still happens and is still judged. ToBePicked: a week the
/// player has not chosen yet (ask first). Beats: the beats of an invited week. Outcome: how a finished week went, in the short words of the data.
/// </summary>
public sealed record EventView(
    string Name, string Place, int FromDay, int ToDay, string When, string Status, IReadOnlyList<Requirement> Requirements, string Story,
    bool Going = false, IReadOnlyList<BeatView>? Beats = null, bool ToBePicked = false, string Outcome = "");

/// <summary>
/// One beat of a picked week (design note 010, second pass H): Text in the player's words, When the day and hour in words, Status "ahead", "on now",
/// "made", "failed: &lt;requirement&gt;" or "missed", Note what it asks while it is still to come.
/// </summary>
public sealed record BeatView(string Text, string When, string Status, string Note);

/// <summary>
/// One upgrade (design note 010, step 3). Name: the item as the chandlery sells it. Status: "fitted on day N", "aboard, not fitted", "on order, due day N",
/// "waiting at the yard" or "not aboard". PriceNok: the part. InstallMinutes: your own work. YardNok and YardDays: the yard's labour and days, null when
/// it is a job for you. Opens: what it makes possible. ValueNok: what the broker adds for it, new. OrderRefusal, InstallRefusal, YardInstallRefusal: why that
/// command would be refused now, in the words the command itself gives (null: it would go ahead); YardInstallRefusal is null too when the yard does not do the job.
/// </summary>
public sealed record UpgradeView(
    string Name, string Status, int PriceNok, int InstallMinutes, int? YardNok, int? YardDays, string Opens, int ValueNok,
    string? OrderRefusal = null, string? InstallRefusal = null, string? YardInstallRefusal = null);

public sealed record UpgradesView(IReadOnlyList<UpgradeView> Items) : QueryResult;

/// <summary>
/// The page the logbook writes at the autumn lift (design note 010, step 4): no grade, only what the season was. MilesSailed is the log's reading,
/// NightsAway the midnights afloat away from home or at sea, PlacesReached the ports other than home in order, EventsMissed with the reason in brackets.
/// SpentNok is spending, bills, interest and unpaid leave of the season; IncomeNok the pay that went into her account; ValueNok the broker's figure
/// for the faults the player knows of. Reputation is a sentence, never a number. Lines are the page in the game's voice. Season 0: none yet.
/// </summary>
public sealed record SeasonPageView(
    int Season, int FromDay, int ToDay, double MilesSailed, int NightsAway, IReadOnlyList<string> PlacesReached,
    IReadOnlyList<string> EventsMade, IReadOnlyList<string> EventsMissed, int HoursWorked, int VacationDaysUsed, int UnpaidDays,
    int SpentNok, int IncomeNok, int ValueNok, string Reputation, IReadOnlyList<string> Lines) : QueryResult;
