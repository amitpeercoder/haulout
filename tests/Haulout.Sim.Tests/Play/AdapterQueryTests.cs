using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

/// <summary>The Aboard and ComponentActions queries the web adapter draws from (design note 003).</summary>
public class AdapterQueryTests
{
    private static AboardView Aboard(Game g) => Assert.IsType<AboardView>(g.Ask(new Aboard()));

    private static ComponentActionsView Actions(Game g, string name) =>
        Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions(name)));

    private static AboardCompartment Compartment(Game g, string id) => Aboard(g).Compartments.Single(c => c.Id == id);

    [Fact]
    public void Nothing_is_found_before_going_aboard()
    {
        var g = Game.NewFirstFault();
        var view = Aboard(g);
        Assert.Equal(8, view.Compartments.Count);
        Assert.All(view.Compartments, c => { Assert.False(c.Opened); Assert.Empty(c.Components); });
        Assert.False(Actions(g, "negative terminal").Found);
    }

    [Fact]
    public void Look_opens_the_plain_view_compartments_only()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        Assert.True(Compartment(g, "cockpit").Opened);
        Assert.True(Compartment(g, "deck").Opened);
        Assert.True(Compartment(g, "hull-outside").Opened);
        Assert.False(Compartment(g, "battery-box").Opened);
        Assert.Empty(Compartment(g, "battery-box").Components);
    }

    [Fact]
    public void Queries_cost_no_time()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        g.Execute(new Open("battery box"));
        var before = Assert.IsType<StatusView>(g.Ask(new Status())).MinutesSpent;
        Aboard(g);
        Actions(g, "negative-terminal");
        Assert.Equal(before, Assert.IsType<StatusView>(g.Ask(new Status())).MinutesSpent);
    }

    [Fact]
    public void Solving_the_first_fault_by_the_options_the_ui_shows()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        g.Execute(new Open("battery-box"));

        var box = Compartment(g, "battery-box");
        Assert.True(box.Opened);
        Assert.Contains(box.Components, c => c.Id == "negative-terminal" && !c.HasKnownFault);

        var options = Actions(g, "negative-terminal");
        Assert.True(options.Found);
        Assert.Equal("Negative terminal", options.ComponentName, ignoreCase: true);
        var measure = options.Options.Single(o => o.Verb == "measure");
        Assert.False(measure.Available);
        Assert.NotNull(measure.UnavailableReason);
        Assert.Contains(options.Options, o => o.Verb == "clean" && o.Available);
        Assert.Contains(options.Options, o => o.Verb == "replace" && o.CostNok == 60);

        // Inspect only hints; the marker stays neutral.
        g.Execute(new Inspect("negative-terminal"));
        Assert.False(Compartment(g, "battery-box").Components.Single(c => c.Id == "negative-terminal").HasKnownFault);

        Assert.True(g.Execute(new Buy("remote start button")).Accepted);
        Assert.True(Actions(g, "negative-terminal").Options.Single(o => o.Verb == "measure").Available);

        Assert.True(g.Execute(new Measure("negative-terminal")).Accepted);
        Assert.True(Compartment(g, "battery-box").Components.Single(c => c.Id == "negative-terminal").HasKnownFault);

        g.Execute(new Clean("negative-terminal"));
        Assert.False(Compartment(g, "battery-box").Components.Single(c => c.Id == "negative-terminal").HasKnownFault);

        Assert.True(g.Execute(new StartEngine()).Finished);
    }

    [Fact]
    public void Battery_offers_charge_and_replace_but_not_clean()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Open("battery box"));
        var verbs = Actions(g, "start-battery").Options.Select(o => o.Verb).ToList();
        Assert.Equal(["inspect", "measure", "replace", "charge"], verbs);
        Assert.Equal(1900, Actions(g, "start-battery").Options.Single(o => o.Verb == "replace").CostNok);
    }

    [Fact]
    public void Check_flow_waits_for_a_running_engine()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Open("engine front"));
        var flow = Actions(g, "impeller").Options.Single(o => o.Verb == "check-raw-water-flow");
        Assert.False(flow.Available);
        g.Execute(new Open("battery box"));
        g.Execute(new Clean("negative terminal"));
        g.Execute(new StartEngine());
        Assert.True(Actions(g, "impeller").Options.Single(o => o.Verb == "check-raw-water-flow").Available);
    }

    [Fact]
    public void Primary_filter_offers_the_bowl_check()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Open("cockpit locker"));
        Assert.Contains(Actions(g, "primary-filter").Options, o => o.Verb == "check-filter-bowl" && o.Available);
    }
}

public class LogReceiptTests
{
    [Fact]
    public void Log_entries_carry_what_they_cost()
    {
        var g = Game.NewFirstFault();
        g.Execute(new Look());
        g.Execute(new Open("battery box"));
        g.Execute(new Buy("remote start button"));
        g.Execute(new Replace("start battery"));
        var log = Assert.IsType<LogView>(g.Ask(new Logbook())).Entries;
        Assert.Equal(400, log.Single(e => e.Text.StartsWith("Bought")).CostNok);
        Assert.Equal(1900, log.Single(e => e.Text.StartsWith("Replaced")).CostNok);
        Assert.Equal(0, log.Single(e => e.Text.StartsWith("Looked")).CostNok);
    }
}
