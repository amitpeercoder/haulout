using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The chart (design note 009, step 1): positions in data, derived routes, the Chart query and the tracks of passages.</summary>
public class ChartTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string ImpellerAlarm =
        "The temperature alarm whines and there's a smell of hot rubber from under the cockpit. Shut her down and check the cooling water at the exhaust.";

    private static WeatherSeries Wind(int force, string from) =>
        WeatherSeries.Constant(new WeatherDay(0, force, from, 0, 12, 8));

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static ChartView Chart(Game g, string? planned = null) => (ChartView)g.Ask(new Chart(planned));

    private static Game Afloat(WeatherSeries weather, out Boat boat, int startDay = 75)
    {
        boat = BoatLoader.LoadSloop28();
        var g = new Game(boat, startDay, new FixedRandom(0.99), Game.DefaultSeed, weather);
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        return g;
    }

    private static void Depart(Game g)
    {
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart()));
    }

    private static string Mutated(Action<JsonNode> edit)
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        var node = JsonNode.Parse(stream)!;
        edit(node);
        return node.ToJsonString();
    }

    // ------------------------------------------------------------------ data and loader

    [Fact]
    public void The_loader_derives_distance_and_course_from_the_ports_positions()
    {
        var trips = BoatLoader.LoadSloop28().Trips!;
        var hest = trips.Routes.Single(r => r.From == "skjaervik" && r.To == "hestholmen");
        var stor = trips.Routes.Single(r => r.From == "skjaervik" && r.To == "storholmen");
        var kvit = trips.Routes.Single(r => r.From == "kvitnes" && r.To == "hestholmen");
        Assert.Equal((14.0, 40), (hest.DistanceNm, hest.CourseDeg));
        Assert.Equal((26.0, 300), (stor.DistanceNm, stor.CourseDeg));
        Assert.Equal((9.0, 300), (kvit.DistanceNm, kvit.CourseDeg));
        var back = trips.RoutesFrom("hestholmen").Single(r => r.To == "skjaervik");
        Assert.Equal((14.0, 220), (back.DistanceNm, back.CourseDeg));
        Assert.True(trips.FindPort("kvitnes")!.Anchorage);
        Assert.False(trips.FindPort("hestholmen")!.Anchorage);
    }

    [Fact]
    public void The_loader_rejects_a_route_whose_port_has_no_position()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BoatLoader.Load(Mutated(n => n["trips"]!["ports"]![1]!.AsObject().Remove("chart"))));
        Assert.Equal("Route skjaervik → hestholmen: port hestholmen has no chart position.", ex.Message);
    }

    [Fact]
    public void The_loader_rejects_a_route_that_still_carries_a_distance_or_a_course()
    {
        Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(Mutated(n => n["trips"]!["routes"]![0]!["distanceNm"] = 14)));
        Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(Mutated(n => n["trips"]!["routes"]![0]!["courseDeg"] = 40)));
    }

    [Fact]
    public void The_loader_rejects_two_ports_at_one_point_and_a_shelter_outside_the_route()
    {
        Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(Mutated(n =>
        {
            n["trips"]!["ports"]![1]!["chart"]!["eastNm"] = 0;
            n["trips"]!["ports"]![1]!["chart"]!["northNm"] = 0;
        })));
        Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(Mutated(n => n["trips"]!["routes"]![0]!["shelter"]![0]!["atNm"] = 14)));
        Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(Mutated(n => n["trips"]!["routes"]![0]!["shelter"]![0]!["atNm"] = 0)));
    }

    [Fact]
    public void PointAlong_walks_the_straight_line_and_clamps_to_the_route()
    {
        var trips = BoatLoader.LoadSloop28().Trips!;
        var route = trips.Routes[0];
        Assert.Equal(new ChartPoint(0, 0), trips.PointAlong(route, 0));
        var mid = trips.PointAlong(route, 7);
        Assert.Equal(4.5, mid.EastNm, 6);
        Assert.Equal(5.35, mid.NorthNm, 6);
        Assert.Equal(new ChartPoint(9.0, 10.7), trips.PointAlong(route, 14));
        Assert.Equal(new ChartPoint(9.0, 10.7), trips.PointAlong(route, 99));
        Assert.Equal(new ChartPoint(0, 0), trips.PointAlong(route, -3));
        var back = route.Reversed();
        Assert.Equal(new ChartPoint(9.0, 10.7), trips.PointAlong(back, 0));
        Assert.Equal(4.5, trips.PointAlong(back, 7).EastNm, 6);
    }

    // ------------------------------------------------------------------ at home

    [Fact]
    public void The_chart_at_home_has_the_ports_with_marks_and_distances_from_Skjaervik()
    {
        var g = Afloat(Wind(3, "southwest"), out _);
        var c = Chart(g);
        Assert.Equal("She lies at Skjærvik.", c.Here);
        Assert.Equal(["Skjærvik", "Hestholmen", "Storholmen", "Kvitnes"], c.Ports.Select(p => p.Name));

        var home = c.Ports[0];
        Assert.True(home.IsHere);
        Assert.True(home.Yard && home.Shop && home.Fuel && !home.Anchorage);
        Assert.False(home.Reachable);
        Assert.Equal((0.0, 0.0, 0.0, 0), (home.EastNm, home.NorthNm, home.DistanceNm, home.BearingDeg));

        var hest = c.Ports[1];
        Assert.Equal((9.0, 10.7, 14.0, 40), (hest.EastNm, hest.NorthNm, hest.DistanceNm, hest.BearingDeg));
        Assert.True(hest.Reachable && hest.Shop && !hest.Yard && !hest.Fuel && !hest.IsHere);

        var stor = c.Ports[2];
        Assert.Equal((-22.5, 13.0, 26.0, 300), (stor.EastNm, stor.NorthNm, stor.DistanceNm, stor.BearingDeg));
        Assert.True(stor.Reachable && stor.Fuel);

        var kvit = c.Ports[3];
        Assert.Equal((16.8, 6.2, 17.9, 70), (kvit.EastNm, kvit.NorthNm, kvit.DistanceNm, kvit.BearingDeg));
        Assert.True(kvit.Anchorage && !kvit.Shop && !kvit.Reachable);
        Assert.Equal(("Hestholmen", 23.0), (kvit.Via, kvit.RouteNm));   // the chart gives the way through another port instead of "no route"
        Assert.Null(hest.Via);

        Assert.Equal(3, c.Routes.Count);
        var r0 = c.Routes[0];
        Assert.Equal(("Skjærvik", "Hestholmen", 14.0, 40), (r0.From, r0.To, r0.DistanceNm, r0.CourseDeg));
        var shelter = Assert.Single(r0.Shelters);
        Assert.Equal(("Lyngskjær", 7.0, 4.5), (shelter.Name, shelter.AtNm, shelter.EastNm));
        Assert.Equal(5.35, shelter.NorthNm, 1);
        Assert.Equal(("Kvitnes", "Hestholmen", 9.0, 300), (c.Routes[2].From, c.Routes[2].To, c.Routes[2].DistanceNm, c.Routes[2].CourseDeg));

        Assert.False(c.Boat.AtSea);
        Assert.Equal("Skjærvik", c.Boat.Port);
        Assert.Equal((0.0, 0.0), (c.Boat.EastNm, c.Boat.NorthNm));
        Assert.Null(c.Track);
        Assert.Empty(c.EarlierTracks);
        Assert.Null(c.Plan);
        Assert.Equal(3, c.Wind.Force);
        Assert.Equal("southwest", c.Wind.From);
        Assert.False(c.Wind.Rain);
    }

    [Fact]
    public void A_planned_route_carries_the_estimate_the_shelters_and_the_blockers()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), Game.DefaultSeed, Wind(3, "southwest"));
        var plan = Chart(g, "Hestholmen").Plan!;
        Assert.Equal(("Skjærvik", "Hestholmen", 14.0, 40), (plan.From, plan.To, plan.DistanceNm, plan.CourseDeg));
        Assert.Equal(2.8, plan.MotorHours);
        Assert.Contains("under engine", plan.Estimate);
        Assert.Equal("Lyngskjær", Assert.Single(plan.Shelters).Name);
        Assert.Contains("She's still on the hard.", plan.Blockers);

        Assert.Null(Chart(g).Plan);
        var via = Chart(g, "Kvitnes").Plan!;       // no route from here: the way is through Hestholmen (design note 010, second pass I)
        Assert.Equal(("Skjærvik", "Kvitnes", 23.0, 40), (via.From, via.To, via.DistanceNm, via.CourseDeg));
        Assert.Equal(4.6, via.MotorHours);
        Assert.Null(Chart(g, "nowhere").Plan);
    }

    // ------------------------------------------------------------------ a passage

    [Fact]
    public void A_passage_is_one_fix_an_hour_on_the_line_from_hour_zero_at_the_origin()
    {
        var g = Afloat(Wind(3, "southwest"), out _);
        Depart(g);
        var c0 = Chart(g);
        Assert.True(c0.Boat.AtSea);
        var t0 = c0.Track!;
        Assert.Equal(("Skjærvik", "Hestholmen", "under way"), (t0.From, t0.To, t0.Outcome));
        var f0 = Assert.Single(t0.Fixes);
        Assert.Equal((0, 0.0, 0.0, "under engine"), (f0.Hour, f0.EastNm, f0.NorthNm, f0.Mode));
        Assert.Equal(["Left Skjærvik for Hestholmen."], f0.Events);

        Ok(g.Execute(new Continue()));
        Ok(g.Execute(new Continue()));
        var c = Chart(g);
        Assert.Equal([0, 1, 2], c.Track!.Fixes.Select(f => f.Hour));
        Assert.Equal((3.2, 3.8), (c.Track.Fixes[1].EastNm, c.Track.Fixes[1].NorthNm));   // 5 nm out
        Assert.Equal((6.4, 7.6), (c.Track.Fixes[2].EastNm, c.Track.Fixes[2].NorthNm));   // 10 nm out
        Assert.Empty(c.Track.Fixes[1].Events);

        Assert.Equal("Under engine, 10.0 nm out of Skjærvik, 4.0 nm to Hestholmen.", c.Here);
        var boat = c.Boat;
        Assert.True(boat.AtSea);
        Assert.Null(boat.Port);
        Assert.Equal(("Skjærvik", "Hestholmen", 10.0, 4.0, "under engine", 40), (boat.From, boat.To, boat.PositionNm, boat.RemainingNm, boat.Mode, boat.HeadingDeg));
        Assert.Equal((6.4, 7.6), (boat.EastNm, boat.NorthNm));
        Assert.All(c.Ports, p => Assert.False(p.Reachable || p.IsHere));
        Assert.Equal(4.0, c.Ports.Single(p => p.Name == "Hestholmen").DistanceNm);
        Assert.Null(c.Plan);   // no plan from the water
        Assert.Null(Chart(g, "Hestholmen").Plan);
    }

    [Fact]
    public void Alerts_heave_to_a_repair_and_the_arrival_sit_on_the_right_hour_and_the_track_moves_to_the_seasons_memory()
    {
        var g = Afloat(Wind(3, "southwest"), out _);
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        Ok(g.Execute(new Inspect("impeller")));
        Ok(g.Execute(new Defer("impeller")));
        Ok(g.Execute(new Buy("impeller")));

        Depart(g);
        Ok(g.Execute(new Continue()));
        Ok(g.Execute(new Continue()));
        Assert.Contains(Chart(g).Track!.Fixes.Single(f => f.Hour == 2).Events, e => e == ImpellerAlarm);
        Assert.DoesNotContain(Chart(g).Track!.Fixes.Single(f => f.Hour == 1).Events, e => e == ImpellerAlarm);

        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HeaveTo()));
        Ok(g.Execute(new StopEngine()));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        var mid = Chart(g).Track!;
        var hour3 = mid.Fixes.Single(f => f.Hour == 3);
        Assert.Equal("hove-to", hour3.Mode);
        Assert.Contains("Hove-to.", hour3.Events);
        Assert.Equal(1, mid.Fixes.Count(f => f.Hour == 3));
        // The repair took a while (walking, the work): it sits on the hour she was in when it was done, the latest fix.
        Assert.Contains("Replaced the raw-water impeller from the spares aboard.", mid.Fixes[^1].Events);
        Assert.Equal("hove-to", mid.Fixes[^1].Mode);

        Ok(Walk.Start(g));
        var arrive = Ok(g.Execute(new Motor()));
        Assert.IsType<TripResultView>(arrive.Observation);

        var c = Chart(g);
        Assert.Null(c.Track);
        Assert.False(c.Boat.AtSea);
        Assert.Equal("Hestholmen", c.Boat.Port);
        Assert.Equal((9.0, 10.7), (c.Boat.EastNm, c.Boat.NorthNm));
        Assert.True(c.Ports.Single(p => p.Name == "Hestholmen").IsHere);
        var earlier = Assert.Single(c.EarlierTracks);
        Assert.Equal(("Skjærvik", "Hestholmen", "arrived"), (earlier.From, earlier.To, earlier.Outcome));
        Assert.Equal(75, earlier.Day);
        var last = earlier.Fixes[^1];
        Assert.Equal((9.0, 10.7), (last.EastNm, last.NorthNm));
        Assert.Contains("Arrived at Hestholmen.", last.Events);
        Assert.Equal(earlier.Fixes.Select(f => f.Hour).Order(), earlier.Fixes.Select(f => f.Hour));
        Assert.Equal(earlier.Fixes.Count, earlier.Fixes.Select(f => f.Hour).Distinct().Count());
        Assert.Contains(earlier.Fixes, f => f.Events.Contains(ImpellerAlarm));
    }

    [Fact]
    public void Earlier_tracks_belong_to_the_season_and_are_gone_in_the_next_year()
    {
        var g = Afloat(Wind(3, "southwest"), out _, startDay: 358);   // the last days of year 0
        Depart(g);
        for (int i = 0; i < 4 && Chart(g).Boat.AtSea; i++) Ok(g.Execute(new Continue()));
        Assert.True(Status(g).Day < 360);
        Assert.Single(Chart(g).EarlierTracks);
        Ok(g.Execute(new WaitDays(3)));
        Assert.True(Status(g).Day >= 360);
        Assert.Empty(Chart(g).EarlierTracks);
    }

    [Fact]
    public void A_turn_back_ends_at_the_origin_marked_turned_back_with_the_event_on_the_track()
    {
        var g = Afloat(Wind(3, "southwest"), out _);
        Depart(g);
        Ok(g.Execute(new Continue()));
        Ok(g.Execute(new TurnBack()));
        var track = Chart(g).Track!;
        Assert.Equal("Hestholmen", track.To);   // still the passage she set out on
        Assert.Contains(track.Fixes.SelectMany(f => f.Events), e => e == "Turned back for Skjærvik.");
        Assert.Equal("Skjærvik", Chart(g).Boat.To);
        Assert.Equal(40 + 180, Chart(g).Boat.HeadingDeg);
        for (int i = 0; i < 6 && Chart(g).Boat.AtSea; i++) Ok(g.Execute(new Continue()));
        var c = Chart(g);
        var earlier = Assert.Single(c.EarlierTracks);
        Assert.Equal("turned back", earlier.Outcome);
        Assert.Equal(("Skjærvik", "Hestholmen"), (earlier.From, earlier.To));
        Assert.Equal((0.0, 0.0), (earlier.Fixes[^1].EastNm, earlier.Fixes[^1].NorthNm));
        Assert.Contains("Arrived at Skjærvik.", earlier.Fixes[^1].Events);
        Assert.Equal("Skjærvik", c.Boat.Port);
    }

    private static Game WaterInTheFuelAtSea()
    {
        var g = Afloat(Wind(4, "northeast"), out _);
        Ok(Walk.Do(g, "primary filter", c => new CheckFilterBowl()));
        Ok(g.Execute(new Defer("water in the fuel tank")));
        Depart(g);
        Ok(g.Execute(new Continue()));
        return g;
    }

    [Fact]
    public void A_tow_ends_the_track_at_the_nearer_port_with_the_towboat_on_it()
    {
        var g = WaterInTheFuelAtSea();
        Ok(g.Execute(new Continue()));   // 10 nm out: Hestholmen is nearer
        Ok(g.Execute(new CallTow()));
        var c = Chart(g);
        Assert.Null(c.Track);
        var t = Assert.Single(c.EarlierTracks);
        Assert.Equal("towed", t.Outcome);
        Assert.Equal((9.0, 10.7), (t.Fixes[^1].EastNm, t.Fixes[^1].NorthNm));
        Assert.Contains(t.Fixes.SelectMany(f => f.Events), e => e.StartsWith("Called the towboat: 6000 NOK", StringComparison.Ordinal));
        Assert.Contains(t.Fixes, f => f.Mode == "under tow");
        Assert.Equal("Hestholmen", c.Boat.Port);
    }

    [Fact]
    public void A_tow_short_of_halfway_ends_at_the_origin()
    {
        var g = WaterInTheFuelAtSea();
        Ok(g.Execute(new CallTow()));
        var t = Assert.Single(Chart(g).EarlierTracks);
        Assert.Equal("towed", t.Outcome);
        Assert.Equal((0.0, 0.0), (t.Fixes[^1].EastNm, t.Fixes[^1].NorthNm));
        Assert.Equal("Skjærvik", Chart(g).Boat.Port);
    }

    // ------------------------------------------------------------------ the wire

    [Fact]
    public void The_chart_query_and_view_round_trip_as_json()
    {
        Assert.Contains("\"type\":\"chart\"", JsonSerializer.Serialize<Query>(new Chart()));
        Assert.Equal(new Chart("Hestholmen"), JsonSerializer.Deserialize<Query>(JsonSerializer.Serialize<Query>(new Chart("Hestholmen"))));
        var g = Afloat(Wind(3, "southwest"), out _);
        var json = JsonSerializer.Serialize<QueryResult>(Chart(g, "Hestholmen"));
        Assert.Contains("\"type\":\"chart-view\"", json);
        var back = Assert.IsType<ChartView>(JsonSerializer.Deserialize<QueryResult>(json));
        Assert.Equal("She lies at Skjærvik.", back.Here);
        Assert.Equal(4, back.Ports.Count);
        Assert.NotNull(back.Plan);
    }

    [Fact]
    public void The_chart_asked_twice_gives_the_same_answer_and_costs_no_time()
    {
        var g = Afloat(Wind(3, "southwest"), out _);
        Depart(g);
        int day = Status(g).Day;
        string a = JsonSerializer.Serialize<QueryResult>(Chart(g)), b = JsonSerializer.Serialize<QueryResult>(Chart(g));
        Assert.Equal(a, b);
        Assert.Equal(day, Status(g).Day);
    }

    [Fact]
    public void The_chart_carries_no_ids_and_no_undiscovered_fault_names()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = Afloat(Wind(3, "southwest"), out boat);
        Depart(g);
        Ok(g.Execute(new Continue()));
        var json = JsonSerializer.Serialize<QueryResult>(Chart(g));
        foreach (var f in boat.AllFaults)
        {
            Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
            if (!f.Discovered) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
        }
        foreach (var p in boat.Trips!.Ports) Assert.False(json.Contains($"\"{p.Id}\"", StringComparison.Ordinal), p.Id);
    }
}
