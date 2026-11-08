using System.Text.Json;
using System.Text.RegularExpressions;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The valuation, the listing, the buyers, the sale and the closing card, and Keep (design note 008, step 3).</summary>
public class SaleTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string Sold = "She's sold. The logbook is all that's left of her for you.";
    private const string PumpCracked = "raw-water-pump-cracked";

    // ------------------------------------------------------------------ builders and small helpers

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static CommandResult No(CommandResult r, string text)
    {
        Assert.False(r.Accepted);
        Assert.Equal(text, Assert.Single(r.Narrative));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static SeasonView Season(Game g) => (SeasonView)g.Ask(new Season());
    private static SaleView Sale(Game g) => (SaleView)g.Ask(new Sale());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static ReadinessView Readiness(Game g) => (ReadinessView)g.Ask(new Readiness());
    private static Fault FaultOf(Boat boat, string id) => boat.AllFaults.Single(f => f.Id == id);

    private static WeatherDay CalmDay => new(0, 3, "southwest", 0, 6, 2);
    private static WeatherSeries Calm() => WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8));

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Boat BoatWith(Func<string, string> edit) => BoatLoader.Load(edit(Sloop28Json()));

    private static Boat BoatWithBalance(int nok) =>
        BoatWith(j => j.Replace("\"startingBalanceNok\": 25000", $"\"startingBalanceNok\": {nok}"));

    /// <summary>The buyer's surveyor never misses what is there to be found.</summary>
    private static Boat SharpSurveyor() => BoatWith(j => j.Replace("\"findChance\": 0.85", "\"findChance\": 1.0"));

    private static Game Fresh(int seed = Game.DefaultSeed, WeatherSeries? weather = null, Boat? boat = null, double luck = 0.99) =>
        new(boat ?? BoatLoader.LoadSloop28(), 75, new FixedRandom(luck), seed, weather ?? Calm());

    private static void Prepare(Game g)
    {
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
    }

    /// <summary>The fixes done and she is afloat: a season has begun (Keep is refused before that).</summary>
    private static Game Launched(Game g)
    {
        Prepare(g);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        return g;
    }

    private static void Wait(Game g, int days)
    {
        while (days > 0)
        {
            int chunk = Math.Min(60, days);
            Ok(g.Execute(new WaitDays(chunk)));
            days -= chunk;
        }
    }

    private static void WaitTo(Game g, int day)
    {
        while (Status(g).Day < day) Ok(g.Execute(new WaitDays(Math.Min(60, day - Status(g).Day))));
    }

    private static void Try(Func<CommandResult> act, Game g, int days = 12)
    {
        for (int i = 0; i < days; i++)
        {
            var r = act();
            if (r.Accepted) return;
            if (!string.Join(" ", r.Narrative).Contains("won't", StringComparison.Ordinal)) Assert.Fail(string.Join(" ", r.Narrative));
            Ok(g.Execute(new WaitDays(1)));
        }
        Assert.Fail("The wind never dropped.");
    }

    /// <summary>A day at a time until a new viewing is on the books; returns the result of the day it came on.</summary>
    private static CommandResult WaitForViewing(Game g, int maxDays = 90)
    {
        int seen = Sale(g).Viewings.Count;
        for (int i = 0; i < maxDays; i++)
        {
            var r = Ok(g.Execute(new WaitDays(1)));
            if (Sale(g).Viewings.Count > seen) return r;
        }
        Assert.Fail($"No one came to look at her in {maxDays} days.");
        return null!;
    }

    private static void WaitForOpenOffers(Game g, int count, int maxDays = 90)
    {
        for (int i = 0; i < maxDays && Sale(g).Viewings.Count(v => v.Open) < count; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.True(Sale(g).Viewings.Count(v => v.Open) >= count, $"fewer than {count} offers open after {maxDays} days");
    }

    private static ValuationLine Line(ValuationLetter letter, string startsWith) =>
        letter.Lines.Single(l => l.Text.StartsWith(startsWith, StringComparison.Ordinal));

    private static bool Seasonal(ValuationLine l) =>
        l.Text.StartsWith("Sold in", StringComparison.Ordinal) || l.Text.StartsWith("Up to the broker's floor", StringComparison.Ordinal);

    // the lift, for the winter scenarios (see LayupTests)
    private static Game LaidUp(Boat boat, WeatherSeries weather, int liftDay = 262, int launchDay = 252, int seed = Game.DefaultSeed)
    {
        var g = new Game(boat, 250, new FixedRandom(0.99), seed, weather);
        Prepare(g);
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(g.Execute(new BookHaulout(liftDay)));
        WaitTo(g, launchDay);
        Ok(Walk.Start(g));
        Try(() => g.Execute(new Launch()), g);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        if (Status(g).Day < liftDay) WaitTo(g, liftDay);
        Walk.ToPlace(g, "cockpit");
        Try(() =>
        {
            if (!Status(g).EngineRunning) Ok(Walk.Start(g));
            return g.Execute(new HaulOut());
        }, g);
        Assert.True(Season(g).LaidUp);
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    private static WeatherSeries Winter(double rainMm, double tempC, double nightC, int fromDay = 262) =>
        WeatherSeries.From(d => d < fromDay ? CalmDay : new WeatherDay(0, 3, "north", rainMm, tempC, nightC));

    private static WeatherSeries Mild() => Winter(0, 8, 7);

    /// <summary>Launched on day 75 and afloat with the engine running, in calm southwesterly weather.</summary>
    private static Game Afloat(out Boat boat, int balance = 25000)
    {
        boat = BoatWithBalance(balance);
        var g = new Game(boat, 75, new FixedRandom(0.99), Game.DefaultSeed, Calm());
        Prepare(g);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        return g;
    }

    private static void MotorToArrival(Game g)
    {
        for (int i = 0; i < 12; i++)
            if (Ok(g.Execute(new Motor())).Observation is TripResultView) return;
        Assert.Fail("She never arrived.");
    }

    // ------------------------------------------------------------------ the valuation, term by term

    [Fact]
    public void At_the_start_the_letter_is_the_base_less_five_undocumented_years_plus_the_spring_swing()
    {
        var g = Fresh();
        var letter = Sale(g).Letter!;

        // Base 180 000; four years at purchase and this season, none serviced yet: five at 3 000; no entries; March is spring: +4 %.
        Assert.Equal(180000, letter.Lines[0].Nok);
        Assert.Equal(-15000, Line(letter, "Five years").Nok);
        int spring = (int)Math.Round((180000 - 15000) * 0.04, MidpointRounding.AwayFromZero);
        Assert.Equal(spring, Line(letter, "Sold in spring").Nok);
        Assert.Equal(180000 - 15000 + spring, letter.ValueNok);
        Assert.Equal(letter.ValueNok, letter.Lines.Sum(l => l.Nok));
        Assert.Equal(3, letter.Lines.Count);
        Assert.Null(letter.AskNok);
        Assert.Equal("Holm & Datter, yacht brokers", letter.Broker);
        Assert.Equal("A buyer's surveyor will go over her for themselves.", Assert.Single(letter.Text));
    }

    [Fact]
    public void An_engine_service_takes_one_undocumented_year_off_and_a_maintenance_entry_adds_to_the_premium()
    {
        var g = Fresh();
        Ok(Walk.Do(g, "engine oil", c => new Replace(c)));
        var letter = Sale(g).Letter!;

        Assert.Equal(-12000, Line(letter, "Four years").Nok);          // 4 at purchase; this season has its service entry
        Assert.Equal(600, Line(letter, "A kept logbook: one maintenance entry").Nok);
        int subtotal = 180000 - 12000 + 600;
        Assert.Equal((int)Math.Round(subtotal * 0.04, MidpointRounding.AwayFromZero), Line(letter, "Sold in spring").Nok);
        Assert.Equal(letter.ValueNok, letter.Lines.Sum(l => l.Nok));
    }

    [Fact]
    public void Each_of_the_service_parts_counts_as_a_service_entry_and_so_does_winterising()
    {
        foreach (var part in new[] { "impeller", "primary filter", "secondary filter", "alternator belt" })
        {
            var g = Fresh();
            Ok(Walk.Do(g, part, c => new Replace(c)));
            Assert.Equal(-12000, Line(Sale(g).Letter!, "Four years").Nok);
        }
        // A part that is not the engine's service does not.
        var other = Fresh();
        Ok(Walk.Do(other, "cockpit drains", c => new Replace(c)));
        Assert.Equal(-15000, Line(Sale(other).Letter!, "Five years").Nok);
    }

    [Fact]
    public void Keep_starts_a_new_season_and_the_current_one_is_undocumented_until_it_has_a_service_entry()
    {
        // Serviced in the first season, kept: the first season is documented, the new one is not yet.
        var g = Launched(Fresh());
        Ok(Walk.Do(g, "engine oil", c => new Replace(c)));
        Ok(g.Execute(new Keep()));
        var letter = Sale(g).Letter!;
        Assert.Equal(-15000, Line(letter, "Five years").Nok);
        Assert.DoesNotContain(letter.Lines, l => l.Text.StartsWith("A kept logbook", StringComparison.Ordinal)); // the count started again
        Ok(Walk.Do(g, "engine oil", c => new Replace(c)));
        Assert.Equal(-12000, Line(Sale(g).Letter!, "Four years").Nok);

        // Never serviced, kept: both seasons are undocumented.
        var h = Launched(Fresh());
        Ok(h.Execute(new Keep()));
        Assert.Equal(-18000, Line(Sale(h).Letter!, "Six years").Nok);
    }

    [Fact]
    public void The_logbook_premium_is_capped()
    {
        var g = Fresh(boat: BoatWithBalance(500000));
        for (int i = 0; i < 5; i++) Ok(Walk.Do(g, "alternator belt", c => new Replace(c)));
        Assert.Equal(5 * 600, Line(Sale(g).Letter!, "A kept logbook").Nok);
        for (int i = 0; i < 10; i++) Ok(Walk.Do(g, "alternator belt", c => new Replace(c)));
        var letter = Sale(g).Letter!;
        Assert.Equal(12 * 600, Line(letter, "A kept logbook").Nok);
        Assert.Contains("twelve maintenance entries", Line(letter, "A kept logbook").Text);
    }

    // lead-review fix: a Clean only counts when it put something right

    [Fact]
    public void Cleaning_a_sound_part_over_and_over_adds_nothing_to_the_premium()
    {
        var g = Fresh();
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));                 // fixes the starter circuit: one entry
        Assert.Equal(600, Line(Sale(g).Letter!, "A kept logbook: one").Nok);
        for (int i = 0; i < 12; i++) Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Assert.Equal(600, Line(Sale(g).Letter!, "A kept logbook: one").Nok);
    }

    [Fact]
    public void A_replace_always_counts_even_of_a_sound_part()
    {
        var g = Fresh();
        for (int i = 0; i < 3; i++) Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Assert.Equal(1800, Line(Sale(g).Letter!, "A kept logbook").Nok);
    }

    [Fact]
    public void Scrubbing_and_cleaning_the_hull_count_only_when_there_was_growth()
    {
        var g = Fresh();
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        Ok(g.Execute(new Scrub()));                                            // clean hull afloat, nothing to take off
        Assert.DoesNotContain(Sale(g).Letter!.Lines, l => l.Text.StartsWith("A kept logbook", StringComparison.Ordinal));
    }

    [Fact]
    public void Cleaning_the_hull_on_the_hard_counts_only_with_growth_on_it()
    {
        // Spring on the hard, no growth: nothing to take off.
        var g = Fresh();
        Ok(Walk.Do(g, "hull", c => new Clean(c)));
        Assert.DoesNotContain(Sale(g).Letter!.Lines, l => l.Text.StartsWith("A kept logbook", StringComparison.Ordinal));

        // After a season afloat there is growth: the clean on the hard counts, and so does the yard wash.
        var h = LaidUp(BoatWith(j => j.Replace("\"startingBalanceNok\": 25000", "\"startingBalanceNok\": 60000").Replace("\"paintProtection\": 0.9", "\"paintProtection\": 0").Replace("\"baseGrowthPerDay\": 0.008", "\"baseGrowthPerDay\": 0.02")), Calm());
        int before = Line(Sale(h).Letter!, "A kept logbook").Nok;
        Ok(Walk.Do(h, "hull", c => new Clean(c)));
        Assert.Equal(before + 600, Line(Sale(h).Letter!, "A kept logbook").Nok);
        Ok(Walk.Do(h, "hull", c => new Clean(c)));                              // now clean: nothing to take off
        Assert.Equal(before + 600, Line(Sale(h).Letter!, "A kept logbook").Nok);
    }

    [Fact]
    public void Spring_autumn_and_winter_move_the_price_and_summer_does_not()
    {
        var g = Fresh();
        void Check(int day, string? expected, double factor)
        {
            WaitTo(g, day);
            var letter = Sale(g).Letter!;
            int subtotal = letter.Lines.Where(l => !Seasonal(l)).Sum(l => l.Nok);
            if (expected is null)
            {
                Assert.DoesNotContain(letter.Lines, Seasonal);
                Assert.Equal(subtotal, letter.ValueNok);
                return;
            }
            Assert.Equal((int)Math.Round(subtotal * factor, MidpointRounding.AwayFromZero), Line(letter, expected).Nok);
            Assert.Equal(letter.ValueNok, letter.Lines.Sum(l => l.Nok));
        }

        Check(75, "Sold in spring", 0.04);       // March
        Check(179, "Sold in spring", 0.04);      // end of June
        Check(180, null, 0);                    // July
        Check(239, null, 0);                    // end of August
        Check(240, "Sold in autumn or winter", -0.04);   // September
        Check(359, "Sold in autumn or winter", -0.04);   // December
        Check(361, "Sold in autumn or winter", -0.04);   // January
        Check(419, "Sold in autumn or winter", -0.04);   // end of February, year two
        Check(420, "Sold in spring", 0.04);
        // The season swing is eight points between a spring and an autumn sale of the same boat.
        Assert.True(Sale(g).Letter!.Lines.Single(Seasonal).Nok > 0);
    }

    [Fact]
    public void The_valuation_never_drops_below_the_floor()
    {
        var g = Fresh(boat: BoatWith(j => j.Replace("\"baseNok\": 180000", "\"baseNok\": 20000")));
        var letter = Sale(g).Letter!;
        // 20 000 less 15 000 is 5 000, 5 200 with the spring swing: under the floor of 30 % of the base, 6 000.
        Assert.Equal(6000, letter.ValueNok);
        Assert.Equal(6000 - 5200, Line(letter, "Up to the broker's floor").Nok);
        Assert.Equal(letter.ValueNok, letter.Lines.Sum(l => l.Nok));
    }

    [Fact]
    public void A_known_fault_costs_its_yard_price_else_its_parts_else_what_it_costs_left()
    {
        // The yard price.
        var g = Fresh();
        Ok(Walk.Do(g, "nav-lights", c => new Inspect(c)));
        var name = Assert.Single(Status(g).DiscoveredFaults).Name;
        Assert.Equal(-700, Line(Sale(g).Letter!, name).Nok);

        // No yard route: the parts.
        var noYard = Fresh(boat: BoatWith(j => Regex.Replace(j, @",\s*""yard"":\s*\{\s*""how"":\s*""The yard electrician fits a new lens"",[^}]*\}", "")));
        Ok(Walk.Do(noYard, "nav-lights", c => new Inspect(c)));
        Assert.Equal(-250, Line(Sale(noYard).Letter!, "Cracked navigation light lens").Nok);

        // Neither: costLater.
        var neither = Fresh(boat: BoatWith(j =>
            Regex.Replace(Regex.Replace(j, @",\s*""yard"":\s*\{\s*""how"":\s*""The yard electrician fits a new lens"",[^}]*\}", ""),
                @",\s*""diy"":\s*\{\s*""how"":\s*""Fit a new lens"",[^}]*\}", "")));
        Ok(Walk.Do(neither, "nav-lights", c => new Inspect(c)));
        Assert.Equal(-900, Line(Sale(neither).Letter!, "Cracked navigation light lens").Nok);
    }

    [Fact]
    public void Heavy_growth_the_player_has_seen_is_a_finding_at_the_yards_wash_price()
    {
        var g = Fresh();
        g.FoulingGrowth = 0.5;
        // Not seen yet: the broker knows nothing of it.
        Assert.DoesNotContain(Sale(g).Letter!.Lines, l => l.Text.Contains("growth", StringComparison.OrdinalIgnoreCase));
        Ok(Walk.Do(g, "hull", c => new Inspect(c)));
        Assert.Equal(-1200, Line(Sale(g).Letter!, "Heavy growth").Nok);

        // A film the player has seen is not heavy.
        var light = Fresh();
        light.FoulingGrowth = 0.1;
        Ok(Walk.Do(light, "hull", c => new Inspect(c)));
        Assert.DoesNotContain(Sale(light).Letter!.Lines, l => l.Text.Contains("growth", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ the letter and what it may say

    [Fact]
    public void The_letter_names_only_the_faults_the_player_knows_are_open()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = Fresh(boat: boat);
        var undiscovered = boat.AllFaults.Where(f => f.Present && !f.Harmless).ToList();
        Assert.True(undiscovered.Count > 5);

        void NoneNamed(ValuationLetter letter)
        {
            foreach (var f in boat.AllFaults.Where(f => !f.Discovered))
                Assert.DoesNotContain(letter.Lines, l => l.Text.Contains(f.Name, StringComparison.Ordinal));
        }
        NoneNamed(Sale(g).Letter!);

        Ok(Walk.Do(g, "nav-lights", c => new Inspect(c)));
        var letter = Sale(g).Letter!;
        Assert.Single(letter.Lines, l => l.Text.StartsWith("Cracked navigation light lens", StringComparison.Ordinal));
        NoneNamed(letter);

        // Fixed, it is no longer in the letter.
        Ok(Walk.Do(g, "nav-lights", c => new Replace(c)));
        Assert.DoesNotContain(Sale(g).Letter!.Lines, l => l.Text.StartsWith("Cracked navigation light lens", StringComparison.Ordinal));
    }

    [Fact]
    public void A_deferred_fault_still_counts_against_the_price()
    {
        var g = Fresh();
        Ok(Walk.Do(g, "nav-lights", c => new Inspect(c)));
        var name = Status(g).DiscoveredFaults.Single().Name;
        Ok(g.Execute(new Defer(name)));
        Assert.Equal(-700, Line(Sale(g).Letter!, name).Nok);
    }

    // ------------------------------------------------------------------ listing

    [Fact]
    public void Listing_takes_the_time_hands_back_the_letter_and_shows_in_the_season_and_the_status()
    {
        var g = Fresh();
        var r = Ok(g.Execute(new ListForSale(150000)));
        Assert.Equal(30, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        var view = Assert.IsType<ListingView>(r.Observation);
        Assert.Equal(150000, view.Letter.AskNok);
        Assert.Equal(Sale(g).Letter!.ValueNok, view.Letter.ValueNok);
        Assert.Contains(r.Narrative, l => l.Contains("150000 NOK", StringComparison.Ordinal));

        var season = Season(g);
        Assert.True(season.Listed);
        Assert.Equal(150000, season.AskNok);
        Assert.Equal(0, season.OpenOffers);
        Assert.False(season.Kept);
        Assert.Contains("She is listed for sale at 150000 NOK.", season.Text);
        Assert.Contains("She is listed for sale at 150000 NOK.", Status(g).Season);
        Assert.Contains(Log(g).Entries, e => e.Text.StartsWith("Listed her for sale with Holm & Datter, yacht brokers at 150000 NOK.", StringComparison.Ordinal));

        var sale = Sale(g);
        Assert.True(sale.Listed);
        Assert.Equal(150000, sale.AskNok);
        Assert.Empty(sale.Viewings);

        // Listing again changes the ask.
        var again = Ok(g.Execute(new ListForSale(140000)));
        Assert.Contains(again.Narrative, l => l.Contains("change the asking price to 140000 NOK", StringComparison.Ordinal));
        Assert.Equal(140000, Sale(g).AskNok);
    }

    [Fact]
    public void Listing_refusals_are_free_and_unlogged()
    {
        var g = Fresh();
        int entries = Log(g).Entries.Count;
        No(g.Execute(new ListForSale(0)), "Name a price above zero: list for sale <nok>.");
        No(g.Execute(new ListForSale(-5)), "Name a price above zero: list for sale <nok>.");
        Assert.False(Season(g).Listed);

        Prepare(g);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        entries = Log(g).Entries.Count;
        No(g.Execute(new ListForSale(150000)), "The broker sells her from the hard.");
        Assert.Equal(entries, Log(g).Entries.Count);
        Assert.False(Sale(g).Listed);
    }

    [Fact]
    public void Listing_and_accepting_are_refused_at_sea_and_away_from_the_yard()
    {
        var g = Afloat(out _);
        Ok(g.Execute(new Depart()));
        No(g.Execute(new ListForSale(150000)), "You're at sea.");
        No(g.Execute(new AcceptOffer()), "You're at sea.");
        No(g.Execute(new Keep()), "You're at sea.");
        MotorToArrival(g);                  // now at Hestholmen, afloat
        Walk.ToPlace(g, "cockpit");
        No(g.Execute(new ListForSale(150000)), "The yard is at Skjærvik.");
    }

    // ------------------------------------------------------------------ the buyers

    [Fact]
    public void Viewings_come_only_while_she_is_listed_and_on_the_hard()
    {
        var unlisted = Fresh();
        Wait(unlisted, 40);
        Assert.Empty(Sale(unlisted).Viewings);

        var g = Fresh();
        Ok(g.Execute(new ListForSale(100000)));
        WaitForViewing(g);
        Prepare(g);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        int before = Sale(g).Viewings.Count;
        Wait(g, 20);
        Assert.Equal(before, Sale(g).Viewings.Count);
    }

    [Fact]
    public void Viewings_are_deterministic_per_seed_and_do_not_depend_on_the_players_luck()
    {
        string Run(int seed, double luck)
        {
            var g = Fresh(seed, WeatherSeries.Generate(seed), luck: luck);
            Ok(g.Execute(new ListForSale(120000)));
            Wait(g, 45);
            return JsonSerializer.Serialize(Sale(g).Viewings);
        }

        foreach (int seed in new[] { 1, 4, 9 })
        {
            string a = Run(seed, 0.99);
            Assert.Equal(a, Run(seed, 0.99));
            Assert.Equal(a, Run(seed, 0.01)); // the player's luck is another stream
            Assert.NotEqual("[]", a);
        }
        Assert.NotEqual(Run(1, 0.99), Run(2, 0.99));
    }

    [Fact]
    public void The_market_draws_nothing_from_the_world_stream()
    {
        double[] Spreads(bool listed)
        {
            // A surveyor who finds nothing, so that no discovery changes how fast the faults age: only the market is different.
            var boat = BoatWith(j => j.Replace("\"findChance\": 0.85", "\"findChance\": 0.0"));
            var g = Fresh(boat: boat, weather: WeatherSeries.Generate(Game.DefaultSeed));
            if (listed) Ok(g.Execute(new ListForSale(100000)));
            Wait(g, 40);
            Assert.True(!listed || Sale(g).Viewings.Count > 0);
            return boat.AllFaults.Select(f => f.Spread ?? 0).ToArray();
        }
        Assert.Equal(Spreads(false), Spreads(true));
    }

    [Fact]
    public void A_high_ask_brings_far_fewer_viewings_than_a_fair_one()
    {
        int Count(int ask)
        {
            int n = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var g = Fresh(seed, WeatherSeries.Generate(seed));
                Ok(g.Execute(new ListForSale(ask)));
                Wait(g, 50);
                n += Sale(g).Viewings.Count;
            }
            return n;
        }
        int fair = Count(100000), dear = Count(600000);
        Assert.True(fair > 5 * dear, $"fair {fair}, dear {dear}");
    }

    [Fact]
    public void The_season_changes_how_often_buyers_come()
    {
        int Count(int start)
        {
            int n = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var g = Fresh(seed, WeatherSeries.Generate(seed));
                WaitTo(g, start);
                Ok(g.Execute(new ListForSale(100000)));
                Wait(g, 40);
                n += Sale(g).Viewings.Count;
            }
            return n;
        }
        int spring = Count(100), autumn = Count(300);   // April, then late October
        Assert.True(spring > autumn, $"spring {spring}, autumn {autumn}");
    }

    [Fact]
    public void A_viewing_is_a_report_a_logbook_entry_and_an_alert()
    {
        var g = Fresh();
        Ok(g.Execute(new ListForSale(100000)));
        var r = WaitForViewing(g);
        var v = Sale(g).Viewings[0];

        Assert.Equal(1, v.Number);
        Assert.Equal(v.Day + 7, v.HoldsUntilDay);
        Assert.True(v.Open);
        Assert.True(v.OfferNok > 0);
        Assert.Equal(1, Season(g).OpenOffers);
        Assert.Contains("with an offer open", Season(g).Text);

        var alert = Assert.Single(r.Alerts, a => a.Text.StartsWith("The broker rang:", StringComparison.Ordinal));
        Assert.Contains($"offers {v.OfferNok} NOK, open until day {v.HoldsUntilDay}", alert.Text);
        Assert.Contains("'sale'", alert.Text);
        var entry = Assert.Single(Log(g).Entries, e => e.Document == "viewing 1");
        Assert.Contains($"{v.OfferNok} NOK", entry.Text);
        Assert.Equal(v.Day, entry.Day);
    }

    [Fact]
    public void The_buyers_surveyor_finds_a_latent_fault_the_player_never_found_and_it_becomes_known()
    {
        var boat = SharpSurveyor();
        var g = Fresh(boat: boat);
        var seacock = FaultOf(boat, "seacock-weeping");
        var neg = FaultOf(boat, "neg-terminal-corrosion");
        var blisters = FaultOf(boat, "hull-old-paint-blisters");
        Assert.False(seacock.Discovered);
        Assert.True(seacock.Present);
        Ok(g.Execute(new ListForSale(100000)));
        int unknownLetter = Sale(g).Letter!.ValueNok;

        var r = WaitForViewing(g);
        var v = Sale(g).Viewings[0];

        Assert.True(seacock.Discovered);
        Assert.Contains(r.NewlyDiscovered, k => k.Name == seacock.Name);
        Assert.Contains(Status(g).DiscoveredFaults, k => k.Name == seacock.Name);
        Assert.Contains($"{seacock.Name}: {seacock.Yard!.Nok} NOK at the yard.", v.Findings);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Found: {seacock.Name}.");
        // Present faults the surveyor can see are all found at a sure chance; a harmless one is ruled out, not found.
        Assert.True(neg.Discovered);
        Assert.False(blisters.Discovered);
        Assert.DoesNotContain(v.Findings, f => f.Contains(blisters.Name, StringComparison.Ordinal));
        // The next letter now names it.
        Assert.Contains(Sale(g).Letter!.Lines, l => l.Text.StartsWith(seacock.Name, StringComparison.Ordinal));

        // They value her at the letter's arithmetic over what they found: much less than the letter that knew nothing.
        Assert.True(v.Findings.Count >= 3);
        Assert.True(v.ValueNok < unknownLetter - 5000, $"{v.ValueNok} against {unknownLetter}");
    }

    [Fact]
    public void Once_the_buyers_findings_are_known_the_letter_values_her_as_the_buyer_did()
    {
        var g = Fresh(boat: SharpSurveyor());
        Ok(g.Execute(new ListForSale(100000)));
        WaitForViewing(g);
        var v = Sale(g).Viewings[0];
        // The same arithmetic over the same findings, in the same month: the letter and the buyer agree to the krone.
        Assert.Equal(new Haulout.Sim.Time.GameClock(v.Day).Month, new Haulout.Sim.Time.GameClock(Status(g).Day).Month);
        Assert.Equal(Sale(g).Letter!.ValueNok, v.ValueNok);
    }

    [Fact]
    public void A_fault_woken_by_the_winter_is_found_with_its_cause_line_told_and_logged()
    {
        var boat = BoatWith(j => j.Replace("\"findChance\": 0.85", "\"findChance\": 1.0"));
        var g = LaidUp(boat, Winter(0, 0, -6));
        var pump = FaultOf(boat, PumpCracked);
        Ok(g.Execute(new ListForSale(100000)));

        CommandResult? found = null;
        for (int i = 0; i < 60 && found is null; i++)
        {
            var r = Ok(g.Execute(new WaitDays(1)));
            if (r.NewlyDiscovered.Any(k => k.Name == pump.Name)) found = r;
        }
        Assert.NotNull(found);
        Assert.True(pump.Discovered);
        var cause = Assert.Single(found!.Narrative, l => l.StartsWith("The raw-water pump body has cracked.", StringComparison.Ordinal));
        Assert.Contains("frost", cause);

        var entries = Log(g).Entries.ToList();
        int causeAt = entries.FindIndex(e => e.Text == cause);
        Assert.True(causeAt >= 0);
        Assert.Equal($"Found: {pump.Name}.", entries[causeAt + 1].Text);
        Assert.Contains(Sale(g).Viewings, v => v.Findings.Any(f => f.StartsWith($"{pump.Name}: {pump.Yard!.Nok} NOK at the yard", StringComparison.Ordinal)));
    }

    [Fact]
    public void Offers_are_in_the_spread_round_to_a_hundred_and_never_exceed_the_ask()
    {
        var g = Fresh();
        Ok(g.Execute(new ListForSale(1000000)));   // far over any value
        for (int i = 0; i < 3; i++) WaitForViewing(g, 400);
        foreach (var v in Sale(g).Viewings)
        {
            Assert.Equal(0, v.OfferNok % 100);
            Assert.InRange(v.OfferNok, (int)Math.Round(v.ValueNok * 0.88 / 100) * 100, (int)Math.Round(v.ValueNok * 1.02 / 100) * 100);
            Assert.True(v.OfferNok < 1000000);
        }
        Assert.True(Sale(g).Viewings.Select(v => v.OfferNok).Distinct().Count() > 1);

        // An ask below any value: every offer is the ask.
        var low = Fresh();
        Ok(low.Execute(new ListForSale(50000)));
        Wait(low, 20);
        Assert.NotEmpty(Sale(low).Viewings);
        Assert.All(Sale(low).Viewings, v => Assert.Equal(50000, v.OfferNok));
    }

    [Fact]
    public void An_offer_lapses_the_day_after_it_holds_until()
    {
        var g = Fresh();
        Ok(g.Execute(new ListForSale(100000)));
        WaitForViewing(g);
        var first = Sale(g).Viewings[0];
        Assert.True(first.Open);

        while (Status(g).Day < first.HoldsUntilDay) Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(first.HoldsUntilDay, Status(g).Day);
        Assert.True(Sale(g).Viewings[0].Open);          // still open on its last day
        Ok(g.Execute(new WaitDays(1)));
        Assert.False(Sale(g).Viewings[0].Open);         // lapsed the day after
        Assert.Equal(Sale(g).Viewings.Count(v => v.Open), Season(g).OpenOffers);
    }

    // ------------------------------------------------------------------ accepting

    [Fact]
    public void Accepting_is_refused_when_not_listed_with_nothing_open_unknown_or_lapsed()
    {
        var g = Fresh();
        int entries = Log(g).Entries.Count;
        No(g.Execute(new AcceptOffer()), "She isn't listed: list for sale <nok>.");
        Ok(g.Execute(new ListForSale(1000000)));        // dear: no viewing for a good while
        entries = Log(g).Entries.Count;
        No(g.Execute(new AcceptOffer()), "There's no offer open.");
        No(g.Execute(new AcceptOffer(3)), "There's no offer number 3.");

        var h = Fresh();
        Ok(h.Execute(new ListForSale(100000)));
        WaitForViewing(h);
        var first = Sale(h).Viewings[0];
        while (Sale(h).Viewings[0].Open) Ok(h.Execute(new WaitDays(1)));
        No(h.Execute(new AcceptOffer(first.Number)), $"Offer {first.Number} has lapsed.");
        Assert.False(Status(h).Sold);
        Assert.Equal(entries, Log(g).Entries.Count);
    }

    [Fact]
    public void Accepting_takes_the_best_open_offer_or_the_numbered_one()
    {
        Game TwoOpen()
        {
            var g = Fresh(boat: SharpSurveyor());
            Ok(g.Execute(new ListForSale(100000)));
            WaitForOpenOffers(g, 2);
            return g;
        }

        var best = TwoOpen();
        var open = Sale(best).Viewings.Where(v => v.Open).ToList();
        var top = open.OrderByDescending(v => v.OfferNok).ThenByDescending(v => v.Number).First();
        var r = Ok(best.Execute(new AcceptOffer()));
        Assert.Equal(top.OfferNok, Assert.IsType<ClosingView>(r.Observation).PriceNok);
        Assert.Contains($"to the buyer who viewed her on day {top.Day}.", r.Narrative.Single(l => l.StartsWith("Sold on day", StringComparison.Ordinal)));

        var numbered = TwoOpen();
        var any = Sale(numbered).Viewings.First(v => v.Open);
        var r2 = Ok(numbered.Execute(new AcceptOffer(any.Number)));
        var card = Assert.IsType<ClosingView>(r2.Observation);
        Assert.Equal(any.OfferNok, card.PriceNok);
        Assert.Equal(0, r2.MinutesTaken);
        Assert.True(Status(numbered).Sold);
    }

    // ------------------------------------------------------------------ the closing card

    [Fact]
    public void The_closing_card_states_the_price_the_spending_the_days_the_hours_the_trips_the_tows_and_what_the_logbook_earned()
    {
        var g = Afloat(out _);
        Ok(g.Execute(new Depart()));
        MotorToArrival(g);                                  // trip one, to Hestholmen
        Walk.ToPlace(g, "cockpit");
        Ok(Walk.Start(g));
        Ok(g.Execute(new Depart()));                         // back towards Skjærvik
        Ok(g.Execute(new Motor()));
        Ok(g.Execute(new Motor()));                          // ten of fourteen miles made: the towboat takes her on
        var tow = Ok(g.Execute(new CallTow()));
        Assert.True(Assert.IsType<TripResultView>(tow.Observation).Towed);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new EmergencyLift()));
        Ok(g.Execute(new ListForSale(100000)));
        WaitForViewing(g);
        var offer = Sale(g).Viewings.Single(v => v.Open);

        var before = Status(g);
        var r = Ok(g.Execute(new AcceptOffer()));
        var card = Assert.IsType<ClosingView>(r.Observation);
        var after = Status(g);

        Assert.Equal(offer.OfferNok, card.PriceNok);
        Assert.Equal(before.Day, card.Day);
        Assert.Equal(before.MoneySpentNok, card.SpentNok);
        Assert.Equal(before.MoneySpentNok, after.MoneySpentNok);               // income is kept apart from spending
        Assert.Equal(25000 - before.MoneySpentNok + offer.OfferNok, card.BalanceNok);
        Assert.Equal(card.BalanceNok, after.BalanceNok);
        Assert.Equal(before.Day - 75, card.Days);
        Assert.Equal((int)Math.Round(before.MinutesSpent / 60.0, MidpointRounding.AwayFromZero), card.HoursWorked);
        Assert.Equal(1, card.Trips);
        Assert.Equal(1, card.Tows);

        Assert.Equal($"Sold on day {card.Day} for {offer.OfferNok} NOK, to the buyer who viewed her on day {offer.Day}.", card.Lines[0]);
        Assert.Equal($"You spent {card.SpentNok} NOK on her over {card.Days} days, and {card.HoursWorked} hours of your own work.", card.Lines[1]);
        Assert.True(card.Days > 1 && card.HoursWorked > 1);
        Assert.Equal("Trips made: 1. Tows: 1.", card.Lines[2]);
        Assert.Matches(@"^Day \d+: Arrived at Hestholmen\.$", card.Lines[3]);
        Assert.Matches(@"^Day \d+: Called the towboat: 6000 NOK to be towed on to Skjærvik\.$", card.Lines[^1]);
        Assert.InRange(card.Lines.Count, 5, 6);   // a cause line sits between them if the logbook told one
        Assert.All(card.Lines, l => Assert.Contains(l, r.Narrative));
        Assert.True(after.Sold);
    }

    [Fact]
    public void The_card_carries_the_first_cause_line_the_logbook_told()
    {
        var boat = SharpSurveyor();
        var g = LaidUp(boat, Winter(0, 0, -6));
        Ok(g.Execute(new ListForSale(100000)));
        for (int i = 0; i < 60 && !boat.AllFaults.Single(f => f.Id == PumpCracked).Discovered; i++) Ok(g.Execute(new WaitDays(1)));
        WaitForOpenOffers(g, 1);
        var card = Assert.IsType<ClosingView>(Ok(g.Execute(new AcceptOffer())).Observation);
        Assert.Contains(card.Lines, l => Regex.IsMatch(l, @"^Day \d+: The (raw-water pump body|heat exchanger) has cracked\."));
        Assert.Equal(0, card.Trips);
        Assert.Equal("Trips made: 0. Tows: 0.", card.Lines[2]);
    }

    [Fact]
    public void After_the_sale_every_command_is_refused_free_and_unlogged_and_the_queries_answer()
    {
        var g = Fresh();
        Ok(g.Execute(new ListForSale(100000)));
        WaitForOpenOffers(g, 1);
        Ok(g.Execute(new AcceptOffer()));
        int entries = Log(g).Entries.Count;
        int day = Status(g).Day;

        Command[] commands =
        [
            new Look(), new GoTo("companionway"), new Remove("steps"), new Refit("steps"), new Inspect("hull"), new Measure("start battery"),
            new StartEngine(), new StopEngine(), new Clean("hull"), new Uncover("hull"), new Replace("engine oil"), new Charge("start battery"),
            new Buy("impeller"), new CheckFilterBowl(), new CheckRawWaterFlow(), new Defer("x"), new Undefer("x"), new FixByYard("x"),
            new OrderSurvey(), new WaitDays(3), new Launch(), new EmergencyLift(), new PumpBilge(), new Depart(), new Continue(), new Motor(),
            new Sail(), new HeaveTo(), new Anchor(), new TurnBack(), new CallTow(), new BookHaulout(270), new HaulOut(), new Scrub(),
            new WashHull(), new WinteriseEngine(), new FillTank(), new BatteryAshore(), new BatteryAboard(), new WinterCharger(),
            new FitWinterCover(), new WaitUntilSpring(), new ListForSale(100000), new AcceptOffer(), new Keep(),
        ];
        foreach (var c in commands) No(g.Execute(c), Sold);
        Assert.Equal(entries, Log(g).Entries.Count);
        Assert.Equal(day, Status(g).Day);

        Query[] queries =
        [
            new Briefing(), new Status(), new Logbook(), new Commands(), new Aboard(), new SurveyReport(), new Readiness(), new Haulout.Sim.Play.Weather(),
            new Notices(), new Inventory(), new PlanTrip(), new Trip(), new Season(), new Sale(),
        ];
        foreach (var q in queries) Assert.NotNull(g.Ask(q));
        var sale = Sale(g);
        Assert.True(sale.Sold);
        Assert.False(sale.Listed);
        Assert.Null(sale.Letter);
        Assert.NotEmpty(sale.Viewings);
        Assert.All(sale.Viewings, v => Assert.False(v.Open));
        Assert.True(Status(g).Sold);
        Assert.StartsWith("Spring, year 1. She's sold.", Season(g).Text);
    }

    // ------------------------------------------------------------------ Keep

    [Fact]
    public void Keep_from_the_autumn_of_year_one_moves_the_deadline_to_day_456_and_the_launch_counts_against_it()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = LaidUp(boat, Mild());
        Assert.Equal(96, Status(g).LaunchDeadlineDay);
        var r = Ok(g.Execute(new Keep()));
        Assert.Contains("You keep her. The yard has her down for launch on day 456.", r.Narrative);
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        Assert.Equal(456, Status(g).LaunchDeadlineDay);
        Assert.Equal(456, Season(g).LaunchDeadlineDay);
        Assert.True(Season(g).Kept);
        Assert.Equal(456, Readiness(g).LaunchDeadlineDay);
        Assert.Equal(456 - Status(g).Day, Readiness(g).DaysToDeadline);

        No(g.Execute(new Keep()), "You've kept her already: the yard has her down for launch on day 456.");

        while (Status(g).Day < 435) Ok(g.Execute(new WaitUntilSpring()));
        CommandResult? launch = null;
        for (int i = 0; i < 30 && launch is null; i++)
        {
            var attempt = g.Execute(new Launch());
            if (attempt.Accepted) launch = attempt;
            else Ok(g.Execute(new WaitDays(1)));
        }
        var card = Assert.IsType<LaunchView>(launch!.Observation);
        Assert.Equal(456, card.DeadlineDay);
        Assert.Contains(launch.Narrative, l => l.StartsWith($"Launched on day {card.Day}, {456 - card.Day} days before the deadline", StringComparison.Ordinal));
        Assert.False(Season(g).Kept);                       // launching starts the stretch over: she can be kept again
        Assert.Equal(456 - card.Day, Readiness(g).DaysToDeadline);
    }

    [Fact]
    public void Keep_before_day_96_sets_it_for_the_same_year_and_on_day_96_for_the_next()
    {
        var early = Launched(Fresh());
        Assert.Contains("on day 96", Ok(early.Execute(new Keep())).Narrative.Single(l => l.StartsWith("You keep her", StringComparison.Ordinal)));
        Assert.Equal(96, Status(early).LaunchDeadlineDay);

        var onTheDay = Launched(Fresh());
        Ok(onTheDay.Execute(new WaitDays(21)));
        Assert.Equal(96, Status(onTheDay).Day);
        Ok(onTheDay.Execute(new Keep()));
        Assert.Equal(456, Status(onTheDay).LaunchDeadlineDay);
    }

    [Fact]
    public void The_launch_card_after_a_keep_counts_against_the_moved_deadline()
    {
        var g = LaidUp(BoatLoader.LoadSloop28(), Mild());
        Ok(g.Execute(new Keep()));                       // laid up in the autumn: the next day 96 is day 456
        Assert.Equal(456, Status(g).LaunchDeadlineDay);
        while (Status(g).Day < 435) Ok(g.Execute(new WaitUntilSpring()));
        CommandResult? r = null;
        for (int i = 0; i < 30 && r is null; i++)
        {
            var attempt = g.Execute(new Launch());
            if (attempt.Accepted) r = attempt;
            else Ok(g.Execute(new WaitDays(1)));
        }
        var card = Assert.IsType<LaunchView>(r!.Observation);
        Assert.Equal(456, card.DeadlineDay);
        Assert.Contains(r.Narrative, l => l.StartsWith($"Launched on day {card.Day}, {456 - card.Day} days before the deadline", StringComparison.Ordinal));
        Assert.Equal(456 - card.Day, Readiness(g).DaysToDeadline);
        Assert.Equal(456, Status(g).LaunchDeadlineDay);
    }

    [Fact]
    public void Keep_withdraws_a_listing_and_closes_its_offers()
    {
        var g = LaidUp(BoatLoader.LoadSloop28(), Mild());
        Ok(g.Execute(new ListForSale(100000)));
        WaitForOpenOffers(g, 1);
        var r = Ok(g.Execute(new Keep()));
        Assert.Contains("You ring the broker and take her off the market.", r.Narrative);
        Assert.False(Sale(g).Listed);
        Assert.False(Season(g).Listed);
        Assert.Equal(0, Season(g).OpenOffers);
        Assert.All(Sale(g).Viewings, v => Assert.False(v.Open));
        No(g.Execute(new AcceptOffer()), "She isn't listed: list for sale <nok>.");
        int viewings = Sale(g).Viewings.Count;
        Wait(g, 30);
        Assert.Equal(viewings, Sale(g).Viewings.Count);
    }

    [Fact]
    public void Launch_withdraws_a_listing_with_a_line_and_the_offers_with_it()
    {
        var g = Fresh();
        Prepare(g);
        Ok(g.Execute(new ListForSale(100000)));
        WaitForOpenOffers(g, 1);
        Ok(Walk.Start(g));
        var r = Ok(g.Execute(new Launch()));
        Assert.Contains("You tell the broker she's going back in the water: the listing is withdrawn, and the offers with it.", r.Narrative);
        Assert.False(Sale(g).Listed);
        Assert.False(Season(g).Listed);
        Assert.Equal(0, Season(g).OpenOffers);
        Assert.All(Sale(g).Viewings, v => Assert.False(v.Open));
        Assert.Contains(Log(g).Entries, e => e.Text == "Took her off the market: the listing is withdrawn.");
        No(g.Execute(new AcceptOffer()), "She isn't listed: list for sale <nok>.");
    }

    [Fact]
    public void A_listing_on_the_hard_through_a_winter_is_a_free_inspection_of_what_the_winter_did()
    {
        var boat = SharpSurveyor();
        var g = LaidUp(boat, Mild());
        Ok(g.Execute(new ListForSale(100000)));
        WaitForViewing(g, 200);
        var v = Sale(g).Viewings[0];
        Assert.Equal(1, v.Number);
        // Autumn and winter value her four points lower than a spring sale would.
        Assert.Contains(Sale(g).Letter!.Lines, l => l.Text.StartsWith("Sold in autumn or winter", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ leaks

    [Fact]
    public void No_view_of_the_season_the_sale_or_the_status_carries_an_id_or_a_fault_nobody_has_found()
    {
        var boat = SharpSurveyor();
        var g = Fresh(boat: boat);
        var seen = new List<string>();
        void Record(object? o) => seen.Add(JsonSerializer.Serialize(o, o?.GetType() ?? typeof(object)));
        void Snap()
        {
            Record(Sale(g)); Record(Season(g)); Record(Status(g)); Record(Log(g));
        }
        void CheckNoLatentFault(bool all)
        {
            var json = string.Concat(seen);
            foreach (var f in boat.AllFaults)
            {
                Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
                bool nameKnown = boat.AllFaults.Any(d => d.Discovered && d.Name == f.Name);   // a dormant copy shares its name
                if (all || (!f.Discovered && !nameKnown)) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
            }
            foreach (var id in boat.Components.Select(c => c.Id).Where(id => id.Contains('-')))
                Assert.False(json.Contains(id, StringComparison.Ordinal), id);
            foreach (var key in new[] { "stateOfCharge", "waterHeld", "fuelLitres", "frostHoursWithWater", "condensationDays", "frozenNights", "snowDays" })
                Assert.DoesNotContain(key, json);
        }

        // Before any viewing nothing of the 25-odd latent and dormant faults is in the letter, the sale, the season or the status.
        Snap();
        var listing = Ok(g.Execute(new ListForSale(100000)));
        Record(listing.Observation);
        Record(listing);
        Snap();
        CheckNoLatentFault(all: true);

        // After a viewing, only what its surveyor found is named; everything else is still absent.
        var r = WaitForViewing(g);
        seen.Clear();
        Record(r);
        Snap();
        CheckNoLatentFault(all: false);
        Assert.Contains(boat.AllFaults, f => f.Discovered);
        Assert.Contains(boat.AllFaults, f => f.Present && !f.Discovered);   // the harmless one, and the dormant ones

        // The closing card has no ids either.
        var accept = Ok(g.Execute(new AcceptOffer()));
        seen.Clear();
        Record(accept);
        Snap();
        CheckNoLatentFault(all: false);
    }

    [Fact]
    public void The_commands_and_queries_round_trip_through_json()
    {
        foreach (Command c in new Command[] { new ListForSale(150000), new AcceptOffer(), new AcceptOffer(2), new Keep() })
        {
            var json = JsonSerializer.Serialize(c);
            Assert.Equal(c, JsonSerializer.Deserialize<Command>(json));
        }
        Assert.Equal(new Sale(), JsonSerializer.Deserialize<Query>(JsonSerializer.Serialize<Query>(new Sale())));
        Assert.Contains("\"type\":\"list-for-sale\"", JsonSerializer.Serialize<Command>(new ListForSale(1)));
        Assert.Contains("\"type\":\"accept-offer\"", JsonSerializer.Serialize<Command>(new AcceptOffer()));
        Assert.Contains("\"type\":\"keep\"", JsonSerializer.Serialize<Command>(new Keep()));
        Assert.Contains("\"type\":\"sale\"", JsonSerializer.Serialize<Query>(new Sale()));
        var catalogue = ((CommandList)new Game(BoatLoader.LoadSloop28()).Ask(new Commands())).Commands;
        foreach (var verb in new[] { "list for sale", "accept offer", "keep", "sale" }) Assert.Contains(catalogue, c => c.Verb == verb);
    }

    [Fact]
    public void The_sale_numbers_are_data_and_the_placeholders_list_says_so()
    {
        var rules = BoatLoader.LoadSloop28().Sale;
        Assert.Equal(180000, rules.BaseNok);
        Assert.Equal(7, rules.OfferHoldsDays);
        Assert.Equal(30, rules.ListMinutes);
        Assert.Equal(6, rules.ServiceComponents.Count);
        Assert.Equal(1.3, rules.SeasonViewingFactor["spring"]);
        var json = Sloop28Json();
        Assert.Contains("PLACEHOLDER (design note 008): sale {", json);
        Assert.Contains("UNVERIFIED (design note 008): the valuation practice", json);
    }
}
