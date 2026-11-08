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
    public void Status_uses_the_right_preposition_and_work_wording()
    {
        var deck = P.Query(new StatusView(95, "10:11", 72, 400, null, false, false, [], [], "On deck"));
        Assert.Contains("You're on deck.", deck);
        Assert.DoesNotContain("in the on", deck);
        Assert.Contains("Day 95, 10:11 · worked 1 h 12 min · money spent 400 NOK.", deck);
        Assert.Contains("You're beside the hull.", P.Query(new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Beside the hull")));
        Assert.Contains("You're on deck.", P.Aboard(new AboardView("On deck", [], [], [])));
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

    [Fact]
    public void Survey_letter_renders_items_and_unordered_hint()
    {
        var view = new SurveyReportView(true, 3, "Henrik Dahl, marine surveyor", "Sea Wren", 5600,
            ["Dear owner,", "Here is what I found."],
            [new SurveyItemView(1, "Anodes", "Note", "Worn."), new SurveyItemView(3, "Raw-water seacock", "Before launch", "Stiff handle, weeping.")],
            ["Regards."]);
        var text = P.Query(view).Replace("\r\n", "\n");
        Assert.Contains("Henrik Dahl, marine surveyor\n", text);
        Assert.Contains("Survey of Sea Wren, day 3. Fee: 5600 NOK.", text);
        Assert.Contains("3. Raw-water seacock [Before launch]: Stiff handle, weeping.", text);
        Assert.Contains("Here is what I found.", text);
        Assert.EndsWith("Regards.\n", text);
        var none = P.Query(new SurveyReportView(false, 0, "", "", 0, [], [], []));
        Assert.Contains("No survey yet. 'order survey' hires the surveyor.", none);
    }

    [Fact]
    public void Readiness_shows_consequence_and_both_costs_without_ids()
    {
        var item = new ReadinessItem("Weeping raw-water seacock", "Raw-water seacock", "Deferred", 1200, 12800,
            "afloat: needs an emergency lift", true, "She could flood when the engine runs.",
            [new FixRoute("DIY", "Swap the seacock yourself", 1200, 180, 0, true, null),
             new FixRoute("Yard", "The yard refits it", 2500, 0, 4, false, "not enough money")]);
        var view = new ReadinessView(10, 96, 86, 15000, false, [item],
            [new InvestigateItem("Bilge pump", "Sounds tired.")], ["Loose clamp"]);
        var text = P.Query(view).Replace("\r\n", "\n");
        Assert.Contains("Weeping raw-water seacock (Raw-water seacock) - deferred", text);
        Assert.Contains("Now: 1200 NOK. Later: 12800 NOK, afloat: needs an emergency lift. On the hard only.", text);
        Assert.Contains("If left: She could flood when the engine runs.", text);
        Assert.Contains("DIY: Swap the seacock yourself, 1200 NOK, 3 h", text);
        Assert.Contains("Yard: The yard refits it, 2500 NOK, 4 days (not available: not enough money)", text);
        Assert.Contains("To look into:", text);
        Assert.Contains("Bilge pump: Sounds tired.", text);
        Assert.Contains("Done: Loose clamp.", text);
        Assert.Contains("86 days to go", text);
        Assert.DoesNotContain("raw-water-seacock", text);
        Assert.Contains("No known problems.", P.Query(new ReadinessView(1, 96, 95, 100, false, [], [], [])));
    }

    [Fact]
    public void Status_shows_money_deadline_and_launch()
    {
        var view = new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Cockpit", 9000, 96, true, true);
        var text = P.Query(view);
        Assert.Contains("Money left: 9000 NOK. Launch deadline: day 96.", text);
        Assert.Contains("She's in the water.", text);
        Assert.DoesNotContain("Money left", P.Query(new StatusView(1, "08:00", 0, 0, null, false, false, [], [], "Cockpit")));
    }

    [Fact]
    public void Log_hints_that_a_document_can_be_reread()
    {
        var text = P.Query(new LogView([new LogEntry(2, "09:00", "Survey report arrived.", 0, "survey report")]));
        Assert.Contains("(read survey)", text);
    }

    [Fact]
    public void Result_prints_cost_and_notes_even_when_finished_and_uses_days()
    {
        var status = new StatusView(5, "08:00", 0, 0, null, false, false, [], [], "Cockpit");
        var result = new CommandResult(true, "wait", ["Days pass."], 4 * 1440, 300, null,
            [new KnownFault("f-1", "Weeping seacock", "Raw-water seacock")], false, true);
        var text = P.Result(result, status).Replace("\r\n", "\n");
        Assert.Contains("  Noted in the log: Weeping seacock.", text);
        Assert.Contains("  [+4 days, 300 NOK · 08:00]", text);
        Assert.DoesNotContain("f-1", text);
    }
}
