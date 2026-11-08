using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The yard calendar and the lift, two more ports, fouling and the mooring (design note 008, step 1).</summary>
public class SeasonTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string LiftWindow = "The yard has finished lifting for this year.";

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
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static TripView Trip(Game g) => (TripView)g.Ask(new Trip());
    private static TripPlanView Plan(Game g, string? to = null) => (TripPlanView)g.Ask(new PlanTrip(to));

    private static WeatherSeries Calm(double tempC = 6) =>
        WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, tempC, 2));

    private static WeatherSeries Wind(int force, string from, double tempC = 12) =>
        WeatherSeries.Constant(new WeatherDay(0, force, from, 0, tempC, 8));

    /// <summary>Calm everywhere except on the given days, which blow at the given force (a gale by default).</summary>
    private static WeatherSeries GalesOn(params int[] days) => BlowOn(8, days);

    private static WeatherSeries BlowOn(int force, params int[] days) =>
        WeatherSeries.From(d => new WeatherDay(0, days.Contains(d) ? force : 3, "southwest", 0, 6, 2));

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Boat BoatWithBalance(int nok) =>
        BoatLoader.Load(Sloop28Json().Replace("\"startingBalanceNok\": 25000", $"\"startingBalanceNok\": {nok}"));

    private static Game OnTheHard(int startDay, WeatherSeries? weather = null, int worldSeed = Game.DefaultSeed, Boat? boat = null) =>
        new(boat ?? BoatLoader.LoadSloop28(), startDay, new FixedRandom(0.99), worldSeed, weather ?? Calm());

    /// <summary>The fixes that let her start and stay afloat for the tests: terminal cleaned, seacock replaced.</summary>
    private static void Prepare(Game g, bool freshPaint = false, bool newImpeller = false)
    {
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        if (newImpeller) Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        if (freshPaint) Ok(Walk.Do(g, "antifouling", c => new Replace(c)));
    }

    /// <summary>Afloat at Skjærvik, the engine running, in the cockpit, on <paramref name="startDay"/>.</summary>
    private static Game Afloat(int startDay, WeatherSeries? weather = null, int worldSeed = Game.DefaultSeed, Boat? boat = null, bool freshPaint = false, bool newImpeller = false)
    {
        var g = OnTheHard(startDay, weather, worldSeed, boat);
        Prepare(g, freshPaint, newImpeller);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Assert.True(Status(g).Afloat);
        Assert.True(Status(g).EngineRunning);
        return g;
    }

    private static void Book(Game g, int day) => Ok(g.Execute(new BookHaulout(day)));

    /// <summary>Waits (at most 60 days at a time, stopping at a booked slot) until the day.</summary>
    private static void WaitTo(Game g, int day)
    {
        while (Status(g).Day < day) Ok(g.Execute(new WaitDays(Math.Min(60, day - Status(g).Day))));
    }

    /// <summary>Booked, waited for, the engine started again in the cockpit: ready to be lifted.</summary>
    private static void ReadyForTheCrane(Game g, int slot)
    {
        Book(g, slot);
        WaitTo(g, slot);
        Assert.Equal(slot, Status(g).Day);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
    }

    private static CommandResult Depart(Game g, string? to = null)
    {
        Walk.ToPlace(g, "cockpit");
        return Ok(g.Execute(new Depart(to)));
    }

    /// <summary>Hour by hour under engine until the trip card.</summary>
    private static TripResultView Motor(Game g)
    {
        for (int i = 0; i < 20; i++)
        {
            var r = Ok(g.Execute(i == 0 ? new Motor() : new Continue()));
            if (r.Observation is TripResultView card) return card;
        }
        throw new Xunit.Sdk.XunitException("She never arrived.");
    }

    // ------------------------------------------------------------------ the weather series

    [Fact]
    public void The_weather_series_now_runs_three_years_and_days_0_to_720_are_the_same_as_before()
    {
        Assert.Equal(1080, WeatherSeries.LastDay);
        var all = new StringBuilder();
        for (int seed = 1; seed <= 20; seed++)
        {
            var s = WeatherSeries.Generate(seed);
            Assert.Equal(1081, s.Count);
            for (int d = 0; d <= 720; d++)
            {
                var w = s[d];
                all.Append(string.Create(CultureInfo.InvariantCulture, $"{seed},{d},{w.WindForce},{w.WindFrom},{w.RainMm:R},{w.TempC:R},{w.NightC:R};"));
            }
        }
        // The hash of those 721 days for 20 seeds, taken before the series was lengthened.
        Assert.Equal("FDC2137184A6AA9D8E49EA438D42F3C95CC1E7DC3F8E09A34D16A49C45C4CA58",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString()))));
    }

    // ------------------------------------------------------------------ the calendar

    [Fact]
    public void Booking_in_July_finds_the_first_day_of_the_window_open()
    {
        var g = OnTheHard(180);
        var view = Season(g);
        Assert.Equal(255, view.FirstFreeDay);
        Assert.Equal((255, 300), (view.SlotsFromDay, view.SlotsToDay));
        Assert.Null(view.BookedDay);

        var r = Ok(g.Execute(new BookHaulout(255)));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        Assert.Equal(255, Season(g).BookedDay);
        Assert.Contains(Log(g).Entries, e => e.Text == "Booked a haulout slot for day 255.");
        Assert.Contains("day 255", Season(g).Text);
    }

    [Fact]
    public void Asking_later_pushes_the_first_slot_later_and_the_same_seed_gives_the_same_answer()
    {
        int? FirstFreeAsking(int day, int seed) => Season(OnTheHard(day, worldSeed: seed)).FirstFreeDay;

        var days = new[] { 240, 247, 254, 261, 268, 275 };
        var first = days.Select(d => FirstFreeAsking(d, Game.DefaultSeed)).ToList();
        Assert.Equal(255, first[0]);
        for (int i = 1; i < first.Count; i++)
            Assert.True(first[i] is null || first[i] >= first[i - 1], $"asking on day {days[i]}");
        // Late enough, the first free slot is weeks off.
        Assert.True(first[4] is null || first[4] >= 268 + 1 + 7);
        Assert.Equal(first, days.Select(d => FirstFreeAsking(d, Game.DefaultSeed)).ToList());

        // The drift is the seed's: some other seed drifts differently.
        Assert.Contains(Enumerable.Range(1, 12), seed => FirstFreeAsking(268, seed) != first[4]);
    }

    [Fact]
    public void A_day_before_the_first_free_slot_is_refused_free_and_the_first_free_day_is_accepted()
    {
        var g = OnTheHard(268);
        var free = Season(g).FirstFreeDay!.Value;
        Assert.True(free >= 269);
        int entries = Log(g).Entries.Count;
        No(g.Execute(new BookHaulout(free - 1)), $"The yard's first free slot is day {free}.");
        Assert.Equal(entries, Log(g).Entries.Count);
        Ok(g.Execute(new BookHaulout(free)));
        Assert.Equal(free, Season(g).BookedDay);
    }

    [Fact]
    public void The_yard_is_full_late_in_the_window_and_finished_after_it()
    {
        var late = Afloat(296);
        Assert.Null(Season(late).FirstFreeDay);
        No(late.Execute(new BookHaulout(299)), "The yard is full for the autumn: the emergency lift is all that's left.");
        Assert.Contains("full for the autumn", Season(late).Text);

        var over = Afloat(310);
        Assert.Null(Season(over).FirstFreeDay);
        No(over.Execute(new BookHaulout(312)), LiftWindow);
        Assert.Contains(LiftWindow, Season(over).Text);
    }

    [Fact]
    public void Booking_refusals_past_day_after_the_window_already_booked_and_at_sea()
    {
        var g = Afloat(180);
        No(g.Execute(new BookHaulout(100)), "Day 100 has already gone by.");
        No(g.Execute(new BookHaulout(301)), "The yard stops lifting on day 300.");
        Book(g, 260);
        No(g.Execute(new BookHaulout(260)), "You're already booked for day 260.");

        Depart(g);
        No(g.Execute(new BookHaulout(270)), "You're at sea: the yard is for when you lie at a port.");
    }

    [Fact]
    public void Changing_a_booking_costs_the_fee_and_a_broke_player_is_refused()
    {
        var g = OnTheHard(180);
        Book(g, 255);
        var change = Ok(g.Execute(new BookHaulout(262)));
        Assert.Equal(500, change.CostNok);
        Assert.Equal(262, Season(g).BookedDay);
        Assert.Contains(Log(g).Entries, e => e.Text == "Moved the haulout slot from day 255 to day 262." && e.CostNok == 500);

        var poor = OnTheHard(180, boat: BoatWithBalance(300));
        Book(poor, 255);
        No(poor.Execute(new BookHaulout(262)), "That's 500 NOK and you have 300 NOK left.");
        Assert.Equal(255, Season(poor).BookedDay);
    }

    // ------------------------------------------------------------------ the lift

    [Fact]
    public void Haul_out_on_the_booked_day_lifts_her_bills_lift_and_storage_and_shows_the_hull()
    {
        var g = Afloat(250);
        int spent = Status(g).MoneySpentNok;
        ReadyForTheCrane(g, 255);

        var r = Ok(g.Execute(new HaulOut()));
        var card = Assert.IsType<HauloutView>(r.Observation);
        Assert.Equal(120, r.MinutesTaken);
        Assert.Equal(16000, r.CostNok);
        Assert.Equal((255, 7000, 9000), (card.Day, card.LiftNok, card.StorageNok));
        Assert.StartsWith("Under the waterline:", card.HullText);
        Assert.Contains(card.HullText, r.Narrative);
        Assert.False(r.EngineRunning);

        var status = Status(g);
        Assert.False(status.Afloat);
        Assert.Equal(spent + 16000, status.MoneySpentNok);
        var season = Season(g);
        Assert.True(season.LaidUp);
        Assert.True(season.HauledOut);
        Assert.Null(season.BookedDay);
        Assert.Contains("laid up", season.Text);
        Assert.Equal(season.Text, status.Season);
        Assert.Contains(Log(g).Entries, e => e.CostNok == 16000 && e.Text.StartsWith("Hauled out for the winter", StringComparison.Ordinal));
    }

    [Fact]
    public void The_lift_bills_below_zero_like_the_tow()
    {
        var g = Afloat(250, boat: BoatWithBalance(12000));
        ReadyForTheCrane(g, 255);
        Assert.True(Status(g).BalanceNok > 0);
        Ok(g.Execute(new HaulOut()));
        Assert.True(Status(g).BalanceNok < 0);
    }

    [Fact]
    public void Launching_clears_the_laid_up_flag()
    {
        var g = Afloat(250);
        ReadyForTheCrane(g, 255);
        Ok(g.Execute(new HaulOut()));
        Assert.True(Season(g).LaidUp);
        Ok(g.Execute(new Launch()));
        var season = Season(g);
        Assert.False(season.LaidUp);
        Assert.True(season.Afloat);
        Assert.False(season.HauledOut);
    }

    [Fact]
    public void Every_haul_out_refusal_is_free_and_unlogged()
    {
        // No booking.
        var g = Afloat(250);
        No(g.Execute(new HaulOut()), "You haven't booked a slot: book haulout <day>.");
        // Not the day.
        Book(g, 262);
        No(g.Execute(new HaulOut()), "Your slot is day 262.");
        // Engine not running, not in the cockpit: on the day.
        WaitTo(g, 262);
        Walk.ToPlace(g, "cockpit");
        No(g.Execute(new HaulOut()), "Start the engine: you motor her round to the crane.");
        Ok(Walk.Start(g));
        Walk.ToPlace(g, "companionway");
        No(g.Execute(new HaulOut()), "You motor her round to the crane from the helm: go to the cockpit.");
        Walk.ToPlace(g, "cockpit");
        int entries = Log(g).Entries.Count;
        Ok(g.Execute(new HaulOut()));
        Assert.True(Log(g).Entries.Count > entries);

        // On the hard already, with a booking for today.
        var hard = OnTheHard(240);
        Book(hard, 256);
        WaitTo(hard, 256);
        No(hard.Execute(new HaulOut()), "She's already ashore.");

        // At sea, and at a port that has no yard.
        var sea = Afloat(250);
        Book(sea, 262);
        Depart(sea);
        No(sea.Execute(new HaulOut()), "You're at sea.");
        Ok(sea.Execute(new Anchor()));
        Assert.Contains("You're at sea.", sea.Execute(new HaulOut()).Narrative);

        var away = Afloat(250, Wind(3, "northeast"));
        Book(away, 262);
        Depart(away);
        Motor(away);
        WaitTo(away, 262);
        Ok(Walk.Start(away));
        No(away.Execute(new HaulOut()), "The yard is at Skjærvik.");
    }

    [Fact]
    public void The_yard_will_not_lift_in_a_gale()
    {
        var g = Afloat(250, GalesOn(262));
        ReadyForTheCrane(g, 262);
        var r = g.Execute(new HaulOut());
        Assert.False(r.Accepted);
        Assert.StartsWith("The yard won't lift in this: ", Assert.Single(r.Narrative));
        Assert.Equal(0, r.MinutesTaken);
        Assert.True(Status(g).Afloat);
    }

    // ------------------------------------------------------------------ the slot

    [Fact]
    public void A_wait_stops_at_the_morning_of_the_booked_day()
    {
        var g = Afloat(250);
        Book(g, 260);
        string time = Status(g).TimeOfDay;
        var r = Ok(g.Execute(new WaitDays(30)));
        Assert.Equal(260, Status(g).Day);
        Assert.Equal(time, Status(g).TimeOfDay);
        Assert.Contains("You come back on day 260 for your haulout slot.", r.Narrative);
        Assert.Equal(10 * 1440, r.MinutesTaken);
        Assert.Equal(260, Season(g).BookedDay);
    }

    [Fact]
    public void A_slot_that_passes_unused_is_forfeited_with_an_alert()
    {
        var g = Afloat(250);
        Book(g, 258);
        WaitTo(g, 258);
        var r = Ok(g.Execute(new WaitDays(1)));
        Assert.Null(Season(g).BookedDay);
        var alert = Assert.Single(r.Alerts, a => a.Text.StartsWith("You missed your haulout slot on day 258;", StringComparison.Ordinal));
        Assert.Contains("the yard has given it to the next boat", alert.Text);
        Assert.Contains("the first free slot is day", alert.Text);
        Assert.Contains(Log(g).Entries, e => e.Alert && e.Text == alert.Text);
    }

    [Fact]
    public void A_gale_on_the_slot_day_moves_the_slot_a_day_instead_of_forfeiting_it()
    {
        var g = Afloat(250, GalesOn(262));
        Book(g, 262);
        WaitTo(g, 262);
        var r = Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(263, Season(g).BookedDay);
        Assert.Contains(r.Alerts, a => a.Text == "The yard couldn't lift in the gale. Your slot moves to day 263.");

        Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new HaulOut()));
        Assert.False(Status(g).Afloat);
    }

    // ------------------------------------------------------------------ the emergency lift in the autumn

    [Fact]
    public void An_emergency_lift_in_the_autumn_bills_storage_lays_her_up_cancels_the_booking_and_shows_the_hull()
    {
        var g = Afloat(250);
        Book(g, 270);
        var r = Ok(g.Execute(new EmergencyLift()));
        var card = Assert.IsType<HauloutView>(r.Observation);
        Assert.Equal(9000 + 9000, r.CostNok);
        Assert.Equal((9000, 9000), (card.LiftNok, card.StorageNok));
        Assert.StartsWith("Under the waterline:", card.HullText);
        var season = Season(g);
        Assert.True(season.LaidUp);
        Assert.Null(season.BookedDay);
        Assert.Contains(r.Narrative, l => l.Contains("booking lapses"));
        Assert.False(Status(g).Afloat);
    }

    [Fact]
    public void An_emergency_lift_in_the_spring_is_what_it_was()
    {
        var g = Afloat(90);
        var r = Ok(g.Execute(new EmergencyLift()));
        Assert.Null(r.Observation);
        Assert.Equal(9000, r.CostNok);
        Assert.False(Season(g).LaidUp);
    }

    // ------------------------------------------------------------------ fouling

    [Fact]
    public void Fouling_grows_in_warm_water_not_in_cold_and_faster_on_thin_paint_than_on_fresh()
    {
        var cold = Afloat(100, Calm(tempC: 4));
        var thin = Afloat(100, Calm(tempC: 15));
        var fresh = Afloat(100, Calm(tempC: 15), freshPaint: true);
        // Unattended she lasts about five weeks afloat (the cockpit drain hose), so the stretch is short.
        foreach (var g in new[] { cold, thin, fresh }) WaitTo(g, 120);

        Assert.Equal(0, cold.FoulingGrowth);
        Assert.True(thin.FoulingGrowth > 0.07, $"thin {thin.FoulingGrowth}");
        Assert.True(fresh.FoulingGrowth > 0);
        Assert.True(thin.FoulingGrowth > 2 * fresh.FoulingGrowth, $"thin {thin.FoulingGrowth} fresh {fresh.FoulingGrowth}");
        Assert.False(Status(thin).Sunk);
    }

    [Fact]
    public void Nothing_grows_on_the_hard()
    {
        var g = OnTheHard(100, Calm(tempC: 15));
        WaitTo(g, 160);
        Assert.Equal(0, g.FoulingGrowth);
    }

    [Fact]
    public void A_fouled_boat_takes_longer_to_Hestholmen_and_wears_the_impeller_faster()
    {
        TripResultView Run(double growth)
        {
            var g = Afloat(100, Wind(3, "northeast"), newImpeller: true);
            g.FoulingGrowth = growth;
            Depart(g);
            return Motor(g);
        }

        var clean = Run(0);
        var fouled = Run(0.8);
        Assert.True(fouled.Minutes > clean.Minutes + 30, $"clean {clean.Minutes} fouled {fouled.Minutes}");

        double VanesAfterTwoHours(double growth)
        {
            var boat = BoatLoader.LoadSloop28();
            var g = Afloat(100, Wind(3, "northeast"), boat: boat);
            g.FoulingGrowth = growth;
            Depart(g);
            Ok(g.Execute(new Motor()));
            Ok(g.Execute(new Continue()));
            return boat.Get("impeller").State[EngineInputsBuilder.VaneFactorProperty];
        }
        Assert.True(VanesAfterTwoHours(0.8) < VanesAfterTwoHours(0));
    }

    [Fact]
    public void Looking_over_the_side_names_the_growth_but_only_when_there_is_some()
    {
        var g = Afloat(100);
        g.FoulingGrowth = 0.02;
        Assert.DoesNotContain(Ok(g.Execute(new Look())).Narrative, l => l.StartsWith("Over the side:", StringComparison.Ordinal));
        g.FoulingGrowth = 0.1;
        Assert.Contains("Over the side: a faint green film at the waterline.", Ok(g.Execute(new Look())).Narrative);
        g.FoulingGrowth = 0.4;
        Assert.Contains("Over the side: a green beard at the waterline.", Ok(g.Execute(new Look())).Narrative);
        g.FoulingGrowth = 0.9;
        Assert.Contains("Over the side: a thick beard of weed, and barnacles where the paint has gone.", Ok(g.Execute(new Look())).Narrative);

        // Not from below, not on the hard, not under way.
        Walk.ToPlace(g, "companionway");
        Assert.DoesNotContain(Ok(g.Execute(new Look())).Narrative, l => l.StartsWith("Over the side:", StringComparison.Ordinal));
        Walk.ToPlace(g, "on-deck");
        Assert.Contains(Ok(g.Execute(new Look())).Narrative, l => l.StartsWith("Over the side:", StringComparison.Ordinal));
        Depart(g);
        Assert.DoesNotContain(Ok(g.Execute(new Look())).Narrative, l => l.StartsWith("Over the side:", StringComparison.Ordinal));
        Ok(g.Execute(new Anchor()));
        Assert.Contains(Ok(g.Execute(new Look())).Narrative, l => l.StartsWith("Over the side:", StringComparison.Ordinal));

        var hard = OnTheHard(100);
        hard.FoulingGrowth = 0.9;
        Assert.DoesNotContain(Ok(hard.Execute(new Look())).Narrative, l => l.StartsWith("Over the side:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_hull_at_the_lift_tells_its_cause_in_the_same_voice_as_a_cause_line()
    {
        var thin = Afloat(250, Calm(tempC: 15));
        thin.FoulingGrowth = 0.9;
        ReadyForTheCrane(thin, 255);
        var card = (HauloutView)Ok(thin.Execute(new HaulOut())).Observation!;
        Assert.Contains("weed in long tails", card.HullText);
        Assert.Contains("It grew over", card.HullText);
        Assert.Contains("days afloat on thin paint, in water that reached 15 °C.", card.HullText);

        var fresh = Afloat(250, Calm(tempC: 15), freshPaint: true);
        fresh.FoulingGrowth = 0.2;
        ReadyForTheCrane(fresh, 255);
        var card2 = (HauloutView)Ok(fresh.Execute(new HaulOut())).Observation!;
        Assert.Contains("a green film over the paint", card2.HullText);
        Assert.Contains("afloat on fresh paint", card2.HullText);

        var clean = Afloat(250);
        ReadyForTheCrane(clean, 255);
        var card3 = (HauloutView)Ok(clean.Execute(new HaulOut())).Observation!;
        Assert.DoesNotContain("It grew", card3.HullText);
    }

    [Fact]
    public void Inspect_hull_on_the_hard_gives_the_hull_words()
    {
        var g = OnTheHard(100);
        g.FoulingGrowth = 0.3;
        var r = Ok(Walk.Do(g, "hull", c => new Inspect(c)));
        Assert.Contains("Under the waterline: a green fur on the hull", string.Join(" ", r.Narrative));
    }

    [Fact]
    public void Clean_hull_on_the_hard_takes_four_hours_and_all_the_growth_afloat_it_is_refused()
    {
        var g = OnTheHard(100);
        g.FoulingGrowth = 0.7;
        var r = Ok(Walk.Do(g, "hull", c => new Clean(c)));
        Assert.Equal(240, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        Assert.Equal(0, g.FoulingGrowth);
        Assert.IsType<RepairView>(r.Observation);

        var afloat = OnTheHard(100);
        Walk.ToPlace(afloat, "beside-the-hull");
        Walk.ToPlace(afloat, "cockpit");
        Prepare(afloat);
        Ok(Walk.Start(afloat));
        Ok(afloat.Execute(new Launch()));
        afloat.FoulingGrowth = 0.7;
        var refused = afloat.Execute(new Clean("hull"));
        Assert.False(refused.Accepted);
        Assert.Contains("below the waterline", Assert.Single(refused.Narrative));
        Assert.Equal(0.7, afloat.FoulingGrowth);
    }

    [Fact]
    public void The_yard_wash_costs_money_takes_all_the_growth_and_only_on_the_hard()
    {
        var g = OnTheHard(100);
        g.FoulingGrowth = 0.7;
        var r = Ok(g.Execute(new WashHull()));
        Assert.Equal(120, r.MinutesTaken);
        Assert.Equal(1200, r.CostNok);
        Assert.Equal(0, g.FoulingGrowth);

        var afloat = Afloat(100);
        No(afloat.Execute(new WashHull()), "She's in the water: the yard washes her on the hard.");

        var poor = OnTheHard(100, boat: BoatWithBalance(500));
        poor.FoulingGrowth = 0.7;
        No(poor.Execute(new WashHull()), "That's 1200 NOK and you have 500 NOK left.");
        Assert.Equal(0.7, poor.FoulingGrowth);
    }

    [Fact]
    public void Scrubbing_afloat_takes_a_part_of_the_growth_never_all()
    {
        var g = Afloat(100);
        g.FoulingGrowth = 0.5;
        var r = Ok(g.Execute(new Scrub()));
        Assert.Equal(120, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        Assert.Equal(0.3, g.FoulingGrowth, 6);
        Ok(g.Execute(new Scrub()));
        Assert.Equal(0.18, g.FoulingGrowth, 6);

        // From on deck, too; not from below.
        Walk.ToPlace(g, "on-deck");
        Ok(g.Execute(new Scrub()));
        Walk.ToPlace(g, "companionway");
        No(g.Execute(new Scrub()), "You scrub over the side: go up to the cockpit or on deck.");
    }

    [Fact]
    public void Scrub_is_refused_on_the_hard_and_under_way_but_works_at_anchor()
    {
        var hard = OnTheHard(100);
        No(hard.Execute(new Scrub()), "Clean the hull properly from the ground.");

        var g = Afloat(100);
        Depart(g);
        No(g.Execute(new Scrub()), "Anchor first.");
        Ok(g.Execute(new Anchor()));
        g.FoulingGrowth = 0.5;
        Ok(g.Execute(new Scrub()));
        Assert.Equal(0.3, g.FoulingGrowth, 6);
    }

    [Fact]
    public void New_antifouling_is_refused_over_growth_until_the_hull_is_clean()
    {
        var g = OnTheHard(100);
        g.FoulingGrowth = 0.3;
        Walk.ToPlace(g, "beside-the-hull");
        string gate = "Scrape the growth off first: clean the hull.";
        No(g.Execute(new Replace("antifouling")), gate);
        var option = ((ComponentActionsView)g.Ask(new ComponentActions("antifouling"))).Options.Single(o => o.Verb == "replace");
        Assert.False(option.Available);
        Assert.Equal(gate, option.UnavailableReason);

        Ok(g.Execute(new Clean("hull")));
        Ok(g.Execute(new Replace("antifouling")));
        // A hull with only a trace of growth takes paint.
        var light = OnTheHard(100);
        light.FoulingGrowth = 0.05;
        Ok(Walk.Do(light, "antifouling", c => new Replace(c)));
    }

    [Fact]
    public void The_trip_plan_uses_only_the_growth_the_player_has_seen()
    {
        var g = Afloat(100, Wind(3, "northeast"));
        double clean = Plan(g).MotorHours;
        Assert.Equal(2.8, clean);

        g.FoulingGrowth = 0.8; // she has fouled, and the player has not looked
        Assert.Equal(clean, Plan(g).MotorHours);

        Ok(g.Execute(new Look()));
        double seen = Plan(g).MotorHours;
        Assert.Equal(Math.Round(14 / (5 - 1.2 * 0.8), 1), seen);
        Assert.Contains("the growth you saw on her hull costs her about", Plan(g).Estimate);

        g.FoulingGrowth = 1.0; // worse, unseen
        Assert.Equal(seen, Plan(g).MotorHours);
        Ok(g.Execute(new Look()));
        Assert.True(Plan(g).MotorHours > seen);
    }

    // ------------------------------------------------------------------ the mooring

    [Fact]
    public void Chafe_accrues_on_gale_days_afloat_in_port_only()
    {
        var calm = Afloat(260);
        double start = calm.MooringChafe;
        Assert.Equal(0.4, start); // the previous owner's lines (design note 008, second pass)
        WaitTo(calm, 266);
        Assert.Equal(start, calm.MooringChafe);

        var gale = Afloat(260, GalesOn(261)); // one force-8 day
        WaitTo(gale, 264);
        Assert.InRange(gale.MooringChafe, 0.4 + 0.09 * 0.6, 0.4 + 0.09 * 1.6);

        // On the hard the same gale does nothing to the lines.
        var hard = OnTheHard(260, GalesOn(261));
        WaitTo(hard, 264);
        Assert.Equal(0.4, hard.MooringChafe);
    }

    [Fact]
    public void A_gale_at_sea_does_not_chafe_the_mooring()
    {
        // Calm on the first day, a gale on the second; she is hove-to at sea through the midnight of the gale.
        var g = Afloat(260, GalesOn(261));
        Depart(g);
        Ok(g.Execute(new HeaveTo()));
        for (int i = 0; i < 40 && Status(g).Day < 262; i++) Ok(g.Execute(new Continue()));
        Assert.Equal(262, Status(g).Day);
        Assert.False(Status(g).Sunk);
        Assert.True(Status(g).AtSea);
        Assert.Equal(0.4, g.MooringChafe);
    }

    [Fact]
    public void Stronger_gales_chafe_more()
    {
        double After(int force)
        {
            var g = Afloat(260, BlowOn(force, 261));
            WaitTo(g, 263);
            return g.MooringChafe;
        }
        // Same day, same seed, same spread: the force alone sets the amount (force 7 adds 0.05, 8 adds 0.09).
        double f7 = After(7) - 0.4, f8 = After(8) - 0.4;
        Assert.Equal(0.09 / 0.05, f8 / f7, 6);
    }

    [Fact]
    public void Inspect_mooring_lines_reads_the_chafe_in_words()
    {
        var g = Afloat(260);
        string Words() => string.Join(" ", Ok(Walk.Do(g, "mooring lines", c => new Inspect(c))).Narrative);
        g.MooringChafe = 0.1;
        Assert.Contains("The lines are sound", Words());
        g.MooringChafe = 0.4;
        Assert.Contains("A little fuzz where the lines run through the fairleads", Words());
        g.MooringChafe = 0.6;
        Assert.Contains("badly chafed", Words());
        g.MooringChafe = 0.9;
        Assert.Contains("chafed almost through", Words());
    }

    [Fact]
    public void A_chafed_mooring_parts_in_a_gale_and_the_bill_may_take_her_below_zero()
    {
        var g = Afloat(260, GalesOn(261), boat: BoatWithBalance(4000));
        g.MooringChafe = 0.95; // a force-8 gale adds at least 0.054
        int spentBefore = Status(g).MoneySpentNok;
        var r = Ok(g.Execute(new WaitDays(2)));

        Assert.Equal(0, g.MooringChafe);
        Assert.Contains(r.Alerts, a => a.Text.StartsWith("The harbourmaster rang: she broke her mooring in the night's gale", StringComparison.Ordinal));
        Assert.Equal(spentBefore + 6000 + 4000 + 600, Status(g).MoneySpentNok);
        Assert.True(Status(g).BalanceNok < 0);
        var bill = Assert.Single(Log(g).Entries, e => e.Text.StartsWith("Her mooring parted in the gale.", StringComparison.Ordinal));
        Assert.Equal(10600, bill.CostNok);
        Assert.False(Status(g).Sunk);
    }

    [Fact]
    public void New_lines_replace_the_old_at_the_yard_chandlery_only()
    {
        var g = Afloat(260, boat: null);
        g.MooringChafe = 0.8;
        int spent = Status(g).MoneySpentNok;
        var r = Ok(Walk.Do(g, "mooring lines", c => new Replace(c)));
        Assert.Equal(600, r.CostNok);
        Assert.Equal(30, r.MinutesTaken);
        Assert.Equal(0, g.MooringChafe);
        Assert.Equal(spent + 600, Status(g).MoneySpentNok);

        // The harbour shop at Hestholmen does not stock them.
        var away = Afloat(260, Wind(3, "northeast"), newImpeller: true);
        Walk.ToPlace(away, "on-deck");
        Depart(away);
        Motor(away);
        var refused = Walk.Do(away, "mooring lines", c => new Replace(c));
        Assert.False(refused.Accepted);
        Assert.StartsWith("The harbour shop doesn't stock the mooring lines.", Assert.Single(refused.Narrative));
    }

    [Fact]
    public void Coming_back_after_a_blow_the_lines_creak_when_they_have_chafed_past_the_warning()
    {
        var g = Afloat(260, GalesOn(261));
        g.MooringChafe = 0.4;
        var r = Ok(g.Execute(new WaitDays(2)));
        Assert.Contains(r.Alerts, a => a.Text == "After the blow the bow lines creak and saw at the fairleads. Worth a look on deck.");

        // Calm days after that add nothing: no new alert.
        var quiet = Afloat(260);
        quiet.MooringChafe = 0.6;
        Assert.DoesNotContain(Ok(quiet.Execute(new WaitDays(2))).Alerts, a => a.Text.Contains("bow lines creak"));

        // A gale that leaves them under the warning level is not announced.
        var mild = Afloat(260, BlowOn(7, 261));
        mild.MooringChafe = 0.0;
        Assert.DoesNotContain(Ok(mild.Execute(new WaitDays(2))).Alerts, a => a.Text.Contains("bow lines creak"));

        // Aboard through the gale: she was there to see it, no alert after.
        var aboard = Afloat(260, GalesOn(261));
        aboard.MooringChafe = 0.4;
        while (Status(aboard).Day < 262) Ok(Walk.Do(aboard, "start battery", c => new Charge(c)));
        Assert.DoesNotContain(Ok(aboard.Execute(new WaitDays(1))).Alerts, a => a.Text.Contains("bow lines creak"));
    }

    // ------------------------------------------------------------------ the new ports

    [Fact]
    public void The_ports_and_routes_are_in_the_data_and_the_default_routes_are_unchanged()
    {
        var trips = BoatLoader.LoadSloop28().Trips!;
        Assert.Equal(["skjaervik", "hestholmen", "storholmen", "kvitnes"], trips.Ports.Select(p => p.Id));
        Assert.True(trips.FindPort("skjaervik")!.Fuel);
        Assert.True(trips.FindPort("storholmen")!.Fuel);
        Assert.False(trips.FindPort("hestholmen")!.Fuel);
        Assert.False(trips.FindPort("kvitnes")!.Shop);
        Assert.True(trips.FindPort("storholmen")!.Shop);

        Assert.Equal("hestholmen", trips.RoutesFrom("skjaervik").First().To);
        Assert.Equal("skjaervik", trips.RoutesFrom("hestholmen").First().To);
        var storholmen = trips.RoutesFrom("skjaervik").Single(r => r.To == "storholmen");
        Assert.Equal((26.0, 300), (storholmen.DistanceNm, storholmen.CourseDeg));
        Assert.Equal(("Måkeskjær", 12.0), (storholmen.Shelter.Single().Name, storholmen.Shelter.Single().AtNm));
        var kvitnes = trips.RoutesFrom("hestholmen").Single(r => r.To == "kvitnes");
        Assert.Equal((9.0, 120), (kvitnes.DistanceNm, kvitnes.CourseDeg));
        Assert.Equal("hestholmen", trips.RoutesFrom("kvitnes").Single().To);
    }

    [Fact]
    public void The_trip_query_lists_the_routes_out_of_each_port()
    {
        var g = Afloat(100, Wind(3, "northeast"), newImpeller: true);
        Assert.Equal(["Hestholmen, 14 nm", "Storholmen, 26 nm"], Trip(g).Destinations);
        Assert.Equal("Hestholmen", Plan(g).To); // no destination: the same default as before
        Depart(g);
        Assert.Null(Trip(g).Destinations);
        Motor(g);
        Assert.Equal(["Skjærvik, 14 nm", "Kvitnes, 9 nm"], Trip(g).Destinations);
        Assert.Equal("Skjærvik", Plan(g).To);
    }

    [Fact]
    public void Depart_to_Storholmen_takes_the_long_passage_and_arrives_with_its_own_lines()
    {
        var g = Afloat(100, Wind(3, "northeast"), newImpeller: true);
        var plan = Plan(g, "Storholmen");
        Assert.Equal((26.0, 300), (plan.DistanceNm, plan.CourseDeg));
        Assert.Contains("Måkeskjær, 12 nm out", plan.Shelter);
        Depart(g, "Storholmen");
        var card = Motor(g);
        Assert.Equal("Storholmen", card.Destination);
        Assert.Contains(card.Lines, l => l.Contains("Storholmen is a low island"));
        Assert.Equal("Storholmen", Trip(g).Port);
        // The kiosk stocks the filter elements and the impeller at a markup, and no mooring lines.
        var shop = (InventoryView)g.Ask(new Inventory());
        Assert.Equal("the harbour kiosk", shop.Shop);
        Assert.Contains(shop.ForSale, l => l.Name == "impeller" && l.PriceNok == 455);
        Assert.DoesNotContain(shop.ForSale, l => l.Name == "mooring lines");
        Assert.Equal(["Skjærvik, 26 nm"], Trip(g).Destinations);
    }

    [Fact]
    public void Kvitnes_has_no_shop_at_all()
    {
        var g = Afloat(100, Wind(3, "northeast"), newImpeller: true);
        Ok(g.Execute(new Buy("impeller"))); // a spare, bought at home
        Depart(g);
        Motor(g);
        Ok(Walk.Start(g));
        var plan = Plan(g, "Kvitnes");
        Assert.Equal((9.0, 120), (plan.DistanceNm, plan.CourseDeg));
        Depart(g, "Kvitnes");
        var card = Motor(g);
        Assert.Equal("Kvitnes", card.Destination);
        Assert.Contains(card.Lines, l => l.StartsWith("Kvitnes is a bay", StringComparison.Ordinal));

        No(g.Execute(new Buy("fuses")), "There's no shop at Kvitnes.");
        var inv = (InventoryView)g.Ask(new Inventory());
        Assert.Null(inv.Shop);
        Assert.Empty(inv.ForSale);
        Assert.Contains(inv.Aboard, l => l.Name == "impeller");

        // The spare aboard is the only way to replace a part here; without one the shop's absence is the answer.
        var spareless = Afloat(100, Wind(3, "northeast"), newImpeller: true);
        Depart(spareless);
        Motor(spareless);
        Ok(Walk.Start(spareless));
        Depart(spareless, "Kvitnes");
        Motor(spareless);
        Walk.Near(spareless, "mooring lines");
        var refused = spareless.Execute(new Replace("mooring lines"));
        Assert.False(refused.Accepted);
        Assert.Equal("There's no shop at Kvitnes, and no spare mooring lines aboard.", Assert.Single(refused.Narrative));
        Assert.Equal(0, refused.CostNok);
        var option = ((ComponentActionsView)spareless.Ask(new ComponentActions("mooring lines"))).Options.Single(o => o.Verb == "replace");
        Assert.False(option.Available);
    }

    // ------------------------------------------------------------------ the season query and the status line

    [Fact]
    public void The_season_query_and_the_status_line_say_where_the_year_stands()
    {
        var g = OnTheHard(75);
        var spring = Season(g);
        Assert.Equal((75, 1, 75, "spring"), (spring.Day, spring.Year, spring.DayOfYear, spring.SeasonWord));
        Assert.Equal("Spring, year 1. She stands on the hard. Launch is set for day 96.", spring.Text);
        Assert.Equal(spring.Text, Status(g).Season);
        Assert.Equal((7000, 9000, 500, 9000), (spring.LiftNok, spring.StorageNok, spring.ChangeFeeNok, spring.EmergencyLiftNok));
        Assert.Equal(96, spring.LaunchDeadlineDay);
        Assert.False(spring.Afloat);
        Assert.Empty(spring.Tasks);
        Assert.False(spring.Listed);
        Assert.Null(spring.AskNok);
        Assert.Equal(0, spring.OpenOffers);
        Assert.False(spring.Kept);

        var summer = Afloat(180);
        Assert.Equal("Summer, year 1. No haulout booked. The yard lifts boats from day 255 to day 300; the first free slot is day 255.", Season(summer).Text);
        Book(summer, 258);
        Assert.Equal("Summer, year 1. Haulout booked for day 258, 78 days away.", Season(summer).Text);

        var autumn = Afloat(256);
        int free = Season(autumn).FirstFreeDay!.Value;
        Assert.Equal($"Autumn, year 1. No haulout booked. The first free slot is day {free}; the yard lifts until day 300.", Season(autumn).Text);
        Book(autumn, free);
        Assert.Equal($"Autumn, year 1. Haulout booked for day {free}, {free - 256} days away.", Season(autumn).Text);
        WaitTo(autumn, free);
        Assert.Equal($"Autumn, year 1. Haulout booked for today, day {free}.", Season(autumn).Text);
        Assert.Equal(Season(autumn).Text, Status(autumn).Season);
    }

    // ------------------------------------------------------------------ the words and the wire

    [Fact]
    public void The_new_commands_and_queries_are_in_the_catalogue_and_serialise_by_their_names()
    {
        var verbs = ((CommandList)new Game(BoatLoader.LoadSloop28()).Ask(new Commands())).Commands.Select(c => c.Verb).ToList();
        foreach (var verb in new[] { "book haulout", "haul out", "scrub", "yard wash", "season", "clean" })
            Assert.Contains(verb, verbs);

        foreach (var (command, name) in new (Command, string)[]
        {
            (new BookHaulout(270), "book-haulout"), (new HaulOut(), "haul-out"), (new Scrub(), "scrub"), (new WashHull(), "wash-hull"),
        })
        {
            var json = JsonSerializer.Serialize(command);
            Assert.Contains($"\"type\":\"{name}\"", json);
            Assert.Equal(command.GetType(), JsonSerializer.Deserialize<Command>(json)!.GetType());
        }
        Assert.Equal(270, ((BookHaulout)JsonSerializer.Deserialize<Command>("{\"type\":\"book-haulout\",\"Day\":270}")!).Day);
        Assert.Contains("\"type\":\"season\"", JsonSerializer.Serialize<Query>(new Season()));
        Assert.Contains("\"type\":\"season-view\"", JsonSerializer.Serialize<QueryResult>(new Game(BoatLoader.LoadSloop28()).Ask(new Season())));
    }

    [Fact]
    public void The_game_executes_and_answers_the_new_commands_through_the_facade_alone()
    {
        var g = OnTheHard(180);
        Assert.IsType<SeasonView>(g.Ask(new Season()));
        Assert.True(g.Execute(new BookHaulout(256)).Accepted);
        Assert.False(g.Execute(new HaulOut()).Accepted);
        Assert.False(g.Execute(new Scrub()).Accepted);
        Assert.True(g.Execute(new WashHull()).Accepted);
    }

    // ------------------------------------------------------------------ nothing leaks

    [Fact]
    public void The_season_views_and_the_lift_carry_no_ids_and_no_hidden_fault_names()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = Afloat(250, boat: boat);
        var seen = new List<string>();
        void Snap()
        {
            foreach (Query q in new Query[] { new Status(), new Season(), new Trip(), new PlanTrip(), new Chart(), new Chart("Hestholmen"), new Logbook(), new Notices(), new Inventory(), new Aboard() })
                seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(q)));
        }
        void Keep(CommandResult r) => seen.Add(JsonSerializer.Serialize(r));

        Snap();
        Book(g, 255);
        Keep(Ok(g.Execute(new Look())));
        Walk.ToPlace(g, "on-deck");
        Keep(Ok(g.Execute(new Inspect("mooring lines"))));
        Keep(Ok(g.Execute(new Scrub())));
        Snap();
        WaitTo(g, 255);
        Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Keep(Ok(g.Execute(new HaulOut())));
        Snap();
        Keep(Ok(Walk.Do(g, "hull", c => new Inspect(c))));
        Keep(Ok(g.Execute(new Clean("hull"))));
        Keep(Ok(g.Execute(new WashHull())));
        Snap();

        var json = string.Concat(seen);
        foreach (var f in boat.AllFaults)
        {
            Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
            if (!f.Discovered) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
        }
        foreach (var id in boat.Components.Select(c => c.Id).Where(id => id.Contains('-')).Concat(["at-the-bow", "hull-exterior"]))
            Assert.False(json.Contains(id, StringComparison.Ordinal), id);
        foreach (var id in boat.Places.Select(p => p.Id).Where(id => id.Contains('-')))
            Assert.False(json.Contains($"\"{id}\"", StringComparison.Ordinal), id);
    }
}
