using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

/// <summary>The Aboard and ComponentActions queries the web adapter draws from (design notes 003, 004).</summary>
public class AdapterQueryTests
{
    private static AboardView Aboard(Game g) => Assert.IsType<AboardView>(g.Ask(new Aboard()));

    private static ComponentActionsView Actions(Game g, string name) =>
        Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions(name)));

    private static FoundComponent Found(Game g, string name) => Aboard(g).Components.Single(c => c.Name == name);

    private static ActionOption Option(Game g, string component, string verb) =>
        Actions(g, component).Options.Single(o => o.Verb == verb);

    [Fact]
    public void Nothing_is_found_before_going_aboard()
    {
        var g = Game.NewFirstFault();
        var view = Aboard(g);
        Assert.Equal("Cockpit", view.Place);
        Assert.Equal(7, view.Places.Count);
        Assert.Equal(7, view.Covers.Count);
        Assert.Empty(view.Components);
        // Only the companionway hatch starts off (open).
        Assert.Equal(["Companionway hatch"], view.Covers.Where(c => c.Removed).Select(c => c.Name));
        Assert.All(view.Places, p => Assert.False(p.Visited));
        Assert.False(Actions(g, "negative terminal").Found);
    }

    [Fact]
    public void Look_finds_what_is_in_view_and_marks_the_place_visited()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        var view = Aboard(g);
        Assert.True(view.Places.Single(p => p.Name == "Cockpit").Visited);
        Assert.True(view.Places.Single(p => p.Name == "Cockpit").Here);
        Assert.False(view.Places.Single(p => p.Name == "Saloon").Visited);
        Assert.Equal(["Companionway", "On deck"], view.Places.Single(p => p.Name == "Cockpit").Adjacent);
        Assert.All(view.Places, p => Assert.True(p.Available));

        var panel = Found(g, "Ignition panel");
        Assert.Equal("Cockpit", panel.Position);
        Assert.True(panel.VisibleHere && panel.ReachableHere);
        var rig = Found(g, "Standing rig");
        Assert.True(rig.VisibleHere);
        Assert.False(rig.ReachableHere);
        Assert.DoesNotContain(view.Components, c => c.Name == "Start battery");
    }

    [Fact]
    public void Queries_cost_no_time()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        Walk.Near(g, "battery");
        var before = Assert.IsType<StatusView>(g.Ask(new Status())).MinutesSpent;
        Aboard(g);
        Actions(g, "negative terminal");
        Assert.Equal(before, Assert.IsType<StatusView>(g.Ask(new Status())).MinutesSpent);
    }

    [Fact]
    public void Solving_the_first_fault_by_the_options_the_ui_shows()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        g.Execute(new GoTo("companionway"));
        g.Execute(new GoTo("quarter berth"));
        g.Execute(new Remove("battery-box lid"));

        var terminal = Found(g, "Negative terminal");
        Assert.Equal("Under the quarter berth", terminal.Position);
        Assert.False(terminal.KnownProblem);
        Assert.True(terminal.VisibleHere && terminal.ReachableHere);

        var options = Actions(g, "negative terminal");
        Assert.True(options.Found);
        Assert.Equal("Negative terminal", options.ComponentName, ignoreCase: true);
        var measure = options.Options.Single(o => o.Verb == "measure");
        Assert.False(measure.Available);
        Assert.NotNull(measure.UnavailableReason);
        Assert.Contains(options.Options, o => o.Verb == "clean" && o.Available);
        Assert.Contains(options.Options, o => o.Verb == "replace" && o.CostNok == 60);

        // A plain inspect shows nothing; the boot has to come off first.
        g.Execute(new Inspect("negative terminal"));
        Assert.False(Found(g, "Negative terminal").KnownProblem);
        Assert.Contains(options.Options, o => o.Verb == "uncover" && o.Available && o.Label == "Pull back the rubber boot" && o.Minutes == 2);
        Assert.True(g.Execute(new Uncover("negative terminal")).Accepted);
        Assert.False(Option(g, "negative terminal", "uncover").Available);
        g.Execute(new Inspect("negative terminal"));
        Assert.False(Found(g, "Negative terminal").KnownProblem); // decision (Amit, 2026-11-08): the look under the boot discovers nothing

        Assert.True(g.Execute(new Buy("remote start button")).Accepted);
        Assert.True(Option(g, "negative terminal", "measure").Available);

        Assert.True(g.Execute(new Measure("negative terminal")).Accepted);
        Assert.True(Found(g, "Negative terminal").KnownProblem);

        g.Execute(new Clean("negative terminal"));
        Assert.False(Found(g, "Negative terminal").KnownProblem);

        Assert.True(Walk.Start(g).Finished);
    }

    [Fact]
    public void Battery_offers_charge_and_replace_but_not_clean()
    {
        var g = Game.NewFirstFault();
        Walk.Near(g, "battery");
        var verbs = Actions(g, "start-battery").Options.Select(o => o.Verb).ToList();
        Assert.Equal(["inspect", "measure", "replace", "charge"], verbs); // no removable part
        Assert.Equal(1900, Option(g, "start-battery", "replace").CostNok);
    }

    [Fact]
    public void Options_know_what_can_be_reached_from_where_the_player_stands()
    {
        var g = Game.NewFirstFault();
        Walk.Near(g, "battery");
        Assert.All(Actions(g, "negative terminal").Options.Where(o => o.Verb != "measure"), o => Assert.True(o.Available, o.Verb));

        // Lid back on: she remembers the part but cannot see it, so nothing is on offer.
        g.Execute(new Refit("battery-box lid"));
        Assert.All(Actions(g, "negative terminal").Options, o =>
        {
            Assert.False(o.Available, o.Verb);
            Assert.Equal("You can't see the negative terminal from here.", o.UnavailableReason);
        });
        var terminal = Found(g, "Negative terminal");
        Assert.False(terminal.VisibleHere);
        Assert.False(terminal.ReachableHere);

        // The heat exchanger from the quarter berth: seen, not touched. The reason is the one the command would give.
        Walk.ToPlace(g, "quarter-berth");
        Walk.Uncover(g, "quarter-berth-panel");
        var inspect = Option(g, "heat exchanger", "inspect");
        Assert.True(inspect.Available);
        var replace = Option(g, "heat exchanger", "replace");
        Assert.False(replace.Available);
        Assert.Equal(
            "You can see the heat exchanger from here but can't get at it; the companionway steps are in the way.",
            replace.UnavailableReason);
        var refusal = g.Execute(new Replace("heat exchanger"));
        Assert.False(refusal.Accepted);
        Assert.Equal(replace.UnavailableReason, refusal.Narrative.Single());

        // Reach reasons come before the button reason.
        Walk.ToPlace(g, "companionway");
        Assert.Equal("You can't see the starter solenoid from here.", Option(g, "starter solenoid", "measure").UnavailableReason);

        // From the companionway with the steps off it is within reach.
        Walk.Near(g, "heat exchanger");
        Assert.True(Option(g, "heat exchanger", "replace").Available);
    }

    [Fact]
    public void Check_flow_waits_for_a_running_engine_and_for_the_outside()
    {
        var g = Game.NewFirstFault();
        Walk.Near(g, "impeller");
        // Inside the boat: the outlet is out of sight.
        var inside = Option(g, "impeller", "check-raw-water-flow");
        Assert.False(inside.Available);
        Assert.Equal("You can't see the exhaust outlet from down here.", inside.UnavailableReason);

        Walk.ToPlace(g, "cockpit");
        var idle = Option(g, "impeller", "check-raw-water-flow");
        Assert.False(idle.Available);
        Assert.Equal("Only tells you anything with the engine running.", idle.UnavailableReason);

        Walk.Do(g, "negative terminal", n => new Clean(n));
        Walk.Start(g);
        Assert.True(Option(g, "impeller", "check-raw-water-flow").Available);
        Walk.ToPlace(g, "saloon");
        Assert.False(Option(g, "impeller", "check-raw-water-flow").Available);
    }

    [Fact]
    public void Primary_filter_offers_the_bowl_check()
    {
        var g = Game.NewFirstFault();
        Walk.Near(g, "primary filter");
        Assert.Contains(Actions(g, "primary-filter").Options, o => o.Verb == "check-filter-bowl" && o.Available);
        Walk.ToPlace(g, "companionway");
        var bowl = Option(g, "primary-filter", "check-filter-bowl");
        Assert.False(bowl.Available);
        Assert.Equal("You can't see the primary fuel filter / water separator from here.", bowl.UnavailableReason);
        var r = g.Execute(new CheckFilterBowl());
        Assert.False(r.Accepted);
        Assert.Equal(bowl.UnavailableReason, r.Narrative.Single());
    }

    [Fact]
    public void Aboard_never_leaks_ids_conditions_or_undiscovered_faults()
    {
        var reference = BoatLoader.LoadSloop28();
        var hyphenated = reference.Places.Select(p => p.Id)
            .Concat(reference.Covers.Select(c => c.Id))
            .Concat(reference.Positions.Select(p => p.Id))
            .Concat(reference.Components.Select(c => c.Id))
            .Concat(reference.AllFaults.Select(f => f.Id))
            .Where(id => id.Contains('-'))
            .ToList();
        var faultNames = reference.AllFaults.Select(f => f.Name).ToList();
        string[] banned = ["condition", "present", "seriesOhms"];

        var g = Game.NewFirstFault();
        void Check()
        {
            var json = JsonSerializer.Serialize(g.Ask(new Aboard()), typeof(QueryResult));
            foreach (var id in hyphenated)
                Assert.DoesNotContain($"\"{id}\"", json, StringComparison.OrdinalIgnoreCase); // as a whole value; labels like "quarter-berth side" are prose
            foreach (var word in banned)
                Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
            foreach (var name in faultNames)
                Assert.DoesNotContain(name, json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"type\":\"aboard-view\"", json);
        }

        Check();
        // Find a lot, including the corroded terminal, and look again.
        foreach (var name in new[] { "negative terminal", "start battery", "primary filter", "impeller", "starter solenoid", "hull", "standing rig" })
            Walk.Near(g, name);
        g.Execute(new Buy("remote start button"));
        Walk.Do(g, "negative terminal", n => new Measure(n));
        Walk.Do(g, "start battery", n => new Measure(n));
        Assert.NotEmpty(Assert.IsType<StatusView>(g.Ask(new Status())).DiscoveredFaults);
        Assert.True(Found(g, "Negative terminal").KnownProblem);
        Check();
    }
}

public class LogReceiptTests
{
    [Fact]
    public void Log_entries_carry_what_they_cost()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        g.Execute(new Buy("remote start button"));
        Walk.Do(g, "start battery", n => new Replace(n));
        var log = Assert.IsType<LogView>(g.Ask(new Logbook())).Entries;
        Assert.Equal(400, log.Single(e => e.Text.StartsWith("Bought")).CostNok);
        Assert.Equal(1900, log.Single(e => e.Text.StartsWith("Replaced")).CostNok);
        Assert.Equal(0, log.Single(e => e.Text.StartsWith("Looked")).CostNok);
        Assert.Equal(0, log.First(e => e.Text.StartsWith("Went")).CostNok);
    }
}
