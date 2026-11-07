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

        return verb switch
        {
            "look" => arg.Length == 0 ? new RunCommand(new Look()) : new RunCommand(new Open(arg)),
            "open" => WithArg(arg, "open <compartment>", a => new Open(a)),
            "inspect" or "examine" or "x" => WithArg(arg, "inspect <component>", a => new Inspect(a)),
            "measure" or "meter" => WithArg(arg, "measure <component>", a => new Measure(a)),
            "start" or "crank" => new RunCommand(new StartEngine()),
            "clean" or "tighten" => WithArg(arg, "clean <component>", a => new Clean(a)),
            "replace" => WithArg(arg, "replace <component>", a => new Replace(a)),
            "charge" => new RunCommand(new Charge(arg.Length == 0 ? "start battery" : arg)),
            "check" => ParseCheck(arg),
            "log" => new RunQuery(new Logbook()),
            "status" => new RunQuery(new Status()),
            "help" => new RunQuery(new Commands()),
            "quit" or "exit" => new Quit(),
            _ => new Problem($"I don't know how to '{verb}'. Type 'help'."),
        };
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
