using System.ComponentModel;
using Haulout.Sim.Play;
using ModelContextProtocol.Server;

namespace Haulout.Mcp;

/// <summary>One tool per player command or question. Each returns plain prose.</summary>
[McpServerToolType]
public sealed class HaulTools(GameSession session)
{
    [McpServerTool(Name = "look", Destructive = false), Description(
        "Look around the place you are in. Takes 2 minutes, free. Tells you where you are, which covers are off or in place here, what you can see from here and where you can go next. Start here.")]
    public string Look() => session.Do("look", new Look());

    [McpServerTool(Name = "go"), Description(
        "Move to a place next to the one you are in (for example the companionway, the saloon or the ground beside the hull). Takes 1 minute, free. You can only reach places adjacent to where you are; look says which.")]
    public string Go([Description("Name of the place, e.g. as listed by look or aboard.")] string place) =>
        session.Do("go", new GoTo(place), ("place", place));

    [McpServerTool(Name = "remove"), Description(
        "Take off a cover in the place you are in (steps, a panel, a lid, floorboards) to see and reach what is behind it. Takes a few minutes, free; how long depends on the cover and the result says.")]
    public string Remove([Description("Name of the cover, e.g. as listed by aboard.")] string cover) =>
        session.Do("remove", new Remove(cover), ("cover", cover));

    [McpServerTool(Name = "refit"), Description(
        "Put a cover you took off back. Takes a few minutes, free; how long depends on the cover and the result says. You must be in the place it belongs to.")]
    public string Refit([Description("Name of the cover.")] string cover) =>
        session.Do("refit", new Refit(cover), ("cover", cover));

    [McpServerTool(Name = "inspect"), Description(
        "Take a closer look at a part you have already found and can see from where you are. Takes 5 minutes, free. Describes its condition and anything visibly wrong with it.")]
    public string Inspect([Description("Name of a part you have found.")] string component) =>
        session.Do("inspect", new Inspect(component), ("component", component));

    [McpServerTool(Name = "uncover"), Description(
        "Pull back a small removable part (a boot or cap) on something you can reach, to see what is underneath. Takes 2 minutes, free.")]
    public string Uncover([Description("Name of a part you have found.")] string component) =>
        session.Do("uncover", new Uncover(component), ("component", component));

    [McpServerTool(Name = "measure"), Description(
        "Put the multimeter on a part you have found and can get a hand to from where you are. Takes 10 minutes, free (2 minutes if there is nothing a meter can read there). " +
        "Readings of the electrical system while the starter is cranking need a remote start button from the chandlery (see buy); without one only the battery's resting voltage can be read.")]
    public string Measure([Description("Name of a part you have found.")] string component) =>
        session.Do("measure", new Measure(component), ("component", component));

    [McpServerTool(Name = "clean"), Description(
        "Clean and tighten a part you have found. Takes 30 minutes, free. Stops the engine first if it is running. Does nothing for parts that cannot be cleaned.")]
    public string Clean([Description("Name of a part you have found.")] string component) =>
        session.Do("clean", new Clean(component), ("component", component));

    [McpServerTool(Name = "replace"), Description(
        "Fit a new part in place of one you have found. Time and price depend on the part, and the cost is charged in NOK; the result tells you what it took. " +
        "Stops the engine first if it is running. Not every part can be replaced.")]
    public string Replace([Description("Name of a part you have found.")] string component) =>
        session.Do("replace", new Replace(component), ("component", component));

    [McpServerTool(Name = "charge"), Description(
        "Charge a battery on shore power. Takes 8 hours, free. Stops the engine first if it is running.")]
    public string Charge([Description("Name of the battery.")] string component) =>
        session.Do("charge", new Charge(component), ("component", component));

    [McpServerTool(Name = "buy"), Description(
        "Buy a tool at the yard chandlery. The only item for sale is the remote start button: 400 NOK and 30 minutes. " +
        "It lets you hold the multimeter probes while the engine cranks.")]
    public string Buy([Description("Name of the item, e.g. remote start button.")] string item) =>
        session.Do("buy", new Buy(item), ("item", item));

    [McpServerTool(Name = "check_filter"), Description(
        "Look in the bowl of the fuel filter you have found. Takes 10 minutes, free.")]
    public string CheckFilter() => session.Do("check_filter", new CheckFilterBowl());

    [McpServerTool(Name = "check_flow"), Description(
        "Look at the cooling water coming out of the exhaust. Takes 5 minutes, free. You can only see the outlet from outside the boat. Only tells you anything while the engine is running.")]
    public string CheckFlow() => session.Do("check_flow", new CheckRawWaterFlow());

    [McpServerTool(Name = "start_engine"), Description(
        "Try to start the engine. Takes 2 minutes, free. Only from the cockpit, where the key is. Reports what happens. If she runs properly the game ends.")]
    public string StartEngine() => session.Do("start_engine", new StartEngine());

    [McpServerTool(Name = "status", ReadOnly = true), Description(
        "Day, time of day, time and money spent so far, whether the engine is running, tools owned and problems you have found. Costs no time.")]
    public string Status() => session.Ask("status", new Status());

    [McpServerTool(Name = "logbook", ReadOnly = true), Description(
        "Read the logbook: everything you have done so far, with the day, time and cost. Costs no time.")]
    public string Logbook() => session.Ask("logbook", new Logbook());

    [McpServerTool(Name = "aboard", ReadOnly = true), Description(
        "Where you are, the places and covers aboard (which are off), and every part you have found, with where it is and whether you can see or reach it from here (marking any with a known problem). Costs no time.")]
    public string Aboard() => session.Ask("aboard", new Aboard());

    [McpServerTool(Name = "help", ReadOnly = true), Description(
        "List the things you can do on the boat. Costs no time.")]
    public string Help() => session.Ask("help", new Commands()) + HelpFooter;

    /// <summary>The facade lists the console's verbs; say which tool each one is here.</summary>
    private const string HelpFooter =
        "Here each command is a tool of the same name, except: start = start_engine, check filter = check_filter, " +
        "check flow = check_flow, log = logbook. There is no quit; also available: status, aboard (what you have found so far), briefing.\n";

    [McpServerTool(Name = "briefing", ReadOnly = true), Description(
        "Re-read the situation you started with. Costs no time.")]
    public string Briefing() => session.Ask("briefing", new Briefing());
}
