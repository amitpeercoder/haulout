using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The chandlery and what is aboard (design note 007, step 1).</summary>
public class InventoryTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static Game NewGame() => new(BoatLoader.LoadSloop28(), 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Fair());

    private static CommandResult Ok(CommandResult r)
    {
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        return r;
    }

    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static InventoryView Inv(Game g) => (InventoryView)g.Ask(new Inventory());

    [Fact]
    public void Buying_a_spare_costs_the_price_and_spares_stack()
    {
        var g = NewGame();
        var first = Ok(g.Execute(new Buy("impeller")));
        Assert.Equal(350, first.CostNok);
        Assert.Equal(15, first.MinutesTaken);
        Assert.Contains("You walk over to the yard chandlery and buy an impeller.", first.Narrative);
        Ok(g.Execute(new Buy("a spare impeller")));
        Assert.Equal(700, Status(g).MoneySpentNok);
        var line = Assert.Single(Inv(g).Aboard);
        Assert.Equal(new InventoryLine("impeller", 2, false), line);
        Assert.Empty(Status(g).Tools);
    }

    [Fact]
    public void A_part_the_chandlery_sells_costs_the_chandlery_price_to_replace_and_in_the_DIY_route()
    {
        // Decision 007/1: one price per part, the chandlery's.
        var boat = BoatLoader.LoadSloop28();
        foreach (var item in boat.Chandlery.Where(i => i.ForComponent is not null))
        {
            var c = boat.Get(item.ForComponent!);
            Assert.Equal(item.PriceNok, c.ReplaceCostNok);
            foreach (var f in c.Faults.Where(f => f.FixedBy.Contains(Haulout.Sim.Faults.PlayerAction.Replace) && f.Diy is not null))
                Assert.Equal(item.PriceNok, f.Diy!.PartsNok);
        }
        Assert.Equal(220, boat.Get("alternator-belt").ReplaceCostNok);
        Assert.Equal(160, boat.Get("secondary-filter").ReplaceCostNok);
        Assert.Equal(300, boat.Get("engine-oil").ReplaceCostNok);

        var g = NewGame();
        var belt = Ok(Walk.Do(g, "alternator belt", c => new Replace(c)));
        Assert.Equal(220, belt.CostNok);
    }

    [Fact]
    public void The_loader_refuses_a_second_price_for_a_part_the_chandlery_sells()
    {
        var json = ReadSloop28Json();
        var broken = json.Replace(
            "\"id\": \"alternator-belt\", \"name\": \"alternator belt\", \"aliases\": [\"belt\", \"fan belt\", \"spare belt\"], \"priceNok\": 220,",
            "\"id\": \"alternator-belt\", \"name\": \"alternator belt\", \"aliases\": [\"belt\", \"fan belt\", \"spare belt\"], \"priceNok\": 225,");
        Assert.NotEqual(json, broken);
        BoatLoader.Load(json);
        // Price changed on the shop item only: still one source, loads fine and follows it.
        Assert.Equal(225, BoatLoader.Load(broken).Get("alternator-belt").ReplaceCostNok);

        var twoPrices = json.Replace("\"id\": \"impeller\",\n", "\"id\": \"impeller\",\n      \"replaceCostNok\": 999,\n");
        Assert.NotEqual(json, twoPrices);
        var ex = Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(twoPrices));
        Assert.Contains("impeller", ex.Message);
    }

    private static string ReadSloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_tool_twice_is_refused_free()
    {
        var g = NewGame();
        Ok(g.Execute(new Buy("remote start button")));
        var again = g.Execute(new Buy("the remote start"));
        Assert.False(again.Accepted);
        Assert.Contains("You already have one.", again.Narrative);
        Assert.Equal(400, Status(g).MoneySpentNok);
        Assert.Equal(["remote start button"], Status(g).Tools);
    }

    [Fact]
    public void An_ambiguous_or_unknown_item_is_refused()
    {
        var g = NewGame();
        Assert.False(g.Execute(new Buy("filter")).Accepted);
        Assert.Contains("doesn't have", string.Join(" ", g.Execute(new Buy("sextant")).Narrative));
        Assert.Equal(0, Status(g).MoneySpentNok);
    }

    [Fact]
    public void Replace_with_a_spare_aboard_uses_it_and_costs_nothing()
    {
        var g = NewGame();
        Ok(g.Execute(new Buy("impeller")));
        int spent = Status(g).MoneySpentNok;
        var r = Ok(Walk.Do(g, "impeller", n => new Replace(n)));
        Assert.Equal(0, r.CostNok);
        Assert.Contains("spares aboard", string.Join(" ", r.Narrative));
        Assert.Equal(spent, Status(g).MoneySpentNok);
        Assert.Empty(Inv(g).Aboard);
    }

    [Fact]
    public void Replace_without_a_spare_ashore_pays_as_before()
    {
        var g = NewGame();
        var r = Ok(Walk.Do(g, "impeller", n => new Replace(n)));
        Assert.Equal(350, r.CostNok);
        Assert.Equal(350, Status(g).MoneySpentNok);
    }

    [Fact]
    public void The_replace_option_shows_cost_zero_when_a_spare_is_aboard()
    {
        var g = NewGame();
        Walk.Near(g, "impeller");
        Assert.Equal(350, Option(g).CostNok);
        Ok(g.Execute(new Buy("impeller")));
        Assert.Equal(0, Option(g).CostNok);

        static ActionOption Option(Game g) =>
            ((ComponentActionsView)g.Ask(new ComponentActions("impeller"))).Options.Single(o => o.Verb == "replace");
    }

    [Fact]
    public void The_remote_start_button_still_unlocks_measuring()
    {
        var g = NewGame();
        Assert.False(g.Execute(new Measure("negative terminal")).Accepted);
        Ok(g.Execute(new Buy("remote start button")));
        var m = Ok(Walk.Do(g, "negative terminal", n => new Measure(n)));
        Assert.Equal("volts-drop", Assert.IsType<MeterReading>(m.Observation).Kind);
    }

    [Fact]
    public void The_inventory_lists_what_is_aboard_and_what_is_for_sale()
    {
        var g = NewGame();
        var empty = Inv(g);
        Assert.Empty(empty.Aboard);
        Assert.Equal("the yard chandlery", empty.Shop);
        Assert.Equal(9, empty.ForSale.Count);
        Assert.Contains(new ShopLine("impeller", 350, 15), empty.ForSale);

        Ok(g.Execute(new Buy("belt")));
        Ok(g.Execute(new Buy("button")));
        var full = Inv(g);
        Assert.Contains(new InventoryLine("alternator belt", 1, false), full.Aboard);
        Assert.Contains(new InventoryLine("remote start button", 1, true), full.Aboard);
        Assert.Equal(9, full.ForSale.Count);
    }

    [Fact]
    public void Status_names_the_port_and_the_yard_is_where_the_yard_is()
    {
        var g = NewGame();
        var s = Status(g);
        Assert.Equal("Skjærvik", s.Port);
        Assert.False(s.AtSea);
    }

    [Fact]
    public void Inventory_and_catalogue_round_trip_through_json()
    {
        Assert.IsType<Inventory>(JsonSerializer.Deserialize<Query>("""{"type":"inventory"}"""));
        var plan = Assert.IsType<PlanTrip>(JsonSerializer.Deserialize<Query>("""{"type":"plan-trip","Destination":"hestholmen"}"""));
        Assert.Equal("hestholmen", plan.Destination);
        var g = NewGame();
        Assert.Contains("\"type\":\"inventory-view\"", JsonSerializer.Serialize<QueryResult>(g.Ask(new Inventory())));
        var catalogue = (CommandList)g.Ask(new Commands());
        Assert.Contains(catalogue.Commands, c => c.Verb == "inventory");
        Assert.Contains(catalogue.Commands, c => c.Verb == "plan trip" && c.Argument == "destination");
        Assert.Contains(catalogue.Commands, c => c.Verb == "buy" && c.Description == "Buy a tool or a spare at the chandlery");
    }

    [Fact]
    public void The_inventory_view_carries_no_fault_ids()
    {
        var g = NewGame();
        Ok(g.Execute(new Buy("impeller")));
        var json = JsonSerializer.Serialize<QueryResult>(g.Ask(new Inventory()));
        foreach (var f in BoatLoader.LoadSloop28().AllFaults)
        {
            Assert.DoesNotContain(f.Id, json);
            Assert.DoesNotContain(f.Name, json);
        }
    }
}
