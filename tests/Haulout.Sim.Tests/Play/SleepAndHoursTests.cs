using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// Sleep and the hours (design note 012, Amit's answers): nothing stops at any hour; the skipper has hours awake, tiredness multiplies the
/// chance of a poor job and from exhaustion hides the subtle signs; the sleep command; shops and the yard office keep Monday to Saturday
/// 09:00 to 17:00; the harbourmaster does not ring in the night. A boat built without hours has none of it.
/// </summary>
public class SleepAndHoursTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static CommandResult No(CommandResult r)
    {
        Assert.False(r.Accepted);
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static SaleView Sale(Game g) => (SaleView)g.Ask(new Sale());
    private static WeatherSeries Warm() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 18, 13));

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The economy with the harbourmaster's night moved, and (when asked) the shops open all day every day.</summary>
    private static string Eco(string nightFrom = "22:00", bool shopsOpen = true) => TestEconomy.Edited(n =>
    {
        var h = n["hours"]!;
        h["nightFrom"] = nightFrom;
        if (!shopsOpen) return;
        foreach (var day in new[] { "weekday", "saturday", "sunday" }) h["shop"]![day] = new JsonArray("00:00", "24:00");
    });

    private static Game NewGame(double roll = 0.99, int startDay = 75, string nightFrom = "22:00") =>
        new(BoatLoader.Load(Sloop28Json(), true, Eco(nightFrom), withHours: true), startDay, new FixedRandom(roll), Game.DefaultSeed, Warm());

    private static Game Shipped(int startDay) =>
        new(BoatLoader.LoadSloop28(withHours: true), startDay, new FixedRandom(0.99), Game.DefaultSeed, Warm());

    private static void Until(Game g, string time)
    {
        for (int i = 0; i < 400 && Status(g).TimeOfDay != time; i++) Ok(g.Execute(new Look()));
        Assert.Equal(time, Status(g).TimeOfDay);
    }

    /// <summary>Looks around until the clock is at or past the time (same day), two minutes at a time.</summary>
    private static void UntilAtLeast(Game g, string time)
    {
        for (int i = 0; i < 600 && string.CompareOrdinal(Status(g).TimeOfDay, time) < 0; i++) Ok(g.Execute(new Look()));
        Assert.True(string.CompareOrdinal(Status(g).TimeOfDay, time) >= 0);
    }


    private static Game Afloat(string nightFrom = "22:00", Boat? boat = null)
    {
        var g = boat is null ? NewGame(nightFrom: nightFrom, startDay: 186) : new Game(boat, 186, new FixedRandom(0.99), Game.DefaultSeed, Warm());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        return g;
    }

    /// <summary>Hours of own work on the hard: each charge is eight hours at the yard's charger.</summary>
    private static void Charge(Game g, int times)
    {
        for (int i = 0; i < times; i++) Ok(Walk.Do(g, "start battery", n => new Charge(n)));
    }

    // ------------------------------------------------------------------ the data

    [Fact]
    public void The_hours_and_the_sleep_rules_come_from_economy_json_and_a_boat_built_without_them_has_none()
    {
        var boat = BoatLoader.LoadSloop28(withHours: true);
        var h = boat.Hours;
        Assert.True(h.Enabled);
        Assert.Equal((22 * 60, 8 * 60), (h.NightFromMinute, h.HarbourmasterFirstCallMinute));
        Assert.Equal((9 * 60, 17 * 60), (h.Weekday.OpenMinute, h.Weekday.CloseMinute));
        Assert.Equal((9 * 60, 17 * 60), (h.Saturday.OpenMinute, h.Saturday.CloseMinute));
        Assert.True(h.Sunday.IsShut);
        var s = boat.Sleep;
        Assert.True(s.Enabled);
        Assert.Equal((16 * 60, 20 * 60, 1.5, 3.0), (s.TiredAtMinutes, s.ExhaustedAtMinutes, s.BotchFactorTired, s.BotchFactorExhausted));
        Assert.Equal((8 * 60, 8 * 60, 6 * 60), (s.WakeMinute, s.SleepMinutes, s.MinSleepMinutes));
        var plain = BoatLoader.LoadSloop28();
        Assert.False(plain.Hours.Enabled);
        Assert.False(plain.Sleep.Enabled);
        Assert.False(BoatLoader.LoadSloop28(withCalendar: false, withHours: true).Sleep.Enabled);
    }

    [Fact]
    public void A_boat_without_the_rules_is_never_tired_and_its_botch_chance_never_moves()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.3), Game.DefaultSeed, Warm());
        Charge(g, 3);
        Assert.Null(Status(g).Tired);
        Assert.Equal(1.0, g.BotchFactorForTests);
        var r = Ok(Walk.Do(g, "impeller", n => new Replace(n)));
        Assert.DoesNotContain("tired", Text(r), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Ok(g.Execute(new Inspect("impeller"))).NewlyDiscovered);   // 0.3 is not under 0.25: no poor job
        var no = g.Execute(new Sleep());
        Assert.False(no.Accepted);
    }

    // ------------------------------------------------------------------ nothing stops at a clock

    [Fact]
    public void Nothing_stops_at_night_a_long_job_runs_straight_through_and_the_clock_shows_it()
    {
        var g = NewGame();
        UntilAtLeast(g, "21:50");
        Assert.Equal(75, Status(g).Day);   // two minutes at a time from 08:00: we are still on Saturday
        var r = Ok(Walk.Do(g, "raw-water seacock", n => new Replace(n)));
        Assert.DoesNotContain("turn in", Text(r), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Log(g).Entries, e => e.Text == "Replaced the raw-water seacock.");
        Assert.True(Status(g).Day == 76 || string.CompareOrdinal(Status(g).TimeOfDay, "22:00") > 0);
    }

    // ------------------------------------------------------------------ hours awake

    [Fact]
    public void Hours_awake_run_on_own_work_and_the_status_says_tired_then_exhausted_in_words()
    {
        var g = NewGame();
        Assert.Null(Status(g).Tired);
        Charge(g, 1);
        Assert.Null(Status(g).Tired);                        // eight hours: fresh
        Assert.InRange(g.AwakeMinutesForTests, 8 * 60, 8 * 60 + 15);
        Charge(g, 1);
        Assert.Contains("tired", Status(g).Tired);
        Assert.DoesNotContain("exhausted", Status(g).Tired);
        Assert.Matches(@"^You've been up \d+ hours: tired\.$", Status(g).Tired);
        Assert.Equal(1.5, g.BotchFactorForTests);
        Charge(g, 1);
        Assert.Contains("exhausted, your eyes are not to be trusted", Status(g).Tired);
        Assert.Equal(3.0, g.BotchFactorForTests);
    }

    [Fact]
    public void An_away_stretch_of_six_hours_or_more_is_sleep_and_a_shorter_one_is_awake_time()
    {
        var g = NewGame();
        Charge(g, 2);
        Assert.NotNull(Status(g).Tired);
        Ok(g.Execute(new WaitDays(1)));                      // a day away: slept
        Assert.Equal(0, g.AwakeMinutesForTests);
        Assert.Null(Status(g).Tired);

        // The friend's arrival cuts a wait two hours short of the day: that is not sleep.
        var node = JsonNode.Parse(Sloop28Json())!;
        node["calendar"]!["events"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "friend-week")!["requires"] = new JsonArray();
        var f = Afloat(boat: BoatLoader.Load(node.ToJsonString(), true, Eco(), withHours: true));
        Ok(f.Execute(new InviteFriend(190)));
        Ok(f.Execute(new WaitDays(30)));                     // to the first morning
        Assert.Equal(0, f.AwakeMinutesForTests);
        Ok(f.Execute(new WaitDays(1)));                      // cut at the friend's hour
        Assert.InRange(f.AwakeMinutesForTests, 60, 6 * 60 - 1);
    }

    [Fact]
    public void At_sea_under_way_the_hours_awake_grow_at_half_rate_because_you_doze_in_the_cockpit()
    {
        var g = Afloat();
        UntilAtLeast(g, "12:00");
        int before = g.AwakeMinutesForTests;
        Ok(g.Execute(new Depart("hestholmen")));
        int afterDeparting = g.AwakeMinutesForTests;
        var r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 40 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        var trip = Assert.IsType<TripResultView>(r.Observation);
        // Casting off is on the mooring (full rate); the passage after it counts half.
        double expected = trip.Minutes * 0.5;
        Assert.InRange(g.AwakeMinutesForTests - afterDeparting, expected - 5, expected + 5);
        Assert.InRange(afterDeparting - before, 0, 30);
    }

    private static string ShortTiredArrivalEconomy() => TestEconomy.Edited(n =>
    {
        foreach (var day in new[] { "weekday", "saturday", "sunday" }) n["hours"]!["shop"]![day] = new JsonArray("00:00", "24:00");
        n["sleep"]!["tiredArrivalHours"] = 2;
    });

    private static TripResultView Passage(Game g, out CommandResult last)
    {
        Ok(g.Execute(new Depart("hestholmen")));
        last = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 40 && last.Observation is not TripResultView; i++) last = Ok(g.Execute(new Continue()));
        return Assert.IsType<TripResultView>(last.Observation);
    }

    [Fact]
    public void A_passage_longer_than_the_tired_arrival_hours_makes_the_arrival_a_tired_one_until_the_skipper_sleeps()
    {
        var g = Afloat(boat: BoatLoader.Load(Sloop28Json(), true, ShortTiredArrivalEconomy(), withHours: true));
        Assert.False(g.TiredArrivalForTests);
        Passage(g, out var r);
        Assert.Contains("Three hours at the helm: you're tired, and it will show in anything you do before you sleep.", r.Narrative);
        Assert.True(g.TiredArrivalForTests);
        Assert.Equal(1.5, g.BotchFactorForTests);                     // at least the tired factor, though only a few hours awake
        Assert.Equal("You came in tired from the long passage: it will show in anything you do before you sleep.", Status(g).Tired);
        Assert.InRange(g.AwakeMinutesForTests, 0, 16 * 60 - 1);

        // Any own-work job that can botch says so; a sleep puts it right.
        Ok(g.Execute(new Buy("primary filter element")));
        var job = Ok(Walk.Do(g, "primary filter", n => new Replace(n)));        // a subsystem she has not worked in yet: a job that can come out poor
        Assert.Contains("You're tired; this is the kind of job that gets done twice.", job.Narrative);
        Ok(g.Execute(new Sleep()));
        Assert.False(g.TiredArrivalForTests);
        Assert.Equal(1.0, g.BotchFactorForTests);
        Assert.Null(Status(g).Tired);
    }

    [Fact]
    public void An_away_stretch_of_six_hours_also_clears_a_tired_arrival_and_a_short_passage_never_sets_one()
    {
        var g = Afloat(boat: BoatLoader.Load(Sloop28Json(), true, ShortTiredArrivalEconomy(), withHours: true));
        Passage(g, out _);
        Assert.True(g.TiredArrivalForTests);
        Ok(g.Execute(new WaitDays(1)));
        Assert.False(g.TiredArrivalForTests);

        // The shipped twelve hours: a passage of three hours is not a tired arrival.
        var h = Afloat();
        Passage(h, out var s);
        Assert.False(h.TiredArrivalForTests);
        Assert.DoesNotContain(s.Narrative, l => l.Contains("at the helm", StringComparison.Ordinal));
    }

    [Fact]
    public void Sleep_sleeps_to_the_next_eight_when_that_is_six_to_twelve_hours_away_else_eight_hours()
    {
        var g = NewGame();
        Until(g, "08:30");
        var r = Ok(g.Execute(new Sleep()));
        Assert.Equal(8 * 60, r.MinutesTaken);                          // from 08:30 not 23 and a half hours
        Assert.Equal("16:30", Status(g).TimeOfDay);
        Assert.Equal(75, Status(g).Day);
        Assert.Contains("up at 16:30", Text(r));

        var h = NewGame();
        UntilAtLeast(h, "20:00");
        var wake = Ok(h.Execute(new Sleep()));                         // twelve hours away (or a little less): to eight
        Assert.Equal("08:00", Status(h).TimeOfDay);
        Assert.Equal(76, Status(h).Day);
        Assert.InRange(wake.MinutesTaken, 6 * 60, 12 * 60);
    }

    // ------------------------------------------------------------------ the yard lifts Monday to Saturday

    [Fact]
    public void The_yard_never_offers_a_Sunday_slot_in_any_year_and_the_rule_is_what_moved_it()
    {
        int sundays = 0, sundaysWithoutHours = 0;
        foreach (int year in new[] { 0, 1, 2 })
            for (int d = 235; d <= 300; d++)
            {
                int day = year * 360 + d;
                var withHours = new Game(BoatLoader.LoadSloop28(withHours: true), day, new FixedRandom(0.99), Game.DefaultSeed, Warm());
                if (((SeasonView)withHours.Ask(new Season())).FirstFreeDay is { } first && new Haulout.Sim.Time.GameClock(first).Weekday == 6) sundays++;
                var plain = new Game(BoatLoader.LoadSloop28(), day, new FixedRandom(0.99), Game.DefaultSeed, Warm());
                if (((SeasonView)plain.Ask(new Season())).FirstFreeDay is { } f && new Haulout.Sim.Time.GameClock(f).Weekday == 6) sundaysWithoutHours++;
            }
        Assert.Equal(0, sundays);
        Assert.True(sundaysWithoutHours > 0);   // without the rule the same sweep does find Sundays
    }

    [Fact]
    public void Booking_a_Sunday_is_refused_with_the_reason_free_and_a_Saturday_or_Monday_is_accepted()
    {
        var g = new Game(BoatLoader.LoadSloop28(withHours: true), 75, new FixedRandom(0.99), Game.DefaultSeed, Warm());
        Assert.Equal(6, new Haulout.Sim.Time.GameClock(258).Weekday);   // a Sunday inside the window
        UntilAtLeast(g, "09:00");                                       // the office opens at nine
        var no = No(g.Execute(new BookHaulout(258)));
        Assert.Equal("The yard doesn't lift on Sundays: the nearest day it does is day 259.", Text(no));
        Assert.Null(((SeasonView)g.Ask(new Season())).BookedDay);
        Ok(g.Execute(new BookHaulout(259)));
        Assert.Equal(259, ((SeasonView)g.Ask(new Season())).BookedDay);
        Ok(g.Execute(new BookHaulout(257)));                            // a Saturday is fine
        Assert.Equal(257, ((SeasonView)g.Ask(new Season())).BookedDay);
    }

    // ------------------------------------------------------------------ tiredness and the botch chance

    private static Game WithSpareImpeller(double roll)
    {
        var g = NewGame(roll);
        UntilAtLeast(g, "09:00");
        Ok(g.Execute(new Buy("impeller")));
        return g;
    }

    [Fact]
    public void A_job_that_can_botch_says_so_when_tired_and_the_same_roll_that_passed_rested_fails_tired()
    {
        // 0.3 is over the rested chance (0.25) and under the tired one (0.375).
        var rested = WithSpareImpeller(0.3);
        var fine = Ok(Walk.Do(rested, "impeller", n => new Replace(n)));
        Assert.DoesNotContain("tired", Text(fine), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Ok(rested.Execute(new Inspect("impeller"))).NewlyDiscovered);

        var tired = WithSpareImpeller(0.3);
        Charge(tired, 2);
        var job = Ok(Walk.Do(tired, "impeller", n => new Replace(n)));
        Assert.Contains("You're tired; this is the kind of job that gets done twice.", job.Narrative);
        var look = Ok(tired.Execute(new Inspect("impeller")));
        var found = Assert.Single(look.NewlyDiscovered);
        Assert.Equal("Hose clamp not seated (your own work)", found.Name);
        Assert.Contains(look.Narrative, l => l.Contains("tired work", StringComparison.Ordinal));        // the cause line, told when it is found
        Assert.Contains(Log(tired).Entries, e => e.Text.Contains("tired work", StringComparison.Ordinal));
        Assert.DoesNotContain("poor-job", Text(look));                                                    // no ids
    }

    [Fact]
    public void Exhausted_the_factor_is_three_and_the_line_is_stronger()
    {
        // 0.5 is over the tired chance (0.375) and under the exhausted one (0.75).
        var tired = WithSpareImpeller(0.5);
        Charge(tired, 2);
        Ok(Walk.Do(tired, "impeller", n => new Replace(n)));
        Assert.Empty(Ok(tired.Execute(new Inspect("impeller"))).NewlyDiscovered);

        var wrecked = WithSpareImpeller(0.5);
        Charge(wrecked, 3);
        var job = Ok(Walk.Do(wrecked, "impeller", n => new Replace(n)));
        Assert.Contains("You're exhausted; this job will almost certainly have to be done twice.", job.Narrative);
        Assert.Single(Ok(wrecked.Execute(new Inspect("impeller"))).NewlyDiscovered);
    }

    // ------------------------------------------------------------------ eyes

    private static CommandResult LookUnderTheBoot(Game g)
    {
        Walk.ToPlace(g, "quarter-berth");
        Walk.Uncover(g, "battery-box-lid");
        Ok(g.Execute(new Uncover("negative terminal")));
        return Ok(g.Execute(new Inspect("negative terminal")));
    }

    [Fact]
    public void Exhausted_inspect_misses_the_bloom_under_the_boot_and_says_so_and_a_rested_look_finds_it()
    {
        const string Bloom = "white bloom along the edge of the clamp";
        var rested = NewGame();
        Assert.Contains(Bloom, Text(LookUnderTheBoot(rested)));

        var g = NewGame();
        Charge(g, 3);
        var tired = LookUnderTheBoot(g);
        Assert.Contains("Your eyes are not to be trusted tonight.", tired.Narrative);
        Assert.DoesNotContain(Bloom, Text(tired));
        Assert.Empty(tired.NewlyDiscovered);                 // nothing that needed sharp eyes was found

        Ok(g.Execute(new WaitDays(1)));                      // slept
        var again = Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Contains(Bloom, Text(again));
        Assert.DoesNotContain("not to be trusted", Text(again));
    }

    // ------------------------------------------------------------------ the sleep command

    [Fact]
    public void Sleep_from_the_evening_wakes_at_eight_and_from_the_small_hours_sleeps_eight_hours()
    {
        var g = NewGame();
        UntilAtLeast(g, "23:00");
        var night = Ok(g.Execute(new Sleep()));
        Assert.Contains("You turn in. Sunday, day 76: up at 08:00.", night.Narrative);
        Assert.Equal((76, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Equal(0, g.AwakeMinutesForTests);
        Assert.Contains(Log(g).Entries, e => e.Text == "Slept until 08:00.");

        var small = NewGame();
        for (int i = 0; i < 700 && !(small.Ask(new Status()) is StatusView { Day: 76, TimeOfDay: var t } && string.CompareOrdinal(t, "03:00") >= 0); i++)
            Ok(small.Execute(new Look()));
        var early = Ok(small.Execute(new Sleep()));          // 08:00 is five hours away: sleep eight
        Assert.Equal(76, Status(small).Day);
        Assert.Contains(Status(small).TimeOfDay, new[] { "11:00", "11:01", "11:02" });
        Assert.Contains("up at 11:0", Text(early));
    }

    [Fact]
    public void Sleep_is_refused_under_way_and_allowed_at_anchor()
    {
        var g = Afloat();
        Ok(g.Execute(new Depart("hestholmen")));
        Ok(g.Execute(new Motor()));
        var refused = g.Execute(new Sleep());
        Assert.False(refused.Accepted);
        Assert.Equal("You're under way: heave to or anchor first.", Assert.Single(refused.Narrative));
        Assert.Equal(0, refused.MinutesTaken);
    }

    // ------------------------------------------------------------------ shop and office hours

    [Fact]
    public void The_chandlery_opens_at_nine_on_a_Saturday_and_is_shut_from_five()
    {
        var g = Shipped(75);                                  // Saturday 08:00
        var early = No(g.Execute(new Buy("impeller")));
        Assert.Equal("The yard chandlery opens at nine.", Assert.Single(early.Narrative));
        UntilAtLeast(g, "09:00");
        Ok(g.Execute(new Buy("impeller")));
        UntilAtLeast(g, "16:30");
        Ok(g.Execute(new Buy("alternator belt")));            // open until five
        UntilAtLeast(g, "17:00");
        var late = No(g.Execute(new Buy("primary filter element")));
        Assert.Equal("The yard chandlery has closed for the day; it opens at nine on Monday.", Assert.Single(late.Narrative));
    }

    [Fact]
    public void The_chandlery_is_shut_on_a_Sunday_and_names_Mondays_opening()
    {
        var g = Shipped(76);
        var r = No(g.Execute(new Buy("impeller")));
        Assert.Equal("The yard chandlery is shut on Sundays; it opens at nine tomorrow.", Assert.Single(r.Narrative));
    }

    [Fact]
    public void On_a_weekday_the_chandlery_is_open_nine_to_five_and_a_Friday_evening_names_Saturday()
    {
        var g = Shipped(77);                                  // Monday
        Assert.Contains("opens at nine", Text(No(g.Execute(new Buy("impeller")))));
        UntilAtLeast(g, "10:00");
        Ok(g.Execute(new Buy("impeller")));
        UntilAtLeast(g, "17:00");
        Assert.Equal("The yard chandlery has closed for the day; it opens at nine tomorrow.", Assert.Single(No(g.Execute(new Buy("alternator belt"))).Narrative));
        var friday = Shipped(81);
        UntilAtLeast(friday, "17:00");
        Assert.Equal("The yard chandlery has closed for the day; it opens at nine tomorrow.", Assert.Single(No(friday.Execute(new Buy("impeller"))).Narrative));
    }

    [Fact]
    public void Replacing_with_a_part_bought_on_the_spot_keeps_the_shop_hours()
    {
        var g = Shipped(76);                                  // Sunday: the chandlery is shut
        var shut = No(Walk.Do(g, "raw-water seacock", n => new Replace(n)));
        Assert.Contains("is shut on Sundays", Text(shut));
        Assert.Equal(25000, Status(g).BalanceNok);
    }

    [Fact]
    public void The_yard_office_keeps_the_same_hours_for_its_jobs_the_booking_and_the_orders()
    {
        var g = Shipped(76);                                  // Sunday
        Assert.Contains("The yard office is shut on Sundays", Text(No(g.Execute(new Order("bunk cushions")))));
        Assert.Contains("The yard office is shut on Sundays", Text(No(g.Execute(new OrderSurvey()))));
        Assert.Contains("The yard office is shut on Sundays", Text(No(g.Execute(new Launch()))));
        var sat = Shipped(75);
        Assert.Contains("The yard office opens at nine.", Text(No(sat.Execute(new OrderSurvey()))));
        UntilAtLeast(sat, "09:00");
        Ok(sat.Execute(new OrderSurvey()));
    }

    [Fact]
    public void The_fuel_pump_is_card_operated_and_works_on_a_Sunday()
    {
        var g = Shipped(76);
        var r = g.Execute(new FillTank());
        Assert.DoesNotContain("opens", Text(r));
        Assert.DoesNotContain("shut", Text(r));
    }

    // ------------------------------------------------------------------ the harbourmaster

    [Fact]
    public void The_harbourmaster_does_not_ring_in_the_night_a_wait_that_would_land_there_ends_at_her_first_call()
    {
        // The night moved to 21:00 puts the friend's 22:00 beat inside it: the wait that is cut there goes on to eight.
        var node = JsonNode.Parse(Sloop28Json())!;
        node["calendar"]!["events"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "friend-week")!["requires"] = new JsonArray();
        var g = Afloat(boat: BoatLoader.Load(node.ToJsonString(), true, Eco("21:00"), withHours: true));
        Ok(g.Execute(new InviteFriend(190)));
        var trail = new List<string>();
        void Mark(string what) => trail.Add($"{what}: day {Status(g).Day} {Status(g).TimeOfDay}");
        Ok(g.Execute(new WaitDays(30))); Mark("to the first morning");
        Ok(g.Execute(new WaitDays(1))); Mark("the arrival");
        Ok(g.Execute(new WaitDays(30))); Mark("to the last day");
        Ok(g.Execute(new WaitDays(1))); Mark("the last night");
        Assert.True(Status(g).TimeOfDay == "08:00", string.Join(" | ", trail));
        Assert.Contains(Log(g).Entries, e => e.Text.Contains("The last night aboard", StringComparison.Ordinal) && e.TimeOfDay == "22:00");
    }
}
