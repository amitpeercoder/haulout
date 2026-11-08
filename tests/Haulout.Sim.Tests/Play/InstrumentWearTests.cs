using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Systems;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// The plotter and the radar wear and fail (design note 010, second pass G, rule 3): a corroded plug on the plotter's power feed blanks the
/// screen in rain; the radar's scanner cable chafes through on the mast and the radar paints nothing. Both are rates by exposure that start
/// when the instrument is fitted, with the house's seeded spread; both are found by inspecting and put right by a cheap fix.
/// </summary>
public class InstrumentWearTests
{
    private const string PlugBlank = "The plotter screen has gone blank; it comes back when you jiggle the plug behind the binnacle, then goes again.";
    private const string RadarEmpty = "The radar sweeps, but the screen stays empty: not even the shore paints.";

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

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonNode UpgradeNode(JsonNode root, string id) =>
        root["upgrades"]!.AsArray().Single(u => u!["component"]!["id"]!.GetValue<string>() == id)!;

    /// <summary>The plotter's plug fault is there from the day she is fitted (no rate to wait for).</summary>
    private static void PresentPlug(JsonNode root) => UpgradeNode(root, "chartplotter")["component"]!["faults"]![0]!["dormant"] = false;

    private static void PresentCable(JsonNode root) => UpgradeNode(root, "radar")["also"]![0]!["faults"]![0]!["dormant"] = false;

    /// <summary>The shipped boat with no calendar, 60000 to start with, a close call every fogbound hour (none reported), and the edit.</summary>
    private static Boat Shipped(Action<JsonNode>? edit = null, double closeCallChance = 0)
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        node["closeCalls"]!["causes"]!["fog"]!["chancePerHour"] = closeCallChance;
        node["closeCalls"]!["causes"]!["fog"]!["reportChance"] = 0;
        node["water"]!["sinkLitres"] = 1e9;               // weeks of heavy rain in a test must not sink her: the rain is not what is under test
        edit?.Invoke(node);
        return BoatLoader.Load(node.ToJsonString(), withCalendar: false, TestEconomy.Edited(n => n["start"]!["startingBalanceNok"] = 60000));
    }

    private static WeatherDay Day(double rainMm = 0, int wind = 3, int fogUntil = 0) =>
        new(0, wind, "southwest", rainMm, 12, 8, FogUntilHour: fogUntil);

    /// <summary>Afloat at Skjærvik on day 100, the lights mended, the engine running, and each named upgrade bought and fitted.</summary>
    private static Game Fitted(Boat boat, WeatherSeries weather, int seed = Game.DefaultSeed, params string[] fitted)
    {
        var g = new Game(boat, 100, new FixedRandom(0.99), seed, weather);
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Do(g, "navigation lights", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Refit("companionway hatch")));   // going below slid it open: weeks of rain would sink her
        foreach (var item in fitted)
        {
            Ok(g.Execute(new Buy(item)));
            Walk.ToPlace(g, item.Contains("radar") ? "on-deck" : "cockpit");
            Ok(g.Execute(new Install(item)));
        }
        Walk.ToPlace(g, "cockpit");
        boat.Get("fuel-tank").State["fuelLitres"] = 60;
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));   // fitting shut the engine down
        return g;
    }

    private static void WaitUntilDay(Game g, int day)
    {
        while (Status(g).Day < day) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
    }

    /// <summary>Waits <paramref name="days"/> days at the mooring and is back at the tiller with the engine going (it overheats and dies at idle in a long wait).</summary>
    private static void Wait(Game g, int days)
    {
        for (int i = 0; i < days; i++) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
    }

    private static double Plug(Game g) => g.Values.Get(SystemValues.PlugWetness);
    private static double Chafe(Game g) => g.Values.Get(SystemValues.ScannerChafe);

    /// <summary>The first day the fault is among the component's live faults, waiting day by day from now; null: not by <paramref name="until"/>.</summary>
    private static int? FirstDayPresent(Game g, Boat boat, string component, int until)
    {
        while (Status(g).Day < until)
        {
            Ok(g.Execute(new WaitDays(1)));
            if (boat.Get(component).ActiveFaults.Any()) return Status(g).Day;
        }
        return null;
    }

    private static double Leg(CommandResult r, ref double position)
    {
        var leg = Assert.IsType<LegView>(r.Observation);
        double delta = leg.PositionNm - position;
        position = leg.PositionNm;
        return delta;
    }

    private static readonly string[] CloseCallStarts = ["A ferry's horn", "A wall of white steel", "A rock you never saw", "A moored fishing float"];

    private static List<string> CloseCalls(IEnumerable<CommandResult> rs) =>
        rs.SelectMany(r => r.Narrative).Where(l => CloseCallStarts.Any(s => l.StartsWith(s, StringComparison.Ordinal))).ToList();

    /// <summary>Under way from the mooring to the card; every result on the way.</summary>
    private static List<CommandResult> Passage(Game g)
    {
        Ok(g.Execute(new Depart("hestholmen")));
        var results = new List<CommandResult> { Ok(g.Execute(new Motor())) };
        for (int i = 0; i < 30 && results[^1].Observation is not TripResultView; i++) results.Add(Ok(g.Execute(new Continue())));
        Assert.IsType<TripResultView>(results[^1].Observation);
        return results;
    }

    // ------------------------------------------------------------------ the data

    [Fact]
    public void The_plug_fault_and_the_scanner_cable_are_data_dormant_until_their_rate_wakes_them()
    {
        var boat = BoatLoader.LoadSloop28();
        var rules = boat.Instruments;
        Assert.True(rules.PlotterPlug.RainPerDay > 0 && rules.PlotterPlug.SprayPerHourAtSea > 0);
        Assert.True(rules.ScannerCable.PerHourByForce.Count > 0 && rules.ScannerCable.UnderWayFactor > 1);

        var plotter = boat.TryGetUpgrade("chartplotter", out var p) ? p.Component : throw new InvalidOperationException();
        var plug = Assert.Single(plotter.Faults);
        Assert.True(plug.Dormant);
        Assert.Equal("rain", plug.FailsWhen);
        Assert.Equal(PlugBlank, plug.Symptom);
        var spawn = Assert.Single(plug.Spawn);
        Assert.Equal(SystemValues.PlugWetness, spawn.When);
        Assert.True(spawn.Spread);                       // the house's seeded spread
        Assert.True(spawn.Repeats);
        Assert.Null(plug.Progression);                   // no timer, no world draw

        Assert.True(boat.TryGetUpgrade("radar", out var radar));
        var cable = Assert.Single(radar.Also);
        Assert.Equal("scanner-cable", cable.Id);
        var chafed = Assert.Single(cable.Faults);
        Assert.True(chafed.Dormant);
        Assert.Null(chafed.FailsWhen);                   // it stops the radar always
        Assert.Equal(RadarEmpty, chafed.Symptom);
        Assert.Equal(SystemValues.ScannerChafe, Assert.Single(chafed.Spawn).When);

        // Neither is aboard before the upgrade is fitted; fitting the radar brings the cable with it.
        Assert.DoesNotContain(boat.Components, c => c.Id == "scanner-cable");
        Assert.DoesNotContain(boat.AllFaults, f => f.Id is "plotter-plug-corroded" or "scanner-cable-chafed");
        Assert.False(boat.TryGet("scanner-cable", out _));
    }

    [Fact]
    public void Fitting_the_radar_brings_the_scanner_cable_aboard_with_it_and_nothing_else_moves()
    {
        var boat = Shipped();
        var g = Fitted(boat, WeatherSeries.Constant(Day()), Game.DefaultSeed);
        int before = boat.Components.Count;
        Ok(g.Execute(new Buy("marine radar")));
        Walk.ToPlace(g, "on-deck");
        Ok(g.Execute(new Install("marine radar")));
        Assert.Equal(before + 2, boat.Components.Count);
        Assert.Equal("scanner-cable", boat.Components[^1].Id);
        Assert.Same(boat.Components[^1], boat.Get("scanner-cable"));
        // The cable is a part, not an upgrade: not in the upgrade list, and not a line of the broker's letter.
        Assert.DoesNotContain(((UpgradesView)g.Ask(new Upgrades())).Items, i => i.Name.Contains("cable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(((SaleView)g.Ask(new Sale())).Letter!.Lines, l => l.Text.Contains("cable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_boat_file_with_an_unknown_failsWhen_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Shipped(n => UpgradeNode(n, "chartplotter")["component"]!["faults"]![0]!["failsWhen"] = "snow"));
        Assert.Contains("failsWhen", ex.Message);
    }

    // ------------------------------------------------------------------ the rates

    [Fact]
    public void The_plug_counts_rain_by_the_exposure_rain_term_and_only_from_the_day_the_plotter_is_fitted()
    {
        var boat = Shipped();
        var wet = WeatherSeries.Constant(Day(rainMm: 10));
        var g = Fitted(boat, wet, Game.DefaultSeed);
        Assert.Equal(0, Plug(g));
        for (int i = 0; i < 30; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(0, Plug(g));                         // 30 days of rain, and no plotter: nothing counts

        Ok(g.Execute(new Buy("chart plotter")));
        Ok(g.Execute(new Install("chart plotter")));
        double start = Plug(g);
        Assert.InRange(start, 0, 0.05);                   // the install's own hours
        for (int i = 0; i < 10; i++) Ok(g.Execute(new WaitDays(1)));
        // Ten days at the rain term of a 10 mm day (0.5 in the data) times the data's rain per day.
        double expected = boat.Instruments.PlotterPlug.RainPerDay * 0.5 * 10;
        Assert.InRange(Plug(g) - start, expected - 0.1, expected + 0.1);
    }

    [Fact]
    public void A_light_rain_wets_the_plug_in_proportion_and_a_dry_day_does_not_wet_it_at_all()
    {
        double Wetness(double rainMm)
        {
            var boat = Shipped();
            var g = Fitted(boat, WeatherSeries.Constant(Day(rainMm: rainMm)), Game.DefaultSeed, "chart plotter");
            double start = Plug(g);
            for (int i = 0; i < 6; i++) Ok(g.Execute(new WaitDays(1)));
            return Plug(g) - start;
        }
        double dry = Wetness(0), light = Wetness(2.5), full = Wetness(10), beyond = Wetness(30);
        Assert.Equal(0, dry, 6);
        Assert.Equal(full / 4, light, 2);                 // the term is proportional up to the cap
        Assert.Equal(full, beyond, 6);                    // and no more than the cap
    }

    [Fact]
    public void Spray_wets_the_plug_only_at_sea_in_a_wind_from_the_data_and_a_dry_mooring_does_not()
    {
        var boat = Shipped();
        int force = boat.Instruments.PlotterPlug.SprayFromForce;
        var g = Fitted(boat, WeatherSeries.Constant(Day(wind: force)), Game.DefaultSeed, "chart plotter");
        double moored = Plug(g);
        Wait(g, 3);
        Assert.Equal(moored, Plug(g), 6);                 // dry, in port: nothing

        Ok(g.Execute(new Depart("hestholmen")));
        double before = Plug(g);
        var hour = Ok(g.Execute(new Motor()));
        Assert.Equal(boat.Instruments.PlotterPlug.SprayPerHourAtSea * hour.MinutesTaken / 60.0, Plug(g) - before, 2);

        var calm = Shipped();
        var c = Fitted(calm, WeatherSeries.Constant(Day(wind: force - 1)), Game.DefaultSeed, "chart plotter");
        Ok(c.Execute(new Depart("hestholmen")));
        double calmBefore = Plug(c);
        Ok(c.Execute(new Motor()));
        Assert.Equal(calmBefore, Plug(c), 6);             // below the data's force: no spray
    }

    [Fact]
    public void The_cable_chafes_by_the_hour_in_the_wind_the_data_names_and_more_at_sea_and_not_on_the_hard()
    {
        var rules = Shipped().Instruments.ScannerCable;
        int force = rules.FromForce + 1;
        double perHour = rules.PerHourByForce[1];

        // Afloat on the mooring, a day at a time.
        var boat = Shipped();
        var g = Fitted(boat, WeatherSeries.Constant(Day(wind: force)), Game.DefaultSeed, "marine radar");
        double start = Chafe(g);
        Wait(g, 3);
        Assert.Equal(perHour * 24 * 3, Chafe(g) - start, 1);

        // Under way it is faster by the data's factor.
        Ok(g.Execute(new Depart("hestholmen")));
        double before = Chafe(g);
        var hour = Ok(g.Execute(new Motor()));
        Assert.Equal(perHour * rules.UnderWayFactor * hour.MinutesTaken / 60.0, Chafe(g) - before, 2);

        // A lighter wind than the data's: nothing.
        var light = Shipped();
        var l = Fitted(light, WeatherSeries.Constant(Day(wind: rules.FromForce - 1)), Game.DefaultSeed, "marine radar");
        double lStart = Chafe(l);
        for (int i = 0; i < 3; i++) Ok(l.Execute(new WaitDays(1)));
        Assert.Equal(lStart, Chafe(l), 6);
    }

    [Fact]
    public void Nothing_counts_before_the_instrument_is_fitted_and_a_boat_without_them_sees_no_wear_and_no_symptom()
    {
        var boat = Shipped();
        var g = Fitted(boat, WeatherSeries.Constant(Day(rainMm: 10, wind: 6)), Game.DefaultSeed);
        for (int i = 0; i < 20; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(0, Plug(g));
        Assert.Equal(0, Chafe(g));
        Walk.ToPlace(g, "cockpit");
        var look = Ok(g.Execute(new Look()));
        Assert.DoesNotContain("screen has gone blank", Text(look));
        Assert.DoesNotContain("stays empty", Text(look));
        Assert.DoesNotContain(boat.AllFaults, f => f.Id is "plotter-plug-corroded" or "scanner-cable-chafed");
    }

    // ------------------------------------------------------------------ waking

    [Fact]
    public void The_plug_wakes_in_a_wet_spell_not_in_a_dry_one_and_on_a_day_that_depends_on_the_world_seed_alone()
    {
        int? Wake(int seed, double rain)
        {
            var boat = Shipped();
            var g = Fitted(boat, WeatherSeries.Constant(Day(rainMm: rain)), seed, "chart plotter");
            return FirstDayPresent(g, boat, "chartplotter", 260);
        }
        Assert.Null(Wake(1, 0));                          // a dry season: the plug never gets wet enough
        var days = Enumerable.Range(1, 5).Select(seed => Wake(seed, 10)).ToList();
        Assert.All(days, d => Assert.NotNull(d));
        Assert.True(days.Distinct().Count() > 1, string.Join(",", days));       // seeded spread: not the same day for every house
        Assert.Equal(days, Enumerable.Range(1, 5).Select(seed => Wake(seed, 10)).ToList());   // replayable
        // At 0.5 a day against a threshold of 7 times the spread (0.6 to 1.6): between 8 and 23 days after the fit.
        Assert.All(days, d => Assert.InRange(d!.Value, 100 + 7, 100 + 24));
    }

    [Fact]
    public void The_cable_wakes_in_a_windy_spell_not_in_a_calm_one_on_a_day_that_depends_on_the_world_seed_alone()
    {
        int? Wake(int seed, int wind)
        {
            var boat = Shipped();
            var g = Fitted(boat, WeatherSeries.Constant(Day(wind: wind)), seed, "marine radar");
            return FirstDayPresent(g, boat, "scanner-cable", 260);
        }
        Assert.Null(Wake(1, 3));
        var days = Enumerable.Range(1, 5).Select(seed => Wake(seed, 6)).ToList();
        Assert.All(days, d => Assert.NotNull(d));
        Assert.True(days.Distinct().Count() > 1, string.Join(",", days));
        Assert.Equal(days, Enumerable.Range(1, 5).Select(seed => Wake(seed, 6)).ToList());
    }

    [Fact]
    public void A_woken_fault_is_silent_until_found_and_then_tells_its_cause()
    {
        var boat = Shipped();
        var g = Fitted(boat, WeatherSeries.Constant(Day(rainMm: 10)), Game.DefaultSeed, "chart plotter");
        Assert.NotNull(FirstDayPresent(g, boat, "chartplotter", 260));
        Walk.ToPlace(g, "cockpit");
        var r = Ok(g.Execute(new Inspect("plotter")));
        Assert.Contains("furred with green corrosion", Text(r));
        Assert.Contains("The plotter's power plug has corroded. It came on from the cockpit rain and the spray at sea", Text(r));
        var found = Assert.Single(r.NewlyDiscovered);
        Assert.Equal("Corroded plotter plug", found.Name);
    }

    // ------------------------------------------------------------------ the symptoms

    [Fact]
    public void A_blank_screen_in_rain_shows_on_a_look_and_in_the_hour_and_a_dry_day_shows_nothing()
    {
        var boat = Shipped(PresentPlug);
        var wet = Fitted(boat, WeatherSeries.Constant(Day(rainMm: 5)), Game.DefaultSeed, "chart plotter");
        Assert.Contains(PlugBlank, Text(Ok(wet.Execute(new Look()))));

        var dryBoat = Shipped(PresentPlug);
        var dry = Fitted(dryBoat, WeatherSeries.Constant(Day()), Game.DefaultSeed, "chart plotter");
        Assert.DoesNotContain("screen has gone blank", Text(Ok(dry.Execute(new Look()))));
        // The fault is there all the same: inspecting finds it on a dry day.
        Assert.Contains("green corrosion", Text(Ok(dry.Execute(new Inspect("plotter")))));
    }

    [Fact]
    public void The_hour_line_under_way_says_the_screen_is_blank_once_in_a_passage_and_only_in_rain()
    {
        var wet = Fitted(Shipped(PresentPlug), WeatherSeries.Constant(Day(rainMm: 5)), Game.DefaultSeed, "chart plotter");
        var results = Passage(wet);
        Assert.True(results.Count > 2);
        Assert.Contains(PlugBlank, Text(results[0]));
        Assert.All(results.Skip(1), r => Assert.DoesNotContain(PlugBlank, Text(r)));          // told once
        Assert.Contains(((LogView)wet.Ask(new Logbook())).Entries, e => e.Text.Contains(PlugBlank));

        var dry = Fitted(Shipped(PresentPlug), WeatherSeries.Constant(Day()), Game.DefaultSeed, "chart plotter");
        Assert.All(Passage(dry), r => Assert.DoesNotContain("plotter screen", Text(r)));
    }

    [Fact]
    public void A_radar_that_paints_nothing_shows_from_the_cockpit_and_on_deck_and_in_the_hour_in_any_weather()
    {
        var g = Fitted(Shipped(PresentCable), WeatherSeries.Constant(Day()), Game.DefaultSeed, "marine radar");
        Assert.Contains(RadarEmpty, Text(Ok(g.Execute(new Look()))));                        // the cockpit
        Walk.ToPlace(g, "on-deck");
        Assert.Contains(RadarEmpty, Text(Ok(g.Execute(new Look()))));
        Walk.ToPlace(g, "cockpit");
        var results = Passage(g);
        Assert.Contains(RadarEmpty, Text(results[0]));
        Assert.All(results.Skip(1), r => Assert.DoesNotContain(RadarEmpty, Text(r)));

        // A radar with a sound cable says nothing.
        var sound = Fitted(Shipped(), WeatherSeries.Constant(Day()), Game.DefaultSeed, "marine radar");
        Assert.DoesNotContain("stays empty", Text(Ok(sound.Execute(new Look()))));
    }

    [Fact]
    public void Inspecting_the_scanner_cable_finds_the_sheath_worn_through_at_the_spreader()
    {
        var g = Fitted(Shipped(PresentCable), WeatherSeries.Constant(Day()), Game.DefaultSeed, "marine radar");
        Walk.ToPlace(g, "on-deck");
        var r = Ok(g.Execute(new Inspect("scanner cable")));
        Assert.Contains("the outer sheath is worn through and the braid shows", Text(r));
        Assert.Equal("Chafed scanner cable", Assert.Single(r.NewlyDiscovered).Name);
        // The cable is seen from the cockpit too (inspecting needs only the eye) but cannot be touched from it: a new run is made on deck.
        Walk.ToPlace(g, "cockpit");
        Assert.True(g.Execute(new Inspect("scanner cable")).Accepted);
        Assert.False(g.Execute(new Replace("scanner cable")).Accepted);
    }

    // ------------------------------------------------------------------ the fixes

    [Fact]
    public void Cleaning_the_plug_is_the_fix_for_nothing_and_half_an_hour_and_the_screen_works_in_rain_again()
    {
        var boat = Shipped(PresentPlug);
        var g = Fitted(boat, WeatherSeries.Constant(Day(rainMm: 5)), Game.DefaultSeed, "chart plotter");
        Assert.Contains(PlugBlank, Text(Ok(g.Execute(new Look()))));
        Ok(g.Execute(new Inspect("plotter")));
        int balance = Status(g).BalanceNok;
        var clean = Ok(g.Execute(new Clean("plotter")));
        Assert.Equal(0, clean.CostNok);
        Assert.Equal(30, clean.MinutesTaken);
        Assert.Contains("scrape the green off the pins", Text(clean));
        Assert.Equal(balance, Status(g).BalanceNok);
        Assert.DoesNotContain("screen has gone blank", Text(Ok(g.Execute(new Look()))));
        Assert.Empty(boat.Get("chartplotter").ActiveFaults);
        // Replacing the plotter would also do, at the plotter's price (not the way anyone would choose).
        var other = Fitted(Shipped(PresentPlug), WeatherSeries.Constant(Day(rainMm: 5)), Game.DefaultSeed, "chart plotter");
        Assert.Equal(9000, Ok(other.Execute(new Replace("plotter"))).CostNok);
    }

    [Fact]
    public void A_fault_that_woke_by_its_rate_and_was_put_right_leaves_a_dormant_copy_that_counts_from_then()
    {
        // The plug: wake by rain, clean, and the next wet days wet it again from the baseline of the clean (not from the day of the fit).
        var boat = Shipped();
        var g = Fitted(boat, WeatherSeries.Constant(Day(rainMm: 10)), Game.DefaultSeed, "chart plotter");
        Assert.NotNull(FirstDayPresent(g, boat, "chartplotter", 260));
        Wait(g, 0);
        Ok(g.Execute(new Clean("plotter")));
        Wait(g, 1);
        var plotter = boat.Get("chartplotter");
        Assert.Equal(2, plotter.Faults.Count);
        Assert.True(plotter.Faults[1].Dormant);
        Assert.Empty(plotter.ActiveFaults);
        Assert.NotNull(FirstDayPresent(g, boat, "chartplotter", 400));      // and it can come back

        var cableBoat = Shipped();
        var r = Fitted(cableBoat, WeatherSeries.Constant(Day(wind: 6)), Game.DefaultSeed, "marine radar");
        Assert.NotNull(FirstDayPresent(r, cableBoat, "scanner-cable", 260));
        Walk.ToPlace(r, "on-deck");
        Ok(r.Execute(new Replace("scanner cable")));
        Wait(r, 1);
        Assert.Equal(2, cableBoat.Get("scanner-cable").Faults.Count);
        Assert.Empty(cableBoat.Get("scanner-cable").ActiveFaults);
        Assert.NotNull(FirstDayPresent(r, cableBoat, "scanner-cable", 400));
    }

    [Fact]
    public void A_new_cable_run_costs_the_data_price_and_two_hours_and_the_radar_paints_again()
    {
        var boat = Shipped(PresentCable);
        var g = Fitted(boat, WeatherSeries.Constant(Day()), Game.DefaultSeed, "marine radar");
        Walk.ToPlace(g, "on-deck");
        Ok(g.Execute(new Inspect("scanner cable")));
        var cable = boat.Get("scanner-cable");
        Assert.Equal(700, cable.ReplaceCostNok);
        Assert.Equal(700, cable.Faults[0].Diy!.PartsNok);
        Assert.Equal(120, cable.Faults[0].Diy!.Minutes);
        Assert.True(cable.ReplaceCostNok < 24000 / 20);                  // well below the radar's price
        int balance = Status(g).BalanceNok;
        var r = Ok(g.Execute(new Replace("scanner cable")));
        Assert.Equal(700, r.CostNok);
        Assert.Equal(120, r.MinutesTaken);
        Assert.Equal(balance - 700, Status(g).BalanceNok);
        Assert.Contains("lead a new one down from the radome", Text(r));
        Assert.Empty(cable.ActiveFaults);
        Walk.ToPlace(g, "cockpit");
        Assert.DoesNotContain("stays empty", Text(Ok(g.Execute(new Look()))));
    }

    [Fact]
    public void The_yard_prices_for_both_fixes_are_in_the_data_well_below_the_instruments()
    {
        var boat = BoatLoader.LoadSloop28();
        var plug = boat.TryGetUpgrade("chartplotter", out var p) ? p.Component.Faults[0] : throw new InvalidOperationException();
        Assert.Equal(400, plug.Yard!.Nok);
        Assert.Equal(0, plug.Diy!.PartsNok);
        Assert.Equal(PlayerAction.Clean, Assert.Single(plug.FixedBy));
        var cable = boat.TryGetUpgrade("radar", out var r) ? r.Also[0].Faults[0] : throw new InvalidOperationException();
        Assert.Equal(1800, cable.Yard!.Nok);
        Assert.Equal(PlayerAction.Replace, Assert.Single(cable.FixedBy));
        Assert.Equal(9000 + 24000, boat.Chandlery.Where(i => i.Upgrade is "chartplotter" or "radar").Sum(i => i.PriceNok));   // prices as built
    }

    // ------------------------------------------------------------------ fog follows the truth

    private static WeatherSeries FogFrom102(double rainMm) =>
        WeatherSeries.From(d => Day(rainMm: d >= 102 ? rainMm : 0, fogUntil: d >= 102 ? 24 : 0));

    private static Game InFog(Boat boat, double rainMm, params string[] fitted)
    {
        var g = Fitted(boat, FogFrom102(rainMm), Game.DefaultSeed, fitted);
        WaitUntilDay(g, 102);
        return g;
    }

    private static double FirstHour(Game g)
    {
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Ok(g.Execute(new Depart("hestholmen")));
        double position = 0;
        return Leg(Ok(g.Execute(new Motor())), ref position);
    }

    [Fact]
    public void A_blank_plotter_in_rain_in_fog_gives_the_two_knot_cap_and_close_calls_and_a_dry_day_gives_three_and_a_half()
    {
        var rain = InFog(Shipped(PresentPlug, closeCallChance: 1), 5, "chart plotter");
        Assert.Equal(2.0, FirstHour(rain), 6);
        var results = Passage(InFog(Shipped(PresentPlug, closeCallChance: 1), 5, "chart plotter"));
        Assert.True(CloseCalls(results).Count >= 2);                       // a roll every whole hour, as with no plotter
        Assert.Contains("creep along at 2 knots", string.Join(" ", results.Select(Text)));

        var dry = InFog(Shipped(PresentPlug, closeCallChance: 1), 0, "chart plotter");
        Assert.Equal(3.5, FirstHour(dry), 6);
        var dryResults = Passage(InFog(Shipped(PresentPlug, closeCallChance: 1), 0, "chart plotter"));
        Assert.Empty(CloseCalls(dryResults));                              // she keeps to the marked route
        Assert.Contains("creep along at 3.5 knots", string.Join(" ", dryResults.Select(Text)));
    }

    [Fact]
    public void A_sound_plotter_in_rain_in_fog_still_gives_three_and_a_half_knots()
    {
        var g = InFog(Shipped(closeCallChance: 1), 5, "chart plotter");
        Assert.Equal(3.5, FirstHour(g), 6);
    }

    [Fact]
    public void A_chafed_radar_gives_no_fog_benefit_but_a_plotter_beside_it_still_does()
    {
        var radarOnly = InFog(Shipped(PresentCable, closeCallChance: 1), 0, "marine radar");
        Assert.Equal(2.0, FirstHour(radarOnly), 6);                         // as with no instrument at all
        Assert.True(CloseCalls(Passage(InFog(Shipped(PresentCable, closeCallChance: 1), 0, "marine radar"))).Count >= 2);

        var both = InFog(Shipped(PresentCable, closeCallChance: 1), 0, "marine radar", "chart plotter");
        Assert.Equal(3.5, FirstHour(both), 6);

        var sound = InFog(Shipped(closeCallChance: 1), 0, "marine radar");
        Assert.InRange(FirstHour(sound), 4.5, 5.0);                         // uncapped
    }

    [Fact]
    public void A_radar_with_a_blank_plotter_beside_it_is_uncapped_and_rolls_no_close_calls()
    {
        var g = InFog(Shipped(n => PresentPlug(n), closeCallChance: 1), 5, "marine radar", "chart plotter");
        Assert.InRange(FirstHour(g), 4.5, 5.0);
        var results = Passage(InFog(Shipped(PresentPlug, closeCallChance: 1), 5, "marine radar", "chart plotter"));
        Assert.Empty(CloseCalls(results));
    }

    [Fact]
    public void The_fix_restores_the_fog_benefit()
    {
        var g = InFog(Shipped(PresentPlug, closeCallChance: 1), 5, "chart plotter");
        Ok(g.Execute(new Clean("plotter")));
        Assert.Equal(3.5, FirstHour(g), 6);

        var r = InFog(Shipped(PresentCable, closeCallChance: 1), 0, "marine radar");
        Walk.ToPlace(r, "on-deck");
        Ok(r.Execute(new Replace("scanner cable")));
        Walk.ToPlace(r, "cockpit");
        Assert.InRange(FirstHour(r), 4.5, 5.0);
    }

    // ------------------------------------------------------------------ the plan states what the player knows

    [Fact]
    public void The_plan_assumes_the_plotter_works_until_the_fault_is_found_and_then_only_in_rain()
    {
        TripPlanView Plan(Game g) => (TripPlanView)g.Ask(new PlanTrip("hestholmen"));
        bool FogWarned(TripPlanView p) => p.Warnings!.Any(w => w.StartsWith("Fog forecast", StringComparison.Ordinal));

        var g = InFog(Shipped(PresentPlug), 5, "chart plotter");
        Assert.False(FogWarned(Plan(g)));                 // the fault is there and unknown: the plan assumes a plotter that works
        Assert.Equal(2.0, FirstHour(g), 6);               // and the truth shows at sea
        var found = InFog(Shipped(PresentPlug), 5, "chart plotter");
        Ok(found.Execute(new Inspect("plotter")));
        Assert.True(FogWarned(Plan(found)));              // found: in rain it is no plotter, and the plan says what that costs

        var dryFound = InFog(Shipped(PresentPlug), 0, "chart plotter");
        Ok(dryFound.Execute(new Inspect("plotter")));
        Assert.False(FogWarned(Plan(dryFound)));          // on a dry day it works

        // The radar's cable: unknown, the plan assumes a radar; known, it assumes none.
        var radar = InFog(Shipped(PresentCable), 0, "marine radar");
        Assert.False(FogWarned(Plan(radar)));
        Walk.ToPlace(radar, "on-deck");
        Ok(radar.Execute(new Inspect("scanner cable")));
        Walk.ToPlace(radar, "cockpit");
        Assert.True(FogWarned(Plan(radar)));
    }

    // ------------------------------------------------------------------ prices and values as built

    [Fact]
    public void The_broker_values_the_instruments_as_built_with_or_without_a_fault_and_the_cable_adds_nothing()
    {
        ValuationLine[] Lines(Boat boat)
        {
            var g = Fitted(boat, WeatherSeries.Constant(Day()), Game.DefaultSeed, "chart plotter", "marine radar");
            return ((SaleView)g.Ask(new Sale())).Letter!.Lines.ToArray();
        }
        foreach (var lines in new[] { Lines(Shipped()), Lines(Shipped(n => { PresentPlug(n); PresentCable(n); })) })
        {
            Assert.Contains(lines, l => l.Text == "Chart plotter, fitted this season" && l.Nok == 5000);
            Assert.Contains(lines, l => l.Text == "Marine radar, fitted this season" && l.Nok == 12000);
            Assert.DoesNotContain(lines, l => l.Text.Contains("cable", StringComparison.OrdinalIgnoreCase));
        }
    }
}
