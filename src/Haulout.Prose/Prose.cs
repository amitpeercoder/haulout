using System.Text;
using Haulout.Sim.Play;

namespace Haulout.Prose;

/// <summary>
/// Renders facade results as plain text. The one renderer shared by every text adapter (console, MCP).
/// Every method returns lines each terminated by <see cref="Environment.NewLine"/>.
/// Facade ids are never rendered: names only.
/// </summary>
public static class Prose
{
    public static string Briefing(BriefingView view, bool includeHelpHint = true)
    {
        var sb = new StringBuilder();
        foreach (var line in view.Lines) sb.AppendLine(line);
        if (includeHelpHint)
        {
            sb.AppendLine();
            sb.AppendLine("Type 'help' for commands.");
        }
        return sb.ToString();
    }

    public static string Result(CommandResult result, StatusView status)
    {
        var sb = new StringBuilder();
        foreach (var line in result.Narrative) sb.AppendLine(line);
        if (!result.Accepted || result.Finished) return sb.ToString();

        foreach (var fault in result.NewlyDiscovered)
            sb.AppendLine($"  Noted in the log: {fault.Name}.");

        if (result.MinutesTaken > 0 || result.CostNok > 0)
        {
            var parts = new List<string>();
            if (result.MinutesTaken > 0) parts.Add($"+{Duration(result.MinutesTaken)}");
            if (result.CostNok > 0) parts.Add($"{result.CostNok} NOK");
            sb.AppendLine($"  [{string.Join(", ", parts)} · {status.TimeOfDay}]");
        }
        return sb.ToString();
    }

    public static string Query(QueryResult result)
    {
        var sb = new StringBuilder();
        switch (result)
        {
            case LogView log when log.Entries.Count == 0:
                sb.AppendLine("The logbook is empty.");
                break;
            case LogView log:
                foreach (var e in log.Entries) sb.AppendLine($"Day {e.Day} {e.TimeOfDay}  {e.Text}");
                break;
            case CommandList list:
                foreach (var c in list.Commands)
                {
                    var usage = c.Argument is null ? c.Verb : $"{c.Verb} <{c.Argument}>";
                    sb.AppendLine($"  {usage,-22} {c.Description}");
                }
                break;
            case StatusView s:
                sb.AppendLine($"Day {s.Day}, {s.TimeOfDay}. Time spent: {s.MinutesSpent / 60} h {s.MinutesSpent % 60} min. Money spent: {s.MoneySpentNok} NOK.");
                sb.AppendLine(s.EngineRunning ? "The engine is running." : "The engine is not running.");
                if (s.Tools.Count > 0) sb.AppendLine("Tools: " + string.Join(", ", s.Tools) + ".");
                foreach (var f in s.DiscoveredFaults) sb.AppendLine($"  Known: {f.Name} ({f.ComponentName})");
                break;
            case CompartmentsView c:
                sb.AppendLine(string.Join(", ", c.Compartments.Select(x => x.Name)));
                break;
            case AboardView a:
                sb.Append(Aboard(a));
                break;
        }
        return sb.ToString();
    }

    /// <summary>Compartments, whether opened, and the components found in each. Names only.</summary>
    public static string Aboard(AboardView view)
    {
        var sb = new StringBuilder();
        foreach (var comp in view.Compartments)
        {
            sb.AppendLine($"{comp.Name} ({(comp.Opened ? "opened" : "not opened")})");
            if (comp.Components.Count == 0)
            {
                sb.AppendLine("  nothing found yet");
                continue;
            }
            foreach (var c in comp.Components)
                sb.AppendLine($"  {c.Name}{(c.HasKnownFault ? " (known problem)" : "")}");
        }
        return sb.ToString();
    }

    private static string Duration(int minutes) =>
        minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";
}
