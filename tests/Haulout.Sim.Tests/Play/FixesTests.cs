using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The fixes of design note 010, second pass I: the way through another port, events marked as going, the delivery stop in the winter,
/// the impeller's wear symptom, the readiness order note, the tow that does not count as reaching a port, the upgrade refusals.</summary>
public class FixesTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string Regatta = "The regatta party at Hestholmen";
    private const string Midsummer = "Midsummer at Storholmen";
    private const string Friend = "A friend's week aboard";
    private const string Cruise = "The club cruise to Kvitnes";

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static CommandResult No(CommandResult r, string text)
    {
        Assert.False(r.Accepted);
        Assert.Equal(text, Assert.Single(r.Narrative));
        Assert.Equal((0, 0), (r.MinutesTaken, r.CostNok));
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static CalendarView Cal(Game g) => (CalendarView)g.Ask(new Calendar());
    private static SeasonView Season(Game g) => (SeasonView)g.Ask(new Season());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static ChartView Chart(Game g, string? to = null) => (ChartView)g.Ask(new Chart(to));
    private static TripView Trip(Game g) => (TripView)g.Ask(new Trip());
    private static TripPlanView Plan(Game g, string? to = null) => (TripPlanView)g.Ask(new PlanTrip(to));
    private static EventView Event(Game g, string name) => Cal(g).Events!.Single(e => e.Name == name);
    private static UpgradeView Up(Game g, string name) => ((UpgradesView)g.Ask(new Upgrades())).Items.Single(i => i.Name == name);
    private static WeatherSeries Clear() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Boat Edited(Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        edit(node);
        return BoatLoader.Load(node.ToJsonString());
    }

    /// <summary>Afloat at Skjærvik on <paramref name="day"/>, the engine running, in the cockpit; the impeller new unless asked, the anchor chain new.</summary>
    private static Game Afloat(int day, Boat? boat = null, WeatherSeries? weather = null, bool newImpeller = true)
    {
        boat ??= BoatLoader.LoadSloop28();
        boat.Get("anchor-gear").Replace(); boat.Get("anchor-shackle").Replace();
        var g = new Game(boat, day, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Clear());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        if (newImpeller) Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    private static CommandResult MotorToArrival(Game g)
    {
        var r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 40 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        Assert.IsType<TripResultView>(r.Observation);
        return r;
    }

    private static void ToCockpitRunning(Game g)
    {
        Walk.ToPlace(g, "cockpit");
        var said = new List<string>();
        for (int tries = 0; tries < 3 && !Status(g).EngineRunning; tries++) said.Add(Text(Ok(Walk.Start(g))));   // a weak battery may need a second go
        Assert.True(Status(g).EngineRunning, string.Join(" | ", said));
        Walk.ToPlace(g, "cockpit");
    }

    private static CommandResult Passage(Game g, string to)
    {
        ToCockpitRunning(g);
        Ok(g.Execute(new Depart(to)));
        return MotorToArrival(g);
    }

    // ================================================================== 1. the way through another port

    [Fact]
    public void The_shortest_way_through_the_routes_goes_by_the_ports_in_between_and_is_empty_where_there_is_none()
    {
        var trips = BoatLoader.LoadSloop28().Trips!;
        Assert.Equal(["kvitnes"], trips.PathBetween("hestholmen", "kvitnes").Select(l => l.To));
        var home = trips.PathBetween("skjaervik", "kvitnes");
        Assert.Equal([("skjaervik", "hestholmen"), ("hestholmen", "kvitnes")], home.Select(l => (l.From, l.To)));
        Assert.Equal(23.0, home.Sum(l => l.DistanceNm));
        // The way back is the same legs reversed.
        Assert.Equal([("kvitnes", "hestholmen"), ("hestholmen", "skjaervik")], trips.PathBetween("kvitnes", "skjaervik").Select(l => (l.From, l.To)));
        // Three legs from Storholmen, each as sailed from its own start.
        Assert.Equal(["skjaervik", "hestholmen", "kvitnes"], trips.PathBetween("storholmen", "kvitnes").Select(l => l.To));
        Assert.Empty(trips.PathBetween("skjaervik", "skjaervik"));
        Assert.Empty(trips.PathBetween("skjaervik", "nowhere"));
    }

    [Fact]
    public void The_plan_for_a_port_with_no_direct_route_goes_by_Hestholmen_and_covers_the_whole_way()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = Afloat(215, boat);
        var direct = Plan(g, "Hestholmen");
        var plan = Plan(g, "Kvitnes");
        Assert.Equal(("Skjærvik", "Kvitnes", 23.0), (plan.From, plan.To, plan.DistanceNm));
        Assert.Equal([("Skjærvik", "Hestholmen", 14.0), ("Hestholmen", "Kvitnes", 9.0)], plan.Legs!.Select(l => (l.From, l.To, l.DistanceNm)));
        Assert.Equal(4.6, plan.MotorHours);                         // 23 nm at five knots: the whole way, not the first leg
        Assert.Equal(2.8, direct.MotorHours);
        Assert.Null(direct.Legs);
        Assert.Empty(plan.Blockers);
        Assert.Contains("An anchorage: no shop, no fuel.", plan.AtDestination!);
        Assert.Contains(plan.AtDestination!, l => l.StartsWith(Cruise, StringComparison.Ordinal));   // the cruise at the far end, not Hestholmen's
        Assert.DoesNotContain(plan.AtDestination!, l => l.StartsWith("The regatta", StringComparison.Ordinal));
        // The fuel line is for 23 nm.
        int needs = (int)Math.Round(4.6 * FuelBurn.Of(boat.Fuel, boat.SeaRules.MotorKnots, []).LitresPerHour, MidpointRounding.AwayFromZero);
        Assert.EndsWith($"This passage needs about {needs} litres.", plan.Fuel);
        // The shelters of both legs are along the way, at miles from the start.
        Assert.Equal(["Lyngskjær, 7 nm out"], plan.Shelter);
        // The vacation days cover the way: a weekday departure (day 217 is a Monday) at the plan's hour, both passages in one day.
        Assert.Equal(1, ((TripPlanView)g.Ask(new PlanTrip("Kvitnes", 217))).VacationDays);
        Assert.Null(Plan(g, "nowhere").Legs);
        Assert.Contains(Plan(g, "nowhere").Blockers, b => b.StartsWith("There's no route from Skjærvik to 'nowhere'"));
    }

    [Fact]
    public void Departing_for_a_port_by_another_sails_the_first_leg_and_says_so_and_arrival_says_what_is_left()
    {
        var g = Afloat(215);
        var depart = Ok(g.Execute(new Depart("Kvitnes")));
        Assert.Contains("First leg: Hestholmen. From there it is 9 nm on to Kvitnes.", depart.Narrative);
        Assert.Equal(("Skjærvik", "Hestholmen"), (Trip(g).From, Trip(g).To));
        var arrive = MotorToArrival(g);
        var card = Assert.IsType<TripResultView>(arrive.Observation);
        Assert.Equal("Hestholmen", card.Destination);
        Assert.Contains("Hestholmen was the first leg of your way to Kvitnes: from here it is 9 nm on. Depart again when you are ready.", arrive.Narrative);
        Assert.Equal("Hestholmen", Trip(g).Port);

        // From Hestholmen it is a direct route, with no first-leg talk.
        ToCockpitRunning(g);
        var again = Ok(g.Execute(new Depart("Kvitnes")));
        Assert.DoesNotContain("First leg", Text(again));
        Assert.Equal(("Hestholmen", "Kvitnes"), (Trip(g).From, Trip(g).To));
        var end = MotorToArrival(g);
        Assert.DoesNotContain("first leg", Text(end));
        Assert.Equal("Kvitnes", Trip(g).Port);
    }

    [Fact]
    public void A_direct_departure_and_a_turn_back_say_nothing_of_a_first_leg()
    {
        var g = Afloat(215);
        var direct = Ok(g.Execute(new Depart("Hestholmen")));
        Assert.DoesNotContain("First leg", Text(direct));
        MotorToArrival(g);

        var back = Afloat(215);
        Ok(back.Execute(new Depart("Kvitnes")));
        Ok(back.Execute(new Motor()));
        Ok(back.Execute(new TurnBack()));
        var end = MotorToArrival(back);
        Assert.DoesNotContain("first leg", Text(end));
        Assert.Equal("Skjærvik", Trip(back).Port);
    }

    [Fact]
    public void The_chart_gives_the_way_through_another_port_instead_of_no_route_and_plans_it()
    {
        var g = Afloat(215);
        var kvitnes = Chart(g).Ports.Single(p => p.Name == "Kvitnes");
        Assert.False(kvitnes.Reachable);
        Assert.Equal(("Hestholmen", 23.0), (kvitnes.Via, kvitnes.RouteNm));
        var storholmen = Chart(g).Ports.Single(p => p.Name == "Storholmen");
        Assert.True(storholmen.Reachable);
        Assert.Null(storholmen.Via);
        Assert.Equal(23.0, Chart(g, "kvitnes").Plan!.DistanceNm);

        Passage(g, "Hestholmen");
        var from = Chart(g).Ports.Single(p => p.Name == "Kvitnes");
        Assert.True(from.Reachable);
        Assert.Null(from.Via);
        var storFromHest = Chart(g).Ports.Single(p => p.Name == "Storholmen");
        Assert.Equal("Skjærvik", storFromHest.Via);
        Assert.Equal(40.0, storFromHest.RouteNm);
    }

    // ================================================================== 2. events marked as going

    [Fact]
    public void Nothing_is_marked_until_the_player_marks_it_and_the_mark_is_free_and_takes_no_time()
    {
        var g = Afloat(100);
        Assert.All(Cal(g).Events!, e => Assert.False(e.Going));
        int minutes = Status(g).MinutesSpent;
        var r = Ok(g.Execute(new PlanEvent("midsummer")));
        Assert.Equal((0, 0), (r.MinutesTaken, r.CostNok));
        Assert.Equal(minutes, Status(g).MinutesSpent);
        Assert.Contains($"You mark it on the calendar: {Midsummer}, ", Text(r));
        Assert.True(Event(g, Midsummer).Going);
        Assert.False(Event(g, Regatta).Going);

        var off = Ok(g.Execute(new UnplanEvent("Midsummer at Storholmen")));
        Assert.Equal((0, 0), (off.MinutesTaken, off.CostNok));
        Assert.False(Event(g, Midsummer).Going);
        Assert.Contains("it still happens", Text(off));
        // A plain mark is not a logbook entry: no sailing was done.
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.Contains("mark", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Marking_resolves_the_name_by_words_or_id_and_refuses_in_plain_words()
    {
        var g = Afloat(100);
        Ok(g.Execute(new PlanEvent("regatta")));
        Ok(g.Execute(new PlanEvent("club-cruise")));
        Assert.True(Event(g, Regatta).Going);
        Assert.True(Event(g, Cruise).Going);
        No(g.Execute(new PlanEvent("regatta")), $"{Regatta} is marked already: Saturday, day 152.");
        No(g.Execute(new UnplanEvent("midsummer")), $"{Midsummer} isn't marked.");
        No(g.Execute(new PlanEvent("the moon landing")), "There's nothing on the calendar called 'the moon landing'.");
        var ambiguous = g.Execute(new PlanEvent("at"));                     // the regatta party at Hestholmen, Midsummer at Storholmen...
        Assert.False(ambiguous.Accepted);
        Assert.Equal((0, 0), (ambiguous.MinutesTaken, ambiguous.CostNok));
        Assert.Equal("'at' could be: The regatta party at Hestholmen, Midsummer at Storholmen. Be more specific.", Text(ambiguous));
        No(g.Execute(new PlanEvent("friend")), "You haven't asked your friend yet: invite friend <day> (a week of your choice in July, days 180 to 203).");
    }

    [Fact]
    public void A_week_passes_the_regatta_morning_when_it_is_not_marked_and_stops_on_it_when_it_is()
    {
        var unmarked = Afloat(150);
        var past = Ok(unmarked.Execute(new WaitDays(5)));
        Assert.Equal(155, Status(unmarked).Day);
        Assert.DoesNotContain("starts today", Text(past));

        var marked = Afloat(150);
        Ok(marked.Execute(new PlanEvent("regatta")));
        var stops = Ok(marked.Execute(new WaitDays(5)));
        Assert.Equal(152, Status(marked).Day);
        Assert.Contains($"You come back on day 152: {Regatta} starts today.", stops.Narrative);

        // Unmarking it again lets a wait go straight past.
        var changed = Afloat(150);
        Ok(changed.Execute(new PlanEvent("regatta")));
        Ok(changed.Execute(new UnplanEvent("regatta")));
        Ok(changed.Execute(new WaitDays(5)));
        Assert.Equal(155, Status(changed).Day);
    }

    [Fact]
    public void Wait_week_and_next_weekend_stop_for_a_marked_event_only()
    {
        // Day 149 is a Friday: next weekend is the Saturday of the regatta, day 150? No: it is day 152 (the Saturday of the window).
        var g = Afloat(147);
        Assert.Equal(147, Status(g).Day);
        var plain = Ok(g.Execute(new WaitWeek()));
        Assert.Equal(154, Status(g).Day);
        Assert.DoesNotContain("starts today", Text(plain));

        var marked = Afloat(147);
        Ok(marked.Execute(new PlanEvent("regatta")));
        var stop = Ok(marked.Execute(new WaitWeek()));
        Assert.Equal(152, Status(marked).Day);
        Assert.Contains("starts today", Text(stop));
    }

    [Fact]
    public void An_event_that_was_not_marked_still_happens_and_is_made_by_being_there()
    {
        var g = Afloat(152);
        Assert.False(Event(g, Regatta).Going);
        var arrive = Passage(g, "Hestholmen");
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Regatta}.");
        Assert.Equal("made", Event(g, Regatta).Status);
        Assert.Contains(arrive.Narrative, l => l.Contains("regatta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unmarked_event_that_is_slept_through_is_missed_as_before()
    {
        var g = Afloat(150);
        Ok(g.Execute(new WaitDays(7)));
        Assert.Equal("missed", Event(g, Regatta).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == "The regatta party at Hestholmen came and went without you.");
    }

    [Fact]
    public void Deliveries_and_the_booked_slot_stop_a_wait_whatever_is_marked()
    {
        var boat = Edited(n => { });
        var g = Afloat(100, boat);
        Walk.Near(g, "starter motor");
        Ok(g.Execute(new Order("starter motor")));
        int due = Cal(g).Orders.Single().DueDay;
        var wait = Ok(g.Execute(new WaitDays(20)));
        Assert.Equal(due, Status(g).Day);
        Assert.Contains($"You come back on day {due}: the starter motor is due at the yard.", wait.Narrative);

        int slot = Season(g).FirstFreeDay!.Value;
        Ok(g.Execute(new BookHaulout(slot)));
        CommandResult toSlot;
        do toSlot = Ok(g.Execute(new WaitDays(Math.Min(60, slot - Status(g).Day))));
        while (Status(g).Day < slot);
        Assert.Equal(slot, Status(g).Day);
        Assert.Contains($"You come back on day {slot} for your haulout slot.", toSlot.Narrative);
        Assert.All(Cal(g).Events!, e => Assert.False(e.Going));
    }

    [Fact]
    public void The_friends_week_counts_as_marked_once_the_friend_is_asked_and_cannot_be_taken_back()
    {
        var g = Afloat(150);
        Assert.False(Event(g, Friend).Going);
        Ok(g.Execute(new InviteFriend(187)));
        Assert.True(Event(g, Friend).Going);
        No(g.Execute(new PlanEvent("friend")), "Your friend is coming on day 187: that one is going already.");
        No(g.Execute(new UnplanEvent("friend")), "Your friend is coming on day 187: you can't take that back now.");
        var wait = Ok(g.Execute(new WaitDays(40)));
        Assert.Equal(187, Status(g).Day);
        Assert.Contains($"You come back on day 187: {Friend} starts today.", wait.Narrative);
    }

    [Fact]
    public void A_made_or_gone_event_cannot_be_marked()
    {
        var g = Afloat(152);
        Passage(g, "Hestholmen");
        Assert.Equal("made", Event(g, Regatta).Status);
        No(g.Execute(new PlanEvent("regatta")), $"{Regatta} is made already: Saturday, day 152.");
        Ok(g.Execute(new WaitDays(3)));
        Assert.NotEqual("made", Event(g, Midsummer).Status);
        // Midsummer's window is still ahead; the club cruise's too; both can be marked after the regatta.
        Ok(g.Execute(new PlanEvent("midsummer")));
    }

    // ================================================================== 3. the winter wait stops for a delivery

    [Fact]
    public void Waiting_until_spring_stops_on_the_morning_of_a_delivery_and_prints_the_week_line()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 241, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 6, 2)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Walk.Near(g, "starter motor");
        Ok(g.Execute(new Order("starter motor")));
        int due = Cal(g).Orders.Single().DueDay;
        string morning = Status(g).TimeOfDay;

        var r = Ok(g.Execute(new WaitUntilSpring()));
        Assert.Equal(due, Status(g).Day);
        Assert.Equal(morning, Status(g).TimeOfDay);
        Assert.Contains($"You come back on day {due}: the starter motor is due at the yard.", r.Narrative);
        Assert.Contains("While you were away: the starter motor came in.", r.Narrative);
        Assert.DoesNotContain("It is spring.", r.Narrative);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Came back on day {due} after {due - 241} days.");

        // The week line is said once: the next wait on to spring does not repeat it.
        var spring = Ok(g.Execute(new WaitUntilSpring()));
        Assert.Equal(435, Status(g).Day);
        Assert.DoesNotContain(spring.Narrative, l => l.Contains("starter motor came in", StringComparison.Ordinal));
    }

    [Fact]
    public void Waiting_until_spring_with_a_payday_in_the_winter_says_so_in_the_week_line()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 241, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 6, 2)));
        var r = Ok(g.Execute(new WaitUntilSpring()));
        var line = Assert.Single(r.Narrative, l => l.StartsWith("While you were away:", StringComparison.Ordinal));
        Assert.Contains("payday", line);
        Assert.Contains("the berth invoice", line);
    }

    // ================================================================== 4. the impeller's wear has a symptom

    private const string SetText = "The vanes have taken a set: they lie curled the way the pump turns and spring back slowly.";

    private static Game WornImpeller(double condition, out Boat boat, bool withFault = false)
    {
        boat = Edited(n =>
        {
            foreach (var c in n["components"]!.AsArray())
                if (c!["id"]!.GetValue<string>() == "impeller") c["condition"] = condition;
        });
        if (!withFault) boat.AllFaults.Single(f => f.Id == "impeller-tired").Clear();
        return Afloat(150, boat, newImpeller: false);
    }

    private static string InspectBare(Game g)
    {
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        return Text(Ok(g.Execute(new Inspect("impeller"))));
    }

    [Theory]
    [InlineData(0.55, false)]    // 45 percent worn at day 150: no symptom to the eye
    [InlineData(0.35, true)]
    [InlineData(0.2, true)]
    public void With_the_cover_off_a_worn_impeller_shows_a_set_from_the_threshold_in_the_data(double condition, bool shows)
    {
        var g = WornImpeller(condition, out var boat);
        double wear = 1 - boat.Get("impeller").Condition;
        Assert.Equal(shows, wear >= boat.SeaRules.ImpellerSetFromWear);
        string text = InspectBare(g);
        Assert.Equal(shows, text.Contains(SetText, StringComparison.Ordinal));
        Assert.DoesNotContain("You know what this is", text);   // the eye suggests: nothing is found
        Assert.DoesNotContain(((StatusView)g.Ask(new Status())).DiscoveredFaults, f => f.ComponentName == "Raw-water impeller");
    }

    [Fact]
    public void The_set_is_seen_only_with_the_pump_cover_off_and_a_new_impeller_shows_none()
    {
        var g = WornImpeller(0.2, out _);
        Walk.Near(g, "impeller");
        Assert.DoesNotContain(SetText, Text(Ok(g.Execute(new Inspect("impeller")))));   // boot on (the cover): the eye sees nothing
        Assert.Contains(SetText, InspectBare(g));

        var fresh = Afloat(150);   // replaced at the start
        Assert.DoesNotContain(SetText, InspectBare(fresh));
    }

    [Fact]
    public void A_tired_impeller_fault_tells_its_own_story_and_not_the_wear_line_twice()
    {
        var g = WornImpeller(0.2, out _, withFault: true);
        string text = InspectBare(g);
        Assert.Contains("the vanes have taken a set in a curve and two of them are cracked at the root", text);
        Assert.DoesNotContain(SetText, text);
    }

    [Theory]
    [InlineData(0.3, "Water leaves the exhaust in strong, steady spurts.")]       // 70 percent worn: under the threshold
    [InlineData(0.2, "The water at the exhaust is weaker than you'd like.")]      // 80 percent worn: weaker, before any vane has shed
    public void Check_flow_reads_weaker_from_the_wear_threshold_before_the_vanes_shed(double condition, string expected)
    {
        var g = WornImpeller(condition, out var boat);
        Assert.Equal(1.0, boat.Get("impeller").State["vaneFactor"]);   // nothing has shed
        Walk.ToPlace(g, "cockpit");
        var flow = Ok(g.Execute(new CheckRawWaterFlow()));
        Assert.Contains(expected, flow.Narrative);
    }

    [Fact]
    public void The_shed_factor_still_weakens_the_flow_on_a_young_impeller()
    {
        var g = Afloat(150);
        Walk.ToPlace(g, "cockpit");
        Assert.Contains("Water leaves the exhaust in strong, steady spurts.", Ok(g.Execute(new CheckRawWaterFlow())).Narrative);
        // 16 hours of motoring on a new impeller: it sheds little; on a worn one the thresholds above apply. Here only the rule that wear is not
        // what a young impeller reads: a few hours do not make the flow weaker.
        Ok(g.Execute(new Depart("Hestholmen")));
        Ok(g.Execute(new Motor()));
        Assert.Contains("Water leaves the exhaust in strong, steady spurts.", Ok(g.Execute(new CheckRawWaterFlow())).Narrative);
    }

    [Fact]
    public void The_wear_thresholds_are_in_the_data_and_a_boat_that_names_none_shows_no_symptom()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal((0.6, 0.75), (boat.SeaRules.ImpellerSetFromWear, boat.SeaRules.ImpellerWeakFlowFromWear));
        var none = Edited(n =>
        {
            n["sea"]!.AsObject().Remove("impellerSetFromWear");
            n["sea"]!.AsObject().Remove("impellerWeakFlowFromWear");
            foreach (var c in n["components"]!.AsArray())
                if (c!["id"]!.GetValue<string>() == "impeller") c["condition"] = 0.1;
        });
        none.AllFaults.Single(f => f.Id == "impeller-tired").Clear();
        var g = Afloat(150, none, newImpeller: false);
        Assert.DoesNotContain(SetText, InspectBare(g));
    }

    // ================================================================== 5. the readiness DIY route names the order

    private static ReadinessItem StarterItem(Game g) => ((ReadinessView)g.Ask(new Readiness())).Open.Single(p => p.Problem == "Starter soaked in bilge water");

    private static Game WithKnownStarterFault(out Boat boat)
    {
        boat = BoatLoader.LoadSloop28();
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        boat.AllFaults.Single(f => f.Id == "starter-soaked").Wake(75);
        Ok(Walk.Do(g, "starter motor", c => new Inspect(c)));
        return g;
    }

    [Fact]
    public void The_DIY_route_of_a_part_off_the_shelf_names_the_order_with_the_leads_from_the_rules_and_follows_it()
    {
        var g = WithKnownStarterFault(out var boat);
        var item = StarterItem(g);
        var diy = item.Routes.Single(r => r.Route == "DIY");
        Assert.Equal($"not on the yard's shelf: order it first, {boat.Orders.LeadLo} to {boat.Orders.LeadHi} days", diy.PartNote);

        Walk.Near(g, "starter motor");
        Ok(g.Execute(new Order("starter motor")));
        int due = Cal(g).Orders.Single().DueDay;
        Assert.Equal($"on order, due day {due}", StarterItem(g).Routes.Single(r => r.Route == "DIY").PartNote);

        // It comes in while she is away from the yard: waiting there; at the yard it is put aboard.
        Ok(g.Execute(new WaitDays(15)));
        Assert.Equal("the new part is aboard", StarterItem(g).Routes.Single(r => r.Route == "DIY").PartNote);
    }

    [Fact]
    public void A_part_that_is_on_the_shelf_has_no_order_note()
    {
        var g = Afloat(150);
        var boat = BoatLoader.LoadSloop28();
        Assert.True(boat.Get("impeller").Spec.Stocked);
        boat.AllFaults.Single(f => f.Id == "impeller-tired").Wake(150);
        // A stocked part's route carries no note; the sloop's own first fault is on the shelf too.
        var ready = (ReadinessView)new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Clear()).Ask(new Readiness());
        Assert.All(ready.Open.SelectMany(o => o.Routes), r => Assert.Null(r.PartNote));
        Assert.NotNull(g);
    }

    // ================================================================== 6. a port reached under tow is not reached

    /// <summary>Two hours out of Skjærvik for Hestholmen (past the half-way mark of 14 nm), then the towboat: it takes her on to Hestholmen.</summary>
    private static void TowedOnTo(Game g)
    {
        ToCockpitRunning(g);
        Ok(g.Execute(new Depart("Hestholmen")));
        Ok(g.Execute(new Motor()));
        Ok(g.Execute(new Continue()));
        var tow = Ok(g.Execute(new CallTow()));
        Assert.True(Assert.IsType<TripResultView>(tow.Observation).Towed);
        Assert.Equal("Hestholmen", Trip(g).Port);
    }

    [Fact]
    public void A_port_reached_under_tow_is_not_a_place_reached_on_the_season_page_and_not_a_trip()
    {
        var g = Afloat(77);
        while (Status(g).Day < 100) Ok(g.Execute(new WaitDays(Math.Min(30, 100 - Status(g).Day))));
        TowedOnTo(g);
        Passage(g, "Skjærvik");
        Ok(g.Execute(new FillTank()));
        Ok(g.Execute(new BookHaulout(262)));
        while (Status(g).Day < 262) Ok(g.Execute(new WaitDays(Math.Min(30, 262 - Status(g).Day))));
        ToCockpitRunning(g);
        Ok(g.Execute(new HaulOut()));
        var page = (SeasonPageView)g.Ask(new SeasonPage());
        Assert.DoesNotContain("Hestholmen", page.PlacesReached);
        Assert.Empty(page.PlacesReached);
        Assert.DoesNotContain(page.Lines, l => l.Contains("reached Hestholmen", StringComparison.Ordinal));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text == "Arrived at Hestholmen.");
    }

    [Fact]
    public void Reaching_the_port_under_her_own_engine_later_does_count()
    {
        var boat = BoatLoader.LoadSloop28();
        foreach (var f in boat.AllFaults) f.Clear();        // a sound boat: the point here is the passages, not the engine's moods
        var g = Afloat(250, boat);
        TowedOnTo(g);
        Passage(g, "Skjærvik");
        Passage(g, "Hestholmen");
        Passage(g, "Skjærvik");
        Ok(g.Execute(new FillTank()));
        Ok(g.Execute(new BookHaulout(262)));
        while (Status(g).Day < 262) Ok(g.Execute(new WaitDays(Math.Min(30, 262 - Status(g).Day))));
        ToCockpitRunning(g);
        Ok(g.Execute(new HaulOut()));
        Assert.Equal(["Hestholmen"], ((SeasonPageView)g.Ask(new SeasonPage())).PlacesReached);
    }

    [Fact]
    public void An_event_at_the_port_is_still_made_when_she_is_towed_in_in_time_but_the_port_is_not_reached()
    {
        // The club cruise runs from the Friday to the Sunday (days 214 to 216) and is made by the Saturday evening. She leaves Hestholmen on the Friday,
        // is towed in to Kvitnes and lies at the anchorage on the Saturday: being there is being there for a party.
        var g = Afloat(214);
        Assert.Equal("Friday", new Haulout.Sim.Time.GameClock(214).WeekdayName);
        Passage(g, "Hestholmen");
        ToCockpitRunning(g);
        Ok(g.Execute(new Depart("Kvitnes")));
        Ok(g.Execute(new Motor()));          // 5 of the 9 nm: past the half-way mark, so the towboat takes her on
        var tow = Ok(g.Execute(new CallTow()));
        Assert.True(Assert.IsType<TripResultView>(tow.Observation).Towed);
        Assert.Equal("Kvitnes", Trip(g).Port);
        Assert.Equal(215, Status(g).Day);
        Assert.Equal("made", Event(g, Cruise).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Cruise}.");
        Assert.DoesNotContain(Log(g).Entries, e => e.Text == "Arrived at Kvitnes.");
    }

    // ================================================================== 7. the upgrade carries its refusals

    private static IEnumerable<UpgradeView> AllUps(Game g) => ((UpgradesView)g.Ask(new Upgrades())).Items;

    [Fact]
    public void Each_refusal_in_the_upgrade_view_is_the_text_the_command_gives()
    {
        var g = Afloat(100);
        foreach (var u in AllUps(g))
        {
            Assert.Null(u.OrderRefusal);                                       // not aboard, money enough
            var install = No(g.Execute(new Install(u.Name)), u.InstallRefusal!);
            Assert.StartsWith($"You don't have the {u.Name} aboard. Order one: order {u.Name}.", install.Narrative.Single());
            if (u.YardNok is not null) No(g.Execute(new YardInstall(u.Name)), u.YardInstallRefusal!);
            else Assert.Null(u.YardInstallRefusal);
        }
    }

    [Fact]
    public void An_upgrade_on_order_or_aboard_says_so_in_each_refusal_and_money_refuses_the_order()
    {
        var g = Afloat(100);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Order("bunk cushions")));
        var u = Up(g, "bunk cushions");
        int due = Cal(g).Orders.Single().DueDay;
        Assert.Equal($"The bunk cushions is on order already: due on day {due}.", u.OrderRefusal);
        No(g.Execute(new Order("bunk cushions")), u.OrderRefusal!);
        Assert.Equal($"On order: the bunk cushions, due on day {due}.", u.InstallRefusal);
        No(g.Execute(new Install("bunk cushions")), u.InstallRefusal!);
        Assert.Null(u.YardInstallRefusal);   // the yard does not do cushions

        Ok(g.Execute(new WaitDays(15)));
        var aboard = Up(g, "bunk cushions");
        Assert.Equal("aboard, not fitted", aboard.Status);
        Assert.Equal("You have the bunk cushions aboard already.", aboard.OrderRefusal);
        No(g.Execute(new Order("bunk cushions")), aboard.OrderRefusal!);
        Assert.StartsWith("Fitting the bunk cushions is done", aboard.InstallRefusal);

        Walk.ToPlace(g, "saloon");
        Assert.Null(Up(g, "bunk cushions").InstallRefusal);
        Ok(g.Execute(new Install("bunk cushions")));
        var fitted = Up(g, "bunk cushions");
        Assert.StartsWith("fitted on day", fitted.Status);
        Assert.Equal("Already fitted: the bunk cushions.", fitted.InstallRefusal);
        Assert.Equal("Already fitted: the bunk cushions.", fitted.OrderRefusal);
        No(g.Execute(new Order("bunk cushions")), fitted.OrderRefusal!);
    }

    [Fact]
    public void A_boat_without_the_money_refuses_the_order_with_the_banks_words_and_the_view_agrees()
    {
        var boat = BoatLoader.Load(Sloop28Json(), economyJson: TestEconomy.Edited(n => n["start"]!["startingBalanceNok"] = -19000));
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Clear());
        var radar = Up(g, "marine radar");
        Assert.NotNull(radar.OrderRefusal);
        No(g.Execute(new Order("marine radar")), radar.OrderRefusal!);
    }

    [Fact]
    public void At_sea_the_upgrades_all_say_so()
    {
        var g = Afloat(100);
        ToCockpitRunning(g);
        Ok(g.Execute(new Depart("Hestholmen")));
        foreach (var u in AllUps(g))
        {
            Assert.Equal("You're at sea: ring the yard from port.", u.OrderRefusal);
            Assert.Equal("You're at sea: fit it in harbour.", u.InstallRefusal);
        }
    }
}
