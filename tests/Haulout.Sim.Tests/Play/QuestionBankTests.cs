using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;

namespace Haulout.Sim.Tests.Play;

/// <summary>The course books and the question banks (design note 012, decision 8, step 2b): the data is complete, checkable and marked UNVERIFIED.</summary>
public class QuestionBankTests
{
    private static string QuestionsJson()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.questions.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string TicketsJson()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.tickets.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static TicketRules Rules() => BoatLoader.LoadSloop28(withHours: true).Tickets;

    private static readonly string[] CourseTickets = ["radio", "coastal"];

    [Fact]
    public void The_bank_loads_with_a_book_and_at_least_twenty_questions_for_every_ticket_whose_course_has_an_exam()
    {
        var rules = Rules();
        foreach (var id in CourseTickets)
        {
            Assert.NotNull(rules.Find(id)!.Course!.Exam);
            Assert.NotNull(rules.BookOf(id));
            Assert.True(rules.QuestionsOf(id).Count >= 20, $"{id} has {rules.QuestionsOf(id).Count} questions");
        }
        Assert.Equal((5, 4, 5), (rules.Find("radio")!.Course!.Exam!.Questions, rules.Find("radio")!.Course!.Exam!.PassAt, rules.Find("radio")!.Course!.Exam!.AnswerMinutes));
        Assert.Equal((500, 1500), (rules.Find("radio")!.Course!.ExamFeeNok, rules.Find("coastal")!.Course!.ExamFeeNok));
        Assert.Null(rules.Find("offshore")!.Course!.Exam);   // a course the club does not run has no exam
    }

    [Fact]
    public void Every_question_has_four_options_exactly_one_right_a_source_and_a_book_page_that_exists_in_its_course_book()
    {
        var rules = Rules();
        foreach (var q in rules.Bank.Questions)
        {
            var book = rules.BookOf(q.Ticket)!;
            Assert.Equal(4, q.Options.Count);
            Assert.Equal(4, q.Options.Distinct().Count());
            Assert.Contains(q.Answer, new[] { "a", "b", "c", "d" });           // one letter: exactly one option is right
            Assert.False(string.IsNullOrWhiteSpace(q.Source), q.Id);
            Assert.False(string.IsNullOrWhiteSpace(q.Checked), q.Id);
            Assert.Contains(book.Pages, p => p.Number == q.BookPage);
            Assert.False(string.IsNullOrWhiteSpace(book.Pages.Single(p => p.Number == q.BookPage).Text));
            Assert.True(q.Text.EndsWith('?') || q.Text.EndsWith(':'), q.Id);
            // The question never gives the answer away.
            Assert.DoesNotContain(q.Options["abcd".IndexOf(q.Answer[0])], q.Text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(rules.Bank.Questions.Count, rules.Bank.Questions.Select(q => q.Id).Distinct().Count());
    }

    [Fact]
    public void The_right_letters_are_spread_over_the_options()
    {
        var rules = Rules();
        foreach (var id in CourseTickets)
            Assert.True(rules.QuestionsOf(id).Select(q => q.Answer).Distinct().Count() >= 3, id);
    }

    [Fact]
    public void The_bank_says_in_its_placeholders_that_every_question_is_unverified_until_amit_has_read_it()
    {
        var rules = Rules();
        Assert.Contains(rules.Bank.Placeholders, p => p.Contains("UNVERIFIED", StringComparison.Ordinal) && p.Contains("Amit", StringComparison.Ordinal));
    }

    [Fact]
    public void Real_organisation_names_are_in_the_source_notes_only_never_in_the_game_text()
    {
        var rules = Rules();
        var banned = new[] { "RYA", "Ofcom", "ITU", "IMO", "Met Office", "NATO", "ICAO", "Coastguard", "Coast Guard", "MCA" };
        var text = rules.Bank.Books.SelectMany(b => new[] { b.Title, b.HandedLine }.Concat(b.Pages.SelectMany(p => new[] { p.Title, p.Text })))
            .Concat(rules.Bank.Questions.SelectMany(q => q.Options.Append(q.Text))).ToList();
        foreach (var name in banned)
            Assert.DoesNotContain(text, t => System.Text.RegularExpressions.Regex.IsMatch(t, $@"\b{name}\b"));
    }

    [Fact]
    public void The_game_text_uses_the_games_own_names()
    {
        var rules = Rules();
        Assert.Contains("Skjærvik harbour, Skjærvik harbour, Skjærvik harbour, this is sailing yacht", rules.BookOf("radio")!.Pages[1].Text);
        Assert.Contains("ferry", rules.BookOf("coastal")!.Pages[2].Text);
    }

    [Fact]
    public void A_book_has_numbered_pages_one_after_another_and_a_line_the_club_says_on_handing_it_over()
    {
        foreach (var book in Rules().Bank.Books)
        {
            Assert.Equal(Enumerable.Range(1, book.Pages.Count), book.Pages.Select(p => p.Number));
            Assert.True(book.Pages.Count >= 6);
            Assert.StartsWith("The club secretary hands you the", book.HandedLine);
        }
    }

    private static string WithBank(Action<JsonObject> edit)
    {
        var node = JsonNode.Parse(QuestionsJson())!.AsObject();
        edit(node);
        return node.ToJsonString();
    }

    private static void Load(string questions) => TicketLoader.LoadTickets(TicketsJson(), BoatLoader.LoadEconomy(), questions);

    [Fact]
    public void A_question_with_a_page_the_book_lacks_three_options_or_a_bad_letter_is_refused_at_load()
    {
        Load(QuestionsJson());   // the shipped file loads
        var page = Assert.Throws<InvalidOperationException>(() => Load(WithBank(n => n["questions"]![0]!["bookPage"] = 99)));
        Assert.Contains("page 99", page.Message);
        var opts = Assert.Throws<InvalidOperationException>(() => Load(WithBank(n => n["questions"]![0]!["options"]!.AsArray().RemoveAt(3))));
        Assert.Contains("not four", opts.Message);
        var letter = Assert.Throws<InvalidOperationException>(() => Load(WithBank(n => n["questions"]![0]!["answer"] = "e")));
        Assert.Contains("answer 'e'", letter.Message);
        var source = Assert.Throws<InvalidOperationException>(() => Load(WithBank(n => n["questions"]![0]!["source"] = "")));
        Assert.Contains("no source", source.Message);
    }

    [Fact]
    public void A_bank_with_too_few_questions_no_book_or_no_unverified_mark_is_refused_at_load()
    {
        var few = Assert.Throws<InvalidOperationException>(() => Load(WithBank(n =>
        {
            var q = n["questions"]!.AsArray();
            for (int i = q.Count - 1; i >= 0; i--) if (q[i]!["ticket"]!.GetValue<string>() == "radio" && i > 2) q.RemoveAt(i);
        })));
        Assert.Contains("the exam asks 5", few.Message);
        var book = Assert.Throws<InvalidOperationException>(() => Load(WithBank(n => n["books"]!.AsArray().RemoveAt(0))));
        Assert.Contains("no course book", book.Message);
        var mark = Assert.Throws<InvalidOperationException>(() => Load(WithBank(n => n["placeholders"] = new JsonArray("fine"))));
        Assert.Contains("UNVERIFIED", mark.Message);
    }

    [Fact]
    public void An_exam_fee_missing_from_economy_json_is_refused_at_load()
    {
        var economy = TestEconomy.Edited(n => n["examFees"]!.AsObject().Remove("coastal"));
        var ex = Assert.Throws<InvalidOperationException>(() => TicketLoader.LoadTickets(TicketsJson(), BoatLoader.ParseEconomy(economy), QuestionsJson()));
        Assert.Contains("examFees", ex.Message);
    }
}
