using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;
using Xunit.Abstractions;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// The second pass of review fixes to design note 008: the pump counter and the open hatch, the month in the engine's start text,
/// readiness after launch, the status list, the mooring chafe and the fouling calibration (measured over 20 seeds), the season line.
/// </summary>
public class SecondPassTests(ITestOutputHelper output)
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    // ------------------------------------------------------------------ builders and small helpers

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static SeasonView Season(Game g) => (SeasonView)g.Ask(new Season());
    private static ReadinessView Readiness(Game g) => (ReadinessView)g.Ask(new Readiness());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static SaleView Sale(Game g) => (SaleView)g.Ask(new Sale());

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Boat RichBoat() =>
        BoatLoader.Load(Sloop28Json().Replace("\"startingBalanceNok\": 25000", "\"startingBalanceNok\": 90000"));

    private static WeatherSeries Calm(double tempC = 12) =>
        WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, tempC, 8));

    private static void WaitTo(Game g, int day)
    {
        while (Status(g).Day < day && !Status(g).Sunk) Ok(g.Execute(new WaitDays(Math.Min(7, day - Status(g).Day))));
    }

    /// <summary>A job on the hard: tried again each day until the weather lets it be done (a gale, rain on the paint).</summary>
    private static void Fix(Game g, string name, Func<string, Command> make)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            try
            {
                if (Walk.Do(g, name, make).Accepted) return;
            }
            catch (Xunit.Sdk.XunitException) { }
            Ok(g.Execute(new WaitDays(1)));
        }
        throw new Xunit.Sdk.XunitException($"Could not do {name}.");
    }

    private static Game Prepared(int startDay, WeatherSeries weather, int seed = Game.DefaultSeed)
    {
        var g = new Game(RichBoat(), startDay, new FixedRandom(0.99), seed, weather);
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        return g;
    }

    private static void TryAgainTomorrow(Func<CommandResult> act, Game g)
    {
        for (int i = 0; i < 12; i++)
        {
            if (act().Accepted) return;
            Ok(g.Execute(new WaitDays(1)));
        }
        Assert.Fail("The wind never dropped.");
    }

    /// <summary>Afloat at the mooring, launched on <paramref name="launchDay"/>, the engine running.</summary>
    private static Game LaunchedOn(int launchDay, WeatherSeries weather, int seed = Game.DefaultSeed)
    {
        var g = Prepared(60, weather, seed);
        Ok(Walk.Start(g));
        WaitTo(g, launchDay);
        TryAgainTomorrow(() => g.Execute(new Launch()), g);
        return g;
    }

    /// <summary>Launched, lifted on the booked day and laid up on the hard, in the cockpit.</summary>
    private static Game LaidUp(WeatherSeries weather, int liftDay = 262, int launchDay = 252)
    {
        var g = Prepared(250, weather);
        Ok(g.Execute(new BookHaulout(liftDay)));
        WaitTo(g, launchDay);
        Ok(Walk.Start(g));
        TryAgainTomorrow(() => g.Execute(new Launch()), g);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        WaitTo(g, liftDay);
        Walk.ToPlace(g, "cockpit");
        TryAgainTomorrow(() =>
        {
            if (!Status(g).EngineRunning) Ok(Walk.Start(g));
            return g.Execute(new HaulOut());
        }, g);
        Assert.True(Season(g).LaidUp);
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    // ------------------------------------------------------------------ 1. the pump counter and the hatch

    private const string HatchAlert = "The bilge pump counter has gone up by";
    private const string HatchCause = "You left the companionway hatch open; rain has been finding its way below.";
    private const string FloorboardsAdvice = "Worth lifting the saloon floorboards to see where the water comes from.";
    private const string HatchOpenAtLeaving = "The companionway hatch is open: slide it shut before you go if you want her dry below.";

    /// <summary>Rain every day from the lift on; mild, so the only thing the winter does is wet the boat.</summary>
    private static WeatherSeries RainyWinter() =>
        WeatherSeries.From(d => d < 262 ? new WeatherDay(0, 3, "southwest", 0, 12, 8) : new WeatherDay(0, 3, "north", 8, 8, 7));

    [Fact]
    public void A_winter_of_rain_through_the_open_hatch_names_the_hatch_and_does_not_stop_the_wait()
    {
        var g = LaidUp(RainyWinter());
        Ok(g.Execute(new WinterCharger())); // so the pump has current all winter and the battery is not the story
        var r = Ok(g.Execute(new WaitUntilSpring()));

        Assert.Contains(HatchOpenAtLeaving, r.Narrative);
        var counter = Assert.Single(r.Alerts, a => a.Text.StartsWith(HatchAlert, StringComparison.Ordinal));
        Assert.EndsWith(HatchCause, counter.Text);
        Assert.DoesNotContain(FloorboardsAdvice, counter.Text);
        // It did not bring her back early: she waited out the winter to day 75 of the next year.
        Assert.Equal(360 + 75, Status(g).Day);
        Assert.Contains("It is spring.", r.Narrative);
        // The count is the whole winter's, read once, when she is back.
        Assert.Contains("The bilge pump counter has gone up by", counter.Text);
    }

    [Fact]
    public void With_the_hatch_shut_there_is_no_such_alert_and_no_note_on_leaving()
    {
        var g = LaidUp(RainyWinter());
        Ok(g.Execute(new WinterCharger()));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Refit("hatch")));
        var r = Ok(g.Execute(new WaitUntilSpring()));

        Assert.DoesNotContain(HatchOpenAtLeaving, r.Narrative);
        Assert.DoesNotContain(r.Alerts, a => a.Text.StartsWith(HatchAlert, StringComparison.Ordinal));
        Assert.Equal(360 + 75, Status(g).Day);
    }

    [Fact]
    public void Water_that_the_floorboards_explain_still_points_at_the_floorboards_and_still_brings_her_back()
    {
        // Water under the floorboards with the hatch shut and no rain is not the hatch: the old advice, and the wait stops on it.
        var g = LaidUp(Calm());
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Refit("hatch")));
        g.BilgeLitres = 40;
        var r = Ok(g.Execute(new WaitUntilSpring()));
        Assert.Contains(r.Alerts, a => a.Text.EndsWith(FloorboardsAdvice, StringComparison.Ordinal));
        Assert.DoesNotContain(r.Alerts, a => a.Text.EndsWith(HatchCause, StringComparison.Ordinal));
        Assert.True(Status(g).Day < 360 + 75, "it should have brought her back early");
        Assert.Contains("You come back before spring: something on her wants your attention.", r.Narrative);

        // The same on a day's wait afloat.
        var afloat = LaunchedOn(100, Calm());
        Walk.ToPlace(afloat, "cockpit");
        Ok(afloat.Execute(new Refit("hatch")));
        afloat.BilgeLitres = 40;
        var day = Ok(afloat.Execute(new WaitDays(2)));
        Assert.Contains(day.Alerts, a => a.Text.EndsWith(FloorboardsAdvice, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ 2. the month in the engine's start text

    [Theory]
    [InlineData(75, "March")]
    [InlineData(255, "September")]
    [InlineData(345, "December")]
    public void The_engine_start_text_takes_its_month_from_the_clock(int day, string month)
    {
        var g = new Game(RichBoat(), day, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        var r = Ok(Walk.Start(g));
        Assert.Contains(r.Narrative, l => l.Contains($"under the grey {month} light."));
        Assert.DoesNotContain(r.Narrative, l => month != "March" && l.Contains("March"));
    }

    // ------------------------------------------------------------------ 3. readiness after launch

    [Fact]
    public void Readiness_leads_with_the_season_once_she_is_launched()
    {
        // Before the first launch: today's deadline line, no season lead.
        var hard = new Game(RichBoat(), 75, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        Assert.Null(Readiness(hard).SeasonLead);

        // Launched, summer, nothing booked: the window and the first free slot.
        var g = LaunchedOn(100, Calm());
        Assert.Equal("No slot booked: the yard lifts from day 255 to day 300; the first free slot is day 255.", Readiness(g).SeasonLead);

        // Booked.
        Ok(g.Execute(new BookHaulout(258)));
        Assert.Equal("Haulout booked for day 258.", Readiness(g).SeasonLead);

        // Past the window, or when it is full.
        var late = LaunchedOn(100, Calm());
        WaitTo(late, 305);
        Assert.Equal("No slot booked: the yard has finished lifting for this year.", Readiness(late).SeasonLead);
    }

    [Fact]
    public void Readiness_in_the_winter_leads_with_the_task_list_and_after_a_keep_with_the_deadline()
    {
        var g = LaidUp(Calm());
        var lead = Readiness(g).SeasonLead!;
        Assert.StartsWith("Winter: 0 of 5 tasks done. Left: ", lead);
        Assert.Contains("Fit the winter cover", lead);

        Ok(g.Execute(new WinterCharger()));
        lead = Readiness(g).SeasonLead!;
        Assert.StartsWith("Winter: 1 of 5 tasks done. Left: ", lead);
        Assert.DoesNotContain("Battery ashore or on a charger", lead);

        Ok(g.Execute(new Keep()));
        Assert.Null(Readiness(g).SeasonLead); // the deadline is next: the views show it
        Assert.True(Readiness(g).LaunchDeadlineDay > Readiness(g).Day);
    }

    // ------------------------------------------------------------------ 4. the status list

    [Fact]
    public void Status_marks_a_fixed_problem_as_fixed_and_is_empty_once_she_is_sold()
    {
        var g = new Game(RichBoat(), 75, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        Ok(g.Execute(new Inspect("impeller")));
        var known = Assert.Single(Status(g).DiscoveredFaults);
        Assert.False(known.Fixed);

        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        known = Assert.Single(Status(g).DiscoveredFaults);
        Assert.True(known.Fixed);

        Ok(g.Execute(new ListForSale(100000)));
        for (int i = 0; i < 90 && !Sale(g).Viewings.Any(v => v.Open); i++) Ok(g.Execute(new WaitDays(1)));
        Ok(g.Execute(new AcceptOffer()));
        Assert.Empty(Status(g).DiscoveredFaults);
        Assert.True(Status(g).Sold);
        Assert.Equal("She's sold.", Readiness(g).SeasonLead);
    }

    // ------------------------------------------------------------------ 5. the mooring chafe, measured

    private sealed record Moored(int? PartedOn, int? WarnedOn);

    /// <summary>Launched on day 96 on new lines or the previous owner's, left alone with a day-by-day wait to day 270.</summary>
    private Moored Season96(int seed, bool newLines)
    {
        var g = Prepared(60, WeatherSeries.Generate(seed), seed);
        if (newLines) Fix(g, "mooring lines", c => new Replace(c));
        Ok(Walk.Start(g));
        WaitTo(g, 96);
        TryAgainTomorrow(() => g.Execute(new Launch()), g);
        int? parted = null, warned = null;
        while (Status(g).Day < 270 && !Status(g).Sunk && parted is null)
        {
            var r = Ok(g.Execute(new WaitDays(1)));
            if (warned is null && r.Alerts.Any(a => a.Text.Contains("bow lines creak"))) warned = Status(g).Day;
            if (r.Alerts.Any(a => a.Text.StartsWith("The harbourmaster rang", StringComparison.Ordinal))) parted = Status(g).Day;
        }
        return new Moored(parted, warned);
    }

    [Fact]
    public void New_lines_fitted_at_launch_survive_an_ordinary_season_and_warn_weeks_before_they_part()
    {
        var fresh = Enumerable.Range(1, 20).Select(seed => Season96(seed, newLines: true)).ToList();
        var old = Enumerable.Range(1, 20).Select(seed => Season96(seed, newLines: false)).ToList();
        string Show(IEnumerable<int?> days) => string.Join(",", days.Select(d => d?.ToString() ?? "-"));
        output.WriteLine($"new lines, launched day 96, to day 270: parted {Show(fresh.Select(m => m.PartedOn))}");
        output.WriteLine($"new lines: first chafe alert        {Show(fresh.Select(m => m.WarnedOn))}");
        output.WriteLine($"old lines: parted                   {Show(old.Select(m => m.PartedOn))}");
        output.WriteLine($"old lines: first chafe alert        {Show(old.Select(m => m.WarnedOn))}");

        // New lines: at most two of twenty seeds part by day 270, none inside the first 30 days, and the warning came weeks before.
        var parted = fresh.Where(m => m.PartedOn is not null).ToList();
        Assert.True(parted.Count <= 2, $"{parted.Count} of 20 new lines parted");
        Assert.All(parted, m =>
        {
            Assert.True(m.PartedOn >= 96 + 30, $"parted on day {m.PartedOn}");
            Assert.NotNull(m.WarnedOn);
            Assert.True(m.PartedOn - m.WarnedOn >= 14, $"warned on {m.WarnedOn}, parted on {m.PartedOn}");
        });

        // The previous owner's lines may still go in the spring, but not in more than a quarter of the seeds by day 130.
        int oldByDay130 = old.Count(m => m.PartedOn is <= 130);
        output.WriteLine($"old lines parted by day 130: {oldByDay130} of 20");
        Assert.True(oldByDay130 <= 5, $"{oldByDay130} of 20 old lines parted by day 130");
        // And they are the ones that say so first: every old line that parted had warned before.
        Assert.All(old.Where(m => m.PartedOn is not null), m => Assert.True(m.WarnedOn < m.PartedOn));
    }

    // ------------------------------------------------------------------ 6. fouling, measured

    private static int Band(Boat boat, double growth) => boat.Fouling.WaterlineWords.Count(b => b.Under is { } u && growth >= u);

    private int[][] BandsAfloat(bool freshPaint)
    {
        var days = new[] { 150, 200, 250, 270 };
        var rows = new List<int[]>();
        for (int seed = 1; seed <= 20; seed++)
        {
            var boat = RichBoat();
            var g = new Game(boat, 60, new FixedRandom(0.99), seed, WeatherSeries.Generate(seed));
            Fix(g, "negative terminal", c => new Clean(c));
            Fix(g, "seacock", c => new Replace(c));
            Fix(g, "cockpit drains", c => new Replace(c));
            Fix(g, "mooring lines", c => new Replace(c));
            if (freshPaint) Fix(g, "antifouling", c => new Replace(c));
            Ok(Walk.Start(g));
            WaitTo(g, 96);
            TryAgainTomorrow(() => g.Execute(new Launch()), g);
            var row = new List<int>();
            foreach (var day in days)
            {
                WaitTo(g, day);
                Assert.False(Status(g).Sunk);
                row.Add(Band(boat, g.FoulingGrowth));
            }
            rows.Add([.. row]);
        }
        return [.. rows];
    }

    [Fact]
    public void Thin_paint_reaches_the_top_band_only_after_a_full_season_and_fresh_paint_stays_low()
    {
        var days = new[] { 150, 200, 250, 270 };
        var thin = BandsAfloat(freshPaint: false);
        var fresh = BandsAfloat(freshPaint: true);
        int top = RichBoat().Fouling.WaterlineWords.Count - 1;
        for (int i = 0; i < days.Length; i++)
        {
            output.WriteLine($"day {days[i]} thin : bands {string.Join("", thin.Select(r => r[i]))}  (top band in {thin.Count(r => r[i] == top)} of 20)");
            output.WriteLine($"day {days[i]} fresh: bands {string.Join("", fresh.Select(r => r[i]))}  (top band in {fresh.Count(r => r[i] == top)} of 20)");
        }

        // Thin paint: nowhere near the top before the middle of the summer, the top band only late, and by the end of the season in some seeds.
        Assert.Equal(0, thin.Count(r => r[0] == top));
        Assert.Equal(0, thin.Count(r => r[1] == top));
        Assert.True(thin.Count(r => r[2] == top) <= 4, "top band at day 250 in too many seeds");
        Assert.InRange(thin.Count(r => r[3] == top), 5, 18);
        // It grows through the bands (a scrub or a look would show something at each visit).
        Assert.True(thin.Average(r => r[2]) > thin.Average(r => r[1]) + 0.5);
        Assert.True(thin.Average(r => r[1]) > thin.Average(r => r[0]) + 0.5);
        // Fresh paint stays in the low bands, all season, in every seed.
        Assert.All(fresh, r => Assert.True(r.Max() <= 3, string.Join(",", r)));
    }

    [Fact]
    public void The_knots_lost_and_the_impeller_load_at_the_top_are_up_to_about_a_knot()
    {
        var f = RichBoat().Fouling;
        Assert.InRange(f.MaxKnotsLost, 0.8, 1.25);
        Assert.InRange(f.LoadPerGrowth, 0.3, 0.6);
    }

    [Fact]
    public void A_look_over_the_side_says_when_the_growth_has_moved_within_the_same_words()
    {
        var g = LaunchedOn(100, Calm());
        string Side() => Ok(g.Execute(new Look())).Narrative.Single(l => l.StartsWith("Over the side:", StringComparison.Ordinal));
        g.FoulingGrowth = 0.31;
        Assert.Equal("Over the side: a green beard at the waterline.", Side());     // first look: nothing to compare with
        g.FoulingGrowth = 0.33;
        Assert.Equal("Over the side: a green beard at the waterline.", Side());     // a small step: no comment
        g.FoulingGrowth = 0.42;
        Assert.Equal("Over the side: a green beard at the waterline, more than when you last looked.", Side());
        g.FoulingGrowth = 0.33;
        Assert.Equal("Over the side: a green beard at the waterline, less than when you last looked.", Side());
    }

    [Fact]
    public void A_scrub_changes_what_the_next_look_shows()
    {
        var g = LaunchedOn(100, Calm());
        string Side() => Ok(g.Execute(new Look())).Narrative.SingleOrDefault(l => l.StartsWith("Over the side:", StringComparison.Ordinal)) ?? "";
        foreach (var growth in new[] { 0.2, 0.35, 0.5, 0.65, 0.9 })
        {
            g.FoulingGrowth = growth;
            var before = Side();
            Ok(g.Execute(new Scrub()));
            var after = Side();
            Assert.NotEqual(before, after);
        }
    }

    // ------------------------------------------------------------------ 7. the season line

    [Theory]
    [InlineData(340, "Winter, year 1.")]
    [InlineData(400, "Winter, year 2.")]
    [InlineData(440, "Spring, year 2.")]
    [InlineData(75, "Spring, year 1.")]
    [InlineData(180, "Summer, year 1.")]
    [InlineData(265, "Autumn, year 1.")]
    public void The_season_line_names_the_season_and_the_year(int day, string head)
    {
        var g = new Game(RichBoat(), day, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        Assert.StartsWith(head, Season(g).Text);
        Assert.StartsWith(head, Status(g).Season);
    }

    // ------------------------------------------------------------------ the season actions, one source of truth

    private static Command CommandFor(Game g, string verb) => verb switch
    {
        "wait-until-spring" => new WaitUntilSpring(),
        "keep" => new Keep(),
        "book-haulout" => new BookHaulout(Season(g).FirstFreeDay ?? Status(g).Day + 1),
        "haul-out" => new HaulOut(),
        "scrub" => new Scrub(),
        "wash-hull" => new WashHull(),
        "clean-hull" => new Clean("hull"),
        "winterise-engine" => new WinteriseEngine(),
        "fill-tank" => new FillTank(),
        "battery-ashore" => new BatteryAshore(),
        "battery-aboard" => new BatteryAboard(),
        "winter-charger" => new WinterCharger(),
        "fit-winter-cover" => new FitWinterCover(),
        "list-for-sale" => new ListForSale(100000),
        "accept-offer" => new AcceptOffer(),
        _ => throw new ArgumentException(verb),
    };

    public static IEnumerable<object[]> ActionStates() =>
    [
        ["day 75, on the hard, not launched"],
        ["mid-summer, afloat"],
        ["laid up, in the cockpit"],
        ["laid up, beside the hull"],
        ["laid up, on deck"],
    ];

    private static Game StateFor(string state)
    {
        switch (state)
        {
            case "day 75, on the hard, not launched":
                return new Game(RichBoat(), 75, new FixedRandom(0.99), Game.DefaultSeed, Calm());
            case "mid-summer, afloat":
                return LaunchedOn(180, Calm());
            default:
                var g = LaidUp(Calm());
                if (state.EndsWith("beside the hull", StringComparison.Ordinal)) Walk.ToPlace(g, "beside-the-hull");
                if (state.EndsWith("on deck", StringComparison.Ordinal)) Walk.ToPlace(g, "on-deck");
                return g;
        }
    }

    [Theory]
    [MemberData(nameof(ActionStates))]
    public void Every_season_action_says_what_the_command_would_do(string state)
    {
        var verbs = Season(StateFor(state)).Actions!.Select(a => a.Verb).ToList();
        Assert.Equal(15, verbs.Count);
        foreach (var verb in verbs)
        {
            var g = StateFor(state);                        // a fresh game for each, so an accepted one changes nothing for the next
            var action = Season(g).Actions!.Single(a => a.Verb == verb);
            var r = g.Execute(CommandFor(g, verb));
            if (action.Available)
            {
                Assert.Null(action.UnavailableReason);
                Assert.True(r.Accepted, $"{state}: {verb} was offered but refused: {string.Join(" ", r.Narrative)}");
            }
            else
            {
                Assert.False(r.Accepted, $"{state}: {verb} was withheld but accepted");
                Assert.Equal(action.UnavailableReason, Assert.Single(r.Narrative));
                Assert.Equal(0, r.MinutesTaken);
                Assert.Equal(0, r.CostNok);
            }
        }
    }

    [Fact]
    public void Waiting_for_spring_and_keeping_her_are_offered_only_when_they_make_sense()
    {
        var spring = Season(StateFor("day 75, on the hard, not launched")).Actions!;
        Assert.Equal("It's already the season.", spring.Single(a => a.Verb == "wait-until-spring").UnavailableReason);
        Assert.Equal("You haven't had a season with her yet.", spring.Single(a => a.Verb == "keep").UnavailableReason);

        var summer = Season(StateFor("mid-summer, afloat")).Actions!;
        Assert.False(summer.Single(a => a.Verb == "wait-until-spring").Available);
        Assert.True(summer.Single(a => a.Verb == "keep").Available);
        Assert.True(summer.Single(a => a.Verb == "scrub").Available);
        Assert.False(summer.Single(a => a.Verb == "winterise-engine").Available);

        var winter = Season(StateFor("laid up, in the cockpit")).Actions!;
        Assert.True(winter.Single(a => a.Verb == "wait-until-spring").Available);
        Assert.True(winter.Single(a => a.Verb == "keep").Available);
        Assert.True(winter.Single(a => a.Verb == "winter-charger").Available);
        Assert.False(winter.Single(a => a.Verb == "scrub").Available);
        Assert.True(Season(StateFor("laid up, on deck")).Actions!.Single(a => a.Verb == "fit-winter-cover").Available);
        Assert.True(Season(StateFor("laid up, beside the hull")).Actions!.Single(a => a.Verb == "clean-hull").Available);
    }

    [Fact]
    public void Once_she_is_sold_no_season_action_is_available()
    {
        var g = new Game(RichBoat(), 75, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        Ok(g.Execute(new ListForSale(100000)));
        for (int i = 0; i < 90 && !Sale(g).Viewings.Any(v => v.Open); i++) Ok(g.Execute(new WaitDays(1)));
        Ok(g.Execute(new AcceptOffer()));
        Assert.All(Season(g).Actions!, a =>
        {
            Assert.False(a.Available);
            Assert.StartsWith("She's sold.", a.UnavailableReason);
        });
    }

    // ------------------------------------------------------------------ the status line leads like readiness

    [Fact]
    public void The_status_line_leads_with_the_season_after_launch_and_with_the_deadline_before()
    {
        var hard = new Game(RichBoat(), 75, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        Assert.Null(Status(hard).SeasonLead);
        Assert.Equal(96, Status(hard).LaunchDeadlineDay);

        var g = LaunchedOn(100, Calm());
        Assert.Equal("No slot booked: the yard lifts from day 255 to day 300; the first free slot is day 255.", Status(g).SeasonLead);
        Ok(g.Execute(new BookHaulout(258)));
        Assert.Equal("Haulout booked for day 258.", Status(g).SeasonLead);
        Assert.Equal(Readiness(g).SeasonLead, Status(g).SeasonLead);
    }
}
