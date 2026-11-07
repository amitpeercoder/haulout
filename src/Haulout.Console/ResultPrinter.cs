using Haulout.Sim.Play;

/// <summary>Prints facade results as plain text.</summary>
public static class ResultPrinter
{
    public static void PrintBriefing(BriefingView view)
    {
        foreach (var line in view.Lines) Console.WriteLine(line);
        Console.WriteLine();
        Console.WriteLine("Type 'help' for commands.");
    }

    public static void Print(CommandResult result, StatusView status)
    {
        foreach (var line in result.Narrative) Console.WriteLine(line);
        if (!result.Accepted || result.Finished) return;

        foreach (var fault in result.NewlyDiscovered)
            Console.WriteLine($"  Noted in the log: {fault.Name}.");

        if (result.MinutesTaken > 0 || result.CostNok > 0)
        {
            var parts = new List<string>();
            if (result.MinutesTaken > 0) parts.Add($"+{Duration(result.MinutesTaken)}");
            if (result.CostNok > 0) parts.Add($"{result.CostNok} NOK");
            Console.WriteLine($"  [{string.Join(", ", parts)} · {status.TimeOfDay}]");
        }
    }

    public static void Print(QueryResult result)
    {
        switch (result)
        {
            case LogView log when log.Entries.Count == 0:
                Console.WriteLine("The logbook is empty.");
                break;
            case LogView log:
                foreach (var e in log.Entries) Console.WriteLine($"Day {e.Day} {e.TimeOfDay}  {e.Text}");
                break;
            case CommandList list:
                foreach (var c in list.Commands)
                {
                    var usage = c.Argument is null ? c.Verb : $"{c.Verb} <{c.Argument}>";
                    Console.WriteLine($"  {usage,-22} {c.Description}");
                }
                break;
            case StatusView s:
                Console.WriteLine($"Day {s.Day}, {s.TimeOfDay}. Time spent: {s.MinutesSpent / 60} h {s.MinutesSpent % 60} min. Money spent: {s.MoneySpentNok} NOK.");
                Console.WriteLine(s.EngineRunning ? "The engine is running." : "The engine is not running.");
                foreach (var f in s.DiscoveredFaults) Console.WriteLine($"  Known: {f.Name} ({f.ComponentName})");
                break;
            case CompartmentsView c:
                Console.WriteLine(string.Join(", ", c.Compartments.Select(x => x.Name)));
                break;
        }
    }

    private static string Duration(int minutes) =>
        minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";
}
