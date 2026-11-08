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
            "remove" or "open" or "lift" => WithArg(arg, $"{verb} <cover>", a => new Remove(a)),
            "take" when arg.StartsWith("off ", StringComparison.OrdinalIgnoreCase) =>
                WithArg(arg[4..].Trim(), "take off <cover>", a => new Remove(a)),
            "refit" or "close" => WithArg(arg, $"{verb} <cover>", a => new Refit(a)),
            "put" when arg.StartsWith("back ", StringComparison.OrdinalIgnoreCase) =>
                WithArg(arg[5..].Trim(), "put back <cover>", a => new Refit(a)),
            "inspect" or "examine" or "x" => WithArg(arg, "inspect <component>", a => new Inspect(a)),
            "measure" or "meter" => WithArg(arg, "measure <component>", a => new Measure(a)),
            "start" or "crank" => new RunCommand(new StartEngine()),
            "clean" or "tighten" => WithArg(arg, "clean <component>", a => new Clean(a)),
            "replace" => WithArg(arg, "replace <component>", a => new Replace(a)),
            "buy" => WithArg(arg, "buy <item>", a => new Buy(a)),
            "charge" => new RunCommand(new Charge(arg.Length == 0 ? "start battery" : arg)),
            "check" => ParseCheck(arg),
            "defer" => WithArg(arg, "defer <problem>", a => new Defer(a)),
            "undefer" => WithArg(arg, "undefer <problem>", a => new Undefer(a)),
            "yard" => ParseYard(arg),
            "order" when arg.Equals("survey", StringComparison.OrdinalIgnoreCase) => new RunCommand(new OrderSurvey()),
            "order" => new Problem("Usage: order survey"),
            "read" when arg.Equals("survey", StringComparison.OrdinalIgnoreCase) => new RunQuery(new SurveyReport()),
            "read" => new Problem("Usage: read survey"),
            "survey" => new RunQuery(new SurveyReport()),
            "readiness" or "ready" => new RunQuery(new Readiness()),
            "wait" => ParseWait(arg),
            "launch" => new RunCommand(new Launch()),
            "emergency" when arg.Equals("lift", StringComparison.OrdinalIgnoreCase) => new RunCommand(new EmergencyLift()),
            "emergency" => new Problem("Usage: emergency lift"),
            "log" => new RunQuery(new Logbook()),
            "status" => new RunQuery(new Status()),
            "help" => new RunQuery(new Commands()),
            "quit" or "exit" => new Quit(),
            _ => new Problem($"I don't know how to '{verb}'. Type 'help'."),
        };
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
        if (arg.StartsWith("fix", StringComparison.OrdinalIgnoreCase) && (arg.Length == 3 || arg[3] == ' ' || arg[3] == '\t'))
            arg = arg[3..].Trim();
        return WithArg(arg, "yard fix <problem>", a => new FixByYard(a));
    }

    /// <summary>"wait 3", "wait 3 days", "wait a day".</summary>
    private static Parsed ParseWait(string arg)
    {
        const string usage = "Usage: wait <days>";
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
