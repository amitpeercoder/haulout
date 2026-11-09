using Haulout.Sim.Play;

namespace Haulout.Sim.Tests;

/// <summary>The console's words for the calendar: the query, marking an event as going and taking the mark off (design note 010, second pass I).</summary>
public class ConsoleParserTests
{
    [Fact]
    public void Calendar_alone_is_the_query_and_plan_and_unplan_take_the_event_by_its_words()
    {
        Assert.IsType<Calendar>(Assert.IsType<RunQuery>(WordParser.Parse("calendar")).Query);
        Assert.Equal(new PlanEvent("regatta"), Assert.IsType<RunCommand>(WordParser.Parse("calendar plan regatta")).Command);
        Assert.Equal(new PlanEvent("the club cruise"), Assert.IsType<RunCommand>(WordParser.Parse("Calendar PLAN the club cruise")).Command);
        Assert.Equal(new UnplanEvent("midsummer"), Assert.IsType<RunCommand>(WordParser.Parse("calendar unplan midsummer")).Command);
        Assert.Equal(new PlanEvent("friend-week"), Assert.IsType<RunCommand>(WordParser.Parse("week plan friend-week")).Command);
    }

    [Fact]
    public void The_club_words_are_skipper_tickets_board_and_enrol_with_a_course_by_its_name()
    {
        Assert.IsType<Skipper>(Assert.IsType<RunQuery>(WordParser.Parse("skipper")).Query);
        Assert.IsType<Tickets>(Assert.IsType<RunQuery>(WordParser.Parse("tickets")).Query);
        Assert.IsType<Noticeboard>(Assert.IsType<RunQuery>(WordParser.Parse("board")).Query);
        Assert.IsType<Noticeboard>(Assert.IsType<RunQuery>(WordParser.Parse("noticeboard")).Query);
        Assert.Equal(new Enrol("radio"), Assert.IsType<RunCommand>(WordParser.Parse("enrol radio")).Command);
        Assert.Equal(new Enrol("coastal course"), Assert.IsType<RunCommand>(WordParser.Parse("enroll on coastal course")).Command);
        Assert.Equal("Usage: enrol <course>", Assert.IsType<Problem>(WordParser.Parse("enrol")).Message);
    }

    [Fact]
    public void The_radio_words_are_call_a_station_with_aliases_and_call_tow_still_calls_the_towboat()
    {
        Assert.Equal(new Call("harbour"), Assert.IsType<RunCommand>(WordParser.Parse("call harbour")).Command);
        Assert.Equal(new Call("ferry"), Assert.IsType<RunCommand>(WordParser.Parse("call ferry")).Command);
        Assert.Equal(new Call("coast radio"), Assert.IsType<RunCommand>(WordParser.Parse("call coast radio")).Command);
        Assert.Equal(new Call("coastguard"), Assert.IsType<RunCommand>(WordParser.Parse("call coastguard")).Command);
        Assert.Equal(new Call("coast radio"), Assert.IsType<RunCommand>(WordParser.Parse("radio check")).Command);
        Assert.IsType<CallTow>(Assert.IsType<RunCommand>(WordParser.Parse("call tow")).Command);
        Assert.IsType<Problem>(WordParser.Parse("call"));
    }

    [Fact]
    public void The_exam_words_are_read_a_document_exam_and_answer_a_letter_and_read_survey_still_reads_the_survey()
    {
        Assert.Equal(new ReadDocument("radio course book"), Assert.IsType<RunQuery>(WordParser.Parse("read radio course book")).Query);
        Assert.Equal(new ReadDocument("course book"), Assert.IsType<RunQuery>(WordParser.Parse("READ course book")).Query);
        Assert.IsType<SurveyReport>(Assert.IsType<RunQuery>(WordParser.Parse("read survey")).Query);
        Assert.Equal("Usage: read survey | read <document>", Assert.IsType<Problem>(WordParser.Parse("read")).Message);
        Assert.IsType<ExamQuestion>(Assert.IsType<RunQuery>(WordParser.Parse("exam")).Query);
        Assert.Equal(new Answer("b"), Assert.IsType<RunCommand>(WordParser.Parse("answer b")).Command);
        Assert.Equal("Usage: answer <a, b, c or d>", Assert.IsType<Problem>(WordParser.Parse("answer")).Message);
    }

    [Theory]
    [InlineData("calendar plan")]
    [InlineData("calendar unplan")]
    [InlineData("calendar sideways")]
    public void An_unfinished_calendar_line_gets_the_usage(string line)
    {
        var p = Assert.IsType<Problem>(WordParser.Parse(line));
        Assert.Equal("Usage: calendar | calendar plan <event> | calendar unplan <event>", p.Message);
    }

    [Fact]
    public void The_commands_have_their_json_names_and_the_help_lists_the_verbs()
    {
        Assert.Equal("{\"type\":\"plan-event\",\"Event\":\"regatta\"}", System.Text.Json.JsonSerializer.Serialize<Command>(new PlanEvent("regatta")));
        Assert.Equal("{\"type\":\"unplan-event\",\"Event\":\"regatta\"}", System.Text.Json.JsonSerializer.Serialize<Command>(new UnplanEvent("regatta")));
        var game = Game.NewFirstFault();
        var verbs = ((CommandList)game.Ask(new Commands())).Commands.Select(c => c.Verb).ToList();
        Assert.Contains("calendar plan", verbs);
        Assert.Contains("calendar unplan", verbs);
    }
}
