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

    private enum BeatStatus { Pending, Made, Failed, Missed }

    private sealed class EventRecord
    {
        public string Id { get; init; } = "";
        public int Year { get; init; }
        public EventStatus Status { get; set; } = EventStatus.Open;
        /// <summary>The requirement (in words) that failed last; told once.</summary>
        public string? FailedRequirement { get; set; }
        public string Story { get; set; } = "";
        /// <summary>The beats of a picked week by id (design note 010, second pass H); absent: still to come.</summary>
        public Dictionary<string, BeatStatus> Beats { get; } = [];
        /// <summary>The week is finished (the friend went home, never came, or the last night is done): no more beats.</summary>
        public bool Over { get; set; }
        /// <summary>The key of the week's outcome (good, made, thin) once it is over.</summary>
        public string Outcome { get; set; } = "";
        /// <summary>The event was made early (the club cruise on the Friday evening).</summary>
        public bool Early { get; set; }
        /// <summary>What a failed beat of the week lacked, by beat id, for the beat's view.</summary>
        public Dictionary<string, string> BeatFailed { get; } = [];
    }

    private readonly List<EventRecord> _eventRecords = [];
    /// <summary>The week the player asked their friend for, by year: the start day.</summary>
    private readonly Dictionary<int, int> _friendStart = [];

    /// <summary>The events the player marked as going, by (event, clock year). The friend's week counts once invited (see <see cref="IsGoing"/>).</summary>
    private readonly HashSet<(string Id, int Year)> _going = [];

    /// <summary>A marked event, or the friend's week once the friend is asked: the waits stop on its first morning.</summary>
    private bool IsGoing(EventRule e, int year) => e.Pick is not null ? _friendStart.ContainsKey(year) : _going.Contains((e.Id, year));

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
        "working" => ComponentWorks(r.Component) && r.Components.All(ComponentWorks),
        "anyWorking" => r.Components.Any(AlternativeWorks),
        "dryBunks" => UpgradeWorks(r.Component) && Day - _lastOverSoleDay >= r.Days,
        "warm" => UpgradeWorks(r.Component) || _weather[Day].NightC >= r.BelowC,
        _ => true,
    };

    /// <summary>An alternative of an anyWorking requirement: one component, or several joined by '+', all aboard and without a present fault.</summary>
    private bool AlternativeWorks(string alternative) => alternative.Split('+').All(id => UpgradeWorks(id));

    /// <summary>Every component id an anyWorking or working requirement can be about.</summary>
    private static IEnumerable<string> RequirementComponents(RequirementRule r) =>
        r.Components.SelectMany(a => a.Split('+')).Append(r.Component).Where(id => id.Length > 0).Distinct();

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
            if (e.Overnight || e.Pick is not null || e.Port != _portId) continue;
            if (WindowOf(e, year) is not { } w || Day < w.From || Day > w.To) continue;
            if (e.MadeBy is { } by && Past(w.From + by.Day, by.Hour)) continue;
            var rec = RecordFor(e, year);
            if (rec.Status is EventStatus.Made or EventStatus.Missed) continue;
            Judge(t, e, rec, w);
        }
    }

    /// <summary>Now is after this hour of this day.</summary>
    private bool Past(int day, int hour) => Day > day || Day == day && HourOfDay > hour;

    private void Judge(Turn t, EventRule e, EventRecord rec, (int From, int To) w)
    {
        var failing = e.Requires.FirstOrDefault(r => !RequirementHolds(r));
        if (failing is null)
        {
            rec.Status = EventStatus.Made;
            rec.Story = e.MadeLines.Count > 0 ? e.MadeLines[0] : "";
            // Early (the Friday evening of the club cruise): her own lines come before the story of the event.
            if (e.Early is { } early && !Past(w.From + early.Day, early.Hour))
            {
                rec.Early = true;
                t.Narrative.AddRange(early.Lines);
            }
            t.Narrative.AddRange(e.MadeLines);
            _log.Add(new LogEntry(Day, TimeOfDay, $"Made it: {e.Name}."));
            if (rec.Early && e.Early!.Log.Length > 0) _log.Add(new LogEntry(Day, TimeOfDay, e.Early.Log));
            Raise($"event-{e.Id}", rec.Story.Length > 0 ? rec.Story : $"Made it: {e.Name}.", 0, onPassage: false);
            return;
        }
        if (rec.Status == EventStatus.Failed && rec.FailedRequirement == failing.Text) return; // told once
        rec.Status = EventStatus.Failed;
        rec.FailedRequirement = failing.Text;
        t.Narrative.Add(e.FailedLines.TryGetValue(failing.Key, out var line)
            ? line
            : $"{e.Name}: {failing.Text} was not there, and it showed.");
        // The event is a symptom: the truth showed itself, and the fault behind it is found.
        foreach (var id in RequirementComponents(failing))
            if (_boat.TryGet(id, out var c))
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
            if (e.Pick is not null)
            {
                WeekDaily(t, e, rec, w, day);
                continue;
            }
            if (e.Overnight && day == w.From && rec.Status is EventStatus.Open or EventStatus.Failed
                && Afloat && !AtSea && e.Port == _portId && _workMinutesToday > 0)
                Judge(t, e, rec, w);

            if (day == w.To && rec.Status == EventStatus.Open)
            {
                rec.Status = EventStatus.Missed;
                Raise($"event-missed-{e.Id}", e.MissedText.Length > 0 ? e.MissedText : $"{e.Name} came and went without you.", 0, onPassage: false);
            }
        }
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

        if (CrewBookedClash(startDay, startDay + e.Days - 1) is { } crewClash) return Reject(action, crewClash);
        _friendStart[year] = startDay;
        var clock = new GameClock(startDay);
        t.Narrative.Add($"You ring your friend. They'll come on {clock.WeekdayName}, day {startDay}, for a week.");
        if (e.InviteCostLine.Length > 0) t.Narrative.Add(e.InviteCostLine);
        return Accept(t, action, _boat.Orders.PhoneMinutes, 0, null, $"Asked a friend aboard for a week from day {startDay}.", work: false);
    }

    // ------------------------------------------------------------------ going

    /// <summary>
    /// Mark an event as going, or take the mark off (free, no time, anywhere). Only an event that can still be made this listing: not made,
    /// missed or gone. The friend's week is going from the moment the friend is asked and cannot be un-asked.
    /// </summary>
    private CommandResult DoPlanEvent(Turn t, string name, bool going)
    {
        string action = going ? "plan-event" : "unplan-event";
        if (EventRules.Count == 0) return Reject(action, "There's nothing on the calendar.");
        var r = NameResolver.Resolve(EventRules, name, e => e.Id, e => e.Name, _ => []);
        if (r.Match is not { } e)
            return r.Kind == ResolutionKind.Ambiguous
                ? RejectResolution(action, r, name, x => x.Name)
                : Reject(action, $"There's nothing on the calendar called '{name.Trim()}'.");

        int year = ListedYear();
        var window = WindowOf(e, year);
        var rec = _eventRecords.FirstOrDefault(x => x.Id == e.Id && x.Year == year);
        string when = WhenWords(e, year);
        int end = window?.To ?? year * GameClock.DaysPerYear + (e.Pick?.ToDayOfYear ?? e.FromDayOfYear);
        if (rec is { Status: EventStatus.Made }) return Reject(action, $"{e.Name} is made already: {when}.");
        if (rec is { Status: EventStatus.Missed } || Day > end) return Reject(action, $"{e.Name} has gone: {when}.");
        if (e.Pick is not null)
        {
            if (window is null)
                return Reject(action, $"You haven't asked your friend yet: invite friend <day> ({when}).");
            return Reject(action, going
                ? $"Your friend is coming on day {window.Value.From}: that one is going already."
                : $"Your friend is coming on day {window.Value.From}: you can't take that back now.");
        }
        if (going == _going.Contains((e.Id, year)))
            return Reject(action, going ? $"{e.Name} is marked already: {when}." : $"{e.Name} isn't marked.");
        if (going) _going.Add((e.Id, year)); else _going.Remove((e.Id, year));
        t.Narrative.Add(going
            ? $"You mark it on the calendar: {e.Name}, {when}. A wait will stop on its morning."
            : $"You take the mark off: {e.Name}, {when}. A wait will not stop for it, but it still happens, and it still counts if you are there.");
        return Accept(t, action, 0, 0, null, null, work: false);
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
        string outcome = rec is { Outcome.Length: > 0 } && e.WeekOutcomes.TryGetValue(rec.Outcome, out var o) ? o.Short : "";
        return new EventView(e.Name, PlaceName(e.Port), from, to, WhenWords(e, year), status,
            e.Requires.Select(r => RequirementView(r, window)).ToList(), rec?.Story ?? "", IsGoing(e, year),
            window is not null && e.Beats.Count > 0 ? BeatViews(e, rec, window.Value) : null, e.Pick is not null && window is null, outcome);
    }

    /// <summary>What the player can tell of a requirement: what is fitted, the problems they have found, the dry days, the forecast. Never the truth they have not seen.</summary>
    private Requirement RequirementView(RequirementRule r, (int From, int To)? window)
    {
        var ids = RequirementComponents(r).ToHashSet();
        var known = KnownOpenFaults().FirstOrDefault(f => ids.Contains(f.ComponentId));
        bool aboard = _boat.TryGet(r.Component, out _) && !_gone.Contains(r.Component);
        switch (r.Kind)
        {
            case "anyWorking":
            {
                // An alternative is open to the player when all of it is aboard and no problem they know of is on it.
                bool Open(string alt) => alt.Split('+').All(id => _boat.TryGet(id, out _) && !_gone.Contains(id))
                    && !KnownOpenFaults().Any(f => alt.Split('+').Contains(f.ComponentId));
                return r.Components.Any(Open) ? new(r.Text, true, "no known problem")
                    : known is not null ? new(r.Text, false, known.Name) : new(r.Text, false, "not aboard");
            }
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
                if (window is { } w && w.To >= Day && w.To - Day <= _boat.ForecastRules.MaxLeadDays)
                    return _weather[w.To].NightC >= r.BelowC
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
    private IEnumerable<(int Day, string Name, bool Going)> OpenWindowStarts()
    {
        int year = new GameClock(Day).Year;
        foreach (int y in new[] { year, year + 1 })
            foreach (var e in EventRules)
            {
                if (WindowOf(e, y) is not { } w || w.From < Day) continue;
                var rec = _eventRecords.FirstOrDefault(r => r.Id == e.Id && r.Year == y);
                if (rec is { Status: EventStatus.Made or EventStatus.Missed or EventStatus.Failed }) continue;
                yield return (w.From, e.Name, IsGoing(e, y));
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
