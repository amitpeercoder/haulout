using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Systems;
using Haulout.Sim.Weather;
using Xunit.Abstractions;

namespace Haulout.Sim.Tests.Play;

/// <summary>The winter layup, the spawns that follow from skipping it, WaitUntilSpring and the year roll-over (design note 008, step 2).</summary>
public class LayupTests(ITestOutputHelper output)
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string PumpCracked = "raw-water-pump-cracked";
    private const string ExchangerCracked = "heat-exchanger-cracked";
    private const string FuelWater = "water-in-fuel-condensation";
    private const string BatteryDead = "battery-dead";
    private const string SprayhoodTorn = "sprayhood-torn";
    private static readonly string[] WinterFaults = [PumpCracked, ExchangerCracked, FuelWater, BatteryDead, SprayhoodTorn];

    // ------------------------------------------------------------------ builders and small helpers

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static CommandResult No(CommandResult r, string text)
    {
        Assert.False(r.Accepted);
        Assert.Equal(text, Assert.Single(r.Narrative));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static SeasonView Season(Game g) => (SeasonView)g.Ask(new Season());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());

    private static Fault FaultOf(Boat boat, string id) => boat.AllFaults.Single(f => f.Id == id);
    private static double Value(Game g, string name) => g.Values.Get(name);

    private static WeatherDay CalmDay => new(0, 3, "southwest", 0, 6, 2);

    /// <summary>Calm until the lift, then the given winter weather every day, calm in the wind.</summary>
    private static WeatherSeries Winter(double rainMm, double tempC, double nightC, int fromDay = 262) =>
        WeatherSeries.From(d => d < fromDay ? CalmDay : new WeatherDay(0, 3, "north", rainMm, tempC, nightC));

    /// <summary>Hard frost, snow on the deck: every winter count grows every day.</summary>
    private static WeatherSeries Harsh() => Winter(3, -3, -6);

    /// <summary>Mild and dry: no count grows.</summary>
    private static WeatherSeries Mild() => Winter(0, 8, 7);

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Boat BoatWithBalance(int nok) =>
        BoatLoader.Load(Sloop28Json().Replace("\"startingBalanceNok\": 25000", $"\"startingBalanceNok\": {nok}"), withCalendar: false);

    private static void Prepare(Game g)
    {
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
    }

    private static void WaitTo(Game g, int day)
    {
        while (Status(g).Day < day) Ok(g.Execute(new WaitDays(Math.Min(60, day - Status(g).Day))));
    }

    /// <summary>Waits <paramref name="days"/> in chunks of at most sixty (the facade's limit).</summary>
    private static void Wait(Game g, int days)
    {
        while (days > 0)
        {
            int chunk = Math.Min(60, days);
            Ok(g.Execute(new WaitDays(chunk)));
            days -= chunk;
        }
    }

    private static void Try(Func<CommandResult> act, Game g, int days = 12)
    {
        for (int i = 0; i < days; i++)
        {
            var r = act();
            if (r.Accepted) return;
            if (!string.Join(" ", r.Narrative).Contains("won't", StringComparison.Ordinal)) Assert.Fail(string.Join(" ", r.Narrative));
            Ok(g.Execute(new WaitDays(1)));
        }
        Assert.Fail("The wind never dropped.");
    }

    /// <summary>
    /// Launched on the hard on <paramref name="launchDay"/>, hauled out on the booked <paramref name="liftDay"/> (booked from the hard on day 250,
    /// so every seed has the slot): laid up for the winter, in the cockpit, nothing done yet.
    /// </summary>
    private static Game LaidUp(WeatherSeries? weather = null, int liftDay = 262, int seed = Game.DefaultSeed, Boat? boat = null, int launchDay = 252)
    {
        var g = new Game(boat ?? BoatLoader.LoadSloop28(), 250, new FixedRandom(0.99), seed, weather ?? Mild());
        Prepare(g);
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c))); // so the old drain hose does not call her owner back early
        Ok(g.Execute(new BookHaulout(liftDay)));
        WaitTo(g, launchDay);
        Ok(Walk.Start(g));
        Try(() => g.Execute(new Launch()), g);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        if (Status(g).Day < liftDay) WaitTo(g, liftDay);
        Walk.ToPlace(g, "cockpit");
        Try(() =>
        {
            if (!Status(g).EngineRunning) Ok(Walk.Start(g));
            return g.Execute(new HaulOut());
        }, g);
        Assert.True(Season(g).LaidUp);
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    /// <summary>Laid up with exactly <paramref name="left"/> NOK of the balance still to spend.</summary>
    private static Game LaidUpWithLeft(int left, WeatherSeries? weather = null)
    {
        int spent = Status(LaidUp(weather, boat: BoatLoader.LoadSloop28(withCalendar: false))).MoneySpentNok;
        return LaidUp(weather, boat: BoatWithBalance(spent + left));
    }

    /// <summary>This winter's battery draw factor (salt 8, rolled from the day of the autumn lift): what the layup draw is multiplied by.</summary>
    private static double DrawSpread(Boat boat, int liftDay = 262, int seed = Game.DefaultSeed) =>
        boat.Rates.SpreadLo + new SeededRandom(Game.SeasonSeed(seed, 8, liftDay, 0)).NextDouble() * (boat.Rates.SpreadHi - boat.Rates.SpreadLo);

    private static CommandResult Winterise(Game g) => Walk.Do(g, "raw-water-pump", _ => new WinteriseEngine());
    private static CommandResult BatteryAshoreAt(Game g) { Walk.Near(g, "start battery"); return g.Execute(new BatteryAshore()); }
    private static CommandResult BatteryAboardAt(Game g) { Walk.Near(g, "start battery"); return g.Execute(new BatteryAboard()); }
    private static CommandResult Cover(Game g) { Walk.ToPlace(g, "on-deck"); return g.Execute(new FitWinterCover()); }
    private static CommandResult Oil(Game g) => Walk.Do(g, "engine oil", c => new Replace(c));

    private static void DoAllFiveTasks(Game g)
    {
        Ok(Winterise(g));
        Ok(g.Execute(new FillTank()));
        Ok(Oil(g));
        Ok(BatteryAshoreAt(g));
        Ok(Cover(g));
    }

    // ------------------------------------------------------------------ the spawn mechanism

    [Fact]
    public void The_stable_hash_is_a_fixed_function_of_the_text()
    {
        Assert.Equal(unchecked((int)0xE40C292C), Game.StableHash("a"));
        Assert.Equal(Game.StableHash(PumpCracked), Game.StableHash(PumpCracked));
        Assert.NotEqual(Game.StableHash(PumpCracked), Game.StableHash(PumpCracked + "#2"));
        Assert.NotEqual(Game.StableHash(PumpCracked), Game.StableHash(ExchangerCracked));
    }

    [Fact]
    public void Only_the_five_winter_faults_spread_repeat_and_tell_a_cause()
    {
        var boat = BoatLoader.LoadSloop28();
        foreach (var f in boat.AllFaults.Where(f => f.Spawn.Count > 0))
        {
            bool winter = WinterFaults.Contains(f.Id);
            Assert.Equal(winter, f.Spawn.All(r => r.Spread && r.Repeats && r.Cause is { Length: > 0 }));
            if (!winter) Assert.All(f.Spawn, r => Assert.False(r.Spread || r.Repeats || r.Cause is not null));
            Assert.True(f.Dormant || !winter);
        }
        Assert.Equal(5, WinterFaults.Count(id => FaultOf(boat, id).Spawn.Count == 1));
    }

    [Fact]
    public void A_winter_fault_wakes_at_its_threshold_times_a_seeded_spread_not_on_a_fixed_count()
    {
        int FrostNightsToCrack(int seed)
        {
            var boat = BoatLoader.LoadSloop28();
            var g = LaidUp(Winter(0, 0, -6), seed: seed, boat: boat);
            int lift = Status(g).Day;
            var pump = FaultOf(boat, PumpCracked);
            for (int night = 1; night <= 6 && !pump.Spawned; night++) Ok(g.Execute(new WaitDays(1)));
            Assert.True(pump.Spawned);
            return pump.ActiveSinceDay!.Value - lift;
        }

        var days = Enumerable.Range(1, 12).Select(FrostNightsToCrack).ToList();
        // 14 frost hours a night against 24 hours times a spread of 0.6 to 1.6: two or three nights.
        Assert.All(days, d => Assert.InRange(d, 2, 3));
        Assert.True(days.Distinct().Count() > 1, "every seed cracked on the same night");
        Assert.Equal(days, Enumerable.Range(1, 12).Select(FrostNightsToCrack).ToList());
    }

    // ------------------------------------------------------------------ the tasks: happy paths and refusals

    [Fact]
    public void Winterising_drains_the_raw_water_side_and_ticks_the_task()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(boat: boat);
        Assert.Equal(1, boat.Get("raw-water-pump").State["waterHeld"]);
        Assert.False(Season(g).Tasks.Single(t => t.Name == "Winterise the engine").Done);

        Ok(Walk.Start(g));
        var r = Winterise(g);
        Assert.Equal((90, 250), (r.MinutesTaken, r.CostNok));
        Assert.Contains("You shut the engine down first.", r.Narrative);
        Assert.False(r.EngineRunning);
        Assert.Equal(0, boat.Get("raw-water-pump").State["waterHeld"]);
        Assert.True(Season(g).Tasks.Single(t => t.Name == "Winterise the engine").Done);
        Assert.Contains(Log(g).Entries, e => e.CostNok == 250 && e.Text.StartsWith("Winterised the engine", StringComparison.Ordinal));
    }

    [Fact]
    public void Running_the_engine_fills_the_raw_water_side_again()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(boat: boat);
        Ok(Winterise(g));
        Ok(Walk.Start(g));
        Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(1, boat.Get("raw-water-pump").State["waterHeld"]);
        Assert.False(Season(g).Tasks.Single(t => t.Name == "Winterise the engine").Done);
    }

    [Fact]
    public void Winterising_is_refused_from_the_wrong_place_when_done_and_when_broke_all_free_and_unlogged()
    {
        var g = LaidUpWithLeft(100);
        Walk.ToPlace(g, "cockpit");
        No(g.Execute(new WinteriseEngine()), "The engine is reached from the companionway, with the steps off.");
        Walk.ToPlace(g, "companionway");
        No(g.Execute(new WinteriseEngine()), "The engine is reached from the companionway, with the steps off.");
        Walk.Near(g, "raw-water-pump");
        int entries = Log(g).Entries.Count;
        No(g.Execute(new WinteriseEngine()), "That's 250 NOK and you have 100 NOK left.");
        Assert.Equal(entries, Log(g).Entries.Count);

        var rich = LaidUp();
        Ok(Winterise(rich));
        No(rich.Execute(new WinteriseEngine()), "The raw-water side is drained and full of antifreeze already.");
    }

    [Fact]
    public void Winterising_is_refused_afloat_and_at_sea()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 180, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(CalmDay));
        Prepare(g);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        Walk.Near(g, "raw-water-pump");
        No(g.Execute(new WinteriseEngine()), "She's in the water: while she's afloat the raw-water side stays full.");
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Ok(g.Execute(new Depart()));
        No(g.Execute(new WinteriseEngine()), "You're at sea.");
        No(g.Execute(new FillTank()), "You're at sea: there's no fuel out here.");
        No(g.Execute(new BatteryAshore()), "You're at sea.");
        No(g.Execute(new FitWinterCover()), "You're at sea.");
    }

    /// <summary>The 60 litre tank, 25.76 litres left after the layup helper's idling: 34.24 litres at 22 NOK, rounded up.</summary>
    private const int FillCostNok = 754;

    [Fact]
    public void Filling_the_tank_pays_for_the_litres_and_makes_it_full()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(boat: boat);
        // 27 litres at the start of the game, less the 124 minutes the engine idled (start, launch, the run to the crane) at 0.6 L/h.
        Assert.Equal(25.76, boat.Get("fuel-tank").State["fuelLitres"], 6);
        var task = Season(g).Tasks.Single(t => t.Name == "Fill the tank");
        Assert.False(task.Done);
        Assert.Equal(FillCostNok, task.CostNok);

        var r = Ok(g.Execute(new FillTank()));
        Assert.Equal((30, FillCostNok), (r.MinutesTaken, r.CostNok));
        Assert.Equal(60, boat.Get("fuel-tank").State["fuelLitres"]);
        Assert.True(Season(g).Tasks.Single(t => t.Name == "Fill the tank").Done);
        No(g.Execute(new FillTank()), "The tank is full.");
    }

    [Fact]
    public void Filling_the_tank_is_refused_without_money()
    {
        var g = LaidUpWithLeft(300);
        No(g.Execute(new FillTank()), $"That's {FillCostNok} NOK and you have 300 NOK left.");
    }

    [Fact]
    public void There_is_no_fuel_at_hestholmen()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 180, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(CalmDay));
        Prepare(g);
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Do(g, "antifouling", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Ok(g.Execute(new Depart("Hestholmen")));
        for (int i = 0; i < 20 && Status(g).AtSea; i++) Ok(g.Execute(i == 0 ? new Motor() : new Continue()));
        Assert.Equal("Hestholmen", Status(g).Port);
        No(g.Execute(new FillTank()), "There's no fuel at Hestholmen.");
    }

    [Fact]
    public void The_battery_goes_ashore_and_comes_back_full()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(boat: boat);

        Ok(Walk.Start(g));
        Walk.Near(g, "start battery");
        boat.Get("start-battery").State["stateOfCharge"] = 0.3;
        var r = Ok(g.Execute(new BatteryAshore()));
        Assert.Equal((30, 400), (r.MinutesTaken, r.CostNok));
        Assert.False(r.EngineRunning);
        Assert.True(Season(g).Tasks.Single(t => t.Name == "Battery ashore or on a charger").Done);
        No(g.Execute(new BatteryAshore()), "The battery is in the yard's battery room already.");

        // Nothing runs it down while it is in the battery room.
        double ashore = boat.Get("start-battery").State["stateOfCharge"];
        Wait(g, 20);
        Assert.Equal(ashore, boat.Get("start-battery").State["stateOfCharge"]);

        var back = Ok(BatteryAboardAt(g));
        Assert.Equal((30, 0), (back.MinutesTaken, back.CostNok));
        Assert.InRange(boat.Get("start-battery").State["stateOfCharge"], 0.999, 1.0);
        No(g.Execute(new BatteryAboard()), "The battery is aboard already.");
    }

    [Fact]
    public void A_battery_aboard_is_charged_back_up_of_a_low_charge_fault()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(boat: boat);
        var low = FaultOf(boat, "battery-low-charge");
        Assert.True(low.Present);
        Ok(BatteryAshoreAt(g));
        Ok(BatteryAboardAt(g));
        Assert.False(low.Present);
    }

    [Fact]
    public void With_the_battery_ashore_nothing_names_it_she_will_not_start_and_she_cannot_be_launched()
    {
        var g = LaidUp();
        Ok(Walk.Do(g, "start battery", c => new Inspect(c)));
        Ok(BatteryAshoreAt(g));

        const string away = "The battery is in the yard's battery room.";
        foreach (Command c in new Command[] { new Inspect("start battery"), new Measure("battery"), new Charge("battery"), new Replace("start battery"), new Clean("battery") })
            No(g.Execute(c), away);

        Walk.ToPlace(g, "cockpit");
        No(g.Execute(new StartEngine()), "The battery is in the yard's battery room: fit it first (battery aboard).");
        No(g.Execute(new Launch()), "The battery is in the yard's battery room: fit it first (battery aboard).");
        var options = (ComponentActionsView)g.Ask(new ComponentActions("start battery"));
        Assert.All(options.Options, o => Assert.False(o.Available));

        // Down at the quarter berth the battery is not there to see.
        Walk.ToPlace(g, "quarter-berth");
        var look = Ok(g.Execute(new Look()));
        Assert.DoesNotContain("Start battery", string.Join(" ", look.Narrative));
    }

    [Fact]
    public void The_winter_charger_holds_the_battery_full_and_clears_what_a_charge_puts_right()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(boat: boat);
        boat.Get("start-battery").State["stateOfCharge"] = 0.4;
        var low = FaultOf(boat, "battery-low-charge");
        Assert.True(low.Present);

        var r = Ok(g.Execute(new WinterCharger()));
        Assert.Equal((20, 600), (r.MinutesTaken, r.CostNok));
        Assert.True(Season(g).Tasks.Single(t => t.Name == "Battery ashore or on a charger").Done);
        Assert.False(low.Present);
        Wait(g, 30);
        Assert.Equal(1.0, boat.Get("start-battery").State["stateOfCharge"]);
        No(g.Execute(new WinterCharger()), "The charger is on the battery already.");
    }

    [Fact]
    public void The_winter_charger_is_refused_with_the_battery_ashore_afloat_and_when_broke()
    {
        var g = LaidUpWithLeft(300);
        No(g.Execute(new WinterCharger()), "That's 600 NOK and you have 300 NOK left.");

        var ashore = LaidUp();
        Ok(BatteryAshoreAt(ashore));
        No(ashore.Execute(new WinterCharger()), "The battery is in the yard's battery room, on their charger already.");

        var afloat = new Game(BoatLoader.LoadSloop28(), 180, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(CalmDay));
        Prepare(afloat);
        Ok(Walk.Start(afloat));
        Ok(afloat.Execute(new Launch()));
        No(afloat.Execute(new WinterCharger()), "She's in the water: shore power is for the hard.");
        No(afloat.Execute(new BatteryAshore()), "She's in the water: the battery stays aboard while she's afloat.");
        Walk.ToPlace(afloat, "on-deck");
        No(afloat.Execute(new FitWinterCover()), "She's in the water: the winter cover is for the hard.");
    }

    [Fact]
    public void The_cover_costs_the_first_time_only_and_is_refused_from_below_when_on_and_when_broke()
    {
        var g = LaidUp();
        Walk.ToPlace(g, "cockpit");
        No(g.Execute(new FitWinterCover()), "The cover goes over the deck from on deck: go up there.");
        var r = Cover(g);
        Ok(r);
        Assert.Equal((120, 2500), (r.MinutesTaken, r.CostNok));
        Assert.True(Season(g).Tasks.Single(t => t.Name == "Fit the winter cover").Done);
        No(g.Execute(new FitWinterCover()), "The winter cover is on already.");

        var poor = LaidUpWithLeft(1000);
        Walk.ToPlace(poor, "on-deck");
        No(poor.Execute(new FitWinterCover()), "That's 2500 NOK and you have 1000 NOK left.");
    }

    [Fact]
    public void Launching_takes_the_cover_off_and_disconnects_the_charger_and_the_cover_is_free_next_winter()
    {
        var g = LaidUp(Mild());
        Ok(Cover(g));
        Ok(g.Execute(new WinterCharger()));
        Walk.ToPlace(g, "cockpit");
        var launch = Ok(g.Execute(new Launch()));
        Assert.Contains("You take the winter cover off, fold it and stow it in the locker.", launch.Narrative);
        Assert.Contains("You unplug the yard's charger and coil the cable.", launch.Narrative);
        Assert.Empty(Season(g).Tasks);

        // The same boat lifted again: the cover is already hers, the charger is not on.
        var lift = Season(g);
        Assert.False(lift.LaidUp);
    }

    [Fact]
    public void The_task_list_is_empty_until_she_is_laid_up_and_names_the_five_jobs_with_their_commands_and_risks()
    {
        var spring = new Game(BoatLoader.LoadSloop28(), 80, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(CalmDay));
        Assert.Empty(Season(spring).Tasks);

        var g = LaidUp();
        var tasks = Season(g).Tasks;
        Assert.Equal(
            ["Winterise the engine", "Change the oil", "Fill the tank", "Battery ashore or on a charger", "Fit the winter cover"],
            tasks.Select(t => t.Name));
        Assert.Equal(["winterise", "replace engine oil", "fill tank", "battery ashore", "fit cover"], tasks.Select(t => t.Command));
        Assert.All(tasks, t => Assert.False(t.Done));
        Assert.All(tasks, t => Assert.False(string.IsNullOrWhiteSpace(t.Risk)));
        Assert.Equal([90, 45, 30, 30, 120], tasks.Select(t => t.Minutes));
        Assert.Equal([250, 300, FillCostNok, 400, 2500], tasks.Select(t => t.CostNok));
    }

    [Fact]
    public void Changing_the_oil_counts_only_when_it_is_changed_after_the_autumn_lift()
    {
        // Changed a week before the lift: not done.
        var early = new Game(BoatLoader.LoadSloop28(), 250, new FixedRandom(0.99), Game.DefaultSeed, Mild());
        Prepare(early);
        Ok(early.Execute(new BookHaulout(262)));
        WaitTo(early, 252);
        Ok(Walk.Start(early));
        Try(() => early.Execute(new Launch()), early);
        Ok(Oil(early));
        WaitTo(early, 262);
        Walk.ToPlace(early, "cockpit");
        Ok(Walk.Start(early));
        Ok(early.Execute(new HaulOut()));
        Assert.False(Season(early).Tasks.Single(t => t.Name == "Change the oil").Done);

        Ok(Oil(early));
        Assert.True(Season(early).Tasks.Single(t => t.Name == "Change the oil").Done);
    }

    // ------------------------------------------------------------------ the accumulators

    [Fact]
    public void Frost_hours_count_only_with_raw_water_held_and_a_frost()
    {
        // A mild winter: no frost, nothing counted.
        var mild = LaidUp(Mild());
        Wait(mild, 10);
        Assert.Equal(0, Value(mild, SystemValues.FrostHoursWithWater));

        // Frost with water held: three hours a degree, at most fourteen a night.
        var frost = LaidUp(Winter(0, 0, -2));
        Wait(frost, 10);
        Assert.Equal(60, Value(frost, SystemValues.FrostHoursWithWater), 6);
        var hard = LaidUp(Winter(0, 0, -9));
        Wait(hard, 10);
        Assert.Equal(140, Value(hard, SystemValues.FrostHoursWithWater), 6);

        // Frost, winterised: nothing.
        var drained = LaidUp(Winter(0, 0, -9));
        Ok(Winterise(drained));
        Wait(drained, 10);
        Assert.Equal(0, Value(drained, SystemValues.FrostHoursWithWater));
    }

    [Fact]
    public void Condensation_counts_only_in_a_part_empty_tank_on_a_cold_night()
    {
        var cold = LaidUp(Winter(0, 3, 2));
        Wait(cold, 10);
        // (1 - 25.76/60) a night for ten nights: the tank is 60 litres and the layup helper's idling left 25.76 (design note 010).
        Assert.Equal(10 * (1 - 25.76 / 60), Value(cold, SystemValues.CondensationDays), 6);

        var warm = LaidUp(Winter(0, 12, 8));
        Wait(warm, 10);
        Assert.Equal(0, Value(warm, SystemValues.CondensationDays));

        var full = LaidUp(Winter(0, 3, 2));
        Ok(full.Execute(new FillTank()));
        Wait(full, 10);
        Assert.Equal(0, Value(full, SystemValues.CondensationDays));
    }

    [Fact]
    public void Frozen_nights_count_only_for_a_flat_battery_aboard_with_no_charger_in_a_frost()
    {
        Game Flat(WeatherSeries w, out Boat boat)
        {
            boat = BoatLoader.LoadSloop28();
            var g = LaidUp(w, boat: boat);
            boat.Get("start-battery").State["stateOfCharge"] = 0.1;
            return g;
        }

        var g = Flat(Winter(0, 0, -4), out _);
        Wait(g, 5);
        Assert.Equal(5, Value(g, SystemValues.FrozenNights));

        var noFrost = Flat(Mild(), out _);
        Wait(noFrost, 5);
        Assert.Equal(0, Value(noFrost, SystemValues.FrozenNights));

        var charged = Flat(Winter(0, 0, -4), out _);
        Ok(charged.Execute(new WinterCharger()));
        Wait(charged, 5);
        Assert.Equal(0, Value(charged, SystemValues.FrozenNights));

        var ashore = Flat(Winter(0, 0, -4), out _);
        Ok(BatteryAshoreAt(ashore));
        Wait(ashore, 5);
        Assert.Equal(0, Value(ashore, SystemValues.FrozenNights));

        // A battery with charge in it does not freeze.
        var boat = BoatLoader.LoadSloop28();
        var full = LaidUp(Winter(0, 0, -4), boat: boat);
        Wait(full, 5);
        Assert.Equal(0, Value(full, SystemValues.FrozenNights));
    }

    [Fact]
    public void Snow_days_count_only_with_precipitation_below_freezing_and_no_cover()
    {
        var snow = LaidUp(Winter(3, -2, -5));
        Wait(snow, 6);
        Assert.Equal(6, Value(snow, SystemValues.SnowDays));

        var rain = LaidUp(Winter(3, 4, 2));
        Wait(rain, 6);
        Assert.Equal(0, Value(rain, SystemValues.SnowDays));

        var dry = LaidUp(Winter(0, -2, -5));
        Wait(dry, 6);
        Assert.Equal(0, Value(dry, SystemValues.SnowDays));

        var covered = LaidUp(Winter(3, -2, -5));
        Ok(Cover(covered));
        Wait(covered, 6);
        Assert.Equal(0, Value(covered, SystemValues.SnowDays));
    }

    [Fact]
    public void A_battery_aboard_without_a_charger_runs_down_on_the_hard_and_ashore_or_on_a_charger_it_does_not()
    {
        double Lost(Func<Game, CommandResult>? task)
        {
            var boat = BoatLoader.LoadSloop28();
            var g = LaidUp(Mild(), boat: boat);
            if (task is not null) Ok(task(g));
            var battery = boat.Get("start-battery");
            double before = battery.State["stateOfCharge"];
            Wait(g, 20);
            return before - battery.State["stateOfCharge"];
        }

        // Self-discharge 0.002 a day and the layup draw 0.006 a day times this winter's draw factor, for twenty days.
        Assert.Equal(20 * (0.002 + 0.006 * DrawSpread(BoatLoader.LoadSloop28())), Lost(null), 3);
        Assert.Equal(0, Lost(g => g.Execute(new WinterCharger())), 6);
        Assert.Equal(0, Lost(BatteryAshoreAt), 6);
    }

    [Fact]
    public void Nothing_counts_before_the_autumn_lift_so_the_spring_boat_on_the_hard_is_unchanged()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Winter(3, -3, -6, fromDay: 0));
        Wait(g, 20);
        foreach (var name in new[] { SystemValues.FrostHoursWithWater, SystemValues.CondensationDays, SystemValues.FrozenNights, SystemValues.SnowDays })
            Assert.Equal(0, Value(g, name));
        Assert.All(WinterFaults, id => Assert.False(FaultOf(boat, id).Spawned));
    }

    // ------------------------------------------------------------------ the faults

    [Fact]
    public void A_winter_left_untouched_wakes_all_five_faults_silently_and_none_shows_before_it_is_found()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Harsh(), boat: boat);
        Wait(g, 120);
        foreach (var id in WinterFaults)
        {
            var f = FaultOf(boat, id);
            Assert.True(f.Spawned, id);
            Assert.True(f.Present, id);
            Assert.False(f.Discovered, id);
            Assert.Equal(FaultState.Latent, f.State);
        }
        var names = WinterFaults.Select(id => FaultOf(boat, id).Name).ToList();
        var json = string.Concat(
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())), JsonSerializer.Serialize<QueryResult>(g.Ask(new Readiness())),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Season())), JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook())));
        foreach (var name in names) Assert.DoesNotContain(name, json);
        foreach (var id in WinterFaults) Assert.DoesNotContain(id, json);
    }

    [Fact]
    public void With_all_five_tasks_done_none_of_the_winter_faults_wakes()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Harsh(), boat: boat);
        DoAllFiveTasks(g);
        Wait(g, 150);
        Assert.All(WinterFaults, id => Assert.False(FaultOf(boat, id).Spawned, id));
        Assert.All(Season(g).Tasks, t => Assert.True(t.Done, t.Name));
    }

    [Fact]
    public void Each_task_left_undone_wakes_its_own_fault_and_only_that_one()
    {
        // (the task that is skipped, the faults that may wake)
        (string Skipped, string[] Wakes, Action<Game> Others)[] cases =
        [
            ("winterise", [PumpCracked, ExchangerCracked], g => { Ok(g.Execute(new FillTank())); Ok(BatteryAshoreAt(g)); Ok(Cover(g)); }),
            ("fill tank", [FuelWater], g => { Ok(Winterise(g)); Ok(BatteryAshoreAt(g)); Ok(Cover(g)); }),
            ("battery", [BatteryDead], g => { Ok(Winterise(g)); Ok(g.Execute(new FillTank())); Ok(Cover(g)); }),
            ("cover", [SprayhoodTorn], g => { Ok(Winterise(g)); Ok(g.Execute(new FillTank())); Ok(BatteryAshoreAt(g)); }),
        ];
        foreach (var (skipped, wakes, others) in cases)
        {
            var boat = BoatLoader.LoadSloop28();
            var g = LaidUp(Harsh(), boat: boat);
            if (skipped == "battery") boat.Get("start-battery").State["stateOfCharge"] = 0.1; // already run down
            others(g);
            Wait(g, 120);
            foreach (var id in WinterFaults)
                Assert.Equal(wakes.Contains(id), FaultOf(boat, id).Spawned);
        }
    }

    [Fact]
    public void A_battery_left_aboard_runs_down_and_then_freezes_on_its_own_schedule()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Winter(0, 2, -3), boat: boat);
        boat.Get("start-battery").State["stateOfCharge"] = 1.0;
        FaultOf(boat, "battery-low-charge").Clear();
        Wait(g, 60);
        Assert.False(FaultOf(boat, BatteryDead).Spawned);
        Wait(g, 60);
        Assert.True(FaultOf(boat, BatteryDead).Spawned);
        // Charge 1 falls at 0.002 + 0.006 times the winter's draw factor a day and reaches a quarter after 0.75 over that; two or three
        // frozen nights later it has split.
        double toQuarter = 0.75 / (0.002 + 0.006 * DrawSpread(boat));
        Assert.InRange(FaultOf(boat, BatteryDead).ActiveSinceDay!.Value - 262, toQuarter, toQuarter + 4);
    }

    [Fact]
    public void The_pump_hint_and_the_cause_line_come_when_the_player_looks_and_inspects()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Winter(0, 0, -6), boat: boat);
        Walk.Near(g, "raw-water-pump");
        Ok(g.Execute(new Look())); // the pump was seen before it cracked
        Wait(g, 5);
        Assert.True(FaultOf(boat, PumpCracked).Spawned);
        Walk.Near(g, "raw-water-pump");

        var look = Ok(g.Execute(new Look()));
        Assert.Contains("A crust of white salt and a few dried drips on the engine bay floor, right under the raw-water pump.", look.Narrative);
        Assert.Empty(look.NewlyDiscovered);
        Assert.DoesNotContain(look.Narrative, l => l.Contains("has cracked", StringComparison.Ordinal));

        var inspect = Ok(g.Execute(new Inspect("raw-water pump")));
        var found = Assert.Single(inspect.NewlyDiscovered);
        Assert.Equal("Cracked raw-water pump body", found.Name);
        var cause = Assert.Single(inspect.Narrative, l => l.StartsWith("The raw-water pump body has cracked. It came on after ", StringComparison.Ordinal));
        Assert.EndsWith(" hours of frost with raw water left in the engine.", cause);

        // The logbook states the cause just before the Found line.
        var entries = Log(g).Entries;
        int found_ = entries.Select((e, i) => (e, i)).Single(x => x.e.Text == "Found: Cracked raw-water pump body.").i;
        Assert.Equal(cause, entries[found_ - 1].Text);
    }

    [Fact]
    public void Every_winter_fault_is_found_by_its_own_verbs_and_tells_its_cause()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Harsh(), boat: boat);
        boat.Get("start-battery").State["stateOfCharge"] = 0.1;
        Wait(g, 120);

        var hx = Ok(Walk.Do(g, "heat exchanger", c => new Inspect(c)));
        Assert.Contains(hx.Narrative, l => l.StartsWith("The heat exchanger has split. It came on after ", StringComparison.Ordinal) && l.EndsWith(" hours of frost with raw water left in the engine.", StringComparison.Ordinal));

        var bowl = Ok(Walk.Do(g, "primary filter", _ => new CheckFilterBowl()));
        Assert.Contains(bowl.Narrative, l => l.StartsWith("Water has condensed in the fuel tank. It came on over the winter: about ", StringComparison.Ordinal));

        var meter = Ok(Walk.Do(g, "start battery", c => new Measure(c)));
        Assert.Contains(meter.Narrative, l => l.StartsWith("The battery has frozen and split. It came on after ", StringComparison.Ordinal) && l.EndsWith(" of frost with the battery flat and left aboard.", StringComparison.Ordinal));
        Assert.Contains("Frozen, dead battery", meter.NewlyDiscovered.Select(k => k.Name));

        Walk.ToPlace(g, "cockpit");
        var look = Ok(g.Execute(new Look()));
        Assert.Contains("The sprayhood sags on its frame, the canvas puckered along the top seams.", look.Narrative);
        var spray = Ok(g.Execute(new Inspect("sprayhood")));
        Assert.Contains(spray.Narrative, l => l.StartsWith("The sprayhood has split along its seams. It came on after ", StringComparison.Ordinal) && l.EndsWith(" of snow lying on it with no cover on her.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cracked_pump_weakens_the_flow_leaks_afloat_and_is_diagnosed_with_check_flow()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Winter(0, 0, -9), boat: boat);
        Ok(Walk.Do(g, "impeller", c => new Replace(c))); // so the old impeller does not muddy the exhaust
        Wait(g, 6);
        Assert.True(FaultOf(boat, PumpCracked).Spawned);
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        var flow = Ok(g.Execute(new CheckRawWaterFlow()));
        Assert.Contains("The water at the exhaust comes in weak, uneven spurts.", flow.Narrative);
        Assert.Contains("The water at the exhaust is weaker than you'd like.", flow.Narrative);
    }

    [Fact]
    public void A_fixed_winter_fault_is_re_armed_and_fires_again_only_after_fresh_accumulation()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = LaidUp(Winter(0, 0, -6), boat: boat);
        Wait(g, 6);
        var first = FaultOf(boat, PumpCracked);
        Assert.True(first.Spawned);
        Ok(Walk.Do(g, "raw-water pump", c => new Inspect(c)));
        Ok(Walk.Do(g, "raw-water pump", c => new Replace(c)));
        Assert.Equal(FaultState.Fixed, first.State);

        // The component now holds a dormant copy, counted from the frost hours of now.
        var copy = boat.Get("raw-water-pump").Faults.Single(f => f.Id == PumpCracked + "#2");
        Assert.True(copy.Dormant);
        Assert.False(copy.Spawned);
        Assert.Equal(first.Name, copy.Name);
        Assert.Equal(first.Diy, copy.Diy);
        Assert.Equal(first.Yard, copy.Yard);
        Assert.Equal(first.Reveals.Count, copy.Reveals.Count);
        Assert.Equal(first.Effects.Count, copy.Effects.Count);
        Assert.True(copy.SpawnBaseline[SystemValues.FrostHoursWithWater] > 0);

        // One more night is not enough (14 hours against at least 14.4); a few more are.
        Ok(g.Execute(new WaitDays(1)));
        Assert.False(copy.Spawned);
        Wait(g, 5);
        Assert.True(copy.Spawned);
        Assert.True(copy.Present);
        Assert.False(copy.Discovered);

        // Fixed again, a third copy stands ready.
        Ok(Walk.Do(g, "raw-water pump", c => new Inspect(c)));
        Ok(Walk.Do(g, "raw-water pump", c => new Replace(c)));
        Assert.Contains(boat.Get("raw-water-pump").Faults, f => f.Id == PumpCracked + "#3");
    }

    [Fact]
    public void A_winterised_boat_whose_pump_was_replaced_is_not_cracked_again_until_the_water_is_back_in()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = LaidUp(Winter(0, 0, -6), boat: boat);
        Wait(g, 6);
        Ok(Walk.Do(g, "raw-water pump", c => new Inspect(c)));
        Ok(Walk.Do(g, "raw-water pump", c => new Replace(c)));
        Ok(Winterise(g));
        Wait(g, 20);
        Assert.False(boat.Get("raw-water-pump").Faults.Single(f => f.Id == PumpCracked + "#2").Spawned);
    }

    // ------------------------------------------------------------------ the cover keeps the rain out

    [Fact]
    public void The_winter_cover_keeps_the_rain_out_of_the_open_hatch_and_off_the_ageing_faults()
    {
        double Bilge(bool cover)
        {
            var g = LaidUp(Winter(6, 4, 3));
            if (cover) Ok(Cover(g));
            Walk.ToPlace(g, "companionway"); // the hatch is slid open on the way below
            g.BilgeLitres = 0;
            Wait(g, 4);
            return g.BilgeLitres;
        }

        double wet = Bilge(cover: false);
        double dry = Bilge(cover: true);
        Assert.True(wet > 5, $"{wet}");
        Assert.True(dry < 0.5, $"{dry}");
    }

    // ------------------------------------------------------------------ waiting for spring

    [Fact]
    public void Waiting_until_spring_lands_on_day_75_of_the_next_year_day_by_day()
    {
        var g = LaidUp(Mild());
        Ok(g.Execute(new WinterCharger()));
        int before = Status(g).Day;
        var r = Ok(g.Execute(new WaitUntilSpring()));
        Assert.Equal(360 + 75, Status(g).Day);
        Assert.Equal(("wait-until-spring", 0), (r.Action, r.CostNok));
        Assert.Equal((360 + 75 - before) * 1440, r.MinutesTaken);
        Assert.Contains("You leave her for the winter.", r.Narrative);
        Assert.Contains($"{360 + 75 - before} days later it is day 435.", r.Narrative);
        Assert.Contains("It is spring.", r.Narrative);
        Assert.Contains(r.Narrative, l => l.StartsWith("In that time:", StringComparison.Ordinal));
        Assert.Contains(Log(g).Entries, e => e.Text == "Left her for the winter.");
        Assert.Equal("Spring, year 2.", Season(g).Text[..15]);
    }

    [Fact]
    public void Waiting_until_spring_stops_at_the_first_alert_but_not_again_for_the_same_going_on_condition()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Mild(), boat: boat);
        boat.Get("start-battery").State["stateOfCharge"] = 1.0;
        FaultOf(boat, "battery-low-charge").Clear();
        var first = Ok(g.Execute(new WaitUntilSpring()));
        // The battery aboard runs down at 0.002 + 0.006 times the winter's draw factor a day: below half a charge after 0.5 over that.
        double toHalf = 0.5 / (0.002 + 0.006 * DrawSpread(boat));
        Assert.InRange(Status(g).Day - 262, toHalf - 3, toHalf + 4);
        Assert.Contains("You come back before spring: something on her wants your attention.", first.Narrative);
        Assert.Contains(first.Alerts, a => a.Text.StartsWith("The panel voltmeter reads", StringComparison.Ordinal));

        // The battery stays low; the next wait goes on to spring.
        var second = Ok(g.Execute(new WaitUntilSpring()));
        Assert.Equal(435, Status(g).Day);
        Assert.Contains("It is spring.", second.Narrative);
    }

    [Fact]
    public void Waiting_until_spring_stops_on_the_morning_of_a_booked_slot()
    {
        // From day 241 (waiting for spring is refused earlier in the season): book the first free slot and wait.
        var g = new Game(BoatLoader.LoadSloop28(), 241, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(CalmDay));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        int slot = Season(g).FirstFreeDay!.Value;
        Ok(g.Execute(new BookHaulout(slot)));
        string morning = Status(g).TimeOfDay;
        var r = Ok(g.Execute(new WaitUntilSpring()));
        Assert.Equal(slot, Status(g).Day);
        Assert.Contains($"You come back on day {slot} for your haulout slot.", r.Narrative);
        Assert.Equal(slot, Season(g).BookedDay);
        Assert.Equal(morning, Status(g).TimeOfDay);
    }

    [Fact]
    public void Waiting_until_spring_is_refused_at_sea_and_on_day_75_it_waits_for_the_next_year()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 180, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(CalmDay));
        Prepare(g);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Ok(g.Execute(new Depart()));
        No(g.Execute(new WaitUntilSpring()), "You're at sea: continue, heave to or anchor.");

        // In the season (day 75 to the autumn lift) and not laid up, there is no winter to wait out.
        var spring = new Game(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(CalmDay));
        No(spring.Execute(new WaitUntilSpring()), "It's already the season.");
    }

    [Fact]
    public void Waiting_until_spring_reports_the_alert_that_stopped_it_among_the_notices_and_the_logbook()
    {
        var g = LaidUp(Mild());
        var r = Ok(g.Execute(new WaitUntilSpring()));
        var alert = Assert.Single(r.Alerts, a => a.Text.StartsWith("The panel voltmeter", StringComparison.Ordinal));
        Assert.Contains(Log(g).Entries, e => e.Alert && e.Text == alert.Text);
        Assert.Empty(((NoticesView)g.Ask(new Notices())).Alerts);
    }

    // ------------------------------------------------------------------ the roll-over

    [Fact]
    public void A_game_hauled_out_in_autumn_year_one_winters_and_is_launched_in_spring_year_two_with_the_winters_faults_to_find()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = LaidUp(Harsh(), boat: boat);
        Assert.Equal(1, Season(g).Year);
        Assert.Contains("laid up", Season(g).Text);

        // A winter with the cover and a charger but not the antifreeze: only the engine pays.
        Ok(Cover(g));
        Ok(g.Execute(new FillTank()));
        Ok(g.Execute(new WinterCharger()));
        int guard = 0;
        while (Status(g).Day < 360 + 75 && guard++ < 20) Ok(g.Execute(new WaitUntilSpring()));
        Assert.Equal(435, Status(g).Day);

        var spring = Season(g);
        Assert.Equal((2, 75, "spring"), (spring.Year, spring.DayOfYear, spring.SeasonWord));
        Assert.True(spring.LaidUp);
        Assert.Contains("laid up", spring.Text);
        Assert.Equal(spring.Text, Status(g).Season);
        // The yard window of year two stands where it should.
        Assert.Equal((615, 660), (spring.SlotsFromDay, spring.SlotsToDay));
        Assert.Equal(615, spring.FirstFreeDay);

        // The engine froze; everything else was seen to.
        Assert.True(FaultOf(boat, PumpCracked).Spawned);
        Assert.False(FaultOf(boat, FuelWater).Spawned);
        Assert.False(FaultOf(boat, BatteryDead).Spawned);
        Assert.False(FaultOf(boat, SprayhoodTorn).Spawned);
        var found = Ok(Walk.Do(g, "raw-water pump", c => new Inspect(c)));
        Assert.Contains(found.Narrative, l => l.StartsWith("The raw-water pump body has cracked.", StringComparison.Ordinal));
        Ok(Walk.Do(g, "raw-water pump", c => new Replace(c)));

        // Spring: the cover and the charger come off at launch, and she floats again in year two.
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        var launch = Ok(g.Execute(new Launch()));
        Assert.Contains("You take the winter cover off, fold it and stow it in the locker.", launch.Narrative);
        var afloat = Season(g);
        Assert.False(afloat.LaidUp);
        Assert.True(afloat.Afloat);
        Assert.Equal(2, afloat.Year);
        Assert.StartsWith("Spring, year 2.", afloat.Text);
        Assert.Equal(afloat.Text, Status(g).Season);
        Assert.Contains($"day {Status(g).Day}", launch.Narrative.Last());

        // The weather, the forecast and the trip plan run on past day 360.
        Assert.Equal(435, ((WeatherView)g.Ask(new Haulout.Sim.Play.Weather())).Day);
        var plan = (TripPlanView)g.Ask(new PlanTrip());
        Assert.NotEmpty(plan.Forecast);
        Assert.All(plan.Forecast, d => Assert.True(d.Day >= 435));
    }

    [Fact]
    public void A_second_autumn_in_year_two_has_its_own_window_and_a_re_armed_winter()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = LaidUp(Winter(0, 0, -6), boat: boat);
        Wait(g, 6);
        Ok(Walk.Do(g, "raw-water pump", c => new Inspect(c)));
        Ok(Walk.Do(g, "raw-water pump", c => new Replace(c)));
        Ok(g.Execute(new WinterCharger()));
        int guard = 0;
        while (Status(g).Day < 435 && guard++ < 20) Ok(g.Execute(new WaitUntilSpring()));
        Walk.ToPlace(g, "cockpit");
        Try(() => g.Execute(new Launch()), g);
        Assert.Equal(2, Season(g).Year);

        // Year two's yard calendar: the window moved on a year, and the first free slot is the window's first day while it is early.
        Assert.Equal((615, 660), (Season(g).SlotsFromDay, Season(g).SlotsToDay));
        Assert.Equal(615, Season(g).FirstFreeDay);
        Ok(g.Execute(new BookHaulout(620)));
        Assert.Equal(620, Season(g).BookedDay);
        Assert.Contains(boat.Get("raw-water-pump").Faults, f => f.Id == PumpCracked + "#2");
    }

    // ------------------------------------------------------------------ the words and the wire

    [Fact]
    public void The_layup_commands_are_in_the_catalogue_and_serialise_by_their_names()
    {
        var verbs = ((CommandList)new Game(BoatLoader.LoadSloop28()).Ask(new Commands())).Commands.Select(c => c.Verb).ToList();
        foreach (var verb in new[] { "winterise", "fill tank", "battery ashore", "battery aboard", "charger", "fit cover", "wait until spring" })
            Assert.Contains(verb, verbs);

        foreach (var (command, name) in new (Command, string)[]
        {
            (new WinteriseEngine(), "winterise-engine"), (new FillTank(), "fill-tank"), (new BatteryAshore(), "battery-ashore"),
            (new BatteryAboard(), "battery-aboard"), (new WinterCharger(), "winter-charger"), (new FitWinterCover(), "fit-winter-cover"),
            (new WaitUntilSpring(), "wait-until-spring"),
        })
        {
            var json = JsonSerializer.Serialize(command);
            Assert.Contains($"\"type\":\"{name}\"", json);
            Assert.Equal(command.GetType(), JsonSerializer.Deserialize<Command>(json)!.GetType());
        }
    }

    [Fact]
    public void The_game_executes_the_layup_commands_through_the_facade_alone()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 180);
        foreach (Command c in new Command[] { new WinteriseEngine(), new BatteryAshore(), new BatteryAboard(), new FitWinterCover() })
            Assert.False(g.Execute(c).Accepted);
        Assert.True(g.Execute(new WinterCharger()).Accepted);
        Assert.True(g.Execute(new FillTank()).Accepted);
    }

    // ------------------------------------------------------------------ nothing leaks

    [Fact]
    public void The_layup_views_and_results_carry_no_ids_and_no_hidden_fault_names()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(Harsh(), boat: boat);
        var seen = new List<string>();
        void Snap()
        {
            foreach (Query q in new Query[] { new Status(), new Season(), new Logbook(), new Notices(), new Readiness(), new Aboard(), new Inventory(), new ComponentActions("start battery") })
                seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(q)));
        }
        void Keep(CommandResult r) => seen.Add(JsonSerializer.Serialize(r));

        Snap();
        Keep(Ok(Winterise(g)));
        Keep(Ok(g.Execute(new FillTank())));
        Keep(Ok(Oil(g)));
        Keep(Ok(BatteryAshoreAt(g)));
        Keep(Ok(Cover(g)));
        Snap();
        Keep(Ok(BatteryAboardAt(g)));
        Keep(Ok(g.Execute(new WinterCharger())));
        Wait(g, 100);
        Snap();
        Keep(Ok(g.Execute(new WaitUntilSpring())));
        Snap();
        Walk.ToPlace(g, "cockpit");
        Keep(Ok(g.Execute(new Look())));
        Snap();

        var json = string.Concat(seen);
        foreach (var f in boat.AllFaults)
        {
            Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
            if (!f.Discovered) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
        }
        Assert.DoesNotContain('#'.ToString(), json.Replace("\"#", "")); // no re-armed copy ids
        foreach (var id in boat.Components.Select(c => c.Id).Where(id => id.Contains('-')))
            Assert.False(json.Contains(id, StringComparison.Ordinal), id);
        foreach (var key in new[] { "waterHeld", "fuelLitres", "frostHoursWithWater", "condensationDays", "frozenNights", "snowDays", "stateOfCharge" })
            Assert.DoesNotContain(key, json);
    }

    // ------------------------------------------------------------------ measurement

    /// <summary>The day each winter fault woke, or null if it never did, for a boat hauled out on day 285 and left until day 435.</summary>
    private static Dictionary<string, int?> WakeDays(int seed, bool allTasks)
    {
        var boat = BoatWithBalance(60000);
        var weather = WeatherSeries.Generate(seed);
        var g = new Game(boat, 250, new FixedRandom(0.99), seed, weather);
        Prepare(g);
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(g.Execute(new BookHaulout(285)));
        WaitTo(g, 270);
        Ok(Walk.Start(g));
        Try(() => g.Execute(new Launch()), g);
        WaitTo(g, 285);
        Walk.ToPlace(g, "cockpit");
        Try(() =>
        {
            if (!Status(g).EngineRunning) Ok(Walk.Start(g));
            return g.Execute(new HaulOut());
        }, g);
        if (allTasks) DoAllFiveTasks(g);
        WaitTo(g, 435);
        return WinterFaults.ToDictionary(id => id, id => FaultOf(boat, id).Spawned ? FaultOf(boat, id).ActiveSinceDay : null);
    }

    private static string Describe(IEnumerable<int?> days)
    {
        var some = days.Where(d => d is not null).Select(d => d!.Value).OrderBy(d => d).ToList();
        int never = days.Count(d => d is null);
        return some.Count == 0
            ? $"never in 20 seeds"
            : $"min {some[0]}, median {some[some.Count / 2]}, max {some[^1]}, {some.Count} of 20 woke, {never} never";
    }

    [Fact]
    public void Measurement_of_the_five_faults_over_twenty_seeds_untouched_and_with_every_task_done()
    {
        var untouched = Enumerable.Range(1, 20).Select(seed => WakeDays(seed, allTasks: false)).ToList();
        var done = Enumerable.Range(1, 20).Select(seed => WakeDays(seed, allTasks: true)).ToList();
        foreach (var id in WinterFaults)
        {
            output.WriteLine($"untouched {id}: {Describe(untouched.Select(d => d[id]))}");
            output.WriteLine($"all five done {id}: {Describe(done.Select(d => d[id]))}");
        }

        // Left alone, every one of them wakes inside the winter in most seeds; with the five tasks done, none does.
        Assert.All(done, d => Assert.All(d.Values, v => Assert.Null(v)));
        foreach (var id in new[] { PumpCracked, ExchangerCracked, FuelWater })
            Assert.True(untouched.Count(d => d[id] is not null) >= 15, id);
        // Not on one fixed day.
        foreach (var id in WinterFaults)
            Assert.True(untouched.Select(d => d[id]).Where(v => v is not null).Distinct().Count() > 1, id);
        // The flat battery is luck too: this winter's draw is a seeded factor, so over twenty seeds it wakes weeks apart (341 to 385 measured).
        var dead = untouched.Select(d => d[BatteryDead]!.Value).OrderBy(d => d).ToList();
        Assert.InRange(dead[^1] - dead[0], 20, 80);
    }
}
