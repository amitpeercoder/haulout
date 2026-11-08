using Haulout.Sim.Play;
using Haulout.Sim.Weather;
using WeatherQuery = Haulout.Sim.Play.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The weather wired into the game (design note 006, step 2): the query, the header, waiting, the gates and the rates.</summary>
public class WeatherGameTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static readonly WeatherDay Rainy = new(0, 3, "southwest", 7, 6, 3);
    private static readonly WeatherDay Dry = new(0, 3, "southwest", 0, 6, 3);
    private static readonly WeatherDay Frosty = new(0, 3, "southwest", 0, 0, -3);
    private static WeatherDay Gale(int force, string from = "west") => new(0, force, from, 0, 6, 3);

    private static Game NewGame(WeatherSeries weather, int seed = Game.DefaultSeed) =>
        new(Data.BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), seed, weather);

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());
    private static ReadinessView Ready(Game g) => (ReadinessView)g.Ask(new Readiness());

    private static void AssertFreeAndUnlogged(Game g, CommandResult refused, int logCount, int minutes)
    {
        Assert.False(refused.Accepted);
        Assert.Equal(0, refused.MinutesTaken);
        Assert.Equal(0, refused.CostNok);
        Assert.Equal(logCount, Log(g).Entries.Count);
        Assert.Equal(minutes, Status(g).MinutesSpent);
    }

    // ------------------------------------------------------------------ the query, the header

    [Fact]
    public void The_weather_query_is_deterministic_for_a_seed_and_matches_the_series()
    {
        var series = WeatherSeries.Generate(4);
        var a = (WeatherView)NewGame(series).Ask(new WeatherQuery());
        var b = (WeatherView)Game.NewFirstFault(4).Ask(new WeatherQuery());
        Assert.Equal(a, b);

        var day = series[75];
        Assert.Equal(75, a.Day);
        Assert.Equal(WeatherWords.Describe(day), a.Text);
        Assert.Equal(day.WindForce, a.WindForce);
        Assert.Equal(day.WindFrom, a.WindFrom);
        Assert.Equal(day.RainMm, a.RainMm);
        Assert.Equal(day.TempC, a.TempC);
    }

    [Fact]
    public void The_weather_follows_the_game_day_and_costs_nothing()
    {
        var series = WeatherSeries.From(d => d == 75 ? Dry : Rainy);
        var g = NewGame(series);
        var before = Status(g).MinutesSpent;
        Assert.Equal(0, ((WeatherView)g.Ask(new WeatherQuery())).RainMm);
        Assert.Equal(before, Status(g).MinutesSpent);

        Ok(g.Execute(new WaitDays(1)));
        var view = (WeatherView)g.Ask(new WeatherQuery());
        Assert.Equal(76, view.Day);
        Assert.Equal(7, view.RainMm);
    }

    [Fact]
    public void The_logbook_header_and_the_status_carry_todays_weather()
    {
        var g = NewGame(WeatherSeries.Constant(Rainy));
        var text = WeatherWords.Describe(Rainy with { Day = 75 });
        Assert.Equal(text, Status(g).Weather);
        Assert.Equal(text, Log(g).Weather);
        Assert.Equal(75, Log(g).Day);
    }

    [Fact]
    public void The_command_catalogue_lists_weather()
    {
        var list = (CommandList)NewGame(WeatherSeries.Fair()).Ask(new Commands());
        Assert.Contains(list.Commands, c => c.Verb == "weather" && c.Description == "Look at the sky and the wind today");
    }

    // ------------------------------------------------------------------ waiting

    [Fact]
    public void Waiting_summarises_the_days_left_behind_not_counting_the_arrival_day()
    {
        // Days 75..80 rain, 81 and 82 dry; she is back on day 83 (dry), which is not part of the summary.
        var series = WeatherSeries.From(d => d <= 80 ? Rainy : Dry);
        var g = NewGame(series);
        var r = Ok(g.Execute(new WaitDays(8)));
        Assert.Equal("You leave her for 8 days.", r.Narrative[0]);
        Assert.Equal("Six days of rain, then two dry ones.", r.Narrative[1]);
        Assert.Equal(83, Status(g).Day);
    }

    [Fact]
    public void Waiting_through_a_gale_says_so()
    {
        var g = NewGame(WeatherSeries.Constant(Gale(8)));
        Assert.Contains("gale", Text(Ok(g.Execute(new WaitDays(2)))));
    }

    // ------------------------------------------------------------------ gates

    [Fact]
    public void Antifouling_is_refused_in_the_rain_for_free_and_works_on_a_dry_day()
    {
        var g = NewGame(WeatherSeries.From(d => d == 75 ? Rainy with { RainMm = 9 } : Dry));
        Walk.ToPlace(g, "beside-the-hull");
        Ok(g.Execute(new Look()));
        int log = Log(g).Entries.Count, minutes = Status(g).MinutesSpent;

        var refused = g.Execute(new Replace("antifouling"));
        AssertFreeAndUnlogged(g, refused, log, minutes);
        Assert.Contains("It's raining (heavy rain, 6 °C)", Text(refused));
        Assert.Contains("Wait for a dry day", Text(refused));

        var actions = (ComponentActionsView)g.Ask(new ComponentActions("antifouling"));
        var option = actions.Options.Single(o => o.Verb == "replace");
        Assert.False(option.Available);
        Assert.Contains("raining", option.UnavailableReason);

        Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "beside-the-hull");
        var done = Ok(g.Execute(new Replace("antifouling")));
        Assert.Equal(1600, done.CostNok);
    }

    [Fact]
    public void The_antifouling_diy_route_in_readiness_is_unavailable_in_the_rain_but_the_yard_is_not()
    {
        ReadinessItem Thin(Game g)
        {
            Walk.ToPlace(g, "beside-the-hull");
            Ok(g.Execute(new Look()));
            Ok(g.Execute(new Inspect("antifouling")));
            return Ready(g).Open.Single(o => o.Problem == "Thin antifouling");
        }

        var wet = Thin(NewGame(WeatherSeries.Constant(Rainy)));
        var diyWet = wet.Routes.Single(r => r.Route == "DIY");
        Assert.False(diyWet.Available);
        Assert.Contains("raining", diyWet.UnavailableReason);
        Assert.True(wet.Routes.Single(r => r.Route == "Yard").Available);

        var diyDry = Thin(NewGame(WeatherSeries.Fair())).Routes.Single(r => r.Route == "DIY");
        Assert.True(diyDry.Available);
    }

    [Fact]
    public void A_near_gale_keeps_you_off_the_deck_and_a_light_wind_does_not()
    {
        var g = NewGame(WeatherSeries.Constant(Gale(7)));
        int log = Log(g).Entries.Count, minutes = Status(g).MinutesSpent;
        var refused = g.Execute(new GoTo("on deck"));
        AssertFreeAndUnlogged(g, refused, log, minutes);
        Assert.Contains("It's blowing a near gale from the west (force 7): not a day to be up on deck.", Text(refused));
        Assert.Equal("Cockpit", ((AboardView)g.Ask(new Aboard())).Place);
        // Other places are not exposed.
        Ok(g.Execute(new GoTo("companionway")));

        var calm = NewGame(WeatherSeries.Constant(Gale(6)));
        Ok(calm.Execute(new GoTo("on deck")));
    }

    [Fact]
    public void The_yard_will_not_launch_in_a_gale_but_does_on_a_good_day()
    {
        var g = NewGame(WeatherSeries.From(d => d == 75 ? Gale(8, "southwest") : Dry));
        int log = Log(g).Entries.Count, minutes = Status(g).MinutesSpent;
        var refused = g.Execute(new Launch());
        AssertFreeAndUnlogged(g, refused, log, minutes);
        Assert.Contains("The yard won't launch her in this: a gale from the southwest (force 8). Try another day.", Text(refused));
        Assert.False(Status(g).Afloat);

        Ok(g.Execute(new WaitDays(1)));
        Ok(g.Execute(new Launch()));
        Assert.True(Status(g).Afloat);
    }

    [Fact]
    public void The_gate_numbers_come_from_the_data()
    {
        var gates = Data.BoatLoader.LoadSloop28().Rates.WeatherGates;
        Assert.Equal(7, gates.WindForce);
        Assert.Equal(WeatherWords.RainThresholdMm, gates.RainMm);
        Assert.Contains("weatherGates", File.ReadAllText(Path.Combine(RepoRoot(), "src/Haulout.Sim/Data/sloop28.json")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Haulout.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Haulout.sln not found.");
    }

    // ------------------------------------------------------------------ rates

    private static int? DayItSeizes(Game g, int maxDays = 90)
    {
        Walk.ToPlace(g, "saloon");
        Ok(g.Execute(new Remove("saloon floorboards")));
        Ok(g.Execute(new Inspect("seacock")));
        for (int i = 0; i < maxDays; i++)
        {
            Ok(g.Execute(new Look()));
            Ok(g.Execute(new WaitDays(1)));
            if (g.HasEscalated("seacock-weeping")) return Status(g).Day; // read from the sim: a wait finds nothing out
        }
        return null;
    }

    /// <summary>Back to the seacock: the finding writes the cause line.</summary>
    private static void LookAtSeacock(Game g) => Ok(Walk.Do(g, "seacock", n => new Inspect(n)));

    [Fact]
    public void Rain_and_frost_make_the_seacock_seize_sooner_than_fair_weather_with_the_same_seed()
    {
        var fair = DayItSeizes(NewGame(WeatherSeries.Fair()));
        var rain = DayItSeizes(NewGame(WeatherSeries.Constant(Rainy)));
        var frost = DayItSeizes(NewGame(WeatherSeries.Constant(Frosty)));
        Assert.NotNull(fair);
        Assert.NotNull(rain);
        Assert.NotNull(frost);
        Assert.True(rain < fair, $"rain {rain} vs fair {fair}");
        Assert.True(frost < fair, $"frost {frost} vs fair {fair}");
    }

    [Fact]
    public void The_cause_line_names_the_days_of_rain_and_the_nights_of_frost()
    {
        var g = NewGame(WeatherSeries.Constant(Rainy with { NightC = -2 }));
        Assert.NotNull(DayItSeizes(g));
        LookAtSeacock(g);
        var line = Log(g).Entries.Single(e => e.Text.StartsWith("The raw-water seacock has seized.", StringComparison.Ordinal)).Text;
        Assert.Contains("days of rain", line);
        Assert.Contains("nights of frost", line);

        var fair = NewGame(WeatherSeries.Fair());
        Assert.NotNull(DayItSeizes(fair));
        LookAtSeacock(fair);
        var dry = Log(fair).Entries.Single(e => e.Text.StartsWith("The raw-water seacock has seized.", StringComparison.Ordinal)).Text;
        Assert.DoesNotContain("rain", dry);
        Assert.DoesNotContain("frost", dry);
    }
}
