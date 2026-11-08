using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// The wants, second pass H (design note 010): the club cruise from the Friday to the Sunday, and the friend's week as three beats (the arrival,
/// a day trip to Hestholmen, the last night), each a day the player plays and each judged on its day.
/// </summary>
public class WeekTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string Friend = "A friend's week aboard";
    private const string Cruise = "The club cruise to Kvitnes";
    private const string Arrival = "Your friend comes aboard";
    private const string Trip = "A day trip to Hestholmen";
    private const string Night = "The last night aboard";
    private const string HeaterNeed = "warm enough below on the last night, if it is cold";

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static CalendarView Cal(Game g) => (CalendarView)g.Ask(new Calendar());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static EventView Event(Game g, string name) => Cal(g).Events!.Single(e => e.Name == name);
    private static BeatView Beat(Game g, string text) => Event(g, Friend).Beats!.Single(b => b.Text == text);
    private static UpgradesView Ups(Game g) => (UpgradesView)g.Ask(new Upgrades());

    private static WeatherSeries Cold() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 6, 3));
    private static WeatherSeries Warm() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 18, 13));

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The shipped boat whose friend asks only for the named requirements (by their keys: heads, stove, bunk-cushions, diesel-heater).</summary>
    private static Boat BoatWhoseFriendAsks(params string[] keys)
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        var friend = node["calendar"]!["events"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "friend-week")!;
        var kept = new JsonArray();
        foreach (var r in friend["requires"]!.AsArray())
            if (keys.Contains((r!["id"] ?? r["component"])!.GetValue<string>())) kept.Add(r.DeepClone());
        friend["requires"] = kept;
        return BoatLoader.Load(node.ToJsonString());
    }

    private static Game Afloat(int day, Boat? boat = null, WeatherSeries? weather = null, bool chainFixed = false)
    {
        boat ??= BoatLoader.LoadSloop28();
        if (chainFixed) { boat.Get("anchor-gear").Replace(); boat.Get("anchor-shackle").Replace(); }
        var g = new Game(boat, day, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Warm());
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

    private static Game Invited(int start, Boat? boat = null, WeatherSeries? weather = null)
    {
        var g = Afloat(start - 4, boat, weather);   // the friend wants three days' notice
        Ok(g.Execute(new InviteFriend(start)));
        return g;
    }

    /// <summary>To the morning of the first day (the wait is cut there) and on to the hour the friend comes (cut there too).</summary>
    private static CommandResult ToArrival(Game g)
    {
        Ok(g.Execute(new WaitDays(30)));
        return Ok(g.Execute(new WaitDays(1)));
    }

    /// <summary>On to the morning of the last day, then to the hour of the last night.</summary>
    private static CommandResult ToLastNight(Game g)
    {
        Ok(g.Execute(new WaitDays(30)));
        return Ok(g.Execute(new WaitDays(1)));
    }

    // ------------------------------------------------------------------ the club cruise

    [Fact]
    public void The_club_cruise_pins_to_the_first_Friday_on_or_after_its_day_and_runs_three_days()
    {
        var g = Afloat(150);
        var cruise = Event(g, Cruise);
        Assert.Equal((214, 216), (cruise.FromDay, cruise.ToDay));
        Assert.Equal("Friday", new GameClock(cruise.FromDay).WeekdayName);
        Assert.Equal("Sunday", new GameClock(cruise.ToDay).WeekdayName);
        Assert.Equal("Friday to Sunday, days 214 to 216", cruise.When);
        // Year two: day 574 is a Monday, so it is the Friday after.
        var next = Afloat(300);
        var again = Event(next, Cruise);
        Assert.Equal((578, 580), (again.FromDay, again.ToDay));
        Assert.Equal("Friday", new GameClock(578).WeekdayName);
    }

    /// <summary>Afloat at Skjærvik at eight on the morning of day <paramref name="day"/> (back from a week's wait), the chain and shackle new.</summary>
    private static Game AtSkjaervikOn(int day)
    {
        var g = Afloat(day - 7, chainFixed: true);
        Ok(g.Execute(new WaitWeek()));
        Assert.Equal((day, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        return g;
    }

    [Fact]
    public void Being_at_Kvitnes_by_the_Saturday_evening_makes_the_cruise_without_the_Friday_line()
    {
        var g = AtSkjaervikOn(215);                          // the Saturday morning
        Motor(g, "hestholmen");
        var arrive = Motor(g, "kvitnes");
        Assert.Equal("made", Event(g, Cruise).Status);
        Assert.Contains("Eleven boats lie to their anchors in the bay at Kvitnes, and by dusk a raft of dinghies has gathered round the biggest one.", arrive.Narrative);
        Assert.DoesNotContain(arrive.Narrative, l => l.StartsWith("You come in on the Friday evening", StringComparison.Ordinal));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.Contains("Friday evening raft-up", StringComparison.Ordinal));
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Cruise}.");
    }

    [Fact]
    public void Being_there_by_the_Friday_evening_adds_the_Friday_line_and_the_logbook_says_she_made_the_Friday()
    {
        var g = AtSkjaervikOn(214);                          // the Friday morning
        int left = Status(g).VacationDaysLeft;
        var toHest = Motor(g, "hestholmen");
        Assert.Contains(Log(g).Entries, e => e.Text == "Vacation day: Friday, day 214.");   // one vacation day, a choice and not a toll
        var arrive = Motor(g, "kvitnes");
        Assert.Contains("You come in on the Friday evening as the raft is forming: the club skipper waves you into the line with a boat hook, and you spend the first night in the middle of it, hearing the whole bay settle.", arrive.Narrative);
        Assert.True(Status(g).TimeOfDay.CompareTo("20:00") < 0);
        Assert.Equal("made", Event(g, Cruise).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == "She made the Friday evening raft-up at Kvitnes.");
        Assert.Equal(left - 1, Status(g).VacationDaysLeft);
    }

    [Fact]
    public void Arriving_on_the_Sunday_is_after_the_raft_has_gone_and_the_cruise_is_missed()
    {
        var g = AtSkjaervikOn(216);                          // the Sunday morning
        Motor(g, "hestholmen");
        Motor(g, "kvitnes");
        Assert.Equal("on now", Event(g, Cruise).Status);   // there, but past the Saturday evening: nobody judges it
        var after = Ok(g.Execute(new WaitDays(1)));
        Assert.Contains(after.Alerts, a => a.Text == "The club cruise to Kvitnes went without you.");
    }

    [Fact]
    public void The_Friday_cruise_needs_a_Friday_departure_and_the_first_leg_in_the_morning_from_Skjaervik()
    {
        var g = AtSkjaervikOn(214);
        var first = Motor(g, "kvitnes");   // from Skjaervik, by Hestholmen
        Assert.Equal("Hestholmen", Assert.IsType<TripResultView>(first.Observation).Destination);
        Assert.Contains("from here it is 9 nm on.", Text(first));
        Assert.Equal("on now", Event(g, Cruise).Status);
        Motor(g, "kvitnes");
        Assert.Equal("made", Event(g, Cruise).Status);
        Assert.Contains(Log(g).Entries, e => e.Text.Contains("Friday evening raft-up", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ before the friend is asked

    [Fact]
    public void Before_the_friend_is_asked_the_week_is_to_be_picked_and_has_no_beats_afterwards_it_has_three()
    {
        var g = Afloat(150);
        var before = Event(g, Friend);
        Assert.True(before.ToBePicked);
        Assert.Null(before.Beats);
        Assert.False(before.Going);
        Ok(g.Execute(new InviteFriend(187)));
        var after = Event(g, Friend);
        Assert.False(after.ToBePicked);
        Assert.True(after.Going);
        Assert.Equal([Arrival, Trip, Night], after.Beats!.Select(b => b.Text));
        Assert.All(after.Beats!, b => Assert.Equal("ahead", b.Status));
        Assert.Equal("Saturday, day 187, about 17:00", after.Beats![0].When);
        Assert.Equal("Friday, day 193, about 22:00", after.Beats![2].When);
        Assert.Equal("a heads that works, a stove, dry bunks", after.Beats![0].Note);
        Assert.Equal(HeaterNeed, after.Beats![2].Note);
    }

    // ------------------------------------------------------------------ the arrival

    [Fact]
    public void The_friend_comes_down_the_pontoon_at_the_hour_in_the_data_and_asks_for_Hestholmen()
    {
        var g = Invited(187, BoatWhoseFriendAsks());
        Ok(g.Execute(new WaitDays(30)));
        Assert.Equal("on now", Beat(g, Arrival).Status);
        var arrive = Ok(g.Execute(new WaitDays(1)));
        Assert.Equal((187, "17:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Contains("You come back for your friend: the ferry is in, and someone is coming down the pontoon.", arrive.Narrative);
        Assert.Contains("Your friend comes down the pontoon with a rucksack and a bag of oranges, looks round the saloon, and says that it is better than they remembered.", arrive.Narrative);
        Assert.Contains("Your friend has looked at the chart and wants to see Hestholmen.", arrive.Narrative);
        Assert.Equal("made", Event(g, Friend).Status);
        Assert.Equal(["made", "on now", "ahead"], Event(g, Friend).Beats!.Select(b => b.Status));
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Friend}.");
    }

    [Fact]
    public void A_hard_requirement_that_fails_at_the_arrival_ends_the_week_and_the_later_beats_do_not_happen()
    {
        var g = Invited(190);                              // the shipped boat: the old heads weeps, there are no cushions
        var arrive = ToArrival(g);
        Assert.Contains("Your friend opens the forepeak door, takes one breath, shuts it again, and books the hotel by the ferry.", arrive.Narrative);
        Assert.Equal("failed: a heads that works", Event(g, Friend).Status);
        Assert.Equal(["failed: a heads that works", "missed", "missed"], Event(g, Friend).Beats!.Select(b => b.Status));
        Assert.Contains(Log(g).Entries, e => e.Text == $"Missed: {Friend} (a heads that works).");
        // Nothing of the week happens again: a passage to Hestholmen is only a passage, and the last night is a night.
        var trip = Motor(g, "hestholmen");
        Assert.DoesNotContain("friend", Text(trip), StringComparison.OrdinalIgnoreCase);
        var rest = Ok(g.Execute(new WaitDays(30)));
        Assert.DoesNotContain("friend", Text(rest), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Missed: {Friend} (a heads that works).");
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.StartsWith("Made it:", StringComparison.Ordinal) && e.Text.Contains("Hestholmen"));
    }

    [Fact]
    public void A_friend_who_finds_the_boat_away_from_Skjaervik_at_the_hour_takes_the_ferry_home_and_the_beats_are_missed()
    {
        var g = Afloat(188, BoatWhoseFriendAsks());
        Ok(g.Execute(new InviteFriend(191)));
        Motor(g, "hestholmen");
        Ok(g.Execute(new WaitDays(10)));                   // the morning of the 191st, at Hestholmen
        Ok(g.Execute(new WaitDays(1)));
        Assert.Equal("missed", Event(g, Friend).Status);
        Assert.Equal(["missed", "missed", "missed"], Event(g, Friend).Beats!.Select(b => b.Status));
        Assert.Single(Log(g).Entries, e => e.Text == "Your friend waited on the quay at Skjærvik and took the evening ferry home.");
    }

    // ------------------------------------------------------------------ the day trip

    [Fact]
    public void The_day_trip_is_made_when_she_arrives_at_Hestholmen_in_the_week_with_the_friend_aboard()
    {
        var g = Invited(187, BoatWhoseFriendAsks());
        ToArrival(g);
        var arrive = Motor(g, "hestholmen");
        Assert.Contains("Your friend stands in the bow all the way in, pointing at things, and on the quay at Hestholmen buys two ice creams and a postcard of the harbour you are standing in.", arrive.Narrative);
        Assert.Equal("made", Beat(g, Trip).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Trip}.");
    }

    [Fact]
    public void The_friend_is_aboard_on_a_passage_that_casts_off_on_a_later_day_of_the_week_too()
    {
        var g = Invited(187, BoatWhoseFriendAsks());
        ToArrival(g);
        var walk = Ok(g.Execute(new WaitDays(2)));          // Monday: the friend goes walking, and the days cost nothing
        Assert.Contains("Your friend goes walking on the island while you are at work.", walk.Narrative);
        Assert.Equal("on now", Beat(g, Trip).Status);
        Motor(g, "hestholmen");
        Assert.Equal("made", Beat(g, Trip).Status);
    }

    [Fact]
    public void A_passage_before_the_friend_has_come_is_not_the_day_trip_and_neither_is_a_tow_in()
    {
        // Before: she goes to Hestholmen on the morning of the first day and is home well before the hour.
        var g = Afloat(183, BoatWhoseFriendAsks());
        Ok(g.Execute(new InviteFriend(187)));
        Ok(g.Execute(new WaitDays(30)));
        Motor(g, "hestholmen");
        // She was at sea at the friend's hour: nobody came, so the passage was not the friend's day trip, and the week is gone.
        Assert.Equal(["missed", "missed", "missed"], Event(g, Friend).Beats!.Select(b => b.Status));

        // A tow in: the friend aboard, a passage towed to Hestholmen is no passage the player made.
        var h = Invited(187, BoatWhoseFriendAsks());
        ToArrival(h);
        Walk.ToPlace(h, "cockpit");
        Ok(Walk.Start(h));
        Ok(h.Execute(new Depart("hestholmen")));
        Ok(h.Execute(new Motor()));
        Ok(h.Execute(new Continue()));                      // 10 of the 14 nm: past the half-way mark, so the towboat takes her on
        var tow = Ok(h.Execute(new CallTow()));
        Assert.True(Assert.IsType<TripResultView>(tow.Observation).Towed);
        Assert.Equal("Hestholmen", Assert.IsType<TripResultView>(tow.Observation).Destination);
        Assert.DoesNotContain("two ice creams", Text(tow));
        Assert.NotEqual("made", Beat(h, Trip).Status);
    }

    // ------------------------------------------------------------------ the last night

    /// <summary>Afloat with the heater fitted (ordered, fitted in the quarter berth), the friend asking only for it, her week from day 190.</summary>
    private static Game WithHeater(WeatherSeries weather)
    {
        var g = Afloat(180, BoatWhoseFriendAsks("diesel-heater"), weather);
        Ok(g.Execute(new Order("diesel heater")));
        for (int i = 0; i < 12 && Ups(g).Items.Single(u => u.Name == "diesel heater").Status != "aboard, not fitted"; i++) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "quarter-berth");
        Ok(g.Execute(new Install("diesel heater")));
        Ok(g.Execute(new InviteFriend(Math.Max(Status(g).Day + 3, 190))));
        return g;
    }

    [Fact]
    public void On_a_cold_last_night_the_week_needs_a_working_heater_and_without_one_the_beat_fails_in_its_own_words()
    {
        var g = Invited(190, BoatWhoseFriendAsks("diesel-heater"), Cold());
        ToArrival(g);
        var night = ToLastNight(g);
        Assert.Equal((196, "22:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Contains("You come back for the evening: it is your friend's last night aboard.", night.Narrative);
        Assert.Contains("By the last evening your friend is in two jumpers and a hat, below, and asks gently whether there is heating. There is not.", night.Narrative);
        Assert.Equal($"failed: {HeaterNeed}", Beat(g, Night).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Missed: {Night} ({HeaterNeed}).");
    }

    [Fact]
    public void On_a_cold_last_night_a_working_heater_makes_the_night()
    {
        var g = WithHeater(Cold());
        ToArrival(g);
        var night = ToLastNight(g);
        Assert.Contains("The heater ticks and glows below, and your friend sits in the warm with a mug and says it is the best last night of any holiday.", night.Narrative);
        Assert.Equal("made", Beat(g, Night).Status);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Made it: {Night}.");
    }

    [Fact]
    public void On_a_warm_last_night_the_beat_passes_with_the_warm_night_line_and_no_heater_is_asked()
    {
        var g = Invited(190, BoatWhoseFriendAsks("diesel-heater"), Warm());
        ToArrival(g);
        var night = ToLastNight(g);
        Assert.Contains("The last night is mild enough to sit out in the cockpit till late, and nobody thinks about the heater at all.", night.Narrative);
        Assert.DoesNotContain("two jumpers", Text(night));
        Assert.Equal("made", Beat(g, Night).Status);
    }

    [Fact]
    public void The_last_night_is_spent_afloat_at_any_port_not_only_at_Skjaervik()
    {
        var g = Invited(187, BoatWhoseFriendAsks(), Warm());
        ToArrival(g);
        Motor(g, "hestholmen");                             // the trip is made; she lies at Hestholmen for the last night
        var night = ToLastNight(g);
        Assert.Equal("made", Beat(g, Night).Status);
        Assert.Contains("The last night is mild enough", Text(night));
    }

    // ------------------------------------------------------------------ made, thin, good

    [Fact]
    public void A_week_is_good_with_both_the_trip_and_the_night_a_week_with_one_and_thin_with_the_arrival_only()
    {
        // Good: the trip and a warm night.
        var good = Invited(187, BoatWhoseFriendAsks("diesel-heater"), Warm());
        ToArrival(good);
        Motor(good, "hestholmen");
        var end = ToLastNight(good);
        Assert.Equal("a good week: the day trip and the last night", Event(good, Friend).Outcome);
        Assert.Contains("A good week: the island trip and the last night both came off, and your friend goes home talking about it.", end.Narrative);
        Assert.Contains(Log(good).Entries, e => e.Text.StartsWith("A good week:", StringComparison.Ordinal));

        // A week, by the trip alone: cold night, no heater.
        var tripOnly = Invited(187, BoatWhoseFriendAsks("diesel-heater"), Cold());
        ToArrival(tripOnly);
        Motor(tripOnly, "hestholmen");
        ToLastNight(tripOnly);
        Assert.StartsWith("a week:", Event(tripOnly, Friend).Outcome);
        Assert.Equal("made", Event(tripOnly, Friend).Status);

        // A week, by the night alone: no trip, a warm night.
        var nightOnly = Invited(187, BoatWhoseFriendAsks("diesel-heater"), Warm());
        ToArrival(nightOnly);
        var nightEnd = ToLastNight(nightOnly);
        Assert.Contains("The week goes by without Hestholmen.", Text(nightEnd));
        Assert.Equal(["made", "missed", "made"], Event(nightOnly, Friend).Beats!.Select(b => b.Status));
        Assert.StartsWith("a week:", Event(nightOnly, Friend).Outcome);

        // Thin: the arrival and nothing else.
        var thin = Invited(187, BoatWhoseFriendAsks("diesel-heater"), Cold());
        ToArrival(thin);
        var thinEnd = ToLastNight(thin);
        Assert.Contains("A thin week: your friend came aboard, and the island trip and the last night came to nothing.", thinEnd.Narrative);
        Assert.StartsWith("a thin week:", Event(thin, Friend).Outcome);
        Assert.Equal("made", Event(thin, Friend).Status);   // the arrival was made: the week counts, thinly
    }

    // ------------------------------------------------------------------ waits

    [Fact]
    public void Waits_stop_on_each_beats_morning_and_at_the_hour_of_a_beat_on_its_own_day()
    {
        var g = Invited(190, BoatWhoseFriendAsks(), Warm());
        var morning = Ok(g.Execute(new WaitDays(30)));
        Assert.Contains("You come back on day 190: A friend's week aboard starts today.", morning.Narrative);
        Assert.Equal(190, Status(g).Day);
        var hour = Ok(g.Execute(new WaitDays(1)));
        Assert.Contains("You come back for your friend: the ferry is in, and someone is coming down the pontoon.", hour.Narrative);
        Assert.Equal("17:00", Status(g).TimeOfDay);
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited until 17:00.");

        var last = Ok(g.Execute(new WaitDays(30)));
        Assert.Contains("You come back: it is your friend's last night aboard tonight.", last.Narrative);
        Assert.Contains("Your friend goes walking on the island while you are at work.", last.Narrative);
        Assert.Equal(196, Status(g).Day);
        var night = Ok(g.Execute(new WaitDays(5)));
        Assert.Contains("You come back for the evening: it is your friend's last night aboard.", night.Narrative);
        Assert.Equal("22:00", Status(g).TimeOfDay);
        // After the week nothing stops a wait.
        var after = Ok(g.Execute(new WaitDays(3)));
        Assert.DoesNotContain("friend", Text(after), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_week_wait_and_next_weekend_stop_the_same_way()
    {
        var g = Invited(190, BoatWhoseFriendAsks(), Warm());
        var week = Ok(g.Execute(new WaitWeek()));
        Assert.Contains("You come back on day 190: A friend's week aboard starts today.", week.Narrative);
        var weekend = Ok(g.Execute(new NextWeekend()));
        Assert.Contains("You come back for your friend: the ferry is in, and someone is coming down the pontoon.", weekend.Narrative);
        Assert.Equal((190, "17:00"), (Status(g).Day, Status(g).TimeOfDay));
    }

    // ------------------------------------------------------------------ the cost

    [Fact]
    public void The_invite_says_what_the_week_costs_and_the_weekday_rule_charges_what_the_player_does()
    {
        var g = Afloat(185, BoatWhoseFriendAsks(), Warm());
        var invite = Ok(g.Execute(new InviteFriend(190)));              // Tuesday 190 to Monday 196
        Assert.Contains("Weekdays you spend with your friend cost vacation days as usual.", invite.Narrative);
        int left = Status(g).VacationDaysLeft;

        ToArrival(g);                                                    // a weekday, but the player was at work until the friend came
        Assert.Equal(left, Status(g).VacationDaysLeft);
        var walk = Ok(g.Execute(new WaitDays(1)));                       // Wednesday at work: the friend goes walking, nothing is charged
        Assert.Contains("Your friend goes walking on the island while you are at work.", walk.Narrative);
        Assert.Equal(left, Status(g).VacationDaysLeft);

        var trip = Motor(g, "hestholmen");                               // a weekday at sea is a vacation day, as always
        Assert.Contains(Log(g).Entries, e => e.Text == "Vacation day: Wednesday, day 191.");
        Assert.Equal(left - 1, Status(g).VacationDaysLeft);
        Assert.Equal("made", Beat(g, Trip).Status);
    }

    [Fact]
    public void A_weekday_aboard_with_the_friend_past_an_evening_costs_a_vacation_day_by_the_ordinary_rule()
    {
        var g = Afloat(185, BoatWhoseFriendAsks(), Warm());
        Ok(g.Execute(new InviteFriend(190)));
        ToArrival(g);                                                    // 17:00 on the Tuesday
        int left = Status(g).VacationDaysLeft;
        Walk.Near(g, "negative terminal");
        string said = "";
        for (int i = 0; i < 80 && Status(g).VacationDaysLeft == left; i++) said = Text(Ok(g.Execute(new Inspect("negative terminal"))));
        Assert.Equal(left - 1, Status(g).VacationDaysLeft);
        Assert.Contains("becomes a vacation day", said);
    }

    // ------------------------------------------------------------------ the data

    [Fact]
    public void The_loader_checks_the_beats_of_an_event()
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        node["calendar"]!["events"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "friend-week")!["beats"]![1]!["port"] = "nowhere";
        Assert.Contains("nowhere", Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(node.ToJsonString())).Message);
        node = JsonNode.Parse(Sloop28Json())!;
        node["calendar"]!["events"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "friend-week")!["beats"]![0]!["kind"] = "lunch";
        Assert.Contains("lunch", Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(node.ToJsonString())).Message);
    }
}
