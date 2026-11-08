using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The boat talks (design note 006, step 5): alerts through the facade.</summary>
public class AlertTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static WeatherSeries Rain(double mm) => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", mm, 6, 3));

    private static Game NewGame(WeatherSeries weather, out Boat boat)
    {
        boat = BoatLoader.LoadSloop28();
        return new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, weather);
    }

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static List<LogEntry> AlertLog(Game g) => Log(g).Entries.Where(e => e.Alert).ToList();
    private static NoticesView Notices(Game g) => (NoticesView)g.Ask(new Notices());

    /// <summary>Ready to start (terminal cleaned, battery charged), floorboards left as they are.</summary>
    private static void ReadyToStart(Game g)
    {
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "start battery", c => new Charge(c)));
    }

    // ------------------------------------------------------------------ arrival: the pump counter

    [Fact]
    public void Waiting_on_the_hard_in_rain_with_the_clamps_gone_brings_a_pump_counter_alert_with_the_right_count()
    {
        var g = NewGame(Rain(12), out var boat);
        var pump = boat.Get("bilge-pump");
        double cyclesBefore = pump.State.GetValueOrDefault("cycles");

        var r = Ok(g.Execute(new WaitDays(3)));
        int cycles = (int)(pump.State.GetValueOrDefault("cycles") - cyclesBefore);
        Assert.True(cycles > 0, "the rain should have run the pump");

        var counter = Assert.Single(r.Alerts, a => a.Text.StartsWith("The bilge pump counter has gone up by"));
        var number = cycles <= 20
            ? new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" }[cycles]
            : cycles.ToString();
        Assert.Contains($"gone up by {number} since you were last aboard", counter.Text);
        Assert.Contains("lifting the saloon floorboards", counter.Text);

        // It is a logbook line, marked as an alert.
        var entry = Assert.Single(AlertLog(g), e => e.Text == counter.Text);
        Assert.Equal(counter.Day, entry.Day);

        // Read once: nothing new now, and the next command carries nothing.
        Assert.Empty(Notices(g).Alerts);
        Assert.Empty(Ok(g.Execute(new Look())).Alerts);

        // A second wait gives a new count, from where the first left off.
        double before2 = pump.State.GetValueOrDefault("cycles");
        var r2 = Ok(g.Execute(new WaitDays(2)));
        int cycles2 = (int)(pump.State.GetValueOrDefault("cycles") - before2);
        Assert.True(cycles2 > 0);
        var counter2 = Assert.Single(r2.Alerts, a => a.Text.StartsWith("The bilge pump counter"));
        Assert.Contains($"gone up by {(cycles2 <= 20 ? new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" }[cycles2] : cycles2.ToString())} since", counter2.Text);
        Assert.Equal(2, AlertLog(g).Count(e => e.Text.StartsWith("The bilge pump counter")));
    }

    [Fact]
    public void A_dry_wait_brings_no_pump_alert()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        var r = Ok(g.Execute(new WaitDays(2)));
        Assert.DoesNotContain(r.Alerts, a => a.Text.Contains("pump counter"));
    }

    [Fact]
    public void Every_accepted_command_hands_its_alerts_over_so_status_and_notices_find_none_unread()
    {
        var g = NewGame(Rain(12), out _);
        var wait = Ok(g.Execute(new WaitDays(3)));
        Assert.NotEmpty(wait.Alerts);
        Assert.Null(((StatusView)g.Ask(new Status())).Notices);
        Assert.Empty(Notices(g).Alerts);
        Assert.Contains(AlertLog(g), e => e.Text == wait.Alerts[0].Text);
    }

    // ------------------------------------------------------------------ arrival: smell, sole, voltmeter

    [Fact]
    public void A_deep_bilge_with_the_floorboards_in_smells_on_coming_back()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        g.BilgeLitres = 60; // the pump (float switch at 15 L) is fitted, so use a dead pump: pull the fuse
        Walk.Uncover(g, "bilge-pump-fuse");
        g.BilgeLitres = 60;
        var r = Ok(g.Execute(new WaitDays(1)));
        Assert.Contains(r.Alerts, a => a.Text.StartsWith("A damp, sour smell from under the saloon sole."));
    }

    [Fact]
    public void A_deep_bilge_with_the_floorboards_out_has_no_smell_alert()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Walk.Uncover(g, "bilge-pump-fuse");
        Walk.Uncover(g, "saloon-floorboards");
        g.BilgeLitres = 60;
        var r = Ok(g.Execute(new WaitDays(1)));
        Assert.DoesNotContain(r.Alerts, a => a.Text.Contains("sour smell"));
    }

    [Fact]
    public void Water_over_the_sole_on_coming_back_is_told()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Walk.Uncover(g, "bilge-pump-fuse");
        g.BilgeLitres = 140;
        var r = Ok(g.Execute(new WaitDays(1)));
        Assert.Contains(r.Alerts, a => a.Text.StartsWith("Water over the saloon sole."));
        Assert.DoesNotContain(r.Alerts, a => a.Text.Contains("sour smell"));
    }

    [Fact]
    public void A_low_battery_reads_on_the_voltmeter_on_coming_back_and_on_the_first_aboard_step_of_a_day()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        boat.Get("start-battery").State["stateOfCharge"] = 0.3;
        var cap = boat.AllFaults.Single(f => f.Id == "battery-low-charge"); // the legacy 60 % cap sits above 0.3: no help needed
        Assert.True(cap.Present);

        var r = Ok(g.Execute(new WaitDays(1)));
        var alert = Assert.Single(r.Alerts, a => a.Text.StartsWith("The panel voltmeter reads"));
        Assert.Matches(@"reads \d+\.\d V this morning\. The battery isn't holding its charge; worth a meter on it\.$", alert.Text);

        // Aboard daily: the morning alert fires once a day.
        var look = Ok(g.Execute(new Look()));
        Assert.Empty(look.Alerts); // today's alert was raised on arrival
        for (int i = 0; i < 30; i++) Ok(g.Execute(new Look())); // ~one hour of looks per ... until the next morning
        Assert.Single(AlertLog(g), e => e.Text.StartsWith("The panel voltmeter") && e.Day == alert.Day);
    }

    [Fact]
    public void A_low_battery_alerts_each_morning_when_the_player_is_aboard_daily()
    {
        var g = NewGame(WeatherSeries.Fair(), out var boat);
        boat.Get("start-battery").State["stateOfCharge"] = 0.3;
        // Stay aboard across a midnight by working: pump the bilge from the cockpit (15 min each) for 20 hours.
        var all = new List<Alert>();
        for (int i = 0; i < 90; i++) all.AddRange(Ok(g.Execute(new PumpBilge())).Alerts);
        Assert.Equal(2, all.Count(a => a.Text.StartsWith("The panel voltmeter")));
        Assert.Equal(all.Select(a => a.Day).Distinct().Count(), all.Count(a => a.Text.StartsWith("The panel voltmeter")));
    }

    // ------------------------------------------------------------------ present: the lamp, the alarm, the sole

    [Fact]
    public void The_alternator_lamp_comes_on_with_the_engine_running_on_a_slipping_belt_once_a_day()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        ReadyToStart(g);
        var start = Ok(Walk.Start(g));
        Assert.True(start.EngineRunning);
        var lamp = Assert.Single(start.Alerts);
        Assert.StartsWith("The alternator lamp on the panel stays lit with the engine running.", lamp.Text);
        Assert.Contains("front of the engine", lamp.Text);
        Assert.DoesNotContain("belt", lamp.Text, StringComparison.OrdinalIgnoreCase);

        // Not on every command.
        for (int i = 0; i < 5; i++) Assert.DoesNotContain(Ok(g.Execute(new PumpBilge())).Alerts, a => a.Text.Contains("alternator lamp"));
        Assert.Single(AlertLog(g), e => e.Text.Contains("alternator lamp"));
    }

    [Fact]
    public void With_a_healthy_alternator_there_is_no_lamp()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        ReadyToStart(g);
        Ok(Walk.Do(g, "alternator belt", c => new Inspect(c)));
        Ok(g.Execute(new Replace("alternator belt")));
        var start = Ok(Walk.Start(g));
        Assert.True(start.EngineRunning);
        Assert.Empty(start.Alerts);
        Assert.Empty(Ok(g.Execute(new PumpBilge())).Alerts);
    }

    [Fact]
    public void A_seized_seacock_sets_the_temperature_alarm_off_once_a_day()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        ReadyToStart(g);
        Ok(g.Execute(new Launch()));
        var start = Ok(Walk.Start(g));
        Assert.True(start.EngineRunning);
        var alarm = Assert.Single(start.Alerts, a => a.Text.StartsWith("The temperature alarm whines"));
        Assert.Contains("smell of hot rubber", alarm.Text);
        Assert.Contains("check the cooling water at the exhaust", alarm.Text);
        Assert.DoesNotContain("seacock", alarm.Text, StringComparison.OrdinalIgnoreCase);

        var flow = Ok(g.Execute(new CheckRawWaterFlow()));
        Assert.DoesNotContain(flow.Alerts, a => a.Text.Contains("temperature alarm"));
        Assert.Single(AlertLog(g), e => e.Text.Contains("temperature alarm"));
    }

    [Fact]
    public void Water_crossing_the_sole_while_aboard_is_told_once()
    {
        var g = NewGame(WeatherSeries.Fair(), out _);
        Ok(g.Execute(new Launch()));
        Walk.Uncover(g, "bilge-pump-fuse");
        Walk.ToPlace(g, "cockpit");
        g.BilgeLitres = 125; // over the sole, with no pump power

        var r = Ok(g.Execute(new Look()));
        var sole = Assert.Single(r.Alerts);
        Assert.Equal("Water over the saloon sole. Pump by hand from the cockpit and find where it's coming in.", sole.Text);
        Assert.Empty(Ok(g.Execute(new PumpBilge())).Alerts.Where(a => a.Text.Contains("saloon sole")));
        Assert.Single(AlertLog(g), e => e.Text.StartsWith("Water over the saloon sole"));
    }

    // ------------------------------------------------------------------ nothing on day one, nothing on refusals

    [Fact]
    public void A_fresh_game_says_nothing_on_the_first_look()
    {
        var g = Game.NewFirstFault();
        var r = Ok(g.Execute(new Look()));
        Assert.Empty(r.Alerts);
        Assert.Null(((StatusView)g.Ask(new Status())).Notices);
        Assert.Empty(Notices(g).Alerts);
        Assert.Empty(AlertLog(g));
        // And no voltmeter on the opening day: the shipped 60 % is above the line.
        Ok(g.Execute(new GoTo("companionway")));
        Assert.Empty(AlertLog(g));
    }

    [Fact]
    public void Refusals_carry_no_alerts_and_do_not_swallow_them()
    {
        var g = NewGame(Rain(12), out _);
        // Raise some alerts without reading them: they come with the next accepted result, never a refusal.
        var wait = Ok(g.Execute(new WaitDays(3)));
        Assert.NotEmpty(wait.Alerts);
        var refused = g.Execute(new GoTo("nowhere in particular"));
        Assert.False(refused.Accepted);
        Assert.Empty(refused.Alerts);

        var g2 = NewGame(WeatherSeries.Fair(), out var boat);
        boat.Get("start-battery").State["stateOfCharge"] = 0.3;
        var refused2 = g2.Execute(new GoTo("nowhere in particular"));
        Assert.Empty(refused2.Alerts);
        var look = Ok(g2.Execute(new Look()));
        Assert.Single(look.Alerts, a => a.Text.StartsWith("The panel voltmeter"));
    }

    // ------------------------------------------------------------------ nothing leaks

    [Fact]
    public void Alerts_name_no_fault_and_nothing_serialised_carries_a_fault_id()
    {
        var g = NewGame(Rain(25), out var boat);
        var results = new List<CommandResult>();
        var json = new List<string>();
        var faults = boat.AllFaults.ToList();

        void Snapshot()
        {
            json.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Status())));
            json.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook())));
            json.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Readiness())));
            json.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Aboard())));
            json.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Notices())));
        }

        void Run(Command c)
        {
            var r = g.Execute(c);
            results.Add(r);
            json.Add(JsonSerializer.Serialize(r));
            Snapshot();
        }

        Run(new Look());
        Run(new WaitDays(4));
        Run(new Launch());
        Run(new WaitDays(3));
        Run(new StartEngine());
        Run(new PumpBilge());
        Walk.Uncover(g, "bilge-pump-fuse");
        Walk.ToPlace(g, "cockpit");
        Run(new StartEngine());
        Run(new CheckRawWaterFlow());
        Run(new StopEngine());
        for (int i = 0; i < 60 && results.All(r => r.Observation is not LossView); i++) Run(new WaitDays(2));
        Run(new Look()); // refused if she sank
        Snapshot();

        var alerts = results.SelectMany(r => r.Alerts).ToList();
        Assert.NotEmpty(alerts);
        foreach (var a in alerts)
            foreach (var f in faults)
                Assert.DoesNotContain(f.Name, a.Text, StringComparison.OrdinalIgnoreCase);
        foreach (var e in AlertLog(g))
            foreach (var f in faults)
                Assert.DoesNotContain(f.Name, e.Text, StringComparison.OrdinalIgnoreCase);

        var everything = string.Concat(json);
        foreach (var f in faults) Assert.DoesNotContain(f.Id, everything);
    }

    [Fact]
    public void The_notices_query_round_trips_through_json_with_its_discriminator()
    {
        var q = JsonSerializer.Deserialize<Query>("""{"type":"notices"}""");
        Assert.IsType<Notices>(q);
        Assert.Contains("\"type\":\"notices\"", JsonSerializer.Serialize<Query>(new Notices()));
        var catalogue = (CommandList)new Game(BoatLoader.LoadSloop28()).Ask(new Commands());
        Assert.Contains(catalogue.Commands, c => c.Verb == "notices");
    }
}
