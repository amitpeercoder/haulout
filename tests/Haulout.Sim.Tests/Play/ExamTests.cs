using System.Text.Json;
using Haulout.Sim.Data;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

/// <summary>The course books and the exam (design note 012, decision 8, step 2b): reading, the exam on the last day, answering, passing, failing, re-sitting.</summary>
public class ExamTests
{
    private static CommandResult Ok(CommandResult r) => ClubGame.Ok(r);
    private static string Text(CommandResult r) => ClubGame.Text(r);
    private static StatusView Status(Game g) => ClubGame.Status(g);
    private static TicketView Ticket(Game g, string name) => ClubGame.Tickets(g).Tickets.Single(t => t.Name == name);
    private static ExamQuestionView Exam(Game g) => (ExamQuestionView)g.Ask(new ExamQuestion());
    private static DocumentView Read(Game g, string name) => (DocumentView)g.Ask(new ReadDocument(name));
    private static TicketRules Rules() => BoatLoader.LoadSloop28(withHours: true).Tickets;

    private static CommandResult No(CommandResult r)
    {
        Assert.False(r.Accepted, Text(r));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    /// <summary>Enrolled on the radio course and at the club on its Saturday, day 138: the first command has been the day, and the exam is open.</summary>
    private static Game RadioExam(int startDay = 100)
    {
        var g = ClubGame.Afloat(startDay);
        Ok(g.Execute(new Enrol("radio")));
        ClubGame.ToMorning(g, 138);
        var day = Ok(g.Execute(new Look()));
        Assert.Equal("course", day.Action);
        Assert.True(Exam(g).Open);
        return g;
    }

    /// <summary>Answers the open exam: right where <paramref name="correct"/> says so, wrong elsewhere. Returns every answer's result.</summary>
    private static List<CommandResult> Sit(Game g, params bool[] correct)
    {
        var results = new List<CommandResult>();
        foreach (var right in correct)
        {
            string letter = g.ExamRightLetterForTests();
            results.Add(Ok(g.Execute(new Answer(right ? letter : ClubGame.WrongLetter(letter)))));
        }
        return results;
    }

    // ------------------------------------------------------------------ the books

    [Fact]
    public void Enrolling_hands_over_the_course_book_into_the_logbook_and_read_returns_its_pages_in_words()
    {
        var g = ClubGame.Afloat(100);
        var before = Read(g, "radio course book");
        Assert.Equal("", before.Title);
        Assert.StartsWith("You have no document called 'radio course book'.", before.Note);

        var enrol = Ok(g.Execute(new Enrol("radio")));
        Assert.Contains("The club secretary hands you the radio course book.", enrol.Narrative);
        var entry = ClubGame.Log(g).Entries.Single(e => e.Document is not null && e.Document.StartsWith("book", StringComparison.Ordinal));
        Assert.Equal(Read(g, "radio course book").Title, Read(g, entry.Document!).Title); // the logbook's own key reads it, so an adapter hands the entry over as it is
        Assert.NotEmpty(Read(g, entry.Document!).Pages);
        Assert.Equal("The club secretary hands you the radio course book.", entry.Text);

        foreach (var name in new[] { "radio course book", "the radio course book", "radio book", "course book", "radio" })
        {
            var book = Read(g, name);
            Assert.Equal("The radio course book", book.Title);
            Assert.Equal(7, book.Pages.Count);
            Assert.Equal("Channel 16 and channel 70", book.Pages[0].Title);
            Assert.Contains("distress, urgency, safety and calling", book.Pages[0].Text);
        }
        Assert.Equal(Rules().BookOf("radio")!.Pages.Select(p => p.Text), Read(g, "radio course book").Pages.Select(p => p.Text));
    }

    [Fact]
    public void A_second_course_book_makes_the_plain_name_ambiguous_and_the_books_stay_readable_for_good()
    {
        var g = ClubGame.Afloat(100);
        Ok(g.Execute(new Enrol("radio")));
        Ok(g.Execute(new Enrol("coastal")));
        var ambiguous = Read(g, "course book");
        Assert.Equal("", ambiguous.Title);
        Assert.Contains("could be: The radio course book, The coastal skipper course book", ambiguous.Note);
        Assert.Equal("The coastal skipper course book", Read(g, "coastal course book").Title);
        // A re-enrolment does not hand over a second copy.
        Assert.Equal(2, ClubGame.Log(g).Entries.Count(e => e.Document is not null && e.Document.StartsWith("book", StringComparison.Ordinal)));
        ClubGame.WaitTo(g, 300);
        Assert.Equal(7, Read(g, "radio course book").Pages.Count);
        Assert.Equal(8, Read(g, "coastal skipper course book").Pages.Count);
    }

    [Fact]
    public void An_unknown_document_says_what_can_be_read_and_the_season_page_is_read_the_same_way()
    {
        var g = ClubGame.Afloat(100);
        Ok(g.Execute(new Enrol("radio")));
        var none = Read(g, "chart of the moon");
        Assert.Contains("You can read: the radio course book.", none.Note);
        Assert.Contains("survey", Read(g, "survey").Note);
        Assert.Contains("sale", Read(g, "sale").Note);
    }

    // ------------------------------------------------------------------ the exam opens

    [Fact]
    public void The_exam_starts_on_the_last_day_after_the_session_and_not_on_the_days_before()
    {
        var g = ClubGame.Afloat(100);
        g.GrantTicketForTests("radio");
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 245);
        for (int i = 0; i < 4; i++)
        {
            var day = Ok(g.Execute(new Look()));
            Assert.False(Exam(g).Open);
            Assert.DoesNotContain(day.Narrative, l => l.StartsWith("Question 1", StringComparison.Ordinal));
            Ok(g.Execute(new Sleep()));
        }
        var last = Ok(g.Execute(new Look()));
        Assert.Equal("16:00", Status(g).TimeOfDay);
        Assert.Contains(last.Narrative, l => l.StartsWith("The examiner sits you down with the paper: 5 questions, one at a time", StringComparison.Ordinal));
        Assert.Contains(last.Narrative, l => l.StartsWith("Question 1 of 5: ", StringComparison.Ordinal));
        var q = Exam(g);
        Assert.True(q.Open);
        Assert.Equal((1, 5), (q.Number, q.Total));
        Assert.Equal(["a", "b", "c", "d"], q.Options.Select(o => o.Letter));
        Assert.StartsWith("Question 1 of 5: ", q.Line);
        Assert.False(Ticket(g, "Coastal skipper").Held);
    }

    [Fact]
    public void Five_distinct_questions_the_same_for_the_same_world_and_day_and_another_set_on_another_day()
    {
        var a = RadioExam();
        var b = RadioExam();
        var first = a.ExamQuestionsForTests();
        Assert.Equal(5, first.Count);
        Assert.Equal(5, first.Distinct().Count());
        Assert.Equal(first, b.ExamQuestionsForTests());                   // same seed, same ticket, same day: the same five
        Assert.All(first, text => Assert.Contains(Rules().QuestionsOf("radio"), q => q.Text == text));

        // The August date is another day: another set.
        var c = ClubGame.Afloat(200);
        Ok(c.Execute(new Enrol("radio")));
        ClubGame.ToMorning(c, 229);
        Ok(c.Execute(new Look()));
        Assert.NotEqual(first, c.ExamQuestionsForTests());
        Assert.Equal(5, c.ExamQuestionsForTests().Distinct().Count());
    }

    [Fact]
    public void The_options_of_a_question_are_the_banks_in_order_and_each_question_of_an_exam_is_asked_once()
    {
        var g = RadioExam();
        var asked = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var q = Exam(g);
            Assert.Equal((i + 1, 5), (q.Number, q.Total));
            asked.Add(q.Text);
            Assert.Equal(g.ExamQuestionsForTests()[i], q.Text);
            Ok(g.Execute(new Answer("a")));
        }
        Assert.Equal(5, asked.Distinct().Count());
        Assert.False(Exam(g).Open);
        Assert.Equal("There is no exam to sit.", Exam(g).Line);
    }

    // ------------------------------------------------------------------ answering

    [Fact]
    public void An_answer_takes_five_minutes_says_only_that_the_examiner_writes_something_down_and_asks_the_next()
    {
        var g = RadioExam();
        var before = Status(g).TimeOfDay;
        var first = Sit(g, true)[0];
        Assert.Equal(("answer", 5, 0), (first.Action, first.MinutesTaken, first.CostNok));
        Assert.Equal("The examiner writes something down.", first.Narrative[0]);
        Assert.Contains(first.Narrative, l => l.StartsWith("Question 2 of 5: ", StringComparison.Ordinal));
        Assert.Equal(2, first.Narrative.Count(l => !l.StartsWith("  ", StringComparison.Ordinal)));     // nothing about right or wrong
        Assert.NotEqual(before, Status(g).TimeOfDay);
        // A wrong answer is told no differently.
        var second = Sit(g, false)[0];
        Assert.Equal("The examiner writes something down.", second.Narrative[0]);
        Assert.DoesNotContain(second.Narrative, l => l.Contains("wrong", StringComparison.OrdinalIgnoreCase) || l.Contains("correct", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_letter_that_is_not_a_to_d_is_refused_free_and_the_question_stays_open()
    {
        var g = RadioExam();
        foreach (var bad in new[] { "e", "", "ab", "yes" })
            Assert.Equal("Answer a, b, c or d.", Text(No(g.Execute(new Answer(bad)))));
        Assert.Equal(1, Exam(g).Number);
        var ok = Ok(g.Execute(new Answer(" B) ")));               // a letter as people write it
        Assert.Equal(5, ok.MinutesTaken);
        Assert.Equal(2, Exam(g).Number);
    }

    [Fact]
    public void An_answer_with_no_exam_open_is_refused()
    {
        var g = ClubGame.Afloat(100);
        Assert.Equal("There is no exam to sit.", Text(No(g.Execute(new Answer("a")))));
    }

    [Fact]
    public void While_the_exam_is_open_what_needs_hands_or_the_boat_is_refused_and_queries_answer()
    {
        var g = RadioExam();
        string status = Status(g).TimeOfDay;
        const string refusal = "The examiner is waiting for your answer: answer a, b, c or d.";
        foreach (Command c in new Command[] { new Look(), new GoTo("saloon"), new StartEngine(), new Depart("Hestholmen"), new Buy("shackle"), new Enrol("coastal"), new PlanEvent("regatta"), new Order("starter motor"), new Launch() })
            Assert.Equal(refusal, Text(No(g.Execute(c))));
        Assert.Equal(status, Status(g).TimeOfDay);
        // Queries answer: the tickets, the log, the board, the book, the question itself.
        Assert.False(Ticket(g, "Radio ticket").Held);
        Assert.NotEmpty(ClubGame.Log(g).Entries);
        Assert.NotEmpty(ClubGame.Board(g).Header);
        Assert.Equal(7, Read(g, "radio course book").Pages.Count);
        Assert.True(Exam(g).Open);
        Assert.Equal(status, Status(g).TimeOfDay);
    }

    // ------------------------------------------------------------------ passing

    [Fact]
    public void Four_of_five_right_passes_the_exam_signs_the_course_and_holds_the_ticket_when_the_log_is_met()
    {
        var g = RadioExam();
        var last = Sit(g, true, true, true, false, true)[4];
        Assert.Contains("You got 4 of 5.", last.Narrative);
        Assert.Contains("The examiner signs your course card. You hold the radio ticket.", last.Narrative);
        Assert.DoesNotContain(last.Narrative, l => l.StartsWith("Not right:", StringComparison.Ordinal));       // a pass names nothing
        Assert.True(Ticket(g, "Radio ticket").Held);
        Assert.Contains(last.Alerts, a => a.Text.Contains("You hold the radio ticket", StringComparison.Ordinal));
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text == "Sat the exam of the radio course: 4 of 5, passed.");
        Assert.False(Exam(g).Open);
    }

    [Fact]
    public void All_five_right_passes_and_the_coastal_ticket_waits_for_a_log_that_is_short()
    {
        var g = ClubGame.Afloat(100);
        g.GrantTicketForTests("radio");
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 245);
        for (int i = 0; i < 5; i++)
        {
            Ok(g.Execute(new Look()));
            if (i < 4) Ok(g.Execute(new Sleep()));
        }
        var last = Sit(g, true, true, true, true, true)[4];
        Assert.Contains("You got 5 of 5.", last.Narrative);
        Assert.Contains(last.Narrative, l => l.StartsWith("The examiner signs the course; the ticket comes when your log is up to it:", StringComparison.Ordinal));
        Assert.False(Ticket(g, "Coastal skipper").Held);
        Assert.Equal("You have done the coastal skipper course; the ticket waits for your log.", Ticket(g, "Coastal skipper").NextCourse);
    }

    // ------------------------------------------------------------------ failing

    [Fact]
    public void Three_of_five_fails_names_each_wrong_question_and_the_page_that_covers_it_and_never_the_right_letter()
    {
        var g = RadioExam();
        var questions = g.ExamQuestionsForTests();
        var rightTexts = g.ExamRightTextsForTests();
        var bank = Rules().QuestionsOf("radio");
        var book = Rules().BookOf("radio")!;
        var results = Sit(g, true, false, true, false, true);
        var last = results[4];
        var text = Text(last);
        Assert.Contains("You got 3 of 5.", last.Narrative);
        Assert.Equal(2, last.Narrative.Count(l => l.StartsWith("Not right:", StringComparison.Ordinal)));
        foreach (int wrong in new[] { 1, 3 })
        {
            var q = bank.Single(x => x.Text == questions[wrong]);
            var page = book.Pages.Single(p => p.Number == q.BookPage);
            Assert.Contains($"Not right: \"{q.Text}\" The radio course book covers it on page {q.BookPage}, {page.Title}.", last.Narrative);
        }
        foreach (int right in new[] { 0, 2, 4 }) Assert.DoesNotContain(questions[right], text);
        // Never the right letter or the right option, in the failing result nor in any answer's result.
        foreach (int wrong in new[] { 1, 3 })
        {
            var q = bank.Single(x => x.Text == questions[wrong]);
            Assert.DoesNotContain(rightTexts[wrong], text);
            Assert.DoesNotContain($"{q.Answer}) {rightTexts[wrong]}", text);
        }
        Assert.Contains("You need 4. The course is done and the examiner will see you again for the exam alone.", last.Narrative);
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text == "Sat the exam of the radio course: 3 of 5, not passed.");
        Assert.DoesNotContain(last.Narrative, l => l.Contains("signs your course card", StringComparison.Ordinal));
        Assert.False(Ticket(g, "Radio ticket").Held);
        Assert.False(Exam(g).Open);
    }

    [Fact]
    public void A_failed_exam_leaves_the_course_done_and_the_tickets_and_the_board_say_the_exam_is_to_re_sit_with_the_next_date_and_the_fee()
    {
        var g = RadioExam();
        var last = Sit(g, false, false, false, false, false)[4];
        Assert.Contains(last.Narrative, l => l == "Next date: Saturday, day 229, at the club in Skjærvik, 500 NOK. Ring the club to put your name down (enrol radio course).");

        Assert.Equal("Course done, exam to re-sit: next date Saturday, day 229, at the club in Skjærvik, 500 NOK.", Ticket(g, "Radio ticket").NextCourse);
        var card = ClubGame.Board(g).Cards.OfType<CourseCard>().First(c => c.Title == "The radio course");
        Assert.Equal((500, "open", 1), (card.PriceNok, card.Status, card.Days));
        Assert.Equal("The radio course: exam to re-sit, Saturday, day 229, at the club in Skjærvik: 500 NOK.", card.Line);
        // Nothing has been signed and nothing is lost: the book is still readable.
        Assert.Equal(7, Read(g, "radio course book").Pages.Count);
    }

    [Fact]
    public void No_exam_outcome_is_seeded_only_the_questions_are_the_same_answers_decide()
    {
        // The same right answers pass on both of the radio course's dates, whichever five questions the day picks.
        var may = RadioExam();
        Assert.Contains("You got 5 of 5.", Sit(may, true, true, true, true, true)[4].Narrative);
        var august = ClubGame.Afloat(200);
        Ok(august.Execute(new Enrol("radio")));
        ClubGame.ToMorning(august, 229);
        Ok(august.Execute(new Look()));
        Assert.Contains("You got 5 of 5.", Sit(august, true, true, true, true, true)[4].Narrative);
        // The same wrong answers fail on both.
        var again = RadioExam();
        Assert.Contains("You got 0 of 5.", Sit(again, false, false, false, false, false)[4].Narrative);
    }

    // ------------------------------------------------------------------ the re-sit

    [Fact]
    public void A_re_sit_is_booked_by_enrolling_again_for_the_exam_fee_only_and_the_player_attends_the_last_day_which_is_the_exam()
    {
        var g = RadioExam();
        Sit(g, true, true, false, false, false);
        int balance = Status(g).BalanceNok;
        var enrol = Ok(g.Execute(new Enrol("radio")));
        Assert.Equal(500, enrol.CostNok);
        Assert.Equal(balance - 500, Status(g).BalanceNok);
        Assert.Contains(enrol.Narrative, l => l == "You ring the club secretary and put your name down to re-sit the exam of the radio course, Saturday, day 229, at the club in Skjærvik. 500 NOK goes on the club's account.");
        Assert.DoesNotContain(enrol.Narrative, l => l.Contains("hands you", StringComparison.Ordinal));      // the book is already yours
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text == "Enrolled to re-sit the exam of the radio course: Saturday, day 229, at the club in Skjærvik, 500 NOK.");
        Assert.Equal("You are enrolled to re-sit the exam of the radio course: Saturday, day 229, at the club in Skjærvik.", Ticket(g, "Radio ticket").NextCourse);
        Assert.Equal("You're enrolled already on the radio course: Saturday, day 229.", Text(No(g.Execute(new Enrol("radio")))));

        // A wait stops on the morning of the exam; the first command of the day is the day at the club, and the exam opens straight after.
        var wait = Ok(g.Execute(new WaitWeek()));
        ClubGame.WaitTo(g, 229);
        var day = Ok(g.Execute(new Look()));
        Assert.Contains("Saturday, day 229: the exam of the radio course.", day.Narrative);
        Assert.DoesNotContain(day.Narrative, l => l.Contains("day 1 of", StringComparison.Ordinal));      // no course day: only the exam
        Assert.Contains(day.Narrative, l => l.StartsWith("Question 1 of 5: ", StringComparison.Ordinal));
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text == "At the club for the exam of the radio course.");
        var last = Sit(g, true, true, true, true, true)[4];
        Assert.Contains("The examiner signs your course card. You hold the radio ticket.", last.Narrative);
        Assert.True(Ticket(g, "Radio ticket").Held);
        Assert.NotNull(wait);
    }

    [Fact]
    public void A_re_sit_missed_forfeits_the_fee_and_the_exam_is_still_to_re_sit()
    {
        var g = RadioExam();
        Sit(g, false, false, false, false, false);
        Ok(g.Execute(new Enrol("radio")));
        // Away at another port on the exam day: the place and the fee are gone.
        ClubGame.ToMorning(g, 228);
        Ok(Walk.Start(g));
        Ok(g.Execute(new Depart("Hestholmen")));
        var arrival = ClubGame.MotorToArrival(g);
        Assert.NotNull(arrival);
        ClubGame.WaitTo(g, 230);
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Alert && e.Text == "You were not at the club for the exam of the radio course. The place and the fee are gone.");
        Assert.StartsWith("Course done, exam to re-sit: next date", Ticket(g, "Radio ticket").NextCourse);
    }

    [Fact]
    public void A_coastal_re_sit_costs_its_own_fee_and_the_exam_day_is_a_weekday_that_costs_a_vacation_day()
    {
        var g = ClubGame.Afloat(100);
        g.GrantTicketForTests("radio");
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 245);
        for (int i = 0; i < 5; i++)
        {
            Ok(g.Execute(new Look()));
            if (i < 4) Ok(g.Execute(new Sleep()));
        }
        Sit(g, false, false, false, false, false);
        var next = Ticket(g, "Coastal skipper").NextCourse;
        Assert.StartsWith("Course done, exam to re-sit: next date Friday, day ", next);
        Assert.EndsWith("at the club in Skjærvik, 1500 NOK.", next);
        var enrol = Ok(g.Execute(new Enrol("coastal")));
        Assert.Equal(1500, enrol.CostNok);
        Assert.Contains(enrol.Narrative, l => l == "It will cost 1 vacation day by the usual rule.");
        var card = Assert.Single(ClubGame.Board(g).Cards.OfType<CourseCard>(), c => c.Title == "The coastal skipper course");
        Assert.Equal((1500, "enrolled", 1), (card.PriceNok, card.Status, card.Days));
    }

    // ------------------------------------------------------------------ leaving the paper

    [Fact]
    public void Leaving_the_exam_unfinished_with_a_wait_marks_what_is_not_answered_as_wrong()
    {
        var g = RadioExam();
        var questions = g.ExamQuestionsForTests();
        Sit(g, true, true);
        var wait = Ok(g.Execute(new WaitDays(1)));
        Assert.Contains("You got up before the paper was finished. The examiner marks the questions you did not answer as wrong.", wait.Narrative);
        Assert.Contains("You got 2 of 5.", wait.Narrative);
        Assert.Equal(3, wait.Narrative.Count(l => l.StartsWith("Not right:", StringComparison.Ordinal)));
        foreach (int i in new[] { 2, 3, 4 }) Assert.Contains(wait.Narrative, l => l.StartsWith($"Not right: \"{questions[i]}\"", StringComparison.Ordinal));
        Assert.False(Exam(g).Open);
        Assert.False(Ticket(g, "Radio ticket").Held);
        Assert.StartsWith("Course done, exam to re-sit", Ticket(g, "Radio ticket").NextCourse);
    }

    [Fact]
    public void Sleeping_leaves_the_paper_too()
    {
        var g = RadioExam();
        var sleep = Ok(g.Execute(new Sleep()));
        Assert.Contains("You got 0 of 5.", sleep.Narrative);
        Assert.False(Exam(g).Open);
    }

    [Fact]
    public void A_wait_that_passes_the_exam_day_without_the_player_at_the_table_hands_in_a_blank_paper()
    {
        var g = ClubGame.Afloat(100);
        Ok(g.Execute(new Enrol("radio")));
        ClubGame.ToMorning(g, 138);
        var wait = Ok(g.Execute(new WaitDays(1)));          // the Saturday goes by at the club, with no command
        Assert.Contains("The paper was not handed in: no questions answered.", wait.Narrative);
        Assert.Contains("You got 0 of 5.", wait.Narrative);
        Assert.Equal(5, wait.Narrative.Count(l => l.StartsWith("Not right:", StringComparison.Ordinal)));
        Assert.DoesNotContain(wait.Narrative, l => l.Contains("You got up before", StringComparison.Ordinal));
        Assert.False(Exam(g).Open);
        Assert.False(Ticket(g, "Radio ticket").Held);
    }

    [Fact]
    public void A_wait_stops_on_the_morning_of_the_exam_day_of_a_multi_day_course()
    {
        var g = ClubGame.Afloat(100);
        g.GrantTicketForTests("radio");
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 247);
        var wait = Ok(g.Execute(new WaitDays(5)));          // Wednesday: would pass Thursday and Friday
        Assert.Equal(249, Status(g).Day);
        Assert.Contains("You come back on day 249: the last day of the coastal skipper course, and the examiner is at the club today.", wait.Narrative);
    }

    // ------------------------------------------------------------------ no leaks

    [Fact]
    public void The_exam_views_carry_no_right_letter_no_id_and_serialise_with_their_wire_names()
    {
        var g = RadioExam();
        var json = JsonSerializer.Serialize(g.Ask(new ExamQuestion()), typeof(QueryResult));
        Assert.Contains("\"type\":\"exam-question-view\"", json);
        Assert.DoesNotContain("answer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("radio-", json, StringComparison.OrdinalIgnoreCase);     // the question ids
        Assert.Contains("\"type\":\"document-view\"", JsonSerializer.Serialize(g.Ask(new ReadDocument("radio course book")), typeof(QueryResult)));
        Assert.Equal("{\"type\":\"exam-question\"}", JsonSerializer.Serialize<Query>(new ExamQuestion()));
        Assert.Equal("{\"type\":\"read-document\",\"Name\":\"radio course book\"}", JsonSerializer.Serialize<Query>(new ReadDocument("radio course book")));
        Assert.Equal("{\"type\":\"answer\",\"Letter\":\"b\"}", JsonSerializer.Serialize<Command>(new Answer("b")));
        Assert.IsType<Answer>(JsonSerializer.Deserialize<Command>("{\"type\":\"answer\",\"Letter\":\"c\"}"));
        // The right option text is among the four options and nowhere marked.
        var q = Exam(g);
        Assert.Contains(g.ExamRightTextsForTests()[0], q.Options.Select(o => o.Text));
    }

    [Fact]
    public void Nothing_a_player_can_ask_during_a_failed_exam_carries_the_right_option_of_a_missed_question()
    {
        var g = RadioExam();
        var rightTexts = g.ExamRightTextsForTests();
        var questions = g.ExamQuestionsForTests();
        var everything = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            // Everything an adapter could show between the answers: the status, the notices, the log, the tickets, the board.
            everything.Add(JsonSerializer.Serialize(g.Ask(new Status()), typeof(QueryResult)));
            everything.Add(JsonSerializer.Serialize(g.Ask(new Logbook()), typeof(QueryResult)));
            everything.Add(JsonSerializer.Serialize(g.Ask(new Tickets()), typeof(QueryResult)));
            everything.Add(JsonSerializer.Serialize(g.Ask(new Noticeboard()), typeof(QueryResult)));
            var answered = Ok(g.Execute(new Answer(ClubGame.WrongLetter(g.ExamRightLetterForTests()))));
            // The next question's options are shown by design: leave those lines out and everything else the answer said is swept.
            everything.Add(string.Join("\n", answered.Narrative.Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"^  [abcd]\) "))));
        }
        everything.Add(JsonSerializer.Serialize(g.Ask(new Logbook()), typeof(QueryResult)));
        everything.Add(JsonSerializer.Serialize(g.Ask(new Tickets()), typeof(QueryResult)));
        everything.Add(JsonSerializer.Serialize(g.Ask(new Noticeboard()), typeof(QueryResult)));
        string all = string.Join("\n", everything);
        for (int i = 0; i < 5; i++)
        {
            // The right option's text is shown only as one of the four options of its question; no result, mark, log line, ticket or card carries it.
            Assert.DoesNotContain(rightTexts[i], all, StringComparison.Ordinal);
        }
    }
}
