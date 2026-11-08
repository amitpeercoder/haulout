using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The PlanTrip query (design note 007, step 1).</summary>
public class PlanTripTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static Game NewGame(int seed = Game.DefaultSeed, WeatherSeries? weather = null) =>
        new(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), seed, weather ?? WeatherSeries.Fair());

    private static TripPlanView Plan(Game g, string? dest = null, int? day = null) =>
        (TripPlanView)g.Ask(new PlanTrip(dest, day));

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    [Fact]
    public void The_route_out_of_Skjaervik_is_fourteen_miles_on_040()
    {
        var p = Plan(NewGame());
        Assert.Equal("Skjærvik", p.From);
        Assert.Equal("Hestholmen", p.To);
        Assert.Equal(14, p.DistanceNm);
        Assert.Equal(40, p.CourseDeg);
        Assert.Equal(2.8, p.MotorHours);
        Assert.Equal(3.5, p.SailHours);
        Assert.Equal("About 2.8 hours under engine at 5 knots; about 3.5 under sail if the forecast wind holds.", p.Estimate);
        Assert.Equal(["Lyngskjær, 7 nm out"], p.Shelter);
        Assert.Equal(6000, p.TowNok);
        Assert.Equal(75, p.DepartDay);
        Assert.Equal(2, p.Forecast.Count);
    }

    [Fact]
    public void Destination_resolves_by_alias_and_unknown_is_a_view_with_a_blocker()
    {
        var g = NewGame();
        Assert.Equal("Hestholmen", Plan(g, "the regatta").To);
        var bad = Plan(g, "Atlantis");
        Assert.Contains("There's no route from Skjærvik to 'Atlantis'.", bad.Blockers);
        Assert.Equal(0, bad.DistanceNm);
    }

    [Fact]
    public void Blockers_say_why_Depart_would_refuse()
    {
        var g = NewGame();
        Assert.Contains("She's still on the hard.", Plan(g).Blockers);
        Assert.Contains("The engine hasn't run yet this season.", Plan(g).Blockers);
    }

    [Fact]
    public void Readiness_lists_only_known_faults_and_their_spares()
    {
        var g = NewGame();
        Assert.Empty(Plan(g).Risks);

        Ok(Walk.Do(g, "impeller", n => new Uncover(n)));
        Ok(g.Execute(new Inspect("impeller")));
        var risk = Assert.Single(Plan(g).Risks, r => r.Component == "Raw-water impeller");
        Assert.Equal("Under load for hours the engine will run hot.", risk.AtSea);
        Assert.Equal("impeller", risk.Spare);
        Assert.False(risk.SpareAboard);
        Assert.Contains("impeller", Plan(g).SparesMissing);

        Ok(g.Execute(new Buy("impeller")));
        var after = Plan(g);
        Assert.True(after.Risks.Single(r => r.Component == "Raw-water impeller").SpareAboard);
        Assert.DoesNotContain("impeller", after.SparesMissing);
        Assert.Contains("impeller", after.SparesAboard);
    }

    [Fact]
    public void A_latent_fault_is_not_listed()
    {
        var g = NewGame();
        Assert.DoesNotContain(Plan(g).Risks, r => r.Problem == "Tired impeller");
    }

    [Fact]
    public void The_forecast_is_deterministic_per_seed()
    {
        var weather = WeatherSeries.Generate(11);
        for (int day = 75; day < 140; day += 3)
            Assert.Equal(
                JsonSerializer.Serialize<QueryResult>(Plan(NewGame(11, weather), null, day)),
                JsonSerializer.Serialize<QueryResult>(Plan(NewGame(11, weather), null, day)));
    }

    [Fact]
    public void The_forecast_is_off_by_at_most_one_force_and_exact_today()
    {
        for (int seed = 1; seed <= 12; seed++)
        {
            var weather = WeatherSeries.Generate(seed);
            var g = NewGame(seed, weather);
            for (int lead = 0; lead <= 3; lead++)
            {
                int day = 75 + lead;
                foreach (var f in Plan(g, null, day).Forecast.Where(f => f.Known))
                {
                    var real = weather[f.Day];
                    Assert.InRange(f.WindForce, Math.Max(0, real.WindForce - 1), Math.Min(9, real.WindForce + 1));
                    if (f.Day == 75)
                    {
                        Assert.Equal(real.WindForce, f.WindForce);
                        Assert.Equal(real.RainMm > 0, f.Rain);
                        Assert.Equal(WeatherWords.Describe(real), f.Text);
                    }
                }
            }
        }
    }

    [Fact]
    public void The_forecast_is_unknown_beyond_its_reach_and_asking_does_not_disturb_the_game()
    {
        var weather = WeatherSeries.Generate(5);
        var asked = NewGame(5, weather);
        var quiet = NewGame(5, weather);
        var far = Plan(asked, null, 75 + 10);
        Assert.All(far.Forecast, f => Assert.False(f.Known));
        Assert.Equal("No forecast that far out.", far.Forecast[0].Text);
        Assert.Contains("no forecast", far.Estimate);
        Assert.Null(far.SailHours);

        // Asking costs nothing and draws nothing: both games go on the same.
        Assert.Equal(
            JsonSerializer.Serialize(Walk.Do(asked, "impeller", n => new Replace(n))),
            JsonSerializer.Serialize(Walk.Do(quiet, "impeller", n => new Replace(n))));
    }

    [Fact]
    public void The_wind_serves_by_force_and_angle()
    {
        var rules = SeaRules.Default;
        Assert.True(Sea.Serves(rules, 4, "southwest", 40));
        Assert.False(Sea.Serves(rules, 4, "northeast", 40));   // on the nose
        Assert.False(Sea.Serves(rules, 2, "southwest", 40));   // too light
        Assert.False(Sea.Serves(rules, 6, "southwest", 40));   // too strong
        Assert.Equal(225, Sea.Degrees("southwest"));
        Assert.Equal(5, Sea.WindOnBoat(45, 40));
        var back = BoatLoader.LoadSloop28().Trips!.RoutesFrom("hestholmen").Single();
        Assert.Equal(220, back.CourseDeg);
        Assert.Equal(7, back.Shelter.Single().AtNm);
    }

    [Fact]
    public void The_plan_and_inventory_views_leak_no_fault_ids_or_latent_names()
    {
        var g = NewGame();
        Ok(Walk.Do(g, "impeller", n => new Uncover(n)));
        Ok(g.Execute(new Inspect("impeller")));
        var json = JsonSerializer.Serialize<QueryResult>(Plan(g)) + JsonSerializer.Serialize<QueryResult>(g.Ask(new Inventory()));
        var known = new HashSet<string> { "impeller-tired" };
        foreach (var f in BoatLoader.LoadSloop28().AllFaults)
        {
            Assert.DoesNotContain(f.Id, json);
            if (!known.Contains(f.Id)) Assert.DoesNotContain(f.Name, json);
        }
    }
}
