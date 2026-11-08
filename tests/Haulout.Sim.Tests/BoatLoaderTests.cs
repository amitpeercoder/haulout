using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;

namespace Haulout.Sim.Tests;

public class BoatLoaderTests
{
    private static readonly string[] RequiredIds =
    [
        "start-battery", "battery-switch", "positive-terminal", "main-positive-cable", "negative-terminal",
        "negative-cable", "engine-ground-stud", "starter-solenoid", "starter-motor", "ignition-panel",
        "fuel-tank", "fuel-shutoff", "fuel-supply-line", "primary-filter", "lift-pump", "secondary-filter",
        "injection-pump", "injectors", "fuel-return-line", "raw-water-seacock", "raw-water-strainer",
        "raw-water-pump", "impeller", "heat-exchanger", "thermostat", "coolant", "exhaust-elbow",
        "exhaust-hose", "anti-siphon-loop", "engine-block", "engine-oil", "oil-filter", "alternator",
        "alternator-belt", "glow-plugs", "hull", "keel", "rudder", "standing-rig", "antifouling",
    ];

    [Fact]
    public void Sloop_has_45_components_7_places_and_7_covers()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(45, boat.Components.Count);
        Assert.Equal(7, boat.Places.Count);
        Assert.Equal(7, boat.Covers.Count);
    }

    [Fact]
    public void All_required_ids_are_present()
    {
        var boat = BoatLoader.LoadSloop28();
        foreach (var id in RequiredIds) Assert.True(boat.TryGet(id, out _), id);
        Assert.Throws<KeyNotFoundException>(() => boat.Get("nope"));
    }

    [Fact]
    public void Every_component_has_a_position_and_is_reachable_from_somewhere()
    {
        var boat = BoatLoader.LoadSloop28();
        var positions = boat.Positions.Select(p => p.Id).ToHashSet();
        Assert.All(boat.Components, c =>
        {
            Assert.Contains(c.PositionId, positions);
            Assert.Contains(c.Reach, e => e.Reachable);
            Assert.All(c.Reach, e => Assert.True(e.Visible || !e.Reachable, c.Id));
        });
    }

    [Fact]
    public void Places_are_symmetric_and_every_place_can_be_reached_from_the_cockpit()
    {
        var boat = BoatLoader.LoadSloop28();
        foreach (var p in boat.Places)
            foreach (var a in p.Adjacent)
                Assert.Contains(p.Id, boat.GetPlace(a).Adjacent);
        var seen = new HashSet<string> { "cockpit" };
        var queue = new Queue<string>(seen);
        while (queue.Count > 0)
            foreach (var n in boat.GetPlace(queue.Dequeue()).Adjacent)
                if (seen.Add(n)) queue.Enqueue(n);
        Assert.Equal(boat.Places.Count, seen.Count);
        Assert.True(boat.GetPlace("beside-the-hull").OnlyOnTheHard);
    }

    [Fact]
    public void Covers_have_times_and_texts()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.All(boat.Covers, c =>
        {
            Assert.True(c.RemoveMinutes > 0 && c.RefitMinutes > 0, c.Id);
            Assert.False(string.IsNullOrWhiteSpace(c.RemoveText), c.Id);
            Assert.False(string.IsNullOrWhiteSpace(c.RefitText), c.Id);
        });
        Assert.Equal(["companionway-steps", "saloon-floorboards"], boat.Covers.Where(c => c.Plural).Select(c => c.Id));
    }

    [Fact]
    public void Every_fault_can_be_discovered()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(20, boat.AllFaults.Count());
        Assert.All(boat.AllFaults, f => Assert.Contains(f.Reveals, r => r.Discovers));
    }

    [Fact]
    public void Nothing_is_broken_beyond_the_faults()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.All(boat.Components, c => Assert.True(c.Condition >= 0.3, c.Id));
        Assert.Equal(0.35, boat.Get("impeller").Condition);
    }

    [Fact]
    public void Cranking_loop_resistance_is_about_059_ohms()
    {
        var boat = BoatLoader.LoadSloop28();
        var loop = boat.Components.Sum(c => c.SeriesOhms ?? 0) + boat.Get("start-battery").Properties["internalOhms"];
        Assert.InRange(loop, 0.058, 0.060);
    }

    [Fact]
    public void Replace_clears_faults_and_restores_condition()
    {
        var boat = BoatLoader.LoadSloop28();
        var terminal = boat.Get("negative-terminal");
        var fault = Assert.Single(terminal.ActiveFaults);
        fault.Discover();

        terminal.Replace();

        Assert.Empty(terminal.ActiveFaults);
        Assert.False(fault.Present);
        Assert.True(fault.Discovered);
        Assert.Equal(1, terminal.Condition);
    }

    [Fact]
    public void Loads_are_independent()
    {
        var a = BoatLoader.LoadSloop28();
        var b = BoatLoader.LoadSloop28();
        a.Get("impeller").Replace();
        Assert.NotSame(a.Get("impeller"), b.Get("impeller"));
        Assert.Single(b.Get("impeller").ActiveFaults);
        Assert.Equal(0.35, b.Get("impeller").Condition);
    }

    [Fact]
    public void Corroded_terminal_matches_the_design()
    {
        var f = BoatLoader.LoadSloop28().Get("negative-terminal").Faults.Single();
        Assert.Equal(FaultEffectKind.SeriesOhms, f.Effect.Kind);
        Assert.Equal(0.060, f.Effect.Value);
        Assert.Null(f.RevealFor(PlayerAction.Inspect));
        var crust = f.RevealFor(PlayerAction.Inspect, uncovered: true)!;
        Assert.True(crust.Discovers);
        Assert.True(crust.NeedsUncovered);
        Assert.True(f.RevealFor(PlayerAction.Measure)!.Discovers);
        Assert.Null(f.RevealFor(PlayerAction.Look));
    }

    private static string Mutated(Action<JsonNode> edit)
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        var node = JsonNode.Parse(stream)!;
        edit(node);
        return node.ToJsonString();
    }

    [Fact]
    public void Boat_rejects_bad_data()
    {
        // Unknown position.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["components"]![0]!["position"] = "nowhere")));
        // Unknown place in a reach entry.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["components"]![0]!["reach"]![0]!["place"] = "nowhere")));
        // Unknown cover in a reach entry.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["components"]![0]!["reach"]![0]!["cover"] = "nothing")));
        // Reachable but not visible.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["components"]![0]!["reach"]![0]!["visible"] = false)));
        // Adjacency one way only.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["places"]![0]!["adjacent"]!.AsArray().Add("forepeak"))));
        // Adjacent to a place that does not exist.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["places"]![0]!["adjacent"]!.AsArray().Add("moon"))));
        // Duplicate ids.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["places"]![1]!["id"] = "cockpit")));
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["covers"]![1]!["id"] = "companionway-steps")));
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["components"]![1]!["id"] = "start-battery")));
        // A cover removed at an unknown place.
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["covers"]![0]!["place"] = "nowhere")));
        // The unmodified data loads.
        Assert.NotNull(BoatLoader.Load(Mutated(_ => { })));
    }
}
