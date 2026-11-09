using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;

namespace Haulout.Sim.Tests.Play;

/// <summary>The broker's window and listing at any time (design note 012, step 5; design notes 011 and 008).</summary>
public class LadderTests
{
    private static CommandResult Ok(CommandResult r) => ClubGame.Ok(r);
    private static NoticeboardView Board(Game g) => ClubGame.Board(g);
    private static SaleView Sale(Game g) => (SaleView)g.Ask(new Sale());

    private static readonly string[] RealBrands =
        ["Beneteau", "Jeanneau", "Bavaria", "Hallberg", "Najad", "Hanse", "Dufour", "Moody", "Oyster", "Catalina", "Lagoon", "Leopard", "Fountaine", "Contessa", "Westerly", "Halberg", "Rassy", "Swan", "Island Packet", "Tayana", "Wauquiez", "Garcia", "Boreal", "Ovni", "Manta", "Catana", "Nautor", "Sabre", "Hunter"];

    private static LadderRules Rules() => ClubGame.Boat().Board.Ladder;

    private static string SloopJson(Func<string, string> edit) => edit(ClubGame.Sloop28Json());

    private static Game AfloatWith(Boat boat, int startDay, bool fixSeacock = true)
    {
        boat.Get("anchor-gear").Replace();
        boat.Get("anchor-shackle").Replace();
        var g = new Game(boat, startDay, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        if (fixSeacock)
        {
            Ok(Walk.Do(g, "seacock", c => new Replace(c)));
            Ok(Walk.Do(g, "impeller", c => new Replace(c)));
            Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
            Ok(Walk.Do(g, "navigation lights", c => new Replace(c)));
        }
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!ClubGame.Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    private static Boat Eager(Func<string, string>? more = null) =>
        BoatLoader.Load(SloopJson(j => (more ?? (x => x))(j.Replace("\"viewingChancePerDay\": 0.2", "\"viewingChancePerDay\": 5.0").Replace("\"findChance\": 0.85", "\"findChance\": 1.0"))),
            true, ClubGame.OpenShops(), withHours: true);

    // ------------------------------------------------------------------ the data

    [Fact]
    public void The_window_has_four_fictional_boats_in_order_of_price_with_sources_and_no_real_names()
    {
        var rules = Rules();
        Assert.Equal(4, rules.Boats.Count);
        Assert.Equal(rules.Boats.Select(b => b.AskingNok).Order().ToArray(), rules.Boats.Select(b => b.AskingNok).ToArray());
        Assert.Equal([32, 36, 40, 42], rules.Boats.Select(b => b.LengthFt).ToArray());
        Assert.Contains(rules.Placeholders, p => p.Contains("UNVERIFIED", StringComparison.Ordinal));
        Assert.Contains(rules.Placeholders, p => p.Contains("SOURCES", StringComparison.Ordinal));
        string all = JsonSerializer.Serialize(Board(ClubGame.New()).Ladder) + JsonSerializer.Serialize(rules.Boats);
        foreach (var brand in RealBrands) Assert.DoesNotContain(brand, all, StringComparison.OrdinalIgnoreCase);
        Assert.All(rules.Boats, b => Assert.Contains(b.Lies, ["lying at a broker's in the south", "lying at a broker's up the coast"]));
        Assert.All(rules.Boats, b => Assert.DoesNotContain("Kvitnes", b.Lies, StringComparison.Ordinal));
    }

    [Fact]
    public void The_insurers_ask_the_coastal_ticket_for_three_boats_and_the_offshore_one_for_the_bluewater_boat()
    {
        var tickets = Rules().Boats.Select(b => b.Ticket).ToArray();
        Assert.Equal(["coastal", "coastal", "offshore", "coastal"], tickets);
    }

    [Fact]
    public void The_first_rung_is_about_240000_over_what_the_players_own_boat_is_worth_with_her_balance()
    {
        var g = ClubGame.New();
        var window = Board(g).Ladder!;
        var cutter = window[1];
        int balance = ClubGame.Status(g).BalanceNok;
        int figure = window[0].PriceNok;
        Assert.InRange(cutter.PriceNok - balance - figure, 230000, 250000);
        Assert.Contains($"You are {cutter.PriceNok - balance - figure} NOK short", cutter.Shortfall[0]);
    }

    // ------------------------------------------------------------------ the cards

    [Fact]
    public void The_first_card_is_the_players_own_boat_with_the_brokers_figure_her_bills_and_her_ticket()
    {
        var g = ClubGame.New();
        var own = Board(g).Ladder![0];
        Assert.True(own.Yours);
        var sale = (SaleView)g.Ask(new Sale());
        Assert.Equal(sale.Letter!.ValueNok, own.PriceNok);
        Assert.Equal([("Berth", 12000), ("Insurance", 8000), ("Lift and storage", 16000)], own.Costs.Select(c => (c.Label, c.Nok)).ToArray());
        Assert.Equal(36000, own.CostsTotalNok);
        Assert.Contains("the boat licence, held", own.Line);
        Assert.Contains($"{own.PriceNok} NOK", own.Line);
        Assert.Empty(own.Shortfall);
    }

    [Fact]
    public void The_brokers_figure_on_the_card_is_only_what_the_player_knows_and_moves_with_the_letter()
    {
        var g = ClubGame.New();
        int before = Board(g).Ladder![0].PriceNok;
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Assert.Equal(((SaleView)g.Ask(new Sale())).Letter!.ValueNok, Board(g).Ladder![0].PriceNok);
        Assert.NotEqual(before, Board(g).Ladder![0].PriceNok); // a kept logbook entry is on the letter now
    }

    [Fact]
    public void Each_listing_has_the_quote_by_the_yard_a_total_and_the_shortfall_in_money_and_tickets_in_words()
    {
        var g = ClubGame.New();
        var cards = Board(g).Ladder!;
        Assert.Equal(5, cards.Count);
        Assert.Equal(cards.Skip(1).Select(c => c.PriceNok).Order().ToArray(), cards.Skip(1).Select(c => c.PriceNok).ToArray());
        foreach (var c in cards.Skip(1))
        {
            Assert.False(c.Yours);
            Assert.Equal(c.Costs.Sum(x => x.Nok), c.CostsTotalNok);
            Assert.Equal(2, c.Shortfall.Count);
            Assert.Contains("short", c.Shortfall[0], StringComparison.Ordinal);
            Assert.Contains("you don't", c.Shortfall[1], StringComparison.Ordinal);
            Assert.Contains("running costs a year", c.Line, StringComparison.Ordinal);
        }
        Assert.Contains("the coastal skipper ticket", cards[1].Shortfall[1]);
        Assert.Contains("the offshore skipper ticket", cards[3].Shortfall[1]);
        Assert.Contains("the coastal skipper ticket", cards[4].Shortfall[1]);
    }

    [Fact]
    public void The_catamarans_berth_is_about_twice_a_monohulls_of_her_length_and_the_bluewater_boat_costs_most_in_parts()
    {
        var cards = Board(ClubGame.New()).Ladder!;
        int Berth(LadderCard c) => c.Costs.Single(x => x.Label == "Berth").Nok;
        Assert.InRange(Berth(cards[4]) / (double)Berth(cards[3]), 1.8, 2.2);
        Assert.True(Berth(cards[0]) < Berth(cards[1]) && Berth(cards[1]) < Berth(cards[2]));
    }

    [Fact]
    public void With_the_money_none_is_for_sale_to_the_player_yet_and_the_ticket_line_follows_the_log()
    {
        string eco = ClubGame.OpenShops(n => n["start"]!["startingBalanceNok"] = 5_000_000);
        var g = ClubGame.New(economy: eco);
        var cards = Board(g).Ladder!;
        Assert.All(cards.Skip(1), c =>
        {
            Assert.DoesNotContain("short", c.Shortfall[0], StringComparison.Ordinal);
            Assert.Equal("You have the money for her, but she is not for sale to you yet: the broker deals only with a skipper who can insure her.", c.Shortfall[0]);
        });
        Assert.DoesNotContain("buy", string.Join(" ", cards.SelectMany(c => c.Shortfall)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_window_is_at_Skjaervik_only_and_nowhere_at_sea()
    {
        var g = ClubGame.Afloat(100);
        Assert.NotNull(Board(g).Ladder);
        Ok(g.Execute(new Depart("Hestholmen")));
        Assert.Null(Board(g).Ladder); // under way: no board
        ClubGame.MotorToArrival(g);
        Assert.Null(Board(g).Ladder);
        Assert.Contains("regatta", JsonSerializer.Serialize(Board(g).Cards), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void There_is_no_command_to_buy_a_listing_and_the_cards_carry_no_ids()
    {
        var g = ClubGame.New();
        string json = JsonSerializer.Serialize(Board(g).Ladder);
        var boat = ClubGame.Boat();
        foreach (var c in boat.Components) Assert.DoesNotContain($"\"{c.Id}\"", json, StringComparison.Ordinal);
        foreach (var f in boat.AllFaultsEver) Assert.DoesNotContain(f.Id, json, StringComparison.Ordinal);
        foreach (var id in new[] { "boat-licence", "coastal", "offshore" }) Assert.DoesNotContain($"\"{id}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(Command).Assembly.GetTypes(), t => typeof(Command).IsAssignableFrom(t) && t.Name.Contains("Ladder", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ listing at any time

    [Fact]
    public void She_can_be_listed_afloat_at_Skjaervik_in_June_and_not_from_Hestholmen_or_under_way()
    {
        var g = ClubGame.Afloat(160);
        var r = Ok(g.Execute(new ListForSale(300000)));
        Assert.Contains("pontoon", string.Join(" ", r.Narrative), StringComparison.Ordinal);
        Assert.True(Sale(g).Listed);
        Ok(Walk.Start(g)); // listing shut the engine down
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart("Hestholmen")));
        var atSea = g.Execute(new ListForSale(310000));
        Assert.False(atSea.Accepted);
        Assert.Equal("You're at sea: list her from Skjærvik or the yard.", Assert.Single(atSea.Narrative));
        ClubGame.MotorToArrival(g);
        var away = g.Execute(new ListForSale(310000));
        Assert.False(away.Accepted);
        Assert.Equal("The broker is at Skjærvik: bring her home to list her.", Assert.Single(away.Narrative));
        Assert.Equal(300000, Sale(g).AskNok);
    }

    [Fact]
    public void The_broker_keeps_the_offices_hours_for_a_listing_made_afloat()
    {
        var g = new Game(BoatLoader.Load(ClubGame.Sloop28Json(), true, null, withHours: true), 161, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        // Launch needs the yard office: do it in hours, then step to a Sunday and try.
        ClubGame.LookUntilAtLeast(g, "09:00");
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        for (int i = 0; i < 8 && new GameClock(ClubGame.Status(g).Day).WeekdayName != "Sunday"; i++) Ok(g.Execute(new WaitDays(1)));
        var shut = g.Execute(new ListForSale(300000));
        Assert.False(shut.Accepted);
        Assert.Equal("The yard office is shut on Sundays; it opens at nine tomorrow.", Assert.Single(shut.Narrative));
    }

    [Fact]
    public void Afloat_a_buyer_comes_only_at_the_weekend_and_only_when_she_is_home()
    {
        var g = AfloatWith(Eager(), 154); // day 154 is a Monday
        Assert.Equal("Monday", new GameClock(154).WeekdayName);
        Ok(g.Execute(new ListForSale(100000)));
        for (int i = 0; i < 21; i++) Ok(g.Execute(new WaitDays(1)));
        var days = Sale(g).Viewings.Select(v => v.Day).ToArray();
        Assert.NotEmpty(days);
        Assert.All(days, d => Assert.True(new GameClock(d).IsWeekend, $"a viewing on day {d}, a {new GameClock(d).WeekdayName}"));
        Assert.True(days.Length >= 4, $"{days.Length} weekend viewings in three weeks");
    }

    [Fact]
    public void A_day_she_is_away_on_a_passage_has_no_viewing_even_at_the_weekend()
    {
        var g = AfloatWith(Eager(), 159); // a Saturday
        Assert.Equal("Saturday", new GameClock(159).WeekdayName);
        Ok(g.Execute(new ListForSale(100000)));
        ClubGame.Sail(g, "Hestholmen");  // out and back on the Saturday: she is home at its midnight, but she was away that day
        ClubGame.Sail(g, "Skjærvik");
        Assert.Equal(159, ClubGame.Status(g).Day);
        Ok(g.Execute(new WaitDays(2)));
        Assert.DoesNotContain(Sale(g).Viewings, v => v.Day == 159);
        Assert.Contains(Sale(g).Viewings, v => v.Day == 160); // the Sunday, at home all day
    }

    [Fact]
    public void A_buyers_surveyor_afloat_finds_no_growth_and_no_fault_below_the_waterline()
    {
        string sharp(string j) => j.Replace("\"foulingFindFrom\": 0.2", "\"foulingFindFrom\": 0.0");
        var hard = ClubGame.New(startDay: 100, economy: null);
        var hardBoat = Eager(sharp);
        var onTheHard = new Game(hardBoat, 100, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Ok(onTheHard.Execute(new ListForSale(100000)));
        for (int i = 0; i < 3 && Sale(onTheHard).Viewings.Count == 0; i++) Ok(onTheHard.Execute(new WaitDays(1)));
        var first = Sale(onTheHard).Viewings.First();
        Assert.Contains(first.Findings, f => f.StartsWith("Heavy growth", StringComparison.Ordinal));
        var below = Eager(sharp).AllFaultsEver.Where(f => Eager().Get(f.ComponentId).BelowWaterline).Select(f => f.Name).ToHashSet();
        Assert.Contains(first.Findings, f => below.Any(n => f.StartsWith(n, StringComparison.Ordinal))); // on the hard she sees both

        var g = AfloatWith(Eager(sharp), 154, fixSeacock: false);
        Ok(g.Execute(new ListForSale(100000)));
        for (int i = 0; i < 21; i++) Ok(g.Execute(new WaitDays(1)));
        var viewings = Sale(g).Viewings;
        Assert.NotEmpty(viewings);
        foreach (var v in viewings)
        {
            Assert.DoesNotContain(v.Findings, f => f.StartsWith("Heavy growth", StringComparison.Ordinal));
            Assert.DoesNotContain(v.Findings, f => below.Any(n => f.StartsWith(n, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void The_hard_side_viewings_are_unchanged_by_the_afloat_rules_every_day_of_the_week_still_counts()
    {
        var g = new Game(Eager(), 75, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Ok(g.Execute(new ListForSale(100000)));
        for (int i = 0; i < 7; i++) Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(7, Sale(g).Viewings.Count); // chance 1 and no weekday limit on the hard
        Assert.Equal(7, Sale(g).Viewings.Select(v => v.Day).Distinct().Count());
    }

    [Fact]
    public void A_listing_made_on_the_hard_stays_through_the_launch_and_keep_still_withdraws_it()
    {
        var g = new Game(Eager(), 75, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Ok(g.Execute(new ListForSale(100000)));
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        Assert.True(Sale(g).Listed);
        Ok(g.Execute(new Keep()));
        Assert.False(Sale(g).Listed);
    }

    [Fact]
    public void No_ladder_or_crewing_boat_shares_a_name_with_a_ferry()
    {
        var boat = ClubGame.Boat();
        var ferries = boat.CloseCalls.Ferries;
        Assert.NotEmpty(ferries);
        var names = boat.Board.Ladder.Boats.Select(b => b.Name)
            .Concat(boat.Board.Crewing.Places.Select(p => p.Boat.Split(' ')[^1])).ToList();
        Assert.True(names.Count >= 6);
        foreach (var n in names) Assert.DoesNotContain(n, ferries, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_board_text_has_the_window_at_the_bottom_with_no_ids()
    {
        var text = new Haulout.Prose.Prose(new Currency("NOK", "", false)).Noticeboard(Board(ClubGame.New()));
        Assert.Contains("The broker's window, by the club:", text);
        Assert.True(text.IndexOf("The week north", StringComparison.Ordinal) < text.IndexOf("The broker's window", StringComparison.Ordinal));
        Assert.Contains("You are", text);
    }

    [Fact]
    public void A_listing_on_the_hard_keeps_the_offices_hours_like_one_afloat()
    {
        var g = new Game(BoatLoader.Load(ClubGame.Sloop28Json(), true, null, withHours: true), 75, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Assert.Equal("08:00", ClubGame.Status(g).TimeOfDay);
        var early = g.Execute(new ListForSale(100000));
        Assert.False(early.Accepted);
        Assert.Equal("The yard office opens at nine.", Assert.Single(early.Narrative));
        Assert.False(Sale(g).Listed);
        ClubGame.LookUntilAtLeast(g, "09:00");
        Ok(g.Execute(new ListForSale(100000)));
        Assert.True(Sale(g).Listed);
    }

    [Fact]
    public void An_offer_cannot_be_accepted_from_another_port_the_broker_is_at_Skjaervik()
    {
        var g = AfloatWith(Eager(), 160);
        Ok(g.Execute(new ListForSale(100000)));
        for (int i = 0; i < 14 && !Sale(g).Viewings.Any(v => v.Open); i++) Ok(g.Execute(new WaitDays(1)));
        Assert.Contains(Sale(g).Viewings, v => v.Open);
        Ok(Walk.Start(g)); // listing shut the engine down
        ClubGame.Sail(g, "Hestholmen");
        var away = g.Execute(new AcceptOffer());
        Assert.False(away.Accepted);
        Assert.Equal("The broker is at Skjærvik: bring her home to accept an offer.", Assert.Single(away.Narrative));
        Assert.False(ClubGame.Status(g).Sold);
        ClubGame.Sail(g, "Skjærvik");
        Assert.Contains("Sold", Text(Ok(g.Execute(new AcceptOffer()))));
    }

    [Fact]
    public void The_course_shortfall_reads_on_in_lower_case_after_your_log()
    {
        var text = new Haulout.Prose.Prose(new Currency("NOK", "", false)).Noticeboard(Board(ClubGame.New()));
        Assert.Contains("Your log: the radio ticket: not yet.", text);
        Assert.DoesNotContain("Your log: The ", text);
        Assert.Contains("Your log: 300 miles: you have 0.", text);
    }

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
}
