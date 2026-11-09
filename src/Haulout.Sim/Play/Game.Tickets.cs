using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// Tickets, the club's courses and the notice board (design note 012, decisions 3 and 6, step 2). A ticket asks things of the skipper's log
/// (<see cref="SkipperLog"/>); a course is a dated card at the club; <see cref="Enrol"/> pays at enrolment and marks the days. A course day is played:
/// the first command of the day (a wait or a sleep included) is the day at the club, from the club's start to its end, with that day's line from data; the
/// player is not aboard, and the evening is their own. A weekday course day costs a vacation day by the weekday rule, once. A day not spent at the
/// club (at sea, or at another port) forfeits the course and the fee. On the last day the log decides (<see cref="DecideLastDay"/>, the one method where
/// the exam of step 2b is slotted in): if it meets every ask the ticket is held, else the course is done and the ticket is held automatically on the
/// first midnight or arrival that the log meets them. Tickets are never lost. Nothing here draws a random number.
/// </summary>
public sealed partial class Game
{
    private enum CourseState { Enrolled, Done, Forfeit }

    private sealed class CourseRecord
    {
        public string TicketId { get; init; } = "";
        public string OfferId { get; init; } = "";
        public int From { get; init; }
        public int To { get; init; }
        public int PaidNok { get; init; }
        public CourseState State { get; set; } = CourseState.Enrolled;
        /// <summary>A re-sit of the exam only (design note 012, decision 8): From and To are the exam day; PaidNok is the exam fee.</summary>
        public bool Resit { get; init; }
        /// <summary>The exam was sat and not passed: the course is done, the exam is to re-sit.</summary>
        public bool ExamFailed { get; set; }
        /// <summary>The days already spent at the club.</summary>
        public HashSet<int> Held { get; } = [];
    }

    private readonly List<CourseRecord> _courseRecords = [];
    /// <summary>Ticket id to the day it was gained. A ticket held from the start is not in here.</summary>
    private readonly Dictionary<string, int> _ticketsHeld = [];
    /// <summary>The skipper is at the club (a course day): the hours do not count as sleep.</summary>
    private bool _atClub;

    private TicketRules TicketData => _boat.Tickets;
    private ClubRules Club => _boat.Tickets.Club;

    /// <summary>For tests only: the ticket is held from today (a scenario that needs the radio ticket without the Saturday at the club).</summary>
    internal void GrantTicketForTests(string id) => _ticketsHeld[id] = Day;

    private bool Holds(TicketRule t) => t.HeldFromStart || _ticketsHeld.ContainsKey(t.Id);

    private string ClubPlace => Fill(Club.PlaceLine, ("port", PortName(Club.Port)));

    /// <summary>"radio course": the course's name without its article, for a sentence that supplies its own.</summary>
    private static string CourseWord(TicketRule t) => t.Course!.Name.StartsWith("The ", StringComparison.Ordinal) ? t.Course.Name[4..] : t.Course.Name;

    // ------------------------------------------------------------------ the asks

    private int AskHave(AskRule a) => a.Kind switch
    {
        "miles" => (int)Math.Floor(_skipper.MilesUnderWay),
        "daysAboard" => _skipper.DaysAboard,
        "nightHours" => (int)Math.Floor(_skipper.NightHoursUnderWay),
        "passagesOver" => _skipper.PassagesOver(a.Nm, a.AsSkipper),
        "passagesOverNight" => _skipper.PassagesOverThroughANight(a.Nm),
        "seasonsNoTow" => _skipper.SeasonsWithoutTow,
        "ticket" => TicketData.Find(a.Ticket) is { } other && Holds(other) ? 1 : 0,
        "firstAid" => _skipper.FirstAidHeld ? 1 : 0,
        _ => 0,
    };

    /// <summary>The log meets the ask. Miles and night hours compare the true figure (the line shows it to the whole unit), so a line that says "you have 300" is met.</summary>
    private bool AskMet(AskRule a) => a.Kind switch
    {
        "miles" => _skipper.MilesUnderWay >= a.Need,
        "nightHours" => _skipper.NightHoursUnderWay >= a.Need,
        "ticket" or "firstAid" => AskHave(a) >= 1,
        _ => AskHave(a) >= a.Need,
    };

    private bool LogMeets(TicketRule t) => t.Asks.All(AskMet);

    private (string Key, string Value)[] AskTokens(AskRule a) =>
    [
        ("need", a.Need.ToString(CultureInfo.InvariantCulture)), ("have", AskHave(a).ToString(CultureInfo.InvariantCulture)),
        ("nm", a.Nm.ToString(CultureInfo.InvariantCulture)), ("passages", a.Need == 1 ? "passage" : "passages"),
        ("seasons", a.Need == 1 ? "season" : "seasons"),
        ("name", a.Kind == "ticket" && TicketData.Find(a.Ticket) is { } other ? other.AskName : ""),
    ];

    /// <summary>One line of a ticket's checklist: "300 miles: you have 126." / "The radio ticket: not yet."</summary>
    private string AskLine(AskRule a)
    {
        var words = TicketData.Asks[a.Kind];
        string template = a.Kind == "passagesOver" && !a.AsSkipper && words.LineCrew.Length > 0 ? words.LineCrew : words.Line;
        string state = AskMet(a) ? words.Held : words.NotYet;
        string line = Fill(template, [.. AskTokens(a), ("state", state)]);
        return char.ToUpperInvariant(line[0]) + line[1..];
    }

    private string AskWait(AskRule a)
    {
        var words = TicketData.Asks[a.Kind];
        string template = a.Kind == "passagesOver" && !a.AsSkipper && words.WaitCrew.Length > 0 ? words.WaitCrew : words.Wait;
        return Fill(template, AskTokens(a));
    }

    // ------------------------------------------------------------------ the dates

    private static int WeekdayIndex(string? weekday) =>
        weekday is { Length: > 0 } w ? Array.IndexOf(WeekdayKeys, w.ToLowerInvariant()) : -1;

    private (int From, int To) CourseWindow(CourseRule course, CourseOffer offer, int year)
    {
        int start = year * GameClock.DaysPerYear + offer.FromDayOfYear;
        int weekday = WeekdayIndex(offer.Weekday);
        if (weekday >= 0) while (new GameClock(start).Weekday != weekday) start++;
        return (start, start + course.Days - 1);
    }

    private sealed record Occurrence(TicketRule Ticket, CourseOffer Offer, int From, int To, CourseRecord? Record);

    private CourseRecord? RecordOf(string ticketId, string offerId, int from) =>
        _courseRecords.FirstOrDefault(r => r.TicketId == ticketId && r.OfferId == offerId && r.From == from);

    /// <summary>
    /// The dates of a ticket's course that are still to come or on now: this clock year's, or next year's once all of this year's have gone. A
    /// course already forfeited is not listed. Empty: no course, or none offered.
    /// </summary>
    private List<Occurrence> Occurrences(TicketRule t)
    {
        var found = new List<Occurrence>();
        if (t.Course is not { Offers.Count: > 0 } course) return found;
        int year = new GameClock(Day).Year;
        for (int y = year; y <= year + 1 && found.Count == 0; y++)
            foreach (var o in course.Offers)
            {
                var (from, to) = CourseWindow(course, o, y);
                if (to < Day) continue;
                var rec = RecordOf(t.Id, o.Id, from);
                if (rec is { State: CourseState.Forfeit }) continue;
                found.Add(new Occurrence(t, o, from, to, rec));
            }
        return found.OrderBy(o => o.From).ToList();
    }

    /// <summary>The course is done and the exam passed (or there is none): the ticket waits only for the log.</summary>
    private bool DoneAndWaiting(TicketRule t) => !Holds(t) && _courseRecords.Any(r => r.TicketId == t.Id && r.State == CourseState.Done && !r.ExamFailed);

    /// <summary>The course is done, the exam failed and not yet passed: the player may re-sit it for the exam fee.</summary>
    private bool ExamToResit(TicketRule t) =>
        !Holds(t) && !DoneAndWaiting(t) && _courseRecords.Any(r => r.TicketId == t.Id && r.State == CourseState.Done && r.ExamFailed);

    /// <summary>
    /// The dates a failed exam can be re-sat: the last day of each date of the course whose enrolment is still open, this clock year's and next year's. The first is
    /// the next date. A date whose re-sit was forfeited is not listed again.
    /// </summary>
    private List<Occurrence> ResitDates(TicketRule t)
    {
        var found = new List<Occurrence>();
        if (t.Course is not { Offers.Count: > 0 } course) return found;
        int year = new GameClock(Day).Year;
        for (int y = year; y <= year + 1; y++)
            foreach (var o in course.Offers)
            {
                var (from, to) = CourseWindow(course, o, y);
                if (to - Day < Club.EnrolDaysBefore) continue;
                if (_courseRecords.Any(r => r.TicketId == t.Id && r.Resit && r.To == to && r.State == CourseState.Forfeit)) continue;
                found.Add(new Occurrence(t, o, from, to, null));
            }
        return found.OrderBy(o => o.To).ToList();
    }

    private IEnumerable<CourseRecord> EnrolledCourses => _courseRecords.Where(r => r.State == CourseState.Enrolled);

    private CourseRecord? CourseRecordToday => EnrolledCourses.FirstOrDefault(r => r.From <= Day && Day <= r.To);

    private int VacationDaysOf(int from, int to) =>
        _boat.Calendar.ChargesDays ? Enumerable.Range(from, to - from + 1).Count(d => !new GameClock(d).IsWeekend) : 0;

    // ------------------------------------------------------------------ enrolling

    private CommandResult DoEnrol(Turn t, string name)
    {
        const string action = "enrol";
        if (AtSea) return Reject(action, "You're at sea: ring the club from port.");
        if (!TicketData.Enabled) return Reject(action, "There's no club to ring.");
        var r = NameResolver.Resolve(TicketData.Tickets.Where(x => x.Course is not null), name, x => x.Id, x => x.Name, x => x.Aliases);
        if (r.Match is not { } ticket)
            return r.Kind == ResolutionKind.Ambiguous
                ? RejectResolution(action, r, name, x => x.Name)
                : Reject(action, $"The club runs no course called '{name.Trim()}'.");
        var course = ticket.Course!;
        if (Holds(ticket)) return Reject(action, $"{ticket.HeldLine} There is nothing to take.");
        if (course.Offers.Count == 0) return Reject(action, ticket.NoCourse);
        if (DoneAndWaiting(ticket)) return Reject(action, Fill(Club.DoneWaiting, ("course", CourseWord(ticket))));
        if (EnrolledCourses.FirstOrDefault(x => x.TicketId == ticket.Id) is { } already)
            return Reject(action, $"You're enrolled already on the {CourseWord(ticket)}: {DaysWords(already.From, already.To)}.");

        bool resit = ExamToResit(ticket);
        var dates = resit ? ResitDates(ticket) : Occurrences(ticket);
        var open = resit ? dates.FirstOrDefault() : dates.FirstOrDefault(o => o.From - Day >= Club.EnrolDaysBefore);
        if (open is null && resit) return Reject(action, Club.Exam.ResitNoDate);
        if (open is null)
        {
            var last = dates.LastOrDefault();
            return Reject(action, last is null
                ? $"{course.Name} has gone this year."
                : last.From <= Day
                    ? $"The {CourseWord(ticket)} has started ({DaysWords(last.From, last.To)}): the club takes names up to the day before."
                    : $"The {CourseWord(ticket)} starts {DaysWords(last.From, last.To)}: the club takes names up to the day before.");
        }
        int from = resit ? open.To : open.From;
        int price = resit ? course.ExamFeeNok : course.PriceNok;
        if (EnrolledCourses.FirstOrDefault(x => x.From <= open.To && from <= x.To) is { } clash && TicketData.Find(clash.TicketId) is { } other)
            return Reject(action, $"That clashes with your {CourseWord(other)} ({DaysWords(clash.From, clash.To)}).");
        if (CrewBookedClash(from, open.To) is { } crewClash) return Reject(action, crewClash);
        if (OpenRefusal(Club.Office) is { } shut) return Reject(action, shut);
        if (MoneyRefusal(price) is { } broke) return Reject(action, broke);

        if (!resit && dates.FirstOrDefault() is { } first && first != open)
            t.Narrative.Add($"The {CourseWord(ticket)} of {DaysWords(first.From, first.To)} is closed to names; you take the next.");
        string when = $"{DaysWords(from, open.To)}, {ClubPlace}";
        _courseRecords.Add(new CourseRecord { TicketId = ticket.Id, OfferId = open.Offer.Id, From = from, To = open.To, PaidNok = price, Resit = resit });
        var tokens = new[] { ("course", CourseWord(ticket)), ("when", when), ("price", Money(price)) };
        t.Narrative.Add(Fill(resit ? Club.Exam.ResitEnrolLine : Club.EnrolLine, tokens));
        if (VacationDaysOf(from, open.To) is > 0 and var vac)
            t.Narrative.Add($"It will cost {Plural(vac, "vacation day", "vacation days")} by the usual rule.");
        t.Narrative.Add("A wait will stop on its first morning.");
        var book = HandOverBook(t, ticket);
        var result = Accept(t, action, _boat.Orders.PhoneMinutes, price, null, Fill(resit ? Club.Exam.ResitEnrolLog : Club.EnrolLog, tokens), work: false);
        if (book is not null) _log.Add(book);
        return result;
    }

    /// <summary>The first morning of an enrolled course: the waits stop on it (like an event the player marked).</summary>
    private void CourseStops(int endDay, Action<int, string> stopAtDay)
    {
        foreach (var rec in EnrolledCourses)
            if (TicketData.Find(rec.TicketId) is { } ticket)
                foreach (int day in StopDays(rec).Where(d => d > Day && d <= endDay))
                    stopAtDay(day, CourseMorningLine(rec, ticket, day));
    }

    /// <summary>The mornings of a course a wait stops on: the first, and the last day of a course with an exam (the examiner is there that day).</summary>
    private IEnumerable<int> StopDays(CourseRecord rec)
    {
        yield return rec.From;
        if (rec.To > rec.From && TicketData.Find(rec.TicketId)?.Course?.Exam is not null) yield return rec.To;
    }

    private string CourseMorningLine(CourseRecord rec, TicketRule ticket, int day)
    {
        var tokens = new[] { ("day", day.ToString(CultureInfo.InvariantCulture)), ("course", CourseWord(ticket)) };
        return rec.Resit ? Fill(Club.Exam.ResitToday, tokens) : day == rec.From ? Fill(Club.StartsLine, tokens) : Fill(Club.Exam.Today, tokens);
    }

    /// <summary>An enrolled course whose first day is today (the winter's day-by-day wait stops there).</summary>
    private string? CourseStartsTodayWords() =>
        EnrolledCourses.FirstOrDefault(c => StopDays(c).Contains(Day)) is { } rec && TicketData.Find(rec.TicketId) is { } ticket
            ? CourseMorningLine(rec, ticket, Day)
            : null;

    // ------------------------------------------------------------------ the course days

    /// <summary>The player is at the club's port, not at sea.</summary>
    private bool AtTheClub => !AtSea && !_sunk && _portId == Club.Port;

    /// <summary>
    /// Commands that are only a phone call, a mark on the calendar or a decision: they do not take the player from the club (and need no boat). A wait or a
    /// sleep is not one: issued at the club before the session ends it is the day at the club (design note 012, finding 7), so the exam is sat, not slept through.
    /// </summary>
    private static bool KeepsCourseDay(Command c) =>
        c is Enrol or Order or BookHaulout or InviteFriend or PlanEvent or UnplanEvent or Defer or Undefer or ListForSale or AcceptOffer or Keep;

    /// <summary>
    /// The first command of a course day, issued at the club's port before the session ends, is the day at the club: that day's line, the weekday
    /// rule, the clock run on to the end of the session; the command itself is not carried out (the player is at the club, not aboard), and the
    /// result says so. Null: nothing to do with a course today.
    /// </summary>
    private CommandResult? CourseGate(Command command, Turn t)
    {
        if (!TicketData.Enabled || KeepsCourseDay(command) || !AtTheClub) return null;
        if (CourseRecordToday is not { } rec || rec.Held.Contains(Day) || TicketData.Find(rec.TicketId) is not { } ticket) return null;
        int now = MinuteOfDay, until = Club.SessionToMinute;
        if (now >= until) return null;
        bool late = now > Club.SessionFromMinute;
        AttendDay(t, rec, ticket, late);
        string untilWords = FormatTime(ClockMinutes + (until - now));
        _atClub = true;
        try
        {
            return Accept(t, "course", until - now, 0, null, null, work: false, afterTick: () =>
            {
                CloseDay(t, rec, ticket);
                if (!ExamOpen) t.Narrative.Add(Fill(Club.DayTail, ("until", untilWords)));
                return null;
            });
        }
        finally { _atClub = false; }
    }

    /// <summary>The day is spent at the club: marked, charged by the weekday rule (once), and told with its line from data.</summary>
    private void AttendDay(Turn t, CourseRecord rec, TicketRule ticket, bool late)
    {
        rec.Held.Add(Day);
        ChargeDay(t, Day, atSea: false);
        DayAtTheClub(t, rec, ticket, Day, late);
    }

    /// <summary>The day's head, its line from data and its logbook line (a re-sit has the exam's own).</summary>
    private void DayAtTheClub(Turn t, CourseRecord rec, TicketRule ticket, int day, bool late)
    {
        var course = ticket.Course!;
        var ci = CultureInfo.InvariantCulture;
        int n = day - rec.From + 1;
        var weekday = ("weekday", new GameClock(day).WeekdayName);
        var dayToken = ("day", day.ToString(ci));
        var courseToken = ("course", CourseWord(ticket));
        if (rec.Resit)
        {
            t.Narrative.Add(Fill(Club.Exam.ResitDayHead, weekday, dayToken, courseToken));
            if (late) t.Narrative.Add(Club.LateLine);
            t.Narrative.Add(Club.Exam.ResitDayLine);
            _log.Add(new LogEntry(day, TimeOfDay, Fill(Club.Exam.ResitDayLog, courseToken)));
            return;
        }
        t.Narrative.Add(Fill(Club.DayHead, weekday, dayToken, ("n", n.ToString(ci)), ("total", course.Days.ToString(ci)), courseToken));
        if (late) t.Narrative.Add(Club.LateLine);
        t.Narrative.Add(course.DayLines[Math.Min(n - 1, course.DayLines.Count - 1)]);
        _log.Add(new LogEntry(day, TimeOfDay, Fill(Club.DayLog, courseToken, ("n", n.ToString(ci)), ("total", course.Days.ToString(ci)))));
    }

    /// <summary>The end of a day at the club: on the last day the log decides.</summary>
    private void CloseDay(Turn t, CourseRecord rec, TicketRule ticket)
    {
        if (Day == rec.To) DecideLastDay(t, rec, ticket);
    }

    /// <summary>
    /// The last day of a course decides, here and nowhere else (design note 012, step 2b puts the exam in front of this): if the log meets every
    /// ask the ticket is held, with the examiner's signature; otherwise the course is done and the ticket waits for the log. No seeded fail.
    /// </summary>
    private void DecideLastDay(Turn t, CourseRecord rec, TicketRule ticket, bool sitting = true)
    {
        if (ticket.Course?.Exam is not null)
        {
            StartExam(t, rec, ticket, sitting);
            return;
        }
        rec.State = CourseState.Done;
        SignCourse(t, ticket);
    }

    /// <summary>The course is signed (the exam passed, or there is none): the log decides, as before. The ticket is held, or waits for the log.</summary>
    private void SignCourse(Turn t, TicketRule ticket)
    {
        if (LogMeets(ticket))
        {
            GrantTicket(t, ticket, $"{Club.SignedHeld} {ticket.HeldLine}");
            return;
        }
        var lacking = ticket.Asks.Where(a => !AskMet(a)).Select(AskWait).ToList();
        string line = Fill(Club.SignedWaiting, ("list", AndList(lacking)));
        t.Narrative.Add(line);
        _log.Add(new LogEntry(Day, TimeOfDay, line));
    }

    /// <summary>The ticket is held from now on (never lost): told, in the logbook and as an alert.</summary>
    private void GrantTicket(Turn t, TicketRule ticket, string line)
    {
        _ticketsHeld[ticket.Id] = Day;
        t.Narrative.Add(line);
        Raise($"ticket-{ticket.Id}", line, 0, onPassage: false);
    }

    /// <summary>
    /// A course done and a ticket waiting: the first midnight or arrival that finds the log meets every ask, the ticket is held, with an alert and
    /// a logbook line. A ticket another waits for (the radio ticket for the coastal one) can arrive in the same pass.
    /// </summary>
    private void TicketsCheck(Turn t)
    {
        if (!TicketData.Enabled) return;
        bool changed;
        do
        {
            changed = false;
            foreach (var ticket in TicketData.Tickets.Where(DoneAndWaiting).Where(LogMeets).ToList())
            {
                GrantTicket(t, ticket, Fill(Club.LateHeld, ("held", ticket.HeldLine)));
                changed = true;
            }
        } while (changed);
    }

    /// <summary>
    /// The midnight that ends <paramref name="day"/>: a course day not yet spent at the club is spent there if the player is at the club's port (a wait
    /// passed it, or the evening was their own); at sea or at another port it is missed, and the course is forfeit with its fee.
    /// </summary>
    private void CoursesDaily(Turn t, int day)
    {
        if (!TicketData.Enabled) return;
        foreach (var rec in EnrolledCourses.Where(c => c.From <= day && day <= c.To).ToList())
        {
            if (rec.Held.Contains(day) || TicketData.Find(rec.TicketId) is not { } ticket) continue;
            if (AtTheClub)
            {
                // The day went by with the player ashore: the session was theirs. The midnight is after it; the line is told in whatever turn is passing.
                rec.Held.Add(day);
                ChargeDay(t, day, atSea: false);
                DayAtTheClub(t, rec, ticket, day, late: false);
                if (day == rec.To) DecideLastDay(t, rec, ticket, sitting: false);   // a wait passed the day: the paper goes in blank
                continue;
            }
            rec.State = CourseState.Forfeit;
            Raise($"course-missed-{rec.TicketId}", rec.Resit
                ? Fill(Club.Exam.ResitMissed, ("course", CourseWord(ticket)))
                : Fill(Club.MissedLine, ("n", (day - rec.From + 1).ToString(CultureInfo.InvariantCulture)), ("course", CourseWord(ticket))), 0, onPassage: false);
        }
    }

    // ------------------------------------------------------------------ the queries

    private string NextCourseWords(TicketRule t)
    {
        if (Holds(t)) return "";
        if (t.Course is null) return t.NoCourse;
        if (DoneAndWaiting(t)) return Fill(Club.DoneWaiting, ("course", CourseWord(t)));
        if (t.Course.Offers.Count == 0) return t.NoCourse;
        var enrolled = EnrolledCourses.FirstOrDefault(c => c.TicketId == t.Id);
        if (enrolled is { Resit: true }) return Fill(Club.Exam.ResitEnrolled, ("course", CourseWord(t)), ("when", DaysWords(enrolled.From, enrolled.To)), ("place", ClubPlace));
        if (enrolled is not null) return $"You are enrolled on the {CourseWord(t)}: {DaysWords(enrolled.From, enrolled.To)}, {ClubPlace}.";
        if (ExamToResit(t))
            return ResitDates(t).FirstOrDefault() is { } resit
                ? Fill(Club.Exam.ResitNext, ("when", DaysWords(resit.To, resit.To)), ("place", ClubPlace), ("price", Money(t.Course.ExamFeeNok)))
                : Club.Exam.ResitNoDate;
        return Occurrences(t).FirstOrDefault() is { } next
            ? $"Next course: {DaysWords(next.From, next.To)}, {ClubPlace}, {Money(t.Course.PriceNok)}."
            : t.NoCourse;
    }

    private TicketsView TicketsSnapshot() => new(TicketData.Tickets.Select(t =>
    {
        bool held = Holds(t);
        string words = held ? (t.HeldFromStart ? t.HeldFromStartLine : t.HeldLine) : "Not held.";
        return new TicketView(t.Name, held, held && !t.HeldFromStart ? _ticketsHeld[t.Id] : null, words, t.Purpose,
            t.Asks.Select(a => new AskView(AskLine(a), AskMet(a))).ToList(), NextCourseWords(t));
    }).ToList());

    // ------------------------------------------------------------------ the board

    private IEnumerable<CourseCard> CourseCards()
    {
        foreach (var t in TicketData.Tickets)
        {
            if (Holds(t) || t.Course is not { Offers.Count: > 0 } course || DoneAndWaiting(t)) continue;
            if (ExamToResit(t))
            {
                foreach (var card in ResitCards(t, course)) yield return card;
                continue;
            }
            foreach (var o in Occurrences(t))
            {
                bool enrolled = o.Record is { State: CourseState.Enrolled };
                int vacation = VacationDaysOf(o.From, o.To);
                string status = enrolled ? (o.From <= Day ? "on now" : "enrolled") : o.From - Day < Club.EnrolDaysBefore ? "enrolment closed" : "open";
                var shortfall = t.Asks.Where(a => !AskMet(a)).Select(AskLine).ToList();
                string line = Fill(Club.BoardCourse, ("Course", course.Name), ("when", DaysWords(o.From, o.To)), ("place", ClubPlace),
                    ("price", Money(course.PriceNok)), ("days", course.Days.ToString(CultureInfo.InvariantCulture)), ("dayWord", course.Days == 1 ? "day" : "days"),
                    ("vac", vacation > 0 ? ", " + Plural(vacation, "vacation day", "vacation days") : ""));
                yield return new CourseCard(o.From, course.Name, DaysWords(o.From, o.To), ClubPlace, course.PriceNok, course.Days, vacation, enrolled, status, shortfall, line);
            }
        }
    }

    /// <summary>The cards of a failed exam: the one re-sit the player is enrolled on, else a card for each date still open to names.</summary>
    private IEnumerable<CourseCard> ResitCards(TicketRule t, CourseRule course)
    {
        var shortfall = t.Asks.Where(a => !AskMet(a)).Select(AskLine).ToList();
        int vacation(int d) => VacationDaysOf(d, d);
        CourseCard Card(int day, bool enrolled, string status)
        {
            int vac = vacation(day);
            string line = Fill(Club.Exam.BoardResit, ("Course", course.Name), ("when", DaysWords(day, day)), ("place", ClubPlace),
                ("price", Money(course.ExamFeeNok)), ("vac", vac > 0 ? ", " + Plural(vac, "vacation day", "vacation days") : ""));
            return new CourseCard(day, course.Name, DaysWords(day, day), ClubPlace, course.ExamFeeNok, 1, vac, enrolled, status, shortfall, line);
        }
        if (EnrolledCourses.FirstOrDefault(c => c.TicketId == t.Id) is { } rec)
        {
            yield return Card(rec.To, true, rec.To <= Day ? "on now" : "enrolled");
            yield break;
        }
        foreach (var o in ResitDates(t)) yield return Card(o.To, false, "open");
    }

    private NoticeboardView NoticeboardSnapshot()
    {
        var board = _boat.Board;
        if (!board.Enabled) return new NoticeboardView("", [], board.NoBoard.Length > 0 ? board.NoBoard : "There is no board here.");
        if (AtSea) return new NoticeboardView(board.AtSea, []);
        if (!board.Ports.TryGetValue(_portId, out var rule)) return new NoticeboardView("", [], board.NoBoard);
        int year = ListedYear();
        var cards = new List<BoardCard>();
        foreach (var e in EventRules.Where(e => rule.All || rule.Events.Contains(e.Id)))
        {
            var view = ViewOf(e, year);
            cards.Add(new EventCard(view.FromDay, view.Name, view));
        }
        if (rule.All) cards.AddRange(CourseCards());
        if (rule.All) cards.AddRange(CrewingCards());
        var sorted = cards.OrderBy(c => c.Day).ToList();
        return new NoticeboardView(rule.Header, sorted, sorted.Count == 0 ? rule.Empty : null, rule.All ? LadderCards() : null);
    }

    // ------------------------------------------------------------------ the briefing

    /// <summary>The closing lines of the opening briefing: when the shops open, and the board.</summary>
    private IEnumerable<string> BriefingTail()
    {
        if (DayHours.Enabled && _boat.Board.Briefing.Hours.Length > 0)
            yield return Fill(_boat.Board.Briefing.Hours, ("shop", Capital(ShopName)), ("hour", HourWords(DayHours.Weekday.OpenMinute)));
        if (_boat.Board.Enabled && _boat.Board.Briefing.Board.Length > 0) yield return _boat.Board.Briefing.Board;
    }
}
