using Haulout.Sim.Play;
using P = Haulout.Prose.Prose;

namespace Haulout.Mcp.Tests;

public class ProseTests
{
    [Fact]
    public void Aboard_renders_names_opened_state_and_known_problems_but_no_ids()
    {
        var view = new AboardView(
        [
            new AboardCompartment("battery-box", "Battery box", true,
            [
                new FoundComponent("negative-terminal", "Negative terminal", true),
                new FoundComponent("start-battery", "Start battery", false),
            ]),
            new AboardCompartment("engine-front", "Engine front", false, []),
        ]);

        var text = P.Aboard(view);

        Assert.Contains("Battery box (opened)", text);
        Assert.Contains("Negative terminal (known problem)", text);
        Assert.Contains("Start battery", text);
        Assert.DoesNotContain("Start battery (known", text);
        Assert.Contains("Engine front (not opened)", text);
        Assert.DoesNotContain("battery-box", text);
        Assert.DoesNotContain("negative-terminal", text);
        Assert.DoesNotContain("start-battery", text);
        Assert.DoesNotContain("engine-front", text);
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
        var result = game.Execute(new Open("battery box"));
        var status = (StatusView)game.Ask(new Status());
        var text = P.Result(result, status).Replace("\r\n", "\n");
        Assert.EndsWith("  [+2 min · 08:02]\n", text);
    }
}
