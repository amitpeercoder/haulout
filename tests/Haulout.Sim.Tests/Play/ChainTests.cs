using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Systems;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// The three chains of design note 006, step 4, played end to end through the facade: a root fault feeds a system, the
/// system value crosses a threshold, and a second fault spawns that is latent at first and then a full fault.
/// Internals are used only to read system values and the battery's live charge.
/// </summary>
public class ChainTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string HoseName = "Exhaust hose cooked";
    private const string TerminalName = "Strained positive terminal";
    private const string BeltName = "Slipping alternator belt";
    private const string PumpName = "Burnt-out bilge pump";
    private const string StarterSoaked = "Starter soaked in bilge water";

    private static Game NewGame(WeatherSeries weather, out Boat boat, bool calendar = true)
    {
        boat = BoatLoader.LoadSloop28(withCalendar: calendar);
        return new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, weather);
    }

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static ReadinessView Ready(Game g) => (ReadinessView)g.Ask(new Readiness());
    private static Fault FaultOf(Boat boat, string id) => boat.AllFaults.Single(f => f.Id == id);
    private static double Live(Boat boat) => boat.Get("start-battery").State["stateOfCharge"];

    /// <summary>Everything the player could read, as the adapters would send it.</summary>
    private static string Everything(Game g, IEnumerable<CommandResult> results) =>
        JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())) + JsonSerializer.Serialize<QueryResult>(g.Ask(new Readiness()))
        + JsonSerializer.Serialize<QueryResult>(g.Ask(new Aboard())) + JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook()))
        + string.Concat(results.Select(r => JsonSerializer.Serialize(r)));

    /// <summary>The boat ready to start: the negative terminal cleaned, the battery charged (which clears the 60 % fault).</summary>
    private static void ReadyToStart(Game g)
    {
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "start battery", c => new Charge(c)));
    }

    private static CommandResult Start(Game g) => Walk.Start(g);

    /// <summary>Work the bilge pump by hand (15 minutes each) with the engine running: the cheapest way to let engine time pass.</summary>
    private static void RunFor(Game g, int quarterHours)
    {
        Walk.ToPlace(g, "cockpit");
        for (int i = 0; i < quarterHours; i++) Ok(g.Execute(new PumpBilge()));
    }

    // ------------------------------------------------------------------ chain 1: seacock -> no cooling -> hose

    private static Game SeizedAndLaunched(out Boat boat)
    {
        var g = NewGame(WeatherSeries.Fair(), out boat);
        ReadyToStart(g);
        Ok(g.Execute(new Launch())); // the weeping seacock seizes on launch
        return g;
    }

    [Fact]
    public void A_seized_seacock_gives_no_cooling_flow_and_the_engine_overheats_when_run()
    {
        var g = SeizedAndLaunched(out var boat);
        Assert.Equal(FaultState.Latent, FaultOf(boat, "seacock-seized").State);

        var start = Ok(Start(g));
        Assert.True(start.EngineRunning);
        Assert.Contains("temperature alarm", Text(start));
        Assert.Equal(0, EngineInputsBuilderFlow(boat));
        Assert.Equal(2, g.Values.Get(SystemValues.OverheatMinutes)); // the two minutes of the start itself
        Assert.Contains("Almost nothing comes out", Text(Ok(g.Execute(new CheckRawWaterFlow()))));
    }

    private static double EngineInputsBuilderFlow(Boat boat) => EngineInputsBuilder.Build(boat).Cooling.RawWaterFlow;

    [Fact]
    public void Stopping_before_ten_minutes_of_overheating_spawns_nothing()
    {
        var g = SeizedAndLaunched(out var boat);
        Ok(Start(g));
        Ok(g.Execute(new CheckRawWaterFlow())); // 2 + 5 = 7 minutes
        var stop = Ok(g.Execute(new StopEngine()));
        Assert.False(stop.EngineRunning);
        Assert.Equal(7, g.Values.Get(SystemValues.OverheatMinutes));

        // Nothing accrues with the engine off, however long she sits.
        Ok(g.Execute(new WaitDays(3)));
        Assert.Equal(7, g.Values.Get(SystemValues.OverheatMinutes));
        var hose = FaultOf(boat, "exhaust-hose-cooked");
        Assert.True(hose.Dormant);
        Assert.False(hose.Spawned);
    }

    [Fact]
    public void Ten_minutes_of_overheating_cook_the_exhaust_hose_which_is_latent_then_found_deferrable_and_fixable()
    {
        var g = SeizedAndLaunched(out var boat);
        var hose = FaultOf(boat, "exhaust-hose-cooked");
        var results = new List<CommandResult> { Ok(Start(g)) };
        results.Add(Ok(g.Execute(new CheckRawWaterFlow()))); // 7 minutes: still fine
        Assert.True(hose.Dormant);
        results.Add(Ok(g.Execute(new CheckRawWaterFlow()))); // 12 minutes
        Assert.True(hose.Spawned);
        Assert.Equal(FaultState.Latent, hose.State);

        // Latent: nowhere in anything the player can read.
        var all = Everything(g, results);
        Assert.DoesNotContain(HoseName, all, StringComparison.Ordinal);
        Assert.DoesNotContain("exhaust-hose-cooked", all, StringComparison.Ordinal);
        Assert.DoesNotContain(Ready(g).Open, p => p.Problem == HoseName);
        Assert.DoesNotContain(Status(g).DiscoveredFaults, k => k.Name == HoseName);

        // Found by looking at it.
        var inspect = Ok(Walk.Do(g, "exhaust hose", c => new Inspect(c)));
        Assert.Contains("the rubber cooked brown", Text(inspect));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == HoseName);
        var item = Ready(g).Open.Single(p => p.Problem == HoseName);
        Assert.Contains("split", item.Consequence);
        Assert.Contains(item.Routes, r => r.Route == "DIY");
        Assert.Contains(item.Routes, r => r.Route == "Yard");

        Ok(g.Execute(new Defer(HoseName)));
        Assert.Equal("Deferred", Ready(g).Open.Single(p => p.Problem == HoseName).State);
        Ok(g.Execute(new Undefer(HoseName)));

        Ok(g.Execute(new Replace("exhaust hose")));
        Assert.False(hose.Present);
        Assert.Contains(HoseName, Ready(g).Fixed);
        Assert.DoesNotContain(Ready(g).Open, p => p.Problem == HoseName);
    }

    // ------------------------------------------------------------------ chain 2: belt -> flat battery -> drags -> terminal

    [Fact]
    public void A_slipping_belt_is_present_and_latent_from_the_start_and_shows_in_no_list()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        var belt = FaultOf(boat, "alternator-belt-slipping");
        Assert.True(belt.Present);
        Assert.Equal(FaultState.Latent, belt.State);
        Assert.Equal(0, Electrical.ChargeFactor(boat));
        Assert.DoesNotContain(BeltName, Everything(g, []), StringComparison.Ordinal);
        Assert.DoesNotContain(Ready(g).Open, p => p.Problem == BeltName);
    }

    [Fact]
    public void A_start_that_cranks_draws_charge_and_one_that_does_not_crank_draws_none()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        double before = Live(boat);
        Assert.Equal("Click", Assert.IsType<StartAttemptView>(Ok(Start(g)).Observation).Outcome); // the corroded terminal: it cranks, barely
        Assert.InRange(before - Live(boat), 0.0099, 0.0111);
    }

    [Fact]
    public void With_a_slipping_belt_the_battery_falls_per_engine_hour_and_rises_once_the_belt_is_replaced()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        ReadyToStart(g);
        Ok(Start(g));
        Assert.Equal(0, g.Values.Get(SystemValues.Charging));
        double at = Live(boat);
        RunFor(g, 4); // an hour
        Assert.InRange(at - Live(boat), 0.028, 0.032); // 0.10 x 0 - 0.03 per hour

        // The belt is found by pressing it; replacing it puts the charge back.
        var inspect = Ok(Walk.Do(g, "alternator belt", c => new Inspect(c)));
        Assert.Contains("It's slipping on the pulley", Text(inspect));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == BeltName);
        var item = Ready(g).Open.Single(p => p.Problem == BeltName);
        Assert.Contains("go flat", item.Consequence);
        Assert.Contains(item.Routes, r => r.Route == "DIY" && r.CostNok == 220 && r.Minutes == 20);
        Ok(g.Execute(new Replace("alternator belt")));
        Assert.Equal(1.0, Electrical.ChargeFactor(boat));
        Assert.Contains(BeltName, Ready(g).Fixed);

        Assert.True(Ok(Start(g)).EngineRunning);
        Assert.Equal(1.0, g.Values.Get(SystemValues.Charging));
        boat.Get("start-battery").State["stateOfCharge"] = 0.5; // flat enough to have room to rise
        at = Live(boat);
        RunFor(g, 4);
        Assert.InRange(Live(boat) - at, 0.068, 0.072); // 0.10 - 0.03 per hour
        Ok(g.Execute(new StopEngine()));
        Assert.Equal(0, g.Values.Get(SystemValues.Charging));
    }

    [Fact]
    public void A_flat_battery_drags_the_starter_and_after_four_drag_starts_the_positive_terminal_is_strained()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        var terminal = FaultOf(boat, "terminal-strained");
        var results = new List<CommandResult>();
        ReadyToStart(g);
        results.Add(Ok(Start(g)));
        Assert.Equal(0, g.Values.Get(SystemValues.DragStarts)); // full battery: she turns over briskly

        // Run on the slipping belt until the battery is low enough to drag (about ten hours).
        int guard = 0;
        while (Electrical.StateOfCharge(boat) >= 0.69 && guard++ < 80) RunFor(g, 4);
        Assert.True(Electrical.StateOfCharge(boat) < 0.69, "the battery never ran down");
        Assert.True(Electrical.StateOfCharge(boat) > 0.5);

        for (int drag = 1; drag <= 4; drag++)
        {
            results.Add(Ok(g.Execute(new StopEngine())));
            var start = Ok(Start(g));
            results.Add(start);
            Assert.True(Assert.IsType<StartAttemptView>(start.Observation).LabouredCrank, $"start {drag}");
            Assert.Contains("laboured", Text(start));
            Assert.Equal(drag, g.Values.Get(SystemValues.DragStarts));
            Assert.Equal(drag == 4, terminal.Spawned);
        }
        Assert.Equal(FaultState.Latent, terminal.State);

        // Latent: not named anywhere the player can look.
        var all = Everything(g, results);
        Assert.DoesNotContain(TerminalName, all, StringComparison.Ordinal);
        Assert.DoesNotContain("terminal-strained", all, StringComparison.Ordinal);
        Assert.DoesNotContain(Ready(g).Open, p => p.Problem == TerminalName);

        // The effect is real: the next start only drags and does not catch.
        Ok(g.Execute(new StopEngine()));
        var slow = Ok(Start(g));
        Assert.Equal("SlowCrank", Assert.IsType<StartAttemptView>(slow.Observation).Outcome);
        Assert.False(slow.EngineRunning);

        // Found by looking at the clamp; then it is a full fault.
        var inspect = Ok(Walk.Do(g, "positive terminal", c => new Inspect(c)));
        Assert.Contains("discoloured with heat", Text(inspect));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == TerminalName);
        var item = Ready(g).Open.Single(p => p.Problem == TerminalName);
        Assert.Contains(item.Routes, r => r.Route == "DIY");
        Assert.False(string.IsNullOrEmpty(item.Consequence));

        Ok(g.Execute(new Defer(TerminalName)));
        Ok(g.Execute(new Undefer(TerminalName)));
        Ok(g.Execute(new Clean("positive terminal")));
        Assert.False(terminal.Present);
        Assert.Contains(TerminalName, Ready(g).Fixed);

        // And it never spawns twice: more drags change nothing.
        for (int i = 0; i < 3; i++)
        {
            Ok(Start(g));
            Ok(g.Execute(new StopEngine()));
        }
        Assert.False(terminal.Present);
    }

    [Fact]
    public void The_strained_terminal_also_shows_as_a_drop_when_measured_while_cranking()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        var terminal = FaultOf(boat, "terminal-strained");
        terminal.SpawnOn(75);
        ReadyToStart(g);
        Ok(g.Execute(new Buy("remote start button")));
        var measure = Ok(Walk.Do(g, "positive terminal", c => new Measure(c)));
        Assert.Contains("lost across the positive terminal", Text(measure));
        Assert.Contains("eating volts", Text(measure));
        Assert.Contains(measure.NewlyDiscovered, k => k.Name == TerminalName);
    }

    // ------------------------------------------------------------------ chain 3: water -> pump -> awash -> soaked

    [Fact]
    public void A_weeping_seacock_and_rain_through_rusted_drains_raise_the_bilge_burn_the_pump_and_soak_the_starter()
    {
        // Steady, very heavy rain so the whole chain plays out in a couple of weeks of waiting.
        var g = NewGame(WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 100, 6, 3)), out var boat, calendar: false);
        var results = new List<CommandResult>();
        Walk.ToPlace(g, "cockpit");
        results.Add(Ok(g.Execute(new Refit("hatch")))); // only the drains let rain in
        results.Add(Ok(g.Execute(new Launch())));
        var pump = boat.Get("bilge-pump");
        var burnt = FaultOf(boat, "bilge-pump-burnt-out");
        var soaked = FaultOf(boat, "starter-soaked");
        int burntDay = -1, soakedDay = -1;

        // The bilge rises and the pump begins to cycle.
        results.Add(Ok(g.Execute(new WaitDays(1))));
        Assert.True(pump.State["cycles"] > 0);
        int cycles = (int)pump.State["cycles"];
        Assert.Contains("a few inches", Text(Ok(Walk.Do(g, "bilge", c => new Inspect(c)))));

        for (int day = 2; day <= 60 && soakedDay < 0; day++)
        {
            results.Add(Ok(g.Execute(new WaitDays(1))));
            if (burntDay < 0 && burnt.Spawned)
            {
                burntDay = day;
                Assert.True(pump.State["cycles"] > cycles); // the cycles went up and up
                Assert.True(pump.State["cycles"] >= 150 || pump.State["runMinutes"] >= 300);
                Assert.Equal(FaultState.Latent, burnt.State);
                Assert.True(soaked.Dormant); // not awash yet
            }
            if (soaked.Spawned) soakedDay = day;
            if (!burnt.Spawned) cycles = (int)pump.State["cycles"];
            Assert.False(Status(g).Sunk);
        }
        Assert.True(burntDay > 0, "the pump never burnt out");
        Assert.True(soakedDay >= burntDay, "she cannot be awash before the pump gives up");
        Assert.True(FaultOf(boat, "drain-hose-failed").Dormant); // the rain alone did it, the hose never came off
        Assert.True(g.Values.Get(SystemValues.AwashHours) >= 2);
        Assert.Equal(FaultState.Latent, soaked.State);

        // Both are latent: not named, not in readiness.
        var all = Everything(g, results);
        Assert.DoesNotContain(PumpName, all, StringComparison.Ordinal);
        Assert.DoesNotContain(StarterSoaked, all, StringComparison.Ordinal);
        Assert.DoesNotContain(Ready(g).Open, p => p.Problem == StarterSoaked);

        // The soaked starter is a full fault once found.
        var inspect = Ok(Walk.Do(g, "starter motor", c => new Inspect(c)));
        Assert.Contains("tide line of bilge scum", Text(inspect));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == StarterSoaked);
        var item = Ready(g).Open.Single(p => p.Problem == StarterSoaked);
        Assert.Contains(item.Routes, r => r.Route == "DIY");
        Ok(g.Execute(new Defer(StarterSoaked)));
        Ok(g.Execute(new Undefer(StarterSoaked)));
        Ok(g.Execute(new Replace("starter motor")));
        Assert.False(soaked.Present);
        Assert.Contains(StarterSoaked, Ready(g).Fixed);
    }

    // ------------------------------------------------------------------ the stop command

    [Fact]
    public void Stopping_a_stopped_engine_is_refused_for_free()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        int minutes = Status(g).MinutesSpent, log = ((LogView)g.Ask(new Logbook())).Entries.Count;
        var r = g.Execute(new StopEngine());
        Assert.False(r.Accepted);
        Assert.Equal("The engine isn't running.", Text(r));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(minutes, Status(g).MinutesSpent);
        Assert.Equal(log, ((LogView)g.Ask(new Logbook())).Entries.Count);
    }

    [Fact]
    public void Stopping_a_running_engine_takes_a_minute_from_anywhere_aboard_and_it_can_be_started_again()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        ReadyToStart(g);
        Assert.True(Ok(Start(g)).EngineRunning);
        Walk.ToPlace(g, "saloon");
        int minutes = Status(g).MinutesSpent;
        var stop = Ok(g.Execute(new StopEngine()));
        Assert.Equal("stop-engine", stop.Action);
        Assert.Equal("You pull the stop and the engine dies.", Text(stop));
        Assert.Equal(1, stop.MinutesTaken);
        Assert.Equal(minutes + 1, Status(g).MinutesSpent);
        Assert.False(stop.EngineRunning);
        Assert.False(Status(g).EngineRunning);
        Assert.Contains(((CommandList)g.Ask(new Commands())).Commands, c => c.Verb == "stop");

        Assert.True(Ok(Start(g)).EngineRunning);
    }

    [Fact]
    public void The_stop_command_round_trips_through_json()
    {
        var json = JsonSerializer.Serialize<Command>(new StopEngine());
        Assert.Contains("\"type\":\"stop-engine\"", json);
        Assert.IsType<StopEngine>(JsonSerializer.Deserialize<Command>(json));
    }
}
