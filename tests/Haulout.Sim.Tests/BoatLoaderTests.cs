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
    public void Sloop_has_40_components_and_8_compartments()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(40, boat.Components.Count);
        Assert.Equal(8, boat.Compartments.Count);
    }

    [Fact]
    public void All_required_ids_are_present()
    {
        var boat = BoatLoader.LoadSloop28();
        foreach (var id in RequiredIds) Assert.True(boat.TryGet(id, out _), id);
        Assert.Throws<KeyNotFoundException>(() => boat.Get("nope"));
    }

    [Fact]
    public void Every_component_is_in_a_known_compartment()
    {
        var boat = BoatLoader.LoadSloop28();
        var ids = boat.Compartments.Select(c => c.Id).ToHashSet();
        Assert.All(boat.Components, c => Assert.Contains(c.Location, ids));
        Assert.Equal(40, ids.Sum(id => boat.In(id).Count()));
    }

    [Fact]
    public void Every_fault_can_be_discovered()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(4, boat.AllFaults.Count());
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
        Assert.False(f.RevealFor(PlayerAction.Inspect)!.Discovers);
        Assert.True(f.RevealFor(PlayerAction.Measure)!.Discovers);
        Assert.Null(f.RevealFor(PlayerAction.Look));
    }

    [Fact]
    public void Boat_rejects_bad_data()
    {
        const string json = """{"name":"x","compartments":[],"components":[{"id":"a","name":"A","subsystem":"hull","location":"nowhere","condition":1}]}""";
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(json));
    }
}
