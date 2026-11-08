using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Systems;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The passage, the at-sea tick, the at-sea commands, arrival and the tow (design note 007, step 2).</summary>
public class TripTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string ImpellerAlarm =
        "The temperature alarm whines and there's a smell of hot rubber from under the cockpit. Shut her down and check the cooling water at the exhaust.";
    private const string FuelAlert = "The engine coughs, picks up, coughs again and dies. Worth a look in the fuel filter bowl.";

    private static WeatherSeries Wind(int force, string from) =>
        WeatherSeries.Constant(new WeatherDay(0, force, from, 0, 12, 8));

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
    private static TripView Trip(Game g) => (TripView)g.Ask(new Trip());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());

    /// <summary>A fresh boat afloat with the engine running: terminal cleaned, seacock replaced (it seizes on launch otherwise), started, launched.</summary>
    private static Game Afloat(WeatherSeries weather, out Boat boat, bool newImpeller = false)
    {
        boat = BoatLoader.LoadSloop28();
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, weather);
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        if (newImpeller) Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Assert.True(Status(g).Finished);
        Assert.True(Status(g).EngineRunning);
        Assert.True(Status(g).Afloat);
        return g;
    }

    private static Game Afloat(WeatherSeries weather) => Afloat(weather, out _);

    private static CommandResult Depart(Game g)
    {
        Walk.ToPlace(g, "cockpit");
        return Ok(g.Execute(new Depart()));
    }

    private static LegView Leg(CommandResult r) => Assert.IsType<LegView>(r.Observation);

    private static TripResultView Card(CommandResult r) => Assert.IsType<TripResultView>(r.Observation);

    // ------------------------------------------------------------------ speed by wind

    [Fact]
    public void Force_4_from_the_southwest_serves_course_040_and_makes_five_knots_under_sail()
    {
        var g = Afloat(Wind(4, "southwest"));
        Depart(g);
        Assert.Equal("under engine", Trip(g).Mode);
        var r = Ok(g.Execute(new Sail()));
        var leg = Leg(r);
        Assert.Equal(60, r.MinutesTaken);
        Assert.Equal("under sail", leg.Mode);
        Assert.Equal(5.0, leg.PositionNm);
        Assert.Equal(9.0, leg.RemainingNm);
        Assert.Equal(1, leg.Hour);
        Assert.Equal(4, leg.WindForce);
        Assert.Equal("southwest", leg.WindFrom);
        Assert.Equal(3, leg.SeaState);       // force 4: a short chop
    }

    [Fact]
    public void Force_4_from_the_northeast_is_on_the_nose_and_Sail_is_refused_free_and_unlogged()
    {
        var g = Afloat(Wind(4, "northeast"));
        Depart(g);
        int entries = Log(g).Entries.Count;
        Assert.False(Trip(g).WindServes);
        var r = g.Execute(new Sail());
        Assert.Contains("on the nose", Assert.Single(r.Narrative));
        Assert.False(r.Accepted);
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(entries, Log(g).Entries.Count);
        Assert.Equal("under engine", Trip(g).Mode);
    }

    [Fact]
    public void Light_wind_and_a_gale_do_not_serve()
    {
        var calm = Afloat(Wind(2, "southwest"));
        Depart(calm);
        Assert.Contains("too little wind", Assert.Single(calm.Execute(new Sail()).Narrative));

        var strong = Afloat(Wind(6, "southwest"));
        Depart(strong);
        Assert.Contains("too rough to sail", Assert.Single(strong.Execute(new Sail()).Narrative));
    }

    [Fact]
    public void Motoring_makes_five_knots_in_any_wind()
    {
        var g = Afloat(Wind(4, "northeast"));
        Depart(g);
        var leg = Leg(Ok(g.Execute(new Motor())));
        Assert.Equal(5.0, leg.PositionNm);
        Assert.Equal("under engine", leg.Mode);
        var leg2 = Leg(Ok(g.Execute(new Continue())));
        Assert.Equal(10.0, leg2.PositionNm);
        Assert.Equal(2, leg2.Hour);
    }

    [Fact]
    public void The_hour_line_has_the_notes_format_and_goes_into_the_narrative_and_the_logbook()
    {
        var g = Afloat(Wind(4, "southwest"));
        Depart(g);
        var r = Ok(g.Execute(new Continue()));
        const string line = "Hour 1. Force 4 from the southwest, a short chop from astern. The engine note is steady; the bilge pump has stayed quiet. 9.0 nm to Hestholmen.";
        Assert.Equal(line, Leg(r).Text);
        Assert.Equal(line, Assert.Single(r.Narrative));
        Assert.Equal(line, Log(g).Entries.Last().Text);
        Assert.Equal(new[] { "Left Skjærvik for Hestholmen.", line }, Log(g).Entries.TakeLast(2).Select(e => e.Text));
    }

    [Fact]
    public void The_trip_query_says_where_she_is_with_the_nearest_shelter_in_words()
    {
        var g = Afloat(Wind(4, "southwest"));
        var port = Trip(g);
        Assert.False(port.AtSea);
        Assert.Equal("Skjærvik", port.Port);
        Depart(g);
        Ok(g.Execute(new Continue()));
        var t = Trip(g);
        Assert.True(t.AtSea);
        Assert.Null(t.Port);
        Assert.Equal("Skjærvik", t.From);
        Assert.Equal("Hestholmen", t.To);
        Assert.Equal(5.0, t.PositionNm);
        Assert.Equal(9.0, t.RemainingNm);
        Assert.Equal("under engine", t.Mode);
        Assert.False(t.CanAnchor);
        Assert.Equal("Lyngskjær, 2.0 nm ahead", t.NearestShelter);
        Assert.True(t.WindServes);
        Assert.True(Status(g).AtSea);
        Assert.Null(Status(g).Port);
    }

    // ------------------------------------------------------------------ the hourly tick

    [Fact]
    public void The_impeller_sheds_vanes_only_under_load()
    {
        var g = Afloat(Wind(3, "southwest"), out var boat);
        var impeller = boat.Get("impeller");
        Assert.Equal(1.0, impeller.State["vaneFactor"]);
        Depart(g);
        Ok(g.Execute(new Motor()));
        double afterLoad = impeller.State["vaneFactor"];
        Assert.InRange(afterLoad, 0.75, 0.85); // 0.3 x (1 - 0.35) shed an hour

        Ok(g.Execute(new HeaveTo()));
        Ok(g.Execute(new Continue()));  // engine idling, hove-to: no load
        Assert.Equal(afterLoad, impeller.State["vaneFactor"]);

        Ok(g.Execute(new Sail()));      // engine still idling, under sail: no load
        Assert.Equal(afterLoad, impeller.State["vaneFactor"]);

        Ok(g.Execute(new Motor()));
        Assert.True(impeller.State["vaneFactor"] < afterLoad);
    }

    [Theory]
    [InlineData(4, true, 60)]   // sea state 3, water in the fuel, engine running
    [InlineData(3, true, 0)]    // sea state 2: not stirred
    [InlineData(4, false, 0)]   // engine stopped
    public void Water_minutes_accrue_only_in_sea_state_3_with_water_in_the_fuel_and_the_engine_running(int force, bool running, double expected)
    {
        var g = Afloat(Wind(force, "southwest"), out var boat);
        Depart(g);
        if (!running)
        {
            Ok(g.Execute(new HeaveTo()));
            Ok(g.Execute(new StopEngine()));
        }
        var filter = boat.Get("primary-filter");
        double before = filter.State["waterMinutes"];
        Ok(g.Execute(new Continue()));
        // Heave-to and stop took a few minutes when the engine was running; the leg itself is an hour.
        Assert.Equal(expected, filter.State["waterMinutes"] - before, 6);
    }

    [Fact]
    public void Without_water_in_the_fuel_the_sea_stirs_nothing()
    {
        var g = Afloat(Wind(5, "southwest"), out var boat);
        foreach (var f in boat.Get("fuel-tank").Faults) f.Clear();
        Depart(g);
        Ok(g.Execute(new Motor()));
        Assert.Equal(0, boat.Get("primary-filter").State["waterMinutes"]);
    }

    [Fact]
    public void A_leak_lets_in_more_water_in_a_chop_and_spray_finds_the_open_hatch()
    {
        // The water system on its own: a weeping seacock (0.05 L/h), step by step.
        double Ingress(int seaState, bool hatchOpen)
        {
            var boat = BoatLoader.LoadSloop28();
            var water = new WaterSystem(boat);
            double start = water.Level;
            water.Step(new WaterSystem.Inputs(60, true, 0, hatchOpen, true, seaState));
            return water.Level - start;
        }

        double calm = Ingress(0, false);
        Assert.InRange(calm, 0.049, 0.051);
        Assert.InRange(Ingress(4, false), 0.149, 0.151);       // x (1 + 0.5 x 4)
        Assert.InRange(Ingress(1, true), 0.074, 0.076);        // below the spray threshold: the slop only
        Assert.InRange(Ingress(4, true), 8.149, 8.151);        // spray: 2 L/h x 4 through the open hatch
        Assert.InRange(Ingress(4, false), 0.149, 0.151);       // hatch shut: none
    }

    [Fact]
    public void Spray_comes_in_through_the_hatch_under_way_and_not_at_the_mooring()
    {
        var g = Afloat(Wind(4, "southwest"), out var boat);
        // The hatch is open from the start. At the mooring an hour passes with next to nothing coming in.
        double before = g.BilgeLitres;
        Ok(g.Execute(new PumpBilge()));
        Walk.ToPlace(g, "cockpit");
        before = g.BilgeLitres;
        Depart(g);
        Ok(g.Execute(new Continue()));
        Assert.True(g.BilgeLitres - before > 5, $"expected spray, bilge went {before} -> {g.BilgeLitres}");

        // Hatch shut: the same hour lets in no spray.
        var h = Afloat(Wind(4, "southwest"));
        Walk.ToPlace(h, "cockpit");
        Ok(h.Execute(new Refit("companionway hatch")));
        double beforeShut = h.BilgeLitres;
        Depart(h);
        Ok(h.Execute(new Continue()));
        Assert.True(h.BilgeLitres - beforeShut < 1);
    }

    // ------------------------------------------------------------------ (a) the tired impeller

    [Fact]
    public void A_deferred_tired_impeller_overheats_in_hour_2_and_the_spare_gets_her_to_Hestholmen()
    {
        var g = Afloat(Wind(3, "southwest"), out var boat);

        // The player finds the impeller, puts it off, and buys a spare to carry.
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        Assert.Contains(Ok(g.Execute(new Inspect("impeller"))).NewlyDiscovered, k => k.Name == "Tired impeller");
        Ok(g.Execute(new Defer("impeller")));
        Ok(g.Execute(new Buy("impeller")));
        var plan = (TripPlanView)g.Ask(new PlanTrip());
        Assert.True(plan.Risks.Single(r => r.Component == "Raw-water impeller").SpareAboard);

        Depart(g);
        var hour1 = Ok(g.Execute(new Continue()));
        Assert.DoesNotContain(hour1.Alerts, a => a.Text == ImpellerAlarm);
        var hour2 = Ok(g.Execute(new Continue()));
        Assert.Contains(hour2.Alerts, a => a.Text == ImpellerAlarm);
        Assert.Contains("temperature alarm whines", Leg(hour2).Text);
        Assert.Equal(2, Leg(hour2).Hour);
        Assert.Equal(10.0, Leg(hour2).PositionNm);

        // Heave to, stop, uncover and replace from the spares.
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HeaveTo()));
        Ok(g.Execute(new StopEngine()));
        var replaced = Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Assert.Contains("from the spares aboard", replaced.Narrative[0]);
        Assert.Equal(0, replaced.CostNok);
        Assert.Equal(1.0, boat.Get("impeller").State["vaneFactor"]);

        // Start, motor on, arrive.
        Ok(Walk.Start(g));
        var arrive = Ok(g.Execute(new Motor()));
        var card = Card(arrive);
        Assert.Equal("Hestholmen", card.Destination);
        Assert.False(card.Towed);
        Assert.False(card.TurnedBack);
        Assert.Contains(card.Lines, l => l.Contains("regatta party"));
        Assert.Contains("You pick up a mooring and stop the engine.", arrive.Narrative);
        Assert.Contains(arrive.Narrative, l => l.StartsWith("Hour ", StringComparison.Ordinal) && l.EndsWith("0.0 nm to Hestholmen.", StringComparison.Ordinal));
        Assert.Equal(0, card.MoneySpentNok); // the spare was bought before she cast off
        Assert.Contains(ImpellerAlarm, card.Failures);
        Assert.Contains("Replaced the raw-water impeller from the spares aboard.", card.Fixes);
        Assert.InRange(card.Minutes, 3 * 60, 6 * 60);
        Assert.Contains(card.Log, e => e.Text.StartsWith("Hour 2.", StringComparison.Ordinal));
        Assert.Equal("Left Skjærvik for Hestholmen.", card.Log[0].Text);
        Assert.Equal("Arrived at Hestholmen.", card.Log[^1].Text);

        var status = Status(g);
        Assert.False(status.AtSea);
        Assert.Equal("Hestholmen", status.Port);
        Assert.False(status.EngineRunning);
        Assert.False(Trip(g).AtSea);
        Assert.Equal("Hestholmen", Trip(g).Port);
    }

    [Fact]
    public void Left_running_the_impeller_alarm_ends_in_the_engine_stopping_itself()
    {
        var g = Afloat(Wind(3, "southwest"));
        Depart(g);
        Ok(g.Execute(new Continue()));
        Ok(g.Execute(new Continue()));  // alarm in hour 2
        var hour3 = Ok(g.Execute(new Continue()));
        Assert.Contains(hour3.Alerts, a => a.Text == "Steam from the exhaust, and the engine labours and stops by itself. She's too hot to run.");
        Assert.False(hour3.EngineRunning);
        Assert.Contains("The engine is silent", hour3.Narrative[0]);
        Assert.Contains("Steam from the exhaust, and the engine labours and stops by itself. She's too hot to run.", Card(hour3).Failures);
    }

    // ------------------------------------------------------------------ (b) water in the fuel

    private static Game WaterInTheFuelAtSea(out Boat boat)
    {
        var g = Afloat(Wind(4, "northeast"), out boat);
        // Known and put off, no spare filter bought.
        Assert.Contains(Ok(Walk.Do(g, "primary filter", c => new CheckFilterBowl())).NewlyDiscovered, k => k.Name == "Water in the fuel tank");
        Ok(g.Execute(new Defer("water in the fuel tank")));
        Depart(g);
        Ok(g.Execute(new Continue()));
        return g;
    }

    [Fact]
    public void Water_in_the_fuel_bites_at_the_end_of_hour_2_in_force_4_and_the_engine_dies()
    {
        var g = WaterInTheFuelAtSea(out var boat);
        Assert.Equal(60, boat.Get("primary-filter").State["waterMinutes"]);
        Assert.True(Status(g).EngineRunning);
        var hour2 = Ok(g.Execute(new Continue()));
        Assert.Contains(hour2.Alerts, a => a.Text == FuelAlert);
        Assert.False(hour2.EngineRunning);
        Assert.Contains("The engine is silent", Leg(hour2).Text);
        Assert.Equal("drifting", Leg(hour2).Mode);
        // Motor is refused while the engine is dead.
        No(g.Execute(new Motor()), "The engine isn't running: start it first.");
        // Starting it again fires and dies.
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HeaveTo()));
        var start = Ok(g.Execute(new StartEngine()));
        Assert.False(start.EngineRunning);
    }

    [Fact]
    public void Without_a_spare_filter_Replace_at_sea_is_refused_and_names_what_you_lack()
    {
        var g = WaterInTheFuelAtSea(out _);
        Ok(g.Execute(new Continue()));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HeaveTo()));
        int entries = Log(g).Entries.Count;
        Walk.Near(g, "primary filter");
        entries = Log(g).Entries.Count;
        var r = g.Execute(new Replace("primary filter"));
        Assert.False(r.Accepted);
        Assert.Equal("You don't have a spare primary filter element aboard.", Assert.Single(r.Narrative));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        Assert.Equal(entries, Log(g).Entries.Count);
    }

    [Fact]
    public void A_tow_costs_6000_and_a_day_and_ends_at_Skjaervik()
    {
        var g = WaterInTheFuelAtSea(out _);
        Ok(g.Execute(new Continue()));
        int balance = Status(g).BalanceNok;
        int day = Status(g).Day;

        var r = Ok(g.Execute(new CallTow()));
        var card = Card(r);
        Assert.Equal(6000, r.CostNok);
        Assert.Equal((6 + 18) * 60, r.MinutesTaken);
        Assert.True(card.Towed);
        Assert.False(card.TurnedBack);
        Assert.Equal("Skjærvik", card.Destination);
        Assert.Equal(6000, card.MoneySpentNok);
        Assert.Contains(FuelAlert, card.Failures);
        Assert.Equal(2, card.Lines.Count);
        Assert.DoesNotContain(card.Lines, l => l.Contains("party"));
        Assert.Contains(card.Log, e => e.Text.Contains("towboat") && e.Text.Contains("6000"));

        var s = Status(g);
        Assert.False(s.AtSea);
        Assert.Equal("Skjærvik", s.Port);
        Assert.Equal(balance - 6000, s.BalanceNok);
        Assert.Equal(day + 1, s.Day);
        Assert.False(s.EngineRunning);
        Assert.False(Trip(g).AtSea);
    }

    [Fact]
    public void A_tow_is_allowed_into_a_negative_balance_and_the_game_goes_on()
    {
        var g = Afloat(Wind(3, "southwest"));
        while (Status(g).BalanceNok >= 6000) Ok(g.Execute(new Buy("hose")));
        Depart(g);
        int balance = Status(g).BalanceNok;
        Assert.InRange(balance, 0, 5999);

        var r = Ok(g.Execute(new CallTow()));
        Assert.True(Card(r).Towed);
        Assert.Equal(balance - 6000, Status(g).BalanceNok);
        Assert.True(Status(g).BalanceNok < 0);
        Assert.False(g.Execute(new Buy("hose")).Accepted);
        var ready = (ReadinessView)g.Ask(new Readiness());
        Assert.True(ready.BalanceNok < 0);
        Assert.Equal(balance - 6000, ((TripPlanView)g.Ask(new PlanTrip())).BalanceNok);
    }

    [Fact]
    public void Turning_back_under_sail_gets_her_home_to_Skjaervik()
    {
        var g = WaterInTheFuelAtSea(out _);
        Ok(g.Execute(new Continue()));   // the engine dies at the end of hour 2, 10 nm out
        Walk.ToPlace(g, "cockpit");
        var turn = Ok(g.Execute(new TurnBack()));
        Assert.Equal(2, turn.MinutesTaken);
        var t = Trip(g);
        Assert.Equal("Hestholmen", t.From);
        Assert.Equal("Skjærvik", t.To);
        Assert.InRange(t.RemainingNm, 9.9, 10.5);
        Assert.True(t.WindServes);        // northeast wind, course 220: from astern
        Assert.False(g.Execute(new TurnBack()).Accepted);

        var r = Ok(g.Execute(new Sail()));
        Assert.Equal("under sail", Leg(r).Mode);
        TripResultView? card = null;
        for (int i = 0; i < 5 && card is null; i++)
            card = Ok(g.Execute(new Continue())).Observation as TripResultView;
        Assert.NotNull(card);
        Assert.Equal("Skjærvik", card!.Destination);
        Assert.True(card.TurnedBack);
        Assert.False(card.Towed);
        Assert.Contains(FuelAlert, card.Failures);
        Assert.Equal("Skjærvik", Status(g).Port);
    }

    // ------------------------------------------------------------------ (c) the belt

    [Fact]
    public void A_slipping_belt_means_no_charging_under_way_the_lamp_and_then_the_battery_alert_in_hour_2()
    {
        var g = Afloat(Wind(3, "southwest"), out var boat, newImpeller: true);
        Depart(g);
        double start = Electrical.StateOfCharge(boat);
        var hour1 = Ok(g.Execute(new Continue()));
        // The lamp was lit when the engine first ran that morning (kinds are raised once a day), and it is in the logbook.
        Assert.Contains(Log(g).Entries, e => e.Alert && e.Text.StartsWith("The alternator lamp on the panel stays lit", StringComparison.Ordinal));
        Assert.DoesNotContain(hour1.Alerts, a => a.Text.StartsWith("The panel voltmeter reads", StringComparison.Ordinal));
        double afterOne = Electrical.StateOfCharge(boat);
        Assert.True(afterOne < start);

        var hour2 = Ok(g.Execute(new Continue()));
        var battery = Assert.Single(hour2.Alerts, a => a.Text.StartsWith("The panel voltmeter reads", StringComparison.Ordinal));
        Assert.Contains("battery isn't holding its charge", battery.Text);
        Assert.True(Electrical.StateOfCharge(boat) < afterOne);
        Assert.True(Electrical.StateOfCharge(boat) < 0.5);
    }

    [Fact]
    public void A_healthy_boat_gains_a_little_charge_an_hour_under_engine()
    {
        var g = Afloat(Wind(3, "southwest"), out var boat, newImpeller: true);
        foreach (var f in boat.Get("alternator-belt").Faults) f.Clear();
        Depart(g);
        double before = Electrical.StateOfCharge(boat);
        double live = boat.Get("start-battery").State["stateOfCharge"];
        Ok(g.Execute(new Continue()));
        Assert.True(boat.Get("start-battery").State["stateOfCharge"] > Math.Min(live, 0.6) - 0.001);
        Assert.True(Electrical.StateOfCharge(boat) >= before - 0.001);
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void Replace_at_sea_without_the_spare_is_refused_free_and_unlogged()
    {
        var g = Afloat(Wind(3, "southwest"));
        Depart(g);
        Ok(g.Execute(new Continue()));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HeaveTo()));
        Ok(g.Execute(new StopEngine()));
        Walk.Near(g, "impeller");
        int entries = Log(g).Entries.Count;
        int balance = Status(g).BalanceNok;
        No(g.Execute(new Replace("impeller")), "You don't have a spare impeller aboard.");
        Assert.Equal(entries, Log(g).Entries.Count);
        Assert.Equal(balance, Status(g).BalanceNok);
    }

    [Fact]
    public void Buy_wait_charge_and_the_yard_are_refused_at_sea()
    {
        var g = Afloat(Wind(3, "southwest"));
        Depart(g);
        int entries = Log(g).Entries.Count;
        No(g.Execute(new Buy("impeller")), "The chandlery is ashore; out here you have what's aboard.");
        No(g.Execute(new WaitDays(1)), "You're at sea: continue, heave to or anchor.");
        No(g.Execute(new OrderSurvey()), "You're at sea.");
        No(g.Execute(new Launch()), "You're at sea.");
        No(g.Execute(new EmergencyLift()), "You're at sea.");
        No(g.Execute(new Charge("start battery")), "You're at sea.");
        No(g.Execute(new FixByYard("tired impeller")), "You don't know of a problem called 'tired impeller'.");
        Assert.Equal(entries, Log(g).Entries.Count);
        var inventory = (InventoryView)g.Ask(new Inventory());
        Assert.Null(inventory.Shop);
        Assert.Empty(inventory.ForSale);
    }

    [Fact]
    public void Engine_and_parts_work_at_sea_needs_her_hove_to_or_at_anchor_and_the_engine_stopped()
    {
        var g = Afloat(Wind(3, "southwest"));
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        Ok(g.Execute(new Buy("impeller")));
        Depart(g);
        Walk.Near(g, "impeller");
        const string under = "Heave to or anchor first: you can't work on the engine under way.";
        No(g.Execute(new Uncover("impeller")), under);
        No(g.Execute(new Measure("impeller")), under);
        No(g.Execute(new Clean("impeller")), under);
        No(g.Execute(new Replace("impeller")), under);   // under way comes before the spare
        var options = ((ComponentActionsView)g.Ask(new ComponentActions("impeller"))).Options;
        Assert.Equal(under, options.Single(o => o.Verb == "replace").UnavailableReason);

        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HeaveTo()));
        Walk.Near(g, "impeller");
        const string stop = "Stop the engine first.";
        No(g.Execute(new Replace("impeller")), stop);
        Assert.Equal(stop, ((ComponentActionsView)g.Ask(new ComponentActions("impeller"))).Options.Single(o => o.Verb == "replace").UnavailableReason);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new StopEngine()));
        Walk.Near(g, "impeller");
        Assert.True(((ComponentActionsView)g.Ask(new ComponentActions("impeller"))).Options.Single(o => o.Verb == "replace").Available);
        Ok(g.Execute(new Replace("impeller")));
    }

    [Fact]
    public void Anchor_works_only_within_a_mile_of_shelter()
    {
        var g = Afloat(Wind(3, "southwest"));
        Depart(g);
        Ok(g.Execute(new Continue()));    // 5 nm out: Lyngskjær is 2 nm ahead
        No(g.Execute(new Anchor()), "Nowhere to anchor here: Lyngskjær is 2.0 nm ahead.");
        Assert.False(Trip(g).CanAnchor);

        Ok(g.Execute(new PumpBilge()));   // 15 minutes at 5 knots: 6.25 nm, 0.75 nm from Lyngskjær
        Assert.True(Trip(g).CanAnchor);
        var r = Ok(g.Execute(new Anchor()));
        Assert.Equal(15, r.MinutesTaken);
        Assert.Equal("at anchor", Trip(g).Mode);
        No(g.Execute(new Anchor()), "You're already at anchor.");
        No(g.Execute(new HeaveTo()), "You're at anchor: there's nothing to heave to.");

        // Work is allowed once the engine is stopped, and Motor weighs anchor.
        Ok(g.Execute(new StopEngine()));
        Ok(g.Execute(new StartEngine()));
        var leg = Leg(Ok(g.Execute(new Motor())));
        Assert.Equal("under engine", leg.Mode);
    }

    [Fact]
    public void Right_after_casting_off_she_can_anchor_at_the_port_she_left()
    {
        var g = Afloat(Wind(3, "southwest"));
        Depart(g);
        Assert.True(Trip(g).CanAnchor);
        Ok(g.Execute(new Anchor()));
    }

    [Fact]
    public void Heaving_to_drifts_her_back_and_costs_time()
    {
        var g = Afloat(Wind(3, "southwest"));
        Depart(g);
        Ok(g.Execute(new Continue()));
        Ok(g.Execute(new HeaveTo()));
        Ok(g.Execute(new StopEngine()));
        double remaining = Trip(g).RemainingNm;
        var leg = Leg(Ok(g.Execute(new Continue())));
        Assert.Equal("hove-to", leg.Mode);
        Assert.True(leg.RemainingNm > remaining + 0.4);  // half a knot for an hour
        Assert.True(leg.PositionNm < 5.0);                // net progress: drift taken off
        No(g.Execute(new HeaveTo()), "You're already hove-to.");
    }

    [Fact]
    public void Helm_commands_need_the_player_in_the_cockpit_and_at_sea()
    {
        var g = Afloat(Wind(3, "southwest"));
        No(g.Execute(new Continue()), "You're not at sea.");
        No(g.Execute(new CallTow()), "You're not at sea.");
        Walk.ToPlace(g, "companionway");
        No(g.Execute(new Depart()), "The tiller is in the cockpit.");
        Walk.ToPlace(g, "cockpit");
        Depart(g);
        No(g.Execute(new Depart()), "You're already at sea.");
        Walk.ToPlace(g, "companionway");
        foreach (Command c in new Command[] { new Continue(), new Motor(), new Sail(), new HeaveTo(), new Anchor(), new TurnBack() })
            No(g.Execute(c), "The tiller is in the cockpit.");
        // The towboat is on the VHF, not at the tiller.
        Assert.True(g.Execute(new CallTow()).Accepted);
    }

    [Fact]
    public void Depart_is_refused_on_the_hard_before_the_first_run_with_the_engine_stopped_in_a_gale_and_to_nowhere()
    {
        var boat = BoatLoader.LoadSloop28();
        var hard = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Wind(3, "southwest"));
        No(hard.Execute(new Depart()), "She's still on the hard.");

        var g = Afloat(Wind(3, "southwest"));
        No(g.Execute(new Depart("Atlantis")), "There's no route from Skjærvik to 'Atlantis'.");
        Ok(g.Execute(new StopEngine()));
        No(g.Execute(new Depart()), "Start the engine first: you'll motor off the mooring.");

        var gale = Afloat(Wind(5, "southwest"));
        var windy = BoatLoader.LoadSloop28();
        var gg = new Game(windy, 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.From(d => new WeatherDay(0, d >= 78 ? 7 : 3, "southwest", 0, 12, 8)));
        Ok(Walk.Do(gg, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(gg, "seacock", c => new Replace(c)));
        Ok(Walk.Start(gg));
        Ok(gg.Execute(new Launch()));
        if (!Status(gg).EngineRunning) Ok(Walk.Start(gg));
        Ok(gg.Execute(new WaitDays(3)));
        Ok(Walk.Start(gg));
        Assert.Contains("It's blowing too hard to leave", Assert.Single(gg.Execute(new Depart()).Narrative));
        Assert.NotNull(gale);
    }

    [Fact]
    public void Depart_names_the_destination_by_alias()
    {
        var g = Afloat(Wind(3, "southwest"));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart("the regatta")));
        Assert.Equal("Hestholmen", Trip(g).To);
    }

    [Fact]
    public void The_way_back_from_Hestholmen_is_the_reversed_route()
    {
        var g = Afloat(Wind(3, "southwest"), out _, newImpeller: true);
        Depart(g);
        TripResultView? card = null;
        for (int i = 0; i < 6 && card is null; i++) card = Ok(g.Execute(new Motor())).Observation as TripResultView;
        Assert.Equal("Hestholmen", card!.Destination);
        var plan = (TripPlanView)g.Ask(new PlanTrip());
        Assert.Equal("Hestholmen", plan.From);
        Assert.Equal("Skjærvik", plan.To);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Depart()));
        Assert.Equal("Skjærvik", Trip(g).To);
    }

    // ------------------------------------------------------------------ sinking at sea

    [Fact]
    public void She_can_go_down_at_sea_and_the_card_says_where()
    {
        var g = Afloat(Wind(4, "southwest"), out var boat);
        // The pump fuse out, the water nearly to the sinking point, the spray coming in.
        Walk.ToPlace(g, "companionway");
        Ok(g.Execute(new Remove("bilge pump fuse")));
        Walk.ToPlace(g, "cockpit");
        Depart(g);
        g.BilgeLitres = 1495;
        var r = g.Execute(new Continue());
        var loss = Assert.IsType<LossView>(r.Observation);
        Assert.Contains("nm out from Skjærvik on day", loss.Lines[0]);
        Assert.StartsWith("She went down ", loss.Lines[0]);
        Assert.DoesNotContain("at her mooring", loss.Lines[0]);
        Assert.Contains(r.Narrative, l => l.StartsWith("Hour 1.", StringComparison.Ordinal));
        Assert.True(Status(g).Sunk);
        Assert.NotNull(boat);
    }

    // ------------------------------------------------------------------ nothing leaks

    [Fact]
    public void Views_of_a_trip_carry_no_fault_ids_and_no_latent_fault_names()
    {
        var g = Afloat(Wind(4, "northeast"), out var boat);
        var seen = new List<string>();
        void Keep(CommandResult r) => seen.Add(JsonSerializer.Serialize(r));
        void Snap()
        {
            seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())));
            seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Trip())));
            seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new PlanTrip())));
            seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook())));
            seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Readiness())));
            seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Notices())));
            seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Inventory())));
        }

        Keep(Ok(Walk.Do(g, "impeller", c => new Uncover(c))));
        Keep(Ok(g.Execute(new Inspect("impeller"))));
        Keep(Ok(g.Execute(new Defer("impeller"))));
        Snap();
        Keep(Depart(g));
        Snap();
        for (int i = 0; i < 2; i++) { Keep(Ok(g.Execute(new Continue()))); Snap(); }
        Keep(Ok(g.Execute(new HeaveTo())));
        Keep(Ok(g.Execute(new TurnBack())));
        Snap();
        Keep(g.Execute(new Replace("impeller")));
        Keep(g.Execute(new Buy("hose")));
        Keep(g.Execute(new Sail()));
        for (int i = 0; i < 5; i++) { var r = Ok(g.Execute(new Continue())); Keep(r); Snap(); if (r.Observation is TripResultView) break; }
        Snap();

        var json = string.Concat(seen);
        foreach (var f in boat.AllFaults)
        {
            Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
            if (!f.Discovered) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
        }
    }

    [Fact]
    public void A_tow_and_the_sea_commands_leak_nothing_either()
    {
        var g = Afloat(Wind(4, "northeast"), out var boat);
        var seen = new List<string>();
        seen.Add(JsonSerializer.Serialize(Depart(g)));
        seen.Add(JsonSerializer.Serialize(Ok(g.Execute(new Continue()))));
        seen.Add(JsonSerializer.Serialize(Ok(g.Execute(new Continue()))));
        seen.Add(JsonSerializer.Serialize(Ok(g.Execute(new CallTow()))));
        seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Trip())));
        seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())));
        seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook())));
        var json = string.Concat(seen);
        foreach (var f in boat.AllFaults)
        {
            Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
            if (!f.Discovered) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
        }
    }

    [Fact]
    public void The_new_commands_are_in_the_catalogue_and_serialise_by_their_names()
    {
        var verbs = ((CommandList)new Game(BoatLoader.LoadSloop28()).Ask(new Commands())).Commands.Select(c => c.Verb).ToList();
        foreach (var verb in new[] { "depart", "continue", "motor", "sail", "heave to", "anchor", "turn back", "call tow", "trip" })
            Assert.Contains(verb, verbs);

        foreach (var (command, name) in new (Command, string)[]
        {
            (new Depart("x"), "depart"), (new Continue(), "continue"), (new Motor(), "motor"), (new Sail(), "sail"),
            (new HeaveTo(), "heave-to"), (new Anchor(), "anchor"), (new TurnBack(), "turn-back"), (new CallTow(), "call-tow"),
        })
        {
            var json = JsonSerializer.Serialize(command);
            Assert.Contains($"\"type\":\"{name}\"", json);
            Assert.Equal(command.GetType(), JsonSerializer.Deserialize<Command>(json)!.GetType());
        }
        Assert.Contains("\"type\":\"trip\"", JsonSerializer.Serialize<Query>(new Trip()));
        Assert.Contains("\"type\":\"trip-view\"", JsonSerializer.Serialize<QueryResult>(new Game(BoatLoader.LoadSloop28()).Ask(new Trip())));
    }
}
