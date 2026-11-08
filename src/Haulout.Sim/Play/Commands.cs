using System.Text.Json.Serialization;

namespace Haulout.Sim.Play;

/// <summary>Something the player does. Names are free text; the sim resolves them.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Look), "look")]
[JsonDerivedType(typeof(GoTo), "go")]
[JsonDerivedType(typeof(Remove), "remove")]
[JsonDerivedType(typeof(Refit), "refit")]
[JsonDerivedType(typeof(Inspect), "inspect")]
[JsonDerivedType(typeof(Measure), "measure")]
[JsonDerivedType(typeof(StartEngine), "start")]
[JsonDerivedType(typeof(Clean), "clean")]
[JsonDerivedType(typeof(Uncover), "uncover")]
[JsonDerivedType(typeof(Replace), "replace")]
[JsonDerivedType(typeof(Charge), "charge")]
[JsonDerivedType(typeof(Buy), "buy")]
[JsonDerivedType(typeof(CheckFilterBowl), "check-filter-bowl")]
[JsonDerivedType(typeof(CheckRawWaterFlow), "check-raw-water-flow")]
[JsonDerivedType(typeof(Defer), "defer")]
[JsonDerivedType(typeof(Undefer), "undefer")]
[JsonDerivedType(typeof(FixByYard), "yard-fix")]
[JsonDerivedType(typeof(OrderSurvey), "order-survey")]
[JsonDerivedType(typeof(WaitDays), "wait")]
[JsonDerivedType(typeof(Launch), "launch")]
[JsonDerivedType(typeof(EmergencyLift), "emergency-lift")]
[JsonDerivedType(typeof(PumpBilge), "pump-bilge")]
[JsonDerivedType(typeof(StopEngine), "stop-engine")]
public abstract record Command;

public sealed record Look() : Command;
/// <summary>Move to an adjacent place (1 min). Place is a name as the player would say it.</summary>
public sealed record GoTo(string Place) : Command;
/// <summary>Take off a cover at the current place: steps, panel, lid, floorboards. Time from data.</summary>
public sealed record Remove(string Cover) : Command;
/// <summary>Put a removed cover back. Time from data.</summary>
public sealed record Refit(string Cover) : Command;
public sealed record Inspect(string Component) : Command;
public sealed record Measure(string Component) : Command;
public sealed record StartEngine() : Command;
/// <summary>Pull the stop and shut the running engine down (1 min), from anywhere aboard.</summary>
public sealed record StopEngine() : Command;
public sealed record Clean(string Component) : Command;
/// <summary>Pull back a small removable part (a boot, a cap) to see what is under it. Needs reach.</summary>
public sealed record Uncover(string Component) : Command;
public sealed record Replace(string Component) : Command;
public sealed record Charge(string Component) : Command;
public sealed record Buy(string Item) : Command;
public sealed record CheckFilterBowl() : Command;
public sealed record CheckRawWaterFlow() : Command;
/// <summary>Leave a discovered problem for later, on purpose. It keeps ageing. Fault: the problem's name or its part's name.</summary>
public sealed record Defer(string Fault) : Command;
/// <summary>Take a deferred problem back onto the list. Fault: the problem's name or its part's name.</summary>
public sealed record Undefer(string Fault) : Command;
/// <summary>Pay the yard to fix a discovered problem: their price, their lead time in days, done properly.</summary>
public sealed record FixByYard(string Fault) : Command;
/// <summary>Hire the surveyor. Costs money per foot and a day; the report arrives as a document.</summary>
public sealed record OrderSurvey() : Command;
/// <summary>Let time pass, e.g. waiting for a part. Days: 1 or more.</summary>
public sealed record WaitDays(int Days) : Command;
/// <summary>Put her in the water. Allowed any time; the choice is recorded.</summary>
public sealed record Launch() : Command;
/// <summary>Afloat only: pay the yard to lift her out again. Costs money and calendar days; she is back on the hard.</summary>
public sealed record EmergencyLift() : Command;

/// <summary>Work the manual bilge pump from the cockpit: 15 minutes, up to 150 litres out.</summary>
public sealed record PumpBilge() : Command;

/// <summary>Something the player asks about. Costs no time.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Briefing), "briefing")]
[JsonDerivedType(typeof(Status), "status")]
[JsonDerivedType(typeof(Logbook), "logbook")]
[JsonDerivedType(typeof(Commands), "commands")]
[JsonDerivedType(typeof(Aboard), "aboard")]
[JsonDerivedType(typeof(ComponentActions), "component-actions")]
[JsonDerivedType(typeof(SurveyReport), "survey-report")]
[JsonDerivedType(typeof(Readiness), "readiness")]
[JsonDerivedType(typeof(Weather), "weather")]
[JsonDerivedType(typeof(Notices), "notices")]
public abstract record Query;

public sealed record Briefing() : Query;
public sealed record Status() : Query;
public sealed record Logbook() : Query;
public sealed record Commands() : Query;
/// <summary>Where the player is, the places and covers aboard, and every component found so far. No ids.</summary>
public sealed record Aboard() : Query;
/// <summary>The verbs that make sense on a component the player has found, with time, cost and why one is unavailable.</summary>
public sealed record ComponentActions(string Component) : Query;
/// <summary>The surveyor's report, once ordered. Before that, Ordered is false and Items is empty.</summary>
public sealed record SurveyReport() : Query;
/// <summary>The launch picture: every discovered open problem with its price now and later, the deadline, the money. Never anything undiscovered.</summary>
public sealed record Readiness() : Query;
/// <summary>Today's sky and wind. Costs no time.</summary>
public sealed record Weather() : Query;
/// <summary>What the boat has told you since you last asked: the unread alerts, which it then marks read. Costs no time.</summary>
public sealed record Notices() : Query;
