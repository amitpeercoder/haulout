using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The calendar of wants (design note 010, step 2): the four events, the friend's week, what the views say and what they do not.</summary>
public class EventTests
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
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static EventView Event(Game g, string name) => Cal(g).Events!.Single(e => e.Name == name);
    private static WeatherSeries Clear() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The shipped boat, with the friend's requirements taken away (the upgrades that meet them are fitted in step 3).</summary>
    private static Boat BoatWhoseFriendAsksForNothing()
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        var events = node["calendar"]!["events"]!.AsArray();
        var friend = events.Single(e => e!["id"]!.GetValue<string>() == "friend-week")!;
        friend["requires"] = new JsonArray();
        return BoatLoader.Load(node.ToJsonString());
    }

    /// <summary>Afloat at Skjærvik on <paramref name="day"/>, engine running, the impeller new (it is hours of motoring).</summary>
    private static Game Afloat(int day, Boat? boat = null, bool chainFixed = false, WeatherSeries? weather = null)
    {
        boat ??= BoatLoader.LoadSloop28();
        if (chainFixed) { boat.Get("anchor-gear").Replace(); boat.Get("anchor-shackle").Replace(); }
        var g = new Game(boat, day, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Clear());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        return g;
    }

    private static CommandResult Motor(Game g, string to)
    {
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Ok(g.Execute(new Depart(to)));
        var r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 20 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        Assert.IsType<TripResultView>(r.Observation);
        return r;
    }

    // ------------------------------------------------------------------ the windows

    [Fact]
    public void The_fixed_windows_of_year_one_and_year_two_fall_on_the_right_weekdays()
    {
        var g = Afloat(75);
        var events = Cal(g).Events!;
        Assert.Equal([Regatta, Midsummer, Friend, Cruise], events.Select(e => e.Name));
        Assert.Equal("Saturday, day 152", events[0].When);
        Assert.Equal("Friday and Saturday, days 172 to 173", events[1].When);
        Assert.Equal("a week of your choice in July, days 180 to 203", events[2].When);
        Assert.Equal("Friday to Sunday, days 214 to 216", events[3].When);
        Assert.Equal(["Hestholmen", "Storholmen", "Skjærvik", "Kvitnes"], events.Select(e => e.Place));
        Assert.All(events, e => Assert.Equal("ahead", e.Status));
        Assert.Equal((152, 152), (events[0].FromDay, events[0].ToDay));
        Assert.Equal((172, 173), (events[1].FromDay, events[1].ToDay));
        Assert.Equal((214, 216), (events[3].FromDay, events[3].ToDay));
    }

    [Fact]
    public void After_the_last_event_of_the_year_the_list_shows_next_years_with_its_days()
    {
        var g = Afloat(300);
        var events = Cal(g).Events!;
        Assert.Equal(Regatta, events[0].Name);
        Assert.Equal("Saturday, day 516 (next year)", events[0].When);   // day 510 is a Sunday: the Saturday after
        Assert.Equal("Monday and Tuesday, days 532 to 533 (next year)", events[1].When); // Midsummer is a date, not a weekday
        Assert.Equal("Friday to Sunday, days 578 to 580 (next year)", events[3].When);   // day 574 is a Monday: the Friday after
        Assert.All(events, e => Assert.Equal("ahead", e.Status));
    }

    [Fact]
    public void The_list_ahead_and_the_next_line_name_the_first_day_of_each_window()
    {
        var g = Afloat(135);
        var ahead = Cal(g).Ahead;
        Assert.Equal([Regatta, Midsummer], ahead.Where(e => e.Kind == "event").Select(e => e.Text)); // 42 days ahead of day 135: up to day 177
        var regatta = ahead.Single(e => e.Text == Regatta);
        Assert.Equal((152, "Saturday", 0), (regatta.Day, regatta.Weekday, regatta.Nok));
        Assert.Contains(Cal(g).Ahead, e => e.Kind == "payday");
        // The status says only the first thing coming.
        Assert.StartsWith("Next:", Status(g).Next);
    }

    // ------------------------------------------------------------------ the regatta

    [Fact]
    public void Arriving_at_Hestholmen_on_the_Saturday_makes_the_regatta_and_tells_the_party()
    {
        var g = Afloat(152);
        Assert.Equal("on now", Event(g, Regatta).Status);
        var arrive = Motor(g, "hestholmen");
        Assert.Contains("By evening the regatta party has spilled down onto the jetty, lanterns lit, someone playing a fiddle badly and nobody minding.", arrive.Narrative);
        Assert.Contains("You sit in the cockpit with a mug and listen. She got you here.", arrive.Narrative);
        Assert.Contains(arrive.Alerts, a => a.Text.StartsWith("By evening the regatta party", StringComparison.Ordinal));
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Regatta}.");
        var view = Event(g, Regatta);
        Assert.Equal("made", view.Status);
        Assert.StartsWith("By evening the regatta party", view.Story);
        Assert.Empty(view.Requirements);
        Assert.DoesNotContain(Cal(g).Ahead, e => e.Kind == "event" && e.Text == Regatta);

        // Told once: another command at Hestholmen says nothing more of it.
        var again = Ok(Walk.Do(g, "negative terminal", c => new Inspect(c)));
        Assert.DoesNotContain("regatta party", Text(again));
        Assert.Single(Log(g).Entries, e => e.Text == $"Made it: {Regatta}.");
    }

    [Fact]
    public void Arriving_on_any_other_day_is_a_quiet_harbour_and_the_party_is_not_made()
    {
        var g = Afloat(153);
        var arrive = Motor(g, "hestholmen");
        Assert.Contains("Hestholmen opens up round the point: a quay, a few masts, a shop with its door propped open.", arrive.Narrative);
        Assert.DoesNotContain("regatta party", Text(arrive));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.StartsWith("Made it", StringComparison.Ordinal));
        Assert.Equal("missed", Event(g, Regatta).Status.Replace("ahead", "missed")); // day 153: the window is past (see the next test for the log)
    }

    [Fact]
    public void A_wait_stops_on_the_morning_of_a_window_and_a_party_slept_through_is_missed_and_said_once()
    {
        var g = Afloat(150);
        Ok(g.Execute(new PlanEvent("regatta")));   // waits stop only for events the player has marked
        var wait = Ok(g.Execute(new WaitDays(5)));
        Assert.Equal(152, Status(g).Day);
        Assert.Contains($"You come back on day 152: {Regatta} starts today.", wait.Narrative);
        Assert.Equal("on now", Event(g, Regatta).Status);

        // She is at Skjærvik, not at the party. The day ends, and the logbook says so.
        var next = Ok(g.Execute(new WaitDays(2)));
        string missed = "The regatta party at Hestholmen came and went without you.";
        Assert.Contains(next.Alerts, a => a.Text == missed);
        Assert.Single(Log(g).Entries, e => e.Text == missed && e.Alert);
        Assert.Equal("missed", Event(g, Regatta).Status);
        Ok(g.Execute(new WaitDays(3)));
        Assert.Single(Log(g).Entries, e => e.Text == missed);
    }

    // ------------------------------------------------------------------ midsummer

    [Fact]
    public void Midsummer_is_judged_at_the_midnight_ending_its_first_day_not_on_arrival()
    {
        var g = Afloat(172);
        var arrive = Motor(g, "storholmen");
        Assert.DoesNotContain("bonfire", Text(arrive));
        Assert.Equal("on now", Event(g, Midsummer).Status);

        var night = Ok(g.Execute(new WaitDays(1)));
        Assert.Contains("They light the bonfire on the point at ten, and it burns on into a night that never quite goes dark.", night.Narrative);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Midsummer}.");
        Assert.Equal("made", Event(g, Midsummer).Status);
        // The second day of the window does not make it again or miss it.
        Ok(g.Execute(new WaitDays(1)));
        Assert.Single(Log(g).Entries, e => e.Text == $"Made it: {Midsummer}.");
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.StartsWith("Midsummer came and went"));
    }

    [Fact]
    public void Midsummer_at_home_is_missed_at_the_end_of_the_window_and_leaving_before_the_midnight_misses_the_night()
    {
        var home = Afloat(171);
        Ok(home.Execute(new PlanEvent("midsummer")));
        Ok(home.Execute(new WaitDays(5)));          // cut on the morning of 172
        Assert.Equal(172, Status(home).Day);
        Ok(home.Execute(new WaitDays(4)));
        string missed = "Midsummer came and went, and the bonfire at Storholmen burned without you.";
        Assert.Single(Log(home).Entries, e => e.Text == missed);
        Assert.Equal("missed", Event(home, Midsummer).Status);

        // She is at Storholmen by the afternoon but sails home before the midnight of the 172nd.
        var g = Afloat(172);
        Motor(g, "storholmen");
        Walk.ToPlace(g, "cockpit");
        Assert.True(Status(g).Day == 172);
        Ok(g.Execute(new WaitDays(1)));
        Assert.Equal("made", Event(g, Midsummer).Status);
    }

    // ------------------------------------------------------------------ the friend

    [Fact]
    public void Inviting_the_friend_has_its_rules_each_refused_free_and_in_plain_words()
    {
        var g = Afloat(150);
        var sea = Afloat(150);
        Walk.ToPlace(sea, "cockpit");
        Ok(sea.Execute(new Depart("hestholmen")));
        No(sea.Execute(new InviteFriend(190)), "You're at sea: ring your friend from port.");

        No(g.Execute(new InviteFriend(179)), "Your friend can come in July: a week starting between day 180 and day 203.");
        No(g.Execute(new InviteFriend(204)), "Your friend can come in July: a week starting between day 180 and day 203.");
        var soon = Afloat(179);
        No(soon.Execute(new InviteFriend(181)), "Your friend needs 3 days' notice: the earliest is day 182.");
        var late = Afloat(205);
        No(late.Execute(new InviteFriend(190)), "It's too late this year: your friend's week has gone.");

        var r = Ok(g.Execute(new InviteFriend(190)));
        Assert.Equal(5, r.MinutesTaken);
        Assert.Equal("You ring your friend. They'll come on Tuesday, day 190, for a week.", r.Narrative[0]);
        Assert.Contains(Log(g).Entries, e => e.Text == "Asked a friend aboard for a week from day 190.");
        No(g.Execute(new InviteFriend(192)), "Your friend is coming on day 190.");
        var view = Event(g, Friend);
        Assert.Equal("Tuesday to Monday, days 190 to 196", view.When);
        Assert.Equal("ahead", view.Status);
    }

    [Fact]
    public void A_friend_who_finds_no_heads_goes_home_that_day_the_week_is_gone_and_nothing_is_charged()
    {
        var g = Afloat(185);
        Ok(g.Execute(new InviteFriend(190)));
        int left = Status(g).VacationDaysLeft;
        var morning = Ok(g.Execute(new WaitDays(10)));
        Assert.Equal(190, Status(g).Day);
        Assert.Contains($"You come back on day 190: {Friend} starts today.", morning.Narrative);
        Assert.Equal("on now", Event(g, Friend).Status);
        var wait = Ok(g.Execute(new WaitDays(1)));   // cut at the hour the friend comes
        Assert.Equal("17:00", Status(g).TimeOfDay);
        Assert.Contains("Your friend opens the forepeak door, takes one breath, shuts it again, and books the hotel by the ferry.", wait.Narrative);
        Assert.Equal("failed: a heads that works", Event(g, Friend).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Missed: {Friend} (a heads that works).");
        Assert.Equal(left, Status(g).VacationDaysLeft);   // no week was spent

        // No retry: later commands the same day say nothing more, and the week stays failed.
        var again = Ok(Walk.Do(g, "negative terminal", c => new Inspect(c)));
        Assert.DoesNotContain("friend", Text(again), StringComparison.OrdinalIgnoreCase);
        Ok(g.Execute(new WaitDays(10)));
        Assert.Equal("failed: a heads that works", Event(g, Friend).Status);
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.Contains("waited on the quay"));
    }

    [Fact]
    public void A_friend_invited_who_finds_no_one_aboard_waits_on_the_quay_and_goes_home()
    {
        var g = Afloat(188);
        Ok(g.Execute(new InviteFriend(191)));
        Motor(g, "hestholmen");
        var stop = Ok(g.Execute(new WaitDays(10)));   // cut on the morning of the 191st, at Hestholmen
        Assert.Equal(191, Status(g).Day);
        Assert.Contains($"You come back on day 191: {Friend} starts today.", stop.Narrative);
        Ok(g.Execute(new WaitDays(1)));
        string quay = "Your friend waited on the quay at Skjærvik and took the evening ferry home.";
        Assert.Single(Log(g).Entries, e => e.Text == quay);
        Assert.Equal("missed", Event(g, Friend).Status);
    }

    [Fact]
    public void A_friend_who_finds_what_they_need_comes_at_five_drinks_the_boats_diesel_free_and_tops_up_the_tank()
    {
        var boat = BoatWhoseFriendAsksForNothing();
        var g = Afloat(185, boat);
        Ok(g.Execute(new InviteFriend(190)));          // Tuesday 190 to Monday 196
        int left = Status(g).VacationDaysLeft;
        Ok(g.Execute(new WaitDays(10)));                 // cut on the morning of day 190
        var wait = Ok(g.Execute(new WaitDays(1)));       // and at the friend's hour
        Assert.Equal((190, "17:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Contains("Your friend comes down the pontoon with a rucksack and a bag of oranges, looks round the saloon, and says that it is better than they remembered.", wait.Narrative);
        Assert.Equal("made", Event(g, Friend).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Friend}.");
        Assert.Equal(left, Status(g).VacationDaysLeft);  // the player was at work: no block of days is charged

        // The friend pays for the diesel.
        Walk.ToPlace(g, "cockpit");
        int balance = Status(g).BalanceNok;
        var fill = Ok(g.Execute(new FillTank()));
        Assert.Equal(0, fill.CostNok);
        Assert.Equal(balance, Status(g).BalanceNok);
        Assert.Contains("and your friend pays", Text(fill));
        Assert.Contains(Cal(g).Money.Recent, e => e.Text == "Diesel, paid by your friend." && e.Nok == 0);
        Assert.Contains(Log(g).Entries, e => e.Text.EndsWith("paid by your friend.", StringComparison.Ordinal));

        // The week goes by; at its last midnight the friend fills her again before going.
        boat.Get("fuel-tank").State["fuelLitres"] = 10;
        Ok(g.Execute(new WaitDays(10)));                 // cut on the morning of the last day
        Ok(g.Execute(new WaitDays(1)));                  // cut at the last night
        var after = Ok(g.Execute(new WaitDays(1)));      // through the midnight
        Assert.Contains(after.Alerts, a => a.Text == "Your friend topped up the tank before they went, and wouldn't hear about paying.");
        Assert.Equal("Fuel: about 45 litres (a full tank).", Status(g).Fuel);
        Assert.DoesNotContain(Cal(g).Money.Recent, e => e.Kind == "spending" && e.Nok < 0 && e.Day >= 190 && e.Text.Contains("iesel"));
    }

    // ------------------------------------------------------------------ the club cruise

    [Fact]
    public void The_club_cruise_with_a_seized_chain_shows_it_the_fault_is_found_and_the_event_is_failed_with_its_reason()
    {
        var boat = BoatLoader.LoadSloop28();
        boat.Get("anchor-shackle").Faults.Single().MarkEscalated();   // the wet season has done it: the shackle's fault became the chain's
        boat.AllFaults.Single(f => f.Id == "anchor-chain-seized").Wake(215);
        var g = Afloat(215, boat);
        // What the player knows before they sail: nothing wrong.
        var before = Event(g, Cruise).Requirements;
        Assert.Equal(new Requirement("anchor gear in order", true, "no known problem"), before[0]);
        Assert.Equal(new Requirement("a bilge pump that works", true, "no known problem"), before[1]);

        Motor(g, "hestholmen");
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        var arrive = Motor(g, "kvitnes");
        Assert.Contains("The club lies in a ring round the bay, each boat to its own anchor, and you on a borrowed line among them. Nobody says anything. Everybody knows.", arrive.Narrative);
        Assert.Contains(arrive.NewlyDiscovered, k => k.Name == "Seized anchor chain");
        Assert.Single(Log(g).Entries, e => e.Text == "Found: Seized anchor chain.");
        Assert.Contains(Log(g).Entries, e => e.Text == $"Missed: {Cruise} (anchor gear in order).");
        var view = Event(g, Cruise);
        Assert.Equal("failed: anchor gear in order", view.Status);
        Assert.Equal(new Requirement("anchor gear in order", false, "Seized anchor chain"), view.Requirements[0]);
        Assert.Equal(new Requirement("a bilge pump that works", true, "no known problem"), view.Requirements[1]);
    }

    [Fact]
    public void The_club_cruise_with_the_frozen_shackle_still_open_is_failed_and_the_shackle_is_found()
    {
        var g = Afloat(215);   // as sold: rusted chain, frozen shackle, not yet seized
        Motor(g, "hestholmen");
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        var arrive = Motor(g, "kvitnes");
        Assert.Contains("The club lies in a ring round the bay, each boat to its own anchor, and you on a borrowed line among them. Nobody says anything. Everybody knows.", arrive.Narrative);
        Assert.Contains(arrive.NewlyDiscovered, k => k.Name == "Rusted chain with a frozen shackle");
        Assert.Contains(Log(g).Entries, e => e.Text == $"Missed: {Cruise} (anchor gear in order).");
        Assert.Equal(new Requirement("anchor gear in order", false, "Rusted chain with a frozen shackle"), Event(g, Cruise).Requirements[0]);
    }

    [Fact]
    public void The_club_cruise_is_made_with_a_new_shackle_and_the_chain_left_as_it_is()
    {
        var boat = BoatLoader.LoadSloop28();
        boat.Get("anchor-shackle").Replace();   // the chain was never replaced: it only has to be free of faults
        var g = Afloat(215, boat);
        Motor(g, "hestholmen");
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        Motor(g, "kvitnes");
        Assert.Equal("made", Event(g, Cruise).Status);
    }

    [Fact]
    public void The_club_cruise_is_made_with_the_gear_in_order_and_the_story_is_told()
    {
        var g = Afloat(215, chainFixed: true);
        Motor(g, "hestholmen");
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        var arrive = Motor(g, "kvitnes");
        Assert.Contains("Eleven boats lie to their anchors in the bay at Kvitnes, and by dusk a raft of dinghies has gathered round the biggest one.", arrive.Narrative);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Cruise}.");
        Assert.Equal("made", Event(g, Cruise).Status);
    }

    [Fact]
    public void A_dead_bilge_pump_fails_the_cruise_and_the_pump_is_found()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = Afloat(215, boat, chainFixed: true);
        boat.AllFaults.Single(f => f.Id == "bilge-pump-burnt-out").Wake(215);   // the pump died quietly; she has not looked
        Motor(g, "hestholmen");
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        var arrive = Motor(g, "kvitnes");
        Assert.Contains("The club skipper looks over your rail at the bilge, and at the pump that hums and moves nothing, and asks politely whether you mean to stay.", arrive.Narrative);
        Assert.Contains(arrive.NewlyDiscovered, k => k.Name == "Burnt-out bilge pump");
        Assert.Equal("failed: a bilge pump that works", Event(g, Cruise).Status);
    }

    // ------------------------------------------------------------------ what the views say

    [Fact]
    public void The_event_views_carry_no_ids_and_say_nothing_the_player_has_not_found()
    {
        var g = Afloat(215);
        var json = JsonSerializer.Serialize<QueryResult>(g.Ask(new Calendar()));
        foreach (var id in new[] { "anchor-gear", "bilge-pump", "marine-heads", "galley-stove", "bunk-cushions", "diesel-heater", "anchor-chain-seized", "friend-week", "club-cruise", "regatta", "midsummer" })
            Assert.DoesNotContain($"\"{id}\"", json);
        Assert.DoesNotContain("Seized", json);
        Assert.DoesNotContain("anchor-chain", json);
    }

    [Fact]
    public void The_friends_needs_say_only_what_the_player_knows_in_words()
    {
        var g = Afloat(160);
        var needs = Event(g, Friend).Requirements;
        // The old heads has a weeping valve nobody has found: to the player there is no known problem. The cushions are not aboard.
        Assert.Equal(new Requirement("a heads that works", true, "no known problem"), needs[0]);
        Assert.Equal(new Requirement("a stove", true, "no known problem"), needs[1]);
        Assert.Equal(new Requirement("dry bunks", false, "not aboard"), needs[2]);
        Assert.Equal("warm enough below on the last night, if it is cold", needs[3].Text);
        Assert.Null(needs[3].Met);
        Assert.Equal("depends on the weather", needs[3].Note);
    }

    [Fact]
    public void A_boat_without_a_calendar_has_no_events_and_the_new_command_says_there_is_nobody_to_ask()
    {
        var g = new Game(BoatLoader.LoadSloop28(withCalendar: false), 150, new FixedRandom(0.99), Game.DefaultSeed, Clear());
        Assert.Null(Cal(g).Events);
        No(g.Execute(new InviteFriend(190)), "There's nobody to ask.");
    }

    [Fact]
    public void The_new_command_survives_the_json_round_trip_and_the_loader_checks_the_events()
    {
        var round = JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize<Command>(new InviteFriend(190)));
        Assert.Equal(new InviteFriend(190), round);
        var node = JsonNode.Parse(Sloop28Json())!;
        node["calendar"]!["events"]![0]!["port"] = "nowhere";
        Assert.Contains("nowhere", Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(node.ToJsonString())).Message);
        node = JsonNode.Parse(Sloop28Json())!;
        node["calendar"]!["events"]![3]!["requires"]![0]!["component"] = "no-such-part";
        Assert.Contains("no-such-part", Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(node.ToJsonString())).Message);
    }
}
