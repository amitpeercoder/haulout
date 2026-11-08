using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// The boat as sold (design note 010, second pass F): a tired heads with a weeping joker valve, an old spirit stove, a rusted chain with a
/// frozen shackle that seizes in a wet season, and upgrades that replace what is there.
/// </summary>
public class BoatAsSoldTests
{
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
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static AboardView Aboard(Game g) => (AboardView)g.Ask(new Aboard());
    private static WeatherSeries Dry() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));
    private static WeatherSeries Wet() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 10, 12, 8));

    /// <summary>As she starts: on the hard on day 75, nothing touched.</summary>
    private static Game OnTheHard(Boat? boat = null, WeatherSeries? weather = null, int seed = Game.DefaultSeed) =>
        new(boat ?? BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), seed, weather ?? Dry());

    private static Fault Frozen(Boat b) => b.Get("anchor-shackle").Faults.Single();
    private static Fault Weeping(Boat b) => b.Get("joker-valve").Faults.Single();

    // ------------------------------------------------------------------ the data

    [Fact]
    public void The_new_parts_come_after_the_chain_with_kinds_and_the_tired_ones_work()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(["anchor-gear", "old-heads", "joker-valve", "spirit-stove", "anchor-shackle"], boat.Components.TakeLast(5).Select(c => c.Id));
        Assert.Equal("appliance", boat.Get("old-heads").Kind!.Id);
        Assert.Equal("appliance", boat.Get("spirit-stove").Kind!.Id);
        Assert.Equal("fitting", boat.Get("joker-valve").Kind!.Id);
        Assert.Equal("fitting", boat.Get("anchor-shackle").Kind!.Id);
        Assert.Empty(boat.Get("old-heads").Faults);
        Assert.Empty(boat.Get("spirit-stove").Faults);
        Assert.Contains("slowly", boat.Get("spirit-stove").Spec.Description);
        Assert.True(Weeping(boat).Present);
        Assert.True(Frozen(boat).Present);
        Assert.False(boat.Get("anchor-gear").Faults.Single().Present);   // the chain does not start seized any more
    }

    [Fact]
    public void The_parts_cost_200_each_and_the_chain_is_as_it_was()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal(200, boat.Get("joker-valve").Spec.ReplaceCostNok);
        Assert.Equal(200, boat.Get("anchor-shackle").Spec.ReplaceCostNok);
        Assert.Equal((60, 60), (boat.Get("joker-valve").Spec.ReplaceMinutes, boat.Get("anchor-shackle").Spec.ReplaceMinutes));
        Assert.Equal(1800, boat.Get("anchor-gear").Spec.ReplaceCostNok);
        Assert.Equal(60, boat.Get("anchor-gear").Spec.ReplaceMinutes);
        var chain = boat.Get("anchor-gear").Faults.Single();
        Assert.Equal((1800, 60, 2400, 1), (chain.Diy!.PartsNok, chain.Diy.Minutes, chain.Yard!.Nok, chain.Yard.LeadDays));
        Assert.All(new[] { "joker-valve", "anchor-shackle" }, id => Assert.True(boat.Chandlery.Single(i => i.ForComponent == id).Stocked));
    }

    // ------------------------------------------------------------------ the heads

    [Fact]
    public void The_forepeak_smells_when_first_seen_and_nothing_is_found_yet()
    {
        var g = OnTheHard();
        Walk.ToPlace(g, "saloon");
        var go = Ok(g.Execute(new GoTo("Forepeak")));
        Assert.Contains("A faint smell of sewage hangs in the forepeak, sweetish and stale, strongest by the heads.", Text(go));
        Assert.Empty(go.NewlyDiscovered);
        Assert.Empty(Status(g).DiscoveredFaults);
    }

    [Fact]
    public void Inspecting_the_valve_finds_the_damp_and_the_worn_flap_but_the_heads_alone_tell_nothing()
    {
        var g = OnTheHard();
        Walk.ToPlace(g, "forepeak");
        var heads = Ok(g.Execute(new Inspect("heads")));
        Assert.Empty(heads.NewlyDiscovered);
        Assert.Contains("hand-pump marine toilet", Text(heads));
        var valve = Ok(g.Execute(new Inspect("joker valve")));
        Assert.Contains("A bead of damp at the pump's outlet, and the rubber flap of the joker valve is worn thin", Text(valve));
        Assert.Contains(valve.NewlyDiscovered, k => k.Name == "Weeping joker valve");
    }

    [Fact]
    public void A_new_valve_is_200_and_an_hour_and_the_fault_is_gone()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = OnTheHard(boat);
        Walk.ToPlace(g, "forepeak");
        Ok(g.Execute(new Inspect("joker valve")));
        int before = Status(g).BalanceNok;
        var fix = Ok(g.Execute(new Replace("joker valve")));
        Assert.Equal((200, 60), (fix.CostNok, fix.MinutesTaken));
        Assert.Equal(FaultState.Fixed, Weeping(boat).State);
        Assert.Equal(before - 200, Status(g).BalanceNok);
        Assert.Contains(Log(g).Entries, e => e.Text == "Replaced the joker valve." && e.CostNok == 200);
        Assert.Contains(Status(g).DiscoveredFaults, k => k.Name == "Weeping joker valve" && k.Fixed);
    }

    [Fact]
    public void The_yard_fits_a_valve_for_600_and_a_day()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = OnTheHard(boat);
        Walk.ToPlace(g, "forepeak");
        Ok(g.Execute(new Inspect("joker valve")));
        int day = Status(g).Day;
        var fix = Ok(g.Execute(new FixByYard("Weeping joker valve")));
        Assert.Equal(600, fix.CostNok);
        Assert.Equal(FaultState.Fixed, Weeping(boat).State);
        Assert.Equal(day + 1, Status(g).Day);
    }

    [Fact]
    public void The_valve_and_the_old_heads_offer_their_kinds_verbs_whatever_is_wrong()
    {
        var g = OnTheHard();
        Walk.ToPlace(g, "forepeak");
        Ok(g.Execute(new Look()));
        var heads = Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions("heads")));
        Assert.Equal(["inspect"], heads.Options.Select(o => o.Verb).Where(v => v is not ("defer" or "undefer" or "yard-fix")));
        var valve = Assert.IsType<ComponentActionsView>(g.Ask(new ComponentActions("joker valve")));
        Assert.Equal(["inspect", "replace"], valve.Options.Select(o => o.Verb).Where(v => v is not ("defer" or "undefer" or "yard-fix")).Order());
    }

    // ------------------------------------------------------------------ the stove

    [Fact]
    public void The_old_stove_is_in_the_galley_and_works_slowly()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = OnTheHard(boat);
        Walk.ToPlace(g, "companionway");
        var stove = Ok(g.Execute(new Inspect("stove")));
        Assert.Contains("old two-burner alcohol stove", Text(stove));
        Assert.Contains("takes its time", Text(stove));
        Assert.Empty(stove.NewlyDiscovered);
        Assert.Empty(boat.Get("spirit-stove").ActiveFaults);
    }

    // ------------------------------------------------------------------ the chain and the shackle

    [Fact]
    public void Inspecting_the_shackle_on_deck_finds_the_frozen_pin_and_the_brown_chain()
    {
        var g = OnTheHard();
        Walk.ToPlace(g, "cockpit");
        var look = Ok(g.Execute(new GoTo("On deck")));
        Assert.Contains("The chain where it leaves the roller is brown with rust.", Text(look));
        Assert.Empty(look.NewlyDiscovered);
        var inspect = Ok(g.Execute(new Inspect("shackle")));
        Assert.Contains("The shackle pin is rusted into the anchor's eye; the chain is brown with rust but the links still move.", Text(inspect));
        Assert.Contains(inspect.NewlyDiscovered, k => k.Name == "Rusted chain with a frozen shackle");
    }

    [Fact]
    public void A_new_shackle_is_200_and_an_hour_and_the_chain_never_seizes_after_it()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = OnTheHard(boat, Wet());
        Walk.ToPlace(g, "on-deck");
        Ok(g.Execute(new Inspect("shackle")));
        var fix = Ok(g.Execute(new Replace("shackle")));
        Assert.Equal((200, 60), (fix.CostNok, fix.MinutesTaken));
        Assert.Equal(FaultState.Fixed, Frozen(boat).State);
        for (int i = 0; i < 300; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.False(boat.AllFaults.Single(f => f.Id == "anchor-chain-seized").Present);
        Assert.Empty(boat.Get("anchor-gear").ActiveFaults);
    }

    [Fact]
    public void The_yard_fits_a_shackle_for_500_and_a_day()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = OnTheHard(boat);
        Walk.ToPlace(g, "on-deck");
        Ok(g.Execute(new Inspect("shackle")));
        var fix = Ok(g.Execute(new FixByYard("Rusted chain with a frozen shackle")));
        Assert.Equal(500, fix.CostNok);
        Assert.Equal(FaultState.Fixed, Frozen(boat).State);
    }

    [Fact]
    public void The_chain_seizes_at_a_rate_that_follows_the_spread_and_the_rain_not_a_timer()
    {
        // Dry: base rate x spread, one day at a time, until it crosses the threshold.
        var dryBoat = BoatLoader.LoadSloop28();
        var dry = OnTheHard(dryBoat, Dry());
        var spec = Frozen(dryBoat).Progression!;
        double spread = Frozen(dryBoat).Spread!.Value;
        Assert.InRange(spread, 0.8, 1.2);
        Ok(dry.Execute(new WaitDays(10)));
        Assert.Equal(spec.BaseRatePerDay * spread * 10, Frozen(dryBoat).Progress, 9);

        // Heavy rain every day: the exposure factor is 1 + rainWeight (here 1.5 times) and the same spread (it is the fault's own roll).
        var wetBoat = BoatLoader.LoadSloop28();
        var wet = OnTheHard(wetBoat, Wet());
        Ok(wet.Execute(new WaitDays(10)));
        Assert.Equal(Frozen(dryBoat).Spread, Frozen(wetBoat).Spread);
        Assert.Equal(1.5, Frozen(wetBoat).Progress / Frozen(dryBoat).Progress, 6);

        int SeizeDay(WeatherSeries weather)
        {
            var boat = BoatLoader.LoadSloop28();
            var g = OnTheHard(boat, weather);
            for (int d = 0; d < 400; d++)
            {
                Ok(g.Execute(new WaitDays(1)));
                if (Frozen(boat).Escalated) return Status(g).Day;
            }
            return -1;
        }
        int dryDay = SeizeDay(Dry()), wetDay = SeizeDay(Wet());
        Assert.InRange(wetDay, 1, dryDay - 1);
        Assert.InRange((double)(wetDay - 75) / (dryDay - 75), 0.6, 0.72);   // about 1 / 1.5
    }

    [Fact]
    public void A_seized_chain_wakes_with_the_shackles_escalation_and_a_known_shackle_says_so_in_words()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = OnTheHard(boat, Wet());
        Walk.ToPlace(g, "on-deck");
        Ok(g.Execute(new Inspect("shackle")));
        var readiness = (ReadinessView)g.Ask(new Readiness());
        var item = readiness.Open.Single(o => o.Problem == "Rusted chain with a frozen shackle");
        Assert.NotNull(item.LikelyMinDays);   // the readiness gives a range for how soon it gets worse
        Assert.Equal(200, item.CostNowNok);
        Assert.Equal(1800, item.CostLaterNok);
        Walk.ToPlace(g, "on-deck");
        for (int i = 0; i < 200 && !Frozen(boat).Escalated; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.True(Frozen(boat).Escalated);
        Assert.True(boat.AllFaults.Single(f => f.Id == "anchor-chain-seized").Present);
        Assert.Empty(Status(g).DiscoveredFaults.Where(k => k.Name == "Seized anchor chain" && k.Fixed));
    }

    [Fact]
    public void Over_the_seeds_one_to_ten_the_chain_never_seizes_before_midsummer_and_usually_by_the_club_cruise()
    {
        var days = new List<int>();
        for (int seed = 1; seed <= 10; seed++)
        {
            var boat = BoatLoader.LoadSloop28();
            var g = new Game(boat, 75, new FixedRandom(0.99), seed);
            for (int d = 75; d < 300 && !Frozen(boat).Escalated; d++) Ok(g.Execute(new WaitDays(1)));
            Assert.True(Frozen(boat).Escalated, $"seed {seed}");
            days.Add(Status(g).Day);
        }
        Assert.All(days, d => Assert.True(d > 172, string.Join(",", days)));          // midsummer is day 172: a shackle fitted by then never meets a seized chain
        Assert.InRange(days.Count(d => d <= 215), 6, 9);                              // usually, not always, by the club cruise (day 215)
    }

    [Fact]
    public void The_own_spread_takes_no_draw_from_the_world_stream()
    {
        // The faults that start active and have a progression take the world stream's draws in data order, one each, and the
        // shackle (its own range, its own roll) takes none: the draws that follow, and every seeded outcome, are where they were.
        var boat = BoatLoader.LoadSloop28();
        _ = OnTheHard(boat);
        var drawn = boat.AllFaults.Where(f => f.Progression is not null && f.Present && f.Progression.Spread is null).ToList();
        Assert.NotEmpty(drawn);
        var world = new SeededRandom(Game.DefaultSeed * 7919 + 17);
        var rates = boat.Rates;
        foreach (var f in drawn)
            Assert.Equal(rates.SpreadLo + world.NextDouble() * (rates.SpreadHi - rates.SpreadLo), f.Spread!.Value, 12);
        Assert.NotNull(Frozen(boat).Spread);
    }

    // ------------------------------------------------------------------ fitting a replacement

    private static Game ShelfGame(Boat boat) =>
        new(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Dry());

    [Fact]
    public void Fitting_the_new_heads_takes_the_old_heads_and_the_valve_off_the_boat_with_their_faults()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);   // no orders: the item comes off the shelf
        var g = ShelfGame(boat);
        Walk.ToPlace(g, "forepeak");
        Ok(g.Execute(new Inspect("joker valve")));   // the player has found it: it is on the known list
        Assert.Contains(Aboard(g).Components, c => c.Name == "Joker valve" && c.KnownProblem);
        Ok(g.Execute(new Buy("marine heads")));
        Ok(g.Execute(new Install("marine heads")));
        Assert.True(boat.IsFitted("marine-heads"));
        Assert.False(boat.TryGet("old-heads", out _));
        Assert.False(boat.TryGet("joker-valve", out _));
        Assert.Equal(["old-heads", "joker-valve"], boat.Retired.Select(c => c.Id));
        Assert.DoesNotContain(boat.AllFaults, f => f.Id == "joker-valve-weeping");
        Assert.Equal(FaultState.Fixed, boat.Retired.Single(c => c.Id == "joker-valve").Faults.Single().State);

        // The cutaway and the lists: the old parts are gone, the new heads are there, and the known problem shows as done.
        var names = Aboard(g).Components.Select(c => c.Name).ToList();
        Assert.DoesNotContain("Joker valve", names);
        Assert.DoesNotContain("Hand-pump heads", names);
        Assert.Contains("Marine heads with holding tank", names);
        Assert.Contains(Status(g).DiscoveredFaults, k => k.Name == "Weeping joker valve" && k.Fixed);
        Assert.DoesNotContain(((ReadinessView)g.Ask(new Readiness())).Open, o => o.Problem == "Weeping joker valve");
        Assert.Contains("Weeping joker valve", ((ReadinessView)g.Ask(new Readiness())).Fixed);
        // Naming the old parts now finds nothing.
        Assert.False(g.Execute(new Inspect("joker valve")).Accepted);
        // And the fault is not in the way of a replacement part: a spare joker valve has nothing to go on.
        Assert.DoesNotContain(boat.AllFaults, f => f.ComponentId == "old-heads");
    }

    [Fact]
    public void Fitting_the_gas_stove_takes_the_spirit_stove_off_the_boat()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var g = ShelfGame(boat);
        Walk.ToPlace(g, "companionway");
        Ok(g.Execute(new Look()));
        Assert.Contains(Aboard(g).Components, c => c.Name == "Spirit stove");
        Ok(g.Execute(new Buy("gas stove")));
        Ok(g.Execute(new Install("gas stove")));
        Assert.False(boat.TryGet("spirit-stove", out _));
        Assert.Equal(["spirit-stove"], boat.Retired.Select(c => c.Id));
        Assert.DoesNotContain(Aboard(g).Components, c => c.Name == "Spirit stove");
        Assert.Contains(Aboard(g).Components, c => c.Name == "Gas stove");
    }

    [Fact]
    public void A_replacement_leaves_the_indices_of_the_other_faults_alone()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        var before = boat.AllFaultsEver.Select(f => f.Id).ToList();
        boat.Fit("marine-heads");
        Assert.Equal(before, boat.AllFaultsEver.Take(before.Count).Select(f => f.Id));
        Assert.Equal(before.Count, boat.AllFaultsEver.Count());   // the new heads carry no fault
    }
}
