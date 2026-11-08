using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// The away-escalation fix (design note 006): what happens while the player is away is not perceived. An escalation that happens
/// during idle time discovers nothing and writes nothing; the player finds it by looking, and the cause line comes with the finding.
/// </summary>
public class LeakTests
{
    private const string Weeping = "Weeping raw-water seacock";
    private const string Seized = "Seized raw-water seacock";
    private const string HoseOff = "Cockpit drain hose off its tail";

    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static Game NewGame() => new(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Fair());

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static ReadinessView Ready(Game g) => (ReadinessView)g.Ask(new Readiness());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());

    /// <summary>Everything an adapter could show the player, as the JSON it would be sent as.</summary>
    private static string Everything(Game g, params CommandResult[] results) =>
        JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())) + JsonSerializer.Serialize<QueryResult>(g.Ask(new Readiness()))
        + JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook())) + JsonSerializer.Serialize<QueryResult>(g.Ask(new Aboard()))
        + string.Concat(results.Select(r => JsonSerializer.Serialize(r)));

    /// <summary>The player knows the weeping seacock and defers it, then waits a day at a time until it has seized (read from the internals).</summary>
    private static (Game G, CommandResult LastWait) DeferredSeacockSeizesWhileAway()
    {
        var g = NewGame();
        Ok(Walk.Do(g, "seacock", n => new Inspect(n)));
        Ok(g.Execute(new Defer("seacock")));
        CommandResult last = null!;
        for (int i = 0; i < 60 && !g.HasEscalated("seacock-weeping"); i++) last = Ok(g.Execute(new WaitDays(1)));
        Assert.True(g.HasEscalated("seacock-weeping"));
        return (g, last);
    }

    [Fact]
    public void A_known_fault_that_escalates_while_she_is_left_shows_nothing_of_it_anywhere()
    {
        var (g, wait) = DeferredSeacockSeizesWhileAway();
        var all = Everything(g, wait);

        Assert.DoesNotContain(Seized, all);
        Assert.DoesNotContain("has seized", all);
        Assert.DoesNotContain("It came on", all);
        Assert.Empty(wait.NewlyDiscovered);
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.StartsWith("Found: Seized", StringComparison.Ordinal));

        // What the player knows has not changed: the weeping seacock is still on the list, still deferred.
        Assert.Equal("Deferred", Ready(g).Open.Single(o => o.Problem == Weeping).State);
        Assert.Contains(Weeping, Status(g).DiscoveredFaults.Select(k => k.Name));
        Assert.True(((AboardView)g.Ask(new Aboard())).Components.Single(c => c.Name == "Raw-water seacock").KnownProblem);
        Assert.Equal(FaultState.Latent, g.StateOf("seacock-seized")); // and the seized one has no sign
    }

    [Fact]
    public void Looking_at_the_seacock_finds_it_seized_writes_the_cause_line_just_before_the_finding_and_replaces_the_readiness_item()
    {
        var (g, _) = DeferredSeacockSeizesWhileAway();
        var inspect = Ok(Walk.Do(g, "seacock", n => new Inspect(n)));

        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == Seized);
        Assert.Contains("It came on", string.Join(" ", inspect.Narrative));
        var entries = Log(g).Entries.ToList();
        int found = entries.FindIndex(e => e.Text == $"Found: {Seized}.");
        Assert.True(found > 0);
        Assert.StartsWith("The raw-water seacock has seized.", entries[found - 1].Text);
        Assert.Contains("It came on", entries[found - 1].Text);

        var open = Ready(g).Open.Select(o => o.Problem).ToList();
        Assert.Contains(Seized, open);
        Assert.DoesNotContain(Weeping, open);
        Assert.DoesNotContain(Weeping, Status(g).DiscoveredFaults.Select(k => k.Name));
    }

    [Fact]
    public void A_known_fault_replaced_before_its_successor_was_found_reads_as_fixed_and_nothing_is_revealed()
    {
        var (g, _) = DeferredSeacockSeizesWhileAway();
        Assert.DoesNotContain(Weeping, Ready(g).Fixed);
        var yard = Ok(g.Execute(new FixByYard("seacock"))); // the player pays the yard for what they know
        Assert.DoesNotContain(Seized, Everything(g, yard));
        Assert.Contains(Weeping, Ready(g).Fixed);
        Assert.DoesNotContain(Weeping, Ready(g).Open.Select(o => o.Problem));
        Assert.Equal(FaultState.Fixed, g.StateOf("seacock-seized"));
    }

    [Fact]
    public void A_drain_hose_that_comes_off_while_she_is_left_shows_its_sign_on_arrival_and_is_symptomatic_not_found()
    {
        var g = NewGame();
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Remove("locker lid")));
        Assert.Contains(Ok(g.Execute(new Inspect("drain hoses"))).NewlyDiscovered, k => k.Name == "Rusted cockpit drain hose clamps");
        Ok(g.Execute(new Refit("locker lid")));

        CommandResult wait = null!;
        for (int i = 0; i < 15 && !g.HasEscalated("drain-clamps-rusted"); i++) wait = Ok(g.Execute(new WaitDays(10)));
        Assert.True(g.HasEscalated("drain-clamps-rusted"));

        const string sign = "The cockpit drain hose hangs loose from its tail; you can see it through the gap at the cockpit-locker lid. Worth lifting the lid.";
        Assert.Contains(wait.Alerts, a => a.Text == sign);
        Assert.Contains(Log(g).Entries, e => e.Alert && e.Text == sign);
        Assert.DoesNotContain(HoseOff, Everything(g, wait));
        Assert.DoesNotContain("has come off its tail", Everything(g, wait));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.StartsWith("Found", StringComparison.Ordinal) && e.Text.Contains("hose off", StringComparison.Ordinal));
        Assert.Equal(FaultState.Symptomatic, g.StateOf("drain-hose-failed"));
        Assert.DoesNotContain(Ready(g).Open, o => o.Problem == HoseOff);

        var inspect = Ok(Walk.Do(g, "drain hoses", n => new Inspect(n)));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == HoseOff);
        var entries = Log(g).Entries.ToList();
        int found = entries.FindIndex(e => e.Text == $"Found: {HoseOff}.");
        Assert.True(found > 0);
        Assert.StartsWith("The cockpit drain hose has come off its tail.", entries[found - 1].Text);
        Assert.Equal(FaultState.Discovered, g.StateOf("drain-hose-failed"));
        Assert.Contains(HoseOff, Ready(g).Open.Select(o => o.Problem));
    }

    [Fact]
    public void A_wait_result_carries_only_the_leaving_the_weather_and_the_arrival_alerts()
    {
        var g = NewGame();
        var wait = Ok(g.Execute(new WaitDays(45)));
        Assert.StartsWith("You leave her for 45 days.", wait.Narrative[0]);
        Assert.Equal(3, wait.Narrative.Count); // the leaving, the weather and the week line (pay and bills fell due on the way: design note 010)
        Assert.StartsWith($"While you were away: payday {BoatLoader.LoadSloop28().Calendar.PayNok} NOK", wait.Narrative[2]);
        Assert.Empty(wait.NewlyDiscovered);
    }
}
