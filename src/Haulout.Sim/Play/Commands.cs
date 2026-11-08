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
[JsonDerivedType(typeof(Depart), "depart")]
[JsonDerivedType(typeof(Continue), "continue")]
[JsonDerivedType(typeof(Motor), "motor")]
[JsonDerivedType(typeof(Sail), "sail")]
[JsonDerivedType(typeof(HeaveTo), "heave-to")]
[JsonDerivedType(typeof(Anchor), "anchor")]
[JsonDerivedType(typeof(TurnBack), "turn-back")]
[JsonDerivedType(typeof(CallTow), "call-tow")]
[JsonDerivedType(typeof(BookHaulout), "book-haulout")]
[JsonDerivedType(typeof(HaulOut), "haul-out")]
[JsonDerivedType(typeof(Scrub), "scrub")]
[JsonDerivedType(typeof(WashHull), "wash-hull")]
[JsonDerivedType(typeof(WinteriseEngine), "winterise-engine")]
[JsonDerivedType(typeof(FillTank), "fill-tank")]
[JsonDerivedType(typeof(BatteryAshore), "battery-ashore")]
[JsonDerivedType(typeof(BatteryAboard), "battery-aboard")]
[JsonDerivedType(typeof(WinterCharger), "winter-charger")]
[JsonDerivedType(typeof(FitWinterCover), "fit-winter-cover")]
[JsonDerivedType(typeof(WaitUntilSpring), "wait-until-spring")]
[JsonDerivedType(typeof(ListForSale), "list-for-sale")]
[JsonDerivedType(typeof(AcceptOffer), "accept-offer")]
[JsonDerivedType(typeof(Keep), "keep")]
[JsonDerivedType(typeof(NextWeekend), "next-weekend")]
[JsonDerivedType(typeof(WaitWeek), "wait-week")]
[JsonDerivedType(typeof(Order), "order")]
[JsonDerivedType(typeof(InviteFriend), "invite-friend")]
[JsonDerivedType(typeof(Install), "install")]
[JsonDerivedType(typeof(YardInstall), "yard-install")]
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

/// <summary>
/// Cast off for another port, the engine running, from the cockpit (design note 007). Destination null: the one route out of this port.
/// </summary>
public sealed record Depart(string? Destination = null) : Command;
/// <summary>At sea, from the cockpit: another hour in the mode she is in.</summary>
public sealed record Continue() : Command;
/// <summary>At sea, from the cockpit, engine running: under engine, then an hour.</summary>
public sealed record Motor() : Command;
/// <summary>At sea, from the cockpit, wind serving: under sail, then an hour.</summary>
public sealed record Sail() : Command;
/// <summary>At sea: stop her where she is so you can work on the boat. She drifts.</summary>
public sealed record HeaveTo() : Command;
/// <summary>At sea, within a mile of shelter (a port or a named anchorage): let the anchor go.</summary>
public sealed record Anchor() : Command;
/// <summary>At sea: give up the passage and head back to the port she left. Once.</summary>
public sealed record TurnBack() : Command;
/// <summary>At sea, anywhere: call the towboat. Costs the tow and the best part of a day; ends the trip at the nearer port along the route.</summary>
public sealed record CallTow() : Command;

/// <summary>
/// Ring the yard and book the crane for an absolute game day (design note 008). A phone call: 0 minutes, anywhere but at sea.
/// Changing an existing booking costs a fee.
/// </summary>
public sealed record BookHaulout(int Day) : Command;
/// <summary>At Skjærvik, afloat, from the cockpit with the engine running, on the booked day: motor to the crane and be lifted out.</summary>
public sealed record HaulOut() : Command;
/// <summary>Afloat in port or at anchor, from the cockpit or on deck: scrub what you can reach at the waterline. Takes a part of the growth off, never all.</summary>
public sealed record Scrub() : Command;
/// <summary>On the hard at the yard: the yard pressure-washes the hull for a price. Takes all the growth off.</summary>
public sealed record WashHull() : Command;

/// <summary>On the hard, from the companionway with the steps off: drain the raw-water side of the engine and run antifreeze through it (design note 008).</summary>
public sealed record WinteriseEngine() : Command;
/// <summary>At a port with fuel: top the tank up. A full tank has no room for damp air to condense in.</summary>
public sealed record FillTank() : Command;
/// <summary>On the hard, from the quarter berth with the battery box lid off: carry the start battery to the yard's battery room for the winter.</summary>
public sealed record BatteryAshore() : Command;
/// <summary>On the hard, from the quarter berth: fit the start battery again.</summary>
public sealed record BatteryAboard() : Command;
/// <summary>On the hard: clip the yard's charger on the battery aboard, so it is held full all winter. Disconnected at launch.</summary>
public sealed record WinterCharger() : Command;
/// <summary>On the hard, on deck: rig the winter cover over the deck. Kept for later winters; taken off at launch.</summary>
public sealed record FitWinterCover() : Command;
/// <summary>Not at sea: leave her day by day to the next day 75 of the year, coming back early if the boat has something to tell you or your haulout slot comes.</summary>
public sealed record WaitUntilSpring() : Command;

public sealed record ListForSale(int AskNok) : Command;
public sealed record AcceptOffer(int? Offer = null) : Command;
public sealed record Keep() : Command;

/// <summary>Not at sea: go back to work and come back aboard at 08:00 on the next Saturday (from a Saturday or Sunday, the Saturday after). Design note 010.</summary>
public sealed record NextWeekend() : Command;
/// <summary>Not at sea: go back to work and come back aboard at 08:00 seven days on. Design note 010.</summary>
public sealed record WaitWeek() : Command;
/// <summary>
/// Ring the yard chandlery from anywhere not at sea and order a part it does not keep on the shelf: a chandlery item or a part you have found.
/// It is paid for at once and arrives at the yard in days. Design note 010.
/// </summary>
public sealed record Order(string Item) : Command;
/// <summary>
/// Ring a friend from anywhere not at sea, once a year, and ask them for a week aboard starting on this absolute day (in the window of July,
/// at least a few days ahead). Design note 010, step 2.
/// </summary>
public sealed record InviteFriend(int Day) : Command;

/// <summary>
/// Fit an upgrade you have aboard (bought or delivered): at the place it goes, not at sea, on a dry day if the job needs one. Its minutes
/// are your own work. Design note 010, step 3.
/// </summary>
public sealed record Install(string Item) : Command;
/// <summary>
/// At the yard, afloat or ashore: the yard fits an upgrade for labour and days, supplying the part if you have none aboard. Design note 010, step 3.
/// </summary>
public sealed record YardInstall(string Item) : Command;

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
[JsonDerivedType(typeof(Inventory), "inventory")]
[JsonDerivedType(typeof(PlanTrip), "plan-trip")]
[JsonDerivedType(typeof(Trip), "trip")]
[JsonDerivedType(typeof(Season), "season")]
[JsonDerivedType(typeof(Sale), "sale")]
[JsonDerivedType(typeof(Chart), "chart")]
[JsonDerivedType(typeof(Calendar), "calendar")]
[JsonDerivedType(typeof(Upgrades), "upgrades")]
[JsonDerivedType(typeof(SeasonPage), "season-page")]
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
/// <summary>What is aboard (tools and spares) and, in port, what the chandlery sells. Costs no time.</summary>
public sealed record Inventory() : Query;
/// <summary>
/// The plan for a trip: distance, hours, the forecast for the window, the known problems that matter at sea, the spares aboard.
/// Destination null: the one route out of this port. DepartDay null: today. Costs no time.
/// </summary>
public sealed record PlanTrip(string? Destination = null, int? DepartDay = null) : Query;

/// <summary>Where she is on the passage: at a port, or at sea with the position, the mode and the nearest shelter. Costs no time.</summary>
public sealed record Trip() : Query;

/// <summary>The yard calendar and the season: the booked slot, the first free slot, what lifting costs. Costs no time.</summary>
public sealed record Season() : Query;

public sealed record Sale() : Query;

/// <summary>
/// The chart (design note 009): the ports and routes, where she is, the passage under way as hourly fixes, the earlier passages of the season,
/// today's wind. Planned: the destination the adapter has planned (the game does not remember a plan); null: none. Costs no time.
/// </summary>
public sealed record Chart(string? Planned = null) : Query;

/// <summary>The week (design note 010): today, the evening and vacation days left, the bills, payday and deliveries ahead, what is on order, the money. Costs no time.</summary>
public sealed record Calendar() : Query;

/// <summary>The upgrades (design note 010, step 3): each with its status, price, install hours, the yard's price and days, what it adds to her value and what it opens. Costs no time.</summary>
public sealed record Upgrades() : Query;

/// <summary>The page the logbook wrote at the autumn lift (design note 010, step 4): miles, nights away, places, events, hours, money, what she is worth. Season null: the latest. Costs no time.</summary>
public sealed record SeasonPage(int? Season = null) : Query;
