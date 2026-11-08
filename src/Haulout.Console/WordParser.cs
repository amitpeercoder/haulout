using Haulout.Sim.Play;

/// <summary>What a typed line turned into.</summary>
public abstract record Parsed;
public sealed record RunCommand(Command Command) : Parsed;
public sealed record RunQuery(Query Query) : Parsed;
public sealed record Quit() : Parsed;
public sealed record Problem(string Message) : Parsed;

/// <summary>Turns a line of words into a Command or Query. No game knowledge beyond the verbs.</summary>
public static class WordParser
{
    public static Parsed? Parse(string line)
    {
        var text = line.Trim();
        if (text.Length == 0) return null;

        var (verb, arg) = Split(text);
        if (verb == "try" && arg.StartsWith("start", StringComparison.OrdinalIgnoreCase))
            (verb, arg) = ("start", "");

        if (verb == "lift" || verb == "pull")
            if (ParseBoot(arg) is { } boot) return boot;

        return verb switch
        {
            "uncover" => WithArg(arg, "uncover <component>", a => new Uncover(a)),
            "look" => ParseLook(arg),
            "go" or "walk" or "climb" or "enter" => WithArg(StripTo(arg), "go <place>", a => new GoTo(a)),
            "remove" or "open" or "lift" or "pull" => WithArg(arg, $"{verb} <cover>", a => new Remove(a)),
            "pump" when arg.Length == 0 || arg.Equals("bilge", StringComparison.OrdinalIgnoreCase) => new RunCommand(new PumpBilge()),
            "pump" => new Problem("Usage: pump | pump bilge"),
            "take" when arg.StartsWith("off ", StringComparison.OrdinalIgnoreCase) =>
                WithArg(arg[4..].Trim(), "take off <cover>", a => new Remove(a)),
            "refit" or "close" => WithArg(arg, $"{verb} <cover>", a => new Refit(a)),
            "put" when arg.StartsWith("back ", StringComparison.OrdinalIgnoreCase) =>
                WithArg(arg[5..].Trim(), "put back <cover>", a => new Refit(a)),
            "inspect" or "examine" or "x" => WithArg(arg, "inspect <component>", a => new Inspect(a)),
            "measure" or "meter" => WithArg(arg, "measure <component>", a => new Measure(a)),
            "start" or "crank" => new RunCommand(new StartEngine()),
            "stop" or "kill" => new RunCommand(new StopEngine()),
            "clean" or "tighten" => WithArg(arg, "clean <component>", a => new Clean(a)),
            "replace" => WithArg(arg, "replace <component>", a => new Replace(a)),
            "plan" => ParsePlan(arg),
            "depart" or "leave" => new RunCommand(new Depart(OrNull(StripFor(arg)))),
            "continue" => new RunCommand(new Continue()),
            "on" when arg.Length == 0 => new RunCommand(new Continue()),
            "carry" when arg.Equals("on", StringComparison.OrdinalIgnoreCase) => new RunCommand(new Continue()),
            "carry" => new Problem("Usage: carry on"),
            "motor" => new RunCommand(new Motor()),
            "sail" => new RunCommand(new Sail()),
            "heave" when arg.Equals("to", StringComparison.OrdinalIgnoreCase) => new RunCommand(new HeaveTo()),
            "heave" => new Problem("Usage: heave to"),
            "heave-to" or "heaveto" => new RunCommand(new HeaveTo()),
            "anchor" => new RunCommand(new Anchor()),
            "turn" when arg.Equals("back", StringComparison.OrdinalIgnoreCase) => new RunCommand(new TurnBack()),
            "turn" => new Problem("Usage: turn back"),
            "tow" => new RunCommand(new CallTow()),
            "call" when arg.Equals("tow", StringComparison.OrdinalIgnoreCase) => new RunCommand(new CallTow()),
            "call" => new Problem("Usage: call tow"),
            "inventory" or "inv" or "spares" => new RunQuery(new Inventory()),
            "trip" => new RunQuery(new Trip()),
            "chart" => new RunQuery(new Chart(OrNull(StripTo(arg)))),
            "season" => ParseSeason(arg),
            "page" => ParseSeason("page " + arg),
            "sale" => new RunQuery(new Sale()),
            "list" or "sell" => ParseList(arg),
            "accept" => ParseAccept(arg),
            "keep" => new RunCommand(new Keep()),
            "book" => ParseBook(arg),
            "haul" when arg.Equals("out", StringComparison.OrdinalIgnoreCase) => new RunCommand(new HaulOut()),
            "haul" => new Problem("Usage: haul out"),
            "haulout" => new RunCommand(new HaulOut()),
            "scrub" => new RunCommand(new Scrub()),
            "winterise" or "winterize" => new RunCommand(new WinteriseEngine()),
            "fill" when arg.Equals("tank", StringComparison.OrdinalIgnoreCase) || arg.Equals("the tank", StringComparison.OrdinalIgnoreCase) => new RunCommand(new FillTank()),
            "fill" => new Problem("Usage: fill tank"),
            "battery" when arg.Equals("ashore", StringComparison.OrdinalIgnoreCase) => new RunCommand(new BatteryAshore()),
            "battery" when arg.Equals("aboard", StringComparison.OrdinalIgnoreCase) => new RunCommand(new BatteryAboard()),
            "battery" => new Problem("Usage: battery ashore | battery aboard"),
            "charger" => new RunCommand(new WinterCharger()),
            "fit" when arg.Equals("cover", StringComparison.OrdinalIgnoreCase) || arg.Equals("winter cover", StringComparison.OrdinalIgnoreCase) || arg.Equals("the cover", StringComparison.OrdinalIgnoreCase) => new RunCommand(new FitWinterCover()),
            "fit" => new Problem("Usage: fit cover"),
            "buy" => WithArg(arg, "buy <item>", a => new Buy(a)),
            "charge" => new RunCommand(new Charge(arg.Length == 0 ? "start battery" : arg)),
            "check" => ParseCheck(arg),
            "defer" => WithArg(arg, "defer <problem>", a => new Defer(a)),
            "undefer" => WithArg(arg, "undefer <problem>", a => new Undefer(a)),
            "yard" => ParseYard(arg),
            "order" when arg.Equals("survey", StringComparison.OrdinalIgnoreCase) => new RunCommand(new OrderSurvey()),
            "order" => WithArg(arg, "order survey | order <item>", a => new Order(a)),
            "calendar" or "week" => new RunQuery(new Calendar()),
            "install" => WithArg(arg, "install <item>", a => new Install(a)),
            "upgrades" => new RunQuery(new Upgrades()),
            "invite" => ParseInvite(arg),
            "next" when arg.Equals("weekend", StringComparison.OrdinalIgnoreCase) => new RunCommand(new NextWeekend()),
            "next" => new Problem("Usage: next weekend"),
            "weekend" => new RunCommand(new NextWeekend()),
            "read" when arg.Equals("survey", StringComparison.OrdinalIgnoreCase) => new RunQuery(new SurveyReport()),
            "read" => new Problem("Usage: read survey"),
            "survey" => new RunQuery(new SurveyReport()),
            "readiness" or "ready" => new RunQuery(new Readiness()),
            "wait" => ParseWait(arg),
            "launch" => new RunCommand(new Launch()),
            "emergency" when arg.Equals("lift", StringComparison.OrdinalIgnoreCase) => new RunCommand(new EmergencyLift()),
            "emergency" => new Problem("Usage: emergency lift"),
            "log" => new RunQuery(new Logbook()),
            "weather" or "sky" => new RunQuery(new Weather()),
            "notices" or "alerts" => new RunQuery(new Notices()),
            "status" => new RunQuery(new Status()),
            "help" => new RunQuery(new Commands()),
            "quit" or "exit" => new Quit(),
            _ => new Problem($"I don't know how to '{verb}'. Type 'help'."),
        };
    }

    private static string? OrNull(string s) => s.Length == 0 ? null : s;

    private static string StripFor(string arg) =>
        arg.StartsWith("for ", StringComparison.OrdinalIgnoreCase) ? arg[4..].Trim() : StripTo(arg);

    /// <summary>"plan", "plan trip", "plan trip to X", "plan trip to X on day N", "plan trip on day N".</summary>
    private static Parsed ParsePlan(string arg)
    {
        const string usage = "Usage: plan trip [to <destination>] [on day <n>]";
        if (arg.Equals("trip", StringComparison.OrdinalIgnoreCase)) arg = "";
        else if (arg.StartsWith("trip ", StringComparison.OrdinalIgnoreCase)) arg = arg[5..].Trim();
        int? day = null;
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        int on = words.FindIndex(w => w.Equals("on", StringComparison.OrdinalIgnoreCase));
        if (on >= 0)
        {
            var rest = words.Skip(on + 1).ToList();
            if (rest.Count > 0 && rest[0].Equals("day", StringComparison.OrdinalIgnoreCase)) rest.RemoveAt(0);
            if (rest.Count != 1 || !int.TryParse(rest[0], out var n) || n < 0) return new Problem(usage);
            day = n;
            words = words.Take(on).ToList();
        }
        var dest = string.Join(' ', words);
        return new RunQuery(new PlanTrip(OrNull(StripTo(dest)), day));
    }

    /// <summary>"lift (the) boot on|of X", "pull back (the) boot on|of X"; null when it is not about a boot.</summary>
    private static Parsed? ParseBoot(string arg)
    {
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Equals("back", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        if (words.Count > 0 && words[0].Equals("the", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        if (words.Count == 0 || !words[0].Equals("boot", StringComparison.OrdinalIgnoreCase)) return null;
        words.RemoveAt(0);
        if (words.Count > 0 && (words[0].Equals("on", StringComparison.OrdinalIgnoreCase) || words[0].Equals("of", StringComparison.OrdinalIgnoreCase)))
            words.RemoveAt(0);
        return words.Count == 0 ? new Problem("Usage: uncover <component>") : new RunCommand(new Uncover(string.Join(' ', words)));
    }

    private static Parsed ParseLook(string arg)
    {
        if (arg.Length == 0) return new RunCommand(new Look());
        if (arg.StartsWith("at ", StringComparison.OrdinalIgnoreCase)) arg = arg[3..].Trim();
        return arg.Length == 0 ? new Problem("Usage: look | look at <component>") : new RunCommand(new Inspect(arg));
    }

    private static string StripTo(string arg) =>
        arg.StartsWith("to ", StringComparison.OrdinalIgnoreCase) ? arg[3..].Trim() : arg;

    private static Parsed ParseYard(string arg)
    {
        if (arg.Equals("wash", StringComparison.OrdinalIgnoreCase)) return new RunCommand(new WashHull());
        if (arg.StartsWith("install", StringComparison.OrdinalIgnoreCase) && (arg.Length == 7 || arg[7] == ' ' || arg[7] == '\t'))
            return WithArg(arg[7..].Trim(), "yard install <item>", a => new YardInstall(a));
        if (arg.StartsWith("fix", StringComparison.OrdinalIgnoreCase) && (arg.Length == 3 || arg[3] == ' ' || arg[3] == '\t'))
            arg = arg[3..].Trim();
        return WithArg(arg, "yard fix <problem>", a => new FixByYard(a));
    }

    /// <summary>"list for sale 150000", "list 150000", "sell 150000", "sell for 150000", "sell her at 150000".</summary>
    private static Parsed ParseList(string arg)
    {
        const string usage = "Usage: list for sale <nok> | sell <nok>";
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !(w.Equals("for", StringComparison.OrdinalIgnoreCase) || w.Equals("sale", StringComparison.OrdinalIgnoreCase)
                || w.Equals("at", StringComparison.OrdinalIgnoreCase) || w.Equals("her", StringComparison.OrdinalIgnoreCase)
                || w.Equals("it", StringComparison.OrdinalIgnoreCase) || w.Equals("nok", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return words.Count == 1 && int.TryParse(words[0], out var n) && n > 0 ? new RunCommand(new ListForSale(n)) : new Problem(usage);
    }

    /// <summary>"accept", "accept offer", "accept 2", "accept offer 2".</summary>
    private static Parsed ParseAccept(string arg)
    {
        const string usage = "Usage: accept [offer] [<n>]";
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Equals("offer", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        if (words.Count == 0) return new RunCommand(new AcceptOffer());
        return words.Count == 1 && int.TryParse(words[0], out var n) && n >= 1 ? new RunCommand(new AcceptOffer(n)) : new Problem(usage);
    }

    /// <summary>"book haulout 270", "book haul out 270", "book 270", "book haulout day 270".</summary>
    private static Parsed ParseBook(string arg)
    {
        const string usage = "Usage: book haulout <day>";
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Equals("haulout", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        else if (words.Count > 1 && words[0].Equals("haul", StringComparison.OrdinalIgnoreCase) && words[1].Equals("out", StringComparison.OrdinalIgnoreCase)) words.RemoveRange(0, 2);
        if (words.Count > 0 && words[0].Equals("day", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        return words.Count == 1 && int.TryParse(words[0], out var n) && n >= 0 ? new RunCommand(new BookHaulout(n)) : new Problem(usage);
    }

    /// <summary>"season" is the yard's calendar; "season page", "season page 1" and "page" read the page the logbook wrote at the autumn lift.</summary>
    private static Parsed ParseSeason(string arg)
    {
        if (arg.Length == 0) return new RunQuery(new Season());
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!words[0].Equals("page", StringComparison.OrdinalIgnoreCase)) return new RunQuery(new Season());
        if (words.Count == 1) return new RunQuery(new SeasonPage());
        return words.Count == 2 && int.TryParse(words[1], out var n) && n >= 1 ? new RunQuery(new SeasonPage(n)) : new Problem("Usage: season page [<season>]");
    }

    /// <summary>"invite friend 183", "invite friend day 183", "invite 183".</summary>
    private static Parsed ParseInvite(string arg)
    {
        const string usage = "Usage: invite friend <day>";
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Equals("friend", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        if (words.Count > 0 && words[0].Equals("day", StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
        return words.Count == 1 && int.TryParse(words[0], out var n) && n >= 0 ? new RunCommand(new InviteFriend(n)) : new Problem(usage);
    }

    /// <summary>"wait 3", "wait 3 days", "wait a day", "wait until spring", "wait spring".</summary>
    private static Parsed ParseWait(string arg)
    {
        const string usage = "Usage: wait <days> | wait a week | wait until spring";
        if (arg.Equals("spring", StringComparison.OrdinalIgnoreCase) || arg.Equals("until spring", StringComparison.OrdinalIgnoreCase))
            return new RunCommand(new WaitUntilSpring());
        if (arg.Equals("week", StringComparison.OrdinalIgnoreCase) || arg.Equals("a week", StringComparison.OrdinalIgnoreCase) || arg.Equals("one week", StringComparison.OrdinalIgnoreCase))
            return new RunCommand(new WaitWeek());
        if (arg.Equals("weekend", StringComparison.OrdinalIgnoreCase) || arg.Equals("until the weekend", StringComparison.OrdinalIgnoreCase) || arg.Equals("for the weekend", StringComparison.OrdinalIgnoreCase))
            return new RunCommand(new NextWeekend());
        var words = arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0 || words.Count > 2) return new Problem(usage);
        if (words.Count == 2 && !words[1].Equals("day", StringComparison.OrdinalIgnoreCase) && !words[1].Equals("days", StringComparison.OrdinalIgnoreCase))
            return new Problem(usage);
        if (words[0].Equals("a", StringComparison.OrdinalIgnoreCase) || words[0].Equals("one", StringComparison.OrdinalIgnoreCase))
            return new RunCommand(new WaitDays(1));
        return int.TryParse(words[0], out var n) && n >= 1 ? new RunCommand(new WaitDays(n)) : new Problem(usage);
    }

    private static Parsed ParseCheck(string arg) => arg.ToLowerInvariant() switch
    {
        "filter" or "bowl" or "filter bowl" => new RunCommand(new CheckFilterBowl()),
        "flow" or "water" or "exhaust" => new RunCommand(new CheckRawWaterFlow()),
        _ => new Problem("Usage: check filter | check flow"),
    };

    private static Parsed WithArg(string arg, string usage, Func<string, Command> make) =>
        arg.Length == 0 ? new Problem($"Usage: {usage}") : new RunCommand(make(arg));

    private static (string Verb, string Arg) Split(string text)
    {
        int i = text.IndexOfAny([' ', '\t']);
        return i < 0
            ? (text.ToLowerInvariant(), "")
            : (text[..i].ToLowerInvariant(), text[(i + 1)..].Trim());
    }
}
