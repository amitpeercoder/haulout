using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>Fuel, daylight, fog, the patrol, the passage plan and the anchor (design note 010, step 2).</summary>
public class PassageTests
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
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static TripPlanView Plan(Game g, string? to = null, int? day = null) => (TripPlanView)g.Ask(new PlanTrip(to, day));
    private static TripView Trip(Game g) => (TripView)g.Ask(new Trip());

    private static WeatherSeries Clear() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));

    /// <summary>A fresh boat afloat at Skjærvik with the engine running (terminal cleaned, seacock replaced, started, launched).</summary>
    private static Game Afloat(WeatherSeries weather, int startDay = 100, Boat? boat = null, bool anchorFixed = false, bool lightsFixed = false, bool newImpeller = false)
    {
        boat ??= BoatLoader.LoadSloop28(withCalendar: false);
        if (anchorFixed) boat.Get("anchor-gear").Replace(); // she starts with a seized anchor chain
        var g = new Game(boat, startDay, new FixedRandom(0.99), Game.DefaultSeed, weather);
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        if (lightsFixed) Ok(Walk.Do(g, "navigation lights", c => new Replace(c)));
        if (newImpeller) Ok(Walk.Do(g, "impeller", c => new Replace(c))); // the tired one cooks the engine after a couple of hours
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Assert.True(Status(g).EngineRunning);
        return g;
    }

    private static CommandResult Depart(Game g, string? to = null)
    {
        Walk.ToPlace(g, "cockpit");
        return g.Execute(new Depart(to));
    }

    /// <summary>Under engine, hour by hour, until the trip card. Returns the last result.</summary>
    private static CommandResult MotorToArrival(Game g)
    {
        CommandResult r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 20 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        Assert.IsType<TripResultView>(r.Observation);
        return r;
    }

    private static double Litres(Boat boat) => boat.Get("fuel-tank").State["fuelLitres"];

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ the weather series

    [Fact]
    public void Fog_is_its_own_stream_the_same_seed_gives_the_same_fog_and_the_main_weather_does_not_move()
    {
        var a = WeatherSeries.Generate(4);
        var b = WeatherSeries.Generate(4);
        for (int d = 0; d <= 1080; d++) Assert.Equal(a[d], b[d]);
        Assert.Contains(Enumerable.Range(0, 1081), d => a[d].FogUntilHour > 0);
        // A climate without fog draws the same wind, rain and temperature: the golden hash of SeasonTests pins the rest.
        var noFog = WeatherSeries.Generate(4, Climate.Load(NoFogJson()));
        for (int d = 0; d <= 720; d++)
            Assert.Equal((a[d].WindForce, a[d].WindFrom, a[d].RainMm, a[d].TempC, a[d].NightC), (noFog[d].WindForce, noFog[d].WindFrom, noFog[d].RainMm, noFog[d].TempC, noFog[d].NightC));
        Assert.All(Enumerable.Range(0, 1081), d => Assert.Equal(0, noFog[d].FogUntilHour));
    }

    private static string NoFogJson()
    {
        // The shipped climate with every fogChance set to zero.
        using var stream = typeof(Climate).Assembly.GetManifestResourceStream("Haulout.Sim.Data.weather.json")!;
        using var reader = new StreamReader(stream);
        return System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "\"fogChance\": [0-9.]+", "\"fogChance\": 0");
    }

    [Fact]
    public void A_foggy_day_is_all_day_or_lifts_at_a_whole_hour_between_nine_and_thirteen()
    {
        var hours = new HashSet<int>();
        for (int seed = 1; seed <= 20; seed++)
        {
            var s = WeatherSeries.Generate(seed);
            for (int d = 0; d <= 1080; d++)
                if (s[d].FogUntilHour > 0) hours.Add(s[d].FogUntilHour);
        }
        Assert.Contains(24, hours);
        Assert.All(hours.Where(h => h != 24), h => Assert.InRange(h, 9, 13));
        Assert.True(hours.Count(h => h != 24) >= 3, "the lifting hour should vary");
    }

    [Fact]
    public void Fog_days_per_month_over_twenty_seeds_stay_near_the_chance_of_the_month()
    {
        var chance = Climate.Default.Months.Select(m => m.FogChance).ToList();
        var counts = new int[12];
        for (int seed = 1; seed <= 20; seed++)
        {
            var s = WeatherSeries.Generate(seed);
            for (int d = 0; d < 360; d++) if (s[d].FogUntilHour > 0) counts[new GameClock(d).Month]++;
        }
        for (int m = 0; m < 12; m++)
        {
            double expected = chance[m] * 30 * 20; // roughly: the blend between month centres moves it a little
            Assert.InRange(counts[m], expected * 0.4, expected * 1.9);
        }
        Assert.True(counts[4] > counts[0], "May is foggier than January");
    }

    [Fact]
    public void Daylight_follows_the_month_centres_long_in_June_short_in_December()
    {
        var s = WeatherSeries.Generate(4);
        Assert.InRange(s[165].DaylightHours, 18.8, 19.1);
        Assert.InRange(s[345].DaylightHours, 5.9, 6.2);
        Assert.InRange(s[75].DaylightHours, 11.4, 11.7); // the centre of March: 11.5
        Assert.True(s[100].DaylightHours > s[75].DaylightHours);
        // Every seed has the same days: daylight is not random.
        Assert.Equal(s[200].DaylightHours, WeatherSeries.Generate(9)[200].DaylightHours);
    }

    [Fact]
    public void A_hand_made_series_is_never_dark_and_never_foggy()
    {
        var day = Clear()[10];
        Assert.Equal(24, day.DaylightHours);
        Assert.Equal(0, day.FogUntilHour);
        Assert.False(WeatherWords.IsDark(day, 3, 13));
        Assert.False(WeatherWords.IsFoggy(day, 3));
    }

    [Fact]
    public void An_hour_is_dark_more_than_half_the_day_from_solar_noon()
    {
        var short_ = new WeatherDay(0, 3, "west", 0, 5, 1, DaylightHours: 6);
        Assert.False(WeatherWords.IsDark(short_, 13, 13));
        Assert.False(WeatherWords.IsDark(short_, 15.9, 13));
        Assert.True(WeatherWords.IsDark(short_, 16.1, 13));
        Assert.True(WeatherWords.IsDark(short_, 9.9, 13));
    }

    [Fact]
    public void The_description_tells_the_fog_only_while_it_lies_and_the_forecast_calls_it_a_morning_you_see_coming()
    {
        var d = new WeatherDay(0, 3, "west", 0, 8, 3, FogUntilHour: 11);
        Assert.DoesNotContain("Fog", WeatherWords.Describe(d));
        Assert.EndsWith("Fog over the water.", WeatherWords.Describe(d, 9));
        Assert.DoesNotContain("Fog", WeatherWords.Describe(d, 12));
        Assert.Equal("morning fog", WeatherWords.FogWord(d));
        Assert.Equal("fog all day", WeatherWords.FogWord(d with { FogUntilHour = 24 }));
        Assert.Equal("", WeatherWords.FogWord(d with { FogUntilHour = 0 }));
    }

    // ------------------------------------------------------------------ fuel

    [Fact]
    public void The_tank_is_sixty_litres_with_twenty_seven_aboard_and_the_gauge_reads_to_five()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        Assert.Equal(60, boat.Get("fuel-tank").State["litres"]);
        Assert.Equal(27, Litres(boat));
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Clear());
        Assert.Equal("Fuel: about 25 litres (a little under half a tank).", Status(g).Fuel);
        Assert.DoesNotContain("27", Status(g).Fuel!);
        boat.Get("fuel-tank").State["fuelLitres"] = 58;
        Assert.Equal("Fuel: about 60 litres (a full tank).", Status(g).Fuel);
        boat.Get("fuel-tank").State["fuelLitres"] = 0.2;
        Assert.Equal("Fuel: the needle lies on empty.", Status(g).Fuel);
        boat.Get("fuel-tank").State["fuelLitres"] = 1.0;
        Assert.Equal("Fuel: almost nothing, the needle just off empty.", Status(g).Fuel);
    }

    [Fact]
    public void A_boat_without_fuel_data_burns_nothing_and_has_no_gauge()
    {
        var json = Sloop28Json();
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove("fuel");
        var boat = BoatLoader.Load(node.ToJsonString());
        var g = Afloat(Clear(), boat: boat);
        Assert.Null(Status(g).Fuel);
        double before = Litres(boat);
        Ok(g.Execute(new PumpBilge()));
        Assert.Equal(before, Litres(boat));
    }

    [Fact]
    public void An_hour_idling_at_the_mooring_burns_six_tenths_of_a_litre_and_an_hour_motoring_two_and_a_half()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        double idleStart = Litres(boat);
        for (int i = 0; i < 4; i++) Ok(g.Execute(new PumpBilge())); // four times 15 minutes with the engine running
        Assert.Equal(0.6, idleStart - Litres(boat), 6);

        Depart(g);
        double motorStart = Litres(boat);
        Ok(g.Execute(new Motor()));
        Assert.Equal(2.5, motorStart - Litres(boat), 6);
    }

    [Fact]
    public void Heaving_to_with_the_engine_running_burns_the_idle_rate_and_a_stopped_engine_burns_nothing()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        Depart(g);
        Ok(g.Execute(new Motor()));
        Ok(g.Execute(new HeaveTo()));
        double before = Litres(boat);
        Ok(g.Execute(new Continue())); // an hour hove-to, engine running
        Assert.Equal(0.6, before - Litres(boat), 6);
        Ok(g.Execute(new StopEngine()));
        before = Litres(boat);
        Ok(g.Execute(new Continue()));
        Assert.Equal(before, Litres(boat));
    }

    [Fact]
    public void An_empty_tank_stops_her_under_way_with_the_gauge_and_the_engine_will_not_start()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        boat.Get("fuel-tank").State["fuelLitres"] = 3.2;
        Depart(g);                                   // ten minutes idling: 3.1 litres
        var first = Ok(g.Execute(new Motor()));      // 2.5 litres gone, 0.6 left
        Assert.True(Status(g).EngineRunning);
        var second = Ok(g.Execute(new Continue()));  // the rest
        Assert.False(Status(g).EngineRunning);
        Assert.Contains(second.Alerts, a => a.Text == "The engine coughs and dies. The fuel gauge needle lies on empty.");
        Assert.DoesNotContain(second.Alerts, a => a.Text.Contains("filter bowl"));
        Assert.Equal("drifting", Trip(g).Mode);
        Assert.Equal("Fuel: the needle lies on empty.", Status(g).Fuel);
        Assert.Equal(0, Litres(boat));

        Ok(g.Execute(new HeaveTo()));
        var start = Ok(Walk.Start(g));
        Assert.False(Status(g).EngineRunning);
        Assert.Contains("dies", Text(start));
    }

    [Fact]
    public void An_empty_tank_at_the_mooring_stops_the_idling_engine_with_the_same_alert()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        boat.Get("fuel-tank").State["fuelLitres"] = 0.1;
        var r = Ok(g.Execute(new PumpBilge()));
        Assert.False(Status(g).EngineRunning);
        Assert.Contains(r.Alerts, a => a.Text == "The engine coughs and dies. The fuel gauge needle lies on empty.");
    }

    [Fact]
    public void The_filter_bowl_of_an_empty_tank_is_half_air_and_inspecting_the_tank_names_the_level()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        var tank = Ok(Walk.Do(g, "fuel tank", c => new Inspect(c)));
        Assert.Contains("In the tank: about 25 litres (a little under half a tank).", Text(tank));
        Ok(Walk.Do(g, "primary filter", c => new Inspect(c)));
        // The tired fuel system has water in the bowl; put it right so the bowl itself can be read.
        foreach (var id in new[] { "primary-filter", "fuel-tank" })
            foreach (var f in boat.Get(id).ActiveFaults.ToList()) f.Clear();
        Assert.Contains("Clear, pale diesel", Text(g.Execute(new CheckFilterBowl())));
        boat.Get("fuel-tank").State["fuelLitres"] = 0;
        Assert.Contains("The bowl is half air. Nothing is coming through from the tank.", Text(g.Execute(new CheckFilterBowl())));
    }

    // ------------------------------------------------------------------ night and the patrol

    /// <summary>An hour of light a day, 12:30 to 13:30: the preparations take until the middle of the afternoon, so she casts off in the dark.</summary>
    private static WeatherSeries ShortDays() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 1));

    [Fact]
    public void Running_in_the_dark_with_a_cracked_lens_meets_the_patrol_once_and_finds_the_fault()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(ShortDays(), boat: boat);
        int balance = Status(g).BalanceNok;
        Depart(g);
        var hour = Ok(g.Execute(new Motor()));
        string patrol = "A patrol boat's searchlight finds you: your bow light is out. They take your name and a fine of 2000 NOK, and tell you to get it seen to.";
        Assert.Contains(hour.Alerts, a => a.Text == patrol);
        Assert.Contains(hour.NewlyDiscovered, k => k.Name == "Cracked navigation light lens");
        Assert.Equal(balance - 2000, Status(g).BalanceNok);
        Assert.Contains(Log(g).Entries, e => e.Text == "Found: Cracked navigation light lens.");
        Assert.Contains(Log(g).Entries, e => e.Text == patrol && e.CostNok == 2000 && e.Alert);

        var next = Ok(g.Execute(new Continue()));
        Assert.DoesNotContain(next.Alerts, a => a.Text == patrol);
        Assert.Equal(balance - 2000, Status(g).BalanceNok);

        var card = Assert.IsType<TripResultView>(MotorToArrival(g).Observation);
        Assert.Contains(patrol, card.Failures);
    }

    [Fact]
    public void With_the_lights_mended_or_by_day_the_patrol_does_not_come()
    {
        var mended = Afloat(ShortDays(), lightsFixed: true);
        int balance = Status(mended).BalanceNok;
        Depart(mended);
        Assert.DoesNotContain(Ok(mended.Execute(new Motor())).Alerts, a => a.Text.Contains("patrol"));
        MotorToArrival(mended);
        Assert.Equal(balance, Status(mended).BalanceNok);

        var day = Afloat(Clear());
        balance = Status(day).BalanceNok;
        Depart(day);
        MotorToArrival(day);
        Assert.Equal(balance, Status(day).BalanceNok);
    }

    [Fact]
    public void At_anchor_the_patrol_does_not_fine_her_and_a_tow_in_the_dark_is_not_running_dark_either()
    {
        var g = Afloat(ShortDays(), anchorFixed: true);
        int balance = Status(g).BalanceNok;
        Depart(g);
        Ok(g.Execute(new Anchor()));       // in the dark, but lying to her anchor
        Assert.Equal("at anchor", Trip(g).Mode);
        var hour = Ok(g.Execute(new Continue()));
        Assert.DoesNotContain(hour.Alerts, a => a.Text.Contains("patrol"));
        Assert.Equal(balance, Status(g).BalanceNok);

        var towed = Afloat(ShortDays());
        int towedBalance = Status(towed).BalanceNok;
        Depart(towed);
        Ok(towed.Execute(new CallTow()));  // the towboat carries her home: no patrol for a boat under tow
        Assert.Equal(towedBalance - 6000, Status(towed).BalanceNok);
    }

    // ------------------------------------------------------------------ fog

    /// <summary>Fog all day on day 101 only, and a clear day 100: she can cast off late on 100 and meet the fog after midnight.</summary>
    private static WeatherSeries FogTomorrow() =>
        WeatherSeries.From(d => new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: d >= 101 ? 24 : 0));

    [Fact]
    public void Fog_in_the_harbour_refuses_the_departure_in_plain_words_free_and_unlogged_until_it_lifts()
    {
        var weather = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: 14));
        var g = Afloat(weather);
        int entries = Log(g).Entries.Count;
        string wall = "Fog lies over the harbour, and the harbourmaster won't let a boat out without a plotter or radar. It often lifts by late morning.";
        Walk.ToPlace(g, "cockpit");
        var no = g.Execute(new Depart());
        Assert.False(no.Accepted);
        Assert.Equal(wall, Assert.Single(no.Narrative));
        Assert.Equal((0, 0), (no.MinutesTaken, no.CostNok));
        Assert.Equal(entries, Log(g).Entries.Count);
        Assert.Contains("Fog over the water.", Status(g).Weather);
        Assert.Contains(wall, Plan(g).Blockers);

        for (int i = 0; i < 30; i++) Ok(g.Execute(new PumpBilge())); // 7.5 hours of waiting for it to lift
        Assert.DoesNotContain("Fog", Status(g).Weather);
        Assert.Empty(Plan(g).Blockers);
        Ok(g.Execute(new Depart()));
    }

    [Fact]
    public void Fog_at_sea_holds_her_to_two_knots_and_the_hour_line_says_so()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(FogTomorrow(), boat: boat, newImpeller: true);
        // The preparations took her to the afternoon of day 100. Work the bilge pump (a quarter of an hour at a time) until ten at night,
        // so that she casts off in the clear and meets the morning fog of day 101 at midnight.
        Walk.ToPlace(g, "cockpit");
        while (Status(g).Day == 100 && string.CompareOrdinal(Status(g).TimeOfDay, "21:50") < 0) Ok(g.Execute(new PumpBilge()));
        Assert.Equal(100, Status(g).Day);
        boat.Get("fuel-tank").State["fuelLitres"] = 60;
        Ok(g.Execute(new Depart()));

        double position = 0;
        double Leg(CommandResult r)
        {
            var leg = Assert.IsType<LegView>(r.Observation);
            double delta = leg.PositionNm - position;
            position = leg.PositionNm;
            return delta;
        }
        string Hour(CommandResult r) => r.Narrative.First(l => l.StartsWith("Hour ", StringComparison.Ordinal));

        var h1 = Ok(g.Execute(new Motor()));
        Assert.InRange(Leg(h1), 4.5, 5.0);                // five knots less a little growth on the hull
        Assert.DoesNotContain("Fog", Hour(h1));
        var h2 = Ok(g.Execute(new Continue()));           // fifty minutes clear, ten in the fog
        Assert.InRange(Leg(h2), 2.1, 4.9);                // partly clear, partly fog
        var h3 = Ok(g.Execute(new Continue()));           // a whole hour in the fog
        Assert.Equal(2.0, Leg(h3), 6);
        Assert.Contains("Fog: you creep along at 2 knots, listening.", Hour(h3));
        var h4 = Ok(g.Execute(new Continue()));
        Assert.Equal(2.0, Leg(h4), 6);
        var last = h4;
        for (int i = 0; i < 3 && last.Observation is not TripResultView; i++) last = Ok(g.Execute(new Continue()));
        var card = Assert.IsType<TripResultView>(last.Observation);
        Assert.True(card.Minutes > 4 * 60, $"fourteen miles took {card.Minutes} minutes");
    }

    [Fact]
    public void A_leg_in_fog_is_planned_at_the_creeping_speed()
    {
        var weather = WeatherSeries.From(d => new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: d >= 101 ? 24 : 0));
        var g = Afloat(weather);
        var plan = Plan(g, "hestholmen", 101);
        Assert.Contains(plan.Warnings!, w => w.StartsWith("The forecast says fog on day 101", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the plan

    [Fact]
    public void The_plan_says_the_fuel_the_gauge_shows_against_what_the_passage_needs()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        var plan = Plan(g, "hestholmen");
        Assert.Equal("About 25 litres aboard: some 10 hours of motoring. This passage needs about 7 litres.", plan.Fuel);
        Assert.DoesNotContain(plan.Warnings!, w => w.Contains("fuel", StringComparison.OrdinalIgnoreCase));

        boat.Get("fuel-tank").State["fuelLitres"] = 5;
        var thin = Plan(g, "hestholmen");
        Assert.Equal("About 5 litres aboard: some 2 hours of motoring. This passage needs about 7 litres.", thin.Fuel);
        Assert.Contains("The fuel aboard won't motor her all the way: fill the tank or count on the wind.", thin.Warnings!);
    }

    [Fact]
    public void The_plan_warns_of_arriving_after_dark_and_mentions_the_lights_only_once_the_player_knows_of_the_problem()
    {
        // 26 nm to Storholmen at five knots from nine in the morning: 5.2 hours, with only two hours of light in the day (12:00 to 14:00).
        var weather = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 2));
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(weather, boat: boat);
        var plan = Plan(g, "storholmen", 110);
        var dark = Assert.Single(plan.Warnings!, w => w.StartsWith("She'll arrive after dark", StringComparison.Ordinal));
        Assert.Equal("She'll arrive after dark (the light goes at about 14:00).", dark);
        Assert.DoesNotContain("navigation lights", dark);   // the lens is cracked, but the player has not found it
        Assert.DoesNotContain(plan.Warnings!, w => w.Contains("lens", StringComparison.OrdinalIgnoreCase));

        Ok(Walk.Do(g, "navigation lights", c => new Inspect(c)));
        var known = Assert.Single(Plan(g, "storholmen", 110).Warnings!, w => w.StartsWith("She'll arrive after dark", StringComparison.Ordinal));
        Assert.Equal("She'll arrive after dark (the light goes at about 14:00), and you know the navigation lights have a problem: the patrol fines boats running without lights.", known);
    }

    [Fact]
    public void A_passage_that_leaves_in_the_dark_and_arrives_in_the_light_says_part_of_it_is_dark_and_a_long_day_says_nothing()
    {
        var short5 = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 5)); // 10:30 to 15:30
        var g = Afloat(short5);
        Assert.Contains("Part of the passage is in the dark (the light comes at about 10:30).", Plan(g, "storholmen", 110).Warnings!);
        var long12 = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 12)); // 07:00 to 19:00
        Assert.DoesNotContain(Plan(Afloat(long12), "storholmen", 110).Warnings!, w => w.Contains("dark"));
    }

    [Fact]
    public void The_plan_counts_the_weekdays_a_passage_touches_and_never_twice_nor_at_weekends()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), Game.DefaultSeed, Clear());
        // Day 77 is a Monday: a passage from there touches one weekday.
        var monday = Plan(g, "hestholmen", 77);
        Assert.Equal(1, monday.VacationDays);
        Assert.Contains("This passage costs 1 vacation day (Monday). You have 25 left.", monday.Warnings!);
        var weekend = Plan(g, "hestholmen", 76);
        Assert.Equal(0, weekend.VacationDays);
        Assert.Contains("This passage falls on a weekend: no vacation days.", weekend.Warnings!);
        // A long passage from a Friday morning at nine, by the clock of the plan, stays in the one day.
        Assert.Equal(1, Plan(g, "storholmen", 81).VacationDays);
    }

    [Fact]
    public void A_weekday_already_charged_is_not_counted_again_and_none_left_means_unpaid_leave()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = new Game(boat, 77, new FixedRandom(0.99), Game.DefaultSeed, Clear()); // a Monday
        Assert.Equal(1, Plan(g, "hestholmen").VacationDays);
        // A long job today makes it a vacation day; the plan no longer counts it.
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        for (int i = 0; i < 40 && Status(g).VacationDaysLeft == 25; i++) Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Equal(24, Status(g).VacationDaysLeft);
        Assert.Equal(0, Plan(g, "hestholmen").VacationDays);
        Assert.Equal(1, Plan(g, "hestholmen", 78).VacationDays);
    }

    [Fact]
    public void The_plan_names_the_event_waiting_at_the_other_end_and_an_anchorage_has_no_shop_or_fuel()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 140, new FixedRandom(0.99), Game.DefaultSeed, Clear());
        var regatta = Plan(g, "hestholmen", 152).AtDestination;
        Assert.Equal(["The regatta party at Hestholmen (Saturday, day 152) asks only that you are there."], regatta);
        Assert.Null(Plan(g, "hestholmen", 145).AtDestination);   // not the day of the party

        var midsummer = Plan(g, "storholmen", 172).AtDestination;
        Assert.Single(midsummer!);
        Assert.StartsWith("Midsummer at Storholmen (Friday and Saturday, days 172 to 173) asks only that you are there.", midsummer![0]);
    }

    [Fact]
    public void The_plan_to_Kvitnes_lists_the_club_cruise_with_what_the_player_knows_and_the_anchorage_rule()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: true);
        var g = Afloat(Clear(), startDay: 215, boat: boat);
        var toHest = Assert.IsType<TripResultView>((Depart(g, "hestholmen") is { Accepted: true } ? MotorToArrival(g) : throw new Xunit.Sdk.XunitException()).Observation);
        Assert.Equal("Hestholmen", toHest.Destination);
        var plan = Plan(g, "kvitnes");
        Assert.NotNull(plan.AtDestination);
        Assert.Equal(2, plan.AtDestination!.Count);
        Assert.Equal("The club cruise to Kvitnes (Saturday and Sunday, days 215 to 216) asks for anchor gear in order (no known problem); a bilge pump that works (no known problem).", plan.AtDestination[0]);
        Assert.Equal("An anchorage: no shop, no fuel.", plan.AtDestination[1]);
        Assert.DoesNotContain("seized", string.Join(" ", plan.AtDestination), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ the anchor

    [Fact]
    public void The_anchor_chain_is_seized_from_the_start_and_anchoring_at_sea_is_refused_and_finds_the_fault()
    {
        var g = Afloat(Clear());
        Depart(g);
        Assert.True(Trip(g).CanAnchor);
        int entries = Log(g).Entries.Count;
        var no = g.Execute(new Anchor());
        Assert.False(no.Accepted);
        Assert.Equal("The chain jams in the hawse after a few metres; the anchor never reaches the bottom. Heave to, or find a mooring.", no.Narrative[0]);
        Assert.Contains("rusted solid", Text(no));
        Assert.Equal((0, 0), (no.MinutesTaken, no.CostNok));
        Assert.Contains(no.NewlyDiscovered, k => k.Name == "Seized anchor chain");
        Assert.Contains(Log(g).Entries, e => e.Text == "Found: Seized anchor chain.");
        Assert.Equal("under engine", Trip(g).Mode);
        Assert.Equal(entries + 1, Log(g).Entries.Count);

        // The second time there is nothing new to find, and it is refused all the same.
        var again = g.Execute(new Anchor());
        Assert.False(again.Accepted);
        Assert.Empty(again.NewlyDiscovered);
    }

    [Fact]
    public void Arriving_at_Kvitnes_with_the_chain_seized_changes_the_anchoring_line_and_finds_the_fault()
    {
        var g = Afloat(Clear(), startDay: 100);
        Depart(g, "hestholmen");
        MotorToArrival(g);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new StartEngine()));
        Depart(g, "kvitnes");
        var arrive = MotorToArrival(g);
        var card = Assert.IsType<TripResultView>(arrive.Observation);
        Assert.Equal("Kvitnes", card.Destination);
        Assert.Contains("The chain jams after a few metres. You lie to a line on the boathouse ring instead, uneasy all night.", card.Lines);
        Assert.DoesNotContain(card.Lines, l => l.Contains("anchor finds the sand"));
        Assert.Contains(arrive.NewlyDiscovered, k => k.Name == "Seized anchor chain");
    }

    [Fact]
    public void Arriving_at_Kvitnes_with_a_sound_chain_is_the_old_quiet_bay()
    {
        var g = Afloat(Clear(), anchorFixed: true);
        Depart(g, "hestholmen");
        MotorToArrival(g);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new StartEngine()));
        Depart(g, "kvitnes");
        var arrive = MotorToArrival(g);
        var card = Assert.IsType<TripResultView>(arrive.Observation);
        Assert.Contains("The anchor finds the sand on the second try, and she swings quietly to it.", card.Lines);
        Assert.Empty(arrive.NewlyDiscovered);
    }

    [Fact]
    public void Replacing_the_chain_lets_her_anchor()
    {
        var g = Afloat(Clear());
        Ok(Walk.Do(g, "anchor", c => new Inspect(c)));
        Assert.Contains("rusted solid", Text(Walk.Do(g, "anchor chain", c => new Inspect(c))));
        Ok(Walk.Do(g, "anchor chain", c => new Replace(c)));
        Ok(Walk.Start(g));                  // mending it shut the engine down
        Ok(Depart(g));
        Ok(g.Execute(new Anchor()));
        Assert.Equal("at anchor", Trip(g).Mode);
    }

    // ------------------------------------------------------------------ ids

    [Fact]
    public void The_new_views_carry_no_ids()
    {
        var g = Afloat(Clear(), boat: BoatLoader.LoadSloop28());
        var data = BoatLoader.LoadSloop28();
        var ids = data.Components.Select(c => c.Id)
            .Concat(data.AllFaults.Select(f => f.Id)).Concat(data.Places.Select(p => p.Id)).Concat(data.Covers.Select(c => c.Id))
            .Concat(data.Positions.Select(p => p.Id)).Concat(data.Trips!.Ports.Select(p => p.Id)).Distinct()
            // Port ids spelt the same as the words they sit among (a port's own name, lowercased) are not secrets.
            .Where(i => i.Contains('-')).ToList();
        string[] json =
        [
            JsonSerializer.Serialize<QueryResult>(Plan(g, "hestholmen", 152)),
            JsonSerializer.Serialize<QueryResult>(Plan(g, "storholmen", 172)),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Calendar())),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())),
        ];
        foreach (var j in json)
            foreach (var id in ids)
                Assert.DoesNotContain($"\"{id}\"", j);
    }
}
