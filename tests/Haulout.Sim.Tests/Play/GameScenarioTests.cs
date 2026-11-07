using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

public class GameScenarioTests
{
    private static string Outcome(CommandResult r) => Assert.IsType<StartAttemptView>(r.Observation).Outcome;

    private static StatusView Status(Game g) => Assert.IsType<StatusView>(g.Ask(new Status()));

    /// <summary>Look around and open every compartment, so every component is nameable.</summary>
    private static Game Explored(Game g)
    {
        Assert.True(g.Execute(new Look()).Accepted);
        foreach (var c in Assert.IsType<CompartmentsView>(g.Ask(new Compartments())).Compartments)
            Assert.True(g.Execute(new Open(c.Id)).Accepted);
        return g;
    }

    private static Game ExploredGame() => Explored(Game.NewFirstFault());

    private static Game WithButton(Game g)
    {
        Assert.True(g.Execute(new Buy("remote start button")).Accepted);
        return g;
    }

    [Fact]
    public void Decoys_alone_do_not_fix_the_click()
    {
        var g = ExploredGame();
        Assert.True(g.Execute(new Charge("battery")).Accepted);
        Assert.True(g.Execute(new Replace("primary filter")).Accepted);
        Assert.True(g.Execute(new Replace("impeller")).Accepted);
        var r = g.Execute(new StartEngine());
        Assert.Equal("Click", Outcome(r));
        Assert.False(r.Finished);
    }

    [Fact]
    public void Cleaning_the_negative_terminal_alone_fixes_it()
    {
        var g = ExploredGame();
        Assert.True(g.Execute(new Clean("negative terminal")).Accepted);
        var r = g.Execute(new StartEngine());
        Assert.Equal("Runs", Outcome(r));
        Assert.True(r.Finished);
        Assert.True(r.EngineRunning);
    }

    [Fact]
    public void Replace_restores()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = Explored(new Game(boat));
        Assert.True(g.Execute(new Replace("negative terminal")).Accepted);
        Assert.Equal("Runs", Outcome(g.Execute(new StartEngine())));

        var stop = g.Execute(new Replace("impeller"));
        Assert.False(stop.EngineRunning);
        Assert.Equal(1.0, boat.Get("impeller").Condition);
        Assert.Equal("Runs", Outcome(g.Execute(new StartEngine())));
        var flow = Assert.IsType<RawWaterFlowView>(g.Execute(new CheckRawWaterFlow()).Observation);
        Assert.Contains("strong", flow.Text);
    }

    [Fact]
    public void Multimeter_readings_add_up()
    {
        var g = WithButton(ExploredGame());
        string[] loop =
        [
            "positive terminal", "main positive cable", "battery switch", "starter solenoid",
            "starter motor", "ground stud", "negative cable", "negative terminal",
        ];

        double SumAndCheck()
        {
            var battery = Assert.IsType<MeterReading>(g.Execute(new Measure("start battery")).Observation);
            double sum = 0;
            foreach (var name in loop)
            {
                var m = Assert.IsType<MeterReading>(g.Execute(new Measure(name)).Observation);
                Assert.NotNull(m.Value);
                sum += m.Value!.Value;
                if (name == "starter motor") Assert.Equal("volts-at-starter", m.Kind);
                else
                {
                    Assert.Equal("volts-drop", m.Kind);
                    if (name != "negative terminal") Assert.True(m.Value < 0.5, $"{name} reads {m.Value}");
                }
            }
            Assert.Equal(battery.CrankingVolts!.Value, sum, 6);
            return sum;
        }

        var dirty = Assert.IsType<MeterReading>(g.Execute(new Measure("negative terminal")).Observation);
        Assert.True(dirty.Value > 5);
        SumAndCheck();

        g.Execute(new Clean("negative terminal"));
        var clean = Assert.IsType<MeterReading>(g.Execute(new Measure("negative terminal")).Observation);
        Assert.True(clean.Value < 0.1);
        SumAndCheck();
    }

    [Fact]
    public void Full_play_through_via_the_facade()
    {
        var g = Game.NewFirstFault();
        Assert.Contains("won't start", string.Join(" ", ((BriefingView)g.Ask(new Briefing())).Lines));

        var results = new List<CommandResult>
        {
            g.Execute(new Look()),
            g.Execute(new Open("battery")),
            g.Execute(new Inspect("negative terminal")),
            g.Execute(new StartEngine()),
            g.Execute(new Buy("remote start button")),
            g.Execute(new Measure("start battery")),
            g.Execute(new Measure("negative terminal")),
            g.Execute(new Clean("negative terminal")),
            g.Execute(new StartEngine()),
        };
        Assert.All(results, r => Assert.True(r.Accepted));
        Assert.Equal("Click", Outcome(results[3]));
        Assert.Equal("Runs", Outcome(results[8]));
        Assert.True(results[8].Finished);
        Assert.Contains(results[8].Narrative, l => l.StartsWith("Time spent: 1 h 33 min. Money spent: 400"));

        var s = Status(g);
        Assert.Equal(93, s.MinutesSpent);
        Assert.Equal(400, s.MoneySpentNok);
        Assert.Equal("09:33", s.TimeOfDay);
        Assert.Equal(["remote start button"], s.Tools);
        Assert.Equal(75, s.Day);
        Assert.Equal("Runs", s.LastStartOutcome);
        Assert.True(s.Finished);
        Assert.Equal(2, s.DiscoveredFaults.Count);

        var log = Assert.IsType<LogView>(g.Ask(new Logbook()));
        Assert.Equal(results.Count + results.Sum(r => r.NewlyDiscovered.Count), log.Entries.Count);
        Assert.Contains(log.Entries, e => e.Text == "Found: Corroded negative terminal.");
    }

    [Fact]
    public void Time_rolls_over_the_day()
    {
        var g = ExploredGame();
        int before = Status(g).MinutesSpent;
        g.Execute(new Charge("battery"));
        g.Execute(new Charge("battery"));
        var s = Status(g);
        Assert.Equal(before + 960, s.MinutesSpent);
        Assert.Equal(76, s.Day);
        Assert.Equal(TimeSpan.FromMinutes((8 * 60 + before + 960) % 1440).ToString(@"hh\:mm"), s.TimeOfDay);
    }

    [Fact]
    public void Every_fault_is_discoverable_through_the_facade()
    {
        var reference = BoatLoader.LoadSloop28();
        foreach (var fault in reference.AllFaults)
        {
            var reveal = fault.Reveals.First(r => r.Discovers);
            var boat = BoatLoader.LoadSloop28();
            var g = WithButton(Explored(new Game(boat)));
            var comp = reference.Get(fault.ComponentId).Name;

            if (reveal.Action == PlayerAction.CheckRawWaterFlow)
            {
                g.Execute(new Clean("negative terminal"));
                Assert.True(g.Execute(new StartEngine()).EngineRunning);
            }
            Command cmd = reveal.Action switch
            {
                PlayerAction.Look => new Open(boat.Compartments.First(c => c.Id == reference.Get(fault.ComponentId).Location).Id),
                PlayerAction.Inspect => new Inspect(comp),
                PlayerAction.Measure => new Measure(comp),
                PlayerAction.Clean => new Clean(comp),
                PlayerAction.Charge => new Charge(comp),
                PlayerAction.Replace => new Replace(comp),
                PlayerAction.CheckFilterBowl => new CheckFilterBowl(),
                PlayerAction.CheckRawWaterFlow => new CheckRawWaterFlow(),
                PlayerAction.Start => new StartEngine(),
                _ => throw new InvalidOperationException(),
            };
            var res = g.Execute(cmd);
            Assert.True(res.Accepted, $"{fault.Id}: {string.Join(" ", res.Narrative)}");
            Assert.Contains(Status(g).DiscoveredFaults, k => k.Id == fault.Id && k.Name == fault.Name);
        }
    }

    private static List<string> PlayThrough(Game g)
    {
        var json = new List<string>();
        var opts = new JsonSerializerOptions();
        void Cmd(Command c) { var r = g.Execute(c); json.Add(JsonSerializer.Serialize(r, opts)); }
        void Ask(Query q) { var r = g.Ask(q); json.Add(JsonSerializer.Serialize(r, typeof(QueryResult), opts)); }

        Ask(new Briefing()); Ask(new Commands()); Ask(new Compartments());
        Cmd(new Look()); Cmd(new Open("battery")); Cmd(new Inspect("negative terminal"));
        Cmd(new StartEngine()); Ask(new Status()); Cmd(new Measure("start battery"));
        Cmd(new Buy("remote start button")); Cmd(new Measure("start battery"));
        Cmd(new Measure("filter")); // rejected
        Ask(new Logbook());
        return json;
    }

    [Fact]
    public void Results_hide_internal_state_and_carry_discriminators()
    {
        var g = Game.NewFirstFault();
        var json = PlayThrough(g);
        // Everything above happens before the corroded terminal is discovered.
        foreach (var j in json)
        {
            foreach (var banned in new[] { "condition", "present", "seriesOhms" })
                Assert.DoesNotContain(banned, j, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Corroded negative terminal", j);
            if (!j.Contains("\"Observation\":null")) Assert.Contains("\"type\"", j);
        }
        Assert.Contains(json, j => j.Contains("\"type\":\"start-attempt-view\""));
        Assert.Contains(json, j => j.Contains("\"type\":\"status-view\""));

        // After discovery the name appears, and the rest of the rules still hold.
        var m = g.Execute(new Measure("negative terminal"));
        var after = JsonSerializer.Serialize(m);
        Assert.Contains("Corroded negative terminal", after);
        Assert.DoesNotContain("seriesOhms", after, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ambiguous_and_unknown_names_are_rejected_for_free()
    {
        var g = ExploredGame();
        var spentBefore = Status(g).MinutesSpent;
        var logBefore = ((LogView)g.Ask(new Logbook())).Entries.Count;
        var amb = g.Execute(new Inspect("filter"));
        Assert.False(amb.Accepted);
        Assert.Contains("Primary fuel filter", string.Join(" ", amb.Narrative));
        Assert.Contains("Oil filter", string.Join(" ", amb.Narrative));

        var unk = g.Execute(new Measure("the flux capacitor"));
        Assert.False(unk.Accepted);
        Assert.Contains("You haven't found anything called 'the flux capacitor' yet.", unk.Narrative);

        Assert.False(g.Execute(new Open("nowhere")).Accepted);
        Assert.False(g.Execute(new Clean("start battery")).Accepted);
        Assert.False(g.Execute(new Replace("hull")).Accepted);
        Assert.False(g.Execute(new Charge("negative terminal")).Accepted);

        Assert.All(new[] { amb, unk }, r => { Assert.Equal(0, r.MinutesTaken); Assert.Equal(0, r.CostNok); });
        var s = Status(g);
        Assert.Equal(spentBefore, s.MinutesSpent);
        Assert.Equal(0, s.MoneySpentNok);
        Assert.Equal(logBefore, ((LogView)g.Ask(new Logbook())).Entries.Count);
    }

    [Fact]
    public void Unseen_components_cannot_be_named_until_found()
    {
        var g = Game.NewFirstFault();
        string[] cases = ["negative terminal", "flux capacitor"];
        var refusals = new List<CommandResult>();
        foreach (var name in cases)
        {
            refusals.Add(g.Execute(new Inspect(name)));
            refusals.Add(g.Execute(new Measure(name)));
            refusals.Add(g.Execute(new Clean(name)));
            refusals.Add(g.Execute(new Replace(name)));
            refusals.Add(g.Execute(new Charge(name)));
        }
        refusals.Add(g.Execute(new CheckFilterBowl()));
        Assert.All(refusals, r => { Assert.False(r.Accepted); Assert.Equal(0, r.MinutesTaken); });
        // A real but unseen part and a made-up one get the same answer: nothing is confirmed.
        Assert.Equal(
            g.Execute(new Inspect("negative terminal")).Narrative.Single().Replace("negative terminal", "X"),
            g.Execute(new Inspect("flux capacitor")).Narrative.Single().Replace("flux capacitor", "X"));
        Assert.Equal(0, Status(g).MinutesSpent);

        // Opening another compartment does not help.
        Assert.True(g.Execute(new Open("engine side")).Accepted);
        Assert.False(g.Execute(new Inspect("negative terminal")).Accepted);

        // Look shows what is in plain view; Open shows a compartment's contents.
        Assert.True(g.Execute(new Look()).Accepted);
        Assert.True(g.Execute(new Inspect("rudder")).Accepted);
        Assert.True(g.Execute(new Open("battery")).Accepted);
        Assert.True(g.Execute(new Inspect("negative terminal")).Accepted);
        WithButton(g);
        Assert.True(g.Execute(new Measure("negative terminal")).Accepted);
    }

    [Fact]
    public void Name_resolution_ignores_case_whitespace_and_leading_the()
    {
        var g = ExploredGame();
        Assert.True(g.Execute(new Inspect("  The NEGATIVE Terminal ")).Accepted);
        Assert.False(g.Execute(new Inspect("terminal")).Accepted); // two terminals
        Assert.True(g.Execute(new Inspect("negative term")).Accepted);
    }

    [Fact]
    public void Same_commands_give_identical_results()
    {
        var a = PlayThrough(Game.NewFirstFault());
        var b = PlayThrough(Game.NewFirstFault());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Without_the_button_cranking_measurements_are_refused_for_free()
    {
        var g = ExploredGame();
        var logBefore = ((LogView)g.Ask(new Logbook())).Entries.Count;
        int minutesBefore = Status(g).MinutesSpent;

        foreach (var name in new[] { "negative terminal", "starter motor" })
        {
            var r = g.Execute(new Measure(name));
            Assert.False(r.Accepted);
            Assert.Equal(0, r.MinutesTaken);
            Assert.Equal(0, r.CostNok);
            Assert.Contains("You can't crank and hold the probes at once. A remote start button from the chandlery would let you.", r.Narrative);
        }
        Assert.Equal(minutesBefore, Status(g).MinutesSpent);
        Assert.Equal(logBefore, ((LogView)g.Ask(new Logbook())).Entries.Count);

        // The battery gives resting volts only, and still reveals the flat battery.
        var b = g.Execute(new Measure("start battery"));
        Assert.True(b.Accepted);
        var m = Assert.IsType<MeterReading>(b.Observation);
        Assert.Equal("volts-resting", m.Kind);
        Assert.Null(m.CrankingVolts);
        Assert.Contains("remote start button", string.Join(" ", b.Narrative));
        Assert.NotEmpty(b.NewlyDiscovered);

        // Glow plugs need no cranking.
        Assert.True(g.Execute(new Measure("glow plugs")).Accepted);

        int money = Status(g).MoneySpentNok;
        var buy = g.Execute(new Buy("Remote Start"));
        Assert.True(buy.Accepted);
        Assert.Equal(400, buy.CostNok);
        Assert.Equal(30, buy.MinutesTaken);
        Assert.Equal(money + 400, Status(g).MoneySpentNok);
        Assert.Contains(((LogView)g.Ask(new Logbook())).Entries, e => e.Text == "Bought a remote start button.");

        var n = g.Execute(new Measure("negative terminal"));
        Assert.True(n.Accepted);
        Assert.Equal("volts-drop", Assert.IsType<MeterReading>(n.Observation).Kind);
        Assert.NotNull(Assert.IsType<MeterReading>(g.Execute(new Measure("start battery")).Observation).CrankingVolts);
    }

    [Fact]
    public void Buying_twice_or_an_unknown_item_is_rejected_for_free()
    {
        var g = ExploredGame();
        var unk = g.Execute(new Buy("anchor"));
        Assert.False(unk.Accepted);
        Assert.Contains("The chandlery doesn't have 'anchor'.", unk.Narrative);
        WithButton(g);
        int minutes = Status(g).MinutesSpent;
        var again = g.Execute(new Buy("button"));
        Assert.False(again.Accepted);
        Assert.Contains("You already have one.", again.Narrative);
        Assert.Equal(minutes, Status(g).MinutesSpent);
        Assert.Equal(400, Status(g).MoneySpentNok);
    }

    [Fact]
    public void Corroded_terminal_is_discoverable_without_the_button()
    {
        var g = ExploredGame();
        Assert.True(g.Execute(new Clean("negative terminal")).Accepted);
        Assert.Contains(Status(g).DiscoveredFaults, k => k.Name == "Corroded negative terminal");
    }

    [Fact]
    public void Low_battery_cranks_laboured_then_runs()
    {
        var g = ExploredGame();
        Assert.True(g.Execute(new Clean("negative terminal")).Accepted);
        var r = g.Execute(new StartEngine());
        var view = Assert.IsType<StartAttemptView>(r.Observation);
        Assert.Equal("Runs", view.Outcome);
        Assert.True(r.Finished);
        Assert.True(view.LabouredCrank);
        Assert.Contains("laboured", view.Description);

        var g2 = ExploredGame();
        Assert.True(g2.Execute(new Charge("battery")).Accepted);
        Assert.True(g2.Execute(new Clean("negative terminal")).Accepted);
        var r2 = g2.Execute(new StartEngine());
        var v2 = Assert.IsType<StartAttemptView>(r2.Observation);
        Assert.Equal("Runs", v2.Outcome);
        Assert.False(v2.LabouredCrank);
        Assert.DoesNotContain("laboured", v2.Description);
    }

    [Fact]
    public void Commands_query_lists_the_verbs()
    {
        var list = Assert.IsType<CommandList>(Game.NewFirstFault().Ask(new Commands()));
        Assert.Contains(list.Commands, c => c.Verb == "inspect" && c.Argument == "component");
        Assert.Contains(list.Commands, c => c.Verb == "buy" && c.Argument == "item");
    }
}
