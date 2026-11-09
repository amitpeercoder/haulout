using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// The raw-water impeller's shed vanes (design note 012, finding 8): a sound impeller lasts a season and a tired one still fails in a handful of hours; the missing
/// vanes can be seen with the pump cover off, a torn impeller is never "in good shape", and once found the shed is a fault on the readiness list.
/// </summary>
public class ImpellerTests
{
    private const string Gone = "Two vanes are gone at the root; the stubs are torn.";
    private const string CoverOn = "It is inside the pump behind a six-screw cover; you can't tell without taking the cover off.";

    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static ReadinessView Readiness(Game g) => (ReadinessView)g.Ask(new Readiness());

    /// <summary>Afloat with the impeller replaced (condition 1, vane factor 1) unless <paramref name="asSold"/>; the cockpit, the engine running.</summary>
    private static Game Afloat(out Boat boat, bool asSold = false)
    {
        boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        if (!asSold) Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        return g;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());

    /// <summary>The vanes shed to <paramref name="vaneFactor"/> and a command's time passed, so the fault that lists them wakes.</summary>
    private static void Shed(Game g, Boat boat, double vaneFactor)
    {
        boat.Get("impeller").State["vaneFactor"] = vaneFactor;
        Ok(g.Execute(new Look()));
    }

    // ------------------------------------------------------------------ the rate

    /// <summary>Load hours on a clean hull for the vane factor to fall from 1 to <paramref name="toFactor"/> at this condition.</summary>
    private static double HoursToShed(SeaRules rules, double condition, double toFactor) =>
        (1 - toFactor) / (rules.ImpellerShedPerLoadHour * Math.Pow(1 - condition, rules.ImpellerShedWearPower));

    [Fact]
    public void A_sound_impeller_lasts_more_than_200_load_hours_to_the_overheat_line_and_the_tired_one_as_sold_fails_in_under_10()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var rules = boat.SeaRules;
        var impeller = boat.Get("impeller");
        Assert.Equal(0.35, impeller.Condition);   // the part as sold

        // Sound (condition 0.8): flow = vane factor, under 0.25 means the vane factor lost 0.75.
        Assert.True(HoursToShed(rules, 0.8, EngineModel.OverheatBelowFlow) > 200, $"{HoursToShed(rules, 0.8, EngineModel.OverheatBelowFlow):0} h");
        // Tired (0.35): its fault carries a flow factor of 0.4 on top of the vanes; without the fault the vanes alone must still fail it in under 10 hours.
        double withFault = HoursToShed(rules, 0.35, EngineModel.OverheatBelowFlow / 0.4);
        double vanesAlone = HoursToShed(rules, 0.35, EngineModel.OverheatBelowFlow);
        Assert.InRange(withFault, 0, 3);
        Assert.InRange(vanesAlone, 0, 10);
        // A new impeller sheds nothing.
        Assert.Equal(double.PositiveInfinity, HoursToShed(rules, 1.0, EngineModel.OverheatBelowFlow));
    }

    [Fact]
    public void The_game_sheds_at_the_data_rate_one_load_hour_of_the_tired_part()
    {
        var g = Afloat(out var boat, asSold: true);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart()));
        Ok(g.Execute(new Motor()));
        double expected = 1 - boat.SeaRules.ImpellerShedPerLoadHour * Math.Pow(1 - 0.35, boat.SeaRules.ImpellerShedWearPower);
        Assert.Equal(expected, boat.Get("impeller").State["vaneFactor"], 3);
    }

    [Fact]
    public void The_fault_wakes_at_the_same_line_the_eye_sees_the_vanes_gone()
    {
        var rules = BoatLoader.LoadSloop28().SeaRules;
        var fault = BoatLoader.LoadSloop28().AllFaults.Single(f => f.Id == "impeller-shed");
        Assert.Equal(1 - rules.ImpellerShedVisibleBelow, fault.Spawn.Single().AtLeast, 6);
        Assert.Equal("cooling.vanesShed", fault.Spawn.Single().When);
    }

    // ------------------------------------------------------------------ seeing it

    [Fact]
    public void With_the_cover_off_inspect_names_the_missing_vanes_below_the_line_and_not_above_it_and_never_says_good_shape()
    {
        var g = Afloat(out var boat);
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        var sound = Text(Ok(g.Execute(new Inspect("impeller"))));
        Assert.DoesNotContain(Gone, sound);
        Assert.Contains("In good shape.", sound);

        Shed(g, boat, 0.69);
        var torn = Text(Ok(Walk.Do(g, "impeller", c => new Inspect(c))));
        Assert.Contains(Gone, torn);
        Assert.DoesNotContain("In good shape.", torn);

        Shed(g, boat, 0.71);
        Assert.DoesNotContain(Gone, Text(Ok(g.Execute(new Inspect("impeller")))));
    }

    [Fact]
    public void An_exhausted_skipper_still_sees_a_missing_vane_because_it_is_not_subtle()
    {
        var boat = BoatLoader.Load(ClubGame.Sloop28Json(), true, ClubGame.OpenShops(), withHours: true);
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        Shed(g, boat, 0.5);
        for (int i = 0; i < 3; i++) Ok(Walk.Do(g, "start battery", n => new Charge(n)));      // eight hours of own work each: exhausted
        Assert.Contains("exhausted", Status(g).Tired);
        Assert.Contains(Gone, Text(Ok(Walk.Do(g, "impeller", c => new Inspect(c)))));
    }

    [Fact]
    public void With_the_cover_on_inspect_says_what_the_eye_can_tell_below_the_line_and_not_good_shape()
    {
        var g = Afloat(out var boat);
        var sound = Text(Ok(Walk.Do(g, "impeller", c => new Inspect(c))));
        Assert.Contains("In good shape.", sound);
        Assert.DoesNotContain(CoverOn, sound);

        Shed(g, boat, 0.4);
        var covered = Text(Ok(Walk.Do(g, "impeller", c => new Inspect(c))));
        Assert.Contains(CoverOn, covered);
        Assert.DoesNotContain("In good shape.", covered);
        Assert.DoesNotContain(Gone, covered);
    }

    // ------------------------------------------------------------------ the list and the fix

    [Fact]
    public void Once_found_the_shed_vanes_are_on_the_readiness_list_and_a_replacement_puts_the_vane_factor_back()
    {
        var g = Afloat(out var boat);
        Assert.DoesNotContain(Readiness(g).Open, i => i.Problem == "Impeller with vanes shed");
        Shed(g, boat, 0.5);
        Ok(Walk.Do(g, "impeller", c => new Uncover(c)));
        var inspect = Ok(g.Execute(new Inspect("impeller")));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == "Impeller with vanes shed");
        var item = Assert.Single(Readiness(g).Open, i => i.Problem == "Impeller with vanes shed");
        Assert.Equal("Raw-water impeller", item.Component);
        Assert.True(item.CostLaterNok > 0);
        Assert.False(string.IsNullOrWhiteSpace(item.Consequence));
        // The flow already carries the vane factor: the fault adds no flow factor of its own.
        Assert.Equal(0, boat.AllFaults.Single(f => f.Id == "impeller-shed").Effect.Value);

        Ok(Walk.Do(g, "impeller", c => new Replace(c)));        // a new impeller: the spare is not aboard, so the shop's price is paid
        Assert.Equal(1.0, boat.Get("impeller").State["vaneFactor"]);
        Assert.DoesNotContain(Readiness(g).Open, i => i.Problem == "Impeller with vanes shed");
        Assert.Contains("Impeller with vanes shed", Readiness(g).Fixed);
        Ok(g.Execute(new Look()));
        Assert.DoesNotContain(boat.AllFaults, f => f.Id.StartsWith("impeller-shed", StringComparison.Ordinal) && f.Present);   // the new part has not shed
        Shed(g, boat, 0.5);                                      // and when it does, the fault that rearmed wakes again
        Assert.Contains(boat.AllFaults, f => f.Id.StartsWith("impeller-shed", StringComparison.Ordinal) && f.Present);
    }
}
