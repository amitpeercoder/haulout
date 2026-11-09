using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// Crewing places (design note 012, decision 5, step 4). Two dated cards a season on the club's board (board.json, <c>crewing</c>): a weekend delivery and a
/// week north on a far boat. <see cref="TakeCrewingPlace"/> is a phone call to the owner (any hour, five minutes, no money) that marks the days. A wait stops
/// at 08:00 on the first morning. The next command of any kind at Skjaervik, before the owner casts off, begins the away days instead of itself
/// (<see cref="CrewingGate"/>): ONE away stretch run day by day through the ordinary tick (the boat's own systems age, the harbourmaster's alerts are queued
/// and come back as one letter on the mat), each day credited to the skipper's log from the plan and that day's weather, as CREW. Nothing lands in the boat's
/// log but one line; the miles are the skipper's, not the boat's. A player who is not at Skjaervik when the owner casts off misses the place. Nothing here
/// draws a random number.
/// </summary>
public sealed partial class Game
{
    private enum CrewState { Taken, Away, Done, Missed }

    private sealed class CrewRecord
    {
        public string PlaceId { get; init; } = "";
        public int From { get; init; }
        public int To { get; init; }
        public CrewState State { get; set; } = CrewState.Taken;
    }

    private readonly List<CrewRecord> _crewRecords = [];

    private CrewingRules Crew => _boat.Board.Crewing;

    private string CW(string key) => Crew.Word(key);

    private IEnumerable<CrewRecord> CrewBooked => _crewRecords.Where(r => r.State is CrewState.Taken or CrewState.Away);

    private bool AtHomePort => !AtSea && !_sunk && _portId == Crew.Port;

    // ------------------------------------------------------------------ the dates

    private sealed record CrewOccurrence(CrewPlaceRule Place, int From, int To, CrewRecord? Record);

    private (int From, int To) CrewWindow(CrewPlaceRule p, int year)
    {
        int start = year * GameClock.DaysPerYear + p.FromDayOfYear;
        int weekday = WeekdayIndex(p.Weekday);
        if (weekday >= 0) while (new GameClock(start).Weekday != weekday) start++;
        return (start, start + p.Days - 1);
    }

    private CrewRecord? CrewRecordOf(string id, int from) => _crewRecords.FirstOrDefault(r => r.PlaceId == id && r.From == from);

    /// <summary>
    /// The cards on the board: this clock year's date of each place while it is still to come or has a record (taken, done, missed), and next year's of every place
    /// once all of this year's have gone. Sorted by day.
    /// </summary>
    private List<CrewOccurrence> CrewOccurrences()
    {
        var found = new List<CrewOccurrence>();
        if (!Crew.Enabled) return found;
        int year = new GameClock(Day).Year;
        bool thisYearGone = Crew.Places.All(p => CrewWindow(p, year).To < Day);
        for (int y = year; y <= year + 1; y++)
        {
            if (y > year && !thisYearGone) break;
            foreach (var p in Crew.Places)
            {
                var (from, to) = CrewWindow(p, y);
                var rec = CrewRecordOf(p.Id, from);
                if (y == year && to < Day && rec is null) continue;
                found.Add(new CrewOccurrence(p, from, to, rec));
            }
        }
        return found.OrderBy(o => o.From).ToList();
    }

    private int CrewVacationDays(int from, int to) => VacationDaysOf(from, to);

    private bool CrewAsksMet(CrewAsksRule a) =>
        a.Ticket.Length > 0 && TicketData.Find(a.Ticket) is { } t && Holds(t)
        || _skipper.MilesUnderWay >= a.Miles && _skipper.NightsAway >= a.NightsAway;

    private string CrewHaveWords(CrewAsksRule a)
    {
        var ci = CultureInfo.InvariantCulture;
        return Fill(CW("haveLine"), ("miles", ((int)Math.Floor(_skipper.MilesUnderWay)).ToString(ci)), ("needMiles", a.Miles.ToString(ci)),
            ("nights", _skipper.NightsAway.ToString(ci)), ("needNights", a.NightsAway.ToString(ci)), ("nightWord", a.NightsAway == 1 ? "night" : "nights"));
    }

    // ------------------------------------------------------------------ taking a place

    private CommandResult DoTakeCrewingPlace(Turn t, string name)
    {
        const string action = "take-crewing-place";
        if (AtSea) return Reject(action, "You're at sea: ring from port.");
        if (!Crew.Enabled) return Reject(action, "Nobody is asking for crew.");
        var r = NameResolver.Resolve(Crew.Places, name, p => p.Id, p => p.Title, p => p.Aliases);
        if (r.Match is not { } place)
            return r.Kind == ResolutionKind.Ambiguous
                ? RejectResolution(action, r, name, p => p.Title)
                : Reject(action, Fill(CW("unknown"), ("name", name.Trim())));
        var occ = CrewOccurrences().FirstOrDefault(o => o.Place == place && o.To >= Day);
        if (occ is null || occ.Record is { State: CrewState.Done or CrewState.Missed }) return Reject(action, CW("gone"));
        string when = DaysWords(occ.From, occ.To);
        if (occ.Record is not null) return Reject(action, Fill(CW("taken"), ("when", when)));
        if (occ.From - Day < Crew.EnrolDaysBefore) return Reject(action, Fill(CW("tooClose"), ("owner", place.Owner), ("when", when)));
        if (place.Asks is { } asks && !CrewAsksMet(asks))
            return Reject(action, Fill(CW("asksNotMet"), ("owner", place.Owner), ("asks", asks.Words), ("have", CrewHaveWords(asks))));
        if (CrewClash(occ.From, occ.To, place) is { } clash) return Reject(action, clash);

        var vac = CrewVacationDays(occ.From, occ.To);
        _crewRecords.Add(new CrewRecord { PlaceId = place.Id, From = occ.From, To = occ.To });
        var tokens = new[] { ("owner", place.Owner), ("boat", place.Boat), ("when", when), ("pay", place.Pay) };
        t.Narrative.Add(Fill(CW("takeLine"), tokens));
        if (vac > 0) t.Narrative.Add(Fill(CW("vacation"), ("vacation", Plural(vac, "vacation day", "vacation days"))));
        t.Narrative.Add(CW("stops"));
        return Accept(t, action, _boat.Orders.PhoneMinutes, 0, null, Fill(CW("takeLog"), tokens), work: false);
    }

    /// <summary>What the days clash with (a booked course, the friend's week, the other place), in words; null when they are free.</summary>
    private string? CrewClash(int from, int to, CrewPlaceRule? self)
    {
        string Said(string what, int a, int b) => Fill(CW("clash"), ("what", what), ("when", DaysWords(a, b)));
        if (EnrolledCourses.FirstOrDefault(c => c.From <= to && from <= c.To) is { } course && TicketData.Find(course.TicketId) is { } ticket)
            return Said(Fill(CW("clashCourse"), ("course", CourseWord(ticket))), course.From, course.To);
        if (PickEvent is { } e)
            foreach (var start in _friendStart.Values)
                if (start <= to && from <= start + e.Days - 1) return Said(CW("clashFriend"), start, start + e.Days - 1);
        return CrewBookedClash(from, to, self);
    }

    /// <summary>A place already taken that the days run into, in words (also what a course or the friend's week is refused for); null when none is.</summary>
    private string? CrewBookedClash(int from, int to, CrewPlaceRule? self = null)
    {
        if (!Crew.Enabled || CrewBooked.FirstOrDefault(c => c.From <= to && from <= c.To && c.PlaceId != self?.Id) is not { } other || Crew.Find(other.PlaceId) is not { } otherPlace)
            return null;
        return Fill(CW("clash"), ("what", Fill(CW("clashCrew"), ("boat", otherPlace.Boat))), ("when", DaysWords(other.From, other.To)));
    }

    // ------------------------------------------------------------------ waits

    /// <summary>A wait that would pass 08:00 on the first morning of a place the player took stops there, whatever time of day it was meant to end.</summary>
    private void CrewStops(int minutes, Action<int, string> stopAtMinutes)
    {
        foreach (var rec in _crewRecords.Where(r => r.State == CrewState.Taken))
        {
            if (Crew.Find(rec.PlaceId) is not { } place) continue;
            int m = (rec.From - _startDay) * 1440 - ClockMinutes;
            if (m > 0 && m < minutes) stopAtMinutes(m, Fill(CW("morning"), ("day", rec.From.ToString(CultureInfo.InvariantCulture)), ("boat", place.Boat), ("meet", place.Meet)));
        }
    }

    // ------------------------------------------------------------------ the first morning

    /// <summary>Phone calls, marks on the calendar and decisions: they do not begin the away days (a wait does).</summary>
    private static bool KeepsCrewDay(Command c) =>
        c is Enrol or Order or BookHaulout or InviteFriend or PlanEvent or UnplanEvent or Defer or Undefer or ListForSale or AcceptOffer or Keep or TakeCrewingPlace or Answer;

    /// <summary>
    /// The first command of any other kind on the first morning of a place the player took: if they are at Skjaervik before the owner casts off, the away days
    /// run in its place (the command is not carried out); if they are not, the owner has gone without them. Null: nothing to do with a place today.
    /// </summary>
    private CommandResult? CrewingGate(Command command, Turn t)
    {
        if (!Crew.Enabled || KeepsCrewDay(command)) return null;
        var rec = _crewRecords.FirstOrDefault(r => r.State == CrewState.Taken && r.From == Day);
        if (rec is null || Crew.Find(rec.PlaceId) is not { } place) return null;
        if (AtHomePort && MinuteOfDay <= place.MeetHour * 60) return RunAway(t, rec, place);
        MissCrewing(rec, place);
        return null;
    }

    private void MissCrewing(CrewRecord rec, CrewPlaceRule place)
    {
        rec.State = CrewState.Missed;
        var tokens = new[] { ("boat", Capital(place.Boat)), ("port", PortName(Crew.Port)), ("meetHour", HourWords(place.MeetHour * 60)), ("owner", place.Owner) };
        Raise($"crew-missed-{place.Id}", Fill(CW("missed"), tokens), 0, onPassage: false);
        _log.Add(new LogEntry(Day, TimeOfDay, Fill(CW("missedLog"), ("boat", place.Boat), ("port", PortName(Crew.Port)))));
    }

    /// <summary>The midnight that ends a day: a place whose first day it was and which was never begun is missed (the player was somewhere else all day).</summary>
    private void CrewDaily(int day)
    {
        if (!Crew.Enabled) return;
        foreach (var rec in _crewRecords.Where(r => r.State == CrewState.Taken && r.From == day).ToList())
            if (Crew.Find(rec.PlaceId) is { } place) MissCrewing(rec, place);
    }

    // ------------------------------------------------------------------ the away days

    private static string HourText(double hour)
    {
        int minutes = (int)Math.Round(hour * 60);
        return $"{minutes / 60 % 24:00}:{minutes % 60:00}";
    }

    private static string Nm(double nm) => nm.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The day's line in the narrative: its head, the weekday rule (a weekday away is a day of the player's own), the hours and the day from data.</summary>
    private void AwayDayLines(Turn t, CrewPlaceRule place, CrewLegRule leg, int index, int day, HashSet<string> seenOff)
    {
        var ci = CultureInfo.InvariantCulture;
        string off = leg.ToOffChart && seenOff.Add(leg.To) ? CW("offChart") : "";
        string route = leg.Nm > 0
            ? Fill(CW("route"), ("from", leg.From), ("to", leg.To), ("off", off), ("nm", Nm(leg.Nm)))
            : Fill(CW("routeLay"), ("to", leg.To), ("off", off));
        t.Narrative.Add(Fill(CW("head"), ("weekday", new GameClock(day).WeekdayName), ("day", day.ToString(ci)), ("n", (index + 1).ToString(ci)),
            ("total", place.Days.ToString(ci)), ("route", route)));
        ChargeDay(t, day, atSea: true);
        if (leg.Nm > 0)
            t.Narrative.Add(Fill(CW("underWay"), ("depart", HourText(leg.DepartHour)), ("hours", leg.Hours % 1 == 0 ? $"{leg.Hours:0} hours" : $"{leg.Hours.ToString("0.#", ci)} hours"),
                ("night", leg.DepartHour + leg.Hours > 24 ? CW("night") : "")));
        t.Narrative.Add(leg.Line);
    }

    /// <summary>
    /// One day of the place in the skipper's log, as crew: the leg's miles; the hours under way after dark by the daylight table of the day and of the next
    /// when she runs through midnight; the strongest force of the days under way from the series; the passage; a day aboard; a night away if she lies away
    /// that night. The boat's own log and the season's miles are not touched.
    /// </summary>
    private int CrewCredit(CrewLegRule leg, int day)
    {
        int forceUsed = 0;
        var log = _skipper;
        if (leg.Nm > 0 && leg.Hours > 0)
        {
            const double step = 0.25;
            double dark = 0;
            for (double h = 0; h < leg.Hours - 1e-9; h += step)
            {
                double mid = leg.DepartHour + h + Math.Min(step, leg.Hours - h) / 2;
                if (IsDark(day + (int)(mid / 24), mid % 24)) dark += Math.Min(step, leg.Hours - h);
            }
            bool overnight = leg.DepartHour + leg.Hours > 24;
            int force = forceUsed = Math.Max(_weather[day].WindForce, overnight ? _weather[day + 1].WindForce : 0);
            log = log with
            {
                MilesUnderWay = log.MilesUnderWay + leg.Nm,
                NightHoursUnderWay = log.NightHoursUnderWay + dark,
                StrongestForce = Math.Max(log.StrongestForce, force),
            };
            log = log.WithPassage(new LoggedPassage(leg.Nm, leg.Hours, dark, overnight, PassageRole.Crew));
        }
        log = log with { DaysAboard = log.DaysAboard + 1, NightsAway = log.NightsAway + (leg.AwayNight ? 1 : 0) };
        _skipper = log;
        return forceUsed;
    }

    private string CrewSummary(SkipperLog before, int force)
    {
        var ci = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        int miles = (int)Math.Floor(_skipper.MilesUnderWay - before.MilesUnderWay + 1e-9);
        if (miles > 0) parts.Add(Fill(CW("sumMiles"), ("n", miles.ToString(ci))));
        int nights = _skipper.NightsAway - before.NightsAway;
        if (nights > 0) parts.Add(Fill(CW("sumNights"), ("n", nights.ToString(ci)), ("nights", nights == 1 ? "night" : "nights")));
        int dark = (int)Math.Floor(_skipper.NightHoursUnderWay - before.NightHoursUnderWay + 1e-9);
        if (dark > 0) parts.Add(Fill(CW("sumDark"), ("n", dark.ToString(ci)), ("hours", dark == 1 ? "hour" : "hours")));
        if (force > 0) parts.Add(Fill(CW("sumForce"), ("n", force.ToString(ci))));
        return Fill(CW("summary"), ("list", parts.Count == 0 ? CW("sumNone") : AndList(parts)));
    }

    private CommandResult RunAway(Turn t, CrewRecord rec, CrewPlaceRule place)
    {
        var ci = CultureInfo.InvariantCulture;
        rec.State = CrewState.Away;
        t.Narrative.Add(Fill(CW("gate"), ("meet", place.Meet)));
        ShutDownEngine(t);
        _weekEvents.Clear();
        string stamp = TimeOfDay;
        int startDay = Day, before = ClockMinutes, logAt = _log.Count;
        var skipperBefore = _skipper;
        var queued = new List<(int Day, string Kind)>();
        var seenOff = new HashSet<string>();
        int stretchForce = 0;
        _holdPumpCounter = true;
        _holdMorningReadings = true;
        _watchingBilge = Afloat;
        try
        {
            for (int i = 0; i < place.Plan.Count; i++)
            {
                var leg = place.Plan[i];
                int day = rec.From + i;
                bool last = i == place.Plan.Count - 1;
                AwayDayLines(t, place, leg, i, day, seenOff);
                stretchForce = Math.Max(stretchForce, CrewCredit(leg, day));
                if (last && place.CostLine.Length > 0)
                    t.Narrative.Add(Fill(place.CostLine, ("owner", place.Owner), ("price", Money(place.NewPriceNok))));
                int now = Day * 1440 + MinuteOfDay;
                int target = last ? day * 1440 + place.ReturnHour * 60 : (day + 1) * 1440 + StartMinuteOfDay;
                var raisedBefore = new Dictionary<string, int>(_alerts.LastRaised);
                _bilgePassedToday = false;
                if (target > now) Tick(t, target - now, aboard: false);
                if (_sunk) break;
                if (_bilgePassedToday) Raise(HarbourmasterKind, _boat.Water.HarbourmasterText);
                foreach (var kind in new[] { HarbourmasterKind, "mooring-parted", "chafe" })
                    if (_alerts.LastRaised.TryGetValue(kind, out var raisedOn) && (!raisedBefore.TryGetValue(kind, out var was) || was != raisedOn))
                        queued.Add((day, kind));
            }
        }
        finally
        {
            _holdPumpCounter = false;
            _holdMorningReadings = false;
            _watchingBilge = false;
        }
        rec.State = CrewState.Done;
        _log.Insert(logAt, new LogEntry(startDay, stamp, Fill(CW("awayLog"), ("boat", place.Boat), ("from", rec.From.ToString(ci)), ("to", rec.To.ToString(ci)))));
        if (_sunk)
        {
            t.Narrative.AddRange(_loss!.Lines);
            return new CommandResult(true, "crewing", t.Narrative.ToList(), ClockMinutes - before, 0, _loss, t.Newly.ToList(), _running, _finished, TakeUnread());
        }
        PumpCounterAlert();
        BilgeSmell(OverSole);
        Voltmeter();
        t.Narrative.Add(place.ReturnLine);
        t.Narrative.Add(CrewSummary(skipperBefore, stretchForce));
        _log.Add(new LogEntry(Day, TimeOfDay, place.BoatCard.Lines[0], 0, $"boat {place.Id}"));
        WriteHarbourmasterLetter(t, place, queued);
        AddWeekLine(t);
        var told = TakeUnread().DistinctBy(a => a.Text).ToList();
        return new CommandResult(true, "crewing", t.Narrative.ToList(), ClockMinutes - before, 0, null, t.Newly.ToList(), _running, _finished, told);
    }

    /// <summary>
    /// What happened to her while the player was away, in the harbourmaster's words with the days: one letter, a logbook document (design note 010 E's form).
    /// It costs nothing in itself (a bill the harbourmaster's action made stays as it was). No letter when nothing happened.
    /// </summary>
    private void WriteHarbourmasterLetter(Turn t, CrewPlaceRule place, List<(int Day, string Kind)> queued)
    {
        // A condition that went on for days is said once, with its days ("Days 231 to 236: ...").
        var ci = CultureInfo.InvariantCulture;
        var lines = new List<string>();
        foreach (var group in queued.GroupBy(q => q.Kind).OrderBy(g => g.Min(q => q.Day)))
        {
            if (!Crew.LetterLines.TryGetValue(group.Key, out var template)) continue;
            int first = group.Min(q => q.Day), last = group.Max(q => q.Day);
            string days = first == last ? Fill(CW("letterDay"), ("day", first.ToString(ci))) : Fill(CW("letterDays"), ("from", first.ToString(ci)), ("to", last.ToString(ci)));
            lines.Add(Fill(template, ("days", days)));
        }
        if (lines.Count == 0) return;
        if (lines.Count == 0) return;
        int number = ++_lettersWritten;
        string text = Fill(CW("letterHead"), ("boat", place.Boat)) + " " + string.Join(" ", lines) + " " + CW("letterTail");
        _log.Add(new LogEntry(Day, TimeOfDay, text, 0, $"letter {number}"));
        t.Narrative.Add(Fill(CW("letterMat"), ("n", number.ToString(ci))));
        t.Narrative.Add(string.Join(" ", lines));
    }

    // ------------------------------------------------------------------ the far boat's card

    private IEnumerable<CrewPlaceRule> HeldBoats => _crewRecords.Where(r => r.State == CrewState.Done).Select(r => Crew.Find(r.PlaceId)).OfType<CrewPlaceRule>().DistinctBy(p => p.Id);

    private DocumentView? CrewBoatDocument(string name)
    {
        var r = NameResolver.Resolve(HeldBoats, name, p => p.Id, p => p.BoatCard.Title, p => p.BoatCard.Aliases);
        if (r.Match is not { } place) return null;
        var lines = place.BoatCard.Lines.ToList();
        if (place.CostLine.Length > 0) lines.Add(Fill(place.CostLine, ("owner", place.Owner), ("price", Money(place.NewPriceNok))));
        return new DocumentView(place.BoatCard.Title, [new DocumentPage(1, "", string.Join("\n", lines))]);
    }

    // ------------------------------------------------------------------ views

    private IEnumerable<CrewingCard> CrewingCards()
    {
        foreach (var o in CrewOccurrences())
        {
            var p = o.Place;
            int vac = CrewVacationDays(o.From, o.To);
            string key = o.Record?.State switch
            {
                CrewState.Done => "done",
                CrewState.Missed => "missed",
                CrewState.Taken or CrewState.Away => "taken",
                _ => o.From - Day < Crew.EnrolDaysBefore ? "tooClose" : "open",
            };
            string status = Crew.BoardStatus.TryGetValue(key, out var s) ? s : key;
            string when = DaysWords(o.From, o.To);
            var shortfall = p.Asks is { } a && o.Record is null && !CrewAsksMet(a) ? new List<string> { CrewHaveWords(a) } : [];
            string vacWords = vac > 0 ? " " + Fill(CW("vacation"), ("vacation", Plural(vac, "vacation day", "vacation days"))) : "";
            string line = Fill(CW("boardLine"), ("card", Fill(p.Card, ("when", when))), ("vac", vacWords));
            yield return new CrewingCard(o.From, p.Title, when, p.Boat, p.Days, vac, o.Record is { State: CrewState.Taken or CrewState.Away }, status, p.Asks?.Words ?? "", shortfall, line);
        }
    }

    private IEnumerable<AheadItem> CrewAhead(int d)
    {
        foreach (var rec in CrewBooked.Where(c => c.From == d && d > Day))
            if (Crew.Find(rec.PlaceId) is { } place)
                yield return new AheadItem(d, "crewing", $"Crewing on {place.Boat}", $"your place on {place.Boat}", 0);
    }
}
