using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// The season page, reputation and keeping her (design note 010, step 4). The yardstick is the logbook, not a score: at the autumn lift the
/// logbook writes a page of what the season was (miles, nights away, places, events, hours, money, what she is worth), with no grade. Reputation
/// at the yard and the harbour is recomputed from counters the game already keeps and is only ever told in words. Nothing here draws from the
/// world or the player's random.
/// </summary>
public sealed partial class Game
{
    private int _slotsKept;
    private int _slotsMissed;
    private int _partings;
    private int _billsOnTime;
    private int _paydays;
    private int _nightsAway;
    /// <summary>The first day of this season (the start of the game, or the last Keep) and the work minutes already done by then.</summary>
    private int? _seasonFromDay;
    private int _seasonWorkBase;
    private readonly List<(int Day, string Place)> _arrivals = [];
    private readonly List<SeasonPageView> _seasonPages = [];

    // ------------------------------------------------------------------ reputation

    /// <summary>The standing at the yard and the harbour, from the counters. Never shown as a number.</summary>
    private int ReputationScore
    {
        get
        {
            var r = _boat.Reputation;
            int bills = r.OnTimeBillsPerPoint > 0 ? _billsOnTime / r.OnTimeBillsPerPoint : 0;
            int raw = bills + r.SlotKept * _slotsKept + r.Tow * _tows + r.MooringParted * _partings
                + r.SlotMissed * _slotsMissed + r.BillPastLimit * _billsPastLimit + r.PatrolFine * _patrolFines;
            return Math.Clamp(raw, r.Min, r.Max);
        }
    }

    /// <summary>For tests only (a player hears the yard's manner, never a number): the standing in points.</summary>
    internal int ReputationPoints => ReputationScore;

    /// <summary>The sentence for the standing; null when the data has no words.</summary>
    private string? ReputationWords => _boat.Reputation.Words.Count == 0 ? null : Band.Pick(_boat.Reputation.Words, ReputationScore);

    /// <summary>What the towboat charges her: the data's price, a little less for a skipper who knows you, a little more for one who doesn't.</summary>
    private int TowPriceNok
    {
        get
        {
            int nok = _boat.Trips?.Tow.Nok ?? 0;
            int score = ReputationScore;
            if (score == 0 || _boat.Reputation.TowFactorPerPoint == 0) return nok;
            double factor = Math.Max(0, 1 - _boat.Reputation.TowFactorPerPoint * score);
            return (int)Math.Round(nok * factor / 100.0, MidpointRounding.AwayFromZero) * 100;
        }
    }

    // ------------------------------------------------------------------ the midnight

    /// <summary>A night spent afloat at a port other than home, or at sea, is a night away.</summary>
    private void NightsDaily()
    {
        if (_boat.Trips is not { } trips || _sunk || !Afloat) return;
        if (AtSea || CurrentPort is { } port && port.Id != trips.Home) _nightsAway++;
    }

    // ------------------------------------------------------------------ the page

    private static string Lower(string name) =>
        name.StartsWith("The ", StringComparison.Ordinal) || name.StartsWith("A ", StringComparison.Ordinal) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    private static string AndList(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>The events of the clock year, split into made and missed (with the reason). Windows still to come are not counted.</summary>
    private (List<string> Made, List<string> Missed) SeasonEvents(int year, int today)
    {
        var made = new List<string>();
        var missed = new List<string>();
        foreach (var e in EventRules)
        {
            var rec = _eventRecords.FirstOrDefault(r => r.Id == e.Id && r.Year == year);
            string name = Lower(e.Name);
            if (rec is { Status: EventStatus.Made }) { made.Add(name); continue; }
            var window = WindowOf(e, year);
            int end = window?.To ?? (e.Pick is { } p ? year * GameClock.DaysPerYear + p.ToDayOfYear : int.MaxValue);
            if (rec is { Status: EventStatus.Failed, FailedRequirement: { } why }) missed.Add($"{name} ({why})");
            else if (rec is { Status: EventStatus.Missed }) missed.Add(name);
            else if (today > end) missed.Add(window is null ? $"{name} (you never asked)" : name);
        }
        return (made, missed);
    }

    private SeasonPageView BuildSeasonPage(int toDay)
    {
        var ci = CultureInfo.InvariantCulture;
        int from = _seasonFromDay ?? _startDay;
        double miles = _pastPassages.Where(p => p.Day >= from).Sum(p => p.Nm);
        var places = _arrivals.Where(a => a.Day >= from).Select(a => a.Place).Distinct().ToList();
        var (made, missed) = SeasonEvents(new GameClock(toDay).Year, toDay);
        int hours = (int)Math.Round((_time.WorkMinutes - _seasonWorkBase) / 60.0, MidpointRounding.AwayFromZero);
        bool charges = _boat.Calendar.ChargesDays;
        int vacation = charges ? _vacationUsed : 0, unpaid = charges ? _unpaidDays : 0;
        var season = _ledger.Skip(_ledgerFrom).ToList();
        int spent = season.Where(e => e.Kind != LedgerIncome).Sum(e => -e.Nok);
        int income = season.Where(e => e.Kind == LedgerIncome).Sum(e => e.Nok);
        int value = BuildLetter(toDay).ValueNok;
        string reputation = ReputationWords ?? "";
        int number = _seasonIndex + 1;
        string home = _boat.Trips is { } trips ? PortName(trips.Home) : "her mooring";

        var lines = new List<string> { $"Season {number}, day {from} to day {toDay}." };
        int whole = (int)Math.Round(miles, MidpointRounding.AwayFromZero);
        var travel = new List<string>
        {
            whole == 0 ? "No miles sailed" : $"{whole} {(whole == 1 ? "mile" : "miles")} sailed",
            _nightsAway == 0 ? $"no nights away from {home}" : $"{Counted(_nightsAway, "night", "nights")} away from {home}",
        };
        if (places.Count > 0) travel.Add("reached " + AndList(places));
        lines.Add(string.Join("; ", travel) + ".");
        if (EventRules.Count > 0)
        {
            var parts = new List<string>();
            if (made.Count > 0) parts.Add("Made: " + string.Join("; ", made) + ".");
            if (missed.Count > 0) parts.Add("Missed: " + string.Join("; ", missed) + ".");
            if (parts.Count == 0) parts.Add("Nothing on the calendar came due.");
            lines.Add(string.Join(" ", parts));
        }
        var work = new List<string> { $"{hours} {(hours == 1 ? "hour" : "hours")} of your own work" };
        if (charges) work.Add(vacation == 0 ? "no vacation days" : Counted(vacation, "vacation day", "vacation days"));
        if (unpaid > 0) work.Add($"{Counted(unpaid, "day", "days")} of unpaid leave");
        lines.Add(string.Join(", ", work) + ".");
        string cost = $"She cost you {spent} NOK this season" + (income > 0 ? $"; paydays put {income} NOK into her account" : "");
        lines.Add($"{cost}. The broker would put her at {value} NOK.");
        if (reputation.Length > 0) lines.Add(reputation);

        return new SeasonPageView(number, from, toDay, Math.Round(miles, 1), _nightsAway, places, made, missed, hours, vacation, unpaid,
            spent, income, value, reputation, lines);
    }

    /// <summary>Writes the season's page once: a logbook document and its lines in the narrative of the lift.</summary>
    private void WriteSeasonPage(Turn t, int liftDay)
    {
        if (_seasonPages.Any(p => p.Season == _seasonIndex + 1)) return;
        var page = BuildSeasonPage(liftDay);
        _seasonPages.Add(page);
        t.Narrative.AddRange(page.Lines);
        _log.Add(new LogEntry(Day, TimeOfDay, $"The page of season {page.Season} is written: day {page.FromDay} to day {page.ToDay}.", 0, $"season {page.Season}"));
    }

    private SeasonPageView SeasonPageSnapshot(int? season)
    {
        var page = season is { } n ? _seasonPages.FirstOrDefault(p => p.Season == n) : _seasonPages.LastOrDefault();
        return page ?? new SeasonPageView(0, 0, 0, 0, 0, [], [], [], 0, 0, 0, 0, 0, 0, "",
            [season is { } m ? $"There is no page for season {m}." : "No season page yet: it is written at the autumn haulout."]);
    }

    // ------------------------------------------------------------------ keeping her

    /// <summary>What Keep tells the player of the year ahead: the events of the calendar and the vacation budget. Null: nothing to say.</summary>
    private string? NextSeasonLine()
    {
        var parts = new List<string>();
        string? friend = null;
        int year = ListedYear();
        foreach (var e in EventRules.Where(e => e.Pick is null).OrderBy(e => WindowOf(e, year)?.From ?? year * GameClock.DaysPerYear + e.FromDayOfYear))
        {
            if (WindowOf(e, year) is not { } w || w.From < Day) continue;
            parts.Add(w.From == w.To
                ? $"{Lower(e.Name)} on {new GameClock(w.From).WeekdayName}, day {w.From}"
                : $"{Lower(e.Name)}, days {w.From} to {w.To}");
        }
        // The friend's week is the one event the player picks, so it comes last.
        if (EventRules.FirstOrDefault(e => e.Pick is not null) is { Pick: { } pick } && year * GameClock.DaysPerYear + pick.ToDayOfYear >= Day)
            friend = $"your friend can come for a week in {new GameClock(year * GameClock.DaysPerYear + pick.FromDayOfYear).MonthName}";
        if (friend is not null) parts.Add(friend);
        var sentences = new List<string>();
        if (parts.Count > 0) sentences.Add("Next season: " + string.Join("; ", parts) + ".");
        if (_boat.Calendar.ChargesDays) sentences.Add($"{_boat.Calendar.VacationDaysPerYear} vacation days from the new year.");
        return sentences.Count == 0 ? null : string.Join(" ", sentences);
    }

    private int EventsMadeCount => _eventRecords.Count(r => r.Status == EventStatus.Made);

    private int EventsMissedCount => _eventRecords.Count(r =>
        r.Status == EventStatus.Missed
        || r.Status == EventStatus.Failed && EventRules.FirstOrDefault(e => e.Id == r.Id) is { } e && WindowOf(e, r.Year) is { } w && Day > w.To);
}
