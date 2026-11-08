using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Haulout.Sim.Data;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests;

/// <summary>The economy is one file (design note 010, second pass A): every money and time knob is in economy.json and reaches its rule record, and nothing of it is a literal in code.</summary>
public class EconomyTests
{
    private sealed class Fixed(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private static string BoatJson()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Haulout.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir!.FullName, .. parts]);
    }

    // ------------------------------------------------------------------ loading

    [Fact]
    public void Every_value_of_economy_json_reaches_its_rule_record()
    {
        var eco = BoatLoader.LoadEconomy();
        var boat = BoatLoader.LoadSloop28();

        Assert.Equal(eco.Start.StartingBalanceNok, boat.Economy.StartingBalanceNok);
        Assert.Equal(eco.Start.LaunchDeadlineDay, boat.Economy.LaunchDeadlineDay);
        Assert.Equal(eco.Start.LaunchMinutes, boat.Economy.LaunchMinutes);
        Assert.Equal(eco.EmergencyLift.Nok, boat.Economy.EmergencyLiftNok);
        Assert.Equal(eco.EmergencyLift.Days, boat.Economy.EmergencyLiftDays);

        Assert.Equal(eco.Calendar.EveningMinutes, boat.Calendar.EveningMinutes);
        Assert.Equal(eco.Calendar.VacationDaysPerYear, boat.Calendar.VacationDaysPerYear);
        Assert.Equal(eco.Calendar.PayNok, boat.Calendar.PayNok);
        Assert.Equal(eco.Calendar.OverdraftLimitNok, boat.Calendar.OverdraftLimitNok);
        Assert.Equal(eco.Calendar.InterestPerMonth, boat.Calendar.InterestPerMonth);
        Assert.Equal(
            eco.Calendar.Bills.Select(b => (b.Id, b.Text, b.Short, b.Nok, string.Join(",", b.DaysOfYear), b.FromDay)),
            boat.Calendar.Bills.Select(b => (b.Id, b.Text, b.Short, b.Nok, string.Join(",", b.DaysOfYear), b.FromDay)));
        Assert.Equal(2, boat.Calendar.Bills.Count);

        Assert.Equal(eco.Yard.LiftNok, boat.Yard.LiftNok);
        Assert.Equal(eco.Yard.StorageNok, boat.Yard.StorageNok);
        Assert.Equal(eco.Yard.ChangeFeeNok, boat.Yard.ChangeFeeNok);
        Assert.Equal(eco.Yard.WashNok, boat.Yard.WashNok);
        Assert.Equal(eco.Tow.Nok, boat.Trips!.Tow.Nok);
        Assert.Equal(eco.Orders.LeadDays, [boat.Orders.LeadLo, boat.Orders.LeadHi]);
        Assert.Equal(eco.Mooring.PartedRepairNok, boat.Mooring.PartedRepairNok);
        Assert.Equal(eco.Fuel.DieselNokPerLitre, boat.Layup.DieselNokPerLitre);
        Assert.Equal(eco.Survey.NokPerFoot, boat.Survey!.NokPerFoot);
        Assert.Equal(eco.CloseCalls.LetterNok, boat.CloseCalls.LetterNok);
        Assert.Equal(eco.Currency, boat.Currency);

        // The shipped numbers (placeholders for Amit): a change here is a change of balance, never of this refactor.
        Assert.Equal((25000, 3500, 20000, 150), (boat.Economy.StartingBalanceNok, boat.Calendar.PayNok, boat.Calendar.OverdraftLimitNok, boat.Calendar.UnpaidLeaveNok));
        Assert.Equal((7000, 9000, 500, 1200, 6000, 4000), (boat.Yard.LiftNok, boat.Yard.StorageNok, boat.Yard.ChangeFeeNok, boat.Yard.WashNok, boat.Trips.Tow.Nok, boat.Mooring.PartedRepairNok));
    }

    [Fact]
    public void Without_a_calendar_the_boat_has_no_pay_no_bills_no_limits_and_no_orders_but_keeps_the_prices()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        Assert.Equal(0, boat.Calendar.PayNok);
        Assert.Empty(boat.Calendar.Bills);
        Assert.False(boat.Calendar.ChargesDays);
        Assert.Equal(0, boat.Calendar.OverdraftLimitNok);
        Assert.False(boat.Orders.Enabled);
        Assert.Equal(7000, boat.Yard.LiftNok);
        Assert.Equal(6000, boat.Trips!.Tow.Nok);
        Assert.Equal(25000, boat.Economy.StartingBalanceNok);
    }

    [Fact]
    public void The_game_exposes_the_currency_and_the_boat_file_holds_none_of_the_knobs()
    {
        Assert.Equal(new Currency("NOK", "", false), new Game(BoatLoader.LoadSloop28()).Currency);
        Assert.Equal("{\"code\":\"NOK\",\"symbol\":\"\",\"before\":false}",
            JsonNode.Parse(TestEconomy.Text())!["currency"]!.ToJsonString());
    }

    // ------------------------------------------------------------------ unpaid leave

    [Theory]
    [InlineData(3500, 150)]
    [InlineData(6000, 300)]
    public void Unpaid_leave_is_the_pay_over_21_working_days_to_the_nearest_50(int pay, int expected)
    {
        var economy = TestEconomy.Edited(n =>
        {
            n["calendar"]!["payNok"] = pay;
            n["calendar"]!.AsObject().Remove("unpaidLeaveNok");
        });
        Assert.Equal(expected, BoatLoader.ParseEconomy(economy).Calendar.DerivedUnpaidLeaveNok());
        Assert.Equal(expected, BoatLoader.Load(BoatJson(), economyJson: economy).Calendar.UnpaidLeaveNok);
    }

    [Fact]
    public void An_explicit_unpaid_leave_overrides_the_derivation_while_it_is_present()
    {
        Assert.False(JsonNode.Parse(TestEconomy.Text())!["calendar"]!.AsObject().ContainsKey("unpaidLeaveNok")); // shipped: derived, not set
        Assert.Equal(BoatLoader.ParseEconomy(TestEconomy.Text()).Calendar.DerivedUnpaidLeaveNok(), BoatLoader.LoadSloop28().Calendar.UnpaidLeaveNok);
        var economy = TestEconomy.Edited(n => n["calendar"]!["unpaidLeaveNok"] = 777);
        Assert.Equal(777, BoatLoader.Load(BoatJson(), economyJson: economy).Calendar.UnpaidLeaveNok);
    }

    // ------------------------------------------------------------------ the currency

    [Fact]
    public void The_currency_label_is_written_in_one_place_and_follows_the_symbol_rule()
    {
        var nok = new Currency("NOK", "", false);
        Assert.Equal("6000 NOK", MoneyWords.Amount(6000, nok));
        Assert.Equal("0 NOK", MoneyWords.Amount(0, nok));
        Assert.Equal("-500 NOK", MoneyWords.Amount(-500, nok));
        Assert.Equal("\u20ac6000", MoneyWords.Amount(6000, new Currency("EUR", "\u20ac", true)));
        Assert.Equal("-\u20ac500", MoneyWords.Amount(-500, new Currency("EUR", "\u20ac", true)));
        Assert.Equal("6000 kr", MoneyWords.Amount(6000, new Currency("SEK", "kr", false)));

        Assert.Equal("+600 NOK", MoneyWords.Signed(600, nok));
        Assert.Equal("-600 NOK", MoneyWords.Signed(-600, nok));
        Assert.Equal("0 NOK", MoneyWords.Signed(0, nok));
        Assert.Equal("+\u20ac600", MoneyWords.Signed(600, new Currency("EUR", "\u20ac", true)));

        Assert.Equal("Money left: 9000 NOK.", MoneyWords.Balance(9000, nok));
        Assert.Equal("You owe 2400 NOK.", MoneyWords.Balance(-2400, nok));
        Assert.Equal("You owe \u20ac2400.", MoneyWords.Balance(-2400, new Currency("EUR", "\u20ac", true)));
        Assert.Equal("9000 NOK left", MoneyWords.BalanceShort(9000, nok));
        Assert.Equal("you owe 2400 NOK", MoneyWords.BalanceShort(-2400, nok));
    }

    [Fact]
    public void A_game_in_another_currency_writes_every_amount_in_it()
    {
        var economy = TestEconomy.Edited(n => n["currency"] = JsonNode.Parse("{\"code\":\"EUR\",\"symbol\":\"\\u20ac\",\"before\":true}"));
        var g = new Game(BoatLoader.Load(BoatJson(), economyJson: economy), 80, new Fixed(0.99), Game.DefaultSeed, Haulout.Sim.Weather.WeatherSeries.Fair());
        var log = g.Execute(new Look());
        Assert.DoesNotContain("NOK", string.Join("\n", log.Narrative));
        Assert.Equal("\u20ac", g.Currency.Symbol);
        // The briefing's money line.
        var briefing = (BriefingView)g.Ask(new Briefing());
        Assert.Contains(briefing.Lines, l => l.Contains("\u20ac25000"));
        Assert.DoesNotContain(briefing.Lines, l => l.Contains("NOK"));
    }

    // ------------------------------------------------------------------ the boat file never disagrees

    public static TheoryData<string> Knobs => new()
    {
        "economy", "survey.nokPerFoot", "yard.liftNok", "yard.storageNok", "yard.changeFeeNok", "yard.washNok", "trips.tow.nok",
        "mooring.partedRepairNok", "layup.dieselNokPerLitre", "closeCalls.letterNok", "orders.leadDays",
        "calendar.eveningMinutes", "calendar.vacationDaysPerYear", "calendar.unpaidLeaveNok", "calendar.payNok",
        "calendar.overdraftLimitNok", "calendar.interestPerMonth", "calendar.bills",
    };

    [Theory]
    [MemberData(nameof(Knobs))]
    public void A_boat_file_that_holds_a_knob_economy_json_owns_is_refused(string path)
    {
        var node = JsonNode.Parse(BoatJson())!;
        var keys = path.Split('.');
        var at = node;
        foreach (var key in keys[..^1]) at = at[key] ?? (at[key] = new JsonObject());
        at[keys[^1]] = keys[^1] == "leadDays" ? new JsonArray(1, 2) : keys[^1] == "bills" ? new JsonArray() : keys.Length == 1 ? new JsonObject() : 1;
        var ex = Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(node.ToJsonString()));
        Assert.Contains(path, ex.Message);
        Assert.Contains("economy.json", ex.Message);
    }

    // ------------------------------------------------------------------ nothing of it is a literal in code

    private static string CodeOf(string path) =>
        string.Join("\n", File.ReadAllLines(path).Select(l =>
        {
            int c = l.IndexOf("//", StringComparison.Ordinal);
            return c >= 0 ? l[..c] : l;
        }));

    private static IEnumerable<string> GameFiles() =>
        Directory.GetFiles(RepoFile("src", "Haulout.Sim", "Play"), "Game*.cs");

    private static void Numbers(JsonNode? node, List<long> into)
    {
        switch (node)
        {
            case JsonObject o: foreach (var kv in o) if (kv.Key != "placeholders") Numbers(kv.Value, into); break;
            case JsonArray a: foreach (var x in a) Numbers(x, into); break;
            case JsonValue v when v.TryGetValue<long>(out var n): into.Add(n); break;
        }
    }

    [Fact]
    public void No_number_of_economy_json_of_100_or_more_is_an_integer_literal_in_the_game_code()
    {
        var numbers = new List<long>();
        Numbers(JsonNode.Parse(File.ReadAllText(RepoFile("src", "Haulout.Sim", "Data", "economy.json"))), numbers);
        var big = numbers.Where(n => n >= 100).Distinct().ToList();
        Assert.Contains(25000L, big);   // the scan reads the file: it is not vacuous

        // Coincidences that are not the economy: each is allowed in one file only, and says why.
        var allowed = new Dictionary<(long Number, string File), string>
        {
            [(2000, "Game.Tick.cs")] = "a cap of 2000 minutes on a search loop; the harbourmaster's letter is 2000 by coincidence",
            [(180, "Game.Chart.cs")] = "degrees of a half turn in a bearing; the evening is 180 minutes by coincidence",
        };

        var offences = new List<string>();
        foreach (var file in GameFiles())
        {
            var name = Path.GetFileName(file);
            var code = CodeOf(file);
            foreach (var n in big)
                if (!allowed.ContainsKey((n, name)) && Regex.IsMatch(code, $@"(?<![\w.]){n}(?![\w.])")) offences.Add($"{name}: {n}");
        }
        Assert.Empty(offences);
        // An allow-list entry that no longer matches is dead weight: it must still be there.
        foreach (var (key, why) in allowed)
            Assert.True(Regex.IsMatch(CodeOf(Path.Combine(RepoFile("src", "Haulout.Sim", "Play"), key.File)), $@"(?<![\w.]){key.Number}(?![\w.])"), why);
    }

    [Fact]
    public void No_NOK_string_is_left_in_the_game_code_except_through_MoneyWords()
    {
        foreach (var file in GameFiles())
            Assert.DoesNotContain("NOK", CodeOf(file));
        // MoneyWords itself spells nothing: the code comes from the loaded currency.
        Assert.DoesNotContain("NOK", CodeOf(RepoFile("src", "Haulout.Sim", "Play", "MoneyWords.cs")));
    }
}
