using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>A game in the shipped world (hours, tickets and the board on) for the club's tests: shops open all day unless the test says otherwise.</summary>
internal static class ClubGame
{
    public sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    public static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    public static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    public static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    public static SkipperView Skipper(Game g) => (SkipperView)g.Ask(new Skipper());
    public static TicketsView Tickets(Game g) => (TicketsView)g.Ask(new Tickets());
    public static NoticeboardView Board(Game g) => (NoticeboardView)g.Ask(new Noticeboard());
    public static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    public static CalendarView Cal(Game g) => (CalendarView)g.Ask(new Calendar());
    public static SeasonPageView Page(Game g) => (SeasonPageView)g.Ask(new SeasonPage());

    public static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The shipped economy with the shops and the yard office open at every hour of every day (the hours themselves are tested elsewhere).</summary>
    public static string OpenShops(Action<JsonNode>? more = null) => TestEconomy.Edited(n =>
    {
        foreach (var day in new[] { "weekday", "saturday", "sunday" }) n["hours"]!["shop"]![day] = new JsonArray("00:00", "24:00");
        more?.Invoke(n);
    });

    public static WeatherSeries Calm() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));

    public static Boat Boat(string? economy = null) => BoatLoader.Load(Sloop28Json(), true, economy ?? OpenShops(), withHours: true);

    public static Game New(int startDay = 75, WeatherSeries? weather = null, string? economy = null) =>
        new(Boat(economy), startDay, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Calm());

    /// <summary>Afloat at Skjærvik, the engine running, in the cockpit (terminal cleaned, seacock and impeller replaced, the anchor chain sound).</summary>
    public static Game Afloat(int startDay, WeatherSeries? weather = null, string? economy = null, bool lightsFixed = true)
    {
        var boat = Boat(economy);
        boat.Get("anchor-gear").Replace();
        boat.Get("anchor-shackle").Replace();
        var g = new Game(boat, startDay, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Calm());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        if (lightsFixed) Ok(Walk.Do(g, "navigation lights", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    /// <summary>A letter that is not the right one for the question now open (a test that must fail on purpose).</summary>
    public static string WrongLetter(string right) => right == "a" ? "b" : "a";

    /// <summary>
    /// Sits the open exam: the first <paramref name="right"/> questions answered right, the rest wrong (the test hook knows the right letter). Returns the result of the
    /// last answer, which carries the marking.
    /// </summary>
    public static CommandResult SitExam(Game g, int right = 5)
    {
        CommandResult? last = null;
        for (int i = 0; ((ExamQuestionView)g.Ask(new ExamQuestion())).Open; i++)
        {
            string letter = g.ExamRightLetterForTests();
            last = Ok(g.Execute(new Answer(i < right ? letter : WrongLetter(letter))));
        }
        return last ?? throw new InvalidOperationException("No exam was open.");
    }

    public static void WaitTo(Game g, int day)
    {
        for (int i = 0; i < 60 && Status(g).Day < day; i++) Ok(g.Execute(new WaitDays(Math.Min(30, day - Status(g).Day))));
        Assert.Equal(day, Status(g).Day);
    }

    /// <summary>To 08:00 on a given day: a week's wait puts the clock at eight, then whole days keep it there.</summary>
    public static void ToMorning(Game g, int day)
    {
        if (Status(g).TimeOfDay != "08:00") Ok(g.Execute(new WaitWeek()));
        WaitTo(g, day);
    }

    public static CommandResult MotorToArrival(Game g)
    {
        var r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 60 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        Assert.IsType<TripResultView>(r.Observation);
        return r;
    }

    /// <summary>Casts off for a port and motors there. The engine is started first when it is not running.</summary>
    public static CommandResult Sail(Game g, string to)
    {
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart(to)));
        return MotorToArrival(g);
    }

    public static void LookUntilAtLeast(Game g, string time)
    {
        for (int i = 0; i < 700 && string.CompareOrdinal(Status(g).TimeOfDay, time) < 0; i++) Ok(g.Execute(new Look()));
        Assert.True(string.CompareOrdinal(Status(g).TimeOfDay, time) >= 0);
    }
}

/// <summary>The skipper's log (design note 012, decision 2, step 2): fed by the passage's end, the midnight, the tow and the autumn page; shown in words.</summary>
public class SkipperTests
{
    private static CommandResult Ok(CommandResult r) => ClubGame.Ok(r);
    private static Game Afloat(int day = 100, WeatherSeries? weather = null) => ClubGame.Afloat(day, weather);

    // ------------------------------------------------------------------ the record

    [Fact]
    public void The_log_is_a_plain_record_that_stands_by_itself_with_no_game_or_boat_in_it()
    {
        var log = new SkipperLog();
        Assert.Equal((0.0, 0, 0.0, 0, 0, 0, 0, false), (log.MilesUnderWay, log.NightsAway, log.NightHoursUnderWay, log.StrongestForce, log.DaysAboard, log.Tows, log.SeasonsWithoutTow, log.FirstAidHeld));
        Assert.Empty(log.Passages);
        foreach (var p in typeof(SkipperLog).GetProperties())
            Assert.DoesNotContain(p.PropertyType.FullName!, new[] { typeof(Game).FullName, typeof(Boat).FullName });
        Assert.True(typeof(SkipperLog).IsSealed);

        var more = log.WithPassage(new LoggedPassage(22, 5, 1, false, PassageRole.Skipper))
            .WithPassage(new LoggedPassage(70, 14, 6, true, PassageRole.Crew))
            .WithPassage(new LoggedPassage(61, 12, 2, true, PassageRole.Skipper));
        Assert.Equal((3, 1, 2), (more.PassagesOver(20), more.PassagesOver(60, true), more.PassagesOverThroughANight(60)));
        Assert.Equal(2, more.PassagesOver(20, true));
        Assert.Empty(log.Passages); // a record: the first is unchanged
        Assert.Equal(JsonSerializer.Serialize(more), JsonSerializer.Serialize(JsonSerializer.Deserialize<SkipperLog>(JsonSerializer.Serialize(more))));
    }

    // ------------------------------------------------------------------ passages

    [Fact]
    public void A_short_passage_logs_its_miles_but_is_not_a_passage_over_twenty_miles()
    {
        var g = Afloat();
        ClubGame.Sail(g, "Hestholmen");
        var log = g.SkipperLogForTests;
        Assert.InRange(log.MilesUnderWay, 13.99, 14.01);
        Assert.Equal(0, log.PassagesOver(20));
        Assert.Single(log.Passages);
        Assert.Equal("14 miles under way.", ClubGame.Skipper(g).Lines[0]);
        Assert.Contains("No passage over 20 miles yet.", ClubGame.Skipper(g).Lines);
    }

    [Fact]
    public void A_passage_to_Storholmen_is_a_passage_over_twenty_miles_as_skipper_and_says_so_in_words()
    {
        var g = Afloat();
        ClubGame.Sail(g, "Storholmen");
        var log = g.SkipperLogForTests;
        Assert.True(log.MilesUnderWay > 20);
        Assert.Equal((1, 1, 0), (log.PassagesOver(20), log.PassagesOver(20, asSkipper: true), log.PassagesOver(60)));
        Assert.Contains("1 passage over 20 miles as skipper.", ClubGame.Skipper(g).Lines);
        Assert.Contains("No passage over 60 miles yet.", ClubGame.Skipper(g).Lines);
    }

    [Fact]
    public void The_strongest_force_under_way_is_logged_and_a_blow_in_harbour_is_not()
    {
        var g = Afloat(100, WeatherSeries.From(d => new WeatherDay(0, d == 100 ? 3 : 1, "southwest", 0, 12, 8)));
        Assert.Equal(0, g.SkipperLogForTests.StrongestForce);
        Assert.Contains("No wind logged under way yet.", ClubGame.Skipper(g).Lines);
        ClubGame.Sail(g, "Hestholmen");
        Assert.Equal(3, g.SkipperLogForTests.StrongestForce);
        Assert.Contains("Strongest wind under way: force 3.", ClubGame.Skipper(g).Lines);
        // A harder day at the mooring changes nothing; a milder passage does not lower it.
        Ok(g.Execute(new WaitDays(2)));
        ClubGame.Sail(g, "Skjærvik");
        Assert.Equal(3, g.SkipperLogForTests.StrongestForce);
    }

    [Fact]
    public void Hours_under_way_after_dark_are_logged_and_a_passage_across_midnight_is_one_through_a_night()
    {
        var dark = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 0.5)); // dark but for a quarter hour round noon
        var g = Afloat(100, dark);
        ClubGame.LookUntilAtLeast(g, "21:00");
        ClubGame.Sail(g, "Storholmen");
        var log = g.SkipperLogForTests;
        Assert.InRange(log.NightHoursUnderWay, 3, log.Passages[0].Hours + 0.01);
        Assert.True(log.Passages[0].Overnight);          // it crossed the midnight
        Assert.Equal(1, log.PassagesOverThroughANight(20));
        Assert.Matches(@"^\d+ hours under way after dark\.$", ClubGame.Skipper(g).Lines[2]);
        Assert.Contains("1 passage over 20 miles as skipper, 1 through a night.", ClubGame.Skipper(g).Lines);
    }

    [Fact]
    public void A_passage_by_day_has_no_night_hours_and_the_words_say_no_night_passages_yet()
    {
        var g = Afloat();
        ClubGame.Sail(g, "Storholmen");
        Assert.Equal(0, g.SkipperLogForTests.NightHoursUnderWay);
        Assert.False(g.SkipperLogForTests.Passages[0].Overnight);
        Assert.Equal("No night passages yet.", ClubGame.Skipper(g).Lines[2]);
    }

    [Fact]
    public void A_turn_back_logs_its_miles_but_is_not_a_passage()
    {
        var g = Afloat();
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart("Storholmen")));
        Ok(g.Execute(new Motor()));
        Ok(g.Execute(new Continue()));
        Ok(g.Execute(new TurnBack()));
        var r = Ok(g.Execute(new Continue()));
        for (int i = 0; i < 30 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        var log = g.SkipperLogForTests;
        Assert.True(log.MilesUnderWay > 8);
        Assert.Empty(log.Passages);
        Assert.Equal(0, log.PassagesOver(0));
    }

    [Fact]
    public void A_tow_counts_its_miles_and_the_tow_but_is_not_a_passage_over_twenty_miles_as_skipper()
    {
        var g = Afloat();
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart("Storholmen")));
        Ok(g.Execute(new Motor()));
        Ok(g.Execute(new CallTow()));
        var log = g.SkipperLogForTests;
        Assert.Equal(1, log.Tows);
        Assert.True(log.MilesUnderWay > 8);      // the hour she made under her own engine, and the tow's miles back (it is the nearer port)
        Assert.Empty(log.Passages);
        Assert.Equal(0, log.PassagesOver(20, asSkipper: true));
        Assert.Contains("1 tow.", ClubGame.Skipper(g).Lines);
        Assert.Contains("No passage over 20 miles yet.", ClubGame.Skipper(g).Lines);
    }

    // ------------------------------------------------------------------ the midnight

    [Fact]
    public void A_day_aboard_is_a_day_the_player_worked_aboard_or_was_at_sea()
    {
        var g = Afloat(100);
        Assert.Equal(0, g.SkipperLogForTests.DaysAboard);
        Ok(g.Execute(new WaitDays(3)));                    // the work of the first day counts at its midnight; the three days away do not
        Assert.Equal(1, g.SkipperLogForTests.DaysAboard);
        Ok(g.Execute(new Look()));
        Ok(g.Execute(new WaitDays(2)));
        Assert.Equal(2, g.SkipperLogForTests.DaysAboard);
        Assert.Contains("2 days aboard.", ClubGame.Skipper(g).Lines);

        // A day at sea is a day aboard.
        var h = Afloat(100, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 0.5)));
        ClubGame.LookUntilAtLeast(h, "21:00");
        int before = h.SkipperLogForTests.DaysAboard;
        ClubGame.Sail(h, "Storholmen");
        Ok(h.Execute(new WaitDays(1)));
        Assert.True(h.SkipperLogForTests.DaysAboard >= before + 1);
    }

    [Fact]
    public void A_night_at_another_port_or_at_sea_is_a_night_away_and_one_at_home_is_not()
    {
        var g = Afloat(100);
        Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(0, g.SkipperLogForTests.NightsAway);
        Assert.Equal("No nights away from home yet.", ClubGame.Skipper(g).Lines[1]);
        ClubGame.Sail(g, "Hestholmen");
        Ok(g.Execute(new WaitDays(2)));
        Assert.Equal(2, g.SkipperLogForTests.NightsAway);
        Assert.Equal("2 nights away from home.", ClubGame.Skipper(g).Lines[1]);
    }

    // ------------------------------------------------------------------ the season

    private static Game LiftedAfterASeason(bool tow)
    {
        var g = Afloat(77);
        ClubGame.WaitTo(g, 152);
        if (tow)
        {
            // Out for Hestholmen and towed back to the mooring (the nearer port): no passage, one tow.
            if (!ClubGame.Status(g).EngineRunning) Ok(Walk.Start(g));
            Walk.ToPlace(g, "cockpit");
            Ok(g.Execute(new Depart("Hestholmen")));
            Ok(g.Execute(new Motor()));
            Ok(g.Execute(new CallTow()));
            Ok(Walk.Start(g));
            Walk.ToPlace(g, "cockpit");
        }
        else
        {
            ClubGame.Sail(g, "Hestholmen");
            ClubGame.Sail(g, "Skjærvik");
        }
        Ok(g.Execute(new BookHaulout(262)));
        ClubGame.WaitTo(g, 262);
        if (!ClubGame.Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HaulOut()));
        return g;
    }

    [Fact]
    public void A_season_with_miles_and_no_tow_is_counted_at_the_autumn_page_and_one_with_a_tow_is_not()
    {
        var clean = LiftedAfterASeason(tow: false);
        Assert.Equal(1, clean.SkipperLogForTests.SeasonsWithoutTow);
        Assert.Contains("1 season without a tow.", ClubGame.Skipper(clean).Lines);

        var towed = LiftedAfterASeason(tow: true);
        Assert.Equal(0, towed.SkipperLogForTests.SeasonsWithoutTow);
        Assert.Equal(1, towed.SkipperLogForTests.Tows);
        Assert.Contains("No season without a tow yet.", ClubGame.Skipper(towed).Lines);
    }

    [Fact]
    public void The_season_page_carries_a_short_skipper_block_in_the_same_words()
    {
        var g = LiftedAfterASeason(tow: false);
        var lines = ClubGame.Page(g).Lines;
        var skipper = ClubGame.Skipper(g).Lines;
        Assert.Contains("28 miles under way.", lines);   // the two passages to Hestholmen and back
        Assert.Contains(skipper[0], lines);
        Assert.Contains("Strongest wind under way: force 3.", lines);
        Assert.Contains("No night passages yet.", lines);
        Assert.Contains("No passage over 20 miles yet.", lines);
        // No score, no grade, no ids.
        Assert.DoesNotContain(lines, l => l.Contains("score", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ the view

    [Fact]
    public void The_skipper_view_is_words_only_with_no_ids_and_no_numbers_of_its_own()
    {
        var g = Afloat();
        ClubGame.Sail(g, "Storholmen");
        var view = ClubGame.Skipper(g);
        var json = JsonSerializer.Serialize<QueryResult>(view);
        Assert.Equal(["Lines", "type"], JsonNode.Parse(json)!.AsObject().Select(kv => kv.Key).OrderBy(k => k).ToArray());
        foreach (var id in new[] { "skjaervik", "storholmen", "nav-lights", "radio" }) Assert.DoesNotContain($"\"{id}\"", json);
        Assert.All(view.Lines, l => Assert.EndsWith(".", l));
        // A boat written without the tickets has a log all the same and a plain line.
        var plain = new Game(BoatLoader.LoadSloop28(withCalendar: false), 75, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Assert.Single(((SkipperView)plain.Ask(new Skipper())).Lines);
    }
}
