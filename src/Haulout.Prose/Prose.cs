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
        // At the start of any look the boat speaks first; after anything else it speaks last.
        bool lookFirst = result.Accepted && result.Action == "look";
        if (lookFirst) AppendAlerts(sb, result.Alerts);
        foreach (var line in result.Narrative) sb.AppendLine(line);
        if (!result.Accepted) return sb.ToString();

        if (result.Observation is TripResultView trip) AppendTripSummary(sb, trip, result.Narrative);

        foreach (var fault in result.NewlyDiscovered)
            sb.AppendLine($"  Noted in the log: {fault.Name}.");

        if (result.MinutesTaken > 0 || result.CostNok > 0)
        {
            var parts = new List<string>();
            if (result.MinutesTaken > 0) parts.Add($"+{Duration(result.MinutesTaken)}");
            if (result.CostNok > 0) parts.Add($"{result.CostNok} NOK");
            sb.AppendLine($"  [{string.Join(", ", parts)} · {status.TimeOfDay}]");
        }
        if (!lookFirst) AppendAlerts(sb, result.Alerts);
        return sb.ToString();
    }

    private static void AppendAlerts(StringBuilder sb, IReadOnlyList<Alert>? alerts)
    {
        if (alerts is null) return;
        foreach (var a in alerts) sb.AppendLine($"  ! {a.Text}");
    }

    public static string Query(QueryResult result)
    {
        var sb = new StringBuilder();
        switch (result)
        {
            case NoticesView n when n.Alerts.Count == 0:
                sb.AppendLine("Nothing new from the boat.");
                break;
            case NoticesView n:
                AppendAlerts(sb, n.Alerts);
                break;
            case LogView log when log.Entries.Count == 0:
                if (WeatherHeader(log) is { } emptyHeader) sb.AppendLine(emptyHeader);
                sb.AppendLine("The logbook is empty.");
                break;
            case LogView log:
                if (WeatherHeader(log) is { } header) sb.AppendLine(header);
                foreach (var e in log.Entries)
                    sb.AppendLine($"Day {e.Day} {e.TimeOfDay}  {(e.Alert ? "! " : "")}{e.Text}{(e.Document is null ? "" : " (read survey)")}");
                break;
            case CommandList list:
                foreach (var c in list.Commands)
                {
                    var usage = c.Argument is null ? c.Verb : $"{c.Verb} <{c.Argument}>";
                    sb.AppendLine($"  {usage,-22} {c.Description}");
                }
                break;
            case StatusView s:
                AppendAlerts(sb, s.Notices);
                sb.AppendLine($"Day {s.Day}, {s.TimeOfDay} · worked {s.MinutesSpent / 60} h {s.MinutesSpent % 60} min · money spent {s.MoneySpentNok} NOK.");
                if (s.LaunchDeadlineDay > 0)
                    sb.AppendLine($"{MoneyWords.Balance(s.BalanceNok)} Launch deadline: day {s.LaunchDeadlineDay}.");
                if (s.Weather is not null) sb.AppendLine(s.Weather);
                sb.AppendLine($"You're {PlacePhrase(s.Place)}.");
                if (s.Sunk) sb.AppendLine(s.AtSea ? "She's on the bottom of the sound." : "She's on the bottom of the harbour.");
                else if (s.AtSea) sb.AppendLine("She's at sea.");
                else if (s.Afloat && s.Port is not null) sb.AppendLine($"She's in the water, at {s.Port}.");
                else if (s.Afloat) sb.AppendLine("She's in the water.");
                sb.AppendLine(s.EngineRunning ? "The engine is running." : "The engine is not running.");
                if (s.Tools.Count > 0) sb.AppendLine("Tools: " + string.Join(", ", s.Tools) + ".");
                foreach (var f in s.DiscoveredFaults) sb.AppendLine($"  Known: {f.Name} ({f.ComponentName})");
                break;
            case WeatherView w:
                sb.AppendLine(w.Text);
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
            case TripPlanView plan:
                sb.Append(TripPlan(plan));
                break;
            case InventoryView inv:
                sb.Append(Inventory(inv));
                break;
            case TripView trip:
                sb.Append(TripState(trip));
                break;
        }
        return sb.ToString();
    }

    /// <summary>The short block after a passage ends: hours, money, what went wrong, what was put right. The narrative already has the lines.</summary>
    private static void AppendTripSummary(StringBuilder sb, TripResultView t, IReadOnlyList<string> narrative)
    {
        bool Said(string text) => narrative.Any(n => n.Contains(text, StringComparison.OrdinalIgnoreCase));
        var hours = (t.Minutes + 59) / 60;
        var how = t.Towed ? "Towed to" : t.TurnedBack ? "Turned back to" : "Arrived at";
        sb.AppendLine($"  {how} {t.Destination}: {hours} {(hours == 1 ? "hour" : "hours")} since casting off, {t.MoneySpentNok} NOK spent.");
        var failures = t.Failures.Where(f => !Said(f)).ToList();
        var fixes = t.Fixes.Where(f => !Said(f)).ToList();
        if (t.Failures.Count == 0) sb.AppendLine("  Trouble on the way: none.");
        else if (failures.Count > 0) sb.AppendLine("  Trouble on the way: " + string.Join(" ", failures));
        else sb.AppendLine($"  Trouble on the way: {t.Failures.Count} {(t.Failures.Count == 1 ? "alarm" : "alarms")}, noted in the logbook.");
        if (t.Fixes.Count == 0) sb.AppendLine("  Put right at sea: nothing.");
        else if (fixes.Count > 0) sb.AppendLine("  Put right at sea: " + string.Join(" ", fixes));
    }

    /// <summary>The plan for a trip: route, estimate, forecast, readiness against it, spares, shelter, money.</summary>
    public static string TripPlan(TripPlanView v)
    {
        var sb = new StringBuilder();
        foreach (var b in v.Blockers) sb.AppendLine($"Can't go yet: {b}");
        if (v.DistanceNm <= 0 && string.IsNullOrEmpty(v.To)) return sb.ToString();
        sb.AppendLine($"{v.From} to {v.To}: {v.DistanceNm:0.#} nm on a course of {v.CourseDeg} degrees. Leaving day {v.DepartDay}.");
        sb.AppendLine(v.Estimate);
        foreach (var d in v.Forecast)
            sb.AppendLine(d.Known
                ? $"  Day {d.Day}: {d.Text}{(d.WindServes ? " The wind would serve under sail." : "")}"
                : $"  Day {d.Day}: {d.Text}");
        sb.AppendLine("Readiness for this trip:");
        if (v.Risks.Count == 0) sb.AppendLine("  No known problems that matter at sea.");
        foreach (var r in v.Risks)
        {
            var spare = r.Spare is null ? "no spare answers it"
                : r.SpareAboard ? $"spare aboard: {r.Spare}" : $"no spare aboard ({r.Spare})";
            sb.AppendLine($"  {r.Problem} ({r.Component}): {r.AtSea} [{spare}]");
        }
        sb.AppendLine(v.SparesAboard.Count == 0 ? "Spares aboard: none." : "Spares aboard: " + string.Join(", ", v.SparesAboard) + ".");
        if (v.SparesMissing.Count > 0)
            sb.AppendLine("Spares you don't have for the known problems: " + string.Join(", ", v.SparesMissing) + ".");
        if (v.Shelter.Count > 0) sb.AppendLine("Shelter on the way: " + string.Join(", ", v.Shelter) + ".");
        sb.AppendLine($"{MoneyWords.Balance(v.BalanceNok)} If it goes wrong, a tow costs {v.TowNok} NOK.");
        return sb.ToString();
    }

    /// <summary>What is aboard, and what the chandlery sells where she lies.</summary>
    public static string Inventory(InventoryView v)
    {
        var sb = new StringBuilder();
        if (v.Aboard.Count == 0) sb.AppendLine("Aboard: nothing in the way of spares or tools.");
        else
        {
            sb.AppendLine("Aboard:");
            foreach (var l in v.Aboard) sb.AppendLine(l.Tool ? $"  {l.Name} (tool)" : $"  {l.Name} x{l.Count}");
        }
        if (v.Shop is null) sb.AppendLine("Nothing for sale out here.");
        else
        {
            sb.AppendLine($"For sale at {v.Shop}:");
            foreach (var l in v.ForSale) sb.AppendLine($"  {l.Name}: {l.PriceNok} NOK, {Duration(l.Minutes)}");
        }
        return sb.ToString();
    }

    /// <summary>Where she is on the passage: the view's own text, plus the numbers when at sea.</summary>
    public static string TripState(TripView v)
    {
        var sb = new StringBuilder();
        sb.AppendLine(v.Text);
        if (v.AtSea)
        {
            sb.AppendLine($"{v.PositionNm:0.#} nm out from {v.From}, {v.RemainingNm:0.#} nm to {v.To}. {(v.Mode is null ? "" : char.ToUpperInvariant(v.Mode[0]) + v.Mode[1..])} " +
                          $"in hour {v.Hours}.");
            if (v.NearestShelter is not null) sb.AppendLine($"Nearest shelter: {v.NearestShelter}.{(v.CanAnchor ? " You could anchor." : "")}");
        }
        return sb.ToString();
    }

    /// <summary>"Day 75 · Bright, 6 °C, ..." the logbook's header; null when the view carries no weather.</summary>
    private static string? WeatherHeader(LogView log) =>
        log.Weather is null ? null : log.Day > 0 ? $"Day {log.Day} \u00b7 {log.Weather}" : log.Weather;

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
        sb.AppendLine($"Day {v.Day}. Launch deadline: day {v.LaunchDeadlineDay} ({deadline}). {MoneyWords.Balance(v.BalanceNok)} " +
                      (v.Afloat ? "She's in the water." : "She's ashore."));
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
            if (EscalationText(item) is { } escalates) sb.AppendLine($"  {escalates}");
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

    /// <summary>"Likely to get worse in 9–24 days." or "Escalates on launch."; null when nothing is expected.</summary>
    public static string? EscalationText(ReadinessItem item) =>
        (item.LikelyMinDays, item.LikelyMaxDays) is ({ } lo, { } hi)
            ? (lo == hi ? $"Likely to get worse in {lo} {(lo == 1 ? "day" : "days")}." : $"Likely to get worse in {lo}\u2013{hi} days.")
        : item.EscalatesOnLaunch ? "Escalates on launch." : null;

    /// <summary>"in the quarter berth", but "on deck" and "beside the hull".</summary>
    private static string PlacePhrase(string place) =>
        place.StartsWith("On ", StringComparison.Ordinal) || place.StartsWith("Beside ", StringComparison.Ordinal)
            ? Lower(place) : $"in the {Lower(place)}";

    private static string Lower(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static string Duration(int minutes) =>
        minutes >= 1440 && minutes % 1440 == 0 ? (minutes == 1440 ? "1 day" : $"{minutes / 1440} days") :
        minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";
}
