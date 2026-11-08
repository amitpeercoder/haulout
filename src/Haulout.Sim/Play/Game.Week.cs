using Haulout.Sim.Data;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// The friend's week as three beats (design note 010, second pass H): an arrival at the port at an hour of the first day, a day trip the friend
/// asks for (a passage the player makes with the friend aboard), and a last night. Each is a day the player plays and is judged on its own day,
/// with its own made and failed lines from data. Nothing here charges a vacation day: the weekday rule charges what the player does (a weekday at
/// sea, a weekday aboard past an evening), and a wait leaves the friend ashore and costs nothing. Nothing here draws a random number.
/// </summary>
public sealed partial class Game
{
    private EventRule? PickEvent => EventRules.FirstOrDefault(e => e.Pick is not null);

    /// <summary>The invited week if today is one of its days: its rule, its record and its window.</summary>
    private (EventRule E, EventRecord Rec, (int From, int To) W)? WeekNow()
    {
        if (_friendStart.Count == 0 || PickEvent is not { } e) return null;
        int year = new GameClock(Day).Year;
        if (WindowOf(e, year) is not { } w || Day < w.From || Day > w.To) return null;
        return (e, RecordFor(e, year), w);
    }

    private static BeatStatus BeatOf(EventRecord rec, BeatRule b) => rec.Beats.GetValueOrDefault(b.Id, BeatStatus.Pending);

    private static BeatRule? BeatOfKind(EventRule e, string kind) => e.Beats.FirstOrDefault(b => b.Kind == kind);

    /// <summary>The friend has come and the week is not over: the friend is aboard on any passage that casts off now.</summary>
    private bool FriendAboardNow =>
        WeekNow() is { } wk && !wk.Rec.Over && BeatOfKind(wk.E, "arrival") is { } a && BeatOf(wk.Rec, a) == BeatStatus.Made;

    /// <summary>The friend's week is on: fuel is free (the friend pays).</summary>
    private bool FriendPays =>
        WeekNow() is { } wk && BeatOfKind(wk.E, "arrival") is { } a && BeatOf(wk.Rec, a) == BeatStatus.Made;

    private bool Before(int day, int hour) => Day < day || Day == day && HourOfDay < hour;

    // ------------------------------------------------------------------ the hours

    /// <summary>Called after every step of time: the arrival and the last night are judged when their hour comes, wherever the player is.</summary>
    private void WeekStep(Turn t)
    {
        if (_sunk || WeekNow() is not { } wk || wk.Rec.Over) return;
        var (e, rec, w) = wk;
        if (BeatOfKind(e, "arrival") is { } arrival && BeatOf(rec, arrival) == BeatStatus.Pending && !Before(w.From, arrival.Hour))
            JudgeArrival(t, e, rec, arrival);
        if (rec.Over) return;
        if (BeatOfKind(e, "night") is { } night && BeatOf(rec, night) == BeatStatus.Pending && !Before(w.To, night.Hour))
            JudgeNight(t, e, rec, w, night);
    }

    private string FailedLine(EventRule e, BeatRule b, RequirementRule failing) =>
        b.FailedLines.TryGetValue(failing.Key, out var line) ? line
        : e.FailedLines.TryGetValue(failing.Key, out line) ? line
        : $"{e.Name}: {failing.Text} was not there, and it showed.";

    /// <summary>The event is a symptom: the truth showed itself, and the fault behind it is found.</summary>
    private void DiscoverBehind(Turn t, RequirementRule failing)
    {
        foreach (var id in RequirementComponents(failing))
            if (_boat.TryGet(id, out var c))
                foreach (var f in c.ActiveFaults.ToList()) DiscoverNow(t, f);
    }

    private RequirementRule? FailingOf(EventRule e, BeatRule b) =>
        e.Requires.FirstOrDefault(r => b.Requires.Contains(r.Key) && !RequirementHolds(r));

    private void JudgeArrival(Turn t, EventRule e, EventRecord rec, BeatRule b)
    {
        if (!Afloat || AtSea || e.Port != _portId)
        {
            rec.Over = true;
            rec.Beats[b.Id] = BeatStatus.Missed;
            rec.Status = EventStatus.Missed;
            string text = b.MissedText.Length > 0 ? b.MissedText : e.MissedText;
            Raise($"event-missed-{e.Id}", text.Length > 0 ? text : $"{e.Name} came and went without you.", 0, onPassage: false);
            return;
        }
        if (FailingOf(e, b) is { } failing)
        {
            rec.Over = true;
            rec.Beats[b.Id] = BeatStatus.Failed;
            rec.BeatFailed[b.Id] = failing.Text;
            rec.Status = EventStatus.Failed;
            rec.FailedRequirement = failing.Text;
            t.Narrative.Add(FailedLine(e, b, failing));
            DiscoverBehind(t, failing);
            _log.Add(new LogEntry(Day, TimeOfDay, $"Missed: {e.Name} ({failing.Text})."));
            return;
        }
        rec.Beats[b.Id] = BeatStatus.Made;
        rec.Status = EventStatus.Made;
        rec.Story = b.MadeLines.Count > 0 ? b.MadeLines[0] : "";
        t.Narrative.AddRange(b.MadeLines);
        _log.Add(new LogEntry(Day, TimeOfDay, $"Made it: {e.Name}."));
        Raise($"event-{e.Id}", rec.Story.Length > 0 ? rec.Story : $"Made it: {e.Name}.", 0, onPassage: false);
        if (BeatOfKind(e, "trip") is { } trip) t.Narrative.AddRange(trip.AnnounceLines);
    }

    /// <summary>A passage with the friend aboard ends at the port the friend asked for, in the week, and not under tow: the day trip is made.</summary>
    private void JudgeTrip(Turn t, Passage p, Port port)
    {
        if (!p.FriendAboard || p.TurnedBack || WeekNow() is not { } wk || wk.Rec.Over) return;
        if (wk.E.Beats.FirstOrDefault(b => b.Kind == "trip" && b.Port == port.Id) is not { } trip || BeatOf(wk.Rec, trip) != BeatStatus.Pending) return;
        wk.Rec.Beats[trip.Id] = BeatStatus.Made;
        t.Narrative.AddRange(trip.MadeLines);
        _log.Add(new LogEntry(Day, TimeOfDay, $"Made it: {trip.Text}."));
    }

    private void JudgeNight(Turn t, EventRule e, EventRecord rec, (int From, int To) w, BeatRule b)
    {
        if (BeatOfKind(e, "trip") is { } trip && BeatOf(rec, trip) == BeatStatus.Pending)
        {
            rec.Beats[trip.Id] = BeatStatus.Missed;
            if (trip.MissedText.Length > 0) t.Narrative.Add(trip.MissedText);
            _log.Add(new LogEntry(Day, TimeOfDay, $"Missed: {trip.Text}."));
        }
        if (!Afloat || AtSea)
        {
            rec.Beats[b.Id] = BeatStatus.Missed;
            if (b.MissedText.Length > 0) t.Narrative.Add(b.MissedText);
            _log.Add(new LogEntry(Day, TimeOfDay, $"Missed: {b.Text}."));
        }
        else if (FailingOf(e, b) is { } failing)
        {
            rec.Beats[b.Id] = BeatStatus.Failed;
            rec.BeatFailed[b.Id] = failing.Text;
            t.Narrative.Add(FailedLine(e, b, failing));
            DiscoverBehind(t, failing);
            _log.Add(new LogEntry(Day, TimeOfDay, $"Missed: {b.Text} ({failing.Text})."));
        }
        else
        {
            bool cold = e.Requires.Any(r => b.Requires.Contains(r.Key) && r.Kind == "warm" && _weather[Day].NightC < r.BelowC);
            rec.Beats[b.Id] = BeatStatus.Made;
            t.Narrative.AddRange(cold || b.PassLines.Count == 0 ? b.MadeLines : b.PassLines);
            _log.Add(new LogEntry(Day, TimeOfDay, $"Made it: {b.Text}."));
        }
        FinishWeek(t, e, rec);
    }

    /// <summary>The week is over: a good week (the day trip and the last night), a week (one of them), a thin one (the arrival only).</summary>
    private void FinishWeek(Turn t, EventRule e, EventRecord rec)
    {
        rec.Over = true;
        int made = e.Beats.Count(b => b.Kind is "trip" or "night" && BeatOf(rec, b) == BeatStatus.Made);
        rec.Outcome = made >= 2 ? "good" : made == 1 ? "made" : "thin";
        if (!e.WeekOutcomes.TryGetValue(rec.Outcome, out var outcome) || outcome.Line.Length == 0) return;
        t.Narrative.Add(outcome.Line);
        _log.Add(new LogEntry(Day, TimeOfDay, outcome.Line));
    }

    /// <summary>The midnight that ends a day of the week: a beat whose hour was somehow never judged is judged now; at the end the friend tops up the tank.</summary>
    private void WeekDaily(Turn t, EventRule e, EventRecord rec, (int From, int To) w, int day)
    {
        if (!rec.Over && day == w.From && BeatOfKind(e, "arrival") is { } a && BeatOf(rec, a) == BeatStatus.Pending) JudgeArrival(t, e, rec, a);
        if (!rec.Over && day == w.To && BeatOfKind(e, "night") is { } n && BeatOf(rec, n) == BeatStatus.Pending) JudgeNight(t, e, rec, w, n);
        if (day == w.To && BeatOfKind(e, "arrival") is { } arrival && BeatOf(rec, arrival) == BeatStatus.Made && _boat.TryGet(FuelTankId, out var tank)
            && tank.State.ContainsKey(FuelLitresProperty) && FuelLitres < TankCapacity)
        {
            tank.State[FuelLitresProperty] = TankCapacity;
            Raise("friend-tank", "Your friend topped up the tank before they went, and wouldn't hear about paying.", 0, onPassage: false);
        }
    }

    // ------------------------------------------------------------------ waits

    /// <summary>
    /// The friend's week and a wait that is about to begin: it is cut at the hour of the next beat the same day, and on the morning of the last night.
    /// The shortest wins, as in <see cref="StopAtCalendar"/>.
    /// </summary>
    private void WeekStops(int minutes, int endDay, Action<int, string> stopAtDay, Action<int, string> stopAtMinutes)
    {
        if (!Afloat || WeekNow() is not { } wk || wk.Rec.Over) return;
        foreach (var b in wk.E.Beats.Where(b => b.Kind is "arrival" or "night" && BeatOf(wk.Rec, b) == BeatStatus.Pending))
        {
            int day = b.Kind == "arrival" ? wk.W.From : wk.W.To;
            int at = (day - _startDay) * 1440 + b.Hour * 60 - StartMinuteOfDay;
            if (at - ClockMinutes is > 0 && at - ClockMinutes < minutes) stopAtMinutes(at - ClockMinutes, b.ComeBackLine);
            if (b.Kind == "night" && day > Day && day <= endDay && b.MorningLine.Length > 0) stopAtDay(day, b.MorningLine);
        }
    }

    /// <summary>A wait in the friend's week: the friend goes walking on the island and the days cost nothing.</summary>
    private void FriendWalks(Turn t)
    {
        if (WeekNow() is { } wk && !wk.Rec.Over && BeatOfKind(wk.E, "arrival") is { } a && BeatOf(wk.Rec, a) == BeatStatus.Made && wk.E.WalkLine.Length > 0)
            t.Narrative.Add(wk.E.WalkLine);
    }

    // ------------------------------------------------------------------ the views

    private List<BeatView> BeatViews(EventRule e, EventRecord? rec, (int From, int To) w)
    {
        var views = new List<BeatView>();
        foreach (var b in e.Beats)
        {
            int day = b.Kind == "night" ? w.To : w.From;
            string when = b.Kind == "trip" ? b.Ask : $"{new GameClock(day).WeekdayName}, day {day}, about {b.Hour:00}:00";
            var state = rec is null ? BeatStatus.Pending : BeatOf(rec, b);
            bool arrived = rec is not null && BeatOfKind(e, "arrival") is { } a && BeatOf(rec, a) == BeatStatus.Made;
            bool open = rec is not { Over: true } && Day <= w.To;
            bool onNow = b.Kind == "trip" ? arrived : Day == day;
            string status = state switch
            {
                BeatStatus.Made => "made",
                BeatStatus.Failed => $"failed: {rec!.BeatFailed.GetValueOrDefault(b.Id, rec.FailedRequirement ?? "")}",
                BeatStatus.Missed => "missed",
                _ => !open ? "missed" : onNow ? "on now" : "ahead",
            };
            string note = state == BeatStatus.Pending && open && b.Kind != "trip"
                ? string.Join(", ", e.Requires.Where(r => b.Requires.Contains(r.Key)).Select(r => r.Text))
                : "";
            views.Add(new BeatView(b.Text, when, status, note));
        }
        return views;
    }
}
