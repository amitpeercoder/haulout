using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The week and the money (design note 010, step 1): the weekday rule, pay and bills, the overdraft, parts on order, the waits that end on a day.</summary>
public class CalendarTests
{
    private sealed class FixedRandom(double value) : IRandom
    {
        public double NextDouble() => value;
    }

    private const string Starter = "starter motor";

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

    private static string Text(CommandResult r) => string.Join(" ", r.Narrative);
    private static StatusView Status(Game g) => (StatusView)g.Ask(new Status());
    private static CalendarView Cal(Game g) => (CalendarView)g.Ask(new Calendar());
    private static LogView Log(Game g) => (LogView)g.Ask(new Logbook());

    private static string Sloop28Json()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.sloop28.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Boat Boat(params (string From, string To)[] edits)
    {
        var json = Sloop28Json();
        foreach (var (from, to) in edits)
        {
            Assert.Contains(from, json);
            json = json.Replace(from, to);
        }
        return BoatLoader.Load(json);
    }

    private static Game NewGame(int startDay, Boat? boat = null, WeatherSeries? weather = null) =>
        new(boat ?? BoatLoader.LoadSloop28(), startDay, new FixedRandom(0.99), Game.DefaultSeed, weather ?? WeatherSeries.Fair());

    /// <summary>Inspects the negative terminal over and over (5 minutes each) until at most 4 minutes of the evening are left.</summary>
    private static void UseUpTheEvening(Game g)
    {
        Ok(Walk.Do(g, "negative terminal", c => new Inspect(c)));
        while (Cal(g).EveningMinutesLeft >= 5) Ok(g.Execute(new Inspect("negative terminal")));
    }

    // ------------------------------------------------------------------ the clock

    [Fact]
    public void Day_zero_is_a_Monday_and_the_first_fault_starts_on_a_Saturday()
    {
        Assert.Equal("Monday", new GameClock(0).WeekdayName);
        Assert.False(new GameClock(0).IsWeekend);
        Assert.Equal("Saturday", new GameClock(75).WeekdayName);
        Assert.True(new GameClock(75).IsWeekend);
        Assert.Equal("Sunday", new GameClock(76).WeekdayName);
        Assert.Equal("Monday", new GameClock(77).WeekdayName);
        Assert.Equal(0, new GameClock(7 * 51).Weekday);
        Assert.NotEqual(0, GameClock.DaysPerYear % GameClock.DaysPerWeek); // the weekday of a day of the year shifts every year

        var week = Cal(NewGame(75));
        Assert.Equal(("Saturday", true), (week.Weekday, week.Weekend));
        Assert.Equal("Saturday, day 75: a weekend, a boat day.", week.Week);
        Assert.Equal(0, week.EveningMinutesLeft);
    }

    // ------------------------------------------------------------------ the weekday rule

    [Fact]
    public void An_evening_is_free_and_the_minute_after_it_costs_one_vacation_day_once()
    {
        var g = NewGame(77); // a Monday
        var start = Cal(g);
        Assert.Equal(180, start.EveningMinutesLeft);
        Assert.Equal((25, 25, 0, 0), (start.VacationDaysLeft, start.VacationDaysPerYear, start.VacationDaysUsed, start.UnpaidDays));
        Assert.Equal("Monday, day 77: a weekday. An evening aboard: 3 h left before it costs a vacation day.", start.Week);

        UseUpTheEvening(g);
        Assert.Equal(25, Cal(g).VacationDaysLeft);
        Assert.DoesNotContain("vacation", Cal(g).Week.Replace("before it costs a vacation day", ""));

        var over = Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Contains("That's more than an evening's work: today, Monday, becomes a vacation day (24 left this year).", over.Narrative);
        var cal = Cal(g);
        Assert.Equal((24, 1, 0), (cal.VacationDaysLeft, cal.VacationDaysUsed, cal.EveningMinutesLeft));
        Assert.Equal("Monday, day 77: a weekday. Today is already a vacation day.", cal.Week);
        Assert.Contains(Log(g).Entries, e => e.Text == "Vacation day: Monday, day 77." && e.CostNok == 0);
        Assert.Equal(24, Status(g).VacationDaysLeft);

        // A second long job the same day costs nothing more.
        var more = Ok(g.Execute(new Charge("start battery")));
        Assert.DoesNotContain("vacation", Text(more));
        Assert.Equal(24, Cal(g).VacationDaysLeft);
        Assert.Single(Log(g).Entries, e => e.Text.StartsWith("Vacation day", StringComparison.Ordinal));
    }

    [Fact]
    public void A_weekend_of_ten_hours_aboard_costs_no_vacation_day()
    {
        var g = NewGame(75); // a Saturday
        var a = Ok(Walk.Do(g, "start battery", c => new Charge(c)));
        var b = Ok(g.Execute(new Charge("start battery")));
        Assert.InRange(Status(g).Day, 75, 76); // Saturday into Sunday
        Assert.DoesNotContain("vacation", Text(a) + Text(b));
        Assert.Equal((25, 0), (Cal(g).VacationDaysLeft, Cal(g).VacationDaysUsed));
        Assert.DoesNotContain(Log(g).Entries, e => e.Text.Contains("acation day", StringComparison.Ordinal));
    }

    [Fact]
    public void Waiting_never_charges_a_day()
    {
        var g = NewGame(77);
        var r = Ok(g.Execute(new WaitDays(3)));
        Assert.DoesNotContain("vacation", Text(r));
        Assert.Equal((25, 0, 0), (Cal(g).VacationDaysLeft, Cal(g).VacationDaysUsed, Cal(g).UnpaidDays));
        Ok(g.Execute(new WaitWeek()));
        Assert.Equal(25, Cal(g).VacationDaysLeft);
    }

    /// <summary>Launched on a Saturday with the fixes in, waited to Monday morning at her mooring, engine running, in the cockpit.</summary>
    private static Game AfloatOnMonday(Boat? boat = null)
    {
        var g = NewGame(75, boat, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        Ok(g.Execute(new WaitDays(2)));
        Assert.Equal(77, Status(g).Day);
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        return g;
    }

    [Fact]
    public void A_weekday_departure_charges_a_vacation_day_and_a_weekend_one_does_not()
    {
        var g = AfloatOnMonday();
        Assert.Equal(25, Cal(g).VacationDaysLeft);
        var depart = Ok(g.Execute(new Depart()));
        Assert.Contains("A weekday at sea: today, Monday, is a vacation day (24 left this year).", depart.Narrative);
        Assert.Contains(Log(g).Entries, e => e.Text == "Vacation day: Monday, day 77.");
        var next = Ok(g.Execute(new Continue()));
        Assert.DoesNotContain("vacation", Text(next));
        Assert.Equal(24, Cal(g).VacationDaysLeft);
    }

    [Fact]
    public void With_the_budget_spent_a_long_weekday_is_unpaid_leave_with_its_bill()
    {
        var g = NewGame(77, Boat(("\"vacationDaysPerYear\": 25", "\"vacationDaysPerYear\": 0")));
        UseUpTheEvening(g);
        int balance = Status(g).BalanceNok;
        var over = Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Contains("No vacation days left: today, Monday, is unpaid leave. 1500 NOK comes off your pay.", over.Narrative);
        Assert.Equal(balance - 1500, Status(g).BalanceNok);
        var cal = Cal(g);
        Assert.Equal((0, 0, 1), (cal.VacationDaysLeft, cal.VacationDaysUsed, cal.UnpaidDays));
        Assert.Equal("Monday, day 77: a weekday. Today is unpaid leave.", cal.Week);
        Assert.Contains(Log(g).Entries, e => e.Text == "Unpaid leave: Monday, day 77." && e.CostNok == 1500);
        Assert.Equal(new LedgerEntry(77, "unpaid", "Unpaid leave: Monday, day 77.", -1500), cal.Money.Recent[0]);
        Assert.Equal(1500, cal.Money.UnpaidNok);
        Assert.Equal(1500, Status(g).MoneySpentNok);
        // Charged once.
        Ok(g.Execute(new Charge("start battery")));
        Assert.Equal(1, Cal(g).UnpaidDays);
    }

    [Fact]
    public void The_vacation_budget_comes_back_at_the_new_year()
    {
        var g = NewGame(357, Boat(("\"vacationDaysPerYear\": 25", "\"vacationDaysPerYear\": 2"))); // 357 is a Monday
        UseUpTheEvening(g);
        Ok(g.Execute(new Inspect("negative terminal")));
        Assert.Equal((1, 1), (Cal(g).VacationDaysLeft, Cal(g).VacationDaysUsed));
        Ok(g.Execute(new WaitDays(2)));
        Assert.Equal(359, Status(g).Day);
        Assert.Equal(1, Cal(g).VacationDaysLeft);
        Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(360, Status(g).Day);
        Assert.Equal((2, 0), (Cal(g).VacationDaysLeft, Cal(g).VacationDaysUsed));
    }

    // ------------------------------------------------------------------ pay and bills

    /// <summary>Waits on the hard to the morning of <paramref name="day"/> (sixty days at most at a time).</summary>
    private static CommandResult WaitTo(Game g, int day)
    {
        CommandResult last = null!;
        while (Status(g).Day < day) last = Ok(g.Execute(new WaitDays(Math.Min(60, day - Status(g).Day))));
        return last;
    }

    [Fact]
    public void Payday_and_the_berth_land_at_midnight_on_day_90_with_alerts_log_lines_and_a_week_line()
    {
        var g = NewGame(85);
        var r = Ok(g.Execute(new WaitDays(5)));
        Assert.Equal(90, Status(g).Day);
        Assert.Equal(25000 + 6000 - 3000, Status(g).BalanceNok);
        Assert.Equal(3000, Status(g).MoneySpentNok);

        Assert.Contains(r.Alerts, a => a.Text == "Payday: 6000 NOK." && a.Day == 90 && a.TimeOfDay == "00:00");
        Assert.Contains(r.Alerts, a => a.Text == "Berth and mooring at Skjærvik, the quarter: 3000 NOK.");
        var log = Log(g).Entries;
        Assert.Contains(log, e => e.Alert && e.Text == "Payday: 6000 NOK." && e.Day == 90);
        Assert.Contains(log, e => e.Alert && e.Text == "Berth and mooring at Skjærvik, the quarter: 3000 NOK." && e.CostNok == 3000);
        Assert.Equal("While you were away: payday 6000 NOK; the berth invoice 3000 NOK.", r.Narrative[^1]);

        var money = Cal(g).Money;
        Assert.Equal((6000, 0, 3000, 0, 0), (money.IncomeNok, money.SpendingNok, money.BillsNok, money.InterestNok, money.UnpaidNok));
        Assert.Equal(new LedgerEntry(90, "bill", "Berth and mooring at Skjærvik, the quarter", -3000), money.Recent[0]);
        Assert.Equal(new LedgerEntry(90, "income", "Payday.", 6000), money.Recent[1]);
        Assert.Equal(Status(g).BalanceNok, money.BalanceNok);
        Assert.Equal(120, Cal(g).NextPaydayDay);
    }

    [Fact]
    public void Pay_comes_every_month_the_berth_every_quarter_and_the_insurance_first_on_day_450()
    {
        var g = NewGame(88);
        WaitTo(g, 121);
        var days = Log(g).Entries.Where(e => e.Text == "Payday: 6000 NOK.").Select(e => e.Day).ToList();
        Assert.Equal([90, 120], days);
        Assert.Single(Log(g).Entries, e => e.Text.StartsWith("Berth and mooring", StringComparison.Ordinal)); // not on 120

        WaitTo(g, 451);
        var berths = Log(g).Entries.Where(e => e.Text.StartsWith("Berth and mooring", StringComparison.Ordinal)).Select(e => e.Day).ToList();
        Assert.Equal([90, 180, 270, 360, 450], berths);
        var insurance = Log(g).Entries.Where(e => e.Text.StartsWith("Insurance", StringComparison.Ordinal)).ToList();
        var renewal = Assert.Single(insurance);
        Assert.Equal((450, 8000), (renewal.Day, renewal.CostNok));
        Assert.Equal(13, Log(g).Entries.Count(e => e.Text == "Payday: 6000 NOK."));
    }

    [Fact]
    public void Interest_is_charged_only_below_zero_and_before_the_pay()
    {
        var rich = NewGame(85);
        Ok(rich.Execute(new WaitDays(5)));
        Assert.DoesNotContain(Cal(rich).Money.Recent, e => e.Kind == "interest");

        var g = NewGame(85, Boat(("\"startingBalanceNok\": 25000", "\"startingBalanceNok\": -10000")));
        var r = Ok(g.Execute(new WaitDays(5)));
        Assert.Contains(r.Alerts, a => a.Text == "The bank took 150 NOK of overdraft interest.");
        var recent = Cal(g).Money.Recent;
        Assert.Equal("bill", recent[0].Kind);
        Assert.Equal(new LedgerEntry(90, "income", "Payday.", 6000), recent[1]);
        Assert.Equal(new LedgerEntry(90, "interest", "Overdraft interest for the month.", -150), recent[2]);
        Assert.Equal(-10000 - 150 + 6000 - 3000, Status(g).BalanceNok);
        Assert.Equal(150, Cal(g).Money.InterestNok);
        Assert.Contains("overdraft interest 150 NOK; payday 6000 NOK; the berth invoice 3000 NOK", r.Narrative[^1]);
    }

    // ------------------------------------------------------------------ the overdraft

    [Fact]
    public void The_bank_lets_a_purchase_overdraw_up_to_the_limit_and_the_refusals_say_how_far()
    {
        var g = NewGame(75, Boat(("\"startingBalanceNok\": 25000", "\"startingBalanceNok\": -19900")));
        Assert.Equal(-19900, Status(g).BalanceNok);
        No(g.Execute(new Buy("remote start button")), "That's 400 NOK, and you have 100 NOK left before the bank's limit (20000 NOK overdrawn).");
        var fuses = Ok(g.Execute(new Buy("fuses"))); // 60 NOK: within the limit
        Assert.Equal(60, fuses.CostNok);
        Assert.Equal(-19960, Status(g).BalanceNok);

        var past = NewGame(75, Boat(("\"startingBalanceNok\": 25000", "\"startingBalanceNok\": -21000")));
        No(past.Execute(new Buy("fuses")), "You're 21000 NOK overdrawn, past the bank's limit of 20000 NOK: nothing more until payday brings you back under.");
        Assert.Equal("Balance -19960 NOK (the bank allows 20000 overdrawn).", Prose.Prose.Calendar(Cal(g)).Split(Environment.NewLine).Single(l => l.StartsWith("Balance", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_tow_the_player_did_not_choose_may_pass_the_limit_and_then_nothing_more_is_bought_until_payday()
    {
        var g = NewGame(75, Boat(("\"startingBalanceNok\": 25000", "\"startingBalanceNok\": 30000")), WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Ok(g.Execute(new FillTank())); // the buying below idles the engine for two days: the 27 litres would not last (design note 010)
        Ok(Walk.Start(g));             // filling the tank shuts the engine down
        while (g.Execute(new Buy("hose")).Accepted) { } // spend down to the bank's limit
        Assert.InRange(Status(g).BalanceNok, -20000, -19750);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart()));
        var tow = Ok(g.Execute(new CallTow()));
        Assert.True(Status(g).BalanceNok < -20000);
        var billed = Cal(g).Money.Recent[0];
        Assert.Equal(("bill", "The towboat.", -tow.CostNok), (billed.Kind, billed.Text, billed.Nok));
        var refused = g.Execute(new Buy("hose"));
        Assert.False(refused.Accepted);
        Assert.StartsWith($"You're {-Status(g).BalanceNok} NOK overdrawn, past the bank's limit of 20000", Text(refused));
    }

    [Fact]
    public void A_boat_without_a_calendar_pays_nothing_charges_no_day_and_keeps_the_old_money_texts()
    {
        var boat = BoatLoader.LoadSloop28(withCalendar: false);
        Assert.Equal(CalendarRules.Default, boat.Calendar);
        Assert.Equal(OrderRules.Default, boat.Orders);
        var g = new Game(boat, 77, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Fair());
        Ok(Walk.Do(g, "start battery", c => new Charge(c)));
        var wait = Ok(g.Execute(new WaitDays(45)));
        Assert.Equal(25000, Status(g).BalanceNok);
        Assert.Equal(0, Status(g).MoneySpentNok);
        Assert.DoesNotContain("vacation", Text(wait));
        Assert.DoesNotContain("While you were away", Text(wait));
        Assert.Empty(Cal(g).Ahead);
        Assert.Equal((0, -1), (Cal(g).VacationDaysLeft, Cal(g).NextPaydayDay));
        var status = Status(g);
        Assert.Equal((null, 0, null), (status.Week, status.VacationDaysLeft, status.Next));

        var poor = new Game(BoatLoader.LoadSloop28(withCalendar: false), 75, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Fair());
        Ok(Walk.Do(poor, "engine block", c => new Replace(c)));
        int left = Status(poor).BalanceNok;
        Assert.Equal($"That's {boat.Survey!.FeeNok} NOK and you have {left} NOK left.", Text(poor.Execute(new OrderSurvey())));
    }

    // ------------------------------------------------------------------ parts take time

    [Fact]
    public void Replace_of_a_part_that_is_not_on_the_shelf_is_refused_with_the_order_offered_and_costs_nothing()
    {
        var g = NewGame(75);
        Walk.Near(g, Starter);
        var r = No(g.Execute(new Replace(Starter)), "The yard chandlery doesn't keep a starter motor on the shelf. Order one (order starter motor): about 3 to 10 days.");
        Assert.Equal(25000, Status(g).BalanceNok);
        // The component's option says the same, and the shelf parts are unchanged.
        var options = (ComponentActionsView)g.Ask(new ComponentActions(Starter));
        var replace = options.Options.Single(o => o.Verb == "replace");
        Assert.False(replace.Available);
        Assert.Equal(r.Narrative.Single(), replace.UnavailableReason);
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
    }

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(143)]
    [InlineData(200)]
    [InlineData(251)]
    [InlineData(310)]
    [InlineData(404)]
    public void An_order_arrives_in_three_to_ten_days_the_same_for_the_same_seed_and_day(int day)
    {
        int Lead()
        {
            var g = NewGame(day);
            Walk.Near(g, Starter);
            var r = Ok(g.Execute(new Order(Starter)));
            var order = Assert.Single(Cal(g).Orders);
            Assert.Equal((Starter, day, "on its way"), (order.Part, order.OrderedDay, order.Status));
            Assert.Contains($"due on day {order.DueDay}", Text(r));
            return order.DueDay - day;
        }
        int lead = Lead();
        Assert.InRange(lead, 3, 10);
        Assert.Equal(lead, Lead());
    }

    [Fact]
    public void An_order_is_paid_at_once_logged_and_makes_replace_say_where_it_stands()
    {
        var g = NewGame(75);
        Walk.Near(g, Starter);
        int minutes = Status(g).MinutesSpent;
        var r = Ok(g.Execute(new Order(Starter)));
        Assert.Equal(3800, r.CostNok);
        Assert.Equal(5, r.MinutesTaken);
        Assert.Equal(minutes, Status(g).MinutesSpent); // a phone call is not time aboard
        Assert.Equal(25000 - 3800, Status(g).BalanceNok);
        int due = Assert.Single(Cal(g).Orders).DueDay;
        Assert.Contains(Log(g).Entries, e => e.CostNok == 3800 && e.Text == $"Ordered a starter motor from the yard chandlery, due on day {due}.");
        Assert.Equal("spending", Cal(g).Money.Recent[0].Kind);

        No(g.Execute(new Replace(Starter)), $"The starter motor is on order: due on day {due}.");
        No(g.Execute(new Order(Starter)), $"The starter motor is on order already: due on day {due}.");
        Assert.Contains(Cal(g).Ahead, e => e is { Kind: "delivery" } && e.Day == due && e.Text == "The starter motor is due at the yard");
    }

    [Fact]
    public void Order_is_refused_at_sea_for_unknown_things_for_what_is_on_the_shelf_and_without_money()
    {
        var g = NewGame(75, Boat(("\"startingBalanceNok\": 25000", "\"startingBalanceNok\": -19000")));
        Walk.Near(g, Starter);
        No(g.Execute(new Order("flux capacitor")), "The yard chandlery doesn't have 'flux capacitor', and you haven't found a part called that.");
        No(g.Execute(new Order("impeller")), "The yard chandlery keeps the impeller on the shelf: buy it.");
        No(g.Execute(new Order(Starter)), "That's 3800 NOK, and you have 1000 NOK left before the bank's limit (20000 NOK overdrawn).");
        Assert.Empty(Cal(g).Orders);
    }

    private static Boat FourDays() => Boat(("\"leadDays\": [3, 10]", "\"leadDays\": [4, 4]"));

    [Fact]
    public void A_part_that_comes_in_while_she_is_at_the_yard_goes_aboard_and_Replace_with_it_costs_nothing()
    {
        var g = NewGame(77, FourDays());
        Walk.Near(g, Starter);
        Ok(g.Execute(new Order(Starter)));
        int balance = Status(g).BalanceNok;

        var wait = Ok(g.Execute(new WaitDays(10)));
        Assert.Equal(81, Status(g).Day);
        Assert.Contains("You come back on day 81: the starter motor is due at the yard.", wait.Narrative);
        Assert.Contains(wait.Alerts, a => a.Text == "The yard chandlery rang: your starter motor has come in. It's aboard.");
        Assert.Contains("While you were away: the starter motor came in.", wait.Narrative);
        Assert.Empty(Cal(g).Orders);
        var inventory = (InventoryView)g.Ask(new Inventory());
        Assert.Contains(inventory.Aboard, l => l is { Name: "starter motor (new, in its box)", Count: 1 });

        Assert.Equal(balance, Status(g).BalanceNok);
        var replace = Ok(g.Execute(new Replace(Starter)));
        Assert.Equal(0, replace.CostNok);
        Assert.Equal(balance, Status(g).BalanceNok); // it was paid for when it was ordered
        Assert.Contains("You take the spare starter motor from the spares aboard.", replace.Narrative);
        Assert.DoesNotContain(((InventoryView)g.Ask(new Inventory())).Aboard, l => l.Name.StartsWith("starter motor", StringComparison.Ordinal));
    }

    [Fact]
    public void A_part_that_comes_in_while_she_is_elsewhere_waits_at_the_yard_and_goes_aboard_on_the_next_arrival_there()
    {
        var g = NewGame(75, FourDays(), WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart()));
        for (int i = 0; i < 6 && Status(g).AtSea; i++) Ok(g.Execute(new Continue()));
        Assert.Equal("Hestholmen", Status(g).Port);

        Walk.Near(g, Starter);
        var order = Ok(g.Execute(new Order(Starter)));
        int due = Assert.Single(Cal(g).Orders).DueDay;
        Assert.Contains("order", order.Action);
        var wait = Ok(g.Execute(new WaitDays(10)));
        Assert.Equal(due, Status(g).Day);
        Assert.Contains(wait.Alerts, a => a.Text == "The yard chandlery rang: your starter motor has come in. It's waiting for you at the yard.");
        Assert.Equal("waiting at the yard", Assert.Single(Cal(g).Orders).Status);
        No(g.Execute(new Replace(Starter)), "The new starter motor is waiting for you at the yard.");
        No(g.Execute(new Order(Starter)), "The starter motor is waiting for you at the yard already.");

        Walk.ToPlace(g, "cockpit");
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Ok(g.Execute(new Depart()));
        CommandResult last = null!;
        for (int i = 0; i < 8 && Status(g).AtSea; i++) last = Ok(g.Execute(new Continue()));
        Assert.Equal("Skjærvik", Status(g).Port);
        Assert.Contains("At the yard: the starter motor that came in is put aboard.", last.Narrative);
        Assert.Empty(Cal(g).Orders);
        Assert.Contains(((InventoryView)g.Ask(new Inventory())).Aboard, l => l.Name == "starter motor (new, in its box)");
        Assert.Contains("starter motor", ((TripPlanView)g.Ask(new PlanTrip())).SparesAboard);
    }

    [Fact]
    public void Buy_of_an_item_the_yard_does_not_keep_is_an_order_at_the_yard_and_names_the_yard_elsewhere()
    {
        var boat = Boat(("\"id\": \"fuses\",", "\"id\": \"fuses\", \"stocked\": false,"));
        var g = NewGame(77, boat);
        var r = Ok(g.Execute(new Buy("fuses")));
        Assert.Equal(60, r.CostNok);
        int due = Assert.Single(Cal(g).Orders).DueDay;
        Assert.Contains($"The yard chandlery doesn't keep fuses (5) on the shelf: ordered, due on day {due}.", r.Narrative);
        WaitTo(g, due);
        Assert.Contains(((InventoryView)g.Ask(new Inventory())).Aboard, l => l.Name == "fuses (5)");
    }

    // ------------------------------------------------------------------ next weekend and wait a week

    [Fact]
    public void Next_weekend_comes_back_aboard_at_eight_on_the_next_Saturday_never_costing_a_day()
    {
        var g = NewGame(78); // Tuesday
        Ok(Walk.Do(g, "start battery", c => new Charge(c))); // 16:00
        var vacation = Cal(g).VacationDaysLeft;
        var r = Ok(g.Execute(new NextWeekend()));
        Assert.Equal((82, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Equal("You go back to work. Saturday, day 82: you're back aboard at eight.", r.Narrative[0]);
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited until Saturday, day 82.");
        Assert.Equal(vacation, Cal(g).VacationDaysLeft);
        Assert.DoesNotContain("While you were away", Text(r)); // nothing came in

        // From a Saturday it is the Saturday after, from a Sunday too, from a Friday the next morning.
        foreach (var (from, to) in new[] { (75, 82), (76, 82), (81, 82), (82, 89) })
        {
            var h = NewGame(from);
            Ok(h.Execute(new NextWeekend()));
            Assert.Equal(to, Status(h).Day);
            Assert.Equal("08:00", Status(h).TimeOfDay);
        }
    }

    [Fact]
    public void Wait_a_week_comes_back_aboard_at_eight_seven_days_on_and_says_what_came_in()
    {
        var g = NewGame(85); // a Tuesday; day 90 holds payday and the berth
        var r = Ok(g.Execute(new WaitWeek()));
        Assert.Equal((92, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Equal("You go back to work for a week. Tuesday, day 92: you're back aboard at eight.", r.Narrative[0]);
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited a week.");
        Assert.Equal("While you were away: payday 6000 NOK; the berth invoice 3000 NOK.", r.Narrative[^1]);
        Assert.Equal(7 * 1440, r.MinutesTaken);
        Assert.Equal(25000 + 6000 - 3000, Status(g).BalanceNok);
    }

    [Fact]
    public void Both_waits_are_refused_at_sea()
    {
        var g = AfloatOnMonday();
        Ok(g.Execute(new Depart()));
        Assert.True(Status(g).AtSea);
        No(g.Execute(new NextWeekend()), "You're at sea: continue, heave to or anchor.");
        No(g.Execute(new WaitWeek()), "You're at sea: continue, heave to or anchor.");
    }

    [Fact]
    public void Both_waits_stop_at_a_booked_slots_morning_and_at_a_delivery_morning()
    {
        // The slot: booked for day 255 from Saturday 250, a week would end on 257.
        var noDrift = Boat(("\"driftMaxDaysPerWeek\": 7", "\"driftMaxDaysPerWeek\": 0"));
        var slot = NewGame(250, noDrift, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        Ok(slot.Execute(new BookHaulout(255)));
        var week = Ok(slot.Execute(new WaitWeek()));
        Assert.Equal((255, "08:00"), (Status(slot).Day, Status(slot).TimeOfDay));
        Assert.Contains("You come back on day 255 for your haulout slot.", week.Narrative);
        Assert.Contains(Cal(slot).Ahead, e => e is { Kind: "slot", Nok: -16000 } && e.Day == 255);

        var weekend = NewGame(250, Boat(("\"driftMaxDaysPerWeek\": 7", "\"driftMaxDaysPerWeek\": 0")), WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8)));
        Ok(weekend.Execute(new BookHaulout(256)));
        Ok(weekend.Execute(new NextWeekend())); // the next Saturday is day 257
        Assert.Equal(256, Status(weekend).Day);

        // The delivery: ordered on Monday 77, due on 81, the weekend is day 82.
        var g = NewGame(77, FourDays());
        Walk.Near(g, Starter);
        Ok(g.Execute(new Order(Starter)));
        var r = Ok(g.Execute(new NextWeekend()));
        Assert.Equal((81, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Contains("You come back on day 81: the starter motor is due at the yard.", r.Narrative);
        Assert.DoesNotContain("You go back to work. Saturday", Text(r));
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited 4 days.");

        // A wait that starts on the delivery day is not stopped by it.
        Ok(g.Execute(new WaitWeek()));
        Assert.Equal(88, Status(g).Day);
    }

    [Fact]
    public void Afloat_the_waits_are_the_harbourmasters_day_by_day_wait_and_stop_when_she_needs_her_owner()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = new Game(boat, 100, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 2)));
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(g.Execute(new Launch()));
        boat.AllFaults.Single(f => f.Id == "bilge-pump-burnt-out").Wake(100);
        boat.AllFaults.Single(f => f.Id == "drain-hose-failed").Wake(100);

        var r = Ok(g.Execute(new WaitWeek()));
        Assert.Contains("You meant to go back to work for a week; the phone rang on day 1.", r.Narrative);
        Assert.Equal(101, Status(g).Day);
        Assert.Contains(r.Alerts, a => a.Text.StartsWith("The harbourmaster rang", StringComparison.Ordinal));
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited 1 day.");
    }

    [Fact]
    public void Afloat_a_quiet_wait_a_week_ends_at_eight_with_the_week_line_and_the_log_line()
    {
        var g = NewGame(
            75, weather: WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 2)));
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(g.Execute(new Launch()));
        var r = Ok(g.Execute(new NextWeekend()));
        Assert.Equal((82, "08:00"), (Status(g).Day, Status(g).TimeOfDay));
        Assert.Contains("You go back to work. Saturday, day 82: you're back aboard at eight.", r.Narrative);
        Assert.Contains(Log(g).Entries, e => e.Text == "Waited until Saturday, day 82.");
        Assert.DoesNotContain("While you were away", Text(r));
    }

    // ------------------------------------------------------------------ the query and the views

    [Fact]
    public void The_calendar_lists_what_is_coming_in_order_and_the_status_names_the_next_thing()
    {
        var g = NewGame(75);
        var cal = Cal(g);
        Assert.Equal(
            [new CalendarEntry(90, "Sunday", "payday", "Payday", 6000),
             new CalendarEntry(90, "Sunday", "bill", "Berth and mooring at Skjærvik, the quarter", -3000)],
            cal.Ahead);
        Assert.Equal((90, 6000, 20000, 25000), (cal.NextPaydayDay, cal.PayNok, cal.OverdraftLimitNok, cal.BalanceNok));
        Assert.Equal("Next: payday on day 90, 6000 NOK.", Status(g).Next);
        Assert.Equal("Saturday, day 75: a weekend, a boat day.", Status(g).Week);
        Assert.Equal(25, Status(g).VacationDaysLeft);

        WaitTo(g, 90);
        Assert.Equal(120, Cal(g).NextPaydayDay);
        Assert.Equal("Next: payday on day 120, 6000 NOK.", Status(g).Next);
    }

    [Fact]
    public void The_calendar_status_and_the_ledger_carry_no_ids_and_no_hidden_fault_names()
    {
        var boat = BoatLoader.LoadSloop28();
        var g = new Game(boat, 77, new FixedRandom(0.99), Game.DefaultSeed, WeatherSeries.Fair());
        var seen = new List<string>();
        void Snap()
        {
            foreach (Query q in new Query[] { new Calendar(), new Status(), new Inventory(), new Notices(), new Logbook() })
                seen.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(q)));
        }
        Snap();
        Walk.Near(g, Starter);
        seen.Add(JsonSerializer.Serialize(Ok(g.Execute(new Order(Starter)))));
        seen.Add(JsonSerializer.Serialize(g.Execute(new Replace(Starter))));
        Snap();
        seen.Add(JsonSerializer.Serialize(Ok(g.Execute(new NextWeekend()))));
        seen.Add(JsonSerializer.Serialize(Ok(g.Execute(new WaitWeek()))));
        Snap();

        var json = string.Concat(seen);
        foreach (var f in boat.AllFaults)
        {
            Assert.False(json.Contains(f.Id, StringComparison.Ordinal), f.Id);
            if (!f.Discovered) Assert.False(json.Contains(f.Name, StringComparison.Ordinal), f.Name);
        }
        foreach (var id in boat.Components.Select(c => c.Id).Where(id => id.Contains('-')))
            Assert.False(json.Contains(id, StringComparison.Ordinal), id);
        Assert.DoesNotContain("delivery-", json, StringComparison.Ordinal);
        Assert.DoesNotContain("bill-berth", json, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"calendar-view\"", JsonSerializer.Serialize<QueryResult>(g.Ask(new Calendar())));
        Assert.Equal(typeof(Calendar), JsonSerializer.Deserialize<Query>("{\"type\":\"calendar\"}")!.GetType());
        foreach (Command c in new Command[] { new NextWeekend(), new WaitWeek(), new Order("starter motor") })
            Assert.Equal(c.GetType(), JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize(c))!.GetType());
    }

    [Fact]
    public void The_shipped_data_turns_the_money_on_and_the_stocked_flags_land_on_the_ten_parts()
    {
        var boat = BoatLoader.LoadSloop28();
        Assert.Equal((180, 25, 1500, 6000, 20000, 0.015), (boat.Calendar.EveningMinutes, boat.Calendar.VacationDaysPerYear, boat.Calendar.UnpaidLeaveNok, boat.Calendar.PayNok, boat.Calendar.OverdraftLimitNok, boat.Calendar.InterestPerMonth));
        Assert.Equal(["berth", "insurance"], boat.Calendar.Bills.Select(b => b.Id));
        Assert.Equal([3, 10], boat.Orders.LeadDays.ToArray());
        Assert.Equal(("skjaervik", 5), (boat.Orders.DeliverTo, boat.Orders.PhoneMinutes));
        Assert.Equal(
            ["alternator", "engine-block", "exhaust-elbow", "fuel-tank", "heat-exchanger", "injection-pump", "injectors", "raw-water-pump", "sprayhood", "starter-motor"],
            boat.Components.Where(c => !c.Spec.Stocked).Select(c => c.Id).Order());
        Assert.Contains(boat.Components, c => c.Id == "impeller" && c.Spec.Stocked);
    }
}
