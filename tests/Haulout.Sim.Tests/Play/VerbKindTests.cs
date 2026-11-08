using System.Text.Json.Nodes;
using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// Design note 002, finding 2: which verbs a component offers depends only on what kind of part it is, never on whether it has a fault.
/// A verb list that shifts with a fault is a hint, and the agents read it as one.
/// </summary>
public class VerbKindTests
{
    private static readonly string[] ProblemVerbs = ["defer", "undefer", "yard-fix"];

    private static Boat LayoutBoat() => BoatLoader.LoadSloop28();

    private static Game Fresh(bool healthy)
    {
        var boat = LayoutBoat();
        if (healthy)
            foreach (var f in boat.AllFaults.ToList()) f.Clear();
        return new Game(boat, weather: WeatherSeries.Fair());
    }

    /// <summary>The player has been to every component and looked: every part is nameable.</summary>
    private static void SeeEverything(Game g)
    {
        foreach (var c in LayoutBoat().Components)
        {
            Walk.Near(g, c.Id);
            Assert.True(g.Execute(new Look()).Accepted);
        }
    }

    private static List<string> Verbs(Game g, string id) =>
        Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions(id))).Options
            .Select(o => o.Verb).Where(v => !ProblemVerbs.Contains(v)).Order().ToList();

    [Fact]
    public void Every_sloop_component_has_a_kind_and_offers_exactly_its_verbs()
    {
        var g = Fresh(healthy: false);
        SeeEverything(g);
        foreach (var c in LayoutBoat().Components)
        {
            Assert.NotNull(c.Kind);
            Assert.Equal(c.Kind!.Verbs.Order().ToList(), Verbs(g, c.Id));
        }
    }

    [Fact]
    public void Parts_of_one_kind_offer_the_same_verbs_whatever_is_wrong_with_them()
    {
        var g = Fresh(healthy: false);
        SeeEverything(g);
        foreach (var kind in LayoutBoat().Components.GroupBy(c => c.Kind!.Id))
        {
            var first = Verbs(g, kind.First().Id);
            foreach (var c in kind) Assert.Equal(first, Verbs(g, c.Id));
        }
    }

    [Fact]
    public void A_healthy_positive_terminal_and_the_corroded_negative_offer_the_same_verbs()
    {
        var g = Fresh(healthy: false);
        SeeEverything(g);
        Assert.Equal(Verbs(g, "positive-terminal"), Verbs(g, "negative-terminal"));
        Assert.Contains("uncover", Verbs(g, "positive-terminal"));
        Assert.Contains("measure", Verbs(g, "positive-terminal"));
    }

    [Fact]
    public void The_options_do_not_move_with_the_faults_at_the_same_place_with_the_same_covers()
    {
        var faulty = Fresh(healthy: false);
        var healthy = Fresh(healthy: true);
        SeeEverything(faulty);
        SeeEverything(healthy);
        var ids = LayoutBoat().Components.Select(c => c.Id).ToList();
        foreach (var at in ids)
        {
            Walk.Near(faulty, at);
            Walk.Near(healthy, at);
            foreach (var id in ids)
            {
                var a = Assert.IsType<ComponentActionsView>(faulty.Ask(new ComponentActions(id))).Options;
                var b = Assert.IsType<ComponentActionsView>(healthy.Ask(new ComponentActions(id))).Options;
                Assert.Equal(
                    b.Select(o => (o.Verb, o.Available, o.UnavailableReason, o.Minutes, o.CostNok)),
                    a.Select(o => (o.Verb, o.Available, o.UnavailableReason, o.Minutes, o.CostNok)));
            }
        }
    }

    [Fact]
    public void Defer_and_yard_fix_are_offered_only_for_a_fault_the_player_has_found()
    {
        var g = Fresh(healthy: false);
        SeeEverything(g);
        foreach (var c in LayoutBoat().Components)
        {
            var options = Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions(c.Id))).Options;
            Assert.DoesNotContain(options, o => ProblemVerbs.Contains(o.Verb));
        }
        // Found by measuring: now the corroded negative terminal, and only it, carries the fault's own options.
        Assert.True(g.Execute(new Buy("remote start button")).Accepted);
        Assert.True(Walk.Do(g, "negative terminal", n => new Measure(n)).Accepted);
        Assert.Contains(Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions("negative-terminal"))).Options, o => o.Verb == "defer");
        Assert.DoesNotContain(Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions("positive-terminal"))).Options, o => o.Verb == "defer");
    }

    [Theory]
    [InlineData("positive terminal", "rubber boot")]
    [InlineData("main positive cable", "rubber boot")]
    [InlineData("negative cable", "rubber boot")]
    [InlineData("engine ground stud", "rubber cap")]
    public void Uncovering_a_healthy_part_takes_the_cover_off_and_finds_nothing(string name, string cover)
    {
        var g = Fresh(healthy: true);
        var r = Walk.Do(g, name, n => new Uncover(n));
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        Assert.Equal(2, r.MinutesTaken);
        Assert.Contains(cover, string.Join(" ", r.Narrative));
        Assert.Empty(r.NewlyDiscovered);
        var look = g.Execute(new Inspect(name));
        Assert.Empty(look.NewlyDiscovered);
    }

    [Fact]
    public void Uncovering_a_healthy_cable_then_looking_shows_a_clean_lug()
    {
        var g = Fresh(healthy: true);
        var r = Walk.Do(g, "negative cable", n => new Uncover(n));
        Assert.DoesNotContain("trace of", string.Join(" ", r.Narrative)); // the look is Inspect's job (decision 2026-11-08: suggestive, not "clean and bright")
        var look = g.Execute(new Inspect("negative cable"));
        Assert.Contains("trace of white bloom where it meets the cable", string.Join(" ", look.Narrative));
    }

    [Fact]
    public void Uncover_says_the_same_on_the_corroded_negative_terminal_as_on_the_positive_apart_from_the_part()
    {
        // The fault is only shown by inspecting afterwards; pulling the boot back narrates the same shape of thing for both.
        var g = Fresh(healthy: false);
        var neg = Walk.Do(g, "negative terminal", n => new Uncover(n));
        var pos = Walk.Do(g, "positive terminal", n => new Uncover(n));
        Assert.Empty(neg.NewlyDiscovered);
        Assert.Equal(pos.Narrative, neg.Narrative); // word for word: uncovering says nothing about what is under the boot
        Assert.Equal(pos.MinutesTaken, neg.MinutesTaken);
    }

    [Theory]
    [InlineData("positive terminal")]
    [InlineData("main positive cable")]
    [InlineData("battery switch")]
    [InlineData("raw-water strainer")]
    public void Cleaning_a_healthy_part_costs_the_full_time_and_says_nothing_was_wrong(string name)
    {
        var g = Fresh(healthy: true);
        var r = Walk.Do(g, name, n => new Clean(n));
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        Assert.Equal(30, r.MinutesTaken);
        Assert.Contains("looked fine before", string.Join(" ", r.Narrative));
        Assert.Empty(r.NewlyDiscovered);
    }

    [Fact]
    public void A_verb_the_kind_does_not_offer_is_refused_free_whatever_the_part_is_suffering()
    {
        var g = Fresh(healthy: false);
        Walk.Near(g, "fuel-tank");
        Assert.False(g.Execute(new Clean("fuel tank")).Accepted);
        Assert.False(g.Execute(new Uncover("fuel tank")).Accepted);
        Assert.False(g.Execute(new Measure("fuel tank")).Accepted);
        // A seacock has a fault the player has not found; cleaning it is refused exactly as cleaning a healthy fitting is.
        var healthy = Fresh(healthy: true);
        Walk.Near(g, "raw-water-seacock");
        Walk.Near(healthy, "raw-water-seacock");
        var a = g.Execute(new Clean("seacock"));
        var b = healthy.Execute(new Clean("seacock"));
        Assert.False(a.Accepted);
        Assert.Equal(b.Narrative, a.Narrative);
    }

    [Fact]
    public void Every_part_that_offers_measure_gives_a_reading()
    {
        var g = Fresh(healthy: false);
        Assert.True(g.Execute(new Buy("remote start button")).Accepted);
        foreach (var c in LayoutBoat().Components.Where(c => c.HasVerb(Verb.Measure)))
        {
            var r = Walk.Do(g, c.Id, _ => new Measure(c.Id));
            Assert.True(r.Accepted, $"{c.Id}: {string.Join(" ", r.Narrative)}");
            Assert.IsType<MeterReading>(r.Observation);
        }
    }

    // ---------------------------------------------------------------- the loader

    private static string Mutated(Action<JsonNode> edit)
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        var node = JsonNode.Parse(stream)!;
        edit(node);
        return node.ToJsonString();
    }

    private static JsonNode Component(JsonNode n, string id) => n["components"]!.AsArray().Single(c => c!["id"]!.GetValue<string>() == id)!;

    [Fact]
    public void The_loader_rejects_a_fault_fixed_by_clean_on_a_kind_that_cannot_be_cleaned()
    {
        // The primary filter's kind has no clean verb; a fault there that cleaning fixes would be a verb the player never sees.
        var ex = Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n =>
            Component(n, "primary-filter")["faults"]![0]!["fixedBy"]!.AsArray().Add("clean"))));
        Assert.Contains("clean", ex.Message);
        Assert.Contains("primary-filter", ex.Message);
    }

    [Fact]
    public void The_loader_rejects_a_kind_that_contradicts_the_components_data()
    {
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => Component(n, "positive-terminal")["kind"] = "nonsense")));
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => Component(n, "positive-terminal").AsObject().Remove("kind"))));
        // uncover without a removable part
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => Component(n, "positive-terminal").AsObject().Remove("removablePart"))));
        // a removable part on a kind that does not uncover
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => Component(n, "battery-switch")["removablePart"] = new JsonObject { ["name"] = "boot" })));
        // replace price on a kind with no replace verb
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => Component(n, "keel")["replaceCostNok"] = 500)));
    }

    [Fact]
    public void The_loader_rejects_an_unknown_verb_in_a_kind()
    {
        Assert.Throws<ArgumentException>(() => BoatLoader.Load(Mutated(n => n["kinds"]![0]!["verbs"]!.AsArray().Add("teleport"))));
    }
}
