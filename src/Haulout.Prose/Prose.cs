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
        if (!result.Accepted) return sb.ToString();

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
                foreach (var e in log.Entries)
                    sb.AppendLine($"Day {e.Day} {e.TimeOfDay}  {e.Text}{(e.Document is null ? "" : " (read survey)")}");
                break;
            case CommandList list:
                foreach (var c in list.Commands)
                {
                    var usage = c.Argument is null ? c.Verb : $"{c.Verb} <{c.Argument}>";
                    sb.AppendLine($"  {usage,-22} {c.Description}");
                }
                break;
            case StatusView s:
                sb.AppendLine($"Day {s.Day}, {s.TimeOfDay} · worked {s.MinutesSpent / 60} h {s.MinutesSpent % 60} min · money spent {s.MoneySpentNok} NOK.");
                if (s.LaunchDeadlineDay > 0)
                    sb.AppendLine($"Money left: {s.BalanceNok} NOK. Launch deadline: day {s.LaunchDeadlineDay}.");
                sb.AppendLine($"You're {PlacePhrase(s.Place)}.");
                if (s.Launched) sb.AppendLine("She's in the water.");
                sb.AppendLine(s.EngineRunning ? "The engine is running." : "The engine is not running.");
                if (s.Tools.Count > 0) sb.AppendLine("Tools: " + string.Join(", ", s.Tools) + ".");
                foreach (var f in s.DiscoveredFaults) sb.AppendLine($"  Known: {f.Name} ({f.ComponentName})");
                break;
            case AboardView a:
                sb.Append(Aboard(a));
                break;
            case SurveyReportView r:
                sb.Append(Survey(r));
                break;
            case ReadinessView r:
                sb.Append(Readiness(r));
                break;
        }
        return sb.ToString();
    }

    /// <summary>Where you are, the places and covers aboard, and what you have found. Names only.</summary>
    public static string Aboard(AboardView view)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You're {PlacePhrase(view.Place)}.");
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

    /// <summary>The surveyor's report as a letter.</summary>
    public static string Survey(SurveyReportView r)
    {
        var sb = new StringBuilder();
        if (!r.Ordered)
        {
            sb.AppendLine("No survey yet. 'order survey' hires the surveyor.");
            return sb.ToString();
        }
        sb.AppendLine(r.Surveyor);
        sb.AppendLine($"Survey of {r.BoatName}, day {r.Day}. Fee: {r.FeeNok} NOK.");
        sb.AppendLine();
        foreach (var l in r.Opening) sb.AppendLine(l);
        sb.AppendLine();
        foreach (var i in r.Items) sb.AppendLine($"{i.Number}. {i.Component} [{i.Severity}]: {i.Text}");
        sb.AppendLine();
        foreach (var l in r.Closing) sb.AppendLine(l);
        return sb.ToString();
    }

    /// <summary>The launch picture: open problems with prices now and later, what to look into, what is done.</summary>
    public static string Readiness(ReadinessView v)
    {
        var sb = new StringBuilder();
        var deadline = v.DaysToDeadline switch
        {
            0 => "that is today",
            1 => "1 day to go",
            -1 => "1 day past",
            > 1 => $"{v.DaysToDeadline} days to go",
            _ => $"{-v.DaysToDeadline} days past",
        };
        sb.AppendLine($"Day {v.Day}. Launch deadline: day {v.LaunchDeadlineDay} ({deadline}). Money left: {v.BalanceNok} NOK. " +
                      (v.Launched ? "She's in the water." : "She's ashore."));
        if (v.Open.Count == 0 && v.ToInvestigate.Count == 0 && v.Fixed.Count == 0)
        {
            sb.AppendLine("No known problems.");
            return sb.ToString();
        }
        foreach (var item in v.Open)
        {
            var state = item.State.Equals("Deferred", StringComparison.OrdinalIgnoreCase) ? " - deferred" : "";
            sb.AppendLine($"{item.Problem} ({item.Component}){state}");
            var later = $"Later: {item.CostLaterNok} NOK" + (string.IsNullOrEmpty(item.LaterNote) ? "" : $", {item.LaterNote}");
            sb.AppendLine($"  Now: {item.CostNowNok} NOK. {later}.{(item.OnHardOnly ? " On the hard only." : "")}");
            sb.AppendLine($"  If left: {item.Consequence}");
            foreach (var r in item.Routes)
            {
                var detail = r.Route.Equals("Yard", StringComparison.OrdinalIgnoreCase)
                    ? $"{r.How}, {r.CostNok} NOK, {r.LeadDays} {(r.LeadDays == 1 ? "day" : "days")}"
                    : $"{r.How}, {r.CostNok} NOK, {Duration(r.Minutes)}";
                var note = r.Available ? "" : $" (not available: {r.UnavailableReason})";
                sb.AppendLine($"  {r.Route}: {detail}{note}");
            }
        }
        if (v.ToInvestigate.Count > 0)
        {
            sb.AppendLine("To look into:");
            foreach (var i in v.ToInvestigate) sb.AppendLine($"  {i.Component}: {i.Note}");
        }
        if (v.Fixed.Count > 0)
            sb.AppendLine("Done: " + string.Join(", ", v.Fixed) + ".");
        return sb.ToString();
    }

    /// <summary>"in the quarter berth", but "on deck" and "beside the hull".</summary>
    private static string PlacePhrase(string place) =>
        place.StartsWith("On ", StringComparison.Ordinal) || place.StartsWith("Beside ", StringComparison.Ordinal)
            ? Lower(place) : $"in the {Lower(place)}";

    private static string Lower(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static string Duration(int minutes) =>
        minutes >= 1440 && minutes % 1440 == 0 ? (minutes == 1440 ? "1 day" : $"{minutes / 1440} days") :
        minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";
}
