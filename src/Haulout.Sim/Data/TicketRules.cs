using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haulout.Sim.Data;

/// <summary>
/// One thing a ticket asks of the skipper's log (design note 012, step 2). Kind: miles, daysAboard, nightHours, passagesOver (Nm, AsSkipper),
/// passagesOverNight (Nm), seasonsNoTow, ticket (Ticket), firstAid. Need: the count (miles, days, hours, passages, seasons).
/// </summary>
public sealed record AskRule
{
    public string Kind { get; init; } = "";
    public int Need { get; init; }
    public int Nm { get; init; }
    public bool AsSkipper { get; init; }
    public string Ticket { get; init; } = "";

    public static readonly IReadOnlyList<string> Kinds =
        ["miles", "daysAboard", "nightHours", "passagesOver", "passagesOverNight", "seasonsNoTow", "ticket", "firstAid"];
}

/// <summary>The words of one kind of ask: the checklist line, and what the log must show (for "the ticket comes when your log is up to it").</summary>
public sealed record AskWords
{
    public string Line { get; init; } = "";
    public string LineCrew { get; init; } = "";
    public string Held { get; init; } = "";
    public string NotYet { get; init; } = "";
    public string Wait { get; init; } = "";
    public string WaitCrew { get; init; } = "";
}

/// <summary>A date of a course: the first day on or after this day of the year with this weekday, for the course's days (like an event).</summary>
public sealed record CourseOffer
{
    public string Id { get; init; } = "";
    public int FromDayOfYear { get; init; }
    public string? Weekday { get; init; }
}

/// <summary>The club's course for a ticket. Empty offers: listed, not offered (no date).</summary>
public sealed record CourseRule
{
    public string Name { get; init; } = "";
    public int Days { get; init; } = 1;
    /// <summary>Monday-to-Friday style: the days are weekdays and cost vacation days by the weekday rule (that is derived from the dates, not stored).</summary>
    public bool Weekdays { get; init; }
    /// <summary>From economy.json (courses), by ticket id; not in tickets.json.</summary>
    [JsonIgnore] public int PriceNok { get; init; }
    /// <summary>The exam on the course's last day (design note 012, decision 8). Null: the course has none (the last day is decided by the log alone).</summary>
    public ExamRule? Exam { get; init; }
    /// <summary>From economy.json (examFees), by ticket id: what a re-sit costs. Not in tickets.json.</summary>
    [JsonIgnore] public int ExamFeeNok { get; init; }
    public IReadOnlyList<CourseOffer> Offers { get; init; } = [];
    public IReadOnlyList<string> DayLines { get; init; } = [];
}

/// <summary>The exam at the end of a course: how many questions are asked, how many right pass, and the minutes an answer takes (the player's own time).</summary>
public sealed record ExamRule
{
    public int Questions { get; init; }
    public int PassAt { get; init; }
    public int AnswerMinutes { get; init; }
}

/// <summary>One page of a course book: a short section of plain text.</summary>
public sealed record BookPage
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
}

/// <summary>A course book (design note 012, decision 8): a logbook document the club hands the player on enrolling. Aliases are what `read` accepts besides the title.</summary>
public sealed record CourseBook
{
    public string Ticket { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string HandedLine { get; init; } = "";
    public IReadOnlyList<BookPage> Pages { get; init; } = [];
}

/// <summary>
/// One question of a ticket's bank: four options (the letters a to d in order), exactly one right (<see cref="Answer"/>, never shown to the player),
/// where it comes from and which page of the course book teaches it. Checked: what the author found when verifying it.
/// </summary>
public sealed record ExamQuestionRule
{
    public string Id { get; init; } = "";
    public string Ticket { get; init; } = "";
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Options { get; init; } = [];
    public string Answer { get; init; } = "";
    public string Source { get; init; } = "";
    public string Checked { get; init; } = "";
    public int BookPage { get; init; }
}

/// <summary>questions.json: the course books and the question banks, with the placeholders that mark every question UNVERIFIED.</summary>
public sealed record QuestionBank
{
    public IReadOnlyList<string> Placeholders { get; init; } = [];
    public IReadOnlyList<CourseBook> Books { get; init; } = [];
    public IReadOnlyList<ExamQuestionRule> Questions { get; init; } = [];

    public static QuestionBank Empty { get; } = new();
}

public sealed record TicketRule
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    /// <summary>How another ticket's checklist names it: "the radio ticket".</summary>
    public string AskName { get; init; } = "";
    /// <summary>What it is for, in words.</summary>
    public string Purpose { get; init; } = "";
    public bool HeldFromStart { get; init; }
    public string HeldFromStartLine { get; init; } = "";
    public string HeldLine { get; init; } = "";
    /// <summary>What to say when there is no course to take (none offered, or nothing to take).</summary>
    public string NoCourse { get; init; } = "";
    public IReadOnlyList<AskRule> Asks { get; init; } = [];
    public CourseRule? Course { get; init; }
}

/// <summary>The club and its words (design note 012, step 2). Templates take {braced} tokens that the game fills.</summary>
public sealed record ClubRules
{
    public string Port { get; init; } = "";
    public string Name { get; init; } = "the club";
    public string Office { get; init; } = "the club office";
    public string SessionFrom { get; init; } = "09:00";
    public string SessionTo { get; init; } = "16:00";
    /// <summary>Enrolment closes this many days before the first day.</summary>
    public int EnrolDaysBefore { get; init; } = 1;
    /// <summary>The passage lengths the skipper's words and the log's counts name (nautical miles), shortest first.</summary>
    public IReadOnlyList<int> PassageNm { get; init; } = [];
    public string PlaceLine { get; init; } = "";
    public string EnrolLine { get; init; } = "";
    public string EnrolLog { get; init; } = "";
    public string StartsLine { get; init; } = "";
    public string DayHead { get; init; } = "";
    public string DayTail { get; init; } = "";
    public string DayLog { get; init; } = "";
    public string LateLine { get; init; } = "";
    public string SignedHeld { get; init; } = "";
    public string SignedWaiting { get; init; } = "";
    public string LateHeld { get; init; } = "";
    public string MissedLine { get; init; } = "";
    public string DoneWaiting { get; init; } = "";
    public string FutureDone { get; init; } = "";
    public string BoardCourse { get; init; } = "";
    public string NoBoard { get; init; } = "";
    /// <summary>The exam's words (decision 8). Tokens: {total}, {n}, {right}, {need}, {course}, {when}, {price}, {text}, {page}, {title}, {book}.</summary>
    public ExamWords Exam { get; init; } = new();

    public int SessionFromMinute => EconomyData.HoursData.Minute(SessionFrom);
    public int SessionToMinute => EconomyData.HoursData.Minute(SessionTo);
}

public sealed record ExamWords
{
    public string Sits { get; init; } = "";
    public string Question { get; init; } = "";
    public string Taken { get; init; } = "";
    public string Waiting { get; init; } = "";
    public string Invalid { get; init; } = "";
    public string NoExam { get; init; } = "";
    public string Score { get; init; } = "";
    public string Missed { get; init; } = "";
    public string Failed { get; init; } = "";
    public string FailedNext { get; init; } = "";
    public string FailedNoDate { get; init; } = "";
    public string FailedLog { get; init; } = "";
    public string PassedLog { get; init; } = "";
    public string Abandoned { get; init; } = "";
    public string Unanswered { get; init; } = "";
    public string ResitNext { get; init; } = "";
    public string ResitNoDate { get; init; } = "";
    public string ResitEnrolLine { get; init; } = "";
    public string ResitEnrolLog { get; init; } = "";
    public string ResitDayHead { get; init; } = "";
    public string ResitDayLine { get; init; } = "";
    public string ResitDayLog { get; init; } = "";
    public string ResitMissed { get; init; } = "";
    public string ResitEnrolled { get; init; } = "";
    public string BoardResit { get; init; } = "";
    public string NotAHeldBook { get; init; } = "";
    public string Today { get; init; } = "";
    public string ResitToday { get; init; } = "";
}

/// <summary>
/// The skipper's tickets (design note 012, decision 3 and step 2): tickets.json. The default has none, so a boat written in a test has no
/// tickets, no club and no board. Every number in the shipped file is a PLACEHOLDER marked UNVERIFIED.
/// </summary>
public sealed record TicketRules
{
    public ClubRules Club { get; init; } = new();
    public IReadOnlyDictionary<string, AskWords> Asks { get; init; } = new Dictionary<string, AskWords>();
    /// <summary>The words of the skipper's log, by key.</summary>
    public IReadOnlyDictionary<string, string> Skipper { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<TicketRule> Tickets { get; init; } = [];
    /// <summary>Filled from questions.json by the loader.</summary>
    [JsonIgnore] public QuestionBank Bank { get; init; } = QuestionBank.Empty;

    public CourseBook? BookOf(string ticketId) => Bank.Books.FirstOrDefault(b => b.Ticket == ticketId);

    public IReadOnlyList<ExamQuestionRule> QuestionsOf(string ticketId) => Bank.Questions.Where(q => q.Ticket == ticketId).ToList();

    public bool Enabled => Tickets.Count > 0;

    public TicketRule? Find(string id) => Tickets.FirstOrDefault(t => t.Id == id);

    public static TicketRules Default { get; } = new();
}

/// <summary>What one port's notice board carries. All: everything (the club's board); else the calendar events posted there, by id.</summary>
public sealed record BoardPortRule
{
    public string Header { get; init; } = "";
    public bool All { get; init; }
    public IReadOnlyList<string> Events { get; init; } = [];
    public string Empty { get; init; } = "";
}

public sealed record BoardBriefing
{
    public string Hours { get; init; } = "";
    public string Board { get; init; } = "";
}

/// <summary>One day of a crewing place's plan (design note 012, step 4): the leg the owner sails that day, from data. Nm 0: a lay day (From and To are the place she lies at).</summary>
public sealed record CrewLegRule
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public double Nm { get; init; }
    /// <summary>Hour of the day she casts off (10.5 is half past ten); it may be followed by hours that run through midnight.</summary>
    public double DepartHour { get; init; }
    public double Hours { get; init; }
    /// <summary>She lies away from Skjaervik at the midnight that ends this day (a night away).</summary>
    public bool AwayNight { get; init; }
    /// <summary>The port named is not on the player's chart; the day says so the first time.</summary>
    public bool ToOffChart { get; init; }
    public string Line { get; init; } = "";
}

/// <summary>What a crewing place asks of the player: the coastal ticket, or a season's log of so many miles and nights away from home.</summary>
public sealed record CrewAsksRule
{
    public string Ticket { get; init; } = "";
    public int Miles { get; init; }
    public int NightsAway { get; init; }
    public string Words { get; init; } = "";
}

/// <summary>The far boat's card: a description the player can read again (a logbook document).</summary>
public sealed record CrewBoatCard
{
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<string> Lines { get; init; } = [];
}

/// <summary>A crewing place: a dated card (a day of the year and a weekday, like an event), a boat, a plan per day, and what it asks. All numbers are placeholders.</summary>
public sealed record CrewPlaceRule
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public int FromDayOfYear { get; init; }
    public string Weekday { get; init; } = "";
    public int Days { get; init; } = 1;
    public string Owner { get; init; } = "";
    public string Boat { get; init; } = "";
    public string Pay { get; init; } = "";
    public string Card { get; init; } = "";
    public CrewAsksRule? Asks { get; init; }
    /// <summary>The player must be at Skjaervik by this hour on the first day, or the boat sails without them.</summary>
    public int MeetHour { get; init; }
    /// <summary>The hour of the last day the player is back at Skjaervik.</summary>
    public int ReturnHour { get; init; }
    public string Meet { get; init; } = "";
    public CrewBoatCard BoatCard { get; init; } = new();
    public string CostLine { get; init; } = "";
    public int NewPriceNok { get; init; }
    public string ReturnLine { get; init; } = "";
    public IReadOnlyList<CrewLegRule> Plan { get; init; } = [];
}

/// <summary>The crewing places and their words (design note 012, decision 5, step 4). Words are templates with {braced} tokens that the game fills.</summary>
public sealed record CrewingRules
{
    public string Port { get; init; } = "";
    /// <summary>The owner needs to know this many days before.</summary>
    public int EnrolDaysBefore { get; init; } = 1;
    public IReadOnlyDictionary<string, string> Words { get; init; } = new Dictionary<string, string>();
    /// <summary>The harbourmaster's letter, a line by the kind of the alert that was queued while the player was away ({day} is filled).</summary>
    public IReadOnlyDictionary<string, string> LetterLines { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> BoardStatus { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<CrewPlaceRule> Places { get; init; } = [];

    public bool Enabled => Places.Count > 0;
    public string Word(string key) => Words.TryGetValue(key, out var w) ? w : "";
    public CrewPlaceRule? Find(string id) => Places.FirstOrDefault(p => p.Id == id);
}

/// <summary>One line of a boat's running costs as the yard would quote them.</summary>
public sealed record LadderCostRule
{
    public string Label { get; init; } = "";
    public int Nok { get; init; }
}

/// <summary>A boat in the broker's window (design note 012, step 5; design note 011): fictional, priced by the broker's word and the yard's quote.</summary>
public sealed record LadderBoatRule
{
    public string Name { get; init; } = "";
    public string Yard { get; init; } = "";
    public int LengthFt { get; init; }
    public double LengthM { get; init; }
    public string Type { get; init; } = "";
    public int Year { get; init; }
    public string Lies { get; init; } = "";
    public int AskingNok { get; init; }
    public string Pro { get; init; } = "";
    public string Con { get; init; } = "";
    public IReadOnlyList<LadderCostRule> Costs { get; init; } = [];
    /// <summary>The ticket her insurer asks, by ticket id.</summary>
    public string Ticket { get; init; } = "";
}

public sealed record LadderYours
{
    public int LengthFt { get; init; }
    public double LengthM { get; init; }
    public string Type { get; init; } = "";
    public string Ticket { get; init; } = "";
}

/// <summary>The broker's window at the club's port (ladder.json). Words are templates with {braced} tokens that the game fills.</summary>
public sealed record LadderRules
{
    public string Port { get; init; } = "";
    public IReadOnlyDictionary<string, string> Words { get; init; } = new Dictionary<string, string>();
    public LadderYours Yours { get; init; } = new();
    public IReadOnlyList<LadderBoatRule> Boats { get; init; } = [];
    public IReadOnlyList<string> Placeholders { get; init; } = [];

    public bool Enabled => Boats.Count > 0;
    public string Word(string key) => Words.TryGetValue(key, out var w) ? w : "";
}

/// <summary>The notice boards (design note 012, decision 6): board.json. The default has no boards.</summary>
public sealed record BoardRules
{
    /// <summary>The broker's window (ladder.json), filled by the loader.</summary>
    [JsonIgnore] public LadderRules Ladder { get; init; } = new();
    public CrewingRules Crewing { get; init; } = new();
    public BoardBriefing Briefing { get; init; } = new();
    public string AtSea { get; init; } = "";
    public string NoBoard { get; init; } = "";
    public IReadOnlyDictionary<string, BoardPortRule> Ports { get; init; } = new Dictionary<string, BoardPortRule>();

    public bool Enabled => Ports.Count > 0;

    public static BoardRules Default { get; } = new();
}

/// <summary>Reads tickets.json and board.json (the ids and ask kinds are checked against each other, and the prices against economy.json).</summary>
internal static class TicketLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] Weekdays = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    public static TicketRules LoadTickets(string json, EconomyData eco, string? questionsJson = null)
    {
        var bank = questionsJson is null
            ? QuestionBank.Empty
            : JsonSerializer.Deserialize<QuestionBank>(questionsJson, Options) ?? throw new InvalidOperationException("questions.json is empty.");
        var rules = JsonSerializer.Deserialize<TicketRules>(json, Options) ?? throw new InvalidOperationException("tickets.json is empty.");
        var tickets = new List<TicketRule>();
        foreach (var t in rules.Tickets)
        {
            if (rules.Tickets.Count(x => x.Id == t.Id) > 1) throw new InvalidOperationException($"tickets.json: ticket '{t.Id}' is listed twice.");
            foreach (var a in t.Asks)
            {
                if (!AskRule.Kinds.Contains(a.Kind)) throw new InvalidOperationException($"tickets.json: ticket '{t.Id}' asks '{a.Kind}', which is not a kind of ask.");
                if (!rules.Asks.ContainsKey(a.Kind)) throw new InvalidOperationException($"tickets.json: no words for the ask '{a.Kind}'.");
                if (a.Kind == "ticket" && rules.Tickets.All(x => x.Id != a.Ticket))
                    throw new InvalidOperationException($"tickets.json: ticket '{t.Id}' asks for the ticket '{a.Ticket}', which is not listed.");
            }
            var course = t.Course;
            if (course is not null)
            {
                if (!eco.Courses.TryGetValue(t.Id, out int price))
                    throw new InvalidOperationException($"economy.json: no price in 'courses' for the course of '{t.Id}'.");
                course = course with { PriceNok = price };
                if (course.Exam is { } exam)
                {
                    if (!eco.ExamFees.TryGetValue(t.Id, out int fee))
                        throw new InvalidOperationException($"economy.json: no fee in 'examFees' for the exam of '{t.Id}'.");
                    course = course with { ExamFeeNok = fee };
                    CheckExam(t.Id, exam, bank);
                }
                if (course.Offers.Count > 0 && course.DayLines.Count != course.Days)
                    throw new InvalidOperationException($"tickets.json: the course of '{t.Id}' has {course.Days} days and {course.DayLines.Count} lines.");
                foreach (var o in course.Offers)
                    if (o.Weekday is { Length: > 0 } w && !Weekdays.Contains(w.ToLowerInvariant()))
                        throw new InvalidOperationException($"tickets.json: the course of '{t.Id}' has an offer on '{w}', which is not a weekday.");
            }
            tickets.Add(t with { Course = course });
        }
        return rules with { Tickets = tickets, Bank = bank };
    }

    /// <summary>An exam needs a book and a bank: enough questions, four options with one right, a source, and a book page that exists.</summary>
    private static void CheckExam(string ticket, ExamRule exam, QuestionBank bank)
    {
        if (exam.Questions <= 0 || exam.PassAt <= 0 || exam.PassAt > exam.Questions || exam.AnswerMinutes < 0)
            throw new InvalidOperationException($"tickets.json: the exam of '{ticket}' asks {exam.Questions} and passes at {exam.PassAt}.");
        var book = bank.Books.FirstOrDefault(b => b.Ticket == ticket) ?? throw new InvalidOperationException($"questions.json: no course book for '{ticket}'.");
        var questions = bank.Questions.Where(q => q.Ticket == ticket).ToList();
        if (questions.Count < exam.Questions) throw new InvalidOperationException($"questions.json: '{ticket}' has {questions.Count} questions, the exam asks {exam.Questions}.");
        if (bank.Placeholders.Count == 0 || !bank.Placeholders.Any(p => p.Contains("UNVERIFIED", StringComparison.Ordinal)))
            throw new InvalidOperationException("questions.json: the bank must say its questions are UNVERIFIED in its placeholders.");
        foreach (var q in questions)
        {
            if (q.Options.Count != 4) throw new InvalidOperationException($"questions.json: '{q.Id}' has {q.Options.Count} options, not four.");
            if (q.Answer is not ("a" or "b" or "c" or "d")) throw new InvalidOperationException($"questions.json: '{q.Id}' has answer '{q.Answer}', not a letter a to d.");
            if (q.Source.Length == 0) throw new InvalidOperationException($"questions.json: '{q.Id}' has no source.");
            if (book.Pages.All(p => p.Number != q.BookPage)) throw new InvalidOperationException($"questions.json: '{q.Id}' is taught on page {q.BookPage}, which the {book.Title} lacks.");
        }
        if (questions.Select(q => q.Id).Distinct().Count() != questions.Count) throw new InvalidOperationException($"questions.json: '{ticket}' has a question id twice.");
    }

    /// <summary>The crewing places (design note 012, step 4): a port that exists, a weekday, a plan of one leg a day, a ticket that is listed, an hour that is an hour.</summary>
    public static void CheckCrewing(CrewingRules c, TripRules? trips, TicketRules? tickets)
    {
        if (trips?.FindPort(c.Port) is null) throw new InvalidOperationException($"board.json: crewing is at '{c.Port}', which is not in the trips.");
        foreach (var p in c.Places)
        {
            if (c.Places.Count(x => x.Id == p.Id) > 1) throw new InvalidOperationException($"board.json: crewing place '{p.Id}' is listed twice.");
            if (!Weekdays.Contains(p.Weekday.ToLowerInvariant())) throw new InvalidOperationException($"board.json: crewing place '{p.Id}' is on '{p.Weekday}', which is not a weekday.");
            if (p.Days <= 0 || p.Plan.Count != p.Days) throw new InvalidOperationException($"board.json: crewing place '{p.Id}' has {p.Days} days and {p.Plan.Count} legs in its plan.");
            if (p.Owner.Length == 0 || p.Boat.Length == 0 || p.Card.Length == 0 || p.BoatCard.Lines.Count == 0)
                throw new InvalidOperationException($"board.json: crewing place '{p.Id}' needs an owner, a boat, a card and a description of the boat.");
            if (p.MeetHour is < 0 or > 23 || p.ReturnHour is < 0 or > 23) throw new InvalidOperationException($"board.json: crewing place '{p.Id}' has an hour outside the day.");
            if (p.Asks is { Ticket.Length: > 0 } asks && (tickets?.Find(asks.Ticket) is null))
                throw new InvalidOperationException($"board.json: crewing place '{p.Id}' asks for the ticket '{asks.Ticket}', which is not listed.");
            foreach (var leg in p.Plan)
                if (leg.Nm < 0 || leg.Hours < 0 || leg.DepartHour < 0 || leg.DepartHour >= 24 || leg.Line.Length == 0)
                    throw new InvalidOperationException($"board.json: crewing place '{p.Id}' has a leg with a bad number or no line.");
        }
    }

    public static BoardRules LoadBoard(string json, string? ladderJson = null)
    {
        var board = JsonSerializer.Deserialize<BoardRules>(json, Options) ?? throw new InvalidOperationException("board.json is empty.");
        if (ladderJson is null) return board;
        var ladder = JsonSerializer.Deserialize<LadderRules>(ladderJson, Options) ?? throw new InvalidOperationException("ladder.json is empty.");
        return board with { Ladder = ladder };
    }

    /// <summary>The broker's window: a port that exists, boats in ascending order of price, a listed ticket for each, costs, and UNVERIFIED in the placeholders.</summary>
    public static void CheckLadder(LadderRules l, TripRules? trips, TicketRules? tickets)
    {
        if (!l.Enabled) return;
        if (trips?.FindPort(l.Port) is null) throw new InvalidOperationException($"ladder.json: the window is at '{l.Port}', which is not in the trips.");
        if (l.Placeholders.Count == 0 || !l.Placeholders.Any(p => p.Contains("UNVERIFIED", StringComparison.Ordinal)))
            throw new InvalidOperationException("ladder.json: the placeholders must say the numbers are UNVERIFIED.");
        if (tickets?.Find(l.Yours.Ticket) is null) throw new InvalidOperationException($"ladder.json: the player's own ticket '{l.Yours.Ticket}' is not listed.");
        int last = 0;
        foreach (var b in l.Boats)
        {
            if (b.AskingNok <= last) throw new InvalidOperationException($"ladder.json: '{b.Name}' is not dearer than the boat before her.");
            last = b.AskingNok;
            if (b.Costs.Count == 0 || b.Costs.Any(c => c.Nok <= 0)) throw new InvalidOperationException($"ladder.json: '{b.Name}' has no running costs or a cost of nothing.");
            if (tickets?.Find(b.Ticket) is null) throw new InvalidOperationException($"ladder.json: '{b.Name}' asks the ticket '{b.Ticket}', which is not listed.");
        }
    }
}
