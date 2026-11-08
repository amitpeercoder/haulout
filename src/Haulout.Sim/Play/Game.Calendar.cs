using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The week and the money (design note 010, step 1). Day 0 is a Monday: weekdays are work, weekends are boat days. More than an evening
/// aboard on a weekday, and every weekday at sea, costs a vacation day (unpaid leave when they are spent). Pay lands on the first of the
/// month, standing bills fall due on the calendar whether or not the player is there, a debt costs interest, and the week passing is the turn.
/// Nothing here draws from the world or the player's random: the only roll is <see cref="SeasonRoll"/> in the orders.
/// </summary>
public sealed partial class Game
{
    private const string LedgerIncome = "income";
    private const string LedgerSpending = "spending";
    private const string LedgerBill = "bill";
    private const string LedgerInterest = "interest";
    private const string LedgerUnpaid = "unpaid";

    private readonly List<LedgerEntry> _ledger = [];
    /// <summary>Where in the ledger the season began (the start of the game, or the last Keep).</summary>
    private int _ledgerFrom;
    /// <summary>Bills the player did not choose that left the balance past the bank's limit (reputation, step 4).</summary>
    private int _billsPastLimit;

    private int _vacationLeft;
    private int _vacationUsed;
    private int _unpaidDays;
    /// <summary>The last day that was charged, and whether it was unpaid leave.</summary>
    private int _chargedDay = -1;
    private bool _chargedUnpaid;
    /// <summary>What came in during a wait, in words, for the week line. Cleared at the start of each wait.</summary>
    private readonly List<string> _weekEvents = [];

    private bool CalendarOn =>
        _boat.Calendar is { PayNok: > 0 } or { Bills.Count: > 0 } or { OverdraftLimitNok: > 0 } or { ChargesDays: true };

    // ------------------------------------------------------------------ the ledger

    /// <summary>Money that goes out. The balance may go below zero (a bill the player did not choose); a purchase checks <see cref="MoneyRefusal"/> first.</summary>
    private void Spend(int nok, string kind, string text)
    {
        if (nok <= 0) return;
        _money += nok;
        _ledger.Add(new LedgerEntry(Day, kind, text, -nok));
        if (kind == LedgerBill && _boat.Calendar.OverdraftLimitNok > 0 && Balance < -_boat.Calendar.OverdraftLimitNok) _billsPastLimit++;
    }

    /// <summary>Money that comes in: the pay, the sale.</summary>
    private void Earn(int nok, string text)
    {
        if (nok <= 0) return;
        _incomeNok += nok;
        _ledger.Add(new LedgerEntry(Day, LedgerIncome, text, nok));
    }

    private string? MoneyRefusal(int costNok)
    {
        int limit = _boat.Calendar.OverdraftLimitNok;
        if (limit <= 0)
            return costNok <= Balance ? null
                : Balance < 0 ? $"That's {Money(costNok)} and you owe {Money(-Balance)} already."
                : $"That's {Money(costNok)} and you have {Money(Balance)} left.";
        if (costNok <= 0 || Balance - costNok >= -limit) return null;
        return Balance >= -limit
            ? $"That's {Money(costNok)}, and you have {Money(Balance + limit)} left before the bank's limit ({Money(limit)} overdrawn)."
            : $"You're {Money(-Balance)} overdrawn, past the bank's limit of {Money(limit)}: nothing more until payday brings you back under.";
    }

    // ------------------------------------------------------------------ the weekday rule

    /// <summary>
    /// Charges <paramref name="day"/> once, if it is a weekday: a vacation day while any are left this year, else unpaid leave. Said at the
    /// moment it is incurred, so the player knows while choosing. Weekends and time away never come here.
    /// </summary>
    private void ChargeDay(Turn t, int day, bool atSea)
    {
        var rules = _boat.Calendar;
        if (!rules.ChargesDays || _chargedDay == day) return;
        var clock = new GameClock(day);
        if (clock.IsWeekend) return;
        _chargedDay = day;
        string name = clock.WeekdayName;
        if (_vacationLeft > 0)
        {
            _vacationLeft--;
            _vacationUsed++;
            _chargedUnpaid = false;
            t.Narrative.Add(atSea
                ? $"A weekday at sea: today, {name}, is a vacation day ({_vacationLeft} left this year)."
                : $"That's more than an evening's work: today, {name}, becomes a vacation day ({_vacationLeft} left this year).");
            _log.Add(new LogEntry(day, TimeOfDay, $"Vacation day: {name}, day {day}."));
            return;
        }
        _unpaidDays++;
        _chargedUnpaid = true;
        Spend(rules.UnpaidLeaveNok, LedgerUnpaid, $"Unpaid leave: {name}, day {day}.");
        t.Narrative.Add($"No vacation days left: today, {name}, is unpaid leave. {Money(rules.UnpaidLeaveNok)} comes off your pay.");
        _log.Add(new LogEntry(day, TimeOfDay, $"Unpaid leave: {name}, day {day}.", rules.UnpaidLeaveNok));
    }

    // ------------------------------------------------------------------ the midnight

    /// <summary>The calendar's part of the midnight: the new year's vacation, payday with its interest, the bills, the deliveries.</summary>
    private void CalendarDaily(int day)
    {
        int today = day + 1; // the clock reads 00:00 of this day
        var rules = _boat.Calendar;
        if (today % GameClock.DaysPerYear == 0)
        {
            _vacationLeft = rules.VacationDaysPerYear;
            _vacationUsed = 0;
            _unpaidDays = 0;
        }
        if (today % GameClock.DaysPerMonth == 0)
        {
            if (rules.InterestPerMonth > 0 && Balance < 0)
            {
                int interest = (int)Math.Ceiling(-Balance * rules.InterestPerMonth);
                Spend(interest, LedgerInterest, "Overdraft interest for the month.");
                RaiseCalendar("interest", $"The bank took {Money(interest)} of overdraft interest.", interest);
                _weekEvents.Add($"overdraft interest {Money(interest)}");
            }
            if (rules.PayNok > 0)
            {
                Earn(rules.PayNok, "Payday.");
                _paydays++;
                RaiseCalendar("payday", $"Payday: {Money(rules.PayNok)}.", 0);
                _weekEvents.Add($"payday {Money(rules.PayNok)}");
            }
        }
        foreach (var bill in rules.Bills)
        {
            if (today < bill.FromDay || !bill.DaysOfYear.Contains(today % GameClock.DaysPerYear)) continue;
            Spend(bill.Nok, LedgerBill, bill.Text);
            if (rules.OverdraftLimitNok > 0 && Balance >= -rules.OverdraftLimitNok) _billsOnTime++;
            RaiseCalendar($"bill-{bill.Id}", $"{bill.Text}: {Money(bill.Nok)}.", bill.Nok);
            _weekEvents.Add($"{(bill.Short.Length > 0 ? bill.Short : bill.Id)} {Money(bill.Nok)}");
        }
        DeliverOrders();
    }

    /// <summary>An alert and its logbook line (with the cost, for a bill) that never goes on a passage's list of what went wrong.</summary>
    private void RaiseCalendar(string kind, string text, int costNok) => Raise(kind, text, costNok, onPassage: false);

    /// <summary>The line at the end of a wait: what came in while the player was away. Nothing when nothing did.</summary>
    private void AddWeekLine(Turn t)
    {
        if (_weekEvents.Count == 0) return;
        t.Narrative.Add("While you were away: " + string.Join("; ", _weekEvents) + ".");
        _weekEvents.Clear();
    }

    // ------------------------------------------------------------------ waits that end on a day

    /// <summary>Minutes from now to 08:00 on the absolute day <paramref name="targetDay"/>.</summary>
    private int MinutesToMorning(int targetDay) => targetDay * 1440 + StartMinuteOfDay - (_startDay * 1440 + StartMinuteOfDay + ClockMinutes);

    /// <summary>
    /// A wait that would pass the morning of something the player was told about (the booked slot, a delivery due at the yard, an event they marked) stops on
    /// that morning, at the same time of day the wait would have ended. A wait that starts on that day is not stopped by it. The first wins.
    /// </summary>
    private int StopAtCalendar(Turn t, int minutes)
    {
        int endDay = DayAt(ClockMinutes + minutes);
        int best = minutes;
        string? why = null;
        // A stop on a morning is at the same time of day the wait would have ended; the first of equal stops wins.
        void StopAtDay(int day, string text)
        {
            int m = minutes - (endDay - day) * 1440;
            if (m < best || why is null && m <= best) { best = m; why = text; }
        }
        void StopAtMinutes(int m, string text)
        {
            if (m < best) { best = m; why = text; }
        }
        if (_bookedDay is { } booked && booked > Day && booked <= endDay)
            StopAtDay(booked, $"You come back on day {booked} for your haulout slot.");
        foreach (var o in _orders.Where(o => !o.Arrived && o.DueDay > Day && o.DueDay <= endDay))
            StopAtDay(o.DueDay, $"You come back on day {o.DueDay}: the {o.Name} is due at the yard.");
        foreach (var (eventDay, name, _) in OpenWindowStarts().Where(e => e.Going && e.Day > Day && e.Day <= endDay))
            StopAtDay(eventDay, $"You come back on day {eventDay}: {name} starts today.");
        WeekStops(minutes, endDay, StopAtDay, StopAtMinutes);
        if (why is null) return minutes;
        t.Narrative.Add(why);
        return best;
    }

    private CommandResult DoNextWeekend(Turn t)
    {
        const string action = "next-weekend";
        if (AtSea) return Reject(action, "You're at sea: continue, heave to or anchor.");
        int target = Day + 1;
        while (new GameClock(target).Weekday != 5) target++;
        // From a Saturday or Sunday it is the Saturday after; from a weekday, the coming one (the loop above starts tomorrow).
        var name = new GameClock(target).WeekdayName;
        return DoWaitUntilMorning(t, action, target,
            $"go back to work until {name}",
            $"You go back to work. {name}, day {target}: you're back aboard at eight.",
            $"Waited until {name}, day {target}.");
    }

    private CommandResult DoWaitWeek(Turn t)
    {
        const string action = "wait-week";
        if (AtSea) return Reject(action, "You're at sea: continue, heave to or anchor.");
        int target = Day + 7;
        var name = new GameClock(target).WeekdayName;
        return DoWaitUntilMorning(t, action, target,
            "go back to work for a week",
            $"You go back to work for a week. {name}, day {target}: you're back aboard at eight.",
            "Waited a week.");
    }

    private CommandResult DoWaitUntilMorning(Turn t, string action, int targetDay, string meant, string done, string logDone)
    {
        ShutDownEngine(t);
        _weekEvents.Clear();
        int total = MinutesToMorning(targetDay);
        int minutes = StopAtCalendar(t, total);
        bool cut = minutes != total;
        if (Afloat) return DoWaitMoored(t, action, minutes, meant, cut ? null : done, cut ? null : logDone);

        if (!cut) t.Narrative.Add(done);
        int days = DayAt(ClockMinutes + minutes) - Day;
        t.Narrative.Add(WeatherWords.Summarise(Enumerable.Range(Day, days).Select(d => _weather[d]).ToList()));
        return Accept(t, action, minutes, 0, null, cut ? $"Waited {Days(days)}." : logDone, work: false, afterTick: () =>
        {
            AddWeekLine(t);
            return null;
        });
    }

    // ------------------------------------------------------------------ the query

    private sealed record AheadItem(int Day, string Kind, string Text, string Words, int Nok);

    private IEnumerable<AheadItem> AheadItems()
    {
        var rules = _boat.Calendar;
        int to = Day + rules.AheadDays;
        for (int d = Day; d <= to; d++)
        {
            if (d > Day && d % GameClock.DaysPerMonth == 0 && rules.PayNok > 0)
                yield return new AheadItem(d, "payday", "Payday", "payday", rules.PayNok);
            if (d > Day)
                foreach (var bill in rules.Bills)
                    if (d >= bill.FromDay && bill.DaysOfYear.Contains(d % GameClock.DaysPerYear))
                        yield return new AheadItem(d, "bill", bill.Text, bill.Short.Length > 0 ? bill.Short : bill.Text, -bill.Nok);
            foreach (var o in _orders.Where(o => !o.Arrived && o.DueDay == d && d > Day))
                yield return new AheadItem(d, "delivery", $"The {o.Name} is due at the yard", $"the {o.Name} due at the yard", 0);
            foreach (var (eventDay, name, _) in OpenWindowStarts().Where(e => e.Day == d))
                yield return new AheadItem(d, "event", name, name, 0);
            if (_bookedDay == d)
            {
                var yard = _boat.Yard;
                int storage = new GameClock(d).DayOfYear >= yard.AutumnFromDayOfYear ? yard.StorageNok : 0;
                yield return new AheadItem(d, "slot", "Your haulout slot at the yard", "your haulout slot", -(yard.LiftNok + storage));
            }
        }
    }

    private static string Hours(int minutes) =>
        minutes >= 60 ? (minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min") : $"{minutes} min";

    private bool DayCharged(int day) => _chargedDay == day;

    private bool DayUnpaid(int day) => _chargedDay == day && _chargedUnpaid;

    private int EveningLeft()
    {
        var rules = _boat.Calendar;
        if (!rules.ChargesDays || new GameClock(Day).IsWeekend || DayCharged(Day) || AtSea) return 0;
        return Math.Max(0, rules.EveningMinutes - _workMinutesToday);
    }

    private string WeekLine()
    {
        var clock = new GameClock(Day);
        string head = $"{clock.WeekdayName}, day {Day}:";
        if (clock.IsWeekend) return $"{head} a weekend, a boat day.";
        if (!_boat.Calendar.ChargesDays) return $"{head} a weekday.";
        if (DayCharged(Day)) return DayUnpaid(Day) ? $"{head} a weekday. Today is unpaid leave." : $"{head} a weekday. Today is already a vacation day.";
        if (AtSea) return $"{head} a weekday at sea.";
        return $"{head} a weekday. An evening aboard: {Hours(EveningLeft())} left before it costs a vacation day.";
    }

    private string? NextWords()
    {
        if (!CalendarOn) return null;
        var first = AheadItems().FirstOrDefault();
        if (first is null) return null;
        string nok = first.Nok == 0 ? "" : $", {Money(Math.Abs(first.Nok))}";
        return $"Next: {first.Words} on day {first.Day}{nok}.";
    }

    private CalendarView CalendarSnapshot()
    {
        var rules = _boat.Calendar;
        var clock = new GameClock(Day);
        var ahead = AheadItems().Select(i => new CalendarEntry(i.Day, new GameClock(i.Day).WeekdayName, i.Kind, i.Text, i.Nok)).ToList();
        var orders = _orders.Where(o => !o.Aboard)
            .Select(o => new OrderView(o.Name, o.OrderedDay, o.DueDay, o.Arrived ? "waiting at the yard" : "on its way")).ToList();
        int nextPay = rules.PayNok > 0 ? (Day / GameClock.DaysPerMonth + 1) * GameClock.DaysPerMonth : -1;
        return new CalendarView(
            Day, clock.WeekdayName, clock.IsWeekend, WeekLine(), EveningLeft(),
            rules.ChargesDays ? _vacationLeft : 0, rules.ChargesDays ? rules.VacationDaysPerYear : 0, _vacationUsed, _unpaidDays,
            Balance, rules.OverdraftLimitNok, rules.PayNok, nextPay, ahead, orders, MoneySnapshot(),
            rules.Events.Count > 0 ? EventViews() : null, ReputationWords, _paydays, EventsMadeCount, EventsMissedCount);
    }

    private MoneyView MoneySnapshot()
    {
        var season = _ledger.Skip(_ledgerFrom).ToList();
        int Sum(string kind) => season.Where(e => e.Kind == kind).Sum(e => Math.Abs(e.Nok));
        return new MoneyView(
            Balance, Sum(LedgerIncome), Sum(LedgerSpending), Sum(LedgerBill), Sum(LedgerInterest), Sum(LedgerUnpaid),
            _ledger.AsEnumerable().Reverse().Take(12).ToList());
    }
}
