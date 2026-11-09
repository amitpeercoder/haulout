using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>Crewing places (design note 012, decision 5, step 4): the cards, taking a place, the first morning, the days away, the log, the letter on the mat.</summary>
public class CrewingTests
{
    private static CommandResult Ok(CommandResult r) => ClubGame.Ok(r);
    private static string Text(CommandResult r) => ClubGame.Text(r);
    private static StatusView Status(Game g) => ClubGame.Status(g);

    private static CommandResult No(CommandResult r)
    {
        Assert.False(r.Accepted, Text(r));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    private static CommandResult Take(Game g, string place) => g.Execute(new TakeCrewingPlace(place));

    /// <summary>A world of the given forces by day, with a day of twelve hours by default (dark before 07:00 and after 19:00).</summary>
    private static WeatherSeries Series(Func<int, int>? force = null, double daylight = 12) =>
        WeatherSeries.From(d => new WeatherDay(d, force?.Invoke(d) ?? 3, "southwest", 0, 12, 8, DaylightHours: daylight));

    private static SkipperLog Qualifying() => new() { MilesUnderWay = 100, NightsAway = 4 };

    private static CrewingCard Card(Game g, string title) => ClubGame.Board(g).Cards.OfType<CrewingCard>().Single(c => c.Title == title);

    private static string ReadBoardJson()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.board.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string EditedBoard(Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(ReadBoardJson())!;
        edit(node);
        return node.ToJsonString();
    }

    private static Game WithBoard(Action<JsonNode> edit, int startDay) =>
        new(BoatLoader.Load(ClubGame.Sloop28Json(), true, ClubGame.OpenShops(), withHours: true, boardJson: EditedBoard(edit)), startDay,
            new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());

    private static JsonObject Place(JsonNode board, string id) =>
        board["crewing"]!["places"]!.AsArray().Select(p => p!.AsObject()).Single(p => p["id"]!.GetValue<string>() == id);

    /// <summary>Takes the week north (the log qualifies) and waits to its first morning.</summary>
    private static Game ReadyForTheWeek(WeatherSeries? weather = null, int startDay = 100)
    {
        var g = ClubGame.New(startDay, weather ?? Series());
        g.SetSkipperLogForTests(Qualifying());
        Ok(Take(g, "north"));
        ClubGame.WaitTo(g, 231);
        return g;
    }

    private static readonly string[] AllIds = BoatLoader.LoadSloop28(withHours: true) is var data
        ? [.. data.Components.Select(c => c.Id).Concat(data.AllFaults.Select(f => f.Id)).Concat(data.Calendar.Events.Select(e => e.Id)).Concat(data.Board.Crewing.Places.Select(p => p.Id)).Distinct()]
        : [];

    // ------------------------------------------------------------------ the data

    [Fact]
    public void Crewing_loads_two_places_with_their_plans_and_marks_every_number_a_placeholder_with_its_sources()
    {
        var boat = BoatLoader.LoadSloop28(withHours: true);
        var crew = boat.Board.Crewing;
        Assert.True(crew.Enabled);
        Assert.Equal(["delivery", "north"], crew.Places.Select(p => p.Id).ToArray());
        Assert.All(crew.Places, p => Assert.Equal(p.Days, p.Plan.Count));
        Assert.Equal((2, "saturday", 7, "monday"), (crew.Places[0].Days, crew.Places[0].Weekday, crew.Places[1].Days, crew.Places[1].Weekday));
        Assert.Null(crew.Places[0].Asks);
        Assert.Equal(("coastal", 60, 3), (crew.Places[1].Asks!.Ticket, crew.Places[1].Asks!.Miles, crew.Places[1].Asks!.NightsAway));

        // The delivery's legs are the chart's own distance between the two ports, there and back.
        var ports = boat.Trips!.Ports;
        var h = ports.Single(p => p.Id == "hestholmen").Chart!;
        var s = ports.Single(p => p.Id == "storholmen").Chart!;
        double chart = Math.Sqrt(Math.Pow(h.EastNm - s.EastNm, 2) + Math.Pow(h.NorthNm - s.NorthNm, 2));
        Assert.All(crew.Places[0].Plan, l => Assert.InRange(l.Nm, chart - 0.1, chart + 0.1));
        Assert.Equal(["Hestholmen", "Storholmen"], [crew.Places[0].Plan[0].From, crew.Places[0].Plan[0].To]);

        // The week north: 200 to 260 miles, a night passage across midnight, passages over 20 and one over 60, far places that are not on the chart.
        var week = crew.Places[1].Plan;
        Assert.InRange(week.Sum(l => l.Nm), 200, 260);
        Assert.True(week.Count(l => l.Nm > 20) >= 2);
        Assert.Contains(week, l => l.Nm > 60);
        Assert.Contains(week, l => l.DepartHour + l.Hours > 24);
        var far = week.Where(l => l.ToOffChart).Select(l => l.To).Distinct().ToList();
        Assert.True(far.Count >= 3);
        Assert.All(far, name => Assert.DoesNotContain(ports, p => p.Name == name));
        Assert.True(crew.Places[1].NewPriceNok > 0);
        Assert.Contains("28-footer", crew.Places[1].CostLine);

        var raw = JsonNode.Parse(ReadBoardJson())!["placeholders"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        var crewing = string.Join(" ", raw.Where(p => p.Contains("crewing places", StringComparison.Ordinal)));
        Assert.Contains("PLACEHOLDER", crewing);
        Assert.Contains("UNVERIFIED", crewing);
        foreach (var source in new[] { "crewseekers.net", "crewbay.com", "boats.com", "Garcia Exploration 45", "design note 012" })
            Assert.Contains(source, crewing);
    }

    [Fact]
    public void A_crewing_file_that_is_wrong_is_refused_at_load()
    {
        var shortPlan = Assert.Throws<InvalidOperationException>(() => WithBoard(b => Place(b, "north")["days"] = 5, 100));
        Assert.Contains("legs", shortPlan.Message);
        var badTicket = Assert.Throws<InvalidOperationException>(() => WithBoard(b => Place(b, "north")["asks"]!["ticket"] = "nope", 100));
        Assert.Contains("nope", badTicket.Message);
        var badDay = Assert.Throws<InvalidOperationException>(() => WithBoard(b => Place(b, "delivery")["weekday"] = "someday", 100));
        Assert.Contains("weekday", badDay.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void The_dates_clash_with_no_event_and_no_course_in_any_year(int year)
    {
        var g = ClubGame.New(year * 360 + 75, Series());
        var ranges = new List<(string Name, int From, int To)>();
        foreach (var card in ClubGame.Board(g).Cards)
            ranges.Add(card switch
            {
                EventCard e => (e.Title, e.Event.FromDay, e.Event.ToDay),
                CourseCard c => (c.Title, c.Day, c.Day + c.Days - 1),
                CrewingCard c => (c.Title, c.Day, c.Day + c.Days - 1),
                _ => throw new InvalidOperationException(),
            });
        var places = ranges.Where(r => r.Name is "The weekend delivery" or "The week north").ToList();
        Assert.Equal(2, places.Count);
        // The friend's week is a pick of the player's in July (days 180 to 203, so up to day 209): the crewing places stay out of the whole window.
        int y0 = year * 360;
        ranges.Add(("friend window", y0 + 180, y0 + 209));
        foreach (var a in places)
            foreach (var b in ranges.Where(r => r.Name != a.Name))
                Assert.False(a.From <= b.To && b.From <= a.To, $"{a.Name} {a.From}-{a.To} runs into {b.Name} {b.From}-{b.To} in year {year}");
        // The week north runs Monday to Sunday after the club cruise and before the coastal course; the delivery is a Saturday and Sunday in June.
        var north = places.Single(r => r.Name == "The week north");
        Assert.Equal((0, 6), (new GameClock(north.From).Weekday, new GameClock(north.To).Weekday));
        Assert.Contains(ranges, r => r.Name == "The club cruise to Kvitnes" && r.To < north.From);
        Assert.Contains(ranges, r => r.Name == "The coastal skipper skipper course".Replace("skipper skipper", "skipper") && r.From > north.To);
        var delivery = places.Single(r => r.Name == "The weekend delivery");
        Assert.Equal((5, 6, 5), (new GameClock(delivery.From).Weekday, new GameClock(delivery.To).Weekday, new GameClock(delivery.From).Month));
    }

    // ------------------------------------------------------------------ taking a place

    [Fact]
    public void A_boat_written_in_a_test_has_no_crewing_places()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 75, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Assert.Equal("Nobody is asking for crew.", Text(No(Take(g, "delivery"))));
    }

    [Fact]
    public void Taking_the_delivery_is_a_phone_call_of_five_minutes_with_no_money_that_marks_the_days()
    {
        var g = ClubGame.New(160, Series());
        int balance = Status(g).BalanceNok;
        var r = Ok(Take(g, "delivery"));
        Assert.Equal((5, 0), (r.MinutesTaken, r.CostNok));
        Assert.Equal("You ring Per Aune and say you will come: Saturday and Sunday, days 166 to 167, on the cutter Teist. Food aboard, no pay.", r.Narrative[0]);
        Assert.Equal("A wait will stop on the first morning.", r.Narrative[^1]);
        Assert.DoesNotContain(r.Narrative, l => l.Contains("vacation", StringComparison.Ordinal));   // a weekend costs none
        Assert.Equal(balance, Status(g).BalanceNok);
        Assert.Equal("Took a crewing place on the cutter Teist: Saturday and Sunday, days 166 to 167.", ClubGame.Log(g).Entries.Last().Text);
        var card = Card(g, "The weekend delivery");
        Assert.Equal((166, true, "taken", 0), (card.Day, card.Taken, card.Status, card.VacationDays));
        Assert.Contains(ClubGame.Cal(g).Ahead, e => e is { Kind: "crewing", Day: 166 } && e.Text == "Crewing on the cutter Teist");
    }

    [Fact]
    public void The_call_is_taken_at_any_hour_the_owner_is_a_person_and_not_an_office()
    {
        // With the real hours the office is shut on a Sunday night; the owner takes the call all the same.
        var g = new Game(BoatLoader.LoadSloop28(withHours: true), 100, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        ClubGame.LookUntilAtLeast(g, "23:00");
        Ok(Take(g, "weekend"));
        Assert.StartsWith("The yard chandlery", Text(g.Execute(new Buy("hose clamps"))));   // the shop, by contrast, is shut
    }

    [Fact]
    public void The_places_resolve_by_words_and_an_unknown_one_is_refused_in_words()
    {
        foreach (var (words, title) in new[]
                 { ("delivery", "The weekend delivery"), ("weekend", "The weekend delivery"), ("the weekend delivery", "The weekend delivery"),
                   ("north", "The week north"), ("week", "The week north"), ("the week north", "The week north"), ("fjellrype", "The week north") })
        {
            var g = ClubGame.New(100, Series());
            g.SetSkipperLogForTests(Qualifying());
            Ok(Take(g, words));
            Assert.True(Card(g, title).Taken, words);
        }
        Assert.Equal("Nobody is asking for crew on 'galley'.", Text(No(Take(ClubGame.New(100), "galley"))));
    }

    [Fact]
    public void A_place_is_refused_at_sea_when_the_date_has_gone_when_it_is_too_close_and_when_it_is_taken_already()
    {
        var sea = ClubGame.Afloat(100);
        Ok(sea.Execute(new Depart("Storholmen")));
        Assert.Equal("You're at sea: ring from port.", Text(No(Take(sea, "delivery"))));

        var gone = ClubGame.New(170, Series());
        Assert.Equal("That place has gone this year.", Text(No(Take(gone, "delivery"))));
        Assert.DoesNotContain(ClubGame.Board(gone).Cards, c => c is CrewingCard { Title: "The weekend delivery" });

        var close = ClubGame.New(166, Series());
        Assert.Equal("Per Aune needs to know the day before: Saturday and Sunday, days 166 to 167 is too close.", Text(No(Take(close, "delivery"))));
        Assert.Equal("too close", Card(close, "The weekend delivery").Status);

        var dayBefore = ClubGame.New(165, Series());
        Ok(Take(dayBefore, "delivery"));
        Assert.Equal("You've said you will come already: Saturday and Sunday, days 166 to 167.", Text(No(Take(dayBefore, "delivery"))));

        // Once both of this year's have gone the board and the call are next year's.
        var next = ClubGame.New(300, Series());
        var cards = ClubGame.Board(next).Cards.OfType<CrewingCard>().ToList();
        Assert.Equal(2, cards.Count);
        Assert.All(cards, c => Assert.True(c.Day > 360));
        Ok(Take(next, "delivery"));
    }

    [Fact]
    public void The_week_north_asks_for_the_coastal_ticket_or_a_log_and_the_refusal_says_how_far_you_are()
    {
        var g = ClubGame.New(100, Series());
        g.SetSkipperLogForTests(new SkipperLog { MilesUnderWay = 40, NightsAway = 1 });
        var r = No(Take(g, "north"));
        Assert.Equal("Mari Heiane wants the coastal skipper ticket or a season's log of 60 miles and 3 nights away from home: you have 40 of 60 miles and 1 of 3 nights away.", Text(r));
        var card = Card(g, "The week north");
        Assert.Equal(["40 of 60 miles and 1 of 3 nights away"], card.Shortfall.ToArray());
        Assert.Equal("the coastal skipper ticket or a season's log of 60 miles and 3 nights away from home", card.Asks);
        Assert.Contains("a coastal skipper ticket or a season's log asked", card.Line);

        // Miles alone are not enough: the nights are asked too.
        g.SetSkipperLogForTests(new SkipperLog { MilesUnderWay = 400, NightsAway = 2 });
        Assert.Contains("you have 400 of 60 miles and 2 of 3 nights away", Text(No(Take(g, "north"))));

        g.SetSkipperLogForTests(new SkipperLog { MilesUnderWay = 60, NightsAway = 3 });
        var ok = Ok(Take(g, "north"));
        Assert.Contains("It will cost 5 vacation days by the usual rule.", ok.Narrative);
        Assert.Empty(Card(g, "The week north").Shortfall);

        // The ticket does as well as the log.
        var held = ClubGame.New(100, Series());
        held.GrantTicketForTests("coastal");
        Ok(Take(held, "north"));
    }

    [Fact]
    public void A_place_that_clashes_with_a_course_the_friends_week_or_the_other_place_is_refused_and_so_is_the_other_way_round()
    {
        // The week north moved onto the coastal course (days 245 to 249).
        var course = WithBoard(b => Place(b, "north")["fromDayOfYear"] = 245, 100);
        course.SetSkipperLogForTests(Qualifying());
        Ok(course.Execute(new Enrol("coastal")));
        Assert.Equal("That clashes with your coastal skipper course (Monday to Friday, days 245 to 249).", Text(No(Take(course, "north"))));
        var courseAfter = WithBoard(b => Place(b, "north")["fromDayOfYear"] = 245, 100);
        courseAfter.SetSkipperLogForTests(Qualifying());
        Ok(Take(courseAfter, "north"));
        Assert.Equal("That clashes with your place on the cutter Fjellrype (Monday to Sunday, days 245 to 251).", Text(No(courseAfter.Execute(new Enrol("coastal")))));

        // The delivery moved onto the friend's week.
        var friend = WithBoard(b => Place(b, "delivery")["fromDayOfYear"] = 187, 100);
        Ok(friend.Execute(new InviteFriend(187)));
        Assert.Equal("That clashes with your friend's week (Saturday to Friday, days 187 to 193).", Text(No(Take(friend, "delivery"))));
        var friendAfter = WithBoard(b => Place(b, "delivery")["fromDayOfYear"] = 187, 100);
        Ok(Take(friendAfter, "delivery"));
        Assert.Equal("That clashes with your place on the cutter Teist (Saturday and Sunday, days 187 to 188).", Text(No(friendAfter.Execute(new InviteFriend(187)))));

        // The week north moved onto the delivery.
        var both = WithBoard(b => Place(b, "north")["fromDayOfYear"] = 160, 100);
        both.SetSkipperLogForTests(Qualifying());
        Ok(Take(both, "delivery"));
        Assert.Equal("That clashes with your place on the cutter Teist (Saturday and Sunday, days 166 to 167).", Text(No(Take(both, "north"))));
    }

    // ------------------------------------------------------------------ the first morning

    [Fact]
    public void A_wait_stops_at_eight_on_the_first_morning_and_the_next_command_begins_the_days_in_its_place()
    {
        var g = ClubGame.New(100, Series());
        Ok(Take(g, "delivery"));
        ClubGame.WaitTo(g, 160);
        var wait = Ok(g.Execute(new WaitDays(10)));
        Assert.Equal((166, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Contains("You come back on day 166: today you crew on the cutter Teist; the owner's launch leaves the club pontoon at nine.", wait.Narrative);

        var go = Ok(g.Execute(new Look()));
        Assert.Equal("You leave that for later: the owner's launch leaves the club pontoon at nine.", go.Narrative[0]);
        Assert.DoesNotContain(go.Narrative, l => l.StartsWith("You're in the cockpit", StringComparison.Ordinal));   // the look was not carried out
        Assert.Equal((167, "18:00"), (Status(g).Day, Status(g).TimeOfDay));
    }

    [Fact]
    public void Next_weekend_and_a_wait_week_stop_there_too_afloat_and_on_the_hard()
    {
        foreach (var afloat in new[] { false, true })
        {
            // From Saturday day 159 a week's wait would end at the Saturday of the delivery: it stops at 08:00 there, and the next command runs the days.
            var g = afloat ? ClubGame.Afloat(159, Series()) : ClubGame.New(159, Series());
            Ok(Take(g, "delivery"));
            var r = Ok(g.Execute(new WaitWeek()));
            Assert.Equal((166, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
            var week = Ok(g.Execute(new WaitWeek()));
            Assert.Equal(167, Status(g).Day);   // the next week was not waited: the days away ran instead
            Assert.Equal("You leave that for later: the owner's launch leaves the club pontoon at nine.", week.Narrative[0]);
            Assert.NotNull(r);
        }
        var h = ClubGame.New(162, Series());
        Ok(Take(h, "delivery"));
        Ok(h.Execute(new NextWeekend()));
        Assert.Equal(166, Status(h).Day);   // the Saturday the wait is for is the Saturday of the delivery
    }

    [Fact]
    public void Phone_calls_and_marks_on_the_calendar_do_not_begin_the_days_a_wait_and_a_sleep_do()
    {
        var g = ClubGame.New(100, Series());
        Ok(Take(g, "delivery"));
        ClubGame.WaitTo(g, 166);
        Ok(g.Execute(new PlanEvent("midsummer")));
        Assert.Equal((166, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        var slept = Ok(g.Execute(new Sleep()));
        Assert.Equal("You leave that for later: the owner's launch leaves the club pontoon at nine.", slept.Narrative[0]);
        Assert.Equal(167, Status(g).Day);
    }

    [Fact]
    public void A_player_at_another_port_when_the_owner_casts_off_misses_the_place_and_nothing_else_happens()
    {
        var g = ClubGame.Afloat(163, Series());
        Ok(Take(g, "delivery"));
        ClubGame.Sail(g, "Hestholmen");
        ClubGame.WaitTo(g, 166);
        var before = g.SkipperLogForTests;
        var r = Ok(g.Execute(new Look()));
        Assert.Contains(r.Alerts, a => a.Text == "The cutter Teist sailed without you: you were not at Skjærvik by nine. Per Aune went without a word.");
        Assert.Equal(before, g.SkipperLogForTests);
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text == "Missed the crewing place on the cutter Teist: not at Skjærvik when she sailed.");
        Assert.Equal(166, Status(g).Day);   // no days were run: the player is where they were
        Assert.Equal("Hestholmen", Status(g).Port);
        Assert.Equal("That place has gone this year.", Text(No(Take(g, "delivery"))));
    }

    [Fact]
    public void A_place_never_begun_is_missed_at_the_midnight_even_if_only_calls_were_made()
    {
        var g = ClubGame.Afloat(163, Series());
        Ok(Take(g, "delivery"));
        ClubGame.Sail(g, "Hestholmen");
        ClubGame.WaitTo(g, 166);
        Ok(g.Execute(new PlanEvent("midsummer")));   // a mark on the calendar is not a command that begins the days
        Ok(g.Execute(new WaitDays(1)));
        Assert.Equal("That place has gone this year.", Text(No(Take(g, "delivery"))));
    }

    // ------------------------------------------------------------------ the days away

    [Fact]
    public void The_delivery_credits_the_skipper_log_as_crew_and_leaves_the_boats_log_and_miles_alone()
    {
        var g = ClubGame.Afloat(163, Series(daylight: 24));
        Ok(Take(g, "delivery"));
        ClubGame.WaitTo(g, 166);
        var start = g.SkipperLogForTests;
        int tracks = ((ChartView)g.Ask(new Chart())).EarlierTracks.Count;
        int logBefore = ClubGame.Log(g).Entries.Count;
        var r = Ok(g.Execute(new Look()));

        var log = g.SkipperLogForTests;
        Assert.InRange(log.MilesUnderWay - start.MilesUnderWay, 63.1, 63.3);
        Assert.Equal((2, 1, 0.0), (log.DaysAboard - start.DaysAboard, log.NightsAway - start.NightsAway, log.NightHoursUnderWay - start.NightHoursUnderWay));
        Assert.All(log.Passages, p => Assert.Equal(PassageRole.Crew, p.Role));
        Assert.Equal((2, 0, 0), (log.PassagesOver(20), log.PassagesOver(20, asSkipper: true), log.PassagesOver(60)));
        Assert.Contains("2 passages over 20 miles as crew.", ClubGame.Skipper(g).Lines);
        Assert.Contains("1 night away from home.", ClubGame.Skipper(g).Lines);

        // The boat's own log has the one line (and the far boat's card), her passages are not extended, and the days are narrated.
        var added = ClubGame.Log(g).Entries.Skip(logBefore).ToList();
        Assert.Contains(added, e => e.Text == "Away crewing on the cutter Teist: days 166 to 167.");
        Assert.DoesNotContain(added.Where(e => e.Document is null), e => e.Text.Contains("Storholmen", StringComparison.Ordinal) || e.Text.Contains("Under way", StringComparison.Ordinal));
        Assert.Equal(tracks, ((ChartView)g.Ask(new Chart())).EarlierTracks.Count);
        Assert.Contains("Saturday, day 166: day 1 of 2, Hestholmen to Storholmen, 31.6 nm.", r.Narrative);
        Assert.Contains("Under way at 10:30 for 6.5 hours.", r.Narrative);
        Assert.Contains("You come home with 63 miles, 1 night away and a force 3 at the worst in your log.", r.Narrative);
        Assert.Equal((167, "18:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Equal("done", Card(g, "The weekend delivery").Status);
        Assert.Null(Status(g).Tired);
    }

    [Fact]
    public void The_week_north_credits_the_plan_and_the_weather_dark_hours_nights_a_force_seven_and_the_crew_passages()
    {
        // A day of twelve hours: dark before 07:00 and after 19:00. Tuesday, day 232 blows force 7; the rest force 3.
        var g = ReadyForTheWeek(Series(d => d == 232 ? 7 : 3));
        int vacation = ClubGame.Cal(g).VacationDaysLeft;
        var r = Ok(g.Execute(new Look()));

        var log = g.SkipperLogForTests;
        Assert.InRange(log.MilesUnderWay, 100 + 240 - 0.01, 100 + 240 + 0.01);
        Assert.Equal(4 + 6, log.NightsAway);
        Assert.Equal(7, log.DaysAboard);
        Assert.Equal(7, log.StrongestForce);
        // Dark hours by the table: Tuesday 06:30 to 07:00 (half an hour), Thursday 18:00 to Friday 06:00 (19:00 to 24:00 and 00:00 to 06:00).
        Assert.InRange(log.NightHoursUnderWay, 11.5 - 0.01, 11.5 + 0.01);
        Assert.Equal((6, 1, 1), (log.PassagesOver(20), log.PassagesOver(60), log.PassagesOverThroughANight(20)));
        Assert.All(log.Passages, p => Assert.Equal(PassageRole.Crew, p.Role));
        Assert.Equal(0, log.PassagesOver(20, asSkipper: true));
        Assert.Contains("6 passages over 20 miles as crew, 1 through a night.", ClubGame.Skipper(g).Lines);
        Assert.Contains("Strongest wind under way: force 7.", ClubGame.Skipper(g).Lines);

        // Five weekdays at sea cost five vacation days, once each and with the existing line.
        Assert.Equal(vacation - 5, ClubGame.Cal(g).VacationDaysLeft);
        Assert.Equal(5, r.Narrative.Count(l => l.StartsWith("A weekday at sea: today, ", StringComparison.Ordinal)));
        // Seven day heads; the last line of the last day is what she cost; the summary closes the stretch.
        Assert.Equal(7, r.Narrative.Count(l => Regex.IsMatch(l, @"^\w+day, day \d+: day \d of 7, ")));
        Assert.Contains("Monday, day 231: day 1 of 7, Skjærvik to Brandholmen (not on your chart), 34 nm.", r.Narrative);
        Assert.Contains("Saturday, day 236: day 6 of 7, at Brandholmen.", r.Narrative);
        var lines = r.Narrative.ToList();
        int cost = lines.FindIndex(l => l.StartsWith("She cost Mari Heiane ", StringComparison.Ordinal));
        Assert.True(cost > lines.FindIndex(l => l.StartsWith("The last leg home", StringComparison.Ordinal)));
        Assert.Contains("when she was new; you could buy a 28-footer like yours for the price of her sails.", lines[cost]);
        Assert.Contains("You come home with 240 miles, 6 nights away, 11 hours under way after dark and a force 7 at the worst in your log.", lines);
        Assert.Equal((237, "20:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Equal(4, lines.Count(l => l.Contains("(not on your chart)", StringComparison.Ordinal)));   // each far place is flagged once, the first time it is named
    }

    [Fact]
    public void The_far_boats_card_is_a_logbook_document_with_her_price_and_can_be_read_by_her_name()
    {
        var g = ReadyForTheWeek();
        Assert.Contains("no document called", ((DocumentView)g.Ask(new ReadDocument("fjellrype"))).Note);
        Ok(g.Execute(new Look()));
        foreach (var name in new[] { "fjellrype", "cutter", "the cutter Fjellrype", "boat north" })
        {
            var doc = (DocumentView)g.Ask(new ReadDocument(name));
            Assert.Equal("The cutter Fjellrype", doc.Title);
            var text = doc.Pages.Single().Text;
            Assert.Contains("45 ft aluminium cutter", text);
            Assert.Contains("Mari Heiane", text);
            Assert.Contains("She cost Mari Heiane 10500000 NOK when she was new", text);
        }
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Document == "boat north");
    }

    [Fact]
    public void The_coastal_checklist_moves_with_the_credited_miles_nights_hours_and_days_but_not_the_skipper_passages()
    {
        var g = ReadyForTheWeek(Series(d => d == 232 ? 7 : 3));
        g.SetSkipperLogForTests(new SkipperLog { MilesUnderWay = 60, NightsAway = 3, DaysAboard = 4 });
        Ok(g.Execute(new Look()));
        var coastal = ClubGame.Tickets(g).Tickets.Single(t => t.Name == "Coastal skipper");
        Assert.Equal("300 miles: you have 300.", coastal.Asks[0].Text);
        Assert.True(coastal.Asks[0].Met);
        Assert.Equal("15 days aboard: you have 11.", coastal.Asks[1].Text);
        Assert.Equal("8 hours under way after dark: you have 11.", coastal.Asks[2].Text);
        Assert.True(coastal.Asks[2].Met);
        Assert.Equal("2 passages over 20 miles as skipper: you have 0.", coastal.Asks[3].Text);   // crewing is miles in the log, not a passage as skipper
        Assert.False(coastal.Asks[3].Met);
    }

    [Fact]
    public void The_days_are_deterministic_and_the_weather_of_the_series_is_what_gives_the_force()
    {
        string Run()
        {
            var g = ReadyForTheWeek(Series(d => d == 232 ? 7 : 3));
            var r = Ok(g.Execute(new Look()));
            return string.Join("\n", r.Narrative) + JsonSerializer.Serialize(g.SkipperLogForTests) + string.Join("|", ClubGame.Log(g).Entries.Select(e => e.Text));
        }
        Assert.Equal(Run(), Run());

        var calm = ReadyForTheWeek(Series());
        Ok(calm.Execute(new Look()));
        Assert.Equal(3, calm.SkipperLogForTests.StrongestForce);
    }

    [Fact]
    public void A_weekday_costs_a_vacation_day_once_and_unpaid_leave_when_none_are_left()
    {
        var g = ReadyForTheWeek();
        Ok(g.Execute(new Look()));
        var vacation = ClubGame.Log(g).Entries.Where(e => e.Text.StartsWith("Vacation day: ", StringComparison.Ordinal)).Select(e => e.Day).ToList();
        Assert.Equal([231, 232, 233, 234, 235], vacation);

        // With only two left the same days are unpaid leave after them.
        var broke = ClubGame.New(100, Series(), ClubGame.OpenShops(n => n["calendar"]!["vacationDaysPerYear"] = 2));
        broke.SetSkipperLogForTests(Qualifying());
        Ok(Take(broke, "north"));
        ClubGame.WaitTo(broke, 231);
        var r = Ok(broke.Execute(new Look()));
        Assert.Equal(3, r.Narrative.Count(l => l.StartsWith("No vacation days left: today, ", StringComparison.Ordinal)));
        Assert.Equal(3, ClubGame.Cal(broke).UnpaidDays);
    }

    // ------------------------------------------------------------------ the letter on the mat

    private static WeatherSeries Gale() => Series(d => d is >= 231 and <= 237 ? 9 : 3);

    [Fact]
    public void Alerts_that_came_while_she_was_left_are_queued_and_come_back_as_one_letter_with_their_days_and_the_alerts_as_usual()
    {
        var g = ClubGame.Afloat(222, Gale());
        g.SetSkipperLogForTests(Qualifying());
        Ok(Take(g, "north"));
        ClubGame.WaitTo(g, 231);
        var r = Ok(g.Execute(new Look()));
        Assert.Equal((237, "20:00"), (Status(g).Day, Status(g).TimeOfDay));   // nothing stopped the days

        var letter = Assert.Single(ClubGame.Log(g).Entries, e => e.Document?.StartsWith("letter ", StringComparison.Ordinal) == true);
        Assert.StartsWith("Dear owner, while you were away crewing on the cutter Fjellrype I looked in on your boat.", letter.Text);
        Assert.Matches(@"Days 23\d to 23\d: the bow lines were sawing at the fairleads in the wind\. They will want looking at\.", letter.Text);
        Assert.Single(Regex.Matches(letter.Text, "sawing at the fairleads"));   // said once for the days it went on
        Assert.EndsWith("Nothing else to report. The harbourmaster's office.", letter.Text);
        Assert.Equal(0, letter.CostNok);
        Assert.Contains(r.Narrative, l => l.StartsWith("There is a letter from the harbourmaster on the mat of the club office (read letter ", StringComparison.Ordinal));
        Assert.NotEmpty(r.Alerts);   // told as alerts as usual
        var doc = (DocumentView)g.Ask(new ReadDocument("letter " + letter.Document!.Split(' ')[1]));
        Assert.Equal(letter.Text, doc.Pages.Single().Text);
        Assert.Contains("Days 231 to 234: the bow lines were sawing", letter.Text);   // from the first day to the day before the lines parted
        Assert.Contains("Day 235: her mooring parted in the night; the towboat brought her back and she is on new lines. The bill is in your logbook.", letter.Text);
    }

    [Fact]
    public void The_bilge_passing_the_harbourmasters_level_is_in_the_letter_in_his_words_and_a_bill_he_made_stays_as_it_was()
    {
        // A dead pump, rain and a bilge at the edge of the harbourmaster's level: she passes it on the first day and keeps rising a little (nowhere near sinking).
        var boat = ClubGame.Boat();
        var rain = WeatherSeries.From(d => new WeatherDay(d, 3, "southwest", d >= 231 ? 6 : 0, 12, 8, DaylightHours: 12));
        var g = new Game(boat, 222, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, rain);
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(g.Execute(new Launch()));
        boat.AllFaults.Single(f => f.Id == "bilge-pump-burnt-out").Wake(222);
        g.SetSkipperLogForTests(Qualifying());
        Ok(Take(g, "north"));
        ClubGame.WaitTo(g, 231);
        g.BilgeLitres = 119.9;
        Assert.False(Status(g).Sunk);
        Ok(g.Execute(new Look()));
        Assert.False(Status(g).Sunk);
        var letter = Assert.Single(ClubGame.Log(g).Entries, e => e.Document?.StartsWith("letter ", StringComparison.Ordinal) == true);
        Assert.Matches(@"Days? 23\d( to 23\d)?: she was sitting low on her mooring; the bilge wants looking at\.", letter.Text);
        Assert.Equal((237, "20:00"), (Status(g).Day, Status(g).TimeOfDay));   // the call did not stop the days

        // A mooring that parts in the blow is billed as it always was; the letter itself costs nothing.
        var blow = ClubGame.Afloat(222, Gale());
        blow.SetSkipperLogForTests(Qualifying());
        Ok(Take(blow, "north"));
        ClubGame.WaitTo(blow, 231);
        Ok(blow.Execute(new Look()));
        foreach (var l in ClubGame.Log(blow).Entries.Where(e => e.Document?.StartsWith("letter ", StringComparison.Ordinal) == true)) Assert.Equal(0, l.CostNok);
    }

    [Fact]
    public void A_quiet_week_leaves_no_letter()
    {
        var g = ClubGame.Afloat(222, Series());
        g.SetSkipperLogForTests(Qualifying());
        Ok(Take(g, "north"));
        ClubGame.WaitTo(g, 231);
        var r = Ok(g.Execute(new Look()));
        Assert.DoesNotContain(ClubGame.Log(g).Entries, e => e.Document?.StartsWith("letter ", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(r.Narrative, l => l.Contains("letter", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ views and words

    [Fact]
    public void The_board_shows_the_crewing_cards_dated_and_sorted_with_their_status_and_nothing_with_an_id()
    {
        var g = ClubGame.New(100, Series());
        var board = ClubGame.Board(g);
        var cards = board.Cards.OfType<CrewingCard>().ToList();
        Assert.Equal([166, 231], cards.Select(c => c.Day).ToArray());
        Assert.Equal("Crew wanted: a 36 ft cutter from Hestholmen to Storholmen and back, Saturday and Sunday, days 166 to 167; food aboard, no pay.", cards[0].Line);
        Assert.Equal((2, 0, "open", ""), (cards[0].Days, cards[0].VacationDays, cards[0].Status, cards[0].Asks));
        Assert.Equal((7, 5), (cards[1].Days, cards[1].VacationDays));
        Assert.Equal(board.Cards.Select(c => c.Day).Order().ToArray(), board.Cards.Select(c => c.Day).ToArray());
        // Only the club's board carries them.
        var elsewhere = ClubGame.Afloat(100);
        ClubGame.Sail(elsewhere, "Hestholmen");
        Assert.DoesNotContain(ClubGame.Board(elsewhere).Cards, c => c is CrewingCard);

        var json = JsonSerializer.Serialize<QueryResult>(board);
        foreach (var id in AllIds) Assert.DoesNotContain($"\"{id}\"", json);
        Assert.Contains("\"type\":\"crewing\"", json);
    }

    [Fact]
    public void No_new_view_or_result_of_the_days_carries_an_id()
    {
        var g = ClubGame.Afloat(222, Gale());
        g.SetSkipperLogForTests(Qualifying());
        var all = new List<string> { JsonSerializer.Serialize(Take(g, "north")) };
        ClubGame.WaitTo(g, 231);
        all.Add(JsonSerializer.Serialize(g.Execute(new Look())));
        all.Add(JsonSerializer.Serialize<QueryResult>(ClubGame.Board(g)));
        all.Add(JsonSerializer.Serialize<QueryResult>(ClubGame.Skipper(g)));
        all.Add(JsonSerializer.Serialize<QueryResult>(ClubGame.Cal(g)));
        all.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new ReadDocument("fjellrype"))));
        all.Add(JsonSerializer.Serialize<QueryResult>(ClubGame.Log(g)));
        foreach (var json in all)
            foreach (var id in AllIds)
                Assert.DoesNotContain($"\"{id}\"", json);
    }

    [Fact]
    public void Skipper_words_tell_crew_passages_apart_from_those_made_as_skipper()
    {
        var g = ClubGame.New(100, Series());
        g.SetSkipperLogForTests(new SkipperLog().WithPassage(new LoggedPassage(26, 5, 0, false, PassageRole.Skipper))
            .WithPassage(new LoggedPassage(34, 6, 0, false, PassageRole.Crew)).WithPassage(new LoggedPassage(64, 12, 9, true, PassageRole.Crew)));
        var lines = ClubGame.Skipper(g).Lines;
        Assert.Contains("1 passage over 20 miles as skipper.", lines);
        Assert.Contains("2 passages over 20 miles as crew, 1 through a night.", lines);
        Assert.Contains("1 passage over 60 miles as crew, 1 through a night.", lines);
    }

    [Fact]
    public void The_console_words_and_the_wire_name_of_the_command()
    {
        Assert.Equal(new TakeCrewingPlace("north"), Assert.IsType<RunCommand>(WordParser.Parse("crew north")).Command);
        Assert.Equal(new TakeCrewingPlace("delivery"), Assert.IsType<RunCommand>(WordParser.Parse("crew on the delivery")).Command);
        Assert.Equal(new TakeCrewingPlace("week"), Assert.IsType<RunCommand>(WordParser.Parse("take crewing place week")).Command);
        Assert.Equal("Usage: crew <delivery | north>", Assert.IsType<Problem>(WordParser.Parse("crew")).Message);
        var json = JsonSerializer.Serialize<Command>(new TakeCrewingPlace("north"));
        Assert.Contains("\"type\":\"take-crewing-place\"", json);
        Assert.Equal(new TakeCrewingPlace("north"), JsonSerializer.Deserialize<Command>(json));
    }
}
