using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>Fuel, daylight, fog, the night under way, the passage plan and the anchor (design note 010, step 2; the night close calls and the harbourmaster's letter, second pass E).</summary>
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
    private static Game Afloat(WeatherSeries weather, int startDay = 100, Boat? boat = null, bool anchorFixed = false, bool lightsFixed = false, bool newImpeller = false, int worldSeed = Game.DefaultSeed)
    {
        boat ??= BoatLoader.LoadSloop28(withCalendar: false);
        if (anchorFixed) { boat.Get("anchor-gear").Replace(); boat.Get("anchor-shackle").Replace(); } // she starts with a seized anchor chain
        var g = new Game(boat, startDay, new FixedRandom(0.99), worldSeed, weather);
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

    /// <summary>The frozen shackle has had its wet season: it has escalated and the chain is seized (design note 010, second pass F).</summary>
    private static Boat WithSeizedChain()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        boat.Get("anchor-shackle").Faults.Single().MarkEscalated();
        boat.AllFaults.Single(f => f.Id == "anchor-chain-seized").Wake(100);
        return boat;
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

    /// <summary>The rule's own number for the boat as sold: <paramref name="knots"/> null is idle; the filter she starts with (undiscovered) adds its share.</summary>
    private static double Burn(Boat boat, double? knots, bool filter = false, double fouling = 0)
    {
        var adds = new List<FuelAdd>();
        if (fouling > 0) adds.Add(new FuelAdd("fouling", boat.Fuel.FoulingMaxAdd * fouling));
        if (filter) adds.Add(new FuelAdd("filter", boat.Fuel.FaultAdds["primary-filter-clogged"]));
        return FuelBurn.Of(boat.Fuel, knots, adds).LitresPerHour;
    }

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ------------------------------------------------------------------ the weather series

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
    public void The_tank_is_forty_five_litres_with_twenty_seven_aboard_and_the_gauge_reads_to_five()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        Assert.Equal(45, boat.Get("fuel-tank").State["litres"]);
        Assert.Equal(27, Litres(boat));
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Clear());
        Assert.Equal("Fuel: about 25 litres (a little over half a tank).", Status(g).Fuel);
        Assert.DoesNotContain("27", Status(g).Fuel!);
        boat.Get("fuel-tank").State["fuelLitres"] = 44;
        Assert.Equal("Fuel: about 45 litres (a full tank).", Status(g).Fuel);
        boat.Get("fuel-tank").State["fuelLitres"] = 0.2;
        Assert.Equal("Fuel: the needle lies on empty.", Status(g).Fuel);
        boat.Get("fuel-tank").State["fuelLitres"] = 1.0;
        Assert.Equal("Fuel: almost nothing, the needle just off empty.", Status(g).Fuel);
    }

    // ------------------------------------------------------------------ fuel by horsepower and load (design note 010, second pass C)

    [Fact]
    public void The_load_is_the_cube_of_the_speed_over_the_hull_speed_clamped_and_the_idle_load_when_going_nowhere()
    {
        var rules = BoatLoader.LoadSloop28(withCalendar: false).Fuel;
        Assert.Equal(Math.Pow(5 / rules.HullSpeedKn, 3), FuelBurn.Load(rules, 5), 9);
        Assert.Equal(rules.LoadMin, FuelBurn.Load(rules, 1.0), 9);                    // crawling: the floor
        Assert.Equal(rules.LoadMin, FuelBurn.Load(rules, 0), 9);
        Assert.Equal(rules.LoadMax, FuelBurn.Load(rules, rules.HullSpeedKn * 1.2), 9); // above hull speed: full load
        Assert.Equal(rules.LoadMax, FuelBurn.Load(rules, rules.HullSpeedKn), 9);
        Assert.Equal(rules.IdleLoad, FuelBurn.Load(rules, null), 9);
        Assert.True(FuelBurn.Load(rules, 4) < FuelBurn.Load(rules, 5));              // monotonic between the clamps
    }

    [Fact]
    public void Clean_at_five_knots_she_burns_about_one_point_eight_litres_an_hour_and_heavy_fouling_makes_it_about_two_and_a_half()
    {
        var rules = BoatLoader.LoadSloop28(withCalendar: false).Fuel;
        Assert.Equal(18, rules.RatedHp);
        var clean = FuelBurn.Of(rules, 5, []);
        Assert.InRange(clean.LitresPerHour, 1.79, 1.81);
        Assert.Empty(clean.Adds);
        var heavy = FuelBurn.Of(rules, 5, [new FuelAdd("heavy fouling", rules.FoulingMaxAdd)]);
        Assert.InRange(heavy.LitresPerHour, 2.5, 2.55);
        Assert.Equal(clean.LitresPerHour * (1 + rules.FoulingMaxAdd), heavy.LitresPerHour, 9);
    }

    [Fact]
    public void Idling_burns_the_idle_load_at_the_rating_and_each_multiplier_multiplies()
    {
        var rules = BoatLoader.LoadSloop28(withCalendar: false).Fuel;
        double idle = rules.RatedHp * rules.LitresPerHpHour * rules.IdleLoad;
        Assert.Equal(idle, FuelBurn.Of(rules, null, []).LitresPerHour, 9);
        Assert.InRange(idle, 0.53, 0.55);
        var both = FuelBurn.Of(rules, null, [new FuelAdd("a", 0.4), new FuelAdd("b", 0.15)]);
        Assert.Equal(idle * 1.4 * 1.15, both.LitresPerHour, 9);
    }

    [Fact]
    public void The_adds_are_in_words_from_data_a_tenth_a_fifth_a_third_nearly_half()
    {
        var rules = BoatLoader.LoadSloop28(withCalendar: false).Fuel;
        string Add(double a) => FuelBurn.Words(rules, [new FuelAdd("heavy fouling", a)]);
        Assert.Equal("heavy fouling adds a little", Add(0.05));
        Assert.Equal("heavy fouling adds a tenth", Add(0.10));
        Assert.Equal("heavy fouling adds a tenth", Add(0.15));
        Assert.Equal("heavy fouling adds a fifth", Add(0.20));
        Assert.Equal("heavy fouling adds a quarter", Add(0.25));
        Assert.Equal("heavy fouling adds a third", Add(0.32));
        Assert.Equal("heavy fouling adds a third", Add(0.40));
        Assert.Equal("heavy fouling adds nearly half", Add(0.50));
        Assert.Equal("heavy fouling adds a third; a clogged fuel filter adds a tenth",
            FuelBurn.Words(rules, [new FuelAdd("heavy fouling", 0.32), new FuelAdd("a clogged fuel filter", 0.15)]));
    }

    [Fact]
    public void Fouling_adds_in_proportion_to_the_growth_up_to_the_data_maximum_in_the_true_burn()
    {
        foreach (double growth in new[] { 0.25, 0.5, 1.0 })
        {
            var boat = BoatLoader.LoadSloop28(withCalendar: false);
            boat.Get("primary-filter").Replace();                       // no filter fault: the fouling alone
            var g = Afloat(Clear(), boat: boat);
            g.FoulingGrowth = growth;
            Depart(g);
            double start = Litres(boat);
            Ok(g.Execute(new Motor()));
            Assert.Equal(Burn(boat, 5, fouling: growth), start - Litres(boat), 6);
        }
    }

    [Fact]
    public void A_clogged_filter_the_player_has_not_found_burns_a_share_more_and_none_is_burned_extra_once_it_is_replaced()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        Depart(g);
        double start = Litres(boat);
        Ok(g.Execute(new Motor()));
        double burned = start - Litres(boat);
        Assert.Equal(Burn(boat, 5) * (1 + boat.Fuel.FaultAdds["primary-filter-clogged"]), burned, 6);

        var fixedBoat = BoatLoader.LoadSloop28(withCalendar: false);
        fixedBoat.Get("primary-filter").Replace();
        var h = Afloat(Clear(), boat: fixedBoat);
        Depart(h);
        double start2 = Litres(fixedBoat);
        Ok(h.Execute(new Motor()));
        Assert.Equal(Burn(fixedBoat, 5), start2 - Litres(fixedBoat), 6);
    }

    [Fact]
    public void A_flat_battery_charging_adds_five_percent_while_the_engine_runs_and_a_full_one_does_not()
    {
        var rules = BoatLoader.LoadSloop28(withCalendar: false).Fuel;
        Assert.Equal(0.05, rules.ChargingAdd);
        double Hour(double soc)
        {
            var boat = BoatLoader.LoadSloop28(withCalendar: false);
            boat.Get("primary-filter").Replace();
            boat.Get("alternator-belt").Replace();
            foreach (var f in boat.Get("start-battery").ActiveFaults.ToList()) f.Clear();   // a weak battery caps the charge she can show                       // a slipping belt charges nothing
            var g = Afloat(Clear(), boat: boat);
            boat.Get("start-battery").State["stateOfCharge"] = soc;
            double start = Litres(boat);
            for (int i = 0; i < 4; i++) Ok(g.Execute(new PumpBilge()));  // an hour with the engine running, going nowhere
            return start - Litres(boat);
        }
        double full = Hour(0.95), flat = Hour(0.4);
        Assert.Equal(Burn(BoatLoader.LoadSloop28(withCalendar: false), null), full, 6);
        Assert.Equal(full * (1 + rules.ChargingAdd), flat, 6);
    }

    [Fact]
    public void The_plan_names_only_the_multipliers_the_player_knows_and_the_truth_burns_the_rest()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);        // she starts with a half-clogged filter nobody has found
        g.FoulingGrowth = 0.8;                      // and the hull has fouled unseen
        var blind = Plan(g, "hestholmen");
        Assert.Contains("at about 1.8 L/h. ", blind.Fuel);
        Assert.DoesNotContain("adds", blind.Fuel);
        int blindNeed = int.Parse(System.Text.RegularExpressions.Regex.Match(blind.Fuel!, @"needs about (\d+) litres").Groups[1].Value);

        // The passage burns what is true; the gauge shows it.
        Depart(g);
        double start = Litres(boat);
        Ok(g.Execute(new Motor()));
        double burned = start - Litres(boat);
        double truth = Burn(boat, 5, filter: true, fouling: 0.8);
        Assert.Equal(truth, burned, 6);
        Assert.True(burned > 1.3 * Burn(boat, 5), "the gauge fell faster than the plan said");
        Assert.True(blindNeed < 14 / 3.8 * truth);
    }

    [Fact]
    public void Once_the_player_has_seen_the_hull_and_found_the_filter_the_plan_names_both()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        g.FoulingGrowth = 0.8;
        Ok(g.Execute(new Look()));                  // she sees the growth at the waterline
        var seen = Plan(g, "hestholmen");
        Assert.Contains("at about 2.4 L/h (heavy fouling adds a third)", seen.Fuel);
        Assert.DoesNotContain("filter", seen.Fuel!);

        Ok(Walk.Do(g, "primary filter", c => new Inspect(c)));   // she finds the filter, then opens its bowl
        Ok(g.Execute(new CheckFilterBowl()));
        var both = Plan(g, "hestholmen");
        Assert.Contains("(heavy fouling adds a third; a clogged fuel filter adds a tenth)", both.Fuel);
        double expect = Burn(boat, 5, filter: true, fouling: 0.8);
        Assert.Contains($"at about {expect.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} L/h", both.Fuel);
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
    public void An_hour_idling_at_the_mooring_and_an_hour_motoring_burn_the_rate_of_the_load_with_the_undiscovered_filter_in_it()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        double idleStart = Litres(boat);
        for (int i = 0; i < 4; i++) Ok(g.Execute(new PumpBilge())); // four times 15 minutes with the engine running
        Assert.Equal(Burn(boat, null, filter: true), idleStart - Litres(boat), 6);

        Depart(g);
        double motorStart = Litres(boat);
        Ok(g.Execute(new Motor()));
        Assert.Equal(Burn(boat, 5, filter: true), motorStart - Litres(boat), 6);
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
        Assert.Equal(Burn(boat, null, filter: true), before - Litres(boat), 6);
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
        Depart(g);                                   // ten minutes idling: about 0.1 litre
        var first = Ok(g.Execute(new Motor()));      // about 2.1 litres gone, about 1 left
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
        Assert.Contains("In the tank: about 25 litres (a little over half a tank).", Text(tank));
        Ok(Walk.Do(g, "primary filter", c => new Inspect(c)));
        // The tired fuel system has water in the bowl; put it right so the bowl itself can be read.
        foreach (var id in new[] { "primary-filter", "fuel-tank" })
            foreach (var f in boat.Get(id).ActiveFaults.ToList()) f.Clear();
        Assert.Contains("Clear, pale diesel", Text(g.Execute(new CheckFilterBowl())));
        boat.Get("fuel-tank").State["fuelLitres"] = 0;
        Assert.Contains("The bowl is half air. Nothing is coming through from the tank.", Text(g.Execute(new CheckFilterBowl())));
    }

    // ------------------------------------------------------------------ night and the close calls

    /// <summary>An hour of light a day, 12:30 to 13:30: the preparations take until the middle of the afternoon, so she casts off in the dark.</summary>
    private static WeatherSeries ShortDays() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 1));

    /// <summary>Dark all day and fog all day at sea, so that a passage meets both in every hour.</summary>
    private static WeatherSeries DarkFog() =>
        WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, DaylightHours: 1, FogUntilHour: 24, Kind: FogKind.Sea));

    /// <summary>The boat as shipped with the close calls of the fog and of the night set (chance an hour, share a ferry reports).</summary>
    private static Boat CloseCallBoat(double night, double nightReport, double fog = 0, double fogReport = 0, bool withCalendar = false, string? economy = null)
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        var causes = node["closeCalls"]!["causes"]!;
        causes["night"]!["chancePerHour"] = night;
        causes["night"]!["reportChance"] = nightReport;
        causes["fog"]!["chancePerHour"] = fog;
        causes["fog"]!["reportChance"] = fogReport;
        return BoatLoader.Load(node.ToJsonString(), withCalendar, economy ?? TestEconomy.Text());
    }

    private static readonly string[] NightStarts = ["A searchlight comes", "A wall of lit windows", "A fishing boat's bow", "Something low and black"];
    private static readonly string[] FogStarts = ["A ferry's horn", "A wall of white steel", "A rock you never saw", "A moored fishing float"];

    private static List<string> Calls(IEnumerable<CommandResult> rs, string[] starts) =>
        rs.SelectMany(r => r.Narrative).Where(l => starts.Any(x => l.StartsWith(x, StringComparison.Ordinal))).ToList();

    /// <summary>From the mooring to the trip card, every result on the way.</summary>
    private static List<CommandResult> Passage(Game g)
    {
        Depart(g);
        var results = new List<CommandResult> { Ok(g.Execute(new Motor())) };
        for (int i = 0; i < 80 && results[^1].Observation is not TripResultView; i++) results.Add(Ok(g.Execute(new Continue())));
        Assert.IsType<TripResultView>(results[^1].Observation);
        return results;
    }

    private static int Hours(IEnumerable<CommandResult> rs) => rs.Count(r => r.Observation is LegView or TripResultView);

    private static List<LedgerEntry> Bills(Game g) =>
        ((CalendarView)g.Ask(new Calendar())).Money.Recent.Where(e => e.Kind == "bill").ToList();

    [Fact]
    public void There_is_no_patrol_and_no_fine_anywhere_in_the_data()
    {
        string boat = Sloop28Json(), economy = TestEconomy.Text();
        foreach (var text in new[] { boat, economy })
        {
            Assert.DoesNotContain("patrol", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("noLightsFine", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("patrolFine", text, StringComparison.OrdinalIgnoreCase);
        }
        // The running of her dark without lights is a risk now, never a fine: with the shipped data, no bill before any letter has come.
        var g = Afloat(ShortDays());
        int balance = Status(g).BalanceNok;
        var results = Passage(g);
        Assert.DoesNotContain(results.SelectMany(r => r.Alerts), a => a.Text.Contains("patrol", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(balance, Status(g).BalanceNok);
    }

    [Fact]
    public void Every_whole_hour_under_way_in_the_dark_without_working_lights_rolls_a_night_close_call_and_the_lights_fault_is_not_found_by_it()
    {
        var g = Afloat(ShortDays(), boat: CloseCallBoat(night: 1, nightReport: 0));
        var results = Passage(g);
        var calls = Calls(results, NightStarts);
        Assert.True(Hours(results) >= 2);
        Assert.Equal(Hours(results), calls.Count);                       // chance 1: one every whole hour under way
        Assert.All(calls, c => Assert.True(c.StartsWith("A fishing boat's bow", StringComparison.Ordinal) || c.StartsWith("Something low", StringComparison.Ordinal)));
        Assert.All(calls, c => Assert.Contains(Log(g).Entries, e => e.Text == c));
        Assert.DoesNotContain(calls, c => c.Contains("the harbour will hear"));   // nobody reported her
        Assert.Empty(Log(g).Entries.Where(e => e.Document is not null && e.Document.StartsWith("letter", StringComparison.Ordinal)));
    }

    [Fact]
    public void With_working_lights_or_by_day_no_night_close_call_is_rolled_even_at_a_chance_of_one()
    {
        var mended = Afloat(ShortDays(), boat: CloseCallBoat(1, 1), lightsFixed: true);
        Assert.Empty(Calls(Passage(mended), NightStarts));
        var day = Afloat(Clear(), boat: CloseCallBoat(1, 1));
        Assert.Empty(Calls(Passage(day), NightStarts));
    }

    [Fact]
    public void At_anchor_and_under_tow_in_the_dark_no_night_close_call_is_rolled()
    {
        var g = Afloat(ShortDays(), boat: CloseCallBoat(1, 1), anchorFixed: true);
        Depart(g);
        Ok(g.Execute(new Anchor()));
        var hour = Ok(g.Execute(new Continue()));
        Assert.Empty(Calls([hour], NightStarts));
        var towed = Afloat(ShortDays(), boat: CloseCallBoat(1, 1));
        Depart(towed);
        Assert.Empty(Calls([Ok(towed.Execute(new CallTow()))], NightStarts));   // the towboat carries her home: nobody is running dark
    }

    [Fact]
    public void In_a_dark_fog_hour_fog_and_night_each_roll_once_and_the_fog_calls_do_not_move_for_the_night_cause()
    {
        var both = Passage(Afloat(DarkFog(), boat: CloseCallBoat(night: 1, nightReport: 0, fog: 1, fogReport: 0), newImpeller: true));
        Assert.Equal(Hours(both), Calls(both, NightStarts).Count);
        Assert.Equal(Hours(both), Calls(both, FogStarts).Count);

        // The fog's own calls, with the night cause on and off, in the same world.
        List<string> Fog(double night) =>
            Calls(Passage(Afloat(DarkFog(), boat: CloseCallBoat(night, nightReport: 0.5, fog: 0.5, fogReport: 0.5), newImpeller: true, worldSeed: 9)), FogStarts);
        var off = Fog(0);
        Assert.NotEmpty(off);
        Assert.Equal(off, Fog(1));
    }

    [Fact]
    public void A_reported_night_call_marks_her_reputation_and_brings_a_letter_with_the_next_morning_post_billed_as_a_bill()
    {
        var g = Afloat(ShortDays(), boat: CloseCallBoat(night: 1, nightReport: 1, withCalendar: true));
        int before = g.ReputationPoints;
        int balance = Status(g).BalanceNok;
        Depart(g);
        var first = Ok(g.Execute(new Motor()));
        var call = Assert.Single(Calls([first], NightStarts));
        Assert.True(call.StartsWith("A searchlight", StringComparison.Ordinal) || call.StartsWith("A wall of lit", StringComparison.Ordinal));
        Assert.Contains("the harbour will hear of it", Text(first));
        Assert.Equal(before - 1, g.ReputationPoints);                      // the ferry's mark, the same as the fog's
        Assert.Equal(balance, Status(g).BalanceNok);                       // the story first: no bill at sea
        Assert.DoesNotContain(first.Alerts, a => a.Text.Contains("harbourmaster's office"));

        var results = new List<CommandResult> { first };
        for (int i = 0; i < 30 && results[^1].Observation is not TripResultView; i++) results.Add(Ok(g.Execute(new Continue())));
        int calls = Calls(results, NightStarts).Count(c => !c.StartsWith("A fishing", StringComparison.Ordinal) && !c.StartsWith("Something", StringComparison.Ordinal));
        Assert.Equal(Hours(results), calls);                               // report chance 1: every call a ferry's
        Assert.DoesNotContain(results.SelectMany(r => r.Alerts), a => a.Text.Contains("harbourmaster's office"));   // nothing came at sea

        // The morning post: the letters come at the midnight, each billed 2 000 through the ledger.
        int arrivedBalance = Status(g).BalanceNok;
        var wait = Ok(g.Execute(new WaitDays(1)));
        int letters = wait.Alerts.Count(a => a.Text.StartsWith("A letter from the harbourmaster's office: the ferry ", StringComparison.Ordinal) && a.Text.Contains("It bills 2000 NOK"));
        Assert.Equal(calls, letters);
        Assert.Equal(calls, Bills(g).Count(e => e.Nok == -2000 && e.Text == "The harbourmaster's letter: the cost of a ferry's report."));
        Assert.True(Status(g).BalanceNok <= arrivedBalance - 2000 * calls);
        var documents = Log(g).Entries.Where(e => e.Document is { } d && d.StartsWith("letter", StringComparison.Ordinal)).ToList();
        Assert.Equal(calls, documents.Count);
        Assert.StartsWith("Dear owner, the ferry ", documents[0].Text, StringComparison.Ordinal);
        Assert.Contains("an unlit sailing boat", documents[0].Text);
        Assert.Contains("2000 NOK", documents[0].Text);
        Assert.Contains(new[] { "Skarvøy", "Havørn", "Sildebris", "Kråkevær" }, f => documents[0].Text.Contains(f));
        Assert.Contains(Log(g).Entries, e => e.Text == "Found: Cracked navigation light lens.");   // the letter says she was unlit
    }

    [Fact]
    public void A_letter_is_billed_even_past_the_overdraft_and_is_never_refused()
    {
        var economy = TestEconomy.Edited(n =>
        {
            n["closeCalls"]!["letterNok"] = 60000;
            n["start"]!["startingBalanceNok"] = 1000;
        });
        var g = Afloat(ShortDays(), boat: CloseCallBoat(night: 1, nightReport: 1, withCalendar: true, economy: economy));
        Passage(g);
        Ok(g.Execute(new WaitDays(1)));
        Assert.True(Status(g).BalanceNok < -20000);   // beyond the overdraft limit: a bill the player did not choose
        Assert.Contains(Bills(g), e => e.Nok == -60000);
    }

    [Fact]
    public void A_reported_fog_call_brings_no_letter()
    {
        var g = Afloat(WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: 24, Kind: FogKind.Sea)),
            boat: CloseCallBoat(night: 0, nightReport: 0, fog: 1, fogReport: 1, withCalendar: true), lightsFixed: true, newImpeller: true);
        int balance = Status(g).BalanceNok;
        var results = Passage(g);
        Assert.NotEmpty(Calls(results, FogStarts));
        Ok(g.Execute(new WaitDays(2)));
        Assert.Equal(balance, Status(g).BalanceNok);
        Assert.Empty(Bills(g));
        Assert.DoesNotContain(Log(g).Entries, e => e.Document is not null && e.Document.StartsWith("letter", StringComparison.Ordinal));
    }

    [Fact]
    public void The_night_close_calls_and_their_ferries_are_the_same_for_the_same_world_and_differ_between_worlds()
    {
        string Run(int seed)
        {
            var g = Afloat(ShortDays(), boat: CloseCallBoat(0.5, 0.7, withCalendar: true), worldSeed: seed);
            var results = Passage(g);
            Ok(g.Execute(new WaitDays(1)));
            return string.Join("|", Calls(results, NightStarts)) + "#" + string.Join("|", Log(g).Entries.Where(e => e.Document is not null).Select(e => e.Text));
        }
        Assert.Equal(Run(7), Run(7));
        Assert.True(Enumerable.Range(1, 8).Select(Run).Distinct().Count() > 1);
    }

    // ------------------------------------------------------------------ fog

    /// <summary>Fog all day on day 101 only, and a clear day 100: she can cast off late on 100 and meet the fog after midnight.</summary>
    private static WeatherSeries FogTomorrow() =>
        WeatherSeries.From(d => new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: d >= 101 ? 24 : 0));

    [Fact]
    public void A_harbour_mist_day_is_planned_as_a_creep_out_for_the_first_hour_not_a_fog_all_morning()
    {
        var weather = WeatherSeries.From(d => new WeatherDay(0, 1, "southwest", 0, 12, 8, FogUntilHour: d >= 101 ? 10 : 0, Kind: d >= 101 ? FogKind.Harbour : FogKind.None));
        var plan = Plan(Afloat(weather), "hestholmen", 101);
        Assert.Contains("Mist in the harbour in the morning: without a plotter or radar you creep out at 2 knots for the first hour.", plan.Warnings!);
        Assert.DoesNotContain(plan.Warnings!, w => w.StartsWith("Fog forecast", StringComparison.Ordinal));
    }

    [Fact]
    public void Fog_in_the_harbour_does_not_refuse_the_departure_the_plan_names_it_a_risk()
    {
        var weather = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: 14));
        var g = Afloat(weather);
        Assert.Contains("Fog over the water.", Status(g).Weather);
        var plan = Plan(g, "hestholmen");
        Assert.Empty(plan.Blockers);
        Assert.Contains("Fog forecast for the morning: without a plotter or radar you creep at 2 knots and listen for ferries.", plan.Warnings!);
        Walk.ToPlace(g, "cockpit");
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
        boat.Get("fuel-tank").State["fuelLitres"] = 45;
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
        Assert.Contains(plan.Warnings!, w => w.StartsWith("Fog forecast for the day: without a plotter or radar you creep at 2 knots", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the plan

    [Fact]
    public void The_plan_says_the_fuel_the_gauge_shows_against_what_the_passage_needs()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = Afloat(Clear(), boat: boat);
        var plan = Plan(g, "hestholmen");
        Assert.Equal("About 25 litres aboard: some 14 hours of motoring at about 1.8 L/h. This passage needs about 5 litres.", plan.Fuel);
        Assert.DoesNotContain(plan.Warnings!, w => w.Contains("fuel", StringComparison.OrdinalIgnoreCase));

        boat.Get("fuel-tank").State["fuelLitres"] = 5;
        var thin = Plan(g, "hestholmen");
        Assert.Equal("About 5 litres aboard: some 3 hours of motoring at about 1.8 L/h. This passage needs about 5 litres.", thin.Fuel);
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
        Assert.Equal("She'll arrive after dark (the light goes at about 14:00), and you know the navigation lights have a problem: without them a ferry may not see her, and ferries report.", known);
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
        Assert.Contains($"This passage costs 1 vacation day (Monday). You have {BoatLoader.LoadSloop28().Calendar.VacationDaysPerYear} left.", monday.Warnings!);
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
        for (int i = 0; i < 40 && Status(g).VacationDaysLeft == boat.Calendar.VacationDaysPerYear; i++) Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Equal(boat.Calendar.VacationDaysPerYear - 1, Status(g).VacationDaysLeft);
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
        Assert.Equal("The club cruise to Kvitnes (Friday to Sunday, days 214 to 216) asks for anchor gear in order (no known problem); a bilge pump that works (no known problem).", plan.AtDestination[0]);
        Assert.Equal("An anchorage: no shop, no fuel.", plan.AtDestination[1]);
        Assert.DoesNotContain("seized", string.Join(" ", plan.AtDestination), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ the anchor

    [Fact]
    public void A_seized_anchor_chain_refuses_anchoring_at_sea_and_finds_the_fault()
    {
        var g = Afloat(Clear(), boat: WithSeizedChain());
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
        var g = Afloat(Clear(), startDay: 100, boat: WithSeizedChain());
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
    public void A_frozen_shackle_does_not_stop_her_anchoring_the_chain_still_runs()
    {
        var g = Afloat(Clear());   // as sold: rusted chain, a frozen shackle, a chain that still runs
        Depart(g);
        var yes = Ok(g.Execute(new Anchor()));
        Assert.Equal("at anchor", Trip(g).Mode);
        Assert.Empty(yes.NewlyDiscovered);
    }

    [Fact]
    public void Replacing_the_chain_lets_her_anchor()
    {
        var g = Afloat(Clear(), boat: WithSeizedChain());
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
