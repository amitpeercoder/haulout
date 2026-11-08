using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>Upgrades are components (design note 010, step 3): ordered, fitted by the player or the yard, and what they open.</summary>
public class UpgradeTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static readonly string[] UpgradeIds =
        ["diesel-heater", "marine-heads", "galley-stove", "chartplotter", "radar", "bunk-cushions", "cockpit-varnish"];

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static CommandResult No(CommandResult r, string text)
    {
        Assert.False(r.Accepted);
        Assert.Equal(text, Assert.Single(r.Narrative));
        Assert.Equal((0, 0), (r.MinutesTaken, r.CostNok));
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static CalendarView Cal(Game g) => (CalendarView)g.Ask(new Calendar());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static UpgradesView Ups(Game g) => (UpgradesView)g.Ask(new Upgrades());
    private static UpgradeView Up(Game g, string name) => Ups(g).Items.Single(i => i.Name == name);
    private static WeatherSeries Clear() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Boat Edited(Action<JsonNode> edit, bool withCalendar = true)
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        edit(node);
        return BoatLoader.Load(node.ToJsonString(), withCalendar);
    }

    /// <summary>Afloat at Skjærvik on <paramref name="day"/> with the engine running (terminal cleaned, seacock and impeller new).</summary>
    private static Game Afloat(int day, Boat? boat = null, WeatherSeries? weather = null, bool lightsFixed = false)
    {
        boat ??= BoatLoader.LoadSloop28();
        var g = new Game(boat, day, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Clear());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        if (lightsFixed) Ok(Walk.Do(g, "navigation lights", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        return g;
    }

    /// <summary>On the hard, as she starts, with no calendar: items are bought off the shelf.</summary>
    private static Game OnTheHard(Boat? boat = null, WeatherSeries? weather = null) =>
        new(boat ?? BoatLoader.LoadSloop28(withCalendar: false), 75, new FixedRandom(0.99), Game.DefaultSeed, weather ?? Clear());

    private static int DueDay(Game g, string name)
    {
        var m = Regex.Match(Up(g, name).Status, @"due day (\d+)");
        Assert.True(m.Success, Up(g, name).Status);
        return int.Parse(m.Groups[1].Value);
    }

    /// <summary>Order the item and wait day by day until it is aboard.</summary>
    private static void Deliver(Game g, string name)
    {
        Ok(g.Execute(new Order(name)));
        for (int i = 0; i < 12 && Up(g, name).Status != "aboard, not fitted"; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.Equal("aboard, not fitted", Up(g, name).Status);
    }

    private static void Install(Game g, string place, string name)
    {
        Walk.ToPlace(g, place);
        Ok(g.Execute(new Install(name)));
    }

    // ------------------------------------------------------------------ the data

    [Fact]
    public void Seven_upgrades_load_are_not_components_until_fitted_and_each_has_one_chandlery_item()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(48, boat.Components.Count);
        Assert.Equal(UpgradeIds.OrderBy(i => i), boat.Upgrades.Select(u => u.Component.Id).OrderBy(i => i));
        foreach (var id in UpgradeIds)
        {
            Assert.False(boat.IsFitted(id));
            Assert.False(boat.TryGet(id, out _));
            Assert.True(boat.TryGetUpgrade(id, out var u));
            Assert.Contains(boat.Positions, p => p.Id == u.Component.PositionId);
            Assert.Contains(boat.Places, p => p.Id == u.PlaceId);
            var item = Assert.Single(boat.Chandlery, i => i.Upgrade == id);
            Assert.False(item.Stocked);
            Assert.Equal(item.PriceNok, u.Component.ReplaceCostNok);
            Assert.All(u.Component.Reach, r => Assert.Contains(boat.Places, p => p.Id == r.PlaceId));
        }
        Assert.Equal(
            [("diesel-heater", 14000, 600, 4000, 3, 8000), ("marine-heads", 6500, 480, 3000, 2, 3500), ("galley-stove", 4500, 240, 1500, 1, 2000),
             ("chartplotter", 9000, 240, 1500, 1, 5000), ("radar", 24000, 480, 5000, 3, 12000)],
            UpgradeIds.Take(5).Select(id =>
            {
                boat.TryGetUpgrade(id, out var u);
                return (id, u.Component.ReplaceCostNok, u.InstallMinutes, u.YardNok ?? 0, u.YardDays ?? 0, u.ValueNok);
            }));
        boat.TryGetUpgrade("bunk-cushions", out var cushions);
        boat.TryGetUpgrade("cockpit-varnish", out var varnish);
        Assert.Equal((5500, 30, null, 2000), (cushions.Component.ReplaceCostNok, cushions.InstallMinutes, cushions.YardNok, cushions.ValueNok));
        Assert.Equal((900, 480, null, 4000, true), (varnish.Component.ReplaceCostNok, varnish.InstallMinutes, varnish.YardNok, varnish.ValueNok, varnish.Component.Spec.NeedsDry));
    }

    [Fact]
    public void The_friends_requirements_name_real_upgrades()
    {
        var node = JsonNode.Parse(Sloop28Json())!;
        var friend = node["calendar"]!["events"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "friend-week")!;
        var needed = friend["requires"]!.AsArray().Select(r => r!["component"]!.GetValue<string>()).ToList();
        Assert.Equal(["marine-heads", "galley-stove", "bunk-cushions", "diesel-heater"], needed);
        Assert.All(needed, id => Assert.Contains(id, UpgradeIds));
    }

    [Fact]
    public void Fitting_appends_the_component_at_the_end_registers_it_and_cannot_happen_twice()
    {
        var boat = BoatLoader.LoadSloop28();
        var last = boat.Components[^1];
        Assert.Equal("anchor-gear", last.Id);
        boat.Fit("bunk-cushions");
        Assert.Equal(49, boat.Components.Count);
        Assert.Equal("bunk-cushions", boat.Components[^1].Id);
        Assert.True(boat.IsFitted("bunk-cushions"));
        Assert.Same(boat.Components[^1], boat.Get("bunk-cushions"));
        Assert.Throws<InvalidOperationException>(() => boat.Fit("bunk-cushions"));
        Assert.Throws<KeyNotFoundException>(() => boat.Fit("start-battery"));
    }

    [Fact]
    public void The_loader_checks_the_upgrades_items_and_requirements()
    {
        // The price of an upgrade's replacement is its item's: without the item the component has no price and the kind check says so.
        var noItem = Assert.Throws<ArgumentException>(() => Edited(n =>
            n["chandlery"]!.AsArray().Remove(n["chandlery"]!.AsArray().Single(i => i!["id"]!.GetValue<string>() == "radar"))));
        Assert.Contains("Component 'radar'", noItem.Message);

        var strayItem = Assert.Throws<InvalidOperationException>(() => Edited(n =>
            n["chandlery"]!.AsArray().Add(JsonNode.Parse("""{ "id": "sonar", "name": "sonar", "priceNok": 100, "upgrade": "sonar" }"""))));
        Assert.Contains("which is not an upgrade", strayItem.Message);

        var twoItems = Assert.Throws<InvalidOperationException>(() => Edited(n =>
            n["chandlery"]!.AsArray().Add(JsonNode.Parse("""{ "id": "radar-2", "name": "second radar", "priceNok": 100, "upgrade": "radar" }"""))));
        Assert.Contains("more than one chandlery item", twoItems.Message);

        var strayEvent = Assert.Throws<InvalidOperationException>(() => Edited(n =>
            n["calendar"]!["events"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "friend-week")!["requires"]!.AsArray()[0]!["component"] = "sauna"));
        Assert.Contains("neither a component nor an upgrade", strayEvent.Message);

        var badPlace = Assert.Throws<ArgumentException>(() => Edited(n => n["upgrades"]!.AsArray()[0]!["place"] = "attic"));
        Assert.Contains("unknown place 'attic'", badPlace.Message);

        var halfYard = Assert.Throws<ArgumentException>(() => Edited(n => n["upgrades"]!.AsArray()[0]!.AsObject().Remove("yardDays")));
        Assert.Contains("both yardNok and yardDays", halfYard.Message);
    }

    // ------------------------------------------------------------------ ordering and installing

    [Fact]
    public void An_upgrade_is_ordered_like_a_part_waits_in_the_query_comes_aboard_and_is_fitted_where_it_goes()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = Afloat(77, boat);
        Assert.Equal("not aboard", Up(g, "bunk cushions").Status);
        int balance = Status(g).BalanceNok;

        var order = Ok(g.Execute(new Order("cushions")));
        Assert.Equal(5500, order.CostNok);
        Assert.Equal(balance - 5500, Status(g).BalanceNok);
        int due = DueDay(g, "bunk cushions");
        Assert.InRange(due - Status(g).Day, 3, 10);

        // Not aboard yet: the order is named.
        Walk.ToPlace(g, "saloon");
        No(g.Execute(new Install("bunk cushions")), $"On order: the bunk cushions, due on day {due}.");

        for (int i = 0; i < 12 && Up(g, "bunk cushions").Status != "aboard, not fitted"; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.Equal("aboard, not fitted", Up(g, "bunk cushions").Status);
        Assert.Equal(due, Status(g).Day);

        // Aboard, but not where it goes.
        Walk.ToPlace(g, "cockpit");
        No(g.Execute(new Install("bunk cushions")), "Fitting the bunk cushions is done in the saloon: go there first.");
        Walk.ToPlace(g, "saloon");
        var r = Ok(g.Execute(new Install("bunk cushions")));
        Assert.Equal((30, 0), (r.MinutesTaken, r.CostNok));
        Assert.Contains("You lay the new cushions along the saloon berths", Text(r));
        var card = Assert.IsType<RepairView>(r.Observation);
        Assert.Equal("Bunk cushions", card.ComponentName);
        Assert.True(boat.IsFitted("bunk-cushions"));
        Assert.Equal(49, boat.Components.Count);
        Assert.Equal($"fitted on day {Status(g).Day}", Up(g, "bunk cushions").Status);
        Assert.Contains(Log(g).Entries, e => e.Text == "Fitted the bunk cushions.");
        Assert.DoesNotContain(((InventoryView)g.Ask(new Inventory())).Aboard, l => l.Name.Contains("cushions"));

        // Now it is an ordinary component: found, seen, inspectable from where it is.
        var found = ((AboardView)g.Ask(new Aboard())).Components.Single(f => f.Name == "Bunk cushions");
        Assert.Equal("Saloon berths", found.Position);
        Assert.True(found.VisibleHere && found.ReachableHere);
        Ok(g.Execute(new Inspect("cushions")));

        No(g.Execute(new Install("bunk cushions")), "Already fitted: the bunk cushions.");
    }

    [Fact]
    public void Install_is_refused_at_sea_with_nothing_aboard_for_a_spare_and_in_the_wet_all_free_and_unlogged()
    {
        var g = Afloat(77);
        Walk.ToPlace(g, "forepeak");
        int entries = Log(g).Entries.Count;
        No(g.Execute(new Install("marine heads")), "You don't have the marine heads aboard. Order one: order marine heads.");
        No(g.Execute(new Install("impeller")), "The impeller is a spare to replace a worn part with: replace it.");
        No(g.Execute(new Install("remote start button")), "The remote start button is not something to fit to the boat.");
        No(g.Execute(new Install("anchor")), "The chandlery doesn't have 'anchor'.");
        Assert.Equal(entries, Log(g).Entries.Count);

        var sea = Afloat(77);
        Walk.ToPlace(sea, "cockpit");
        Ok(sea.Execute(new Depart("hestholmen")));
        No(sea.Execute(new Install("marine heads")), "You're at sea: fit it in harbour.");
        No(sea.Execute(new YardInstall("marine heads")), "You're at sea.");

        // Varnish needs a dry day.
        var rain = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 4, 10, 6));
        var wet = OnTheHard(weather: rain);
        Ok(wet.Execute(new Buy("varnish")));
        Walk.ToPlace(wet, "cockpit");
        var no = wet.Execute(new Install("varnish"));
        Assert.False(no.Accepted);
        Assert.Contains("the varnish and brushes won't take in the wet. Wait for a dry day.", Text(no));
        Assert.Equal((0, 0), (no.MinutesTaken, no.CostNok));
        var dry = OnTheHard();
        Ok(dry.Execute(new Buy("varnish")));
        Walk.ToPlace(dry, "cockpit");
        Assert.Equal(480, Ok(dry.Execute(new Install("varnish"))).MinutesTaken);
    }

    [Fact]
    public void A_long_install_on_a_weekday_is_a_vacation_day_by_the_rule_of_step_one_and_costs_no_money()
    {
        var g = Afloat(77);
        Deliver(g, "diesel heater");
        while (new GameClock(Status(g).Day).IsWeekend) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "quarter-berth");
        int left = Status(g).VacationDaysLeft;
        int balance = Status(g).BalanceNok;
        var r = Ok(g.Execute(new Install("heater")));
        Assert.Equal(600, r.MinutesTaken);
        Assert.Contains("becomes a vacation day", Text(r));
        Assert.Equal(left - 1, Status(g).VacationDaysLeft);
        Assert.Equal(balance, Status(g).BalanceNok);
    }

    [Fact]
    public void The_same_install_on_a_saturday_costs_no_vacation_day()
    {
        var g = Afloat(75);   // Saturday
        Deliver(g, "marine heads");
        while (new GameClock(Status(g).Day).Weekday != 5) Ok(g.Execute(new WaitDays(1)));
        int left = Status(g).VacationDaysLeft;
        Walk.ToPlace(g, "forepeak");
        Assert.Equal(480, Ok(g.Execute(new Install("heads"))).MinutesTaken);
        Assert.Equal(left, Status(g).VacationDaysLeft);
    }

    [Fact]
    public void The_upgrades_query_follows_an_item_from_the_shelf_to_the_hull()
    {
        var g = Afloat(77);
        var items = Ups(g).Items;
        Assert.Equal(7, items.Count);
        Assert.Equal(
            ["diesel heater", "marine heads", "two-burner stove", "chart plotter", "marine radar", "bunk cushions", "varnish and brushes"],
            items.Select(i => i.Name));
        var radar = items.Single(i => i.Name == "marine radar");
        Assert.Equal((24000, 480, 5000, 3, "fog at full speed", 12000, "not aboard"),
            (radar.PriceNok, radar.InstallMinutes, radar.YardNok, radar.YardDays, radar.Opens, radar.ValueNok, radar.Status));
        Assert.Null(items.Single(i => i.Name == "bunk cushions").YardNok);

        Ok(g.Execute(new Order("stove")));
        Assert.StartsWith("on order, due day ", Up(g, "two-burner stove").Status);
        for (int i = 0; i < 12 && !Up(g, "two-burner stove").Status.StartsWith("aboard", StringComparison.Ordinal); i++) Ok(g.Execute(new WaitDays(1)));
        Assert.Equal("aboard, not fitted", Up(g, "two-burner stove").Status);
        Install(g, "companionway", "stove");
        Assert.StartsWith("fitted on day ", Up(g, "two-burner stove").Status);
        Assert.Equal(7, ((SeasonView)g.Ask(new Haulout.Sim.Play.Season())).Upgrades!.Count);
    }

    [Fact]
    public void A_part_that_came_in_while_she_was_away_waits_at_the_yard_and_goes_aboard_when_she_is_back()
    {
        var g = Afloat(77);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Order("marine heads")));
        int due = DueDay(g, "marine heads");
        Ok(g.Execute(new Depart("hestholmen")));
        var r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 20 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        for (int i = 0; i < 12 && Status(g).Day < due; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.True(Status(g).Day >= due);
        Assert.Equal("waiting at the yard", Up(g, "marine heads").Status);

        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        Ok(g.Execute(new Depart("skjaervik")));
        r = Ok(g.Execute(new Motor()));
        for (int i = 0; i < 20 && r.Observation is not TripResultView; i++) r = Ok(g.Execute(new Continue()));
        Ok(g.Execute(new Look()));
        Assert.Equal("aboard, not fitted", Up(g, "marine heads").Status);
    }

    // ------------------------------------------------------------------ the yard

    [Fact]
    public void The_yard_fits_the_part_the_player_brought_for_labour_only_and_takes_days()
    {
        // Step 4a review fix: the yard no longer supplies the part (money skipped the order's wait); the part must be aboard.
        var boat = BoatLoader.LoadSloop28();
        var g = Afloat(77, boat);
        Deliver(g, "two-burner stove");
        int balance = Status(g).BalanceNok;
        int day = Status(g).Day;
        var r = Ok(g.Execute(new YardInstall("two-burner stove")));
        Assert.Equal(1500, r.CostNok);
        Assert.Equal(1440, r.MinutesTaken);
        Assert.Equal(balance - 1500, Status(g).BalanceNok);
        Assert.Equal(day + 1, Status(g).Day);
        Assert.True(boat.IsFitted("galley-stove"));
        Assert.Contains("The yard fits the two-burner stove you brought: 1500 NOK of labour. 1 day later it's done.", Text(r));
        Assert.Contains(Log(g).Entries, e => e.Text == "The yard fitted the two-burner stove." && e.CostNok == 1500);
        Assert.Equal($"fitted on day {Status(g).Day}", Up(g, "two-burner stove").Status);
        No(g.Execute(new YardInstall("stove")), "Already fitted: the two-burner stove.");

        // A bigger job: labour only, and 3 days.
        Deliver(g, "marine radar");
        int before = Status(g).BalanceNok;
        int d2 = Status(g).Day;
        var radar = Ok(g.Execute(new YardInstall("radar")));
        Assert.Equal(5000, radar.CostNok);
        // The yard's three days can cross a payday or a bill (day 90 is both): the balance moves by the labour and whatever the calendar booked.
        int calendar = Cal(g).Money.Recent.Where(e => e.Day > d2 && e.Nok != -5000).Sum(e => e.Nok);
        Assert.Equal(before - 5000 + calendar, Status(g).BalanceNok);
        Assert.Equal(d2 + 3, Status(g).Day);
        Assert.Contains("The yard fits the marine radar you brought: 5000 NOK of labour. 3 days later it's done.", Text(radar));
        Assert.True(boat.IsFitted("radar"));
        Assert.Equal(49 + 1, boat.Components.Count);
    }

    [Fact]
    public void The_yard_refuses_a_job_that_is_yours_a_part_on_order_a_boat_that_is_not_at_the_yard_and_an_empty_purse()
    {
        var g = Afloat(77);
        No(g.Execute(new YardInstall("bunk cushions")), "That's a job for you, not the yard.");
        No(g.Execute(new YardInstall("varnish")), "That's a job for you, not the yard.");
        // Money does not skip the order's wait: the yard needs the part first.
        No(g.Execute(new YardInstall("diesel heater")),
            "The yard needs the part first: order it (order diesel heater), and they'll fit it when it's here.");
        Assert.Equal(0, Log(g).Entries.Count(e => e.Text.Contains("yard fitted")));
        Ok(g.Execute(new Order("diesel heater")));
        string onOrder = $"On order already: the diesel heater, due on day {DueDay(g, "diesel heater")}. Ask the yard again once it has come in.";
        No(g.Execute(new YardInstall("heater")), onOrder);

        var away = Afloat(77);
        Walk.ToPlace(away, "cockpit");
        Ok(away.Execute(new Depart("hestholmen")));
        var r = Ok(away.Execute(new Motor()));
        for (int i = 0; i < 20 && r.Observation is not TripResultView; i++) r = Ok(away.Execute(new Continue()));
        No(away.Execute(new YardInstall("chart plotter")), "The yard is at Skjærvik.");

        // Without a calendar there is no overdraft: the radar (24000) leaves 1000, and the yard's 5000 of labour is more than that.
        var broke = OnTheHard();
        Ok(broke.Execute(new Buy("marine radar")));
        var no = broke.Execute(new YardInstall("radar"));
        Assert.False(no.Accepted);
        Assert.Contains("5000", Text(no));
        Assert.Equal((0, 0), (no.MinutesTaken, no.CostNok));
    }

    [Fact]
    public void The_yard_can_fit_her_on_the_hard_too()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = OnTheHard(boat);
        No(g.Execute(new YardInstall("chart plotter")), "The yard needs the part first: buy it (buy chart plotter), and they'll fit it.");
        Ok(g.Execute(new Buy("chart plotter")));
        Ok(g.Execute(new YardInstall("chart plotter")));
        Assert.True(boat.IsFitted("chartplotter"));
    }

    // ------------------------------------------------------------------ fog

    private static WeatherSeries FogAllDay() =>
        WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: 24));

    private static Game FoggyAfloat(out Boat boat, params string[] fitted)
    {
        boat = Edited(n => n["economy"]!["startingBalanceNok"] = 60000, withCalendar: false);   // a radar is 24000
        // Launched in the clear (the fog starts on day 102), and the lights mended so that the dark costs nothing.
        var weather = WeatherSeries.From(d => new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: d >= 102 ? 24 : 0));
        var g = Afloat(100, boat, weather, lightsFixed: true);
        foreach (var f in fitted)
        {
            Ok(g.Execute(new Buy(f)));
            var place = Regex.IsMatch(f, "radar") ? "on-deck" : "cockpit";
            Install(g, place, f);
        }
        // Wait into the fog (day 102, ten in the morning).
        while (Status(g).Day < 102) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "cockpit");
        boat.Get("fuel-tank").State["fuelLitres"] = 60;
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        return g;
    }

    private static double Leg(CommandResult r, ref double position)
    {
        var leg = Assert.IsType<LegView>(r.Observation);
        double delta = leg.PositionNm - position;
        position = leg.PositionNm;
        return delta;
    }

    [Fact]
    public void Without_a_plotter_or_radar_the_harbourmaster_holds_her_and_a_fitted_plotter_or_radar_lets_her_out()
    {
        var held = FoggyAfloat(out _);
        var wall = held.Execute(new Depart());
        Assert.False(wall.Accepted);
        Assert.Contains("harbourmaster won't let a boat out without a plotter or radar", Text(wall));

        foreach (var item in new[] { "chart plotter", "marine radar" })
        {
            var g = FoggyAfloat(out var boat, item);
            Assert.Equal(item == "chart plotter" ? "chartplotter" : "radar", boat.Components[^1].Id);
            Ok(g.Execute(new Depart()));
        }
    }

    [Fact]
    public void A_fitted_plotter_caps_her_in_fog_at_three_and_a_half_knots_and_a_fitted_radar_does_not_cap_her_at_all()
    {
        double Hour(Game g, ref double position)
        {
            Ok(g.Execute(new Depart()));
            var r = Ok(g.Execute(new Motor()));
            return Leg(r, ref position);
        }

        var plotter = FoggyAfloat(out _, "chart plotter");
        double p = 0;
        Assert.Equal(3.5, Hour(plotter, ref p), 6);
        var more = Ok(plotter.Execute(new Continue()));
        Assert.Equal(3.5, Leg(more, ref p), 6);
        Assert.Contains("Fog: you creep along at 3.5 knots, listening.", Text(more));

        var radar = FoggyAfloat(out _, "marine radar");
        double q = 0;
        Assert.InRange(Hour(radar, ref q), 4.5, 5.0);   // five knots less a little growth on the hull
        Assert.DoesNotContain("creep", Text(Ok(radar.Execute(new Continue()))));
    }

    [Fact]
    public void A_fitted_plotter_with_a_fault_on_it_is_no_plotter_at_all()
    {
        var boat = Edited(n =>
        {
            var plotter = n["upgrades"]!.AsArray().Single(u => u!["component"]!["id"]!.GetValue<string>() == "chartplotter")!["component"]!.AsObject();
            plotter["faults"] = JsonNode.Parse("""
                [ { "id": "plotter-dead", "name": "Dead chart plotter", "effect": { "kind": "none", "value": 0 },
                    "reveals": [ { "action": "inspect", "text": "The screen is black.", "discovers": true } ],
                    "fixedBy": ["replace"], "consequence": "No plotter.", "costLaterNok": 0,
                    "diy": { "how": "Fit a new plotter", "minutes": 60 } } ]
                """);
        }, withCalendar: false);
        var weather = WeatherSeries.From(d => new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: d >= 102 ? 24 : 0));
        var g = Afloat(100, boat, weather, lightsFixed: true);
        Ok(g.Execute(new Buy("plotter")));
        Install(g, "cockpit", "plotter");
        while (Status(g).Day < 102) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        Assert.False(g.Execute(new Depart()).Accepted);   // fitted, but with a present fault: the harbourmaster is not impressed
        Ok(g.Execute(new Replace("chart plotter")));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Assert.True(g.Execute(new Depart()).Accepted);
    }

    // ------------------------------------------------------------------ what they open: the friend's week

    private static (string Status, Game Game) FriendWeek(WeatherSeries weather, params string[] items)
    {
        var g = Afloat(180, weather: weather);
        foreach (var i in items) Ok(g.Execute(new Order(i)));
        for (int i = 0; i < 12 && items.Any(n => Up(g, n).Status != "aboard, not fitted"); i++) Ok(g.Execute(new WaitDays(1)));
        foreach (var i in items)
        {
            Assert.Equal("aboard, not fitted", Up(g, i).Status);
            var place = i switch { "marine heads" => "forepeak", "two-burner stove" => "companionway", "bunk cushions" => "saloon", _ => "quarter-berth" };
            Install(g, place, i);
        }
        int start = Math.Max(Status(g).Day + 3, 190);
        Ok(g.Execute(new InviteFriend(start)));
        Ok(g.Execute(new WaitDays(Math.Min(60, start - Status(g).Day + 1))));
        var ev = Cal(g).Events!.Single(e => e.Name == "A friend's week aboard");
        return (ev.Status, g);
    }

    [Fact]
    public void Heads_stove_and_cushions_make_the_friends_week_on_the_shipped_data_in_a_mild_july()
    {
        var (status, g) = FriendWeek(Clear(), "marine heads", "two-burner stove", "bunk cushions");
        Assert.Equal("made", status);
        Assert.Contains(Log(g).Entries, e => e.Text == "Made it: A friend's week aboard.");
        Assert.All(Cal(g).Events!.Single(e => e.Name == "A friend's week aboard").Requirements, r => Assert.True(r.Met));
    }

    [Fact]
    public void Without_one_of_the_three_the_friend_goes_home_and_the_fault_in_the_week_is_named()
    {
        var (status, g) = FriendWeek(Clear(), "marine heads", "bunk cushions");
        Assert.Equal("failed: a stove that works", status);
        Assert.Contains(Log(g).Entries, e => e.Text == "Missed: A friend's week aboard (a stove that works).");
    }

    [Fact]
    public void In_a_cold_july_the_week_needs_the_heater_too()
    {
        var cold = WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 6, 3));
        var (without, _) = FriendWeek(cold, "marine heads", "two-burner stove", "bunk cushions");
        Assert.Equal("failed: warm enough below, if the week is cold", without);
        var (with, _) = FriendWeek(cold, "marine heads", "two-burner stove", "bunk cushions", "diesel heater");
        Assert.Equal("made", with);
    }

    // ------------------------------------------------------------------ the broker's letter

    [Fact]
    public void The_letter_adds_a_line_for_each_fitted_upgrade_at_its_condition_to_the_hundred()
    {
        var g = OnTheHard();
        var before = ((SaleView)g.Ask(new Sale())).Letter!;
        Ok(g.Execute(new Buy("heater")));
        Install(g, "quarter-berth", "heater");
        var fresh = ((SaleView)g.Ask(new Sale())).Letter!;
        Assert.Contains(new ValuationLine("Diesel heater, fitted this season", 8000), fresh.Lines);
        // The seasonal factor of the letter applies to the total, as it always did: 8000 NOK of heater is 8320 NOK of price in spring.
        Assert.Equal(fresh.ValueNok, fresh.Lines.Sum(l => l.Nok));
        Assert.InRange(fresh.ValueNok - before.ValueNok, 8300, 8350);

        // 90 days later the heater has worn by 90 / 3600 of its life: 8000 x 0.975 = 7800.
        Ok(g.Execute(new WaitDays(45)));
        Ok(g.Execute(new WaitDays(45)));
        var aged = ((SaleView)g.Ask(new Sale())).Letter!;
        Assert.Contains(aged.Lines, l => l.Text == "Diesel heater, fitted this season" && l.Nok == 7800);
        Assert.Equal(aged.ValueNok, aged.Lines.Sum(l => l.Nok));
    }

    [Fact]
    public void The_letter_calls_an_upgrade_from_an_earlier_season_by_that_name()
    {
        var g = OnTheHard();
        Ok(g.Execute(new Buy("cushions")));
        Install(g, "saloon", "cushions");
        Ok(g.Execute(new Launch()));
        Ok(g.Execute(new Keep()));
        Assert.Contains(((SaleView)g.Ask(new Sale())).Letter!.Lines, l => l.Text == "Bunk cushions, fitted earlier");
    }

    // ------------------------------------------------------------------ the seeds

    [Fact]
    public void A_fit_moves_no_existing_faults_spread_progress_or_the_finding_of_a_later_viewing()
    {
        (Game G, Boat B) Make(bool fit)
        {
            var boat = BoatLoader.LoadSloop28(withCalendar: false);
            var g = OnTheHard(boat);
            if (fit)
            {
                Ok(g.Execute(new Buy("cushions")));
                Install(g, "saloon", "cushions");   // 30 minutes of work, the day she starts
            }
            else
            {
                Walk.ToPlace(g, "cockpit");
                Ok(g.Execute(new PumpBilge()));
                Ok(g.Execute(new PumpBilge()));
            }
            return (g, boat);
        }

        var (a, boatA) = Make(false);
        var (b, boatB) = Make(true);
        Assert.Equal(
            boatA.AllFaults.Select(f => (f.Id, f.Spread)).ToList(),
            boatB.AllFaults.Select(f => (f.Id, f.Spread)).ToList());

        foreach (var g in new[] { a, b })
        {
            Ok(g.Execute(new WaitDays(40)));
            var letter = ((SaleView)g.Ask(new Sale())).Letter!;
            Ok(g.Execute(new ListForSale(letter.ValueNok)));
            for (int i = 0; i < 4; i++) Ok(g.Execute(new WaitDays(10)));
        }
        Assert.Equal(
            boatA.AllFaults.Select(f => (f.Id, f.Spread, f.Progress, f.Present, f.State)).ToList(),
            boatB.AllFaults.Select(f => (f.Id, f.Spread, f.Progress, f.Present, f.State)).ToList());

        var viewsA = ((SaleView)a.Ask(new Sale())).Viewings;
        var viewsB = ((SaleView)b.Ask(new Sale())).Viewings;
        Assert.NotEmpty(viewsA);
        Assert.Equal(viewsA.Select(v => (v.Day, v.Findings)).ToList(), viewsB.Select(v => (v.Day, v.Findings)).ToList());
        // Only the value differs: the cushions' 2000 NOK, with the spring's 4 % on the total.
        Assert.All(viewsA.Zip(viewsB), p => Assert.InRange(p.Second.ValueNok - p.First.ValueNok, 2000, 2100));
    }

    // ------------------------------------------------------------------ views and wire

    [Fact]
    public void The_upgrade_views_and_results_carry_no_ids()
    {
        var g = Afloat(77);
        var ids = UpgradeIds.Where(i => i.Contains('-')).ToList();
        Ok(g.Execute(new Order("marine heads")));
        Deliver(g, "two-burner stove");
        var yard = Ok(g.Execute(new YardInstall("two-burner stove")));
        string[] json =
        [
            JsonSerializer.Serialize<QueryResult>(Ups(g)),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Haulout.Sim.Play.Season())),
            JsonSerializer.Serialize(yard),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Aboard())),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Inventory())),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Calendar())),
        ];
        foreach (var j in json)
            foreach (var id in ids)
                Assert.DoesNotContain($"\"{id}\"", j);
        Assert.DoesNotContain("\"chartplotter\"", json[0]);
        Assert.DoesNotContain("\"radar\"", json[0]);
    }

    [Fact]
    public void The_new_commands_and_query_round_trip_through_json()
    {
        Assert.Equal(new Install("diesel heater"), JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize<Command>(new Install("diesel heater"))));
        Assert.Equal(new YardInstall("radar"), JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize<Command>(new YardInstall("radar"))));
        Assert.Equal(new Upgrades(), JsonSerializer.Deserialize<Query>(JsonSerializer.Serialize<Query>(new Upgrades())));
        Assert.Contains("\"type\":\"install\"", JsonSerializer.Serialize<Command>(new Install("x")));
        Assert.Contains("\"type\":\"yard-install\"", JsonSerializer.Serialize<Command>(new YardInstall("x")));
        Assert.Contains("\"type\":\"upgrades\"", JsonSerializer.Serialize<Query>(new Upgrades()));
        var g = Afloat(77);
        var view = JsonSerializer.Deserialize<QueryResult>(JsonSerializer.Serialize<QueryResult>(Ups(g)));
        Assert.Equal(7, Assert.IsType<UpgradesView>(view).Items.Count);
    }

    [Fact]
    public void The_commands_are_in_the_catalogue()
    {
        var g = Afloat(77);
        var verbs = ((CommandList)g.Ask(new Commands())).Commands.Select(c => c.Verb).ToList();
        Assert.Contains("install", verbs);
        Assert.Contains("yard install", verbs);
        Assert.Contains("upgrades", verbs);
    }

    [Fact]
    public void A_boat_without_a_calendar_buys_an_upgrade_off_the_shelf_and_says_buy_not_order()
    {
        var g = OnTheHard();
        Walk.ToPlace(g, "forepeak");
        No(g.Execute(new Install("heads")), "You don't have the marine heads aboard. Buy one: buy marine heads.");
        var buy = Ok(g.Execute(new Buy("heads")));
        Assert.Equal(6500, buy.CostNok);
        Ok(g.Execute(new Install("heads")));
    }
}
