using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The season page, reputation and keeping her as the pull (design note 010, step 4a).</summary>
public class SeasonPageTests
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

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static CalendarView Cal(Game g) => (CalendarView)g.Ask(new Calendar());
    private static SeasonView Season(Game g) => (SeasonView)g.Ask(new Season());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static SeasonPageView Page(Game g, int? season = null) => (SeasonPageView)g.Ask(new SeasonPage(season));
    private static TripPlanView Plan(Game g, string? to = null) => (TripPlanView)g.Ask(new PlanTrip(to));

    private static WeatherSeries Calm() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));
    private static WeatherSeries GalesOn(params int[] days) =>
        WeatherSeries.From(d => new WeatherDay(0, days.Contains(d) ? 8 : 3, "southwest", 0, 12, 8));

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

    private static Boat WithReputation(params (string Key, JsonNode Value)[] changes) =>
        Edited(n => { foreach (var (k, v) in changes) n["reputation"]![k] = v; });

    /// <summary>Afloat at Skjærvik, the engine running, in the cockpit (terminal cleaned, seacock replaced).</summary>
    private static Game Afloat(int startDay, WeatherSeries? weather = null, Boat? boat = null, bool lightsFixed = false)
    {
        boat ??= BoatLoader.LoadSloop28();
        boat.Get("anchor-gear").Replace(); // she starts with a seized anchor chain
        var g = new Game(boat, startDay, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Calm());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c))); // so the old hose does not let the sea in over a summer
        if (lightsFixed) Ok(Walk.Do(g, "navigation lights", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    private static void WaitTo(Game g, int day)
    {
        while (Status(g).Day < day) Ok(g.Execute(new WaitDays(Math.Min(30, day - Status(g).Day))));
    }

    private static CommandResult MotorToArrival(Game g)
    {
        var r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 40 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        Assert.IsType<TripResultView>(r.Observation);
        return r;
    }

    private static CommandResult Sail(Game g, string to)
    {
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart(to)));
        return MotorToArrival(g);
    }

    private static void Tow(Game g, string to = "Hestholmen")
    {
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart(to)));
        Ok(g.Execute(new CallTow()));
    }

    /// <summary>
    /// Season one as a script: launched in April, the regatta party at Hestholmen on Saturday day 152 (there and back, a night away),
    /// then the summer waited out, a slot booked, and the lift on day 262. Returns the game after the lift and the lift's result.
    /// </summary>
    private static (Game G, CommandResult Lift, Boat Boat) ScriptedSeason(Boat? boat = null)
    {
        boat ??= BoatLoader.LoadSloop28();
        var g = Afloat(77, boat: boat);
        WaitTo(g, 152);
        Assert.Equal(5, Status(g).Day % 7); // a Saturday
        Sail(g, "Hestholmen");
        Ok(g.Execute(new WaitDays(1)));
        Sail(g, "Skjærvik");
        Ok(g.Execute(new BookHaulout(262)));
        WaitTo(g, 262);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        var lift = Ok(g.Execute(new HaulOut()));
        return (g, lift, boat);
    }

    // ------------------------------------------------------------------ the page

    [Fact]
    public void The_autumn_lift_writes_the_page_with_every_number_of_the_season()
    {
        var (g, lift, _) = ScriptedSeason();
        var page = Page(g);
        var cal = Cal(g);

        Assert.Equal(1, page.Season);
        Assert.Equal((77, 262), (page.FromDay, page.ToDay));
        Assert.Equal(["Hestholmen"], page.PlacesReached);
        Assert.Equal(1, page.NightsAway); // the Saturday night at Hestholmen; the passages were by day
        Assert.Equal(["the regatta party at Hestholmen"], page.EventsMade);
        Assert.Equal(["Midsummer at Storholmen", "a friend's week aboard (you never asked)", "the club cruise to Kvitnes"], page.EventsMissed);
        Assert.Equal((int)Math.Round(Status(g).MinutesSpent / 60.0, MidpointRounding.AwayFromZero), page.HoursWorked);
        Assert.Equal(cal.VacationDaysUsed, page.VacationDaysUsed);
        Assert.Equal(Status(g).MoneySpentNok, page.SpentNok);          // everything that went out in season one, the lift included
        Assert.Equal(cal.Money.IncomeNok, page.IncomeNok);
        Assert.Equal(((SaleView)g.Ask(new Sale())).Letter!.ValueNok, page.ValueNok);
        Assert.Equal("The yard knows you pay on time.", page.Reputation); // two berth bills in time and the slot kept: two points

        // Two trips of 14 nm each, by the log (the steps end on the arrival).
        double route = 14;
        Assert.InRange(page.MilesSailed, 2 * route - 0.01, 2 * route + 0.01);

        Assert.Equal("Season 1, day 77 to day 262.", page.Lines[0]);
        Assert.Equal("28 miles sailed; one night away from Skjærvik; reached Hestholmen.", page.Lines[1]);
        Assert.Equal("Made: the regatta party at Hestholmen. Missed: Midsummer at Storholmen; a friend's week aboard (you never asked); the club cruise to Kvitnes.", page.Lines[2]);
        Assert.StartsWith($"{page.HoursWorked} hours of your own work, ", page.Lines[3]);
        Assert.Equal(
            $"She cost you {page.SpentNok} NOK this season; paydays put {page.IncomeNok} NOK into her account. The broker would put her at {page.ValueNok} NOK.",
            page.Lines[4]);

        // The lift carries it: in the narrative, in the card, and as a document in the logbook.
        foreach (var line in page.Lines) Assert.Contains(line, lift.Narrative);
        var card = Assert.IsType<HauloutView>(lift.Observation);
        Assert.Equal(page.Lines, card.Lines.Skip(card.Lines.Count - page.Lines.Count).ToList());
        var entry = Assert.Single(Log(g).Entries, e => e.Document == "season 1");
        Assert.Equal("The page of season 1 is written: day 77 to day 262.", entry.Text);
    }

    [Fact]
    public void The_page_is_written_once_and_the_query_finds_it_by_season_and_says_so_before_there_is_one()
    {
        var g = Afloat(77);
        var none = Page(g);
        Assert.Equal(0, none.Season);
        Assert.Equal(["No season page yet: it is written at the autumn haulout."], none.Lines);
        Assert.Equal(["There is no page for season 1."], Page(g, 1).Lines);

        var (done, _, _) = ScriptedSeason();
        Assert.Equal(Page(done, 1), Page(done));
        Assert.Equal(["There is no page for season 2."], Page(done, 2).Lines);
        // Nothing writes a second page for the same season.
        Ok(done.Execute(new WaitDays(3)));
        Assert.Single(Log(done).Entries, e => e.Document is not null);
        Assert.Equal(1, Page(done).Season);
    }

    [Fact]
    public void An_emergency_lift_on_or_after_the_autumn_day_writes_it_too_and_one_before_does_not()
    {
        var early = Afloat(150);
        var before = Ok(early.Execute(new EmergencyLift()));
        Assert.DoesNotContain(before.Narrative, l => l.StartsWith("Season 1", StringComparison.Ordinal));
        Assert.Equal(0, Page(early).Season);

        var g = Afloat(77);
        WaitTo(g, 245);
        Walk.ToPlace(g, "cockpit");
        var r = Ok(g.Execute(new EmergencyLift()));
        Assert.Contains("Season 1, day 77 to day 245.", r.Narrative);
        var card = Assert.IsType<HauloutView>(r.Observation);
        Assert.Contains("Season 1, day 77 to day 245.", card.Lines);
        Assert.Equal(245, Page(g).ToDay);
        Assert.Single(Log(g).Entries, e => e.Document == "season 1");
    }

    [Fact]
    public void Nights_away_count_the_midnights_at_another_port_or_at_sea_and_never_at_home_or_on_the_hard()
    {
        var g = Afloat(80);
        Ok(g.Execute(new WaitDays(3)));                 // at home
        Sail(g, "Hestholmen");
        Ok(g.Execute(new WaitDays(3)));                 // three nights at Hestholmen
        // Out again and hove to for the night: a midnight at sea.
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart("Skjærvik")));
        Ok(g.Execute(new Motor()));
        Ok(g.Execute(new HeaveTo()));
        int day = Status(g).Day;
        for (int i = 0; i < 40 && Status(g).Day == day; i++) Ok(g.Execute(new Continue()));
        Assert.True(Status(g).AtSea);
        Ok(g.Execute(new Motor()));
        MotorToArrival(g);
        Ok(g.Execute(new WaitDays(2)));                 // home again

        WaitTo(g, 255);
        Ok(g.Execute(new BookHaulout(262)));
        WaitTo(g, 262);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HaulOut()));
        Assert.Equal(4, Page(g).NightsAway);
        Ok(g.Execute(new WaitDays(5)));                 // on the hard
        Assert.Equal(4, Page(g).NightsAway);
    }

    [Fact]
    public void A_season_with_no_trips_and_no_calendar_says_so_plainly()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(250, boat: boat);
        Ok(g.Execute(new BookHaulout(255)));
        WaitTo(g, 255);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HaulOut()));
        var page = Page(g);
        Assert.Equal("Season 1, day 250 to day 255.", page.Lines[0]);
        Assert.Equal("No miles sailed; no nights away from Skjærvik.", page.Lines[1]);
        Assert.Equal($"{page.HoursWorked} {(page.HoursWorked == 1 ? "hour" : "hours")} of your own work.", page.Lines[2]);
        Assert.Equal("", page.Reputation);
        Assert.DoesNotContain(page.Lines, l => l.StartsWith("Made", StringComparison.Ordinal) || l.StartsWith("Missed", StringComparison.Ordinal));
        Assert.DoesNotContain("paydays", page.Lines[3]);
    }

    // ------------------------------------------------------------------ reputation

    [Fact]
    public void A_new_game_stands_at_nothing_and_says_the_yard_knows_the_boat_not_yet_you()
    {
        var g = Afloat(77);
        Assert.Equal(0, g.ReputationPoints);
        Assert.Equal("The yard knows your boat, not yet you.", Season(g).Reputation);
        Assert.Equal("The yard knows your boat, not yet you.", Cal(g).Reputation);
    }

    [Fact]
    public void The_words_of_each_band_are_the_data_and_the_number_is_never_in_a_view()
    {
        var words = BoatLoader.LoadSloop28().Reputation.Words;
        string[] expected =
        [
            "The yard has started asking for payment before the work.",       // -5
            "The yard has started asking for payment before the work.",       // -4
            "The yard has started asking for payment before the work.",       // -3
            "The yard is polite with you, and a little careful.",             // -2
            "The yard is polite with you, and a little careful.",             // -1
            "The yard knows your boat, not yet you.",                         // 0
            "The yard knows you pay on time.",                                // 1
            "The yard knows you pay on time.",                                // 2
            "The yard keeps an eye on her for you, and the towboat skipper waves.", // 3
            "The yard keeps an eye on her for you, and the towboat skipper waves.", // 4
            "The yard keeps an eye on her for you, and the towboat skipper waves.", // 5
        ];
        for (int score = -5; score <= 5; score++) Assert.Equal(expected[score + 5], Band.Pick(words, score));
        Assert.Empty(BoatLoader.LoadSloop28(withCalendar: false).Reputation.Words);
    }

    [Fact]
    public void A_tow_costs_a_point_and_the_second_tow_costs_more_because_the_skipper_has_met_you()
    {
        var g = Afloat(80);
        Tow(g);
        Assert.Equal(-1, g.ReputationPoints);
        Assert.Equal("The yard is polite with you, and a little careful.", Season(g).Reputation);
        Assert.Equal(6300, Plan(g, "Hestholmen").TowNok);       // 6000 and 5 % more for the point
        Tow(g, "Hestholmen");
        var bills = Cal(g).Money.Recent.Where(e => e.Text == "The towboat.").Select(e => -e.Nok).ToList();
        Assert.Equal([6300, 6000], bills);
        Assert.Equal(-2, g.ReputationPoints);
    }

    [Fact]
    public void A_good_name_makes_the_tow_cheaper_and_the_price_is_rounded_to_a_hundred()
    {
        var boat = WithReputation(("onTimeBillsPerPoint", 1), ("towFactorPerPoint", 0.07));
        var g = Afloat(77, boat: boat);
        WaitTo(g, 181); // the berth bills of days 90 and 180, both paid in time: two points
        Assert.Equal(2, g.ReputationPoints);
        Assert.Equal(5200, Plan(g, "Hestholmen").TowNok); // 6000 * 0.86 = 5160 -> 5200
        Tow(g);
        Assert.Contains(Cal(g).Money.Recent, e => e.Text == "The towboat." && e.Nok == -5200);
    }

    [Fact]
    public void Standing_bills_paid_in_time_earn_a_point_for_every_two()
    {
        var g = Afloat(77);
        WaitTo(g, 91);
        Assert.Equal(0, g.ReputationPoints);   // the berth of day 90: one bill
        WaitTo(g, 181);
        Assert.Equal(1, g.ReputationPoints);   // and the one of day 180
        Assert.Equal("The yard knows you pay on time.", Season(g).Reputation);
    }

    [Fact]
    public void A_bill_that_took_her_past_the_banks_limit_costs_a_point_and_is_not_a_bill_paid_in_time()
    {
        // A berth bill of 60000 NOK takes the balance past the bank's 20000 on day 90 and again on day 180.
        var boat = Edited(n => n["calendar"]!["bills"]![0]!["nok"] = 60000);
        var g = Afloat(77, boat: boat);
        WaitTo(g, 91);
        Assert.True(Status(g).BalanceNok < -20000);
        Assert.Equal(-1, g.ReputationPoints);
        WaitTo(g, 181);
        Assert.Equal(-2, g.ReputationPoints);  // the next berth bill too; neither counts as paid in time
    }

    [Fact]
    public void A_haulout_on_the_booked_day_earns_a_point_and_a_forfeited_slot_costs_one()
    {
        var kept = ScriptedSeason().G;
        // Two berth bills (+1) and the slot kept (+1).
        Assert.Equal(2, kept.ReputationPoints);

        var g = Afloat(250, boat: WithReputation(("onTimeBillsPerPoint", 0)));
        Ok(g.Execute(new BookHaulout(256)));
        var alerts = new List<Alert>();
        while (Status(g).Day < 258) alerts.AddRange(Ok(g.Execute(new WaitDays(1))).Alerts);
        Assert.Equal(-1, g.ReputationPoints);
        Assert.Contains(alerts, a => a.Text.StartsWith("You missed your haulout slot on day 256", StringComparison.Ordinal));
    }

    [Fact]
    public void A_parted_mooring_costs_two_points_and_a_patrol_fine_one()
    {
        var parted = Afloat(260, GalesOn(261), WithReputation(("onTimeBillsPerPoint", 0)));
        parted.MooringChafe = 0.95;
        Ok(parted.Execute(new WaitDays(2)));
        Assert.Equal(-2, parted.ReputationPoints);

        var dark = Afloat(100, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 1)), WithReputation(("onTimeBillsPerPoint", 0)));
        Walk.ToPlace(dark, "cockpit");
        Ok(dark.Execute(new Depart("Hestholmen")));
        var hour = Ok(dark.Execute(new Motor()));
        Assert.Contains(hour.Alerts, a => a.Text.StartsWith("A patrol boat's searchlight finds you", StringComparison.Ordinal));
        Assert.Equal(-1, dark.ReputationPoints);
    }

    [Fact]
    public void The_standing_is_clamped_to_the_data_range()
    {
        var g = Afloat(80, boat: WithReputation(("tow", -9)));
        Tow(g);
        Assert.Equal(-5, g.ReputationPoints);
        Assert.Equal("The yard has started asking for payment before the work.", Season(g).Reputation);
        Assert.Equal(7500, Plan(g, "Hestholmen").TowNok);  // 6000 + 25 %
    }

    [Fact]
    public void The_yards_first_free_slot_comes_a_day_nearer_for_a_good_name_and_later_for_a_bad_one()
    {
        // A good name: two berth bills in time at one point each, against the same game without the effect.
        int FreeAt(Boat boat, int day)
        {
            var g = Afloat(77, boat: boat);
            WaitTo(g, day);
            return Season(g).FirstFreeDay!.Value;
        }
        var good = WithReputation(("onTimeBillsPerPoint", 1));
        var plain = WithReputation(("onTimeBillsPerPoint", 1), ("slotDaysPerPoint", 0));
        int baseline = FreeAt(plain, 262);
        Assert.True(baseline > 263, "the yard's drift has started"); // seeded: the same every run
        Assert.Equal(baseline - 2, FreeAt(good, 262));

        // Never before the earliest day there is: the window's first day while the drift has not begun.
        Assert.Equal(255, FreeAt(good, 200));
        Assert.Equal(255, FreeAt(plain, 200));

        // A bad name: a missed slot pushes it later.
        int Missed(Boat boat)
        {
            var g = Afloat(77, boat: boat);
            WaitTo(g, 200);
            Ok(g.Execute(new BookHaulout(255)));
            WaitTo(g, 257);
            Assert.Null(Season(g).BookedDay);
            return Season(g).FirstFreeDay!.Value;
        }
        int slow = Missed(WithReputation(("onTimeBillsPerPoint", 0)));
        int same = Missed(WithReputation(("onTimeBillsPerPoint", 0), ("slotDaysPerPoint", 0)));
        Assert.Equal(same + 1, slow);
    }

    [Fact]
    public void At_a_standing_of_nothing_the_tow_and_the_slot_are_the_datas_own()
    {
        var g = Afloat(77);
        Assert.Equal(0, g.ReputationPoints);
        Assert.Equal(6000, Plan(g, "Hestholmen").TowNok);
        WaitTo(g, 200);
        Assert.Equal(255, Season(g).FirstFreeDay);
    }

    // ------------------------------------------------------------------ keeping her

    [Fact]
    public void Keep_tells_the_year_ahead_and_that_the_page_is_in_the_logbook()
    {
        var (g, _, _) = ScriptedSeason();
        var keep = Ok(g.Execute(new Keep()));
        Assert.Equal("You keep her. The yard has her down for launch on day 456.", keep.Narrative[0]);
        Assert.Equal(
            "Next season: the regatta party at Hestholmen on Saturday, day 516; Midsummer at Storholmen, days 532 to 533; "
            + "the club cruise to Kvitnes, days 579 to 580; your friend can come for a week in July. 25 vacation days from the new year.",
            keep.Narrative[1]);
        Assert.Equal("The page of season 1 is in the logbook.", keep.Narrative[2]);
        Assert.Equal(3, keep.Narrative.Count);
        Assert.Equal(1, Page(g).Season); // the page stays readable
    }

    [Fact]
    public void Keep_without_a_page_or_a_calendar_says_no_more_than_before()
    {
        var g = Afloat(150, boat: BoatLoader.LoadSloop28(withCalendar: false));
        var keep = Ok(g.Execute(new Keep()));
        Assert.Equal(["You keep her. The yard has her down for launch on day 456."], keep.Narrative);

        var early = Afloat(150);
        var narrative = Ok(early.Execute(new Keep())).Narrative;
        Assert.DoesNotContain(narrative, l => l.Contains("is in the logbook"));
        // Kept in the spring, the year ahead is this year's: only what is still to come.
        Assert.StartsWith("Next season: Midsummer at Storholmen, days 172 to 173;", narrative[1].Replace("the regatta party at Hestholmen on Saturday, day 152; ", ""));
    }

    [Fact]
    public void A_second_season_starts_its_page_from_the_keep_and_writes_its_own()
    {
        var (g, _, boat) = ScriptedSeason();
        int keptDay = Status(g).Day;
        Ok(g.Execute(new Keep()));
        // The winter on the hard, then launch in the spring of year two and a second autumn.
        WaitTo(g, 452);
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        WaitTo(g, 620);
        WaitTo(g, 630);
        Ok(g.Execute(new EmergencyLift())); // the engine has not run for a year: the yard's crane takes her out all the same

        var two = Page(g, 2);
        Assert.Equal(2, two.Season);
        Assert.Equal(keptDay, two.FromDay);
        Assert.Equal(630, two.ToDay);
        Assert.Equal(0, two.MilesSailed);       // nothing sailed since the keep
        Assert.Equal(0, two.NightsAway);
        Assert.Empty(two.PlacesReached);
        Assert.Equal(1, Page(g, 1).Season);
        Assert.Equal(two, Page(g));
        Assert.Equal(2, Log(g).Entries.Count(e => e.Document is not null));
    }

    [Fact]
    public void The_briefing_ends_with_the_season_that_runs_on_and_the_two_ways_it_can_end()
    {
        var lines = ((BriefingView)new Game(BoatLoader.LoadSloop28()).Ask(new Briefing())).Lines;
        Assert.Equal(
            "The season runs on past the launch: paydays and bills, a calendar of things worth sailing for, the autumn haulout and the winter; then sell her or keep her for another year.",
            lines[^1]);
    }

    // ------------------------------------------------------------------ the wire

    [Fact]
    public void The_season_page_query_is_in_the_catalogue_and_round_trips()
    {
        var verbs = ((CommandList)new Game(BoatLoader.LoadSloop28()).Ask(new Commands())).Commands.Select(c => c.Verb).ToList();
        Assert.Contains("season page", verbs);

        foreach (Query q in new Query[] { new SeasonPage(), new SeasonPage(2) })
        {
            var json = JsonSerializer.Serialize(q);
            Assert.Contains("\"type\":\"season-page\"", json);
            Assert.Equal(q, JsonSerializer.Deserialize<Query>(json));
        }
        var view = JsonSerializer.Serialize<QueryResult>(Page(ScriptedSeason().G));
        Assert.Contains("\"type\":\"season-page-view\"", view);
    }

    [Fact]
    public void The_season_page_and_the_new_fields_carry_no_ids_and_no_hidden_fault_names()
    {
        var (g, lift, boat) = ScriptedSeason();
        var seen = new List<string>
        {
            JsonSerializer.Serialize<QueryResult>(Page(g)),
            JsonSerializer.Serialize<QueryResult>(Season(g)),
            JsonSerializer.Serialize<QueryResult>(Cal(g)),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook())),
            JsonSerializer.Serialize(lift),
            JsonSerializer.Serialize(g.Execute(new Keep())),
        };
        var json = string.Concat(seen);
        foreach (var f in boat.AllFaults)
        {
            Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
            if (!f.Discovered) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
        }
        foreach (var id in boat.Components.Select(c => c.Id).Concat(boat.Upgrades.Select(u => u.Component.Id)).Where(id => id.Contains('-')))
            Assert.False(json.Contains(id, StringComparison.Ordinal), id);
        // And no number stands for the standing: only the sentence.
        Assert.DoesNotContain("ReputationPoints", json);
        Assert.DoesNotContain("reputationScore", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_calendar_counts_paydays_and_events_for_the_harness()
    {
        var (g, _, _) = ScriptedSeason();
        var cal = Cal(g);
        Assert.Equal(6, cal.PaydaysSeen);            // days 90, 120, 150, 180, 210, 240
        Assert.Equal(1, cal.EventsMade);
        Assert.True(cal.EventsMissed >= 2);          // midsummer and the club cruise
    }

    [Fact]
    public void The_reputation_data_is_flagged_as_a_placeholder_of_design_note_010()
    {
        var rules = BoatLoader.LoadSloop28().Reputation;
        Assert.Equal((2, 1, -1, -2, -1, -1, -1), (rules.OnTimeBillsPerPoint, rules.SlotKept, rules.Tow, rules.MooringParted, rules.SlotMissed, rules.BillPastLimit, rules.PatrolFine));
        Assert.Equal((-5, 5, 0.05, 1), (rules.Min, rules.Max, rules.TowFactorPerPoint, rules.SlotDaysPerPoint));
        Assert.Equal(5, rules.Words.Count);
        Assert.Contains("reputation {", Sloop28Json());
        var placeholders = JsonNode.Parse(Sloop28Json())!["placeholders"]!.AsArray().Select(p => p!.GetValue<string>());
        Assert.Contains(placeholders, p => p.Contains("reputation {") && p.Contains("(design note 010)"));
    }
}
