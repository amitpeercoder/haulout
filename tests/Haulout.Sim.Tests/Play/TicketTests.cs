using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>Tickets, the club's courses and Enrol (design note 012, decision 3, step 2): the data, the checklists, the enrolment, the course days and the last day.</summary>
public class TicketTests
{
    private static CommandResult Ok(CommandResult r) => ClubGame.Ok(r);
    private static string Text(CommandResult r) => ClubGame.Text(r);
    private static StatusView Status(Game g) => ClubGame.Status(g);
    private static TicketsView Tickets(Game g) => ClubGame.Tickets(g);
    private static TicketView Ticket(Game g, string name) => Tickets(g).Tickets.Single(t => t.Name == name);

    private static CommandResult No(CommandResult r)
    {
        Assert.False(r.Accepted, Text(r));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    /// <summary>A log that meets every ask of the coastal ticket but the radio ticket (which a scenario grants or earns).</summary>
    private static SkipperLog CoastalLog(double miles = 310) => new()
    {
        MilesUnderWay = miles, DaysAboard = 16, NightHoursUnderWay = 9,
        Passages = [new LoggedPassage(22, 5, 0, false, PassageRole.Skipper), new LoggedPassage(23, 5, 0, false, PassageRole.Skipper)],
    };

    // ------------------------------------------------------------------ the data

    [Fact]
    public void Tickets_json_has_four_tickets_with_the_numbers_of_the_note_and_marks_its_placeholders_unverified_with_their_sources()
    {
        var boat = BoatLoader.LoadSloop28(withHours: true);
        var rules = boat.Tickets;
        Assert.True(rules.Enabled);
        Assert.Equal(["Boat licence", "Radio ticket", "Coastal skipper", "Offshore skipper"], rules.Tickets.Select(t => t.Name).ToArray());

        Assert.True(rules.Find("boat-licence")!.HeldFromStart);
        Assert.Null(rules.Find("boat-licence")!.Course);

        var radio = rules.Find("radio")!;
        Assert.Empty(radio.Asks);
        Assert.Equal((1800, 1, 2), (radio.Course!.PriceNok, radio.Course.Days, radio.Course.Offers.Count));
        Assert.All(radio.Course.Offers, o => Assert.Equal("saturday", o.Weekday));
        Assert.InRange(radio.Course.Offers[0].FromDayOfYear, 120, 150);   // May, before Midsummer
        Assert.InRange(radio.Course.Offers[1].FromDayOfYear, 210, 239);   // August

        var coastal = rules.Find("coastal")!;
        Assert.Equal(["miles", "daysAboard", "nightHours", "passagesOver", "ticket"], coastal.Asks.Select(a => a.Kind).ToArray());
        Assert.Equal([300, 15, 8, 2], coastal.Asks.Take(4).Select(a => a.Need).ToArray());
        Assert.Equal((20, true, "radio"), (coastal.Asks[3].Nm, coastal.Asks[3].AsSkipper, coastal.Asks[4].Ticket));
        Assert.Equal((9500, 5, true, 1), (coastal.Course!.PriceNok, coastal.Course.Days, coastal.Course.Weekdays, coastal.Course.Offers.Count));
        Assert.InRange(coastal.Course.Offers[0].FromDayOfYear, 240, 269);   // September
        Assert.Equal("monday", coastal.Course.Offers[0].Weekday);
        Assert.Equal(5, coastal.Course.DayLines.Count);

        var offshore = rules.Find("offshore")!;
        Assert.Equal(["miles", "daysAboard", "passagesOver", "passagesOverNight", "seasonsNoTow", "firstAid"], offshore.Asks.Select(a => a.Kind).ToArray());
        Assert.Equal([2500, 50, 5, 2, 1], offshore.Asks.Take(5).Select(a => a.Need).ToArray());
        Assert.Equal((15000, 5, 0), (offshore.Course!.PriceNok, offshore.Course.Days, offshore.Course.Offers.Count));
        Assert.Equal([20, 60], rules.Club.PassageNm.ToArray());

        // The file says what it is: placeholders, UNVERIFIED, and where the numbers came from.
        var raw = JsonNode.Parse(ReadResource("Haulout.Sim.Data.tickets.json"))!;
        var placeholders = string.Join(" ", raw["placeholders"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Contains("PLACEHOLDER", placeholders);
        Assert.Contains("UNVERIFIED", placeholders);
        foreach (var source in new[] { "rya.org.uk", "firstclasssailing.com", "uksa.org", "carsingtonsailingclub.co.uk", "yotspot.com", "Yachtmaster Offshore", "Coastal Skipper", "SRC" })
            Assert.Contains(source, placeholders);
        // The prices are the club's, in economy.json, and reach the rules.
        Assert.Equal((1800, 9500, 15000), (BoatLoader.LoadEconomy().Courses["radio"], BoatLoader.LoadEconomy().Courses["coastal"], BoatLoader.LoadEconomy().Courses["offshore"]));
        // No RYA names in what the player reads.
        Assert.DoesNotContain("RYA", JsonSerializer.Serialize(rules));
        Assert.DoesNotContain("Yachtmaster", JsonSerializer.Serialize(rules));
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void A_boat_written_in_a_test_has_no_tickets_and_no_club()
    {
        var g = new Game(BoatLoader.LoadSloop28(), 75, new ClubGame.FixedRandom(0.99), Game.DefaultSeed, ClubGame.Calm());
        Assert.False(g.Execute(new Enrol("radio")).Accepted);
        Assert.Empty(Tickets(g).Tickets);
        Assert.Empty(ClubGame.Board(g).Cards);
        Assert.Equal(6, ((BriefingView)g.Ask(new Briefing())).Lines.Count);   // the briefing as it was
    }

    [Fact]
    public void A_course_price_that_economy_json_lacks_is_refused_at_load()
    {
        var economy = TestEconomy.Edited(n => n["courses"]!.AsObject().Remove("coastal"));
        var ex = Assert.Throws<InvalidOperationException>(() => BoatLoader.Load(ClubGame.Sloop28Json(), true, economy, withHours: true));
        Assert.Contains("coastal", ex.Message);
        Assert.Contains("economy.json", ex.Message);
    }

    // ------------------------------------------------------------------ the checklist in words

    [Fact]
    public void The_tickets_query_is_a_checklist_in_words_with_the_next_course_and_no_ids()
    {
        var g = ClubGame.New();
        var licence = Ticket(g, "Boat licence");
        Assert.True(licence.Held);
        Assert.Null(licence.HeldSinceDay);
        Assert.Equal("You passed it in the winter, the night before you saw her.", licence.Words);

        var radio = Ticket(g, "Radio ticket");
        Assert.False(radio.Held);
        Assert.Equal("Needed to use a VHF radio.", radio.Purpose);
        Assert.Empty(radio.Asks);
        Assert.Equal("Next course: Saturday, day 138, at the club in Skjærvik, 1800 NOK.", radio.NextCourse);

        var coastal = Ticket(g, "Coastal skipper");
        Assert.Equal("The insurer of anything over 10 m asks for it.", coastal.Purpose);
        Assert.Equal(
            ["300 miles: you have 0.", "15 days aboard: you have 0.", "8 hours under way after dark: you have 0.", "2 passages over 20 miles as skipper: you have 0.", "The radio ticket: not yet."],
            coastal.Asks.Select(a => a.Text).ToArray());
        Assert.All(coastal.Asks, a => Assert.False(a.Met));
        Assert.Equal("Next course: Monday to Friday, days 245 to 249, at the club in Skjærvik, 9500 NOK.", coastal.NextCourse);

        var offshore = Ticket(g, "Offshore skipper");
        Assert.Equal(
            ["2500 miles: you have 0.", "50 days aboard: you have 0.", "5 passages over 60 miles as skipper: you have 0.", "2 of them through a night: you have 0.", "1 season without a tow: you have 0.", "A first-aid day: not yet."],
            offshore.Asks.Select(a => a.Text).ToArray());
        Assert.Equal("No course is offered yet: the club does not run one.", offshore.NextCourse);
    }

    [Fact]
    public void The_checklist_follows_the_log_and_a_line_is_met_only_when_the_whole_figure_is_there()
    {
        var g = ClubGame.Afloat(100);
        ClubGame.Sail(g, "Hestholmen");
        var coastal = Ticket(g, "Coastal skipper");
        Assert.Equal("300 miles: you have 14.", coastal.Asks[0].Text);
        Assert.False(coastal.Asks[0].Met);

        g.SetSkipperLogForTests(CoastalLog(299.9));
        Assert.Equal("300 miles: you have 299.", Ticket(g, "Coastal skipper").Asks[0].Text);
        Assert.False(Ticket(g, "Coastal skipper").Asks[0].Met);
        g.SetSkipperLogForTests(CoastalLog(300));
        var met = Ticket(g, "Coastal skipper").Asks;
        Assert.Equal(["300 miles: you have 300.", "15 days aboard: you have 16.", "8 hours under way after dark: you have 9.", "2 passages over 20 miles as skipper: you have 2.", "The radio ticket: not yet."],
            met.Select(a => a.Text).ToArray());
        Assert.Equal([true, true, true, true, false], met.Select(a => a.Met).ToArray());
        g.GrantTicketForTests("radio");
        Assert.Equal("The radio ticket: held.", Ticket(g, "Coastal skipper").Asks[4].Text);
        Assert.True(Ticket(g, "Radio ticket").Held);
    }

    [Fact]
    public void The_new_views_carry_no_ids_and_no_scores_anywhere()
    {
        var g = ClubGame.Afloat(100);
        Ok(g.Execute(new Enrol("radio")));
        string[] json =
        [
            JsonSerializer.Serialize<QueryResult>(Tickets(g)),
            JsonSerializer.Serialize<QueryResult>(ClubGame.Board(g)),
            JsonSerializer.Serialize<QueryResult>(ClubGame.Skipper(g)),
            JsonSerializer.Serialize<QueryResult>(ClubGame.Cal(g)),
        ];
        var data = BoatLoader.LoadSloop28(withHours: true);
        var ids = data.Tickets.Tickets.SelectMany(t => new[] { t.Id }.Concat(t.Course?.Offers.Select(o => o.Id) ?? []))
            .Concat(data.Calendar.Events.Select(e => e.Id)).Concat(data.Trips!.Ports.Select(p => p.Id))
            .Concat(data.Components.Select(c => c.Id)).Distinct().ToList();
        foreach (var j in json)
        {
            foreach (var id in ids) Assert.DoesNotContain($"\"{id}\"", j);
            Assert.DoesNotContain("\"Id\"", j);
            Assert.DoesNotContain("score", j, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------ enrolling

    [Fact]
    public void Enrol_pays_once_at_enrolment_logs_it_takes_five_minutes_and_a_wait_stops_on_the_first_morning()
    {
        var g = ClubGame.Afloat(100);
        int balance = Status(g).BalanceNok, spending = ClubGame.Cal(g).Money.SpendingNok;
        var r = Ok(g.Execute(new Enrol("radio")));
        Assert.Equal((5, 1800), (r.MinutesTaken, r.CostNok));
        Assert.Contains("put your name down for the radio course, Saturday, day 138, at the club in Skjærvik", Text(r));
        Assert.Equal(balance - 1800, Status(g).BalanceNok);
        Assert.Equal(spending + 1800, ClubGame.Cal(g).Money.SpendingNok);   // the ledger's spending, once
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text == "Enrolled on the radio course: Saturday, day 138, at the club in Skjærvik, 1800 NOK." && e.CostNok == 1800);
        Assert.Equal("You are enrolled on the radio course: Saturday, day 138, at the club in Skjærvik.", Ticket(g, "Radio ticket").NextCourse);

        // A long wait stops on the first morning of the course, and says so; a second enrolment is refused and costs nothing.
        No(g.Execute(new Enrol("radio")));
        var wait = Ok(g.Execute(new WaitDays(60)));
        Assert.Equal(138, Status(g).Day);
        Assert.Contains("You come back on day 138: the radio course starts today.", Text(wait));
        Assert.Equal(spending + 1800, ClubGame.Cal(g).Money.SpendingNok); // the fee was paid at enrolment and not again
    }

    [Fact]
    public void A_week_of_waiting_and_the_next_weekend_stop_on_the_first_morning_too()
    {
        var g = ClubGame.Afloat(100);
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 238);   // a Monday, a week before
        var week = Ok(g.Execute(new WaitWeek()));   // to day 245: the stop is the morning itself
        Assert.Equal(245, Status(g).Day);
        Assert.Contains("the coastal skipper course starts today", Text(week));

        var h = ClubGame.Afloat(100);
        Ok(h.Execute(new Enrol("radio")));
        for (int i = 0; i < 12 && Status(h).Day < 138; i++) Ok(h.Execute(new NextWeekend()));
        Assert.Equal(138, Status(h).Day);
        Assert.Equal("08:00", Status(h).TimeOfDay);
    }

    [Fact]
    public void The_enrolled_course_is_on_the_calendars_week_ahead_and_the_board_shows_it_enrolled()
    {
        var g = ClubGame.Afloat(100);
        Ok(g.Execute(new Enrol("radio")));
        ClubGame.ToMorning(g, 120);
        var item = Assert.Single(ClubGame.Cal(g).Ahead, a => a.Kind == "course");
        Assert.Equal((138, "The radio course starts"), (item.Day, item.Text));
        var card = ClubGame.Board(g).Cards.OfType<CourseCard>().First(c => c.Title == "The radio course");
        Assert.True(card.Enrolled);
        Assert.Equal("enrolled", card.Status);
    }

    [Fact]
    public void Enrol_is_refused_at_sea_for_a_ticket_held_for_a_name_the_club_does_not_know_and_for_a_course_not_offered()
    {
        var g = ClubGame.Afloat(100);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart("Hestholmen")));
        Assert.Equal("You're at sea: ring the club from port.", Text(No(g.Execute(new Enrol("radio")))));
        Ok(g.Execute(new TurnBack()));
        for (int i = 0; i < 30 && Status(g).AtSea; i++) Ok(g.Execute(new Continue()));
        Assert.False(Status(g).AtSea);

        Assert.Equal("The club runs no course called 'licence'.", Text(No(g.Execute(new Enrol("licence")))));
        Assert.Equal("The club runs no course called 'kayak'.", Text(No(g.Execute(new Enrol("kayak")))));
        Assert.Contains("could be: Coastal skipper, Offshore skipper", Text(No(g.Execute(new Enrol("skipper")))));
        Assert.Equal("No course is offered yet: the club does not run one.", Text(No(g.Execute(new Enrol("offshore")))));
        g.GrantTicketForTests("radio");
        Assert.Equal("You hold the radio ticket. There is nothing to take.", Text(No(g.Execute(new Enrol("radio")))));
        // Names as the player says them: "the coastal course" finds the coastal skipper.
        Ok(g.Execute(new Enrol("the coastal course")));
    }

    [Fact]
    public void Enrol_is_refused_for_a_course_that_has_started_and_takes_the_next_date_when_the_first_is_closed()
    {
        var late = ClubGame.New(246);   // the coastal course began on day 245
        Assert.Equal("The coastal skipper course has started (Monday to Friday, days 245 to 249): the club takes names up to the day before.",
            Text(No(late.Execute(new Enrol("coastal")))));

        var onTheDay = ClubGame.New(138); // the May course starts today: the August one is the next
        var r = Ok(onTheDay.Execute(new Enrol("radio")));
        Assert.Contains("is closed to names; you take the next.", Text(r));
        Assert.Contains("Saturday, day 229", Text(r));

        var dayBefore = ClubGame.New(244); // the day before: just in time
        Ok(dayBefore.Execute(new Enrol("coastal")));
    }

    [Fact]
    public void Enrol_is_refused_for_money_with_the_usual_words_and_in_the_clubs_office_hours()
    {
        var poor = ClubGame.New(75, economy: ClubGame.OpenShops(n =>
        {
            n["calendar"]!["overdraftLimitNok"] = 0;
            n["start"]!["startingBalanceNok"] = 1000;
        }));
        var broke = Text(No(poor.Execute(new Enrol("radio"))));
        Assert.Contains("1800 NOK", broke);
        Assert.Contains("1000 NOK left", broke);

        // The shipped hours: the office opens at nine on a Saturday and is shut on a Sunday.
        var g = Game.NewFirstFault(withHours: true);
        Assert.Equal("The club office opens at nine.", Text(No(g.Execute(new Enrol("radio")))));
        ClubGame.LookUntilAtLeast(g, "09:00");
        Ok(g.Execute(new Enrol("radio")));
        Ok(g.Execute(new WaitDays(1)));
        Assert.Equal(76, Status(g).Day);
        Assert.Equal("The club office is shut on Sundays; it opens at nine tomorrow.", Text(No(g.Execute(new Enrol("coastal")))));
    }

    // ------------------------------------------------------------------ the course days

    private static Game RadioMorning()
    {
        var g = ClubGame.Afloat(100);
        Ok(g.Execute(new Enrol("radio")));
        ClubGame.ToMorning(g, 138);
        Assert.Equal(("Saturday", "08:00"), (new Haulout.Sim.Time.GameClock(138).WeekdayName, Status(g).TimeOfDay));
        return g;
    }

    [Fact]
    public void The_first_command_of_a_course_day_is_the_day_at_the_club_with_its_line_and_the_evening_is_the_players_own()
    {
        var g = RadioMorning();
        int vacation = ClubGame.Cal(g).VacationDaysUsed;

        // Queries answer, and do not take the day.
        Assert.False(Ticket(g, "Radio ticket").Held);
        Assert.Equal("08:00", Status(g).TimeOfDay);
        Ok(g.Execute(new PlanEvent("regatta")));                       // a phone call or a mark on the calendar is not the day either
        Assert.Equal("08:00", Status(g).TimeOfDay);

        // The first command that needs her (departing here) is the day at the club: she is not left, and the command is not carried out.
        Walk.ToPlace(g, "cockpit");
        var day = Ok(g.Execute(new Depart("Hestholmen")));
        Assert.Equal("course", day.Action);
        Assert.Equal(8 * 60, day.MinutesTaken);                        // 08:00 to 16:00
        Assert.Equal("16:00", Status(g).TimeOfDay);
        Assert.False(Status(g).AtSea);
        Assert.Contains("Saturday, day 138: day 1 of 1 of the radio course.", day.Narrative);
        Assert.Contains(day.Narrative, l => l.StartsWith("The club room smells of coffee and wet coats.", StringComparison.Ordinal));
        // The last day ends at the examiner's table (decision 8), not at the door: the exam opens and the ticket waits for the paper.
        Assert.Contains(day.Narrative, l => l.StartsWith("The examiner sits you down with the paper: 5 questions", StringComparison.Ordinal));
        Assert.DoesNotContain(day.Narrative, l => l.Contains("signs your course card", StringComparison.Ordinal));
        Assert.DoesNotContain(day.Narrative, l => l.StartsWith("It's 16:00", StringComparison.Ordinal));
        Assert.False(Ticket(g, "Radio ticket").Held);
        var signed = ClubGame.SitExam(g);
        Assert.Contains("The examiner signs your course card. You hold the radio ticket.", signed.Narrative);
        Assert.Equal(vacation, ClubGame.Cal(g).VacationDaysUsed);       // a Saturday costs no vacation day

        // The evening is the player's own: the next command is carried out, not the club.
        var evening = Ok(g.Execute(new Look()));
        Assert.Equal("look", evening.Action);
        Assert.Equal(2, evening.MinutesTaken);
    }

    [Fact]
    public void The_radio_ticket_is_held_on_the_day_with_an_alert_and_a_logbook_line_and_stays_held()
    {
        var g = RadioMorning();
        Ok(g.Execute(new Look()));
        var day = ClubGame.SitExam(g);
        Assert.Contains(day.Alerts, a => a.Text == "The examiner signs your course card. You hold the radio ticket.");
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Alert && e.Text == "The examiner signs your course card. You hold the radio ticket.");
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text == "At the club for the radio course, day 1 of 1.");
        var radio = Ticket(g, "Radio ticket");
        Assert.True(radio.Held);
        Assert.Equal(138, radio.HeldSinceDay);
        Assert.Equal("You hold the radio ticket.", radio.Words);
        Assert.Equal("", radio.NextCourse);
        // The course disappears from the board, and a second enrolment is refused.
        Assert.DoesNotContain(ClubGame.Board(g).Cards.OfType<CourseCard>(), c => c.Title == "The radio course");
        Assert.Equal("You hold the radio ticket. There is nothing to take.", Text(No(g.Execute(new Enrol("radio")))));
        // Never lost: a tow, a season, a year later.
        ClubGame.WaitTo(g, 400);
        Assert.True(Ticket(g, "Radio ticket").Held);
    }

    private static (Game G, List<CommandResult> Days) CoastalWeek(bool logMet, double miles = 310, int right = 5)
    {
        var g = ClubGame.Afloat(100);
        g.GrantTicketForTests("radio");
        if (logMet) g.SetSkipperLogForTests(CoastalLog(miles));
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 245);
        var days = new List<CommandResult>();
        for (int i = 0; i < 5; i++)
        {
            days.Add(Ok(g.Execute(new Look())));
            if (i < 4) Ok(g.Execute(new Sleep()));      // from 16:00 she sleeps eight hours and wakes at midnight: the next day at the club is the next command
        }
        days[4] = ClubGame.SitExam(g, right);            // the last day ends at the examiner's table: the marking comes with the last answer
        return (g, days);
    }

    [Fact]
    public void Each_of_the_five_days_of_the_coastal_course_gets_its_own_line_from_data_and_the_weekday_rule_charges_each_once()
    {
        var boat = BoatLoader.LoadSloop28(withHours: true);
        var lines = boat.Tickets.Find("coastal")!.Course!.DayLines;
        var g = ClubGame.Afloat(100);
        g.GrantTicketForTests("radio");
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 245);
        int vacation = ClubGame.Cal(g).VacationDaysUsed;
        var days = new List<CommandResult>();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(245 + i, Status(g).Day);
            days.Add(Ok(g.Execute(new Look())));
            if (i < 4) Ok(g.Execute(new Sleep()));
        }
        for (int i = 0; i < 5; i++)
        {
            Assert.Contains($"{new Haulout.Sim.Time.GameClock(245 + i).WeekdayName}, day {245 + i}: day {i + 1} of 5 of the coastal skipper course.", days[i].Narrative);
            Assert.Contains(lines[i], days[i].Narrative);
            Assert.Equal("course", days[i].Action);
            Assert.Contains(days[i].Narrative, l => l.Contains("becomes a vacation day", StringComparison.Ordinal));   // the existing line, once a day
            Assert.Equal(1, days[i].Narrative.Count(l => l.Contains("vacation day", StringComparison.Ordinal)));
        }
        ClubGame.SitExam(g);
        Assert.Equal(vacation + 5, ClubGame.Cal(g).VacationDaysUsed);
        // An evening's work aboard on a day already charged costs nothing more.
        for (int i = 0; i < 100; i++) Ok(g.Execute(new Look()));      // more than the evening's three hours
        Assert.Equal(vacation + 5, ClubGame.Cal(g).VacationDaysUsed);
        Assert.Equal(5, ClubGame.Log(g).Entries.Count(e => e.Text.StartsWith("At the club for the coastal skipper course", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_wait_passing_a_course_day_is_the_day_at_the_club_and_its_line_comes_in_the_wait()
    {
        var g = ClubGame.Afloat(100);
        g.GrantTicketForTests("radio");
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 245);
        int vacation = ClubGame.Cal(g).VacationDaysUsed;
        var wait = Ok(g.Execute(new WaitDays(2)));       // Monday and Tuesday go by at the club
        Assert.Contains(wait.Narrative, l => l.StartsWith("Monday, day 245: day 1 of 5", StringComparison.Ordinal));
        Assert.Contains(wait.Narrative, l => l.StartsWith("Tuesday, day 246: day 2 of 5", StringComparison.Ordinal));
        Assert.DoesNotContain(wait.Narrative, l => l.StartsWith("Wednesday, day 247", StringComparison.Ordinal));   // the wait ends on the Wednesday morning
        Assert.Equal(247, Status(g).Day);
        Assert.Equal(vacation + 2, ClubGame.Cal(g).VacationDaysUsed);                 // Monday and Tuesday, each once, by the weekday rule
    }

    [Fact]
    public void The_last_day_with_the_log_met_holds_the_ticket_with_the_examiners_signature()
    {
        var (g, days) = CoastalWeek(logMet: true);
        Assert.DoesNotContain(days[3].Narrative, l => l.Contains("examiner", StringComparison.Ordinal));   // only the last day decides
        Assert.Contains("The examiner signs your course card. You hold the coastal skipper ticket.", days[4].Narrative);
        Assert.Contains(days[4].Alerts, a => a.Text == "The examiner signs your course card. You hold the coastal skipper ticket.");
        var coastal = Ticket(g, "Coastal skipper");
        Assert.True(coastal.Held);
        Assert.Equal(249, coastal.HeldSinceDay);
        Assert.All(coastal.Asks, a => Assert.True(a.Met));
    }

    [Fact]
    public void The_last_day_with_the_log_short_leaves_the_course_done_and_the_ticket_waiting_and_it_is_held_at_the_first_midnight_that_meets_the_log()
    {
        var (g, days) = CoastalWeek(logMet: false);
        Assert.Contains(days[4].Narrative, l => l ==
            "The examiner signs the course; the ticket comes when your log is up to it: 300 miles under way, 15 days aboard, 8 hours under way after dark and 2 passages over 20 miles as skipper.");
        Assert.DoesNotContain(days[4].Narrative, l => l.Contains("You hold", StringComparison.Ordinal));
        var waiting = Ticket(g, "Coastal skipper");
        Assert.False(waiting.Held);
        Assert.Equal("You have done the coastal skipper course; the ticket waits for your log.", waiting.NextCourse);
        Assert.DoesNotContain(ClubGame.Board(g).Cards.OfType<CourseCard>(), c => c.Title == "The coastal skipper course");
        Assert.Equal("You have done the coastal skipper course; the ticket waits for your log.", Text(No(g.Execute(new Enrol("coastal")))));

        // The log catches up: the first midnight after, the ticket is held, with an alert and a logbook line.
        g.SetSkipperLogForTests(CoastalLog());
        var night = Ok(g.Execute(new WaitDays(1)));
        const string line = "Your log is up to it now: the examiner's signature stands. You hold the coastal skipper ticket.";
        Assert.Contains(line, night.Narrative);
        Assert.Contains(night.Alerts, a => a.Text == line);
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Alert && e.Text == line);
        Assert.True(Ticket(g, "Coastal skipper").Held);
    }

    [Fact]
    public void A_ticket_that_waited_for_a_passage_is_held_on_the_arrival_that_gives_the_log_its_last_miles()
    {
        var (g, _) = CoastalWeek(logMet: true, miles: 290);   // everything but the last miles
        Assert.False(Ticket(g, "Coastal skipper").Held);
        Ok(g.Execute(new WaitDays(1)));
        var arrival = ClubGame.Sail(g, "Hestholmen");        // 14 nm more: 304
        Assert.Contains(arrival.Alerts, a => a.Text.Contains("You hold the coastal skipper ticket.", StringComparison.Ordinal));
        Assert.True(Ticket(g, "Coastal skipper").Held);
        Assert.Equal(Status(g).Day, Ticket(g, "Coastal skipper").HeldSinceDay);
    }

    [Fact]
    public void A_ticket_waits_for_the_radio_ticket_as_for_any_ask_and_arrives_the_midnight_after_the_radio_course()
    {
        // The coastal course attended before the radio ticket is held: the ticket waits for the radio ticket like the other asks.
        var g = ClubGame.Afloat(100);
        g.SetSkipperLogForTests(CoastalLog());
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 245);
        for (int i = 0; i < 5; i++)
        {
            Ok(g.Execute(new Look()));
            if (i < 4) Ok(g.Execute(new Sleep()));
        }
        ClubGame.SitExam(g);
        var coastal = Ticket(g, "Coastal skipper");
        Assert.False(coastal.Held);
        Assert.Equal([true, true, true, true, false], coastal.Asks.Select(a => a.Met).ToArray());
        Assert.Equal("The radio ticket: not yet.", coastal.Asks[4].Text);
        Assert.StartsWith("You have done the coastal skipper course", coastal.NextCourse);

        // The radio course of next May: enrolled, attended; the coastal ticket follows at the midnight.
        Ok(g.Execute(new Enrol("radio")));
        Assert.Contains("days", Ticket(g, "Radio ticket").NextCourse + "days");
        ClubGame.ToMorning(g, 495);                                     // a Saturday: the first of next May's two dates
        Ok(g.Execute(new Look()));
        ClubGame.SitExam(g);
        Assert.True(Ticket(g, "Radio ticket").Held);
        Assert.False(Ticket(g, "Coastal skipper").Held);               // the log was met all along: only the radio ticket was missing, and it came at the end of the day
        var night = Ok(g.Execute(new WaitDays(1)));
        Assert.Contains(night.Alerts, a => a.Text.Contains("You hold the coastal skipper ticket.", StringComparison.Ordinal));
        Assert.True(Ticket(g, "Coastal skipper").Held);
    }

    // ------------------------------------------------------------------ a day missed

    [Fact]
    public void A_course_day_spent_at_sea_or_at_another_port_is_missed_and_the_course_is_forfeit_with_its_fee()
    {
        var g = ClubGame.Afloat(230);
        Ok(g.Execute(new Enrol("coastal")));
        ClubGame.ToMorning(g, 244);                               // the Sunday before
        ClubGame.LookUntilAtLeast(g, "21:00");
        int balance = Status(g).BalanceNok;
        ClubGame.Sail(g, "Storholmen");                            // across the midnight, in at about two on the Monday morning
        Assert.Equal(245, Status(g).Day);
        Ok(g.Execute(new WaitDays(1)));                            // Monday's midnight, at Storholmen: she was not at the club
        const string missed = "You were not at the club for day 1 of the coastal skipper course. The place and the fee are gone.";
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Alert && e.Text == missed);
        Assert.True(Status(g).BalanceNok <= balance);              // nothing comes back
        var coastal = Ticket(g, "Coastal skipper");
        Assert.False(coastal.Held);
        Assert.StartsWith("Next course: Monday to Friday, days 609 to 613", coastal.NextCourse);   // next year's: this one is gone
        // The days after it are not a course: back at the club on Tuesday nothing is held, and nothing is told.
        ClubGame.Sail(g, "Skjærvik");
        var look = Ok(g.Execute(new Look()));
        Assert.NotEqual("course", look.Action);
        // She can enrol again, for next year.
        var again = Ok(g.Execute(new Enrol("coastal")));
        Assert.Contains("days 609 to 613", Text(again));
    }
}
