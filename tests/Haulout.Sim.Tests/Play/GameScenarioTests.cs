using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

public class GameScenarioTests
{
    private static string Outcome(CommandResult r) => Assert.IsType<StartAttemptView>(r.Observation).Outcome;

    private static StatusView Status(Game g) => Assert.IsType<StatusView>(g.Ask(new Status()));

    private static Game NewGame() => Game.NewFirstFault();

    private static Game WithButton(Game g)
    {
        Assert.True(g.Execute(new Buy("remote start button")).Accepted);
        return g;
    }

    private static CommandResult Do(Game g, string name, Func<string, Command> make) => Walk.Do(g, name, make);

    private static CommandResult Start(Game g) => Walk.Start(g);

    private static int LogCount(Game g) => ((LogView)g.Ask(new Logbook())).Entries.Count;

    /// <summary>The command is refused, says why, and costs nothing: no time, no money, no log line.</summary>
    private static string AssertRefusedForFree(Game g, Command c)
    {
        int minutes = Status(g).MinutesSpent, log = LogCount(g);
        var r = g.Execute(c);
        Assert.False(r.Accepted);
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        Assert.Equal(minutes, Status(g).MinutesSpent);
        Assert.Equal(log, LogCount(g));
        return string.Join(" ", r.Narrative);
    }

    [Fact]
    public void Decoys_alone_do_not_fix_the_click()
    {
        var g = NewGame();
        Assert.True(Do(g, "battery", n => new Charge(n)).Accepted);
        Assert.True(Do(g, "primary filter", n => new Replace(n)).Accepted);
        Assert.True(Do(g, "impeller", n => new Replace(n)).Accepted);
        var r = Start(g);
        Assert.Equal("Click", Outcome(r));
        Assert.False(r.Finished);
    }

    [Fact]
    public void Cleaning_the_negative_terminal_alone_fixes_it()
    {
        var g = NewGame();
        Assert.True(Do(g, "negative terminal", n => new Clean(n)).Accepted);
        var r = Start(g);
        Assert.Equal("Runs", Outcome(r));
        Assert.True(r.Finished);
        Assert.True(r.EngineRunning);
    }

    [Fact]
    public void Replace_restores()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = new Game(boat);
        Assert.True(Do(g, "negative terminal", n => new Replace(n)).Accepted);
        Assert.Equal("Runs", Outcome(Start(g)));

        var stop = Do(g, "impeller", n => new Replace(n));
        Assert.False(stop.EngineRunning);
        Assert.Equal(1.0, boat.Get("impeller").Condition);
        Assert.Equal("Runs", Outcome(Start(g)));
        var flow = Assert.IsType<RawWaterFlowView>(g.Execute(new CheckRawWaterFlow()).Observation);
        Assert.Contains("strong", flow.Text);
    }

    [Fact]
    public void Multimeter_readings_add_up()
    {
        var g = WithButton(NewGame());
        string[] loop =
        [
            "positive terminal", "main positive cable", "battery switch", "starter solenoid",
            "starter motor", "ground stud", "negative cable", "negative terminal",
        ];

        double SumAndCheck()
        {
            var battery = Assert.IsType<MeterReading>(Do(g, "start battery", n => new Measure(n)).Observation);
            double sum = 0;
            foreach (var name in loop)
            {
                var m = Assert.IsType<MeterReading>(Do(g, name, n => new Measure(n)).Observation);
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

        var dirty = Assert.IsType<MeterReading>(Do(g, "negative terminal", n => new Measure(n)).Observation);
        Assert.True(dirty.Value > 5);
        SumAndCheck();

        Do(g, "negative terminal", n => new Clean(n));
        var clean = Assert.IsType<MeterReading>(Do(g, "negative terminal", n => new Measure(n)).Observation);
        Assert.True(clean.Value < 0.1);
        SumAndCheck();
    }

    [Fact]
    public void Full_play_through_via_the_facade()
    {
        var g = NewGame();
        Assert.Contains("won't start", string.Join(" ", ((BriefingView)g.Ask(new Briefing())).Lines));
        Assert.Contains("cockpit", string.Join(" ", ((BriefingView)g.Ask(new Briefing())).Lines));

        var results = new List<CommandResult>
        {
            g.Execute(new Look()),                          // 2
            g.Execute(new GoTo("companionway")),            // 1
            g.Execute(new GoTo("quarter berth")),           // 1
            g.Execute(new Remove("battery-box lid")),       // 2
            g.Execute(new Inspect("negative terminal")),    // 5
            g.Execute(new GoTo("companionway")),            // 1
            g.Execute(new GoTo("cockpit")),                 // 1
            g.Execute(new StartEngine()),                   // 2  click
            g.Execute(new Buy("remote start button")),      // 30, 400
            g.Execute(new GoTo("companionway")),            // 1
            g.Execute(new GoTo("quarter berth")),           // 1
            g.Execute(new Measure("start battery")),        // 10
            g.Execute(new Measure("negative terminal")),    // 10
            g.Execute(new Clean("negative terminal")),      // 30
            g.Execute(new GoTo("companionway")),            // 1
            g.Execute(new GoTo("cockpit")),                 // 1
            g.Execute(new StartEngine()),                   // 2  runs
        };
        Assert.All(results, r => Assert.True(r.Accepted, string.Join(" ", r.Narrative)));
        Assert.Equal("Click", Outcome(results[7]));
        var last = results[^1];
        Assert.Equal("Runs", Outcome(last));
        Assert.True(last.Finished);
        Assert.Contains(last.Narrative, l => l.StartsWith("Time spent: 1 h 41 min. Money spent: 400"));

        var s = Status(g);
        Assert.Equal(101, s.MinutesSpent);
        Assert.Equal(400, s.MoneySpentNok);
        Assert.Equal("09:41", s.TimeOfDay);
        Assert.Equal("Cockpit", s.Place);
        Assert.Equal(["remote start button"], s.Tools);
        Assert.Equal(75, s.Day);
        Assert.Equal("Runs", s.LastStartOutcome);
        Assert.True(s.Finished);
        Assert.Equal(2, s.DiscoveredFaults.Count);

        var log = Assert.IsType<LogView>(g.Ask(new Logbook()));
        Assert.Equal(results.Count + results.Sum(r => r.NewlyDiscovered.Count), log.Entries.Count);
        Assert.Contains(log.Entries, e => e.Text == "Found: Corroded negative terminal.");
        Assert.Contains(log.Entries, e => e.Text == "Went into the quarter berth.");
        Assert.Contains(log.Entries, e => e.Text == "Took off the battery-box lid.");
    }

    [Fact]
    public void Time_rolls_over_the_day()
    {
        var g = NewGame();
        Walk.Near(g, "battery");
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
        // Dormant faults are inert successors: nothing the player does finds one before its predecessor escalates.
        foreach (var fault in reference.AllFaults.Where(f => !f.Dormant))
        {
            var reveal = fault.Reveals.First(r => r.Discovers);
            var g = WithButton(new Game(BoatLoader.LoadSloop28()));
            var comp = reference.Get(fault.ComponentId).Name;

            if (reveal.Action == PlayerAction.CheckRawWaterFlow)
            {
                Do(g, "negative terminal", n => new Clean(n));
                Assert.True(Start(g).EngineRunning);
            }
            Walk.Near(g, comp);
            if (reveal.NeedsUncovered) Assert.True(g.Execute(new Uncover(comp)).Accepted);
            Command cmd = reveal.Action switch
            {
                PlayerAction.Look => new Look(),
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
            if (cmd is CheckRawWaterFlow or StartEngine) Walk.ToPlace(g, "cockpit");
            var res = g.Execute(cmd);
            Assert.True(res.Accepted, $"{fault.Id}: {string.Join(" ", res.Narrative)}");
            Assert.Contains(Status(g).DiscoveredFaults, k => k.Id == fault.Id && k.Name == fault.Name);
        }
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);

    [Fact]
    public void A_plain_inspect_of_the_negative_terminal_gives_no_hint_until_the_boot_is_pulled_back()
    {
        var g = NewGame();
        Walk.Near(g, "negative terminal");
        var plain = g.Execute(new Inspect("negative terminal"));
        Assert.True(plain.Accepted);
        Assert.Empty(plain.NewlyDiscovered);
        Assert.Empty(Status(g).DiscoveredFaults);
        var text = Text(plain);
        Assert.Equal("A lead clamp under a black rubber boot, greasy from some earlier owner's smear of vaseline. Worn but serviceable.", text);
        foreach (var w in new[] { "green", "bloom", "crust" }) Assert.DoesNotContain(w, text, StringComparison.OrdinalIgnoreCase);

        var pull = g.Execute(new Uncover("negative terminal"));
        Assert.True(pull.Accepted);
        Assert.Equal(2, pull.MinutesTaken);
        Assert.Empty(pull.NewlyDiscovered);
        Assert.StartsWith("You peel the rubber boot back off the clamp.", Text(pull));

        var look = g.Execute(new Inspect("negative terminal"));
        Assert.Contains("crusted green-white", Text(look));
        Assert.Contains(look.NewlyDiscovered, k => k.Name == "Corroded negative terminal");
        Assert.Contains(((LogView)g.Ask(new Logbook())).Entries, e => e.Text == "Pulled back the rubber boot on the negative terminal.");
    }

    [Fact]
    public void Measure_still_discovers_the_fault_with_the_boot_on()
    {
        var g = WithButton(NewGame());
        var m = Do(g, "negative terminal", n => new Measure(n));
        Assert.Contains(m.NewlyDiscovered, k => k.Name == "Corroded negative terminal");
    }

    [Fact]
    public void The_positive_terminal_uncovers_to_clean_metal_and_nothing_is_found()
    {
        var g = NewGame();
        Walk.Near(g, "positive terminal");
        Assert.DoesNotContain("bright", Text(g.Execute(new Inspect("positive terminal"))));
        var pull = g.Execute(new Uncover("positive terminal"));
        Assert.True(pull.Accepted);
        Assert.Contains("clean and bright", Text(pull));
        var look = g.Execute(new Inspect("positive terminal"));
        Assert.Contains("clean and bright", Text(look));
        Assert.Empty(look.NewlyDiscovered);
        Assert.Empty(Status(g).DiscoveredFaults);
    }

    [Fact]
    public void Uncover_refusals_are_free()
    {
        var g = NewGame();
        Walk.Near(g, "negative terminal");
        Assert.Equal("There's nothing to pull back on the start battery.", AssertRefusedForFree(g, new Uncover("start battery")));
        Assert.True(g.Execute(new Uncover("negative terminal")).Accepted);
        Assert.Equal("The rubber boot is already pulled back.", AssertRefusedForFree(g, new Uncover("negative terminal")));
        Walk.ToPlace(g, "companionway");
        Assert.Equal(AssertRefusedForFree(g, new Clean("negative terminal")), AssertRefusedForFree(g, new Uncover("negative terminal")));
        Assert.Contains("haven't found", AssertRefusedForFree(g, new Uncover("flux capacitor")));
    }

    [Fact]
    public void Cleaning_or_replacing_refits_the_boot()
    {
        var g = NewGame();
        Walk.Near(g, "negative terminal");
        g.Execute(new Uncover("negative terminal"));
        var clean = g.Execute(new Clean("negative terminal"));
        Assert.EndsWith("You push the rubber boot back over the clamp.", Text(clean));
        Assert.True(g.Execute(new Uncover("negative terminal")).Accepted);

        g.Execute(new Replace("negative terminal"));
        Assert.True(g.Execute(new Uncover("negative terminal")).Accepted); // boot was on again
    }

    [Fact]
    public void Uncover_is_offered_for_the_terminals_and_the_pump_cover_only()
    {
        var g = NewGame();
        Walk.Near(g, "negative terminal");
        g.Execute(new Look());
        Walk.Near(g, "impeller");
        g.Execute(new Look());
        var withPart = new List<string>();
        foreach (var f in ((AboardView)g.Ask(new Aboard())).Components)
            if (((ComponentActionsView)g.Ask(new ComponentActions(f.Name))).Options.Any(o => o.Verb == "uncover")) withPart.Add(f.Name);
        Assert.Equal(["Negative terminal", "Positive terminal", "Raw-water impeller"], withPart.Order().ToList());
    }

    private static List<string> PlayThrough(Game g)
    {
        var json = new List<string>();
        var opts = new JsonSerializerOptions();
        void Cmd(Command c) { var r = g.Execute(c); json.Add(JsonSerializer.Serialize(r, opts)); }
        void Ask(Query q) { var r = g.Ask(q); json.Add(JsonSerializer.Serialize(r, typeof(QueryResult), opts)); }

        Ask(new Briefing()); Ask(new Commands()); Ask(new Aboard());
        Cmd(new Look()); Cmd(new GoTo("companionway")); Cmd(new GoTo("quarter berth"));
        Cmd(new Remove("battery-box lid")); Cmd(new Inspect("negative terminal"));
        Cmd(new Uncover("positive terminal"));
        Cmd(new GoTo("forepeak")); // rejected
        Cmd(new GoTo("companionway")); Cmd(new GoTo("cockpit"));
        Cmd(new StartEngine()); Ask(new Status());
        Cmd(new GoTo("companionway")); Cmd(new GoTo("quarter berth"));
        Cmd(new Measure("start battery"));
        Cmd(new Buy("remote start button")); Cmd(new Measure("start battery"));
        Cmd(new Measure("filter")); // rejected
        Ask(new Aboard());
        Ask(new Logbook());
        return json;
    }

    [Fact]
    public void Results_hide_internal_state_and_carry_discriminators()
    {
        var g = NewGame();
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
        Assert.Contains(json, j => j.Contains("\"type\":\"place-view\""));
        Assert.Contains(json, j => j.Contains("\"type\":\"cover-view\""));

        // After discovery the name appears, and the rest of the rules still hold.
        var m = g.Execute(new Measure("negative terminal"));
        var after = JsonSerializer.Serialize(m);
        Assert.Contains("Corroded negative terminal", after);
        Assert.DoesNotContain("seriesOhms", after, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ambiguous_and_unknown_names_are_rejected_for_free()
    {
        var g = NewGame();
        Walk.Near(g, "primary filter");
        Walk.Near(g, "oil filter");
        var amb = AssertRefusedForFree(g, new Inspect("filter"));
        Assert.Contains("Primary fuel filter", amb);
        Assert.Contains("Oil filter", amb);

        var unk = AssertRefusedForFree(g, new Measure("the flux capacitor"));
        Assert.Contains("You haven't found anything called 'the flux capacitor' yet.", unk);

        AssertRefusedForFree(g, new GoTo("nowhere"));
        Walk.Near(g, "battery");
        AssertRefusedForFree(g, new Clean("start battery"));
        AssertRefusedForFree(g, new Charge("negative terminal"));
        Walk.Near(g, "hull");
        AssertRefusedForFree(g, new Replace("hull"));
        Assert.Equal(0, Status(g).MoneySpentNok);
    }

    [Fact]
    public void Unseen_components_cannot_be_named_until_found()
    {
        var g = NewGame();
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
        // Nothing is nameable before the first command, not even what is in plain view.
        Assert.False(g.Execute(new Inspect("ignition panel")).Accepted);

        // Look shows what is in view from here.
        Assert.True(g.Execute(new Look()).Accepted);
        Assert.True(g.Execute(new Inspect("ignition panel")).Accepted);
        Assert.True(g.Execute(new Inspect("standing rig")).Accepted);
        Assert.False(g.Execute(new Inspect("negative terminal")).Accepted);

        // Standing in the quarter berth does not help while the lid is on; taking it off does.
        Walk.ToPlace(g, "quarter-berth");
        Assert.False(g.Execute(new Inspect("negative terminal")).Accepted);
        Assert.True(g.Execute(new Remove("battery lid")).Accepted);
        Assert.True(g.Execute(new Inspect("negative terminal")).Accepted);
        WithButton(g);
        Assert.True(g.Execute(new Measure("negative terminal")).Accepted);
    }

    [Fact]
    public void Name_resolution_ignores_case_whitespace_and_leading_the()
    {
        var g = NewGame();
        Walk.Near(g, "negative terminal");
        Walk.Near(g, "positive terminal");
        Assert.True(g.Execute(new Inspect("  The NEGATIVE Terminal ")).Accepted);
        Assert.False(g.Execute(new Inspect("terminal")).Accepted); // two terminals
        Assert.True(g.Execute(new Inspect("negative term")).Accepted);
        Assert.True(g.Execute(new GoTo("  The COMPANIONWAY ")).Accepted);
    }

    [Fact]
    public void Same_commands_give_identical_results()
    {
        var a = PlayThrough(NewGame());
        var b = PlayThrough(NewGame());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Without_the_button_cranking_measurements_are_refused_for_free()
    {
        var g = NewGame();
        foreach (var name in new[] { "negative terminal", "starter motor" })
        {
            Walk.Near(g, name);
            var text = AssertRefusedForFree(g, new Measure(name));
            Assert.Contains("You can't crank and hold the probes at once. A remote start button from the chandlery would let you.", text);
        }

        // The battery gives resting volts only, and still reveals the flat battery.
        var b = Do(g, "start battery", n => new Measure(n));
        Assert.True(b.Accepted);
        var m = Assert.IsType<MeterReading>(b.Observation);
        Assert.Equal("volts-resting", m.Kind);
        Assert.Null(m.CrankingVolts);
        Assert.Contains("remote start button", string.Join(" ", b.Narrative));
        Assert.NotEmpty(b.NewlyDiscovered);

        // Glow plugs need no cranking.
        Assert.True(Do(g, "glow plugs", n => new Measure(n)).Accepted);

        int money = Status(g).MoneySpentNok;
        var buy = g.Execute(new Buy("Remote Start"));
        Assert.True(buy.Accepted);
        Assert.Equal(400, buy.CostNok);
        Assert.Equal(30, buy.MinutesTaken);
        Assert.Equal(money + 400, Status(g).MoneySpentNok);
        Assert.Contains(((LogView)g.Ask(new Logbook())).Entries, e => e.Text == "Bought a remote start button.");

        var n = Do(g, "negative terminal", x => new Measure(x));
        Assert.True(n.Accepted);
        Assert.Equal("volts-drop", Assert.IsType<MeterReading>(n.Observation).Kind);
        Assert.NotNull(Assert.IsType<MeterReading>(Do(g, "start battery", x => new Measure(x)).Observation).CrankingVolts);
    }

    [Fact]
    public void Buying_twice_or_an_unknown_item_is_rejected_for_free()
    {
        var g = NewGame();
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
        var g = NewGame();
        Assert.True(Do(g, "negative terminal", n => new Clean(n)).Accepted);
        Assert.Contains(Status(g).DiscoveredFaults, k => k.Name == "Corroded negative terminal");
    }

    [Fact]
    public void Low_battery_cranks_laboured_then_runs()
    {
        var g = NewGame();
        Assert.True(Do(g, "negative terminal", n => new Clean(n)).Accepted);
        var r = Start(g);
        var view = Assert.IsType<StartAttemptView>(r.Observation);
        Assert.Equal("Runs", view.Outcome);
        Assert.True(r.Finished);
        Assert.True(view.LabouredCrank);
        Assert.Contains("laboured", view.Description);

        var g2 = NewGame();
        Assert.True(Do(g2, "battery", n => new Charge(n)).Accepted);
        Assert.True(Do(g2, "negative terminal", n => new Clean(n)).Accepted);
        var r2 = Start(g2);
        var v2 = Assert.IsType<StartAttemptView>(r2.Observation);
        Assert.Equal("Runs", v2.Outcome);
        Assert.False(v2.LabouredCrank);
        Assert.DoesNotContain("laboured", v2.Description);
    }

    [Fact]
    public void Commands_query_lists_the_verbs()
    {
        var list = Assert.IsType<CommandList>(NewGame().Ask(new Commands()));
        Assert.Contains(list.Commands, c => c.Verb == "inspect" && c.Argument == "component");
        Assert.Contains(list.Commands, c => c.Verb == "buy" && c.Argument == "item");
        Assert.Contains(list.Commands, c => c.Verb == "go" && c.Argument == "place");
        Assert.Contains(list.Commands, c => c.Verb == "remove" && c.Argument == "cover");
        Assert.Contains(list.Commands, c => c.Verb == "refit" && c.Argument == "cover");
        Assert.DoesNotContain(list.Commands, c => c.Verb == "open");
    }

    // ------------------------------------------------------------ places and reach

    [Fact]
    public void Movement_is_limited_to_adjacent_places()
    {
        var g = NewGame();
        Assert.Equal("Cockpit", Status(g).Place);

        var text = AssertRefusedForFree(g, new GoTo("forepeak"));
        Assert.StartsWith("You can't get to the forepeak from here.", text);
        Assert.Contains("From here you can go into the companionway or up on deck.", text);
        Assert.Equal("Cockpit", Status(g).Place);

        Assert.Equal("You're already in the cockpit.", AssertRefusedForFree(g, new GoTo("cockpit")));
        Assert.Equal("There's no place aboard called 'the moon'.", AssertRefusedForFree(g, new GoTo("the moon")));
        AssertRefusedForFree(g, new GoTo("beside the hull")); // two moves away

        var r = g.Execute(new GoTo("companionway"));
        Assert.True(r.Accepted);
        Assert.Equal(1, r.MinutesTaken);
        Assert.Equal("Companionway", Status(g).Place);
        var view = Assert.IsType<PlaceView>(r.Observation);
        Assert.Equal("Companionway", view.Place);
        Assert.Equal(["Cockpit", "Saloon", "Quarter berth"], view.Exits);
        Assert.Contains(((LogView)g.Ask(new Logbook())).Entries, e => e.Text == "Went into the companionway.");

        // Aliases and the ladder.
        Assert.True(g.Execute(new GoTo("cabin")).Accepted);
        Assert.True(g.Execute(new GoTo("bow")).Accepted);
        Walk.ToPlace(g, "beside-the-hull");
        Assert.Equal("Beside the hull", Status(g).Place);
    }

    [Fact]
    public void The_starter_and_solenoid_cannot_be_seen_from_the_companionway()
    {
        var g = NewGame();
        Assert.True(g.Execute(new GoTo("companionway")).Accepted);
        Assert.True(g.Execute(new Remove("steps")).Accepted);
        var aboard = Assert.IsType<AboardView>(g.Ask(new Aboard()));
        // Both sit on the flywheel housing at the aft end: out of sight from the front (Amit, 2026-11-07).
        Assert.DoesNotContain(aboard.Components, c => c.Name is "Starter motor" or "Starter solenoid");
        Assert.Contains("You haven't found anything called 'starter motor' yet.",
            AssertRefusedForFree(g, new Inspect("starter motor")));

        // Once found from the quarter berth, they are still out of sight from the companionway.
        Walk.Uncover(g, "quarter-berth-panel");
        Walk.ToPlace(g, "companionway");
        Assert.Equal("You can't see the starter solenoid from here.", AssertRefusedForFree(g, new Inspect("solenoid")));
        Assert.Equal("You can't see the starter motor from here.", AssertRefusedForFree(g, new Replace("starter motor")));
        Walk.ToPlace(g, "quarter-berth");
        g.Execute(new Buy("remote start button"));
        Assert.True(g.Execute(new Measure("starter motor")).Accepted);
        Assert.True(g.Execute(new Measure("solenoid")).Accepted);
    }

    [Fact]
    public void The_heat_exchanger_shows_from_the_quarter_berth_but_is_reached_from_the_front()
    {
        var g = NewGame();
        Walk.ToPlace(g, "quarter-berth");
        Walk.Uncover(g, "quarter-berth-panel");
        var hx = Assert.Single(Assert.IsType<AboardView>(g.Ask(new Aboard())).Components, c => c.Name == "Heat exchanger");
        Assert.True(hx.VisibleHere);
        Assert.False(hx.ReachableHere);
        Assert.True(g.Execute(new Inspect("heat exchanger")).Accepted);
        Assert.Equal(
            "You can see the heat exchanger from here but can't get at it; the companionway steps are in the way.",
            AssertRefusedForFree(g, new Replace("heat exchanger")));

        Walk.ToPlace(g, "companionway");
        Walk.Uncover(g, "companionway-steps");
        Walk.ToPlace(g, "quarter-berth");
        Assert.Equal(
            "You can see the heat exchanger from here but can't get at it; you'd have to get at it from the companionway.",
            AssertRefusedForFree(g, new Replace("heat exchanger")));
    }

    [Fact]
    public void The_negative_terminal_needs_the_battery_box_lid_off()
    {
        var g = WithButton(NewGame());
        Walk.ToPlace(g, "quarter-berth");
        // Not even nameable: she has not seen it.
        foreach (Command c in new Command[]
                 { new Inspect("negative terminal"), new Measure("negative terminal"), new Clean("negative terminal"), new Replace("negative terminal") })
            Assert.Contains("You haven't found anything called 'negative terminal' yet.", AssertRefusedForFree(g, c));

        Assert.True(g.Execute(new Remove("battery-box lid")).Accepted);
        Assert.True(g.Execute(new Measure("negative terminal")).Accepted);

        // Lid back on: she knows the part but cannot see it.
        Assert.True(g.Execute(new Refit("battery-box lid")).Accepted);
        const string blind = "You can't see the negative terminal from here.";
        Assert.Equal(blind, AssertRefusedForFree(g, new Measure("negative terminal")));
        Assert.Equal(blind, AssertRefusedForFree(g, new Clean("negative terminal")));
        Assert.Equal(blind, AssertRefusedForFree(g, new Inspect("negative terminal")));
    }

    [Fact]
    public void Covers_come_off_and_go_back_only_where_they_are()
    {
        var g = NewGame();
        Assert.Equal("The battery-box lid is in the quarter berth.", AssertRefusedForFree(g, new Remove("battery-box lid")));
        Assert.Equal("There's nothing called 'hatch cover' to take off.", AssertRefusedForFree(g, new Remove("hatch cover")));
        Assert.Contains("could be:", AssertRefusedForFree(g, new Remove("lid")));
        Assert.Equal("The cockpit-locker lid is already in place.", AssertRefusedForFree(g, new Refit("locker lid")));

        Walk.ToPlace(g, "companionway");
        int before = Status(g).MinutesSpent;
        var off = g.Execute(new Remove("steps"));
        Assert.True(off.Accepted);
        Assert.Equal(3, off.MinutesTaken); // companionway steps: 3 min in the data
        Assert.Equal(before + 3, Status(g).MinutesSpent);
        var view = Assert.IsType<CoverView>(off.Observation);
        Assert.True(view.Removed);
        Assert.Equal("Companionway steps", view.Cover);
        Assert.Equal("Companionway", view.Place);
        Assert.Contains("Raw-water pump", view.NowVisible, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(off.Narrative, l => l.StartsWith("Behind it:"));
        Assert.Contains(((LogView)g.Ask(new Logbook())).Entries, e => e.Text == "Took off the companionway steps.");

        Assert.Equal("The companionway steps are already off.", AssertRefusedForFree(g, new Remove("steps")));

        // Look reports them off.
        Assert.Contains("The companionway steps are off.", g.Execute(new Look()).Narrative);

        Walk.ToPlace(g, "cockpit");
        Assert.Equal("The companionway steps are in the companionway.", AssertRefusedForFree(g, new Refit("steps")));
        Walk.ToPlace(g, "companionway");
        var on = g.Execute(new Refit("steps"));
        Assert.True(on.Accepted);
        Assert.Equal(3, on.MinutesTaken);
        Assert.Empty(Assert.IsType<CoverView>(on.Observation).NowVisible);
        Assert.Contains(((LogView)g.Ask(new Logbook())).Entries, e => e.Text == "Put back the companionway steps.");
        Assert.Contains("The companionway steps are in place.", g.Execute(new Look()).Narrative);
        // What she saw stays known, but is out of sight again.
        Assert.Equal("You can't see the raw-water pump from here.", AssertRefusedForFree(g, new Inspect("raw-water pump")));

        // Times come from the data.
        Walk.ToPlace(g, "quarter-berth");
        Assert.Equal(5, g.Execute(new Remove("panel")).MinutesTaken);
        Assert.Equal(2, g.Execute(new Remove("battery lid")).MinutesTaken);
        Assert.Equal(5, g.Execute(new Refit("panel")).MinutesTaken);
    }

    [Fact]
    public void The_engine_starts_only_from_the_cockpit()
    {
        var g = NewGame();
        Walk.ToPlace(g, "companionway");
        Assert.Equal("The key is on the panel in the cockpit.", AssertRefusedForFree(g, new StartEngine()));
        Walk.ToPlace(g, "on-deck");
        Assert.Equal("The key is on the panel in the cockpit.", AssertRefusedForFree(g, new StartEngine()));
        Assert.Null(Status(g).LastStartOutcome);
        Walk.ToPlace(g, "cockpit");
        Assert.True(g.Execute(new StartEngine()).Accepted);
    }

    [Fact]
    public void The_cooling_water_is_checked_from_outside_only()
    {
        var g = NewGame();
        Do(g, "negative terminal", n => new Clean(n));
        Assert.True(Start(g).EngineRunning);

        const string nothing = "You can't see the exhaust outlet from down here.";
        Walk.ToPlace(g, "companionway");
        Assert.Equal(nothing, AssertRefusedForFree(g, new CheckRawWaterFlow()));
        Walk.ToPlace(g, "saloon");
        Assert.Equal(nothing, AssertRefusedForFree(g, new CheckRawWaterFlow()));

        foreach (var place in new[] { "cockpit", "on-deck", "beside-the-hull" })
        {
            Walk.ToPlace(g, place);
            Assert.True(g.Execute(new CheckRawWaterFlow()).Accepted, place);
        }

        // The same rule when the engine is not running.
        var idle = NewGame();
        Walk.ToPlace(idle, "companionway");
        Assert.Equal(nothing, AssertRefusedForFree(idle, new CheckRawWaterFlow()));
    }

    [Fact]
    public void Looking_names_the_place_covers_and_what_is_in_view()
    {
        var g = NewGame();
        var look = g.Execute(new Look());
        Assert.True(look.Accepted);
        Assert.Equal(2, look.MinutesTaken);
        Assert.Contains("The cockpit-locker lid is in place.", look.Narrative);
        Assert.Contains(look.Narrative, l => l.StartsWith("You can see:") && l.Contains("Ignition panel"));
        Assert.DoesNotContain(look.Narrative, l => l.Contains("Fuel tank"));
        Assert.Contains("From here you can go into the companionway or up on deck.", look.Narrative);
        var view = Assert.IsType<PlaceView>(look.Observation);
        Assert.Contains("Ignition panel", view.Reachable);
        Assert.Contains("Standing rig", view.Visible);
        Assert.DoesNotContain("Standing rig", view.Reachable);
        Assert.Contains(((LogView)g.Ask(new Logbook())).Entries, e => e.Text == "Looked around the cockpit.");

        g.Execute(new Remove("locker lid"));
        Assert.Contains("Fuel tank", Assert.IsType<PlaceView>(g.Execute(new Look()).Observation).Reachable);
    }
}
