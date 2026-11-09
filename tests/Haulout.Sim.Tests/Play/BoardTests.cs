using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

/// <summary>The notice board and the opening briefing (design note 012, decision 6, step 2): dated cards sorted by day, a board per port, none at sea.</summary>
public class BoardTests
{
    private static CommandResult Ok(CommandResult r) => ClubGame.Ok(r);
    private static NoticeboardView Board(Game g) => ClubGame.Board(g);

    [Fact]
    public void The_clubs_board_carries_every_event_and_the_courses_sorted_by_day()
    {
        var g = ClubGame.New();
        var board = Board(g);
        Assert.Equal("The club's board by the slipway, pinned by the committee:", board.Header);
        Assert.Null(board.Empty);
        Assert.Equal(board.Cards.Select(c => c.Day).OrderBy(d => d).ToArray(), board.Cards.Select(c => c.Day).ToArray());
        Assert.Equal(
            new[] { "The club cruise to Kvitnes", "A friend's week aboard", "Midsummer at Storholmen", "The coastal skipper course", "The radio course", "The regatta party at Hestholmen", "The weekend delivery", "The week north" }.Order().ToArray(),
            board.Cards.Select(c => c.Title).Distinct().Order().ToArray());
        // The two radio dates and the coastal one, in day order among the events.
        Assert.Equal([138, 152, 166, 172, 180, 214, 229, 231, 245], board.Cards.Select(c => c.Day).ToArray());
        Assert.Equal([typeof(CourseCard), typeof(EventCard), typeof(CrewingCard), typeof(EventCard), typeof(EventCard), typeof(EventCard), typeof(CourseCard), typeof(CrewingCard), typeof(CourseCard)],
            board.Cards.Select(c => c.GetType()).ToArray());
    }

    [Fact]
    public void An_event_card_is_the_same_view_the_calendar_gives_with_its_status_and_requirements_in_the_players_words()
    {
        var g = ClubGame.New();
        var cal = ClubGame.Cal(g).Events!;
        foreach (var card in Board(g).Cards.OfType<EventCard>())
            Assert.Equal(JsonSerializer.Serialize(cal.Single(e => e.Name == card.Title)), JsonSerializer.Serialize(card.Event));
        var cruise = Board(g).Cards.OfType<EventCard>().Single(c => c.Title == "The club cruise to Kvitnes").Event;
        Assert.Contains(cruise.Requirements, r => r.Text == "anchor gear in order");
    }

    [Fact]
    public void A_course_card_has_the_date_price_days_vacation_days_status_and_what_the_log_still_lacks()
    {
        var g = ClubGame.New();
        var coastal = Board(g).Cards.OfType<CourseCard>().Single(c => c.Title == "The coastal skipper course");
        Assert.Equal((245, "Monday to Friday, days 245 to 249", "at the club in Skjærvik", 9500, 5, 5), (coastal.Day, coastal.When, coastal.Place, coastal.PriceNok, coastal.Days, coastal.VacationDays));
        Assert.False(coastal.Enrolled);
        Assert.Equal("open", coastal.Status);
        Assert.Equal(["300 miles: you have 0.", "15 days aboard: you have 0.", "8 hours under way after dark: you have 0.", "2 passages over 20 miles as skipper: you have 0.", "The radio ticket: not yet."], coastal.Shortfall.ToArray());
        Assert.Equal("The coastal skipper course, Monday to Friday, days 245 to 249, at the club in Skjærvik: 9500 NOK, 5 days, 5 vacation days.", coastal.Line);

        var radio = Board(g).Cards.OfType<CourseCard>().Where(c => c.Title == "The radio course").ToList();
        Assert.Equal([138, 229], radio.Select(c => c.Day).ToArray());
        Assert.All(radio, c => Assert.Equal((1800, 1, 0, "open"), (c.PriceNok, c.Days, c.VacationDays, c.Status)));
        Assert.All(radio, c => Assert.Empty(c.Shortfall));   // it asks nothing of the log
        Assert.Equal("The radio course, Saturday, day 138, at the club in Skjærvik: 1800 NOK, 1 day.", radio[0].Line);
    }

    [Fact]
    public void A_course_card_follows_enrolment_and_the_shortfall_follows_the_log()
    {
        var g = ClubGame.New();
        Ok(g.Execute(new Enrol("coastal")));
        var card = Board(g).Cards.OfType<CourseCard>().Single(c => c.Title == "The coastal skipper course");
        Assert.True(card.Enrolled);
        Assert.Equal("enrolled", card.Status);
        g.SetSkipperLogForTests(new SkipperLog { MilesUnderWay = 126.4 });
        Assert.Equal("300 miles: you have 126.", Board(g).Cards.OfType<CourseCard>().Single(c => c.Title == "The coastal skipper course").Shortfall[0]);
    }

    [Fact]
    public void A_course_card_says_the_enrolment_has_closed_once_the_day_before_has_gone()
    {
        var g = ClubGame.New(245);
        var card = Board(g).Cards.OfType<CourseCard>().Single(c => c.Title == "The coastal skipper course");
        Assert.Equal("enrolment closed", card.Status);
        // Past the course the card is next year\x27s.
        var after = ClubGame.New(250);
        var next = Board(after).Cards.OfType<CourseCard>().Single(c => c.Title == "The coastal skipper course");
        Assert.Equal((609, "open"), (next.Day, next.Status));
    }

    [Fact]
    public void At_Hestholmen_only_the_regatta_is_pinned_and_at_Kvitnes_nothing_is()
    {
        var g = ClubGame.Afloat(100);
        ClubGame.Sail(g, "Hestholmen");
        var hest = Board(g);
        Assert.Equal("The notice board at the Hestholmen quay:", hest.Header);
        var card = Assert.Single(hest.Cards);
        Assert.Equal("The regatta party at Hestholmen", card.Title);
        Assert.IsType<EventCard>(card);
        Assert.DoesNotContain(hest.Cards, c => c is CourseCard);

        // Kvitnes is reached through Hestholmen (the club cruise is held there, yet its board carries only the old notice).
        ClubGame.Sail(g, "Kvitnes");
        var kvitnes = Board(g);
        Assert.Empty(kvitnes.Cards);
        Assert.Equal("The board by the boathouse is empty but for a faded notice about the water.", kvitnes.Empty);
    }

    [Fact]
    public void At_Storholmen_the_board_is_a_line_about_the_ferry_and_at_sea_there_is_none()
    {
        var g = ClubGame.Afloat(100);
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Depart("Storholmen")));
        Ok(g.Execute(new Motor()));
        var sea = Board(g);
        Assert.Equal("You're at sea: the board is at the club.", sea.Header);
        Assert.Empty(sea.Cards);
        for (int i = 0; i < 40 && ClubGame.Status(g).AtSea; i++) Ok(g.Execute(new Continue()));
        var storholmen = Board(g);
        Assert.Empty(storholmen.Cards);
        Assert.StartsWith("The board outside the harbour office", storholmen.Empty);
    }

    [Fact]
    public void The_board_shows_only_what_the_player_can_know()
    {
        var g = ClubGame.New();
        var json = JsonSerializer.Serialize<QueryResult>(Board(g));
        var data = BoatLoader.LoadSloop28(withHours: true);
        foreach (var id in data.Components.Select(c => c.Id).Concat(data.AllFaults.Select(f => f.Id)).Concat(data.Calendar.Events.Select(e => e.Id)).Distinct())
            Assert.DoesNotContain($"\"{id}\"", json);
        // The cards are typed and open: each carries a discriminator, not an id.
        var types = JsonNode.Parse(json)!["Cards"]!.AsArray().Select(c => c!["type"]!.GetValue<string>()).Distinct().Order().ToArray();
        Assert.Equal(["course", "crewing", "event"], types);
        Assert.DoesNotContain("\"Id\"", json);
    }

    [Fact]
    public void The_opening_briefing_says_when_the_shops_open_and_ends_with_the_board()
    {
        var g = Game.NewFirstFault(withHours: true);
        var lines = ((BriefingView)g.Ask(new Briefing())).Lines;
        Assert.Equal("The club's board by the slipway has the season on it.", lines[^1]);
        Assert.Equal("The yard chandlery opens at nine; the yard office too.", lines[^2]);
        Assert.Equal(8, lines.Count);
        // A game without the hours says neither.
        Assert.Equal(6, ((BriefingView)Game.NewFirstFault().Ask(new Briefing())).Lines.Count);
    }

    [Fact]
    public void The_hours_in_the_briefing_follow_the_data()
    {
        var late = ClubGame.New(economy: TestEconomy.Edited(n => n["hours"]!["shop"]!["weekday"] = new JsonArray("10:30", "17:00")));
        Assert.Contains("The yard chandlery opens at 10:30; the yard office too.", ((BriefingView)late.Ask(new Briefing())).Lines);
    }
}
