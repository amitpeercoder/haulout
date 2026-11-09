using System.Globalization;
using System.Text.RegularExpressions;
using Haulout.Sim.Data;

namespace Haulout.Sim.Play;

/// <summary>
/// The course books and the exam (design note 012, decision 8, step 2b). A course book is a logbook document the club hands over on enrolling; it stays
/// readable (<see cref="ReadDocumentSnapshot"/>). The last day of a course that has an exam is the exam: <see cref="StartExam"/> picks the questions from the
/// ticket's bank with a stream of its own (salt <see cref="SaltExam"/>, keyed on the ticket and the day), the examiner asks them one at a time
/// (<see cref="ExamQuestionSnapshot"/>), <see cref="DoAnswer"/> takes the player's letters, and <see cref="FinishExam"/> marks the paper. The only uncertainty is
/// the player's answers: nothing here draws a random number except the choice of questions. Leaving the paper (a wait, sleep) marks what is not answered as wrong.
/// No view or line ever carries the right letter or the right option.
/// </summary>
public sealed partial class Game
{
    /// <summary>The stream that picks an exam's questions: world seed, this salt, the ticket and the day.</summary>
    private const int SaltExam = 13;

    private sealed class ExamState
    {
        public required CourseRecord Record { get; init; }
        public required TicketRule Ticket { get; init; }
        public required List<ExamQuestionRule> Questions { get; init; }
        /// <summary>The option index chosen for each question asked so far.</summary>
        public List<int> Given { get; } = [];
        public int Next => Given.Count;
        /// <summary>The player sat down at the table (false: a wait passed the day and the paper went in blank).</summary>
        public bool Sat { get; set; }
    }

    private ExamState? _exam;
    /// <summary>The tickets whose course book the club has handed over, in order.</summary>
    private readonly List<string> _books = [];

    private ExamWords ExamText => Club.Exam;

    /// <summary>The exam is open: the examiner is waiting for an answer.</summary>
    private bool ExamOpen => _exam is not null;

    /// <summary>For tests only: the letter that would be right for the question now open (a test must be able to pass, and to fail, on purpose).</summary>
    internal string ExamRightLetterForTests() => _exam!.Questions[_exam.Next].Answer;

    /// <summary>For tests only: the question texts of the open exam, in the order asked.</summary>
    internal IReadOnlyList<string> ExamQuestionsForTests() => _exam!.Questions.Select(q => q.Text).ToList();

    /// <summary>For tests only: the right option text of each question of the open exam.</summary>
    internal IReadOnlyList<string> ExamRightTextsForTests() => _exam!.Questions.Select(q => q.Options["abcd".IndexOf(q.Answer[0])]).ToList();

    /// <summary>Waiting and sleeping leave the table; everything else needs the player's hands or the boat and is refused while the paper is open.</summary>
    private static bool LeavesExam(Command c) => c is WaitDays or NextWeekend or WaitWeek or WaitUntilSpring or Sleep;

    // ------------------------------------------------------------------ the books

    /// <summary>The club hands over the course book on the first enrolment on a course that has one. Returns the logbook entry to add after the command's own.</summary>
    private LogEntry? HandOverBook(Turn t, TicketRule ticket)
    {
        if (_books.Contains(ticket.Id) || TicketData.BookOf(ticket.Id) is not { } book) return null;
        _books.Add(ticket.Id);
        t.Narrative.Add(book.HandedLine);
        return new LogEntry(Day, TimeOfDay, book.HandedLine, 0, $"book {ticket.Id}");
    }

    private IEnumerable<CourseBook> HeldBooks => _books.Select(TicketData.BookOf).OfType<CourseBook>();

    private static string BookWord(CourseBook b) => b.Title.StartsWith("The ", StringComparison.Ordinal) ? b.Title[4..] : b.Title;

    /// <summary>What can be read by name: the held books, the season pages written, the letters received.</summary>
    private List<string> DocumentNames()
    {
        var names = HeldBooks.Select(b => Lower(b.Title)).ToList();
        names.AddRange(HeldBoats.Select(p => Lower(p.BoatCard.Title)));
        names.AddRange(_seasonPages.Select(p => $"season page {p.Season}"));
        names.AddRange(_log.Where(e => e.Document?.StartsWith("letter ", StringComparison.Ordinal) == true).Select(e => e.Document!));
        return names;
    }

    private static readonly Regex SeasonName = new(@"^(?:season(?: page)?|page)\s*(\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LetterName = new(@"^letter\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private DocumentView ReadDocumentSnapshot(string name)
    {
        var books = HeldBooks.ToList();
        // The logbook's own key for a course book ("book radio") reads it too, so an adapter can hand the entry's document over as it is.
        if (NameResolver.Normalize(name) is var key && key.StartsWith("book ", StringComparison.Ordinal) && NameResolver.Resolve(books, key[5..], b => b.Ticket, b => b.Title, b => b.Aliases).Match is { } keyed)
            return new DocumentView(keyed.Title, keyed.Pages.Select(p => new DocumentPage(p.Number, p.Title, p.Text)).ToList());
        var r = NameResolver.Resolve(books, name, b => b.Ticket, b => b.Title, b => b.Aliases);
        if (r.Match is { } book)
            return new DocumentView(book.Title, book.Pages.Select(p => new DocumentPage(p.Number, p.Title, p.Text)).ToList());
        if (r.Kind == ResolutionKind.Ambiguous)
            return new DocumentView("", [], $"'{name.Trim()}' could be: {string.Join(", ", r.Candidates.Select(b => b.Title))}. Be more specific.");

        if (CrewBoatDocument(name) is { } boatCard) return boatCard;
        string q = NameResolver.Normalize(name);
        if (q.StartsWith("boat ", StringComparison.Ordinal) && CrewBoatDocument(q[5..]) is { } byKey) return byKey;
        if (SeasonName.Match(q) is { Success: true } sm)
        {
            int? n = sm.Groups[1].Success ? int.Parse(sm.Groups[1].Value, CultureInfo.InvariantCulture) : null;
            var page = n is { } k ? _seasonPages.FirstOrDefault(p => p.Season == k) : _seasonPages.LastOrDefault();
            if (page is not null)
                return new DocumentView($"Season {page.Season}", [new DocumentPage(1, "", string.Join("\n", page.Lines))]);
        }
        if (LetterName.Match(q) is { Success: true } lm && _log.FirstOrDefault(e => e.Document == $"letter {lm.Groups[1].Value}") is { } letter)
            return new DocumentView($"Letter {lm.Groups[1].Value}", [new DocumentPage(1, "", letter.Text)]);
        if (q is "survey" or "survey report") return new DocumentView("", [], "The survey report is read with 'survey'.");
        if (q is "sale" or "viewing" || q.StartsWith("viewing ", StringComparison.Ordinal)) return new DocumentView("", [], "The buyers' notes are read with 'sale'.");

        var readable = DocumentNames();
        string tail = readable.Count == 0 ? " There is nothing in the logbook to read this way yet." : $" You can read: {AndList(readable)}.";
        return new DocumentView("", [], Fill(ExamText.NotAHeldBook, ("name", name.Trim())) + tail);
    }

    // ------------------------------------------------------------------ the exam

    /// <summary>The questions of an exam: distinct, from the ticket's bank, by a stream keyed on the ticket and the day.</summary>
    private List<ExamQuestionRule> PickQuestions(TicketRule ticket, int day)
    {
        var pool = TicketData.QuestionsOf(ticket.Id).ToList();
        var rng = new SeededRandom(SeasonSeed(_worldSeed, SaltExam, StableHash(ticket.Id), day));
        var picked = new List<ExamQuestionRule>();
        for (int n = Math.Min(ticket.Course!.Exam!.Questions, pool.Count); picked.Count < n;)
        {
            int i = (int)(rng.NextDouble() * pool.Count);
            picked.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return picked;
    }

    private List<string> QuestionLines(ExamState ex)
    {
        var q = ex.Questions[ex.Next];
        var lines = new List<string>
        {
            Fill(ExamText.Question, ("n", (ex.Next + 1).ToString(CultureInfo.InvariantCulture)), ("total", ex.Questions.Count.ToString(CultureInfo.InvariantCulture)), ("text", q.Text)),
        };
        for (int i = 0; i < q.Options.Count; i++) lines.Add($"  {"abcd"[i]}) {q.Options[i]}");
        return lines;
    }

    /// <summary>
    /// The last day's exam. <paramref name="sitting"/>: the player is at the table after the day's session; the first question is put. Not sitting (a wait passed
    /// the day): the paper is marked with nothing answered. A course with no exam is decided by the log alone, as before.
    /// </summary>
    private void StartExam(Turn t, CourseRecord rec, TicketRule ticket, bool sitting)
    {
        _exam = new ExamState { Record = rec, Ticket = ticket, Questions = PickQuestions(ticket, Day) };
        if (!sitting)
        {
            t.Narrative.Add(ExamText.Unanswered);
            FinishExam(t);
            return;
        }
        _exam.Sat = true;
        t.Narrative.Add(Fill(ExamText.Sits, ("total", _exam.Questions.Count.ToString(CultureInfo.InvariantCulture))));
        t.Narrative.AddRange(QuestionLines(_exam));
    }

    private ExamQuestionView ExamQuestionSnapshot()
    {
        if (_exam is not { } ex) return new ExamQuestionView(false, 0, 0, "", [], ExamText.NoExam.Length > 0 ? ExamText.NoExam : "There is no exam to sit.");
        var q = ex.Questions[ex.Next];
        var lines = QuestionLines(ex);
        return new ExamQuestionView(true, ex.Next + 1, ex.Questions.Count, q.Text,
            q.Options.Select((o, i) => new ExamOptionView("abcd"[i].ToString(), o)).ToList(), lines[0]);
    }

    private CommandResult DoAnswer(Turn t, string letter)
    {
        const string action = "answer";
        var ex = _exam!;
        string l = (letter ?? "").Trim().TrimEnd(')', '.').ToLowerInvariant();
        int index = l.Length == 1 ? "abcd".IndexOf(l[0]) : -1;
        if (index < 0) return Reject(action, ExamText.Invalid);

        ex.Given.Add(index);
        t.Narrative.Add(ExamText.Taken);
        bool last = ex.Next >= ex.Questions.Count;
        _atClub = true;
        try
        {
            return Accept(t, action, ex.Ticket.Course!.Exam!.AnswerMinutes, 0, null, null, work: false, afterTick: () =>
            {
                if (last) FinishExam(t);
                else t.Narrative.AddRange(QuestionLines(ex));
                return null;
            });
        }
        finally { _atClub = false; }
    }

    /// <summary>
    /// The paper is marked: a pass at the data's threshold signs the course (and the log rule then decides the ticket), a fail names each wrong question and where
    /// the course book covers it, never the right option. The course is done either way; a fail leaves the exam to re-sit.
    /// </summary>
    private void FinishExam(Turn t)
    {
        var ex = _exam!;
        _exam = null;
        var rec = ex.Record;
        var ticket = ex.Ticket;
        var rule = ticket.Course!.Exam!;
        var ci = CultureInfo.InvariantCulture;
        int total = ex.Questions.Count;
        var wrong = new List<ExamQuestionRule>();
        for (int i = 0; i < total; i++)
        {
            var q = ex.Questions[i];
            if (i >= ex.Given.Count || ex.Given[i] != "abcd".IndexOf(q.Answer[0])) wrong.Add(q);
        }
        int right = total - wrong.Count;
        bool left = ex.Sat && ex.Given.Count < total;
        if (left) t.Narrative.Add(ExamText.Abandoned);
        var tokens = new[] { ("right", right.ToString(ci)), ("total", total.ToString(ci)), ("need", rule.PassAt.ToString(ci)), ("course", CourseWord(ticket)) };
        t.Narrative.Add(Fill(ExamText.Score, tokens));
        rec.State = CourseState.Done;
        if (right >= rule.PassAt)
        {
            _log.Add(new LogEntry(Day, TimeOfDay, Fill(ExamText.PassedLog, tokens)));
            SignCourse(t, ticket);
            return;
        }
        rec.ExamFailed = true;
        var book = TicketData.BookOf(ticket.Id);
        foreach (var q in wrong)
        {
            var page = book?.Pages.FirstOrDefault(p => p.Number == q.BookPage);
            t.Narrative.Add(Fill(ExamText.Missed, ("text", q.Text), ("book", book is null ? "course book" : BookWord(book)),
                ("page", q.BookPage.ToString(ci)), ("title", page?.Title ?? "")));
        }
        t.Narrative.Add(Fill(ExamText.Failed, tokens));
        t.Narrative.Add(ResitDates(ticket).FirstOrDefault() is { } next
            ? Fill(ExamText.FailedNext, ("when", DaysWords(next.To, next.To)), ("place", ClubPlace), ("price", Money(ticket.Course!.ExamFeeNok)), ("course", CourseWord(ticket)))
            : ExamText.FailedNoDate);
        _log.Add(new LogEntry(Day, TimeOfDay, Fill(ExamText.FailedLog, tokens)));
    }
}
