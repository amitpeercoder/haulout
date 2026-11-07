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
                sb.AppendLine($"You're in the {Lower(s.Place)}.");
                sb.AppendLine(s.EngineRunning ? "The engine is running." : "The engine is not running.");
                if (s.Tools.Count > 0) sb.AppendLine("Tools: " + string.Join(", ", s.Tools) + ".");
                foreach (var f in s.DiscoveredFaults) sb.AppendLine($"  Known: {f.Name} ({f.ComponentName})");
                break;
            case AboardView a:
                sb.Append(Aboard(a));
                break;
        }
        return sb.ToString();
    }

    /// <summary>Where you are, the places and covers aboard, and what you have found. Names only.</summary>
    public static string Aboard(AboardView view)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You're in the {Lower(view.Place)}.");
        sb.AppendLine("Places:");
        foreach (var p in view.Places)
        {
            var notes = new List<string>();
            if (p.Here) notes.Add("you are here");
            else if (p.Visited) notes.Add("been there");
            if (!p.Available) notes.Add("not available now");
            var tag = notes.Count > 0 ? $" ({string.Join(", ", notes)})" : "";
            var next = p.Adjacent.Count > 0 ? $" - next to: {string.Join(", ", p.Adjacent)}" : "";
            sb.AppendLine($"  {p.Name}{tag}{next}");
        }
        if (view.Covers.Count > 0)
        {
            sb.AppendLine("Covers:");
            foreach (var c in view.Covers)
                sb.AppendLine($"  {c.Name} ({c.Place}): {(c.Removed ? "off" : "in place")}");
        }
        if (view.Components.Count == 0)
        {
            sb.AppendLine("Found so far: nothing.");
            return sb.ToString();
        }
        sb.AppendLine("Found so far:");
        foreach (var c in view.Components)
        {
            var here = c.ReachableHere ? "; within reach" : c.VisibleHere ? "; in sight" : "";
            sb.AppendLine($"  {c.Name}{(c.KnownProblem ? " (known problem)" : "")} - {c.Position}{here}");
        }
        return sb.ToString();
    }

    private static string Lower(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static string Duration(int minutes) =>
        minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";
}
