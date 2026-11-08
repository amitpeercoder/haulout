using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The harbourmaster (design note 008, finding 3 in 002): a plain wait at a mooring is not blind.</summary>
public class HarbourmasterTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string Rang = "The harbourmaster rang: she's sitting low on her mooring. Worth getting down there.";

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());

    private static WeatherSeries Calm() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 2));

    /// <summary>Afloat at her first port, the seacock new and the terminal clean, in the cockpit.</summary>
    private static Game Moored(int day, WeatherSeries weather, out Boat boat, bool newSeacock = true, bool calendar = true)
    {
        boat = BoatLoader.LoadSloop28(withCalendar: calendar);
        var g = new Game(boat, day, new FixedRandom(0.99), Game.DefaultSeed, weather);
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        if (newSeacock) Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        if (newSeacock) Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(g.Execute(new Launch()));
        Assert.True(Status(g).Afloat);
        return g;
    }

    private static void Wake(Boat boat, string faultId, int day) => boat.AllFaults.Single(f => f.Id == faultId).Wake(day);

    // ------------------------------------------------------------------ the bilge

    [Fact]
    public void A_failed_drain_hose_and_a_dead_pump_stop_a_sixty_day_wait_after_the_first_day_and_she_does_not_sink()
    {
        var g = Moored(100, Calm(), out var boat, calendar: false); // a calendar would cut the sixty days at the regatta (day 152)
        Wake(boat, "bilge-pump-burnt-out", 100);
        Wake(boat, "drain-hose-failed", 100);
        int start = Status(g).Day;

        var r = Ok(g.Execute(new WaitDays(60)));

        Assert.False(Status(g).Sunk);
        Assert.Equal(start + 1, Status(g).Day);
        Assert.Equal(1440, r.MinutesTaken);
        Assert.Contains("You meant to leave her for 60 days; the phone rang on day 1.", r.Narrative);
        Assert.DoesNotContain(r.Narrative, l => l.StartsWith("You leave her for", StringComparison.Ordinal));
        Assert.Contains(r.Alerts, a => a.Text == Rang);
        Assert.Null(r.Observation);

        // Left alone she would go down: the call is the warning, not a rescue.
        var next = Ok(g.Execute(new WaitDays(3)));
        Assert.True(Status(g).Sunk);
        Assert.IsType<LossView>(next.Observation);
    }

    [Fact]
    public void A_slow_leak_stops_the_wait_on_the_day_the_bilge_passes_the_level_and_the_narrative_counts_the_days_waited()
    {
        var g = Moored(100, Calm(), out var boat, newSeacock: false, calendar: false); // the sixty days would otherwise be cut at the regatta
        Wake(boat, "bilge-pump-burnt-out", 100);
        g.BilgeLitres = 115; // the weeping seacock adds 1.2 litres a day: over 120 at the end of the fifth day
        int start = Status(g).Day;

        var r = Ok(g.Execute(new WaitDays(60)));

        Assert.Equal(start + 5, Status(g).Day);
        Assert.Equal(5 * 1440, r.MinutesTaken);
        Assert.Contains("You meant to leave her for 60 days; the phone rang on day 5.", r.Narrative);
        Assert.Contains(r.Alerts, a => a.Text == Rang);
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited 5 days.");
        Assert.Contains(Log(g).Entries, e => e.Text == Rang && e.Alert);
    }

    [Fact]
    public void The_trigger_level_is_data_and_defaults_to_the_sole()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(boat.Water.SoleLitres, boat.Water.HarbourmasterLitres);
        Assert.Equal(Rang, boat.Water.HarbourmasterText);

        // The line is read from data: the same leak and the same bilge ring at 20 litres and stay quiet at the shipped 120.
        foreach (var (litres, rings) in new[] { (20, true), (120, false) })
        {
            var tuned = BoatLoader.Load(SloopJson().Replace("\"harbourmasterLitres\": 120", $"\"harbourmasterLitres\": {litres}"));
            var g = new Game(tuned, 100, new FixedRandom(0.99), Game.DefaultSeed, Calm());
            Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
            Ok(g.Execute(new Launch()));
            Wake(tuned, "bilge-pump-burnt-out", 100);
            g.BilgeLitres = 19.5; // the weeping seacock adds 1.2 litres a day
            var r = Ok(g.Execute(new WaitDays(3)));
            Assert.Equal(rings, r.Alerts.Any(a => a.Text == Rang));
        }
    }

    private static string SloopJson()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_dry_boat_waits_the_full_sixty_days_as_before()
    {
        var g = Moored(100, Calm(), out _, calendar: false); // with a calendar the sixty days stop on the morning of the regatta (day 152)
        int start = Status(g).Day;
        var r = Ok(g.Execute(new WaitDays(60)));

        Assert.Equal(start + 60, Status(g).Day);
        Assert.Equal(60 * 1440, r.MinutesTaken);
        Assert.Contains("You leave her for 60 days.", r.Narrative);
        Assert.DoesNotContain(r.Narrative, l => l.StartsWith("You meant to", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Alerts, a => a.Text == Rang);
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited 60 days.");
    }

    // ------------------------------------------------------------------ the lines

    [Fact]
    public void Lines_that_part_in_a_gale_stop_the_wait_that_day()
    {
        var weather = WeatherSeries.From(d => new WeatherDay(0, d == 255 ? 8 : 3, "southwest", 0, 6, 2));
        var g = Moored(250, weather, out _);
        g.MooringChafe = 0.95;
        var r = Ok(g.Execute(new WaitDays(30)));

        Assert.Equal(256, Status(g).Day);
        Assert.Contains("You meant to leave her for 30 days; the phone rang on day 6.", r.Narrative);
        Assert.Contains(r.Alerts, a => a.Text.StartsWith("The harbourmaster rang: she broke her mooring", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Alerts, a => a.Text == Rang);
    }

    // ------------------------------------------------------------------ what does not stop it

    [Fact]
    public void The_hatch_counter_does_not_stop_the_wait_and_is_read_once_at_the_end()
    {
        var rain = WeatherSeries.Constant(new WeatherDay(0, 3, "north", 8, 8, 7));
        var g = Moored(100, rain, out _);
        Assert.True(((AboardView)g.Ask(new Aboard())).Covers.Any(c => c.Removed), "the hatch starts open");
        var r = Ok(g.Execute(new WaitDays(40)));

        Assert.Equal(140, Status(g).Day);
        Assert.Contains("You leave her for 40 days.", r.Narrative);
        var counter = Assert.Single(r.Alerts, a => a.Text.StartsWith("The bilge pump counter has gone up by", StringComparison.Ordinal));
        Assert.EndsWith("You left the companionway hatch open; rain has been finding its way below.", counter.Text);
        Assert.Equal(0, g.PumpCyclesSinceAboard);
    }

    [Fact]
    public void The_voltmeter_does_not_stop_the_wait()
    {
        var g = Moored(100, Calm(), out var boat);
        boat.Get("start-battery").State["stateOfCharge"] = 0.3;
        var r = Ok(g.Execute(new WaitDays(20)));

        Assert.Equal(120, Status(g).Day);
        Assert.Contains("You leave her for 20 days.", r.Narrative);
        Assert.Contains(r.Alerts, a => a.Text.StartsWith("The panel voltmeter reads", StringComparison.Ordinal));
        // Read once, when she is back, as one long stretch away would read it: not a line every morning.
        Assert.Single(r.Alerts, a => a.Text.StartsWith("The panel voltmeter reads", StringComparison.Ordinal));
        Assert.Single(Log(g).Entries, e => e.Text.StartsWith("The panel voltmeter reads", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wait_that_runs_its_full_span_leaves_the_boat_as_the_same_days_waited_one_at_a_time()
    {
        // The neglect days-away count, the fault progress and the pump counter do not depend on how the days are cut up.
        var rain = WeatherSeries.Constant(new WeatherDay(0, 3, "north", 4, 2, 7));
        var (whole, wholeBoat) = Settled(rain, w => Ok(w.Execute(new WaitDays(30))));
        var (parts, partsBoat) = Settled(rain, w => { for (int i = 0; i < 30; i++) Ok(w.Execute(new WaitDays(1))); });

        foreach (var f in wholeBoat.AllFaults)
        {
            var other = partsBoat.AllFaults.Single(x => x.Id == f.Id);
            Assert.Equal(f.Progress, other.Progress, 9);
            Assert.Equal(f.State, other.State);
            Assert.Equal(f.History.LongestAway, other.History.LongestAway);
        }
        Assert.Equal(Status(whole).Day, Status(parts).Day);
        Assert.Equal(Status(whole).MinutesSpent, Status(parts).MinutesSpent);
        Assert.Equal(whole.PumpCyclesSinceAboard, parts.PumpCyclesSinceAboard);
    }

    private static (Game, Boat) Settled(WeatherSeries weather, Action<Game> wait)
    {
        var g = Moored(100, weather, out var boat);
        wait(g);
        return (g, boat);
    }

    [Fact]
    public void A_plain_pump_counter_does_not_stop_the_wait_either()
    {
        var g = Moored(100, Calm(), out _);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Refit("hatch")));
        g.BilgeLitres = 40; // under the sole: the pump runs it down on the first night
        var r = Ok(g.Execute(new WaitDays(10)));

        Assert.Equal(110, Status(g).Day);
        Assert.Contains(r.Alerts, a => a.Text.EndsWith("Worth lifting the saloon floorboards to see where the water comes from.", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Alerts, a => a.Text == Rang);
    }

    // ------------------------------------------------------------------ the hard, the sea

    [Fact]
    public void On_the_hard_nothing_changes_and_at_sea_it_is_still_refused()
    {
        var boat = BoatLoader.LoadSloop28();
        var hard = new Game(boat, 100, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        hard.BilgeLitres = 400; // nothing rings about a boat on the hard
        var r = Ok(hard.Execute(new WaitDays(30)));
        Assert.Equal(130, Status(hard).Day);
        Assert.Contains("You leave her for 30 days.", r.Narrative);
        Assert.DoesNotContain(r.Alerts, a => a.Text == Rang);
        Assert.DoesNotContain(r.Narrative, l => l.StartsWith("You meant to", StringComparison.Ordinal));

        var g = Moored(100, Calm(), out _);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Depart("Hestholmen")));
        var refused = g.Execute(new WaitDays(2));
        Assert.False(refused.Accepted);
        Assert.Equal("You're at sea: continue, heave to or anchor.", Assert.Single(refused.Narrative));
    }

    // ------------------------------------------------------------------ the leak check

    [Fact]
    public void The_call_names_no_fault()
    {
        var g = Moored(100, Calm(), out var boat, calendar: false); // a calendar would cut the sixty days at the regatta (day 152)
        Wake(boat, "bilge-pump-burnt-out", 100);
        Wake(boat, "drain-hose-failed", 100);
        var r = Ok(g.Execute(new WaitDays(60)));

        var call = Assert.Single(r.Alerts, a => a.Text == Rang);
        var said = call.Text + " " + string.Join(" ", r.Narrative.Where(l => l.StartsWith("You meant to", StringComparison.Ordinal)));
        foreach (var f in boat.AllFaults)
            Assert.DoesNotContain(f.Name, said, StringComparison.OrdinalIgnoreCase);
        foreach (var word in new[] { "hose", "pump", "seacock", "clamp", "leak", "burnt" })
            Assert.DoesNotContain(word, said, StringComparison.OrdinalIgnoreCase);
        // And nothing the call put in the logbook says what is wrong until the player finds it.
        Assert.DoesNotContain(Log(g).Entries.Where(e => e.Alert || e.Text.StartsWith("Waited", StringComparison.Ordinal)), e => e.Text.Contains("hose", StringComparison.OrdinalIgnoreCase) || e.Text.Contains("burnt", StringComparison.OrdinalIgnoreCase));
    }
}
