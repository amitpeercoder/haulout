using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The time driver and the rates (design note 006, step 1), played through the facade where possible.</summary>
public class TickTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string WeepingSeacock = "Weeping raw-water seacock";
    private const string SeizedSeacock = "Seized raw-water seacock";
    private static readonly Boat Data = BoatLoader.LoadSloop28();

    private static Game NewGame(int worldSeed = Game.DefaultSeed) =>
        new(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), worldSeed, WeatherSeries.Fair());

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static ReadinessView Ready(Game g) => (ReadinessView)g.Ask(new Readiness());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);

    /// <summary>The clock as one number: minutes since day 0, 00:00.</summary>
    private static int Clock(Game g)
    {
        var s = Status(g);
        var parts = s.TimeOfDay.Split(':');
        return s.Day * 1440 + int.Parse(parts[0], CultureInfo.InvariantCulture) * 60 + int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    /// <summary>Takes the floorboards off and finds the weeping seacock. The player ends in the saloon.</summary>
    private static void FindSeacock(Game g)
    {
        Walk.ToPlace(g, "saloon");
        Ok(g.Execute(new Remove("saloon floorboards")));
        Ok(g.Execute(new Inspect("seacock")));
    }

    /// <summary>
    /// Stay aboard every day (a look, then a day's wait) until the weeping seacock has seized; returns the day the wait that did it ended, or null.
    /// A wait is time away, so the player finds nothing out by it: the seizure is read from the sim's internals, never from the results.
    /// </summary>
    private static int? DayItSeizes(Game g, bool aboard, int maxDays = 60)
    {
        for (int i = 0; i < maxDays; i++)
        {
            if (aboard) Ok(g.Execute(new Look()));
            Ok(g.Execute(new WaitDays(1)));
            if (g.HasEscalated("seacock-weeping")) return Status(g).Day;
        }
        return null;
    }

    /// <summary>The player goes back to the seacock and looks at it: the one way to find out it has seized.</summary>
    private static CommandResult LookAtSeacock(Game g) => Ok(Walk.Do(g, "seacock", n => new Inspect(n)));

    // ------------------------------------------------------------------ one clock

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Haulout.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Haulout.sln not found above the test binaries.");
    }

    [Fact]
    public void Tick_is_the_only_time_path_Advance_is_called_nowhere_else()
    {
        var sim = Path.Combine(RepoRoot(), "src", "Haulout.Sim");
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(sim, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}")).ToList();
        Assert.NotEmpty(files);

        var callers = files.Where(f => File.ReadAllText(f).Contains(".Advance(", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToList();
        Assert.Equal(["Game.Tick.cs"], callers);
        Assert.Contains(files, f => Path.GetFileName(f) == "SimTime.cs" && File.ReadAllText(f).Contains("void Advance(", StringComparison.Ordinal));
        // No second clock in the facade.
        Assert.DoesNotContain(files.Where(f => Path.GetFileName(f).StartsWith("Game", StringComparison.Ordinal)),
            f => File.ReadAllText(f).Contains("_idleMinutes", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_accepted_command_moves_the_clock_by_its_minutes_and_a_refusal_moves_nothing()
    {
        var g = NewGame();
        int accepted = 0, refused = 0;

        void Run(Command c)
        {
            int before = Clock(g);
            var r = g.Execute(c);
            if (r.Accepted) { accepted++; Assert.Equal(before + r.MinutesTaken, Clock(g)); }
            else { refused++; Assert.Equal(0, r.MinutesTaken); Assert.Equal(before, Clock(g)); }
        }

        Run(new Look());
        Run(new GoTo("galaxy"));                // refused
        Run(new GoTo("companionway"));
        Run(new Inspect("steps"));              // refused: not found yet
        Run(new Remove("steps"));
        Run(new GoTo("quarter berth"));
        Run(new Remove("battery lid"));
        Run(new Inspect("negative terminal"));
        Run(new Uncover("negative terminal"));
        Run(new Measure("start battery"));
        Run(new Buy("remote start button"));
        Run(new Clean("negative terminal"));
        Run(new Charge("battery"));
        Run(new GoTo("cockpit"));               // refused: not adjacent
        Run(new GoTo("companionway"));
        Run(new GoTo("cockpit"));
        Run(new StartEngine());
        Run(new CheckRawWaterFlow());
        Run(new Defer("flux capacitor"));       // refused
        Run(new OrderSurvey());
        Run(new OrderSurvey());                 // refused: once only
        Run(new WaitDays(0));                   // refused
        Run(new WaitDays(3));
        Run(new Defer("seacock"));              // refused: not known yet
        FindSeacockQuietly(g, Run);
        Run(new Defer("seacock"));
        Run(new Undefer("seacock"));
        Run(new FixByYard("seacock"));
        Run(new WaitDays(2));
        Run(new Refit("hatch"));                // refused: not in the cockpit
        Run(new Launch());
        Run(new WaitDays(1));
        Run(new EmergencyLift());

        Assert.True(accepted > 20);
        Assert.True(refused >= 8);
    }

    private static void FindSeacockQuietly(Game g, Action<Command> run)
    {
        foreach (var place in new[] { "companionway", "saloon" }) run(new GoTo(place));
        run(new Remove("floorboards"));
        run(new Inspect("seacock"));
    }

    // ------------------------------------------------------------------ the spread

    [Fact]
    public void The_same_world_seed_seizes_the_seacock_on_the_same_day_and_other_seeds_differ()
    {
        int? Run(int seed)
        {
            var g = NewGame(seed);
            FindSeacock(g);
            return DayItSeizes(g, aboard: true);
        }

        var days = Enumerable.Range(0, 8).Select(Run).ToList();
        Assert.All(days, d => Assert.NotNull(d));
        Assert.Equal(days, Enumerable.Range(0, 8).Select(Run).ToList());
        Assert.True(days.Distinct().Count() >= 3, "the spread should move the day: " + string.Join(",", days));
    }

    [Fact]
    public void At_neutral_factors_the_median_seacock_seizes_in_about_twenty_days()
    {
        var weeping = Data.AllFaults.Single(f => f.Id == "seacock-weeping");
        var spec = weeping.Progression!;
        var rates = Data.Rates;
        var days = Enumerable.Range(0, 200).Select(seed =>
        {
            double spread = rates.SpreadLo + new SeededRandom(seed).NextDouble() * (rates.SpreadHi - rates.SpreadLo);
            return (int)Math.Ceiling(rates.Threshold / (spec.BaseRatePerDay * spread));
        }).OrderBy(d => d).ToList();

        double median = (days[99] + days[100]) / 2.0;
        Assert.InRange(median, 18, 22);
        Assert.True(days[0] < days[^1]);
    }

    [Fact]
    public void The_rates_load_from_the_data_and_a_boat_without_rates_gets_the_defaults()
    {
        Assert.Equal(Rates.Default, BoatLoader.Load("{\"name\":\"Bare\",\"places\":[],\"covers\":[],\"positions\":[],\"components\":[]}").Rates);
        var rates = Data.Rates;
        Assert.Equal([0.6, 1.6], [rates.SpreadLo, rates.SpreadHi]);
        Assert.Equal(1.0, rates.Threshold);
        Assert.Contains("companionway-hatch", rates.Neglect.Covers);
    }

    // ------------------------------------------------------------------ neglect

    [Fact]
    public void A_seacock_left_open_to_the_air_with_nobody_aboard_seizes_sooner_than_one_kept_shut_and_watched()
    {
        double neglected = 0, tended = 0;
        const int seeds = 6;
        for (int seed = 0; seed < seeds; seed++)
        {
            // Away: floorboards up, hatch open, the player waits.
            var away = NewGame(seed);
            FindSeacock(away);
            neglected += DayItSeizes(away, aboard: false)!.Value;

            // Tended: found, then everything shut, and the player is aboard every day.
            var kept = NewGame(seed);
            FindSeacock(kept);
            Ok(kept.Execute(new Refit("floorboards")));
            Walk.ToPlace(kept, "cockpit");
            Ok(kept.Execute(new Refit("hatch")));
            tended += DayItSeizes(kept, aboard: true)!.Value;
        }
        Assert.True(neglected / seeds < tended / seeds, $"neglected {neglected / seeds}, tended {tended / seeds}");
    }

    // ------------------------------------------------------------------ readiness range

    [Fact]
    public void Readiness_shows_a_range_for_the_discovered_seacock_none_for_a_latent_fault_and_the_day_falls_inside_it()
    {
        var g = NewGame();
        Assert.Empty(Ready(g).Open); // the latent seacock is not mentioned

        FindSeacock(g);
        var item = Ready(g).Open.Single(o => o.Problem == WeepingSeacock);
        Assert.NotNull(item.LikelyMinDays);
        Assert.NotNull(item.LikelyMaxDays);
        Assert.True(item.LikelyMinDays < item.LikelyMaxDays);
        int from = Status(g).Day;

        int day = DayItSeizes(g, aboard: true) ?? -1; // aboard each day: the factors stay constant
        Assert.InRange(day, from + item.LikelyMinDays!.Value, from + item.LikelyMaxDays!.Value);
    }

    [Fact]
    public void The_range_floor_holds_when_she_is_left_in_heavy_rain_and_frost_and_the_ceiling_holds_when_tended_in_fair_weather()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var wet = new Game(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), seed,
                WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 12, 1, -3)));
            FindSeacock(wet); // floorboards up, hatch open
            var wetItem = Ready(wet).Open.Single(o => o.Problem == WeepingSeacock);
            int wetFrom = Status(wet).Day;
            int wetDay = DayItSeizes(wet, aboard: false) ?? throw new Xunit.Sdk.XunitException($"seed {seed}: never seized");
            Assert.True(wetDay >= wetFrom + wetItem.LikelyMinDays!.Value,
                $"seed {seed}: seized on {wetDay}, range {wetItem.LikelyMinDays}-{wetItem.LikelyMaxDays} from {wetFrom}");
            Assert.True(wetItem.LikelyMinDays <= wetItem.LikelyMaxDays);

            var fair = NewGame(seed);
            FindSeacock(fair);
            var fairItem = Ready(fair).Open.Single(o => o.Problem == WeepingSeacock);
            int fairFrom = Status(fair).Day;
            int fairDay = DayItSeizes(fair, aboard: true) ?? throw new Xunit.Sdk.XunitException($"seed {seed}: never seized");
            Assert.InRange(fairDay, fairFrom + fairItem.LikelyMinDays!.Value, fairFrom + fairItem.LikelyMaxDays!.Value);
        }
    }

    [Fact]
    public void The_cause_line_names_the_real_length_of_the_absence()
    {
        var g = NewGame();
        FindSeacock(g);
        Assert.NotNull(DayItSeizes(g, aboard: false));
        LookAtSeacock(g);
        var line = Log(g).Entries.Single(e => e.Text.StartsWith("The raw-water seacock has seized.", StringComparison.Ordinal)).Text;
        Assert.DoesNotContain("while she sat", line);
        Assert.Matches(@"no one aboard for (\w+) days", line);
        int n = Array.IndexOf(new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" },
            System.Text.RegularExpressions.Regex.Match(line, @"no one aboard for (\w+) days").Groups[1].Value);
        Assert.True(n > 3);
    }

    [Fact]
    public void Covers_with_state_words_log_their_own_wording()
    {
        var g = NewGame();
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Refit("hatch")));
        Ok(g.Execute(new Remove("hatch")));
        Walk.ToPlace(g, "companionway");
        Ok(g.Execute(new Remove("fuse")));
        Ok(g.Execute(new Refit("fuse")));
        var texts = Log(g).Entries.Select(e => e.Text).ToList();
        Assert.Contains("Slid the companionway hatch shut.", texts);
        Assert.Contains("Slid the companionway hatch open.", texts);
        Assert.Contains("Pulled the bilge pump fuse.", texts);
        Assert.Contains("Put the bilge pump fuse back in.", texts);
    }

    // ------------------------------------------------------------------ the cause line

    [Fact]
    public void A_known_fault_names_its_causes_in_the_logbook_when_it_escalates()
    {
        var g = NewGame();
        FindSeacock(g); // floorboards up, hatch open
        Assert.NotNull(DayItSeizes(g, aboard: true));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.StartsWith("The raw-water seacock has seized.", StringComparison.Ordinal)); // not at midnight
        LookAtSeacock(g); // the cause line comes with the finding

        var line = Log(g).Entries.Single(e => e.Text.StartsWith("The raw-water seacock has seized.", StringComparison.Ordinal));
        Assert.Contains("It came on with the floorboards up and the hatch open", line.Text);
        Assert.Contains(Log(g).Entries, e => e.Text == $"Found: {SeizedSeacock}.");
        Assert.True(Log(g).Entries.ToList().IndexOf(line) < Log(g).Entries.ToList().FindIndex(e => e.Text == $"Found: {SeizedSeacock}."));
    }

    [Fact]
    public void A_latent_seacock_escalates_without_a_log_line_or_a_narrative()
    {
        var g = NewGame();
        var results = new List<CommandResult>();
        for (int i = 0; i < 45; i++) results.Add(Ok(g.Execute(new WaitDays(1))));

        Assert.All(results, r => Assert.DoesNotContain("seacock", Text(r), StringComparison.OrdinalIgnoreCase));
        Assert.All(results, r => Assert.Empty(r.NewlyDiscovered));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.Contains("came on", StringComparison.Ordinal) || e.Text.StartsWith("Found", StringComparison.Ordinal));

        // It did escalate: inspecting finds it seized.
        Walk.ToPlace(g, "saloon");
        Ok(g.Execute(new Remove("floorboards")));
        Assert.Contains("seized solid", Text(Ok(g.Execute(new Inspect("seacock")))));
    }

    // ------------------------------------------------------------------ the hatch

    [Fact]
    public void The_companionway_hatch_starts_open_is_described_in_its_own_words_and_slides_open_for_free_when_going_below()
    {
        var g = NewGame();
        var look = Ok(g.Execute(new Look()));
        Assert.Contains("The companionway hatch is open.", Text(look));

        var shut = Ok(g.Execute(new Refit("hatch")));
        Assert.Contains("shut", Text(shut));
        Assert.Contains("The companionway hatch is shut.", Text(Ok(g.Execute(new Look()))));
        Assert.Contains("already shut", Text(g.Execute(new Refit("hatch"))));

        int before = Clock(g);
        var below = Ok(g.Execute(new GoTo("companionway")));
        Assert.Contains("slide the hatch open", Text(below));
        Assert.Equal(before + 1, Clock(g)); // no extra time
        Walk.ToPlace(g, "cockpit");
        Assert.Contains("The companionway hatch is open.", Text(Ok(g.Execute(new Look()))));
    }

    // ------------------------------------------------------------------ the drain chain

    [Fact]
    public void Rusted_drain_clamps_progress_to_a_hose_off_its_tail()
    {
        var g = NewGame();
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Remove("locker lid")));
        Assert.Contains(Ok(g.Execute(new Inspect("drain hoses"))).NewlyDiscovered, k => k.Name == "Rusted cockpit drain hose clamps");

        for (int i = 0; i < 4 && !g.HasEscalated("drain-clamps-rusted"); i++) Ok(g.Execute(new WaitDays(60)));
        Assert.True(g.HasEscalated("drain-clamps-rusted"));
        // Away: nothing was found by waiting. The player finds the hose off by looking.
        Assert.DoesNotContain(Log(g).Entries, e => e.Text == "Found: Cockpit drain hose off its tail.");
        var found = Ok(Walk.Do(g, "drain hoses", n => new Inspect(n)));
        Assert.Contains(found.NewlyDiscovered, k => k.Name == "Cockpit drain hose off its tail");
        Assert.Contains(Log(g).Entries, e => e.Text.StartsWith("The cockpit drain hose has come off its tail.", StringComparison.Ordinal));
        Assert.Contains("Cockpit drain hose off its tail", Ready(g).Open.Select(o => o.Problem));
    }
}
