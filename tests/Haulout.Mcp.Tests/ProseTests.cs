using Haulout.Sim.Play;
using Haulout.Sim.Data;
using ProseWriter = Haulout.Prose.Prose;

namespace Haulout.Mcp.Tests;

public class ProseTests
{
    private static readonly ProseWriter P = new(Currency.Default);

    [Fact]
    public void Aboard_renders_place_covers_and_known_problems_but_no_ids()
    {
        var view = new AboardView(
            "Quarter berth",
            [
                new AboardPlace("Cockpit", false, true, true, ["Companionway", "On deck"]),
                new AboardPlace("Quarter berth", true, true, true, ["Companionway"]),
                new AboardPlace("Beside the hull", false, false, false, ["On deck"]),
            ],
            [
                new AboardCover("Battery-box lid", "Quarter berth", true),
                new AboardCover("Quarter-berth panel", "Quarter berth", false),
            ],
            [
                new FoundComponent("Negative terminal", "Under the quarter berth", true, true, true),
                new FoundComponent("Starter solenoid", "Engine bay, quarter-berth side", false, true, false),
                new FoundComponent("Fuel tank", "Under the cockpit locker", false, false, false),
            ]);

        var text = P.Aboard(view);

        Assert.Contains("You're in the quarter berth.", text);
        Assert.Contains("Quarter berth (you are here", text);
        Assert.Contains("Cockpit (been there)", text);
        Assert.Contains("Battery-box lid (Quarter berth): off", text);
        Assert.Contains("Quarter-berth panel (Quarter berth): in place", text);
        Assert.Contains("Negative terminal (known problem) - Under the quarter berth; within reach", text);
        Assert.Contains("Starter solenoid - Engine bay, quarter-berth side; in sight", text);
        Assert.Contains("Fuel tank - Under the cockpit locker", text);
        Assert.DoesNotContain("Fuel tank - Under the cockpit locker;", text);
        Assert.DoesNotContain("Starter solenoid (known", text);
        Assert.DoesNotContain("quarter-berth\n", text);
        Assert.DoesNotContain("battery-box-lid", text);
        Assert.DoesNotContain("negative-terminal", text);
    }

    [Fact]
    public void Status_shows_the_place()
    {
        var view = new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Cockpit");
        var text = P.Query(view);
        Assert.Contains("You're in the cockpit.", text);
    }

    [Fact]
    public void Status_uses_the_right_preposition_and_work_wording()
    {
        var deck = P.Query(new StatusView(95, "10:11", 72, 400, null, false, false, [], [], "On deck"));
        Assert.Contains("You're on deck.", deck);
        Assert.DoesNotContain("in the on", deck);
        Assert.Contains("Day 95, 10:11 · worked 1 h 12 min · money spent 400 NOK.", deck);
        Assert.Contains("You're beside the hull.", P.Query(new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Beside the hull")));
        Assert.Contains("You're on deck.", P.Aboard(new AboardView("On deck", [], [], [])));
    }

    [Fact]
    public void Logbook_header_status_and_weather_view_carry_the_weather()
    {
        var game = Game.NewFirstFault();
        var weather = (WeatherView)game.Ask(new Haulout.Sim.Play.Weather());
        var log = P.Query(game.Ask(new Logbook())).Replace("\r\n", "\n");
        // The logbook's header keeps the day's weather short: the dew point and the sea are for the weather report and the status.
        string header = weather.Text[..weather.Text.IndexOf(" Dew point", StringComparison.Ordinal)];
        Assert.StartsWith($"Day 75 \u00b7 {header}\n", log);
        Assert.Contains(weather.Text, P.Query(game.Ask(new Status())));
        Assert.Equal(weather.Text + Environment.NewLine, P.Query(weather));
    }

    [Fact]
    public void Briefing_help_hint_is_optional()
    {
        var view = new BriefingView(["One.", "Two."]);
        Assert.Contains("Type 'help'", P.Briefing(view));
        Assert.DoesNotContain("Type 'help'", P.Briefing(view, includeHelpHint: false));
    }

    [Fact]
    public void Result_matches_console_format()
    {
        var game = Game.NewFirstFault();
        var result = game.Execute(new GoTo("companionway"));
        var status = (StatusView)game.Ask(new Status());
        var text = P.Result(result, status).Replace("\r\n", "\n");
        Assert.EndsWith("  [+1 min · 08:01]\n", text);
    }

    [Fact]
    public void Money_is_written_with_the_games_currency_label()
    {
        var view = new SurveyReportView(true, 3, "Henrik Dahl, marine surveyor", "Sea Wren", 5600, ["Dear owner,"], [], ["Regards."]);
        Assert.Contains("Fee: \u20ac5600.", new ProseWriter(new Currency("EUR", "\u20ac", true)).Query(view));
        Assert.Contains("Fee: 5600 kr.", new ProseWriter(new Currency("SEK", "kr", false)).Query(view));
        Assert.Contains("Fee: 5600 NOK.", new ProseWriter(Currency.Default).Query(view));
    }

    [Fact]
    public void Survey_letter_renders_items_and_unordered_hint()
    {
        var view = new SurveyReportView(true, 3, "Henrik Dahl, marine surveyor", "Sea Wren", 5600,
            ["Dear owner,", "Here is what I found."],
            [new SurveyItemView(1, "Anodes", "Note", "Worn."), new SurveyItemView(3, "Raw-water seacock", "Before launch", "Stiff handle, weeping.")],
            ["Regards."]);
        var text = P.Query(view).Replace("\r\n", "\n");
        Assert.Contains("Henrik Dahl, marine surveyor\n", text);
        Assert.Contains("Survey of Sea Wren, day 3. Fee: 5600 NOK.", text);
        Assert.Contains("3. Raw-water seacock [Before launch]: Stiff handle, weeping.", text);
        Assert.Contains("Here is what I found.", text);
        Assert.EndsWith("Regards.\n", text);
        var none = P.Query(new SurveyReportView(false, 0, "", "", 0, [], [], []));
        Assert.Contains("No survey yet. 'order survey' hires the surveyor.", none);
    }

    [Fact]
    public void Readiness_shows_consequence_and_both_costs_without_ids()
    {
        var item = new ReadinessItem("Weeping raw-water seacock", "Raw-water seacock", "Deferred", 1200, 12800,
            "afloat: needs an emergency lift", true, "She could flood when the engine runs.",
            [new FixRoute("DIY", "Swap the seacock yourself", 1200, 180, 0, true, null),
             new FixRoute("Yard", "The yard refits it", 2500, 0, 4, false, "not enough money")]);
        var view = new ReadinessView(10, 96, 86, 15000, false, [item],
            [new InvestigateItem("Bilge pump", "Sounds tired.")], ["Loose clamp"]);
        var text = P.Query(view).Replace("\r\n", "\n");
        Assert.Contains("Weeping raw-water seacock (Raw-water seacock) - deferred", text);
        Assert.Contains("Now: 1200 NOK. Later: 12800 NOK, afloat: needs an emergency lift. On the hard only.", text);
        Assert.Contains("If left: She could flood when the engine runs.", text);
        Assert.Contains("DIY: Swap the seacock yourself, 1200 NOK, 3 h", text);
        Assert.Contains("Yard: The yard refits it, 2500 NOK, 4 days (not available: not enough money)", text);
        Assert.Contains("To look into:", text);
        Assert.Contains("Bilge pump: Sounds tired.", text);
        Assert.Contains("Done: Loose clamp.", text);
        Assert.Contains("86 days to go", text);
        Assert.DoesNotContain("raw-water-seacock", text);
        Assert.Contains("No known problems.", P.Query(new ReadinessView(1, 96, 95, 100, false, [], [], [])));
    }

    [Fact]
    public void A_diy_route_names_where_the_part_stands_when_it_is_not_on_the_shelf()
    {
        var item = new ReadinessItem("Starter soaked in bilge water", "Starter motor", "Discovered", 3800, 9000, null, false, "She won't start.",
            [new FixRoute("DIY", "Fit a new starter motor", 3800, 120, 0, true, null, "not on the yard's shelf: order it first, 3 to 10 days"),
             new FixRoute("Yard", "The yard fits one", 5000, 0, 4, true, null)]);
        var text = P.Query(new ReadinessView(80, 96, 16, 25000, false, [item], [], []));
        Assert.Contains("DIY: Fit a new starter motor (not on the yard's shelf: order it first, 3 to 10 days), 3800 NOK, 2 h", text);
        Assert.Contains("Yard: The yard fits one, 5000 NOK, 4 days", text);
        var onShelf = new ReadinessItem("Tired impeller", "Raw-water impeller", "Discovered", 350, 4500, null, false, "x",
            [new FixRoute("DIY", "Fit a new impeller", 350, 60, 0, true, null)]);
        Assert.Contains("DIY: Fit a new impeller, 350 NOK, 1 h", P.Query(new ReadinessView(80, 96, 16, 25000, false, [onShelf], [], [])));
    }

    [Fact]
    public void A_plan_by_another_port_says_so_in_the_first_line_and_names_the_legs()
    {
        var game = Game.NewFirstFault();
        var text = P.Query(game.Ask(new PlanTrip("Kvitnes")));
        Assert.Contains("Skjærvik to Kvitnes by Hestholmen: 23 nm. Leaving day 75.", text);
        Assert.Contains("The legs: Skjærvik to Hestholmen, 14 nm on 040; then Hestholmen to Kvitnes, 9 nm on 120. A passage is one leg: you depart again from Hestholmen.", text);
        Assert.Contains("About 4.6 hours under engine", text);
        // A direct plan keeps its old line.
        Assert.Contains("Skjærvik to Hestholmen: 14 nm on a course of 40 degrees.", P.Query(game.Ask(new PlanTrip("Hestholmen"))));
        Assert.Contains("(by Hestholmen: 23 nm along the routes)", P.Query(game.Ask(new Chart())));
    }

    [Fact]
    public void The_calendar_says_which_events_are_going_and_a_made_one_says_nothing_of_it()
    {
        var game = Game.NewFirstFault();
        var plain = P.Query(game.Ask(new Calendar()));
        Assert.Contains("not marked: a wait goes past it", plain);
        game.Execute(new PlanEvent("Midsummer"));
        var text = P.Query(game.Ask(new Calendar()));
        Assert.Contains("Midsummer at Storholmen, Friday and Saturday, days 172 to 173: ahead; going: a wait stops on its morning.", text);
        Assert.Contains("The regatta party at Hestholmen, Saturday, day 152: ahead; not marked: a wait goes past it.", text);
    }

    [Fact]
    public void Calendar_renders_the_week_the_money_the_list_ahead_and_the_orders_without_ids()
    {
        var money = new MoneyView(12400, 6000, 3800, 3000, 0, 0, [new LedgerEntry(90, "income", "Payday.", 6000), new LedgerEntry(90, "bill", "Berth and mooring at Skjærvik, the quarter", -3000)]);
        var view = new CalendarView(
            89, "Wednesday", false, "Wednesday, day 89: a weekday. An evening aboard: 2 h 10 min left before it costs a vacation day.", 130, 22, 25, 3, 0,
            12400, 20000, 6000, 90,
            [new CalendarEntry(90, "Thursday", "payday", "Payday", 6000), new CalendarEntry(92, "Saturday", "delivery", "The starter motor is due at the yard", 0)],
            [new OrderView("starter motor", 85, 92, "on its way")], money);
        var text = P.Query(view);
        Assert.Contains("Wednesday, day 89: a weekday. An evening aboard: 2 h 10 min left before it costs a vacation day.", text);
        Assert.Contains("Vacation days: 22 of 25 left this year (3 used).", text);
        Assert.Contains("Balance 12400 NOK (the bank allows 20000 overdrawn).", text);
        Assert.Contains("Day 90, Thursday: Payday +6000 NOK.", text);
        Assert.Contains("Day 92, Saturday: The starter motor is due at the yard.", text);
        Assert.Contains("starter motor: ordered day 85, due day 92 (on its way).", text);
        Assert.Contains("Day 90: Berth and mooring at Skjærvik, the quarter -3000 NOK.", text);
        Assert.DoesNotContain("starter-motor", text);
    }

    [Fact]
    public void Upgrades_render_status_price_hours_the_yard_and_what_they_open_without_ids()
    {
        var view = new UpgradesView(
        [
            new UpgradeView("diesel heater", "on order, due day 92", 14000, 600, 4000, 3, "the friend's week in a cold July", 8000),
            new UpgradeView("bunk cushions", "fitted on day 100", 5500, 30, null, null, "dry bunks for the friend", 2000),
        ]);
        var text = P.Query(view);
        Assert.Contains("diesel heater (on order, due day 92): 14000 NOK;", text);
        Assert.Contains("; 10 h of your own work, or", text);
        Assert.Contains("; 30 min of your own work, or not a job for the yard;", text);
        Assert.Contains("or the yard 4000 NOK labour and 3 days;", text);
        Assert.Contains("adds 8000 NOK to her value; opens the friend's week in a cold July.", text);
        Assert.Contains("bunk cushions (fitted on day 100): 5500 NOK;", text);
        Assert.Contains("not a job for the yard", text);
        Assert.DoesNotContain("diesel-heater", text);
        Assert.Contains("nothing to fit", P.Query(new UpgradesView([])));
    }

    [Fact]
    public void Calendar_renders_the_events_with_their_requirements_in_words_not_symbols()
    {
        var money = new MoneyView(12400, 0, 0, 0, 0, 0, []);
        var events = new List<EventView>
        {
            new("The club cruise to Kvitnes", "Kvitnes", 215, 216, "Saturday and Sunday, days 215 to 216", "ahead",
                [new Requirement("anchor gear in order", true, "no known problem"), new Requirement("a bilge pump that works", false, "Burnt-out bilge pump")], ""),
            new("A friend's week aboard", "Skjærvik", 180, 203, "a week of your choice in July, days 180 to 203", "ahead",
                [new Requirement("warm enough below, if the week is cold", null, "depends on the weather")], ""),
            new("The regatta party at Hestholmen", "Hestholmen", 152, 152, "Saturday, day 152", "made", [], "By evening the regatta party has spilled down onto the jetty."),
        };
        var view = new CalendarView(
            89, "Wednesday", false, "Wednesday, day 89: a weekday.", 130, 22, 25, 3, 0, 12400, 20000, 6000, 90, [], [], money, events);
        var text = P.Query(view);
        Assert.Contains("Worth sailing for:", text);
        Assert.Contains("The club cruise to Kvitnes, Saturday and Sunday, days 215 to 216: ahead; not marked: a wait goes past it.", text);
        Assert.Contains("anchor gear in order: met (no known problem).", text);
        Assert.Contains("a bilge pump that works: not met (Burnt-out bilge pump).", text);
        Assert.Contains("warm enough below, if the week is cold: not known yet (depends on the weather).", text);
        Assert.Contains("A friend's week aboard at Skjærvik, a week of your choice in July", text);
        Assert.Contains("The regatta party at Hestholmen, Saturday, day 152: made.", text);
        Assert.Contains("By evening the regatta party has spilled down onto the jetty.", text);
        Assert.DoesNotContain("✓", text);
        Assert.DoesNotContain("✗", text);
    }

    [Fact]
    public void Calendar_says_to_ask_the_friend_first_before_the_week_is_picked_and_lists_the_beats_after()
    {
        var money = new MoneyView(12400, 0, 0, 0, 0, 0, []);
        var unasked = new EventView("A friend's week aboard", "Skjærvik", 180, 203, "a week of your choice in July, days 180 to 203", "ahead",
            [new Requirement("a heads that works", true, "no known problem")], "", ToBePicked: true);
        var asked = new EventView("A friend's week aboard", "Skjærvik", 187, 193, "Saturday to Friday, days 187 to 193", "made",
            [new Requirement("a heads that works", true, "no known problem")], "", Going: true,
            Beats:
            [
                new BeatView("Your friend comes aboard", "Saturday, day 187, about 17:00", "made", ""),
                new BeatView("A day trip to Hestholmen", "any day of the week", "on now", ""),
                new BeatView("The last night aboard", "Friday, day 193, about 22:00", "ahead", "warm enough below on the last night, if it is cold"),
            ], Outcome: "a week: one of the day trip and the last night");
        CalendarView View(EventView e) => new(89, "Wednesday", false, "Wednesday, day 89: a weekday.", 130, 22, 25, 3, 0, 12400, 20000, 6000, 90, [], [], money, [e]);

        var before = P.Query(View(unasked));
        Assert.Contains("ask your friend first (invite friend <day>, days 180 to 203)", before);
        Assert.DoesNotContain("a wait goes past it", before);

        var after = P.Query(View(asked));
        Assert.DoesNotContain("ask your friend first", after);
        Assert.Contains("A friend's week aboard at Skjærvik, Saturday to Friday, days 187 to 193: made (a week: one of the day trip and the last night).", after);
        Assert.Contains("Your friend comes aboard, Saturday, day 187, about 17:00: made.", after);
        Assert.Contains("A day trip to Hestholmen, any day of the week: on now.", after);
        Assert.Contains("The last night aboard, Friday, day 193, about 22:00: ahead (warm enough below on the last night, if it is cold).", after);
    }

    [Fact]
    public void Status_prints_the_fuel_gauge_and_the_plan_prints_fuel_warnings_and_the_other_end()
    {
        var status = P.Query(new StatusView(75, "08:00", 0, 0, null, true, true, [], [], "Cockpit", Fuel: "Fuel: about 25 litres (a little under half a tank)."));
        Assert.Contains("Fuel: about 25 litres (a little under half a tank).", status);
        Assert.DoesNotContain("Fuel:", P.Query(new StatusView(75, "08:00", 0, 0, null, true, true, [], [], "Cockpit")));

        var plan = SamplePlan() with
        {
            Fuel = "About 25 litres aboard: some 10 hours of motoring. This passage needs about 7 litres.",
            Warnings = ["She'll arrive after dark (the light goes at about 22:30).", "This passage costs 1 vacation day (Friday). You have 23 left."],
            AtDestination = ["The club cruise to Kvitnes (Saturday and Sunday, days 215 to 216) asks for anchor gear in order (no known problem).", "An anchorage: no shop, no fuel."],
        };
        var text = P.Query(plan);
        Assert.Contains("About 25 litres aboard: some 10 hours of motoring.", text);
        Assert.Contains("Mind: She'll arrive after dark (the light goes at about 22:30).", text);
        Assert.Contains("Mind: This passage costs 1 vacation day (Friday). You have 23 left.", text);
        Assert.Contains("At Hestholmen: An anchorage: no shop, no fuel.", text);
        Assert.Contains("At Hestholmen: The club cruise to Kvitnes", text);
        Assert.DoesNotContain("Mind:", P.Query(SamplePlan()));
    }

    [Fact]
    public void Status_shows_the_week_and_what_is_next_only_when_there_is_a_calendar()
    {
        var text = P.Query(new StatusView(75, "08:00", 0, 0, null, false, false, [], [], "Cockpit", 25000, 96, Week: "Saturday, day 75: a weekend, a boat day.", VacationDaysLeft: 25, Next: "Next: payday on day 90, 6000 NOK."));
        Assert.Contains("Saturday, day 75: a weekend, a boat day.", text);
        Assert.Contains("Next: payday on day 90, 6000 NOK.", text);
        var plain = P.Query(new StatusView(75, "08:00", 0, 0, null, false, false, [], [], "Cockpit"));
        Assert.DoesNotContain("Next:", plain);
        Assert.DoesNotContain("weekend", plain);
    }

    [Fact]
    public void Status_shows_money_deadline_and_launch()
    {
        var view = new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Cockpit", 9000, 96, true, true);
        var text = P.Query(view);
        Assert.Contains("Money left: 9000 NOK. Launch deadline: day 96.", text);
        Assert.Contains("She's in the water.", text);
        Assert.DoesNotContain("Money left", P.Query(new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Cockpit")));
    }

    [Fact]
    public void Status_shows_a_negative_balance_as_debt()
    {
        var view = new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Cockpit", -2400, 96, true, true);
        var text = P.Query(view);
        Assert.Contains("You owe 2400 NOK. Launch deadline: day 96.", text);
        Assert.DoesNotContain("-2400", text);
    }

    [Fact]
    public void Log_hints_that_a_document_can_be_reread()
    {
        var text = P.Query(new LogView([new LogEntry(2, "09:00", "Survey report arrived.", 0, "survey report")]));
        Assert.Contains("(read survey)", text);
    }

    [Fact]
    public void Result_prints_cost_and_notes_even_when_finished_and_uses_days()
    {
        var status = new StatusView(5, "08:00", 0, 0, null, false, false, [], [], "Cockpit");
        var result = new CommandResult(true, "wait", ["Days pass."], 4 * 1440, 300, null,
            [new KnownFault("f-1", "Weeping seacock", "Raw-water seacock")], false, true, []);
        var text = P.Result(result, status).Replace("\r\n", "\n");
        Assert.Contains("  Noted in the log: Weeping seacock.", text);
        Assert.Contains("  [+4 days, 300 NOK · 08:00]", text);
        Assert.DoesNotContain("f-1", text);
    }

    [Fact]
    public void Result_prints_alerts_after_the_stamp_and_before_the_narrative_for_a_look()
    {
        var status = new StatusView(5, "08:00", 0, 0, null, false, false, [], [], "Cockpit");
        var alerts = new[] { new Alert(5, "07:00", "Water over the saloon sole.") };
        var wait = new CommandResult(true, "wait", ["Days pass."], 1440, 0, null, [], false, false, alerts);
        var text = P.Result(wait, status).Replace("\r\n", "\n");
        Assert.Contains("  ! Water over the saloon sole.\n", text);
        Assert.True(text.IndexOf("Days pass.", StringComparison.Ordinal) < text.IndexOf("  ! ", StringComparison.Ordinal));
        Assert.True(text.IndexOf("[+1 day", StringComparison.Ordinal) < text.IndexOf("  ! ", StringComparison.Ordinal));

        var look = new CommandResult(true, "look", ["You look around."], 2, 0, null, [], false, false, alerts);
        var lookText = P.Result(look, status).Replace("\r\n", "\n");
        Assert.StartsWith("  ! Water over the saloon sole.\n", lookText);

        var refused = new CommandResult(false, "go", ["No."], 0, 0, null, [], false, false, []);
        Assert.DoesNotContain("  ! ", P.Result(refused, status));
    }

    [Fact]
    public void Status_and_notices_print_alerts_and_the_logbook_marks_them()
    {
        var alert = new Alert(5, "07:00", "A damp, sour smell from under the saloon sole.");
        var status = new StatusView(5, "08:00", 0, 0, null, false, false, [], [], "Cockpit", Notices: [alert]);
        Assert.StartsWith("  ! A damp, sour smell", P.Query(status).Replace("\r\n", "\n"));
        Assert.Contains("  ! A damp", P.Query(new NoticesView([alert])));
        Assert.Contains("Nothing new from the boat.", P.Query(new NoticesView([])));
        Assert.Contains("Day 5 07:00  ! A damp", P.Query(new LogView([new LogEntry(5, "07:00", alert.Text, Alert: true)])));
    }

    private static TripPlanView SamplePlan(bool blocked = false) => new(
        "Skjærvik", "Hestholmen", 14, 40, 150, 2.8, 3.1, "About 2.8 hours under engine, or 3.1 under sail.",
        [new ForecastDay(150, true, "Bright, force 3 from the southwest.", 3, "southwest", false, true), new ForecastDay(151, false, "No forecast that far out.", 0, "", false, false)],
        [new TripRisk("Tired impeller", "Impeller", "Under load for hours the engine will run hot.", "impeller", false)],
        ["hose clamps (10) x1"], ["impeller"], ["Lyngskjær, 7 nm out"], 4200, 6000,
        blocked ? ["She's still on the hard."] : []);

    [Fact]
    public void Plan_renders_route_forecast_readiness_spares_and_tow_price()
    {
        var text = P.Query(SamplePlan());
        Assert.Contains("Skjærvik to Hestholmen: 14 nm", text);
        Assert.Contains("About 2.8 hours under engine", text);
        Assert.Contains("Day 150: Bright, force 3 from the southwest. The wind would serve under sail.", text);
        Assert.Contains("Day 151: No forecast that far out.", text);
        Assert.Contains("Readiness for this trip:", text);
        Assert.Contains("Tired impeller (Impeller): Under load for hours the engine will run hot. [no spare aboard (impeller)]", text);
        Assert.Contains("Spares aboard: hose clamps (10) x1.", text);
        Assert.Contains("Spares you don't have for the known problems: impeller.", text);
        Assert.Contains("Lyngskjær, 7 nm out", text);
        Assert.Contains("If it goes wrong, a tow costs 6000 NOK.", text);
        Assert.StartsWith("Skjærvik", text);
        Assert.StartsWith("Can't go yet: She's still on the hard.", P.Query(SamplePlan(blocked: true)));
        Assert.EndsWith(Environment.NewLine, text);
    }

    [Fact]
    public void Inventory_renders_aboard_shop_and_the_sea()
    {
        var port = P.Query(new InventoryView([new InventoryLine("remote start button", 1, true), new InventoryLine("impeller", 2, false)],
            "the yard chandlery", [new ShopLine("fuses (5)", 60, 15)]));
        Assert.Contains("remote start button (tool)", port);
        Assert.Contains("impeller x2", port);
        Assert.Contains("For sale at the yard chandlery:", port);
        Assert.Contains("fuses (5): 60 NOK, 15 min", port);
        var sea = P.Query(new InventoryView([], null, []));
        Assert.Contains("Nothing for sale out here.", sea);
    }

    [Fact]
    public void Trip_and_status_say_where_she_is()
    {
        var text = P.Query(new TripView(true, null, "Skjærvik", "Hestholmen", 5, 9, "under engine", 1, false, "Lyngskjær, 2.0 nm ahead", true, "You're at sea."));
        Assert.Contains("You're at sea.", text);
        Assert.Contains("5 nm out from Skjærvik, 9 nm to Hestholmen. Under engine in hour 1.", text);
        Assert.Contains("She's at sea.", P.Query(new StatusView(80, "10:00", 0, 0, null, true, true, [], [], "Cockpit", AtSea: true)));
        Assert.Contains("at Skjærvik", P.Query(new StatusView(80, "10:00", 0, 0, null, true, true, [], [], "Cockpit", Afloat: true, Port: "Skjærvik")));
    }

    [Fact]
    public void A_leg_line_comes_through_and_the_trip_card_adds_a_summary_without_repeating()
    {
        const string hour = "Hour 1. Force 3 from the southwest, small wavelets from astern. The engine note is steady. 9.0 nm to Hestholmen.";
        var leg = new CommandResult(true, "continue", [hour], 60, 0,
            new LegView(1, "under engine", 5, 9, 3, "southwest", 2, hour), [], true, true, []);
        var legText = P.Result(leg, new StatusView(80, "10:00", 0, 0, null, true, true, [], [], "Cockpit"));
        Assert.Contains(hour, legText);

        const string alarm = "The temperature alarm whines.";
        var card = new TripResultView("Hestholmen", false, false, 190, 350, [alarm, "The battery is low."], ["Replaced the impeller from the spares aboard."],
            [], ["The party is in full swing."]);
        var res = new CommandResult(true, "continue", ["Hour 4.", "You pick up a mooring and stop the engine.", "The party is in full swing.", alarm],
            60, 0, card, [], false, true, []);
        var text = P.Result(res, new StatusView(80, "13:00", 0, 0, null, false, true, [], [], "Cockpit"));
        Assert.Contains("Arrived at Hestholmen: 4 hours since casting off, 350 NOK spent.", text);
        Assert.Contains("Trouble on the way: The battery is low.", text);
        Assert.DoesNotContain("Trouble on the way: The temperature alarm", text);
        Assert.Contains("Put right at sea: Replaced the impeller from the spares aboard.", text);
        Assert.Single(text.Split('\n'), l => l.Contains("The party is in full swing."));
    }

    [Fact]
    public void The_season_view_prints_its_line_and_the_prices_and_the_status_carries_the_season_line()
    {
        var view = new SeasonView(262, 1, 262, "autumn", "Autumn. Haulout booked for day 270, 8 days away.", true, false, 96, 270, null,
            255, 300, 7000, 9000, 500, 9000, false, [], false, null, 0, false);
        var text = P.Query(view);
        Assert.Contains("Autumn. Haulout booked for day 270, 8 days away.", text);
        Assert.Contains("The yard lifts boats from day 255 to day 300.", text);
        Assert.Contains("A lift costs 7000 NOK and winter storage on the hard 9000 NOK; changing a booking costs 500 NOK; the emergency lift costs 9000 NOK.", text);

        var status = P.Query(new StatusView(262, "08:00", 0, 0, null, false, true, [], [], "Cockpit", Season: view.Text));
        Assert.Contains(view.Text, status);
        Assert.DoesNotContain("Autumn.", P.Query(new StatusView(262, "08:00", 0, 0, null, false, true, [], [], "Cockpit")));
    }

    [Fact]
    public void The_season_view_lists_the_winter_tasks_done_and_to_do_with_their_command_minutes_price_and_risk()
    {
        var tasks = new[]
        {
            new LayupTask("Winterise the engine", true, 90, 250, "winterise", "Raw water left in the engine can freeze."),
            new LayupTask("Fill the tank", false, 30, 484, "fill tank", "A part-empty tank breathes damp air."),
            new LayupTask("Change the oil", false, 45, 0, "replace engine oil", "Old oil sits in the sump."),
        };
        var view = new SeasonView(270, 1, 270, "autumn", "Autumn. She is laid up on the hard for the winter.", false, true, 96, null, null,
            255, 300, 7000, 9000, 500, 9000, true, tasks, false, null, 0, false);
        var text = P.Query(view);
        Assert.Contains("The winter work on her:", text);
        Assert.Contains("  Done: Winterise the engine.", text);
        Assert.Contains("  To do: Fill the tank (fill tank; 30 min, 484 NOK). Left alone: A part-empty tank breathes damp air.", text);
        Assert.Contains("  To do: Change the oil (replace engine oil; 45 min). Left alone: Old oil sits in the sump.", text);
        Assert.DoesNotContain("The winter work", P.Query(view with { Tasks = [] }));
    }

    [Fact]
    public void The_lift_card_adds_the_bill_and_the_trip_state_lists_the_routes_out()
    {
        var card = new HauloutView(270, 7000, 9000, "Under the waterline: clean paint.", ["The crane lifts her out.", "Under the waterline: clean paint."]);
        var res = new CommandResult(true, "haul-out", ["The crane lifts her out.", "Under the waterline: clean paint."], 120, 16000, card, [], false, true, []);
        var text = P.Result(res, new StatusView(270, "10:00", 0, 0, null, false, true, [], [], "Cockpit"));
        Assert.Contains("Lift 7000 NOK, winter storage 9000 NOK: 16000 NOK in all, in the logbook.", text);
        Assert.Single(text.Split('\n'), l => l.StartsWith("Under the waterline: clean paint.", StringComparison.Ordinal));

        var trip = P.Query(new TripView(false, "Skjærvik", null, null, 0, 0, null, 0, false, null, false, "She lies at Skjærvik.",
            ["Hestholmen, 14 nm", "Storholmen, 26 nm"]));
        Assert.Contains("Routes out: Hestholmen, 14 nm; Storholmen, 26 nm.", trip);
        Assert.DoesNotContain("Routes out", P.Query(new TripView(false, "Skjærvik", null, null, 0, 0, null, 0, false, null, false, "She lies at Skjærvik.")));
    }

    private static ValuationLetter SomeLetter(int? ask) => new(
        85, "Holm & Datter, yacht brokers",
        [
            new ValuationLine("Base value of a 28-footer of her kind", 180000),
            new ValuationLine("Seized raw-water seacock, at the yard's price", -4800),
            new ValuationLine("Five years of engine hours with no service entry, at 3000 NOK each", -15000),
        ],
        160200, ask, ["A buyer's surveyor will go over her for themselves."]);

    [Fact]
    public void The_listing_prints_the_letter_as_a_letter_with_the_lines_the_value_the_ask_and_the_brokers_words()
    {
        var letter = SomeLetter(150000);
        var res = new CommandResult(true, "list-for-sale", ["You list her at 150000 NOK."], 30, 0, new ListingView(letter), [], false, false, []);
        var text = P.Result(res, new StatusView(85, "08:30", 30, 0, null, false, false, [], [], "Cockpit"));
        Assert.Contains("Holm & Datter, yacht brokers", text);
        Assert.Contains("Valuation, day 85.", text);
        Assert.Contains("  Base value of a 28-footer of her kind: +180000 NOK", text);
        Assert.Contains("  Seized raw-water seacock, at the yard's price: -4800 NOK", text);
        Assert.Contains("The broker values her at 160200 NOK.", text);
        Assert.Contains("You are asking 150000 NOK, 10200 NOK under that.", text);
        Assert.Contains("A buyer's surveyor will go over her for themselves.", text);
        Assert.Contains("You are asking 170000 NOK, 9800 NOK over that.", P.Letter(SomeLetter(170000)));
        Assert.DoesNotContain("asking", P.Letter(SomeLetter(null)));
    }

    [Fact]
    public void The_sale_view_prints_the_listing_the_viewings_with_their_findings_and_whether_each_offer_is_open()
    {
        var viewings = new[]
        {
            new ViewingReport(1, 80, ["Water in the fuel tank: 2800 NOK at the yard.", "Slipping alternator belt: 450 NOK at the yard."], 152000, 140000, 87, false),
            new ViewingReport(2, 84, [], 160200, 150000, 91, true),
        };
        var text = P.Query(new SaleView(true, 150000, SomeLetter(150000), viewings, false));
        Assert.Contains("Listed for sale at 150000 NOK.", text);
        Assert.Contains("Viewing 1, day 80: their surveyor found:", text);
        Assert.Contains("  Water in the fuel tank: 2800 NOK at the yard.", text);
        Assert.Contains("They value her at 152000 NOK and offer 140000 NOK, which lapsed on day 87.", text);
        Assert.Contains("Viewing 2, day 84: their surveyor found nothing wrong.", text);
        Assert.Contains("They value her at 160200 NOK and offer 150000 NOK, open until day 91.", text);
        Assert.Contains("'accept' takes the best open offer", text);

        Assert.Contains("Not listed for sale.", P.Query(new SaleView(false, null, SomeLetter(null), [], false)));
        Assert.Contains("No one has been to look at her yet.", P.Query(new SaleView(true, 150000, SomeLetter(150000), [], false)));
        var sold = P.Query(new SaleView(false, null, null, viewings, true));
        Assert.Contains("She's sold.", sold);
        Assert.DoesNotContain("accept", sold);
    }

    [Fact]
    public void The_closing_card_is_the_narrative_plus_the_balance_and_the_game_over_note_and_a_viewing_document_reads_as_the_sale()
    {
        string[] lines = ["Sold on day 85 for 150000 NOK, to the buyer who viewed her on day 80.", "You spent 4200 NOK on her over 10 days, and 3 hours of your own work.", "Trips made: 0. Tows: 0."];
        var card = new ClosingView(85, 150000, 4200, 170800, 10, 3, 0, 0, lines);
        var res = new CommandResult(true, "accept-offer", ["You shake hands.", .. lines], 0, 0, card, [], false, false, []);
        var text = P.Result(res, new StatusView(85, "08:30", 180, 4200, null, false, false, [], [], "Cockpit", Sold: true));
        foreach (var line in lines) Assert.Single(text.Split('\n'), l => l.TrimEnd() == line);
        Assert.Contains("Money left: 170800 NOK. The game is over; the logbook still reads.", text);
        Assert.Contains("She's sold.", P.Query(new StatusView(85, "08:30", 180, 4200, null, false, false, [], [], "Cockpit", Sold: true)));

        var log = P.Query(new LogView([new LogEntry(80, "12:00", "A buyer looked her over.", 0, "viewing 1"), new LogEntry(81, "09:00", "Surveyed.", 0, "survey report")]));
        Assert.Contains("A buyer looked her over. (sale)", log);
        Assert.Contains("Surveyed. (read survey)", log);
    }

    // ------------------------------------------------------------------ the second pass (design note 008)

    [Fact]
    public void Readiness_leads_with_the_launch_deadline_before_the_first_launch_and_with_the_season_after()
    {
        // Before the first launch: today's deadline line, unchanged.
        var before = P.Query(new ReadinessView(10, 96, 86, 15000, false, [], [], []));
        Assert.Contains("Day 10. Launch deadline: day 96 (86 days to go). Money left: 15000 NOK. She's ashore.", before);

        // Launched: the season leads and the stale deadline is not mentioned, in each of the states.
        var states = new[]
        {
            "Haulout booked for day 258.",
            "No slot booked: the yard lifts from day 255 to day 300; the first free slot is day 258.",
            "No slot booked: the yard is full for the autumn; the emergency lift is all that's left.",
            "Winter: 3 of 5 tasks done. Left: Fill the tank; Fit the winter cover.",
            "She's sold.",
        };
        foreach (var lead in states)
        {
            var text = P.Query(new ReadinessView(269, 96, -173, 15000, true, [], [], [], Afloat: true, SeasonLead: lead));
            Assert.Contains($"Day 269. {lead} Money left: 15000 NOK. She's in the water.", text);
            Assert.DoesNotContain("deadline", text);
            Assert.DoesNotContain("days past", text);
        }

        // After a Keep the lead is null and the new deadline shows again.
        var kept = P.Query(new ReadinessView(300, 456, 156, 15000, true, [], [], []));
        Assert.Contains("Launch deadline: day 456 (156 days to go).", kept);
    }

    [Fact]
    public void Status_marks_a_fixed_problem_and_keeps_the_open_ones_plain()
    {
        var status = new StatusView(5, "08:00", 0, 0, null, false, false,
            [new KnownFault("f-1", "Weeping seacock", "Raw-water seacock", Fixed: true), new KnownFault("f-2", "Tired impeller", "Impeller")],
            [], "Cockpit");
        var text = P.Query(status);
        Assert.Contains("  Known: Weeping seacock (Raw-water seacock) - fixed", text);
        Assert.Contains("  Known: Tired impeller (Impeller)\n", text.Replace("\r\n", "\n"));
        Assert.DoesNotContain("f-1", text);
    }

    [Fact]
    public void Status_leads_with_the_season_once_launched_and_the_deadline_before()
    {
        var before = P.Query(new StatusView(5, "08:00", 0, 0, null, false, false, [], [], "Cockpit", 1000, 96));
        Assert.Contains("Money left: 1000 NOK. Launch deadline: day 96.", before);
        var after = P.Query(new StatusView(100, "08:00", 0, 0, null, false, false, [], [], "Cockpit", 1000, 96, Launched: true, SeasonLead: "Haulout booked for day 258."));
        Assert.Contains("Money left: 1000 NOK. Haulout booked for day 258.", after);
        Assert.DoesNotContain("Launch deadline", after);
    }

    [Fact]
    public void The_season_page_is_its_lines_and_the_logbook_marks_it_as_a_page_to_reread()
    {
        string[] lines =
        [
            "Season 1, day 75 to day 262.",
            "62 miles sailed; three nights away from Skjærvik; reached Hestholmen and Storholmen.",
            "She cost you 41300 NOK this season. The broker would put her at 171000 NOK.",
            "The yard knows you pay on time.",
        ];
        var page = new SeasonPageView(1, 75, 262, 62, 3, ["Hestholmen", "Storholmen"], [], [], 212, 9, 0, 41300, 0, 171000, "The yard knows you pay on time.", lines);
        var text = P.Query(page).Replace("\r\n", "\n");
        Assert.Equal(string.Join("\n", lines) + "\n", text);
        Assert.DoesNotContain("62.0", text);

        Assert.Contains("(season page)", P.Query(new LogView([new LogEntry(262, "17:24", "The page of season 1 is written: day 75 to day 262.", 0, "season 1")])));
        Assert.Contains("No season page yet", P.Query(new SeasonPageView(0, 0, 0, 0, 0, [], [], [], 0, 0, 0, 0, 0, 0, "", ["No season page yet: it is written at the autumn haulout."])));
    }

    [Fact]
    public void The_yards_opinion_is_a_sentence_in_the_calendar_and_the_season_never_a_number()
    {
        var money = new MoneyView(12400, 0, 0, 0, 0, 0, []);
        var calendar = new CalendarView(
            89, "Wednesday", false, "Wednesday, day 89: a weekday.", 0, 22, 25, 3, 0, 12400, 20000, 6000, 90, [], [], money,
            Reputation: "The yard knows you pay on time.", PaydaysSeen: 3);
        var text = P.Query(calendar);
        Assert.Contains("The yard knows you pay on time.", text);
        Assert.DoesNotContain("PaydaysSeen", text);
        Assert.DoesNotContain("3 paydays", text);
        Assert.DoesNotContain("The yard", P.Query(calendar with { Reputation = null }));
    }
}
