using System.Text.Json;
using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The pre-launch slice, played only through the facade: lifecycle, escalation, the survey, money, DIY quality, launch.</summary>
public class PreLaunchTests
{
    // ------------------------------------------------------------------ helpers

    /// <summary>A random that always returns the same number.</summary>
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static readonly Boat Data = BoatLoader.LoadSloop28();
    private const string WeepingSeacock = "Weeping raw-water seacock";
    private const string SeizedSeacock = "Seized raw-water seacock";

    private static string FaultName(string id) => Data.AllFaults.Single(f => f.Id == id).Name;
    private static Fault DataFault(string id) => Data.AllFaults.Single(f => f.Id == id);
    private static Component DataComponent(string id) => Data.Get(id);

    /// <summary>A fresh game whose poor-job roll never comes up, unless a test says otherwise.</summary>
    private static Game NewGame(IRandom? random = null) => new(BoatLoader.LoadSloop28(), 75, random ?? new FixedRandom(0.99), weather: WeatherSeries.Fair());

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, Text(r));
        return r;
    }

    private static CommandResult Refused(CommandResult r)
    {
        Assert.False(r.Accepted, "Expected a refusal but it was accepted: " + Text(r));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static ReadinessView Ready(Game g) => (ReadinessView)g.Ask(new Readiness());
    private static AboardView Aboard(Game g) => (AboardView)g.Ask(new Aboard());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static IEnumerable<string> OpenNames(Game g) => Ready(g).Open.Select(o => o.Problem);

    private static void Survey(Game g) => Ok(g.Execute(new OrderSurvey()));

    private static void Inspect(Game g, string name) => Ok(Walk.Do(g, name, n => new Inspect(n)));

    /// <summary>Take the lid off, clean the terminal (the first fault), then start from the cockpit.</summary>
    private static void FixTerminalAndStart(Game g)
    {
        Ok(Walk.Do(g, "negative terminal", n => new Clean(n)));
        Assert.True(Ok(Walk.Start(g)).EngineRunning);
    }

    private static string Json(CommandResult r) => JsonSerializer.Serialize(r);
    private static string Json(QueryResult q) => JsonSerializer.Serialize(q, typeof(QueryResult));

    // ------------------------------------------------------------------ 1. lifecycle

    [Fact]
    public void Survey_investigate_item_only_flags_the_part_then_inspecting_discovers_the_fault()
    {
        var g = NewGame();
        Survey(g);
        var corroded = FaultName("neg-terminal-corrosion");

        Assert.Contains(Aboard(g).Components, c => c.Name == "Starter motor" && c.Flagged);
        Assert.DoesNotContain(Status(g).DiscoveredFaults, k => k.Name == corroded);
        Assert.DoesNotContain(corroded, OpenNames(g));
        Assert.Contains(Ready(g).ToInvestigate, i => i.Component == "Starter motor");

        Ok(Walk.Do(g, "negative terminal", n => new Uncover(n)));
        var inspected = Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Contains(inspected.NewlyDiscovered, k => k.Name == corroded);

        Assert.DoesNotContain(Aboard(g).Components, c => c.Name == "Starter motor" && c.Flagged);
        Assert.DoesNotContain(Ready(g).ToInvestigate, i => i.Component == "Starter motor");
        var item = Ready(g).Open.Single(o => o.Problem == corroded);
        Assert.Equal("Discovered", item.State);
    }

    [Fact]
    public void Seacock_damp_patch_is_only_a_hint_until_it_is_inspected()
    {
        var g = NewGame();
        Walk.ToPlace(g, "saloon");
        var floorboards = Ok(g.Execute(new Remove("saloon floorboards")));
        Assert.Contains("damp patch", Text(floorboards));
        Assert.Empty(Status(g).DiscoveredFaults);
        Assert.Empty(Ready(g).Open);
        Refused(g.Execute(new Defer("seacock")));

        var inspected = Ok(g.Execute(new Inspect("seacock")));
        Assert.Contains(inspected.NewlyDiscovered, k => k.Name == WeepingSeacock);
        Assert.Equal("Discovered", Ready(g).Open.Single().State);
    }

    [Fact]
    public void Weak_exhaust_flow_is_only_a_hint_for_the_tired_impeller()
    {
        var g = NewGame();
        FixTerminalAndStart(g);
        Walk.ToPlace(g, "cockpit");
        var flow = Ok(g.Execute(new CheckRawWaterFlow()));
        Assert.Contains("weaker", Text(flow));
        Assert.Empty(flow.NewlyDiscovered);
        Assert.DoesNotContain(FaultName("impeller-tired"), OpenNames(g));
        Refused(g.Execute(new Defer("impeller")));

        Ok(Walk.Do(g, "impeller", n => new Uncover(n)));
        var inspected = Ok(g.Execute(new Inspect("impeller")));
        Assert.Contains(inspected.NewlyDiscovered, k => k.Name == "Tired impeller");
    }

    [Fact]
    public void Defer_and_undefer_move_a_discovered_fault_back_and_forth()
    {
        var g = NewGame();
        Survey(g);
        var before = Status(g).MinutesSpent;

        var deferred = Ok(g.Execute(new Defer("seacock")));
        Assert.Equal(0, deferred.MinutesTaken);
        Assert.Equal(0, deferred.CostNok);
        Assert.Equal("Deferred", Ready(g).Open.Single(o => o.Problem == WeepingSeacock).State);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Deferred: {WeepingSeacock}.");
        Refused(g.Execute(new Defer("seacock")));

        Ok(g.Execute(new Undefer("seacock")));
        Assert.Equal("Discovered", Ready(g).Open.Single(o => o.Problem == WeepingSeacock).State);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Back on the list: {WeepingSeacock}.");
        Refused(g.Execute(new Undefer("seacock")));
        Assert.Equal(before, Status(g).MinutesSpent);
    }

    [Fact]
    public void Deferred_fault_can_be_fixed_by_hand_and_stays_in_the_history()
    {
        var g = NewGame();
        Survey(g);
        Ok(g.Execute(new Defer("seacock")));
        Ok(Walk.Do(g, "seacock", n => new Replace(n)));

        AssertFixedAndRemembered(g, WeepingSeacock, "Replaced the raw-water seacock.");
    }

    [Fact]
    public void Yard_fix_also_leaves_the_fault_in_the_history()
    {
        var g = NewGame();
        Survey(g);
        var yard = DataFault("nav-lens-cracked").Yard!;
        var day = Status(g).Day;

        var fix = Ok(g.Execute(new FixByYard("lens")));
        Assert.Equal(yard.Nok, fix.CostNok);
        Assert.Equal(yard.LeadDays * 1440, fix.MinutesTaken);
        Assert.Equal(day + yard.LeadDays, Status(g).Day);
        AssertFixedAndRemembered(g, FaultName("nav-lens-cracked"), $"The yard fixed: {FaultName("nav-lens-cracked")}.");
    }

    private static void AssertFixedAndRemembered(Game g, string faultName, string fixLogText)
    {
        Assert.DoesNotContain(faultName, OpenNames(g));
        Assert.Contains(faultName, Ready(g).Fixed);
        Assert.Contains(Status(g).DiscoveredFaults, k => k.Name == faultName);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Found: {faultName}.");
        Assert.Contains(Log(g).Entries, e => e.Text == fixLogText);
        Refused(g.Execute(new Defer(faultName)));
    }

    [Fact]
    public void Deferring_a_fault_the_player_does_not_know_gets_the_same_answer_as_a_made_up_name()
    {
        var g = NewGame();
        var hidden = Refused(g.Execute(new Defer("seacock")));
        var madeUp = Refused(g.Execute(new Defer("flux capacitor")));

        Assert.Equal("You don't know of a problem called 'seacock'.", Text(hidden));
        Assert.Equal("You don't know of a problem called 'flux capacitor'.", Text(madeUp));
        Assert.Empty(Log(g).Entries);
        Assert.Equal(0, Status(g).MinutesSpent);
    }

    // ------------------------------------------------------------------ 2. escalation

    [Fact]
    public void Deferred_seacock_seizes_within_its_range_and_the_weeping_one_leaves_the_list()
    {
        var g = NewGame();
        Survey(g);
        Ok(g.Execute(new Defer("seacock")));
        var range = Ready(g).Open.Single(o => o.Problem == WeepingSeacock);
        int from = Status(g).Day;

        var late = WaitUntilSeized(g); // every earlier day found nothing, or WaitUntilSeized would have returned
        Assert.InRange(Status(g).Day, from + range.LikelyMinDays!.Value, from + range.LikelyMaxDays!.Value + 2);
        Assert.DoesNotContain("seized", Text(late)); // she was left: the wait tells nothing
        Assert.Contains(WeepingSeacock, OpenNames(g)); // the player still believes what they knew
        Assert.DoesNotContain(SeizedSeacock, OpenNames(g));

        Assert.Contains("seized solid", Text(Ok(Walk.Do(g, "seacock", n => new Inspect(n)))));
        Assert.DoesNotContain(WeepingSeacock, OpenNames(g));
        Assert.DoesNotContain(WeepingSeacock, Ready(g).Fixed);
        Assert.Contains(SeizedSeacock, OpenNames(g));
        Assert.Contains(Log(g).Entries, e => e.Text == $"Found: {SeizedSeacock}.");
    }

    [Fact]
    public void After_escalation_only_the_seized_seacock_is_known_and_it_is_not_deferred()
    {
        var g = NewGame();
        Survey(g);
        Ok(g.Execute(new Defer("seacock")));
        WaitUntilSeized(g);
        Inspect(g, "seacock"); // the finding

        var known = Status(g).DiscoveredFaults.Select(k => k.Name).ToList();
        Assert.Contains(SeizedSeacock, known);
        Assert.DoesNotContain(WeepingSeacock, known);
        var open = Ready(g).Open.Where(o => o.Problem.Contains("seacock", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(SeizedSeacock, Assert.Single(open).Problem);
        Assert.Equal("Discovered", open[0].State);
        Assert.DoesNotContain(WeepingSeacock, Ready(g).Fixed);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Found: {WeepingSeacock}.");

        Ok(g.Execute(new Defer("seacock")));
        Assert.Equal("Deferred", Ready(g).Open.Single(o => o.Problem == SeizedSeacock).State);
    }

    [Fact]
    public void Launch_counts_the_seized_seacock_as_open_until_the_player_defers_it_again()
    {
        var g = NewGame();
        FindSeacock(g);
        Ok(g.Execute(new Defer("seacock")));
        WaitUntilSeized(g);
        Inspect(g, "seacock"); // found seized, and not deferred again
        var launch = Ok(g.Execute(new Launch()));
        var view = Assert.IsType<LaunchView>(launch.Observation);
        Assert.Equal(0, view.DeferredCount);
        Assert.Equal(1, view.OpenCount);
        Assert.Contains("with 0 deferred items, and 1 other known problem still open.", Text(launch));

        var g2 = NewGame();
        FindSeacock(g2);
        Ok(g2.Execute(new Defer("seacock")));
        WaitUntilSeized(g2);
        Inspect(g2, "seacock");
        Ok(g2.Execute(new Defer("seacock")));
        var view2 = Assert.IsType<LaunchView>(Ok(g2.Execute(new Launch())).Observation);
        Assert.Equal(1, view2.DeferredCount);
        Assert.Equal(1, view2.OpenCount);
    }

    /// <summary>
    /// Wait one day at a time until the weeping seacock has seized (read from the sim's internals: the player learns nothing from a wait);
    /// returns the wait that did it. The spread moves the day, so no fixed day is assumed.
    /// </summary>
    private static CommandResult WaitUntilSeized(Game g, int maxDays = 45)
    {
        for (int i = 0; i < maxDays; i++)
        {
            var r = Ok(g.Execute(new WaitDays(1)));
            if (g.HasEscalated("seacock-weeping")) return r;
        }
        Assert.Fail($"The seacock had not seized after {maxDays} days.");
        return null!;
    }

    private static void FindSeacock(Game g)
    {
        Walk.ToPlace(g, "saloon");
        Ok(g.Execute(new Remove("saloon floorboards")));
        Ok(g.Execute(new Inspect("seacock")));
    }

    [Fact]
    public void Waiting_moves_the_calendar_but_not_the_work_time()
    {
        var g = NewGame();
        int day = Status(g).Day;
        int worked = Status(g).MinutesSpent;
        var wait = Ok(g.Execute(new WaitDays(20)));
        Assert.Equal(day + 20, Status(g).Day);
        Assert.Equal(worked, Status(g).MinutesSpent);
        Assert.Equal(20 * 1440, wait.MinutesTaken);
    }

    [Fact]
    public void A_discovered_but_undeferred_seacock_escalates_by_waiting_inside_the_range_readiness_gave()
    {
        var g = NewGame();
        FindSeacock(g);
        var item = Ready(g).Open.Single(o => o.Problem == WeepingSeacock);
        Assert.Equal("Discovered", item.State);
        int from = Status(g).Day;

        // Keep the player aboard every day so the factors stay constant while the range is tested.
        CommandResult? late = null;
        for (int i = 0; i < 45 && late is null; i++)
        {
            Ok(g.Execute(new Look()));
            var r = Ok(g.Execute(new WaitDays(1)));
            if (g.HasEscalated("seacock-weeping")) late = r;
        }
        Assert.NotNull(late);
        Assert.InRange(Status(g).Day, from + item.LikelyMinDays!.Value, from + item.LikelyMaxDays!.Value);
        Assert.DoesNotContain("seized", Text(late!));
        Assert.Contains("seized solid", Text(Ok(Walk.Do(g, "seacock", n => new Inspect(n)))));
        var open = Ready(g).Open.Single(o => o.Problem == SeizedSeacock);
        Assert.Equal("Discovered", open.State);
        Assert.DoesNotContain(WeepingSeacock, OpenNames(g));
    }

    [Fact]
    public void A_latent_seacock_escalates_silently_by_waiting()
    {
        var g = NewGame();
        var wait = Ok(g.Execute(new WaitDays(45))); // long enough for any spread

        Assert.Empty(wait.NewlyDiscovered);
        Assert.DoesNotContain("seacock", Text(wait), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Log(g).Entries.Where(e => e.Text.StartsWith("Found")));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.Contains("came on", StringComparison.Ordinal));
        Assert.Empty(Ready(g).Open);

        var inspected = Ok(Walk.Do(g, "seacock", n => new Inspect(n)));
        Assert.Contains(inspected.NewlyDiscovered, k => k.Name == SeizedSeacock);
        Assert.Contains("seized solid", Text(inspected));
    }

    [Fact]
    public void Readiness_gives_a_likely_range_for_a_known_fault_and_none_for_the_rest()
    {
        var g = NewGame();
        Assert.Empty(Ready(g).Open);

        FindSeacock(g);
        var item = Ready(g).Open.Single(o => o.Problem == WeepingSeacock);
        Assert.True(item.LikelyMinDays < item.LikelyMaxDays);
        Assert.True(item.EscalatesOnLaunch);

        Ok(g.Execute(new WaitDays(5)));
        var later = Ready(g).Open.Single(o => o.Problem == WeepingSeacock);
        Assert.True(later.LikelyMaxDays < item.LikelyMaxDays, "five days of progress shorten the range");

        // A fault with no progression reports nothing.
        Assert.All(Ready(g).Open.Where(o => o.Problem != WeepingSeacock), o => Assert.Null(o.LikelyMaxDays));
    }

    [Fact]
    public void Seized_seacock_with_the_engine_started_overheats_through_the_cooling_warning()
    {
        var g = NewGame();
        Ok(g.Execute(new WaitDays(45))); // seized by now, whatever the spread
        Ok(Walk.Do(g, "negative terminal", n => new Clean(n)));
        var start = Ok(Walk.Start(g));

        Assert.True(start.EngineRunning);
        Assert.Contains("temperature alarm", Text(start));
        Assert.False(start.Finished);
        var flow = Ok(g.Execute(new CheckRawWaterFlow()));
        Assert.Contains("Almost nothing comes out of the exhaust.", Text(flow));
        Assert.Equal("Seized shut: no cooling water, the engine overheats within minutes.", DataFault("seacock-seized").Consequence);
    }

    // ------------------------------------------------------------------ emergency lift

    [Fact]
    public void Emergency_lift_is_refused_ashore()
    {
        var g = NewGame();
        var r = Refused(g.Execute(new EmergencyLift()));
        Assert.Equal("She's already ashore.", Text(r));
        Assert.False(Status(g).Afloat);
    }

    [Fact]
    public void Emergency_lift_afloat_costs_the_fee_and_days_then_the_seacock_can_be_replaced_and_she_relaunches_free()
    {
        var eco = Data.Economy;
        Assert.Equal(2, eco.EmergencyLiftDays);
        var g = NewGame();
        Survey(g);
        Ok(g.Execute(new Launch()));
        Assert.True(Status(g).Afloat);
        Assert.True(Ready(g).Afloat);
        int balance = Status(g).BalanceNok, day = Status(g).Day, worked = Status(g).MinutesSpent;

        var lift = Ok(g.Execute(new EmergencyLift()));
        Assert.Equal(eco.EmergencyLiftNok, lift.CostNok);
        Assert.Equal(eco.EmergencyLiftDays * 1440, lift.MinutesTaken);
        Assert.Equal(balance - eco.EmergencyLiftNok, Status(g).BalanceNok);
        Assert.Equal(day + eco.EmergencyLiftDays, Status(g).Day);
        Assert.Equal(worked, Status(g).MinutesSpent);
        Assert.False(Status(g).Afloat);
        Assert.True(Status(g).Launched);
        Assert.Contains(Log(g).Entries, e => e.Text.StartsWith("Emergency lift") && e.CostNok == eco.EmergencyLiftNok);

        // Below-waterline work and the ground beside the hull are back.
        Walk.ToPlace(g, "beside-the-hull");
        Assert.True(Aboard(g).Places.Single(p => p.Name == "Beside the hull").Available);
        Ok(Walk.Do(g, "seacock", n => new Replace(n)));
        Assert.DoesNotContain(SeizedSeacock, OpenNames(g));

        // Back in the water: the existing Launch, no fee, launch time only.
        var again = Ok(g.Execute(new Launch()));
        Assert.Equal(0, again.CostNok);
        Assert.Equal(eco.LaunchMinutes, again.MinutesTaken);
        Assert.Empty(again.NewlyDiscovered);
        Assert.True(Status(g).Afloat);
        Refused(g.Execute(new Launch()));
    }

    [Fact]
    public void Rusted_drain_clamps_can_be_replaced_afloat()
    {
        var g = NewGame();
        Survey(g);
        Ok(g.Execute(new Launch()));
        var item = Ready(g).Open.Single(o => o.Problem == FaultName("drain-clamps-rusted"));
        Assert.False(item.OnHardOnly);
        Assert.Equal("can be done afloat, with the drain seacock shut", item.LaterNote);
        Assert.All(item.Routes, r => Assert.True(r.Available, r.UnavailableReason));

        Ok(Walk.Do(g, "drain clamps", n => new Replace(n)));
        Assert.DoesNotContain(FaultName("drain-clamps-rusted"), OpenNames(g));
    }

    [Fact]
    public void Launching_with_the_seacock_still_on_the_list_seizes_it()
    {
        var g = NewGame();
        Survey(g);

        var launch = Ok(g.Execute(new Launch()));
        Assert.Contains(launch.NewlyDiscovered, k => k.Name == SeizedSeacock);
        Assert.Contains(SeizedSeacock, OpenNames(g));
        Assert.DoesNotContain(WeepingSeacock, OpenNames(g));
    }

    [Fact]
    public void An_undiscovered_seacock_escalates_silently_and_is_found_later_already_seized()
    {
        var g = NewGame();
        var launch = Ok(g.Execute(new Launch()));

        Assert.Empty(launch.NewlyDiscovered);
        Assert.DoesNotContain("seacock", Text(launch), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seized", Text(launch), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Ready(g).Open);
        Assert.Empty(Ready(g).Fixed);
        Assert.Empty(Log(g).Entries.Where(e => e.Text.StartsWith("Found")));

        var inspected = Ok(Walk.Do(g, "seacock", n => new Inspect(n)));
        Assert.Contains(inspected.NewlyDiscovered, k => k.Name == SeizedSeacock);
        Assert.Contains("seized solid", Text(inspected));
        Assert.DoesNotContain(WeepingSeacock, Status(g).DiscoveredFaults.Select(k => k.Name));
    }

    // ------------------------------------------------------------------ 3. below the waterline afloat

    [Fact]
    public void Afloat_the_seacock_cannot_be_replaced_or_sent_to_the_yard_but_engine_work_goes_on()
    {
        var g = NewGame();
        Survey(g);
        Ok(g.Execute(new Launch()));
        var lift = $"{Data.Economy.EmergencyLiftNok} NOK";
        var balance = Status(g).BalanceNok;

        Walk.Near(g, "seacock");
        var replace = Refused(g.Execute(new Replace("seacock")));
        Assert.Contains("emergency lift", Text(replace));
        Assert.Contains(lift, Text(replace));
        var yard = Refused(g.Execute(new FixByYard("seized")));
        Assert.Contains("emergency lift", Text(yard));
        Assert.Contains(lift, Text(yard));
        Assert.Equal(balance, Status(g).BalanceNok);

        var options = (ComponentActionsView)g.Ask(new ComponentActions("seacock"));
        var replaceOption = options.Options.Single(o => o.Verb == "replace");
        Assert.False(replaceOption.Available);
        Assert.Contains("emergency lift", replaceOption.UnavailableReason);
        Assert.False(options.Options.Single(o => o.Verb == "yard-fix").Available);

        Ok(Walk.Do(g, "negative terminal", n => new Clean(n)));
        Assert.Contains(FaultName("neg-terminal-corrosion"), Status(g).DiscoveredFaults.Select(k => k.Name));
        Refused(g.Execute(new GoTo("Beside the hull")));
        Assert.False(Aboard(g).Places.Single(p => p.Name == "Beside the hull").Available);
    }

    // ------------------------------------------------------------------ 4. survey

    [Fact]
    public void Survey_costs_the_fee_takes_a_day_and_writes_a_twelve_item_report()
    {
        var g = NewGame();
        var before = (SurveyReportView)g.Ask(new SurveyReport());
        Assert.False(before.Ordered);
        Assert.Empty(before.Items);

        var data = Data.Survey!;
        var ordered = Ok(g.Execute(new OrderSurvey()));
        Assert.Equal(data.NokPerFoot * data.LengthFeet, ordered.CostNok);
        Assert.Equal(data.Days * 1440, ordered.MinutesTaken);
        Assert.Equal(Data.Economy.StartingBalanceNok - ordered.CostNok, Status(g).BalanceNok);

        var report = (SurveyReportView)g.Ask(new SurveyReport());
        Assert.True(report.Ordered);
        Assert.Equal(12, report.Items.Count);
        Assert.Equal(Enumerable.Range(1, 12), report.Items.Select(i => i.Number));
        Assert.Equal(3, report.Items.Count(i => i.Severity == "Before launch"));
        Assert.Equal(2, report.Items.Count(i => i.Severity == "Note"));
        Assert.Equal(7, report.Items.Count(i => i.Severity == "Attention"));

        var entry = Log(g).Entries.Single(e => e.Document == "survey report");
        Assert.StartsWith("Survey report received from", entry.Text);
        Refused(g.Execute(new OrderSurvey()));
    }

    [Fact]
    public void Cause_items_discover_their_faults_and_investigate_items_only_point_at_the_part()
    {
        var g = NewGame();
        var ordered = Survey2(g);

        var causes = Data.Survey!.Items.Where(i => i.Finding == SurveyFinding.Cause).Select(i => FaultName(i.Fault!)).ToList();
        Assert.Equal(7, causes.Count);
        Assert.Equal(causes.Order(), ordered.NewlyDiscovered.Select(k => k.Name).Order());
        Assert.Equal(causes.Order(), OpenNames(g).Order());

        var investigate = Data.Survey.Items.Where(i => i.Finding == SurveyFinding.Investigate).ToList();
        Assert.Equal(3, investigate.Count);
        var report = (SurveyReportView)g.Ask(new SurveyReport());
        var flagged = Aboard(g).Components.Where(c => c.Flagged).Select(c => c.Name).Order().ToList();
        Assert.Equal(investigate.Select(i => DataComponent(i.Component).Name).Order(), flagged);
        foreach (var item in investigate)
        {
            Assert.DoesNotContain(FaultName(item.Fault!), OpenNames(g));
            Assert.DoesNotContain(FaultName(item.Fault!), Status(g).DiscoveredFaults.Select(k => k.Name));
            var text = report.Items.Single(r => r.Text == item.Text).Text;
            foreach (var word in new[] { "terminal", "corro", "impeller" })
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static CommandResult Survey2(Game g) => Ok(g.Execute(new OrderSurvey()));

    [Fact]
    public void No_survey_item_names_a_fault_it_does_not_discover()
    {
        var g = NewGame();
        Survey(g);
        var report = (SurveyReportView)g.Ask(new SurveyReport());

        foreach (var (item, data) in report.Items.Zip(Data.Survey!.Items))
            foreach (var fault in Data.AllFaults)
            {
                bool itsOwnCause = data.Finding == SurveyFinding.Cause && data.Fault == fault.Id;
                if (!itsOwnCause)
                    Assert.False(item.Text.Contains(fault.Name, StringComparison.OrdinalIgnoreCase),
                        $"Survey item {item.Number} gives away '{fault.Name}'.");
            }
    }

    [Fact]
    public void Blistered_hull_is_a_red_herring_that_inspection_rules_out()
    {
        var g = NewGame();
        Survey(g);
        var blisters = FaultName("hull-old-paint-blisters");
        Assert.Contains(Aboard(g).Components, c => c.Name == "Hull" && c.Flagged);
        Assert.DoesNotContain(blisters, OpenNames(g));

        var inspected = Ok(Walk.Do(g, "hull", n => new Inspect(n)));

        Assert.Contains($"Ruled out: {blisters}.", Text(inspected));
        Assert.Contains(blisters, Ready(g).Fixed);
        Assert.DoesNotContain(blisters, OpenNames(g));
        Assert.DoesNotContain(Ready(g).ToInvestigate, i => i.Component == "Hull");
        Assert.Contains(Log(g).Entries, e => e.Text == $"Ruled out: {blisters}.");
        Assert.DoesNotContain(Aboard(g).Components, c => c.Name == "Hull" && c.Flagged);
    }

    // ------------------------------------------------------------------ 5. readiness honesty

    private static void AssertReadinessIsHonest(Game g)
    {
        var known = Status(g).DiscoveredFaults.Select(k => k.Name).ToHashSet();
        var ready = Ready(g);
        Assert.All(ready.Open, o => Assert.Contains(o.Problem, known));
        Assert.All(ready.Fixed, name => Assert.Contains(name, known));
        var json = Json(ready);
        foreach (var fault in Data.AllFaults.Where(f => !known.Contains(f.Name)))
            Assert.DoesNotContain(fault.Name, json);
    }

    [Fact]
    public void Readiness_starts_empty_and_never_lists_what_the_player_has_not_found()
    {
        var g = NewGame();
        var start = Ready(g);
        Assert.Empty(start.Open);
        Assert.Empty(start.ToInvestigate);
        Assert.Empty(start.Fixed);
        AssertReadinessIsHonest(g);

        Walk.ToPlace(g, "saloon");
        Ok(g.Execute(new Remove("saloon floorboards")));
        AssertReadinessIsHonest(g);
        Survey(g);
        AssertReadinessIsHonest(g);
        Ok(g.Execute(new Defer("seacock")));
        AssertReadinessIsHonest(g);
        Ok(Walk.Do(g, "hull", n => new Inspect(n)));
        AssertReadinessIsHonest(g);
        FixTerminalAndStart(g);
        AssertReadinessIsHonest(g);
        Ok(g.Execute(new WaitDays(25)));
        AssertReadinessIsHonest(g);
        Ok(g.Execute(new Launch()));
        AssertReadinessIsHonest(g);
    }

    // ------------------------------------------------------------------ 6. DIY quality

    private static string PoorJobName(Subsystem s) => Data.DiyQuality!.PoorJobs[s].Name;

    [Fact]
    public void An_unlucky_first_clean_leaves_a_poor_job_that_inspection_finds()
    {
        var g = NewGame(new FixedRandom(0.0));
        Ok(Walk.Do(g, "negative terminal", n => new Clean(n)));
        Assert.Empty(Ready(g).Open);
        Assert.Empty(Log(g).Entries.Where(e => e.Text.Contains("your own work")));

        var inspected = Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Equal([PoorJobName(Subsystem.Electrical)], inspected.NewlyDiscovered.Select(k => k.Name));
        Assert.Equal(PoorJobName(Subsystem.Electrical), Ready(g).Open.Single().Problem);

        // Doing it again properly: the redo clears it.
        Ok(g.Execute(new Clean("negative terminal")));
        Assert.Contains(PoorJobName(Subsystem.Electrical), Ready(g).Fixed);
    }

    [Fact]
    public void A_lucky_random_never_produces_a_poor_job()
    {
        var g = NewGame(new FixedRandom(0.99));
        Ok(Walk.Do(g, "negative terminal", n => new Clean(n)));
        Assert.Empty(Ok(g.Execute(new Inspect("negative terminal"))).NewlyDiscovered);
        Ok(g.Execute(new Clean("positive terminal")));
        Assert.Empty(Ok(g.Execute(new Inspect("positive terminal"))).NewlyDiscovered);
        Assert.Empty(Ready(g).Open);
    }

    [Fact]
    public void Skill_rises_so_that_after_the_threshold_no_more_poor_jobs_happen()
    {
        var threshold = Data.DiyQuality!.Threshold;
        var g = NewGame(new FixedRandom(0.0));
        var electrical = new[] { "negative terminal", "positive terminal", "negative cable", "main positive cable" };
        Walk.Near(g, "negative terminal");

        for (int job = 0; job < electrical.Length; job++)
        {
            Ok(g.Execute(new Clean(electrical[job])));
            var found = Ok(g.Execute(new Inspect(electrical[job]))).NewlyDiscovered;
            if (job < threshold) Assert.Single(found);
            else Assert.Empty(found);
        }
    }

    /// <summary>A scripted session; every result goes into the transcript as JSON.</summary>
    private static string PlayScript(int seed)
    {
        var g = NewGame(new SeededRandom(seed));
        var transcript = new List<string>();
        void Run(CommandResult r) => transcript.Add(Json(r));
        Run(g.Execute(new OrderSurvey()));
        foreach (var name in new[] { "negative terminal", "positive terminal", "negative cable", "primary filter", "seacock", "start battery" })
        {
            Walk.Near(g, name);
            Run(g.Execute(new Clean(name)));
            Run(g.Execute(new Inspect(name)));
            Run(g.Execute(new Replace(name)));
            Run(g.Execute(new Inspect(name)));
        }
        Run(Walk.Start(g));
        transcript.Add(Json(g.Ask(new Readiness())));
        transcript.Add(Json(g.Ask(new Logbook())));
        return string.Join("\n", transcript);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(99)]
    public void The_same_seed_plays_out_identically(int seed) =>
        Assert.Equal(PlayScript(seed), PlayScript(seed));

    // ------------------------------------------------------------------ 7. money floor

    [Fact]
    public void Spending_the_balance_down_refuses_the_next_purchase_and_names_both_amounts()
    {
        var engine = DataComponent("engine-block").ReplaceCostNok;
        var g = NewGame();
        Ok(Walk.Do(g, "engine block", n => new Replace(n)));
        var left = Data.Economy.StartingBalanceNok - engine;
        Assert.Equal(left, Status(g).BalanceNok);

        var fee = Data.Survey!.FeeNok;
        Assert.True(fee > left, "Test premise: the survey must cost more than what is left.");
        var refused = Refused(g.Execute(new OrderSurvey()));
        Assert.Equal($"That's {fee} NOK and you have {left} NOK left.", Text(refused));

        var tank = DataComponent("fuel-tank").ReplaceCostNok;
        var replace = Refused(Walk.Do(g, "fuel tank", n => new Replace(n)));
        Assert.Equal($"That's {tank} NOK and you have {left} NOK left.", Text(replace));
        Assert.Equal(left, Status(g).BalanceNok);
    }

    [Fact]
    public void The_balance_never_goes_negative_and_the_yard_is_refused_too_when_it_is_short()
    {
        var g = NewGame();
        Survey(g);
        var pump = DataComponent("injection-pump").ReplaceCostNok;
        CommandResult last;
        int guard = 0;
        do
        {
            last = Walk.Do(g, "injection pump", n => new Replace(n));
            Assert.True(Status(g).BalanceNok >= 0);
        } while (last.Accepted && ++guard < 50);

        Assert.False(last.Accepted);
        var balance = Status(g).BalanceNok;
        Assert.Equal($"That's {pump} NOK and you have {balance} NOK left.", Text(last));

        var yardPrice = DataFault("seacock-weeping").Yard!.Nok;
        Assert.True(yardPrice > balance, "Test premise: the yard fix must cost more than what is left.");
        var yard = Refused(g.Execute(new FixByYard("seacock")));
        Assert.Equal($"That's {yardPrice} NOK and you have {balance} NOK left.", Text(yard));
        var route = Ready(g).Open.Single(o => o.Problem == WeepingSeacock).Routes.Single(r => r.Route == "Yard");
        Assert.False(route.Available);
        Assert.Equal(balance, Status(g).BalanceNok);
    }

    // ------------------------------------------------------------------ 8. water in the fuel

    private static readonly string WaterName = FaultName("water-in-fuel");

    [Fact]
    public void Water_in_the_fuel_is_only_found_by_the_filter_bowl_not_by_looking_at_the_tank()
    {
        var g = NewGame();
        Walk.Near(g, "fuel tank");
        Assert.Empty(Ok(g.Execute(new Inspect("fuel tank"))).NewlyDiscovered);
        Assert.Empty(Ok(g.Execute(new Measure("fuel tank"))).NewlyDiscovered);
        Assert.DoesNotContain(WaterName, OpenNames(g));

        var bowl = Ok(g.Execute(new CheckFilterBowl()));
        Assert.Contains(bowl.NewlyDiscovered, k => k.Name == WaterName);
        Assert.Contains(WaterName, OpenNames(g));
    }

    [Fact]
    public void Water_in_the_fuel_goes_with_three_drains_on_three_days_and_a_new_filter()
    {
        var g = NewGame();
        Walk.Near(g, "primary filter");
        var need = (int)DataFault("water-in-fuel").Properties["diyDrainDays"];
        Assert.Equal(3, need);
        for (int day = 0; day < need; day++)
        {
            Ok(g.Execute(new CheckFilterBowl()));
            Ok(g.Execute(new WaitDays(1)));
        }
        var replace = Ok(g.Execute(new Replace("primary filter")));

        Assert.Contains("fuel runs clean", Text(replace));
        Assert.Contains(WaterName, Ready(g).Fixed);
        Assert.DoesNotContain(WaterName, OpenNames(g));
    }

    [Fact]
    public void Draining_three_times_on_one_day_is_not_enough()
    {
        var g = NewGame();
        Walk.Near(g, "primary filter");
        for (int i = 0; i < 3; i++) Ok(g.Execute(new CheckFilterBowl()));
        var replace = Ok(g.Execute(new Replace("primary filter")));

        Assert.DoesNotContain("fuel runs clean", Text(replace));
        Assert.Contains(WaterName, OpenNames(g));
        Assert.DoesNotContain(WaterName, Ready(g).Fixed);
    }

    [Fact]
    public void A_new_filter_without_the_drains_leaves_the_water_in_the_tank()
    {
        var g = NewGame();
        Survey(g);
        Ok(Walk.Do(g, "primary filter", n => new Replace(n)));
        Assert.Contains(WaterName, OpenNames(g));
        Assert.DoesNotContain(FaultName("primary-filter-clogged"), OpenNames(g));
    }

    // ------------------------------------------------------------------ 9 and 10. the whole thing

    /// <summary>Plays the pre-launch story, recording every result as JSON and checking the no-leak rules as it goes.</summary>
    private sealed class Session
    {
        public Game Game { get; } = NewGame();
        public List<string> Transcript { get; } = [];

        public CommandResult Do(CommandResult r)
        {
            Record(Json(r));
            return r;
        }

        public CommandResult Do(Func<string, Command> make, string name) => Do(Walk.Do(Game, name, make));
        public CommandResult Do(Command c) => Do(Game.Execute(c));

        public T Ask<T>(Query q) where T : QueryResult
        {
            var r = Game.Ask(q);
            Record(Json(r));
            return (T)r;
        }

        private void Record(string json)
        {
            Transcript.Add(json);
            var known = Status(Game).DiscoveredFaults.Select(k => k.Name).ToHashSet();
            foreach (var fault in Data.AllFaults.Where(f => !known.Contains(f.Name)))
                Assert.DoesNotContain(fault.Name, json);
            foreach (var template in Data.DiyQuality!.PoorJobs.Values)
                Assert.DoesNotContain(template.Name, json);
        }
    }

    private static Session PlayThrough()
    {
        var s = new Session();
        var g = s.Game;
        s.Ask<BriefingView>(new Briefing());
        s.Ask<SurveyReportView>(new SurveyReport());
        Ok(s.Do(new OrderSurvey()));
        var report = s.Ask<SurveyReportView>(new SurveyReport());
        Assert.Equal(12, report.Items.Count);

        // The seacock, by hand, on the hard.
        Walk.ToPlace(g, "saloon");
        Ok(s.Do(new Remove("saloon floorboards")));
        s.Ask<AboardView>(new Aboard());
        Ok(s.Do(new Replace("seacock")));

        // The impeller: found, and put off.
        Walk.Near(g, "impeller");
        Ok(s.Do(new Uncover("impeller")));
        var found = Ok(s.Do(new Inspect("impeller")));
        Assert.Contains(found.NewlyDiscovered, k => k.Name == "Tired impeller");
        Ok(s.Do(new Defer("impeller")));
        s.Ask<ComponentActionsView>(new ComponentActions("Raw-water impeller"));

        // The terminal, and a start.
        Ok(s.Do(n => new Clean(n), "negative terminal"));
        Walk.ToPlace(g, "cockpit");
        var start = Ok(s.Do(new StartEngine()));
        Assert.True(start.EngineRunning, Text(start));
        Assert.True(start.Finished);

        var launch = Ok(s.Do(new Launch()));
        Assert.IsType<LaunchView>(launch.Observation);
        s.Ask<ReadinessView>(new Readiness());
        s.Ask<StatusView>(new Status());
        s.Ask<LogView>(new Logbook());
        s.Ask<AboardView>(new Aboard());
        s.Ask<CommandList>(new Commands());
        return s;
    }

    [Fact]
    public void Full_pre_launch_play_through_ends_with_the_impeller_deferred_and_the_boat_launched()
    {
        var s = PlayThrough();
        var g = s.Game;
        var impeller = DataFault("impeller-tired");

        Assert.True(Status(g).Launched);
        var ready = Ready(g);
        Assert.True(ready.Launched);
        Assert.Contains(WeepingSeacock, ready.Fixed);
        var item = ready.Open.Single(o => o.Problem == impeller.Name);
        Assert.Equal("Deferred", item.State);
        Assert.Equal(impeller.Consequence, item.Consequence);
        Assert.Equal(impeller.Diy!.PartsNok, item.CostNowNok);
        Assert.Equal(impeller.CostLaterNok, item.CostLaterNok);
        Assert.Equal(impeller.LaterNote, item.LaterNote);

        var launchEntry = Log(g).Entries.Single(e => e.Text == "Launched.");
        Assert.NotNull(launchEntry);
        var launchResult = s.Transcript.Single(t => t.Contains("\"type\":\"launch-view\""));
        var view = JsonSerializer.Deserialize<JsonElement>(launchResult).GetProperty("Observation");
        Assert.Equal(1, view.GetProperty("DeferredCount").GetInt32());
        Assert.Contains("with 1 deferred item", string.Join(" ", view.GetProperty("Lines").EnumerateArray().Select(l => l.GetString())));
        Assert.Equal(ready.Open.Count, view.GetProperty("OpenCount").GetInt32());
    }

    private static IEnumerable<string> AllIds() =>
        Data.Components.Select(c => c.Id)
            .Concat(Data.AllFaults.Select(f => f.Id))
            .Concat(Data.Places.Select(p => p.Id))
            .Concat(Data.Covers.Select(c => c.Id))
            .Concat(Data.Positions.Select(p => p.Id))
            .Distinct();

    private static void AssertNoIds(string json, string hint)
    {
        foreach (var id in AllIds())
        {
            var at = json.IndexOf($"\"{id}\"", StringComparison.Ordinal);
            Assert.False(at >= 0, $"The id \"{id}\" appears as a whole value in a serialised result{hint}: ...{(at >= 0 ? json[Math.Max(0, at - 80)..Math.Min(json.Length, at + 80)] : "")}...");
        }
    }

    [Fact]
    public void Nothing_serialised_in_the_play_through_leaks_ids_or_internals()
    {
        var json = string.Join("\n", PlayThrough().Transcript);
        AssertNoIds(json, " (KnownFault.Id in NewlyDiscovered / Status.DiscoveredFaults carries the raw fault id: spec no-leak rule vs. the public KnownFault shape)");
    }

    [Fact]
    public void Apart_from_the_known_fault_id_nothing_in_the_play_through_leaks_ids_or_internals()
    {
        var json = System.Text.RegularExpressions.Regex.Replace(
            string.Join("\n", PlayThrough().Transcript), "\"Id\":\"[^\"]*\",", "");
        AssertNoIds(json, "");

        foreach (var banned in new[] { "condition", "seriesOhms", "present" })
            Assert.False(json.Contains(banned, StringComparison.OrdinalIgnoreCase), $"'{banned}' appears in a serialised result.");
    }
}
