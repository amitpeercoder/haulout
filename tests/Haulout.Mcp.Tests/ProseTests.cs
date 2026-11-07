using Haulout.Sim.Play;
using P = Haulout.Prose.Prose;

namespace Haulout.Mcp.Tests;

public class ProseTests
{
    [Fact]
    public void Aboard_renders_place_covers_and_known_problems_but_no_ids()
    {
        var view = new AboardView(
            "Quarter berth",
            [
                new AboardPlace("Cockpit", false, true, true, ["Companionway", "On deck"]),
                new AboardPlace("Quarter berth", true, true, true, ["Companionway"]),
                new AboardPlace("Beside the hull", false, false, false, ["On deck"]),
            ],
            [
                new AboardCover("Battery-box lid", "Quarter berth", true),
                new AboardCover("Quarter-berth panel", "Quarter berth", false),
            ],
            [
                new FoundComponent("Negative terminal", "Under the quarter berth", true, true, true),
                new FoundComponent("Starter solenoid", "Engine bay, quarter-berth side", false, true, false),
                new FoundComponent("Fuel tank", "Under the cockpit locker", false, false, false),
            ]);

        var text = P.Aboard(view);

        Assert.Contains("You're in the quarter berth.", text);
        Assert.Contains("Quarter berth (you are here", text);
        Assert.Contains("Cockpit (been there)", text);
        Assert.Contains("Battery-box lid (Quarter berth): off", text);
        Assert.Contains("Quarter-berth panel (Quarter berth): in place", text);
        Assert.Contains("Negative terminal (known problem) - Under the quarter berth; within reach", text);
        Assert.Contains("Starter solenoid - Engine bay, quarter-berth side; in sight", text);
        Assert.Contains("Fuel tank - Under the cockpit locker", text);
        Assert.DoesNotContain("Fuel tank - Under the cockpit locker;", text);
        Assert.DoesNotContain("Starter solenoid (known", text);
        Assert.DoesNotContain("quarter-berth\n", text);
        Assert.DoesNotContain("battery-box-lid", text);
        Assert.DoesNotContain("negative-terminal", text);
    }

    [Fact]
    public void Status_shows_the_place()
    {
        var view = new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Cockpit");
        var text = P.Query(view);
        Assert.Contains("You're in the cockpit.", text);
    }

    [Fact]
    public void Briefing_help_hint_is_optional()
    {
        var view = new BriefingView(["One.", "Two."]);
        Assert.Contains("Type 'help'", P.Briefing(view));
        Assert.DoesNotContain("Type 'help'", P.Briefing(view, includeHelpHint: false));
    }

    [Fact]
    public void Result_matches_console_format()
    {
        var game = Game.NewFirstFault();
        var result = game.Execute(new GoTo("companionway"));
        var status = (StatusView)game.Ask(new Status());
        var text = P.Result(result, status).Replace("\r\n", "\n");
        Assert.EndsWith("  [+1 min · 08:01]\n", text);
    }
}
