using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

/// <summary>002 finding 4: once the starter has been measured while cranking, the meter names the gap, never a part.</summary>
public class CircuitGapTests
{
    private static readonly string[] HaikuOrder =
    [
        "start battery", "main positive cable", "battery switch", "starter solenoid", "negative cable", "ground stud",
        "starter motor", "positive terminal", "negative terminal",
    ];

    private static Game NewGame()
    {
        var g = Game.NewFirstFault();
        Assert.True(g.Execute(new Buy("remote start button")).Accepted);
        return g;
    }

    private static MeterReading Measure(Game g, string name)
    {
        var r = Walk.Do(g, name, n => new Measure(n));
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return Assert.IsType<MeterReading>(r.Observation);
    }

    /// <summary>The model's own battery-minus-starter cranking voltage for the boat as it stands.</summary>
    private static double ModelGap(Game g)
    {
        var boat = (Boat)typeof(Game).GetField("_boat", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;
        var e = EngineModel.Attempt(EngineInputsBuilder.Build(boat)).Electrical;
        return e.BatteryTerminalVolts - e.StarterVolts;
    }

    [Fact]
    public void In_Haikus_order_the_gap_shows_from_the_starter_on_and_the_accounted_sum_grows_with_each_drop()
    {
        var g = NewGame();
        double accounted = 0;
        bool starterSeen = false;
        foreach (var name in HaikuOrder)
        {
            var m = Measure(g, name);
            if (m.Kind == "volts-drop") accounted += Math.Round(m.Value!.Value, 2);
            if (name == "starter motor") starterSeen = true;

            if (!starterSeen)
            {
                Assert.Null(m.Gap);
                continue;
            }
            var gap = Assert.IsType<CircuitGap>(m.Gap);
            Assert.Equal(ModelGap(g), gap.GapVolts, 6);
            Assert.Equal(accounted, gap.AccountedVolts, 6);
            Assert.True(gap.GapVolts > 5, "the corroded terminal makes a big gap");
            if (name == "starter motor" || name == "positive terminal") Assert.False(gap.AccountedFor, name);
            if (name == "negative terminal") Assert.True(gap.AccountedFor);
        }
        Assert.True(starterSeen);
    }

    [Fact]
    public void A_healthy_boat_shows_only_the_accounted_for_wording()
    {
        var g = NewGame();
        Walk.Do(g, "negative terminal", n => new Clean(n));
        Assert.Null(Measure(g, "start battery").Gap);
        var m = Measure(g, "starter motor");
        var gap = Assert.IsType<CircuitGap>(m.Gap);
        Assert.True(gap.Ordinary);
        var line = Haulout.Prose.Prose.CircuitGapLine(gap);
        Assert.DoesNotContain("lost somewhere", line);
        Assert.Contains("sound one", line);
    }

    [Fact]
    public void A_reading_made_stale_by_a_repair_is_dropped_and_the_gap_follows_the_current_chain()
    {
        var g = NewGame();
        Measure(g, "start battery");
        Measure(g, "negative terminal");               // the big drop, shown
        var before = Assert.IsType<CircuitGap>(Measure(g, "starter motor").Gap);
        Assert.True(before.AccountedVolts > 5);

        Walk.Do(g, "negative terminal", n => new Clean(n));
        // The battery and starter readings were taken before the clean and no longer match the chain: no line until both are read again.
        Assert.Null(Measure(g, "main positive cable").Gap);
        Assert.Null(Measure(g, "start battery").Gap);
        var after = Assert.IsType<CircuitGap>(Measure(g, "starter motor").Gap);
        Assert.Equal(ModelGap(g), after.GapVolts, 6);
        Assert.True(after.Ordinary);
        Assert.True(after.AccountedVolts < 0.5, "the stale terminal reading no longer counts");
    }

    [Fact]
    public void Drops_measured_on_a_previous_day_do_not_count()
    {
        var g = NewGame();
        Measure(g, "start battery");
        Measure(g, "negative terminal");
        Assert.True(g.Execute(new WaitDays(1)).Accepted);
        Assert.Null(Measure(g, "main positive cable").Gap);   // yesterday's readings are gone: nothing known today
        Assert.Null(Measure(g, "starter motor").Gap);          // no battery reading today
        Measure(g, "start battery");
        var gap = Assert.IsType<CircuitGap>(Measure(g, "starter motor").Gap);
        Assert.True(gap.AccountedVolts < 0.5, "only today's cable counts, not yesterday's terminal");
        Assert.False(gap.AccountedFor);
    }

    [Fact]
    public void The_line_never_names_a_part()
    {
        var boat = BoatLoader.LoadSloop28();
        var names = boat.Components.SelectMany(c => c.Aliases.Append(c.Name).Append(c.Id)).Where(n => n.Length > 3).Distinct();
        foreach (var gap in new[] { new CircuitGap(7.8, 0.37), new CircuitGap(7.9, 7.8), new CircuitGap(1.2, 0.0) })
        {
            var line = Haulout.Prose.Prose.CircuitGapLine(gap).ToLowerInvariant();
            foreach (var n in names) Assert.DoesNotContain(n.ToLowerInvariant(), line);
        }
        Assert.Equal("7.8 V lost somewhere in the starting circuit while cranking; your readings so far account for 0.37 V of it.",
            Haulout.Prose.Prose.CircuitGapLine(new CircuitGap(7.8, 0.37)));
    }

    [Fact]
    public void The_starter_alone_shows_no_line_and_the_battery_measured_after_it_brings_the_line_in()
    {
        var g = NewGame();
        Assert.Null(Measure(g, "starter motor").Gap);          // the battery has not been read today
        Assert.Null(Measure(g, "main positive cable").Gap);
        var m = Measure(g, "start battery");
        var gap = Assert.IsType<CircuitGap>(m.Gap);
        Assert.Equal(ModelGap(g), gap.GapVolts, 6);
        Assert.Equal(Math.Round(Measure(g, "main positive cable").Value!.Value, 2), gap.AccountedVolts, 6);
    }
}
