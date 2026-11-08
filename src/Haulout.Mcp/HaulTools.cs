using System.ComponentModel;
using Haulout.Sim.Play;
using ModelContextProtocol.Server;

namespace Haulout.Mcp;

/// <summary>One tool per player command or question. Each returns plain prose.</summary>
[McpServerToolType]
public sealed class HaulTools(GameSession session)
{
    [McpServerTool(Name = "look", Destructive = false), Description(
        "Look around the place you are in. Takes 2 minutes, free. Tells you where you are, which covers are off or in place here, what you can see from here and where you can go next. Start here.")]
    public string Look() => session.Do("look", new Look());

    [McpServerTool(Name = "go"), Description(
        "Move to a place next to the one you are in (for example the companionway, the saloon or the ground beside the hull). Takes 1 minute, free. You can only reach places adjacent to where you are; look says which.")]
    public string Go([Description("Name of the place, e.g. as listed by look or aboard.")] string place) =>
        session.Do("go", new GoTo(place), ("place", place));

    [McpServerTool(Name = "remove"), Description(
        "Take off a cover in the place you are in (steps, a panel, a lid, floorboards) to see and reach what is behind it. Takes a few minutes, free; how long depends on the cover and the result says.")]
    public string Remove([Description("Name of the cover, e.g. as listed by aboard.")] string cover) =>
        session.Do("remove", new Remove(cover), ("cover", cover));

    [McpServerTool(Name = "refit"), Description(
        "Put a cover you took off back. Takes a few minutes, free; how long depends on the cover and the result says. You must be in the place it belongs to.")]
    public string Refit([Description("Name of the cover.")] string cover) =>
        session.Do("refit", new Refit(cover), ("cover", cover));

    [McpServerTool(Name = "inspect"), Description(
        "Take a closer look at a part you have already found and can see from where you are. Takes 5 minutes, free. Describes its condition and anything visibly wrong with it.")]
    public string Inspect([Description("Name of a part you have found.")] string component) =>
        session.Do("inspect", new Inspect(component), ("component", component));

    [McpServerTool(Name = "uncover"), Description(
        "Pull back a small removable part (a boot or cap) on something you can reach, to see what is underneath. Takes 2 minutes, free. Refused, free, for parts of a kind that have nothing to pull back.")]
    public string Uncover([Description("Name of a part you have found.")] string component) =>
        session.Do("uncover", new Uncover(component), ("component", component));

    [McpServerTool(Name = "measure"), Description(
        "Put the multimeter on a part you have found and can get a hand to from where you are. Takes 10 minutes, free. Refused, free, for parts a meter has nothing to read on. " +
        "Readings of the electrical system while the starter is cranking need a remote start button from the chandlery (see buy); without one only the battery's resting voltage can be read.")]
    public string Measure([Description("Name of a part you have found.")] string component) =>
        session.Do("measure", new Measure(component), ("component", component));

    [McpServerTool(Name = "clean"), Description(
        "Clean and tighten a part you have found. Takes 30 minutes, free. Stops the engine first if it is running. Refused, free, for parts of a kind that cannot be cleaned; cleaning a part that was fine takes the same time and finds nothing.")]
    public string Clean([Description("Name of a part you have found.")] string component) =>
        session.Do("clean", new Clean(component), ("component", component));

    [McpServerTool(Name = "replace"), Description(
        "Fit a new part in place of one you have found. Time and price depend on the part, and the cost is charged as money; the result tells you what it took. " +
        "Stops the engine first if it is running. Not every part can be replaced.")]
    public string Replace([Description("Name of a part you have found.")] string component) =>
        session.Do("replace", new Replace(component), ("component", component));

    [McpServerTool(Name = "charge"), Description(
        "Charge a battery on shore power. Takes 8 hours, free. Stops the engine first if it is running.")]
    public string Charge([Description("Name of the battery.")] string component) =>
        session.Do("charge", new Charge(component), ("component", component));

    [McpServerTool(Name = "buy"), Description(
        "Buy a tool or a spare at the chandlery, only while she lies at a port (refused at sea). Prices and minutes are listed by inventory and charged as money; the result tells you what it cost. " +
        "A tool is bought once; spares stack. A spare is used if you replace the matching part later.")]
    public string Buy([Description("Name of the item, e.g. remote start button, impeller.")] string item) =>
        session.Do("buy", new Buy(item), ("item", item));

    [McpServerTool(Name = "check_filter"), Description(
        "Look in the bowl of the fuel filter you have found. Takes 10 minutes, free.")]
    public string CheckFilter() => session.Do("check_filter", new CheckFilterBowl());

    [McpServerTool(Name = "check_flow"), Description(
        "Look at the cooling water coming out of the exhaust. Takes 5 minutes, free. You can only see the outlet from outside the boat. Only tells you anything while the engine is running.")]
    public string CheckFlow() => session.Do("check_flow", new CheckRawWaterFlow());

    [McpServerTool(Name = "start_engine"), Description(
        "Try to start the engine. Takes 2 minutes, free. Only from the cockpit, where the key is. Reports what happens.")]
    public string StartEngine() => session.Do("start_engine", new StartEngine());

    [McpServerTool(Name = "stop_engine"), Description(
        "Pull the stop and shut the running engine down. Takes 1 minute, free. Works from anywhere aboard; refused when the engine is not running.")]
    public string StopEngine() => session.Do("stop_engine", new StopEngine());

    [McpServerTool(Name = "order_survey"), Description(
        "Hire the surveyor to go over the boat. The surveyor charges by the length of the boat, and the report names the fee; it takes one day. The report arrives in the logbook; read it with survey.")]
    public string OrderSurvey() => session.Do("order_survey", new OrderSurvey());

    [McpServerTool(Name = "survey", ReadOnly = true), Description(
        "Read the surveyor's report, once you have ordered the survey. Costs no time.")]
    public string Survey() => session.Ask("survey", new SurveyReport());

    [McpServerTool(Name = "readiness", ReadOnly = true), Description(
        "Where you stand before launch: the launch deadline, your money, and every problem you have found that is still open, with what it costs to fix now or later, the routes to fix it (your own time or the yard's price and lead time in days) and what happens if it is left. Costs no time.")]
    public string Readiness() => session.Ask("readiness", new Readiness());

    [McpServerTool(Name = "defer"), Description(
        "Leave a problem you have found for later, on purpose. It keeps getting worse meanwhile. Costs no time and no money.")]
    public string Defer([Description("Name of the problem, or of its part.")] string problem) =>
        session.Do("defer", new Defer(problem), ("problem", problem));

    [McpServerTool(Name = "undefer"), Description(
        "Take a problem you deferred back onto your list. Costs no time and no money.")]
    public string Undefer([Description("Name of the problem, or of its part.")] string problem) =>
        session.Do("undefer", new Undefer(problem), ("problem", problem));

    [McpServerTool(Name = "yard_fix"), Description(
        "Pay the yard to fix a problem you have found. The yard's price and lead time in days are shown by readiness; the cost is charged when you order it.")]
    public string YardFix([Description("Name of the problem, or of its part.")] string problem) =>
        session.Do("yard_fix", new FixByYard(problem), ("problem", problem));

    [McpServerTool(Name = "wait"), Description(
        "Let days pass, e.g. while waiting for the yard. Costs the days, free.")]
    public string Wait([Description("Number of days, 1 or more.")] int days) =>
        session.Do("wait", new WaitDays(days), ("days", days.ToString()));

    [McpServerTool(Name = "launch"), Description(
        "Put her in the water. Takes 2 hours, free; she goes in and stays in, and below-waterline work then needs an emergency lift.")]
    public string Launch() => session.Do("launch", new Launch());

    [McpServerTool(Name = "emergency_lift"), Description(
        "Only when she is afloat: pay the yard to lift her out again. Costs the emergency lift and some days of calendar time (no work time); the season tool lists the price. She is back on the hard: the ground beside the hull is there again and below-waterline fixes are possible. Launch again afterwards, free.")]
    public string EmergencyLift() => session.Do("emergency_lift", new EmergencyLift());

    [McpServerTool(Name = "pump_bilge"), Description(
        "Work the manual bilge pump by hand. Takes 15 minutes of work, free, and moves up to 150 litres out of the bilge. Only from the cockpit, where the handle is. The result says whether the pump emptied her or the water still comes.")]
    public string PumpBilge() => session.Do("pump_bilge", new PumpBilge());

    [McpServerTool(Name = "plan_trip", ReadOnly = true), Description(
        "Plan a trip from the port she lies at: distance and course, expected hours, the forecast for the departure day and the day after (it can be wrong, and is not known far ahead), " +
        "the problems you have found that matter at sea and whether you carry the spare for each, shelter on the way, your money and what a tow would cost, " +
        "the fuel the gauge shows against what the passage needs, whether she would arrive after dark, fog, the vacation days it costs, and any event waiting at the other end. Costs no time. " +
        "Leave the destination out for the one route out of this port; depart_day defaults to today.")]
    public string PlanTrip(
        [Description("Where to, e.g. Hestholmen. Optional.")] string? destination = null,
        [Description("The day to leave. Optional; defaults to today.")] int? depart_day = null) =>
        session.Ask("plan_trip", new PlanTrip(destination, depart_day));

    [McpServerTool(Name = "inventory", ReadOnly = true), Description(
        "What is aboard (tools and spares, with counts) and, in port, what the chandlery sells with prices; nothing is for sale at sea. Costs no time.")]
    public string Inventory() => session.Ask("inventory", new Inventory());

    [McpServerTool(Name = "trip", ReadOnly = true), Description(
        "Where she is on the passage: at which port, or at sea with the distance made good, the distance left, how she is being sailed and the nearest place to anchor. Costs no time.")]
    public string Trip() => session.Ask("trip", new Trip());

    [McpServerTool(Name = "chart", ReadOnly = true), Description(
        "The chart: where she is in nautical miles east and north of Skjærvik, each port with its marks (yard, shop, fuel, anchorage), its position, its distance and bearing from where she is, " +
        "the shelters on the routes, today's wind, and the track of the passage under way as hourly fixes with what happened in each hour, and this season's earlier passages. " +
        "Name a destination to see the planned route with its estimate. Costs no time.")]
    public string Chart([Description("Where to, e.g. Hestholmen. Optional: shows the planned route and estimate.")] string? destination = null) =>
        session.Ask("chart", new Chart(destination));

    [McpServerTool(Name = "depart"), Description(
        "Cast off for another port. Takes 10 minutes, free. Only from the cockpit, afloat at a port, with the engine already running (you motor off the mooring) and not in a gale; " +
        "refused otherwise (fog does not refuse it: it slows her and risks close calls without a plotter or radar), and the engine must have run this season. Leave the destination out for the one route out of this port. Then use continue and the other at-sea tools, an hour at a time.")]
    public string Depart([Description("Where to, e.g. Hestholmen. Optional.")] string? destination = null) =>
        session.Do("depart", new Depart(destination), ("destination", destination ?? ""));

    [McpServerTool(Name = "continue"), Description(
        "At sea, from the cockpit: another hour as she is now (under engine, under sail, hove-to, drifting). Takes up to an hour; the result is a line about the hour. Refused in port.")]
    public string ContinuePassage() => session.Do("continue", new Continue());

    [McpServerTool(Name = "motor"), Description(
        "At sea, from the cockpit: go under engine (weighing anchor if at anchor), then an hour. Takes up to an hour. Refused if the engine is not running or you are not at sea.")]
    public string Motor() => session.Do("motor", new Motor());

    [McpServerTool(Name = "sail"), Description(
        "At sea, from the cockpit: go under sail, then an hour. Takes up to an hour. Refused when the wind is too light, too strong or on the nose.")]
    public string Sail() => session.Do("sail", new Sail());

    [McpServerTool(Name = "heave_to"), Description(
        "At sea: stop her where she is so you can work on the boat; she drifts slowly and the hours still pass. Takes 5 minutes. Refused at anchor, if already hove-to, or in port. " +
        "Work on the engine at sea needs her hove-to or at anchor with the engine stopped.")]
    public string HeaveTo() => session.Do("heave_to", new HeaveTo());

    [McpServerTool(Name = "anchor"), Description(
        "At sea: let the anchor go. Takes 15 minutes. Only within a mile of a port or a named anchorage; otherwise refused and the result says where the nearest is. Refused too if the anchor chain won't run out.")]
    public string Anchor() => session.Do("anchor", new Anchor());

    [McpServerTool(Name = "turn_back"), Description(
        "At sea: give up the passage and head for the port she left. Takes 2 minutes. Once only; the way back is as long as the way made good.")]
    public string TurnBack() => session.Do("turn_back", new TurnBack());

    [McpServerTool(Name = "call_tow"), Description(
        "At sea, anywhere: call the towboat on the VHF. Costs the tow (plan_trip shows the price; it is charged even if you cannot afford it) and the best part of a day. Ends the trip at the nearer port along the route (the one she left or the one she was bound for).")]
    public string CallTow() => session.Do("call_tow", new CallTow());

    [McpServerTool(Name = "season", ReadOnly = true), Description(
        "The season and the yard's calendar: the lifting window, the slot you hold, the first free slot (it moves later every week you wait past the end of summer, and the yard fills up), and what a lift, winter storage, changing a booking and the emergency lift cost. Costs no time.")]
    public string Season() => session.Ask("season", new Season());

    [McpServerTool(Name = "season_page", ReadOnly = true), Description(
        "The page the logbook writes at the autumn haulout, once a season and with no grade: the miles sailed, the nights away from home, the places reached, the events of the calendar made and missed, " +
        "the hours of your own work and the vacation days, what she cost you and what the broker would put her at, and how the yard and the harbour think of you, in words. " +
        "Optionally a season number; the latest by default. Costs no time. The same page is in the logbook.")]
    public string SeasonPage([Description("The season number, as the page names it. Optional; defaults to the latest.")] int? season = null) =>
        session.Ask("season_page", new SeasonPage(season));

    [McpServerTool(Name = "book_haulout"), Description(
        "Ring the yard and book the crane for a day, to be lifted out of the water. Free and takes no time, from any port (refused at sea). Changing an existing booking costs a fee, which the season tool lists. " +
        "Refused if the day is before the first free slot (season tells you), past the lifting window, already yours, or the yard is full or has finished for the year. A slot you miss is given away.")]
    public string BookHaulout([Description("The day of the slot, as a game day number, e.g. 270.")] int day) =>
        session.Do("book_haulout", new BookHaulout(day), ("day", day.ToString()));

    [McpServerTool(Name = "haul_out"), Description(
        "Motor her round to the yard crane and be lifted out. Takes 2 hours and costs the lift and, in the autumn, winter storage on the hard (the season tool lists both prices), billed even if that takes your balance below zero. " +
        "Only at Skjærvik, afloat, on your booked day, from the cockpit with the engine running and not in a gale; refused otherwise, and a missed slot is given away. Shows what the hull looks like out of the water.")]
    public string HaulOut() => session.Do("haul_out", new HaulOut());

    [McpServerTool(Name = "scrub"), Description(
        "Scrub the growth off the hull from the rail. Takes 2 hours, free. Only afloat in port or at anchor, from the cockpit or on deck; it takes part of the growth off, never all. " +
        "Refused on the hard (clean the hull instead) and under way.")]
    public string Scrub() => session.Do("scrub", new Scrub());

    [McpServerTool(Name = "yard_wash"), Description(
        "Pay the yard to pressure-wash the hull clean. Costs the yard's price for a wash (the result says what) and takes 2 hours of the yard's time (not your work). Only on the hard at Skjærvik; refused afloat or without the money.")]
    public string YardWash() => session.Do("yard_wash", new WashHull());

    [McpServerTool(Name = "winterise_engine"), Description(
        "Winter layup, on the hard: drain the raw-water side of the engine (the pump and the heat exchanger) and run antifreeze through it, so frost cannot crack them. Takes 90 minutes and costs money (the result tells you what). " +
        "Only on the hard, from the companionway with the steps off; refused afloat or when it is done already. Running the engine afterwards fills the raw-water side again. The season query lists the layup tasks.")]
    public string WinteriseEngine() => session.Do("winterise_engine", new WinteriseEngine());

    [McpServerTool(Name = "fill_tank"), Description(
        "Top the fuel tank up to the brim with diesel, at a port that sells it (Skjærvik and Storholmen do). Takes 30 minutes and costs the diesel (the result shows the price per litre). " +
        "A full tank has no room for damp air to condense into water over a cold winter. Refused at sea, at a port with no fuel, or when the tank is full.")]
    public string FillTank() => session.Do("fill_tank", new FillTank());

    [McpServerTool(Name = "battery_ashore"), Description(
        "Winter layup, on the hard at Skjærvik: carry the start battery to the yard's battery room, where it sits on a charger all winter. Takes 30 minutes and costs money (the result tells you what). " +
        "From the quarter berth with the battery box lid off. While it is ashore nothing aboard can start or draw on it, and she cannot be launched: fit it again with battery_aboard.")]
    public string BatteryAshore() => session.Do("battery_ashore", new BatteryAshore());

    [McpServerTool(Name = "battery_aboard"), Description(
        "On the hard at Skjærvik, from the quarter berth with the battery box lid off: fit the start battery again from the yard's battery room (30 minutes, free). It comes back full.")]
    public string BatteryAboard() => session.Do("battery_aboard", new BatteryAboard());

    [McpServerTool(Name = "winter_charger"), Description(
        "Winter layup, on the hard at Skjærvik: clip the yard's charger on the battery aboard so it is held full all winter. Takes 20 minutes and costs money (the result tells you what). It is disconnected at launch. " +
        "The other way to keep the battery from running down in the cold is battery_ashore.")]
    public string WinterCharger() => session.Do("winter_charger", new WinterCharger());

    [McpServerTool(Name = "fit_winter_cover"), Description(
        "Winter layup, on the hard, from on deck: rig the winter cover over the deck so rain and snow stay off it. Takes 2 hours; the cover costs money the first time and is kept for later winters. It comes off at launch.")]
    public string FitWinterCover() => session.Do("fit_winter_cover", new FitWinterCover());

    [McpServerTool(Name = "wait_until_spring"), Description(
        "Leave her for the winter, day by day, to the next day 75 of the year. Not at sea. You come back early if the boat has something to tell you (an alert), if a delivery is due at the yard or, if you hold a haulout booking, on the morning of the slot. " +
        "The result says how many days passed and why you stopped; read notices and the logbook for what the winter did.")]
    public string WaitUntilSpring() => session.Do("wait_until_spring", new WaitUntilSpring());

    [McpServerTool(Name = "list_for_sale"), Description(
        "Put her on the market with the broker at your asking price; calling it again changes the ask. Takes 30 minutes, free. Only on the hard at Skjærvik. You get the broker's valuation letter: " +
        "a base value, less the yard's price of every problem you have found and not fixed, less the engine years nobody wrote down, plus a kept logbook, with spring dearer than autumn. " +
        "While she is listed buyers come to look now and then (more often when you ask less than the letter says, and in spring); each viewing's surveyor goes over her and finds what you could find with your own tools, " +
        "and the buyer offers about what they found her worth, never over your ask, open for 7 days. Their findings become known problems. Launching her takes the listing off.")]
    public string ListForSale([Description("Your asking price, in money.")] int ask_nok) =>
        session.Do("list_for_sale", new ListForSale(ask_nok), ("ask_nok", ask_nok.ToString()));

    [McpServerTool(Name = "accept_offer"), Description(
        "Sell her to the best open offer, or to the numbered one (sale lists them). Costs no time. The game ends here: the closing card states what you got, what you spent, the days and hours, the trips and tows; every later command is refused, but the queries still answer. " +
        "Refused if she is not listed, the offer has lapsed or there is none open.")]
    public string AcceptOffer([Description("The number of the offer, as sale lists it. Optional; defaults to the best open one.")] int? offer = null) =>
        session.Do("accept_offer", new AcceptOffer(offer), ("offer", offer?.ToString() ?? ""));

    [McpServerTool(Name = "keep"), Description(
        "Keep her for another season instead of selling: takes her off the market, moves the launch deadline to the next day 96 of the year, and starts the logbook's maintenance count and the engine-service count again for the next sale. Free, no time. " +
        "The year ahead is told: the calendar repeats with the boat as she is, upgrades and all, and the vacation days renew at the new year. " +
        "Nothing else is reset: the clock, what the winter did to her and the money carry on. Refused once already since you last launched.")]
    public string Keep() => session.Do("keep", new Keep());

    [McpServerTool(Name = "sale", ReadOnly = true), Description(
        "The broker's valuation letter as it stands now (only what you know of her), whether she is listed and at what ask, and every viewing so far with what the buyer's surveyor found, what they offer and whether the offer is still open. Costs no time.")]
    public string Sale() => session.Ask("sale", new Sale());

    [McpServerTool(Name = "calendar", ReadOnly = true), Description(
        "The week: today's weekday and whether it is a boat day, how much of a weekday evening you have left before it costs a vacation day, the vacation days left this year, the balance and the bank's overdraft limit, " +
        "the paydays, bills, deliveries and your haulout slot coming up, what is on order, and the money of this season. Costs no time.")]
    public string Calendar() => session.Ask("calendar", new Calendar());

    [McpServerTool(Name = "next_weekend"), Description(
        "Go back to work and come back aboard at eight on the next Saturday (from a Saturday or Sunday, the Saturday after). Not at sea. Takes no vacation days. Afloat, you come back early if the boat has something to tell you; " +
        "you also come back on the morning of a booked haulout slot, a delivery at the yard or an event you marked (plan_event). The result says what came in meanwhile: pay, bills, parts.")]
    public string NextWeekend() => session.Do("next_weekend", new NextWeekend());

    [McpServerTool(Name = "wait_week"), Description(
        "Go back to work and come back aboard at eight, a week on. Not at sea. Takes no vacation days. Stops early like next_weekend (so mark the events you want to be there for with plan_event first). The result says what came in meanwhile.")]
    public string WaitWeek() => session.Do("wait_week", new WaitWeek());

    [McpServerTool(Name = "order"), Description(
        "Ring the yard chandlery and order a part it does not keep on the shelf: a part you have found, such as the starter motor. Five minutes on the phone, from anywhere but at sea, never a vacation day. " +
        "The price is paid at once and the part arrives at the yard in a few days (3 to 10); you are told the day. replace says when a part has to be ordered.")]
    public string Order([Description("Name of the part, as you would say it, e.g. starter motor.")] string item) =>
        session.Do("order", new Order(item), ("item", item));

    [McpServerTool(Name = "invite_friend"), Description(
        "Ring a friend and ask them aboard for a week starting on a game day: once a year, a week starting in July (days 180 to 203 of the year), at least three days ahead. Five minutes on the phone, from anywhere but at sea. " +
        "The week is three beats, each a day you play: on the first day, in the evening, the friend comes if she is at the mooring and the boat has a heads that works, a stove and dry bunks; the friend then wants a day trip to Hestholmen, a passage you make in the normal way with the friend aboard (a tow does not count); and on the last night, if it is cold, a working heater is wanted. " +
        "Weekdays you spend with your friend (at sea, or aboard past an evening) cost vacation days as usual; a wait leaves the friend ashore and costs nothing; the friend pays for the diesel. calendar shows the week, its beats and what each asks.")]
    public string InviteFriend([Description("The first day of the week, as a game day number, e.g. 183.")] int day) =>
        session.Do("invite_friend", new InviteFriend(day), ("day", day.ToString()));

    [McpServerTool(Name = "plan_event"), Description(
        "Mark an event of the calendar as one you are going to, or take the mark off. Free, no time, from anywhere. A wait (wait, wait_week, next_weekend) stops on the morning an event you marked begins, " +
        "so you are aboard when it starts; a wait goes straight past an event you did not mark. An event you did not mark still happens, and is still made or missed by the boat's state if you are there. " +
        "The friend's week counts as marked once you have asked your friend. calendar shows which events are marked.")]
    public string PlanEvent(
        [Description("The event, as you would say it, e.g. regatta or midsummer.")] string @event,
        [Description("True to mark it, false to take the mark off. Optional; defaults to true.")] bool going = true) =>
        going ? session.Do("plan_event", new PlanEvent(@event), ("event", @event), ("going", "true"))
              : session.Do("plan_event", new UnplanEvent(@event), ("event", @event), ("going", "false"));

    [McpServerTool(Name = "install"), Description(
        "Fit an upgrade you have aboard (a diesel heater, marine heads with a holding tank, a gas stove, a chart plotter, a marine radar, bunk cushions, varnish and brushes), yourself: go to the place it goes first. " +
        "It takes your own work, from half an hour to ten hours (a long job on a weekday is a vacation day), not at sea, and varnish needs a dry day. Order the item first (order); upgrades lists them with their status.")]
    public string Install([Description("Name of the upgrade, as you would say it, e.g. diesel heater.")] string item) =>
        session.Do("install", new Install(item), ("item", item));

    [McpServerTool(Name = "yard_install"), Description(
        "At the yard, afloat or ashore: the yard fits an upgrade for its labour and days (away time, no work of yours). The part must be aboard first (order it): the yard fits it when you bring it, for the labour only. " +
        "Not every job is the yard's (cushions and varnish are yours). upgrades shows the yard's price and days.")]
    public string YardInstall([Description("Name of the upgrade, as you would say it, e.g. marine radar.")] string item) =>
        session.Do("yard_install", new YardInstall(item), ("item", item));

    [McpServerTool(Name = "upgrades", ReadOnly = true), Description(
        "The upgrades you could give her: for each the chandlery price, your install hours, the yard's labour and days (if the yard does it), what the broker adds, what it makes possible, and whether it is aboard, on order or fitted. Costs no time.")]
    public string Upgrades() => session.Ask("upgrades", new Upgrades());

    [McpServerTool(Name = "status", ReadOnly = true), Description(
        "Day, time of day, time and money spent so far, whether the engine is running, tools owned and problems you have found. Costs no time.")]
    public string Status() => session.Ask("status", new Status());

    [McpServerTool(Name = "weather", ReadOnly = true), Description(
        "Look at the sky and the wind today: rain, temperature, wind force and direction, the dew point against the sea temperature (what they say about fog), and fog while it lies. Costs no time. Rain stops paint jobs and a gale keeps you off the deck and the yard from launching.")]
    public string Weather() => session.Ask("weather", new Weather());

    [McpServerTool(Name = "notices", ReadOnly = true), Description(
        "What the boat has told you since you last asked: a lamp, an alarm, a smell, a counter, the voltmeter. Lines marked '!' are the boat talking; they also arrive with the result of any action and in look and status. Asking marks them read. Costs no time.")]
    public string Notices() => session.Ask("notices", new Notices());

    [McpServerTool(Name = "logbook", ReadOnly = true), Description(
        "Read the logbook: everything you have done so far, with the day, time and cost. Costs no time.")]
    public string Logbook() => session.Ask("logbook", new Logbook());

    [McpServerTool(Name = "aboard", ReadOnly = true), Description(
        "Where you are, the places and covers aboard (which are off), and every part you have found, with where it is and whether you can see or reach it from here (marking any with a known problem). Costs no time.")]
    public string Aboard() => session.Ask("aboard", new Aboard());

    [McpServerTool(Name = "help", ReadOnly = true), Description(
        "List the things you can do on the boat. Costs no time.")]
    public string Help() => session.Ask("help", new Commands()) + HelpFooter;

    /// <summary>The facade lists the console's verbs; say which tool each one is here.</summary>
    private const string HelpFooter =
        "Here each command is a tool of the same name, except: start = start_engine, stop = stop_engine, check filter = check_filter, " +
        "check flow = check_flow, log = logbook, order survey = order_survey, emergency lift = emergency_lift, read survey = survey, yard fix = yard_fix, pump bilge = pump_bilge, plan trip = plan_trip, heave to = heave_to, turn back = turn_back, call tow = call_tow, book haulout = book_haulout, haul out = haul_out, yard wash = yard_wash, winterise = winterise_engine, fill tank = fill_tank, battery ashore = battery_ashore, battery aboard = battery_aboard, charger = winter_charger, fit cover = fit_winter_cover, wait until spring = wait_until_spring, next weekend = next_weekend, wait week = wait_week, invite friend = invite_friend, calendar plan = plan_event (going true) and calendar unplan = plan_event (going false), yard install = yard_install, season page = season_page, list for sale = list_for_sale, sell = list_for_sale, accept = accept_offer. " +
        "There is no quit; also available: status, aboard (what you have found so far), calendar (the week and the money), readiness, weather (today's sky and wind), inventory, trip, chart, notices (what the boat has told you), briefing.\n";

    [McpServerTool(Name = "briefing", ReadOnly = true), Description(
        "Re-read the situation you started with. Costs no time.")]
    public string Briefing() => session.Ask("briefing", new Briefing());
}
