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
