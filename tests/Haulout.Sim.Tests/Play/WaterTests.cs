using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Systems;
using Haulout.Sim.Weather;
using WeatherQuery = Haulout.Sim.Play.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The effect graph and the water model (design note 006, step 3), played through the facade where possible.</summary>
public class WaterTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string Weeping = "Weeping raw-water seacock";
    private const string StarterSoaked = "Starter soaked in bilge water";
    private const int Monsoon = 50;

    private static WeatherSeries Rain(double mm) => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", mm, 6, 3));

    private static Game NewGame(WeatherSeries weather, out Boat boat, int day = 75, bool calendar = true)
    {
        boat = BoatLoader.LoadSloop28(withCalendar: calendar);
        return new Game(boat, day, new FixedRandom(0.99), Game.DefaultSeed, weather);
    }

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static ReadinessView Ready(Game g) => (ReadinessView)g.Ask(new Readiness());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());

    private static void PullFuse(Game g)
    {
        Walk.Uncover(g, "bilge-pump-fuse");
        Walk.ToPlace(g, "cockpit");
    }

    private static void Launch(Game g) => Ok(g.Execute(new Launch()));

    private static void Wait(Game g, int days) => Ok(g.Execute(new WaitDays(days)));

    /// <summary>Wait a day at a time until she sinks (the card comes back as the result), up to a limit.</summary>
    private static (CommandResult Result, LossView Loss) WaitUntilSunk(Game g, int maxDays = 100)
    {
        for (int i = 0; i < maxDays; i++)
        {
            var r = Ok(g.Execute(new WaitDays(1)));
            if (r.Observation is LossView loss) return (r, loss);
        }
        throw new Xunit.Sdk.XunitException($"She did not sink in {maxDays} days.");
    }

    private static Fault FaultOf(Boat boat, string id) => boat.AllFaults.Single(f => f.Id == id);

    // ------------------------------------------------------------------ the effect graph

    private const string TinyBoat = """
        {
          "name": "Tiny",
          "places": [ { "id": "a", "name": "A" } ],
          "covers": [],
          "positions": [ { "id": "p", "label": "P" } ],
          "components": [
            { "id": "c", "name": "C", "subsystem": "hull", "position": "p", "condition": 1,
              "properties": { "x": 10 },
              "faults": [
                { "id": "first", "name": "First", "effects": [ { "kind": "property", "property": "x", "op": "ORDER_1", "value": ARG_1 } ] },
                { "id": "second", "name": "Second", "effects": [ { "kind": "property", "property": "x", "op": "ORDER_2", "value": ARG_2 } ] },
                { "id": "sleeper", "name": "Sleeper", "dormant": true, "effects": [ { "kind": "property", "property": "x", "op": "set", "value": 999 } ] },
                { "id": "elsewhere", "name": "Elsewhere", "effects": [ { "kind": "property", "property": "y", "op": "set", "value": 7, "component": "d" } ] }
              ] },
            { "id": "d", "name": "D", "subsystem": "hull", "position": "p", "condition": 1, "properties": { "y": 1 } }
          ]
        }
        """;

    private static Boat Tiny(string op1, double v1, string op2, double v2) =>
        BoatLoader.Load(TinyBoat.Replace("ORDER_1", op1).Replace("ARG_1", v1.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("ORDER_2", op2).Replace("ARG_2", v2.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Property_effects_apply_in_data_order()
    {
        // add 5 then multiply 2: (10 + 5) * 2.   multiply 2 then add 5: 10 * 2 + 5.
        Assert.Equal(30, Properties.Effective(Tiny("add", 5, "multiply", 2), "c", "x", 0));
        Assert.Equal(25, Properties.Effective(Tiny("multiply", 2, "add", 5), "c", "x", 0));
        // set then min caps it; min then set replaces it.
        Assert.Equal(4, Properties.Effective(Tiny("set", 8, "min", 4), "c", "x", 0));
        Assert.Equal(8, Properties.Effective(Tiny("min", 4, "set", 8), "c", "x", 0));
    }

    [Fact]
    public void Property_effects_target_their_component_and_ignore_dormant_and_fixed_faults()
    {
        var boat = Tiny("add", 5, "add", 1);
        // The sleeper (dormant, set 999) does nothing; the fault aimed at D changes D, not C.
        Assert.Equal(16, Properties.Effective(boat, "c", "x", 0));
        Assert.Equal(7, Properties.Effective(boat, "d", "y", 0));
        Assert.Equal(3, Properties.Effective(boat, "d", "nothing", 3));

        FaultOf(boat, "first").Clear();
        Assert.Equal(11, Properties.Effective(boat, "c", "x", 0));
        boat.Get("c").Replace();
        Assert.Equal(10, Properties.Effective(boat, "c", "x", 0));
    }

    [Fact]
    public void A_fault_can_carry_the_legacy_effect_and_a_list_of_effects()
    {
        var boat = BoatLoader.LoadSloop28();
        var seized = FaultOf(boat, "seacock-seized");
        Assert.Equal(FaultEffectKind.RawWaterFlowFactor, seized.Effect.Kind);
        Assert.Equal(2, seized.Effects.Count);
        Assert.Contains(seized.Effects, e => e is { Kind: FaultEffectKind.Property, Property: "leakLitresPerHour", Op: PropertyOp.Add });

        var hose = FaultOf(boat, "drain-hose-failed");
        Assert.Equal(FaultEffectKind.Property, hose.Effect.Kind); // no legacy effect: the first listed one
        Assert.Equal(2, hose.Effects.Count);
    }

    [Fact]
    public void Component_state_starts_as_the_spec_and_replace_puts_it_back()
    {
        var boat = BoatLoader.LoadSloop28();
        var pump = boat.Get("bilge-pump");
        Assert.Equal(20, pump.State["litresPerMinute"]);
        pump.State["cycles"] = 40;
        pump.State["litresPerMinute"] = 3;
        pump.Replace();
        Assert.Equal(20, pump.State["litresPerMinute"]);
        Assert.False(pump.State.ContainsKey("cycles"));
    }

    // ------------------------------------------------------------------ the bilge

    [Fact]
    public void A_weeping_seacock_afloat_with_no_pump_raises_the_level_at_the_weep_rate()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Launch(g);
        PullFuse(g);
        double before = g.BilgeLitres;
        Wait(g, 10);
        Assert.InRange(g.BilgeLitres - before, 11.9, 12.1); // 240 h at 0.05 L/h
    }

    [Fact]
    public void On_the_hard_a_weeping_seacock_lets_nothing_in()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        PullFuse(g);
        double before = g.BilgeLitres;
        Wait(g, 20);
        Assert.Equal(before, g.BilgeLitres, 6);
    }

    [Fact]
    public void With_the_pump_the_level_stays_below_the_float_switch_and_the_cycles_are_counted()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        Launch(g);
        for (int day = 0; day < 25; day++) // before the clamps let go and the hose comes off
        {
            Wait(g, 1);
            Assert.True(g.BilgeLitres <= 15.0, $"day {day}: {g.BilgeLitres}");
        }
        var pump = boat.Get("bilge-pump");
        Assert.InRange(pump.State["cycles"], 1, 4);
        Assert.Equal(pump.State["cycles"], g.Values.Get(SystemValues.PumpCycles));
        // Nobody aboard: each day's wait ends with the arrival alert, which reads the counter and starts it again.
        Assert.Contains(Log(g).Entries, e => e.Alert && e.Text.StartsWith("The bilge pump counter"));
        Assert.True(boat.Get("start-battery").State["stateOfCharge"] < 1.0); // the pump and the self-discharge took some
    }

    [Fact]
    public void Rain_through_the_drains_adds_water_and_none_once_the_clamps_are_replaced()
    {
        // Hatch shut both times, so only the drains let rain in. The clamps fault: 1.5 L per mm.
        var rusty = NewGame(Rain(10), out _);
        PullFuse(rusty);
        Ok(rusty.Execute(new Refit("hatch")));
        double before = rusty.BilgeLitres;
        Wait(rusty, 2);
        Assert.InRange(rusty.BilgeLitres - before, 29, 31);

        var fixedGame = NewGame(Rain(10), out _);
        PullFuse(fixedGame);
        Ok(Walk.Do(fixedGame, "cockpit drains", c => new Replace(c)));
        Ok(fixedGame.Execute(new Refit("hatch")));
        before = fixedGame.BilgeLitres;
        Wait(fixedGame, 2);
        Assert.Equal(before, fixedGame.BilgeLitres, 6);
    }

    [Fact]
    public void An_open_hatch_lets_rain_into_the_bilge()
    {
        var g = NewGame(Rain(10), out _);
        PullFuse(g);
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        double before = g.BilgeLitres;
        Wait(g, 2); // the hatch starts open: 0.5 L per mm
        Assert.InRange(g.BilgeLitres - before, 9.9, 10.1);
    }

    [Fact]
    public void The_pump_does_not_run_without_its_fuse_or_on_a_flat_battery()
    {
        var g = NewGame(Rain(Monsoon), out var boat);
        PullFuse(g);
        Wait(g, 1);
        Assert.True(g.BilgeLitres > 50);
        Assert.Equal(0, boat.Get("bilge-pump").State.GetValueOrDefault("cycles"));

        // Fuse back in but the battery flat: still nothing.
        Walk.ToPlace(g, "companionway");
        boat.Get("start-battery").State["stateOfCharge"] = 0.1;
        Ok(g.Execute(new Refit("fuse")));
        double level = g.BilgeLitres;
        Wait(g, 1);
        Assert.True(g.BilgeLitres > level);
        Assert.Equal(0, boat.Get("bilge-pump").State.GetValueOrDefault("cycles"));

        // Charged, it pumps again.
        boat.Get("start-battery").State["stateOfCharge"] = 1.0;
        Wait(g, 1);
        Assert.True(boat.Get("bilge-pump").State["cycles"] > 0);
    }

    [Fact]
    public void Charging_fills_the_live_charge()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        var battery = boat.Get("start-battery");
        battery.State["stateOfCharge"] = 0.3;
        Assert.Equal(0.3, Electrical.StateOfCharge(boat), 6);
        Walk.Do(g, "start battery", c => new Inspect(c));
        Ok(g.Execute(new Charge("start battery")));
        Assert.InRange(battery.State["stateOfCharge"], 0.998, 1.0); // full, less eight hours of self-discharge
        // The legacy fault was a cap: the charge fixed it.
        Assert.InRange(Electrical.StateOfCharge(boat), 0.998, 1.0);
    }

    [Fact]
    public void The_legacy_low_charge_fault_caps_the_live_charge()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(0.6, Electrical.StateOfCharge(boat), 6);
        boat.Get("start-battery").State["stateOfCharge"] = 0.4;
        Assert.Equal(0.4, Electrical.StateOfCharge(boat), 6);
        boat.Get("start-battery").Replace();
        Assert.Equal(1.0, Electrical.StateOfCharge(boat), 6);
    }

    // ------------------------------------------------------------------ words and the manual pump

    [Fact]
    public void Inspecting_the_bilge_gives_the_level_in_words()
    {
        var g = NewGame(Rain(1), out _);
        PullFuse(g);
        var dry = Ok(Walk.Do(g, "bilge", c => new Inspect(c)));
        Assert.Contains("dry, a damp sheen", Text(dry));

        Wait(g, 1);
        var finger = Ok(Walk.Do(g, "bilge", c => new Inspect(c)));
        Assert.Contains("a finger of water", Text(finger));
    }

    [Fact]
    public void Over_the_sole_the_bilge_says_so_and_so_does_the_saloon_with_the_floorboards_in()
    {
        var g = NewGame(Rain(Monsoon), out _);
        PullFuse(g);
        Wait(g, 3);
        Walk.ToPlace(g, "saloon");
        var look = Ok(g.Execute(new Look()));
        Assert.Contains("Water stands over the saloon sole.", Text(look));
        var inspect = Ok(Walk.Do(g, "bilge", c => new Inspect(c)));
        Assert.Contains("over the sole", Text(inspect));

        // And the way in says it too.
        Walk.ToPlace(g, "companionway");
        var go = Ok(g.Execute(new GoTo("saloon")));
        Assert.Contains("Water stands over the saloon sole.", Text(go));
    }

    [Fact]
    public void The_saloon_is_silent_while_the_water_is_below_the_sole()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Walk.ToPlace(g, "saloon");
        Assert.DoesNotContain("sole", Text(Ok(g.Execute(new Look()))), StringComparison.Ordinal);
    }

    [Fact]
    public void Pumping_by_hand_takes_water_out_from_the_cockpit()
    {
        var g = NewGame(Rain(Monsoon), out _);
        PullFuse(g);
        Wait(g, 1);
        double before = g.BilgeLitres;
        Assert.InRange(before, 60, 140);
        int minutes = Status(g).MinutesSpent;

        var r = Ok(g.Execute(new PumpBilge()));
        Assert.Equal(15, r.MinutesTaken);
        Assert.Equal(minutes + 15, Status(g).MinutesSpent);
        Assert.Contains("sucks air", Text(r));
        Assert.True(g.BilgeLitres < 2.0, g.BilgeLitres.ToString());
    }

    [Fact]
    public void Pumping_by_hand_moves_at_most_150_litres_and_says_the_water_still_comes()
    {
        var g = NewGame(Rain(Monsoon), out _);
        PullFuse(g);
        Wait(g, 4);
        double before = g.BilgeLitres;
        Assert.True(before > 300, before.ToString());
        var r = Ok(g.Execute(new PumpBilge()));
        Assert.Contains("the water still comes", Text(r));
        Assert.InRange(before - g.BilgeLitres, 140, 150); // 150 out, a little rain in meanwhile
    }

    [Fact]
    public void The_pump_handle_is_in_the_cockpit()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Walk.ToPlace(g, "companionway");
        int log = Log(g).Entries.Count, minutes = Status(g).MinutesSpent;
        var r = g.Execute(new PumpBilge());
        Assert.False(r.Accepted);
        Assert.Equal("The bilge pump handle is in the cockpit.", Text(r));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(log, Log(g).Entries.Count);
        Assert.Equal(minutes, Status(g).MinutesSpent);
        Assert.Contains(((CommandList)g.Ask(new Commands())).Commands, c => c.Verb == "pump bilge");
    }

    // ------------------------------------------------------------------ awash: faults that spawn

    [Fact]
    public void Staying_awash_spawns_the_soaked_faults_once_silently_and_they_can_be_found_deferred_and_fixed()
    {
        var g = NewGame(Rain(Monsoon), out var boat, calendar: false);
        PullFuse(g);
        var starter = FaultOf(boat, "starter-soaked");
        Assert.True(starter.Dormant);
        int logBefore = Log(g).Entries.Count;

        Wait(g, 3);
        Assert.True(g.Values.Get(SystemValues.AwashHours) >= 2);
        Assert.False(starter.Dormant);
        Assert.True(starter.Spawned);
        Assert.Equal(FaultState.Latent, starter.State);
        Assert.Equal(FaultState.Latent, FaultOf(boat, "battery-soaked").State);
        Assert.Equal(FaultState.Latent, FaultOf(boat, "ground-stud-soaked").State);

        // Silent: nothing in the log, nothing known, nothing in readiness.
        Assert.DoesNotContain(Log(g).Entries.Skip(logBefore), e => e.Text.Contains("soaked", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Status(g).DiscoveredFaults, k => k.Name == StarterSoaked);
        Assert.DoesNotContain(Ready(g).Open, p => p.Problem == StarterSoaked);

        // The effect is real before anyone has seen it.
        Assert.Equal(0.075, Properties.Effective(boat, "starter-motor", "seriesOhms", 0.045), 6);
        Assert.Equal(0.052, Properties.Effective(boat, "start-battery", "selfDischargePerDay", 0.002), 6);

        // Found by looking at the starter; then it is in readiness, deferrable and fixable.
        var inspect = Ok(Walk.Do(g, "starter motor", c => new Inspect(c)));
        Assert.Contains("tide line of bilge scum", Text(inspect));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == StarterSoaked);
        var item = Ready(g).Open.Single(p => p.Problem == StarterSoaked);
        Assert.Contains(item.Routes, r => r.Route == "DIY");
        Assert.False(string.IsNullOrEmpty(item.Consequence));

        Ok(g.Execute(new Defer(StarterSoaked)));
        Assert.Equal("Deferred", Ready(g).Open.Single(p => p.Problem == StarterSoaked).State);
        Ok(g.Execute(new Undefer(StarterSoaked)));

        Ok(g.Execute(new Replace("starter motor")));
        Assert.DoesNotContain(Ready(g).Open, p => p.Problem == StarterSoaked);
        Assert.Contains(StarterSoaked, Ready(g).Fixed);
        Assert.Equal(0.045, Properties.Effective(boat, "starter-motor", "seriesOhms", 0.045), 6);

        // It never spawns twice, however long she stays awash.
        Wait(g, 5);
        Assert.True(g.BilgeLitres > 120);
        Assert.False(starter.Present);
        Assert.Equal(FaultState.Fixed, starter.State);
    }

    [Fact]
    public void The_soaked_ground_stud_is_cleaned_and_the_battery_replaced()
    {
        var g = NewGame(Rain(Monsoon), out var boat);
        PullFuse(g);
        Wait(g, 3);
        Ok(Walk.Do(g, "ground stud", c => new Inspect(c)));
        Ok(g.Execute(new Clean("ground stud")));
        Assert.False(FaultOf(boat, "ground-stud-soaked").Present);

        Ok(Walk.Do(g, "start battery", c => new Inspect(c)));
        Assert.True(FaultOf(boat, "battery-soaked").Discovered);
        Ok(g.Execute(new Replace("start battery")));
        Assert.False(FaultOf(boat, "battery-soaked").Present);
        Assert.Equal(0.002, Properties.Effective(boat, "start-battery", "selfDischargePerDay", 0.002), 6);
    }

    [Fact]
    public void The_pump_burns_out_after_enough_cycles_and_a_new_one_starts_counting_again()
    {
        var g = NewGame(Rain(Monsoon), out var boat);
        Launch(g);
        var burnt = FaultOf(boat, "bilge-pump-burnt-out");
        var pump = boat.Get("bilge-pump");
        for (int day = 0; day < 60 && !burnt.Spawned; day++) Wait(g, 1);
        Assert.True(burnt.Spawned);
        Assert.True(pump.State["cycles"] >= 150 || pump.State["runMinutes"] >= 300);
        Assert.Equal(FaultState.Latent, burnt.State);
        Assert.Equal(0, Properties.Effective(boat, "bilge-pump", "litresPerMinute", 20)); // multiply 0

        var inspect = Ok(Walk.Do(g, "bilge pump", c => new Inspect(c)));
        Assert.Contains("hums but nothing moves", Text(inspect));
        Ok(g.Execute(new Replace("bilge pump")));
        Assert.False(burnt.Present);
        Assert.Equal(20, Properties.Effective(boat, "bilge-pump", "litresPerMinute", 0));
        Assert.True(pump.State.GetValueOrDefault("cycles") < 5); // the counter started again with the new pump
    }

    // ------------------------------------------------------------------ sinking

    [Fact]
    public void Sinking_ends_with_the_card_and_every_later_command_is_refused_while_queries_answer()
    {
        var g = NewGame(Rain(Monsoon), out _);
        Launch(g);
        PullFuse(g);
        var (result, loss) = WaitUntilSunk(g);

        Assert.True(result.Accepted);
        Assert.True(result.MinutesTaken is > 0 and <= 1440);
        Assert.Same(loss, result.Observation);
        Assert.Contains("another tired boat for sale", loss.Lines[^1]);
        Assert.Contains(loss.Lines, l => l.Contains("fuse was out", StringComparison.Ordinal));
        Assert.All(loss.Lines, l => Assert.Contains(l, result.Narrative));
        Assert.True(Status(g).Sunk);
        Assert.Contains(Log(g).Entries, e => e.Text.StartsWith("She sank.", StringComparison.Ordinal));

        int minutes = Status(g).MinutesSpent;
        foreach (Command c in new Command[] { new Look(), new WaitDays(3), new PumpBilge(), new Launch(), new Inspect("bilge") })
        {
            var r = g.Execute(c);
            Assert.False(r.Accepted);
            Assert.Equal("She's on the bottom of the harbour. The logbook is all that's left.", Text(r));
        }
        Assert.Equal(minutes, Status(g).MinutesSpent);
        Assert.IsType<WeatherView>(g.Ask(new WeatherQuery()));
        Assert.IsType<AboardView>(g.Ask(new Aboard()));
    }

    [Fact]
    public void The_loss_wins_over_the_observation_of_the_command_that_was_running()
    {
        var g = NewGame(Rain(Monsoon), out _);
        Launch(g);
        PullFuse(g);
        Walk.Near(g, "bilge");
        g.BilgeLitres = 1499.9;
        var r = Ok(g.Execute(new Inspect("bilge")));
        Assert.IsType<LossView>(r.Observation);
        Assert.True(r.MinutesTaken <= 5);
        Assert.True(Status(g).Sunk);
    }

    [Fact]
    public void Insurance_pays_the_fraction_of_her_value()
    {
        var g = NewGame(Rain(Monsoon), out _);
        Launch(g);
        PullFuse(g);
        var (_, loss) = WaitUntilSunk(g);
        Assert.False(loss.Voided);
        Assert.Null(loss.VoidReason);
        Assert.Equal(180000, loss.ValueNok);
        Assert.Equal(108000, loss.PayoutNok);
        Assert.Contains(loss.Lines, l => l.Contains("108000 NOK", StringComparison.Ordinal));
    }

    [Fact]
    public void Insurance_pays_nothing_when_the_before_launch_seacock_was_put_off()
    {
        var g = NewGame(Rain(Monsoon), out _);
        Ok(g.Execute(new OrderSurvey()));
        Ok(g.Execute(new Defer(Weeping)));
        Launch(g);
        PullFuse(g);
        var (_, loss) = WaitUntilSunk(g);
        Assert.True(loss.Voided);
        Assert.Equal(0, loss.PayoutNok);
        Assert.Contains("raw-water seacock", loss.VoidReason);
        Assert.EndsWith("and it was put off", loss.VoidReason);
        Assert.Contains(loss.Lines, l => l.Contains("pays nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void Insurance_pays_nothing_when_a_before_launch_item_was_left_open_and_never_put_off()
    {
        var g = NewGame(Rain(Monsoon), out _);
        Ok(g.Execute(new OrderSurvey()));
        Launch(g);
        PullFuse(g);
        var (_, loss) = WaitUntilSunk(g);
        Assert.True(loss.Voided);
        Assert.Equal(0, loss.PayoutNok);
        Assert.Contains("the surveyor had marked the raw-water seacock for attention before launch", loss.VoidReason);
        Assert.EndsWith("and it was never put right", loss.VoidReason);
    }

    [Fact]
    public void Insurance_names_the_deferral_when_one_item_was_put_off_and_another_left_open()
    {
        var g = NewGame(Rain(Monsoon), out var boat);
        Ok(g.Execute(new OrderSurvey()));
        // The seacock comes first in the report and is left open; the fuel is the one put off.
        FaultOf(boat, "water-in-fuel").Defer(75);
        Launch(g);
        PullFuse(g);
        var (_, loss) = WaitUntilSunk(g);
        Assert.Contains("fuel tank", loss.VoidReason);
        Assert.EndsWith("and it was put off", loss.VoidReason);
    }

    [Fact]
    public void Insurance_voids_for_a_before_launch_investigate_item_whose_fault_was_never_found()
    {
        var g = NewGame(Rain(Monsoon), out var boat);
        Ok(g.Execute(new OrderSurvey()));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        FaultOf(boat, "water-in-fuel").Clear();
        Launch(g);
        PullFuse(g);
        var (_, loss) = WaitUntilSunk(g);
        Assert.True(loss.Voided);
        Assert.Contains("starter motor", loss.VoidReason);
        Assert.EndsWith("and it was never put right", loss.VoidReason);
    }

    [Fact]
    public void Insurance_still_pays_when_every_before_launch_item_was_fixed_before_launch()
    {
        var fixedBefore = NewGame(Rain(Monsoon), out var boat);
        Ok(fixedBefore.Execute(new OrderSurvey()));
        Ok(fixedBefore.Execute(new Defer(Weeping)));
        Ok(Walk.Do(fixedBefore, "seacock", c => new Replace(c)));
        Ok(Walk.Do(fixedBefore, "negative terminal", c => new Clean(c)));
        FaultOf(boat, "neg-terminal-corrosion").Clear();
        FaultOf(boat, "water-in-fuel").Clear();
        Launch(fixedBefore);
        PullFuse(fixedBefore);
        var loss = WaitUntilSunk(fixedBefore).Loss;
        Assert.False(loss.Voided);
        Assert.Equal(108000, loss.PayoutNok);
    }

    [Fact]
    public void On_the_hard_she_cannot_sink_the_level_stops_at_the_sinking_mark()
    {
        var g = NewGame(Rain(Monsoon), out var boat);
        PullFuse(g);
        for (int i = 0; i < 40; i++)
        {
            var r = Ok(g.Execute(new WaitDays(1)));
            Assert.IsNotType<LossView>(r.Observation);
        }
        Assert.False(Status(g).Sunk);
        Assert.Equal(boat.Water.SinkLitres, g.BilgeLitres, 6);
        Ok(g.Execute(new Look()));
    }

    // ------------------------------------------------------------------ cheap interactions

    [Fact]
    public void Pulling_the_fuse_is_a_cover_with_its_own_words()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Walk.ToPlace(g, "companionway");
        var look = Ok(g.Execute(new Look()));
        Assert.Contains("The bilge pump fuse is in.", Text(look));
        var pulled = Ok(g.Execute(new Remove("pump fuse")));
        Assert.Equal("You pull the bilge pump fuse out of its holder.", Text(pulled));
        Assert.Equal(1, pulled.MinutesTaken);
        Assert.Contains("The bilge pump fuse is out.", Text(Ok(g.Execute(new Look()))));
        Ok(g.Execute(new Refit("fuse")));
    }

    [Fact]
    public void The_bilge_and_its_pump_are_seen_from_the_saloon_with_the_floorboards_up()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Walk.ToPlace(g, "saloon");
        Assert.DoesNotContain("Bilge pump", Text(Ok(g.Execute(new Look()))));
        var off = Ok(g.Execute(new Remove("floorboards")));
        Assert.Contains("Bilge pump", Text(off));
        var view = (AboardView)g.Ask(new Aboard());
        Assert.True(view.Components.Single(c => c.Name == "Bilge pump").ReachableHere);
        Assert.True(view.Components.Single(c => c.Name == "Bilge").ReachableHere);
    }

    [Fact]
    public void The_pump_cycles_with_the_fuse_in_on_the_hard_through_a_wet_spring()
    {
        var g = NewGame(Rain(5), out var boat);
        Walk.Uncover(g, "saloon-floorboards");
        Wait(g, 30);
        Assert.True(boat.Get("bilge-pump").State["cycles"] >= 1);
        Assert.True(g.BilgeLitres < 15);
    }
}
