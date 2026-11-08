using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// The calendar of wants (design note 010, step 2). Events are data: a window, a place, requirements and a story. An event is made when the
/// player is aboard at its place in its window and every requirement holds in truth; one that fails shows the fault behind it. Missed, it is gone
/// for the year and the logbook says so. The views say only what the player knows. No state but one record per (event, year) and the friend's
/// chosen week; nothing here draws a random number.
/// </summary>
public sealed partial class Game
{
    private enum EventStatus { Open, Made, Failed, Missed }

    private sealed class EventRecord
    {
        public string Id { get; init; } = "";
        public int Year { get; init; }
        public EventStatus Status { get; set; } = EventStatus.Open;
        /// <summary>The requirement (in words) that failed last; told once.</summary>
        public string? FailedRequirement { get; set; }
        /// <summary>No second try (the friend's week).</summary>
        public bool Closed { get; set; }
        public string Story { get; set; } = "";
    }

    private readonly List<EventRecord> _eventRecords = [];
    /// <summary>The week the player asked their friend for, by year: the start day.</summary>
    private readonly Dictionary<int, int> _friendStart = [];
    /// <summary>Weekdays of the friend's week charged at once, and the ones among them that were unpaid leave.</summary>
    private readonly HashSet<int> _prepaid = [];
    private readonly HashSet<int> _prepaidUnpaid = [];

    private IReadOnlyList<EventRule> EventRules => _boat.Calendar.Events;

    private EventRecord RecordFor(EventRule e, int year)
    {
        var rec = _eventRecords.FirstOrDefault(r => r.Id == e.Id && r.Year == year);
        if (rec is null) _eventRecords.Add(rec = new EventRecord { Id = e.Id, Year = year });
        return rec;
    }

    private static readonly string[] WeekdayKeys = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    /// <summary>The window of the event in the clock year: the first day on or after its day of year with the right weekday, for its days. Null: a pick the player has not made.</summary>
    private (int From, int To)? WindowOf(EventRule e, int year)
    {
        if (e.Pick is not null)
            return _friendStart.TryGetValue(year, out var s) ? (s, s + e.Days - 1) : null;
        int start = year * GameClock.DaysPerYear + e.FromDayOfYear;
        int weekday = e.Weekday is { Length: > 0 } w ? Array.IndexOf(WeekdayKeys, w.ToLowerInvariant()) : -1;
        if (weekday >= 0) while (new GameClock(start).Weekday != weekday) start++;
        return (start, start + e.Days - 1);
    }

    private string PlaceName(string portId) => _boat.Trips?.FindPort(portId)?.Name ?? portId;

    // ------------------------------------------------------------------ the truth of a requirement

    private bool RequirementHolds(RequirementRule r) => r.Kind switch
    {
        "fitted" => UpgradeWorks(r.Component),
        "working" => ComponentWorks(r.Component),
        "dryBunks" => UpgradeWorks(r.Component) && Day - _lastOverSoleDay >= r.Days,
        "warm" => UpgradeWorks(r.Component) || _weather[Day].TempC >= r.BelowC,
        _ => true,
    };

    // ------------------------------------------------------------------ made, failed

    /// <summary>
    /// After an accepted command not at sea, and on arrival: every open event whose window holds today, at the port she lies at, afloat, with
    /// the player aboard, is judged against the truth. (Overnight events wait for the midnight.)
    /// </summary>
    private void CheckEvents(Turn t)
    {
        if (EventRules.Count == 0 || !Afloat) return;
        int year = new GameClock(Day).Year;
        foreach (var e in EventRules)
        {
            if (e.Overnight || e.Port != _portId) continue;
            if (WindowOf(e, year) is not { } w || Day < w.From || Day > w.To) continue;
            if (e.Pick is not null && Day != w.From) continue;
            var rec = RecordFor(e, year);
            if (rec.Closed || rec.Status is EventStatus.Made or EventStatus.Missed) continue;
            Judge(t, e, rec, w);
        }
    }

    private void Judge(Turn t, EventRule e, EventRecord rec, (int From, int To) w)
    {
        var failing = e.Requires.FirstOrDefault(r => !RequirementHolds(r));
        if (failing is null)
        {
            rec.Status = EventStatus.Made;
            rec.Story = e.MadeLines.Count > 0 ? e.MadeLines[0] : "";
            t.Narrative.AddRange(e.MadeLines);
            _log.Add(new LogEntry(Day, TimeOfDay, $"Made it: {e.Name}."));
            Raise($"event-{e.Id}", rec.Story.Length > 0 ? rec.Story : $"Made it: {e.Name}.", 0, onPassage: false);
            if (e.Pick is not null) ChargeFriendWeek(t, w);
            return;
        }
        if (rec.Status == EventStatus.Failed && rec.FailedRequirement == failing.Text) return; // told once
        rec.Status = EventStatus.Failed;
        rec.FailedRequirement = failing.Text;
        if (e.Pick is not null) rec.Closed = true;
        t.Narrative.Add(e.FailedLines.TryGetValue(failing.Component, out var line)
            ? line
            : $"{e.Name}: {failing.Text} was not there, and it showed.");
        // The event is a symptom: the truth showed itself, and the fault behind it is found.
        if (_boat.TryGet(failing.Component, out var c))
            foreach (var f in c.ActiveFaults.ToList()) DiscoverNow(t, f);
        _log.Add(new LogEntry(Day, TimeOfDay, $"Missed: {e.Name} ({failing.Text})."));
    }

    // ------------------------------------------------------------------ the midnight

    /// <summary>The events' part of the midnight that ends <paramref name="day"/>: overnight events are judged, windows close, the friend leaves.</summary>
    private void EventsDaily(Turn t, int day)
    {
        if (EventRules.Count == 0) return;
        int year = new GameClock(day).Year;
        foreach (var e in EventRules)
        {
            if (WindowOf(e, year) is not { } w || day < w.From || day > w.To) continue;
            var rec = RecordFor(e, year);
            if (e.Overnight && day == w.From && !rec.Closed && rec.Status is EventStatus.Open or EventStatus.Failed
                && Afloat && !AtSea && e.Port == _portId && _workMinutesToday > 0)
                Judge(t, e, rec, w);

            // The friend is only there on the first day; the others are open to their last.
            int lastChance = e.Pick is not null ? w.From : w.To;
            if (day == lastChance && rec.Status == EventStatus.Open)
            {
                rec.Status = EventStatus.Missed;
                Raise($"event-missed-{e.Id}", e.MissedText.Length > 0 ? e.MissedText : $"{e.Name} came and went without you.", 0, onPassage: false);
            }
            if (e.Pick is not null && day == w.To && rec.Status == EventStatus.Made && _boat.TryGet(FuelTankId, out var tank)
                && tank.State.ContainsKey(FuelLitresProperty) && FuelLitres < TankCapacity)
            {
                tank.State[FuelLitresProperty] = TankCapacity;
                Raise("friend-tank", "Your friend topped up the tank before they went, and wouldn't hear about paying.", 0, onPassage: false);
            }
        }
    }

    /// <summary>The friend's week is on: fuel is free (the friend pays).</summary>
    private bool FriendPays
    {
        get
        {
            if (EventRules.FirstOrDefault(e => e.Pick is not null) is not { } e) return false;
            int year = new GameClock(Day).Year;
            return WindowOf(e, year) is { } w && Day >= w.From && Day <= w.To
                && _eventRecords.Any(r => r.Id == e.Id && r.Year == year && r.Status == EventStatus.Made);
        }
    }

    /// <summary>Every weekday of the friend's week costs a vacation day (or unpaid leave) at once, in one line.</summary>
    private void ChargeFriendWeek(Turn t, (int From, int To) w)
    {
        var rules = _boat.Calendar;
        if (!rules.ChargesDays) return;
        int vacation = 0, unpaid = 0;
        for (int d = w.From; d <= w.To; d++)
        {
            if (new GameClock(d).IsWeekend || d == _chargedDay || _prepaid.Contains(d)) continue;
            _prepaid.Add(d);
            if (_vacationLeft > 0)
            {
                _vacationLeft--;
                _vacationUsed++;
                vacation++;
                continue;
            }
            _unpaidDays++;
            unpaid++;
            _prepaidUnpaid.Add(d);
            Spend(rules.UnpaidLeaveNok, LedgerUnpaid, $"Unpaid leave: {new GameClock(d).WeekdayName}, day {d}.");
        }
        if (vacation + unpaid == 0) return;
        var parts = new List<string>();
        if (vacation > 0) parts.Add($"{Counted(vacation, "vacation day", "vacation days")} ({_vacationLeft} left this year)");
        if (unpaid > 0) parts.Add($"{Counted(unpaid, "day", "days")} of unpaid leave ({unpaid * rules.UnpaidLeaveNok} NOK off your pay)");
        string line = $"The week costs {string.Join(" and ", parts)}.";
        t.Narrative.Add(line);
        _log.Add(new LogEntry(Day, TimeOfDay, line, unpaid * rules.UnpaidLeaveNok));
    }

    // ------------------------------------------------------------------ the friend

    private CommandResult DoInviteFriend(Turn t, int startDay)
    {
        const string action = "invite-friend";
        if (AtSea) return Reject(action, "You're at sea: ring your friend from port.");
        if (EventRules.FirstOrDefault(e => e.Pick is not null) is not { Pick: { } pick } e) return Reject(action, "There's nobody to ask.");
        int year = new GameClock(Day).Year;
        if (WindowOf(e, year) is { } booked)
            return Reject(action, Day > booked.To
                ? "Your friend came already this year."
                : $"Your friend is coming on day {booked.From}.");
        int from = year * GameClock.DaysPerYear + pick.FromDayOfYear, to = year * GameClock.DaysPerYear + pick.ToDayOfYear;
        string month = new GameClock(from).MonthName;
        if (Day > to) return Reject(action, "It's too late this year: your friend's week has gone.");
        if (startDay < from || startDay > to)
            return Reject(action, $"Your friend can come in {month}: a week starting between day {from} and day {to}.");
        if (startDay < Day + pick.NoticeDays)
            return Reject(action, $"Your friend needs {pick.NoticeDays} days' notice: the earliest is day {Day + pick.NoticeDays}.");

        _friendStart[year] = startDay;
        var clock = new GameClock(startDay);
        t.Narrative.Add($"You ring your friend. They'll come on {clock.WeekdayName}, day {startDay}, for a week.");
        return Accept(t, action, _boat.Orders.PhoneMinutes, 0, null, $"Asked a friend aboard for a week from day {startDay}.", work: false);
    }

    // ------------------------------------------------------------------ views

    private int ListedYear()
    {
        int year = new GameClock(Day).Year;
        foreach (var e in EventRules)
        {
            int end = WindowOf(e, year)?.To ?? (e.Pick is { } p ? year * GameClock.DaysPerYear + p.ToDayOfYear : int.MinValue);
            if (end >= Day) return year;
        }
        return EventRules.Count == 0 ? year : year + 1;
    }

    private static string DaysWords(int from, int to)
    {
        var a = new GameClock(from).WeekdayName;
        if (from == to) return $"{a}, day {from}";
        var b = new GameClock(to).WeekdayName;
        return to - from == 1 ? $"{a} and {b}, days {from} to {to}" : $"{a} to {b}, days {from} to {to}";
    }

    private string WhenWords(EventRule e, int year)
    {
        string next = year > new GameClock(Day).Year ? " (next year)" : "";
        if (WindowOf(e, year) is { } w) return DaysWords(w.From, w.To) + next;
        var p = e.Pick!;
        int from = year * GameClock.DaysPerYear + p.FromDayOfYear, to = year * GameClock.DaysPerYear + p.ToDayOfYear;
        return $"a week of your choice in {new GameClock(from).MonthName}, days {from} to {to}{next}";
    }

    private EventView ViewOf(EventRule e, int year)
    {
        var window = WindowOf(e, year);
        var rec = _eventRecords.FirstOrDefault(r => r.Id == e.Id && r.Year == year);
        int from = window?.From ?? year * GameClock.DaysPerYear + (e.Pick?.FromDayOfYear ?? e.FromDayOfYear);
        int to = window?.To ?? year * GameClock.DaysPerYear + (e.Pick?.ToDayOfYear ?? e.FromDayOfYear);
        string status = rec switch
        {
            { Status: EventStatus.Made } => "made",
            { Status: EventStatus.Failed } => $"failed: {rec.FailedRequirement}",
            { Status: EventStatus.Missed } => "missed",
            _ => Day > to ? "missed" : window is { } w && Day >= w.From ? "on now" : "ahead",
        };
        return new EventView(e.Name, PlaceName(e.Port), from, to, WhenWords(e, year), status,
            e.Requires.Select(r => RequirementView(r, window)).ToList(), rec?.Story ?? "");
    }

    /// <summary>What the player can tell of a requirement: what is fitted, the problems they have found, the dry days, the forecast. Never the truth they have not seen.</summary>
    private Requirement RequirementView(RequirementRule r, (int From, int To)? window)
    {
        var known = KnownOpenFaults().FirstOrDefault(f => f.ComponentId == r.Component);
        bool aboard = _boat.TryGet(r.Component, out _) && !_gone.Contains(r.Component);
        switch (r.Kind)
        {
            case "fitted":
                return !aboard ? new(r.Text, false, "not aboard")
                    : known is not null ? new(r.Text, false, known.Name) : new(r.Text, true, "fitted");
            case "working":
                return known is not null ? new(r.Text, false, known.Name) : new(r.Text, true, "no known problem");
            case "dryBunks":
                if (!aboard) return new(r.Text, false, "not aboard");
                if (known is not null) return new(r.Text, false, known.Name);
                return Day - _lastOverSoleDay < r.Days
                    ? new(r.Text, false, $"water was over the sole {Day - _lastOverSoleDay} days ago")
                    : new(r.Text, true, "dry");
            case "warm":
                if (aboard && known is null) return new(r.Text, true, "heater fitted");
                if (window is { } w && w.From >= Day && w.From - Day <= _boat.ForecastRules.MaxLeadDays)
                    return _weather[w.From].TempC >= r.BelowC
                        ? new(r.Text, true, "the forecast is warm enough")
                        : new(r.Text, false, "the forecast is cold and there is no heater");
                return new(r.Text, null, "depends on the weather");
            default:
                return new(r.Text, null, "not known yet");
        }
    }

    private List<EventView> EventViews()
    {
        int year = ListedYear();
        return EventRules
            .Select(e => (E: e, From: WindowOf(e, year)?.From ?? year * GameClock.DaysPerYear + (e.Pick?.FromDayOfYear ?? e.FromDayOfYear)))
            .OrderBy(x => x.From).Select(x => ViewOf(x.E, year)).ToList();
    }

    /// <summary>The first day of every event window that is still open from today on (a made or missed event is not ahead).</summary>
    private IEnumerable<(int Day, string Name)> OpenWindowStarts()
    {
        int year = new GameClock(Day).Year;
        foreach (int y in new[] { year, year + 1 })
            foreach (var e in EventRules)
            {
                if (WindowOf(e, y) is not { } w || w.From < Day) continue;
                var rec = _eventRecords.FirstOrDefault(r => r.Id == e.Id && r.Year == y);
                if (rec is { Status: EventStatus.Made or EventStatus.Missed or EventStatus.Failed }) continue;
                yield return (w.From, e.Name);
            }
    }

    /// <summary>The events at this port whose window holds the day, with what they ask, for the plan of a trip.</summary>
    private List<string> EventsAt(string portId, int day)
    {
        var lines = new List<string>();
        int year = new GameClock(day).Year;
        foreach (var e in EventRules.Where(e => e.Port == portId && e.Pick is null))
        {
            if (WindowOf(e, year) is not { } w || day < w.From || day > w.To) continue;
            var rec = _eventRecords.FirstOrDefault(r => r.Id == e.Id && r.Year == year);
            if (rec is { Status: EventStatus.Made or EventStatus.Missed }) continue;
            string asks = e.Requires.Count == 0
                ? "asks only that you are there"
                : "asks for " + string.Join("; ", e.Requires.Select(r =>
                {
                    var v = RequirementView(r, w);
                    return $"{r.Text} ({(v.Met == true ? v.Note : v.Met == false ? "not met: " + v.Note : v.Note)})";
                }));
            lines.Add($"{e.Name} ({DaysWords(w.From, w.To)}) {asks}.");
        }
        return lines;
    }
}
