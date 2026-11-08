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

        if (result.Observation is MeterReading { Gap: { } gap }) sb.AppendLine(CircuitGapLine(gap));
        if (result.Observation is TripResultView trip) AppendTripSummary(sb, trip, result.Narrative);
        if (result.Observation is HauloutView lift) AppendLiftSummary(sb, lift);
        if (result.Observation is ListingView listing) sb.Append(Letter(listing.Letter));
        if (result.Observation is ClosingView closing) sb.AppendLine($"  {MoneyWords.Balance(closing.BalanceNok)} The game is over; the logbook still reads.");

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

    /// <summary>The sum the player can do with their own readings. Names no part (002 finding 4).</summary>
    public static string CircuitGapLine(CircuitGap gap)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (gap.Ordinary) return "Nothing lost in the starting circuit beyond what a sound one loses.";
        return gap.AccountedFor
            ? "Nothing lost in the starting circuit that your readings don't account for."
            : $"{gap.GapVolts.ToString("F1", ci)} V lost somewhere in the starting circuit while cranking; your readings so far account for {gap.AccountedVolts.ToString("F2", ci)} V of it.";
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
                    sb.AppendLine($"Day {e.Day} {e.TimeOfDay}  {(e.Alert ? "! " : "")}{e.Text}{DocumentNote(e.Document)}");
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
                    sb.AppendLine(s.SeasonLead is { Length: > 0 } lead
                        ? $"{MoneyWords.Balance(s.BalanceNok)} {lead}"
                        : $"{MoneyWords.Balance(s.BalanceNok)} Launch deadline: day {s.LaunchDeadlineDay}.");
                if (s.Season is not null) sb.AppendLine(s.Season);
                if (s.Week is not null) sb.AppendLine(s.Week);
                if (s.Next is not null) sb.AppendLine(s.Next);
                if (s.Sold) sb.AppendLine("She's sold. The logbook is all that's left of her for you.");
                if (s.Weather is not null) sb.AppendLine(s.Weather);
                sb.AppendLine($"You're {PlacePhrase(s.Place)}.");
                if (s.Sunk) sb.AppendLine(s.AtSea ? "She's on the bottom of the sound." : "She's on the bottom of the harbour.");
                else if (s.AtSea) sb.AppendLine("She's at sea.");
                else if (s.Afloat && s.Port is not null) sb.AppendLine($"She's in the water, at {s.Port}.");
                else if (s.Afloat) sb.AppendLine("She's in the water.");
                sb.AppendLine(s.EngineRunning ? "The engine is running." : "The engine is not running.");
                if (s.Fuel is not null) sb.AppendLine(s.Fuel);
                if (s.Tools.Count > 0) sb.AppendLine("Tools: " + string.Join(", ", s.Tools) + ".");
                foreach (var f in s.DiscoveredFaults) sb.AppendLine($"  Known: {f.Name} ({f.ComponentName}){(f.Fixed ? " - fixed" : "")}");
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
            case SeasonView season:
                sb.Append(Season(season));
                break;
            case SaleView sale:
                sb.Append(Sale(sale));
                break;
            case ChartView chart:
                sb.Append(Chart(chart));
                break;
            case CalendarView calendar:
                sb.Append(Calendar(calendar));
                break;
            case UpgradesView upgrades:
                sb.Append(Upgrades(upgrades));
                break;
            case SeasonPageView page:
                sb.Append(SeasonPage(page));
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

    /// <summary>The bill of a lift, after the card in the narrative.</summary>
    private static void AppendLiftSummary(StringBuilder sb, HauloutView v)
    {
        sb.AppendLine(v.StorageNok > 0
            ? $"  Lift {v.LiftNok} NOK, winter storage {v.StorageNok} NOK: {v.LiftNok + v.StorageNok} NOK in all, in the logbook."
            : $"  Lift {v.LiftNok} NOK, in the logbook.");
    }

    /// <summary>The note at the end of a logbook line that has a document behind it.</summary>
    private static string DocumentNote(string? document) =>
        document is null ? ""
        : document.StartsWith("viewing", StringComparison.Ordinal) ? " (sale)"
        : document.StartsWith("season", StringComparison.Ordinal) ? " (season page)"
        : " (read survey)";

    /// <summary>The broker's valuation letter, as a letter: who, the lines with their NOK, the value, the ask, the broker's words.</summary>
    public static string Letter(ValuationLetter v)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(v.Broker);
        sb.AppendLine($"Valuation, day {v.Day}.");
        foreach (var l in v.Lines) sb.AppendLine($"  {l.Text}: {Signed(l.Nok)} NOK");
        sb.AppendLine($"The broker values her at {v.ValueNok} NOK.");
        if (v.AskNok is { } ask)
            sb.AppendLine(ask > v.ValueNok ? $"You are asking {ask} NOK, {ask - v.ValueNok} NOK over that."
                : ask < v.ValueNok ? $"You are asking {ask} NOK, {v.ValueNok - ask} NOK under that."
                : $"You are asking {ask} NOK.");
        foreach (var line in v.Text) sb.AppendLine(line);
        return sb.ToString();
    }

    private static string Signed(int nok) => nok > 0 ? $"+{nok}" : nok.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The sale: the letter, the listing, and every viewing with what the buyer's surveyor found.</summary>
    public static string Sale(SaleView v)
    {
        var sb = new StringBuilder();
        if (v.Sold) sb.AppendLine("She's sold.");
        else sb.AppendLine(v.Listed ? $"Listed for sale at {v.AskNok} NOK." : "Not listed for sale. 'list for sale <nok>' puts her on the market from the hard.");
        if (v.Letter is { } letter) sb.Append(Letter(letter));
        if (v.Viewings.Count == 0)
        {
            if (v.Listed) sb.AppendLine("No one has been to look at her yet.");
            return sb.ToString();
        }
        sb.AppendLine();
        foreach (var viewing in v.Viewings)
        {
            sb.AppendLine($"Viewing {viewing.Number}, day {viewing.Day}: " + (viewing.Findings.Count == 0
                ? "their surveyor found nothing wrong."
                : "their surveyor found:"));
            foreach (var f in viewing.Findings) sb.AppendLine($"  {f}");
            sb.AppendLine($"  They value her at {viewing.ValueNok} NOK and offer {viewing.OfferNok} NOK, " +
                          (viewing.Open ? $"open until day {viewing.HoldsUntilDay}." : $"which lapsed on day {viewing.HoldsUntilDay}."));
        }
        if (v.Listed && v.Viewings.Any(x => x.Open)) sb.AppendLine("'accept' takes the best open offer; 'accept <n>' takes offer n.");
        return sb.ToString();
    }

    /// <summary>The week (design note 010): today, the days, the money, what is coming and what is on order.</summary>
    public static string Calendar(CalendarView v)
    {
        var sb = new StringBuilder();
        sb.AppendLine(v.Week);
        if (v.VacationDaysPerYear > 0)
        {
            string unpaid = v.UnpaidDays > 0 ? $", {v.UnpaidDays} unpaid" : "";
            sb.AppendLine($"Vacation days: {v.VacationDaysLeft} of {v.VacationDaysPerYear} left this year ({v.VacationDaysUsed} used{unpaid}).");
        }
        sb.AppendLine(v.OverdraftLimitNok > 0
            ? $"Balance {v.BalanceNok} NOK (the bank allows {v.OverdraftLimitNok} overdrawn)."
            : MoneyWords.Balance(v.BalanceNok));
        if (v.PayNok > 0) sb.AppendLine($"Payday: {v.PayNok} NOK, next on day {v.NextPaydayDay}.");
        if (v.Ahead.Count > 0)
        {
            sb.AppendLine("Coming up:");
            foreach (var e in v.Ahead)
                sb.AppendLine($"  Day {e.Day}, {e.Weekday}: {e.Text}{(e.Nok != 0 ? $" {Signed(e.Nok)} NOK" : "")}.");
        }
        if (v.Orders.Count > 0)
        {
            sb.AppendLine("On order:");
            foreach (var o in v.Orders) sb.AppendLine($"  {o.Part}: ordered day {o.OrderedDay}, due day {o.DueDay} ({o.Status}).");
        }
        if (v.Events is { Count: > 0 } events)
        {
            sb.AppendLine("Worth sailing for:");
            foreach (var e in events)
            {
                sb.AppendLine($"  {e.Name}{(e.Name.Contains(e.Place, StringComparison.Ordinal) ? "" : $" at {e.Place}")}, {e.When}: {e.Status}.");
                foreach (var r in e.Requirements)
                    sb.AppendLine($"    {r.Text}: {(r.Met == true ? "met" : r.Met == false ? "not met" : "not known yet")} ({r.Note}).");
                if (e.Story.Length > 0) sb.AppendLine($"    {e.Story}");
            }
        }
        if (v.Reputation is { Length: > 0 } standing) sb.AppendLine(standing);
        var m = v.Money;
        sb.AppendLine($"This season: in {m.IncomeNok} NOK; out {m.SpendingNok} NOK spent, {m.BillsNok} NOK bills"
            + (m.InterestNok > 0 ? $", {m.InterestNok} NOK interest" : "") + (m.UnpaidNok > 0 ? $", {m.UnpaidNok} NOK unpaid leave" : "") + ".");
        if (m.Recent.Count > 0)
        {
            sb.AppendLine("Recent:");
            foreach (var e in m.Recent) sb.AppendLine($"  Day {e.Day}: {e.Text} {Signed(e.Nok)} NOK.");
        }
        return sb.ToString();
    }

    /// <summary>The yard calendar: the season line and the prices.</summary>
    public static string Season(SeasonView v)
    {
        var sb = new StringBuilder();
        sb.AppendLine(v.Text);
        sb.AppendLine($"The yard lifts boats from day {v.SlotsFromDay} to day {v.SlotsToDay}.");
        sb.AppendLine($"A lift costs {v.LiftNok} NOK and winter storage on the hard {v.StorageNok} NOK; changing a booking costs {v.ChangeFeeNok} NOK; the emergency lift costs {v.EmergencyLiftNok} NOK.");
        if (v.Reputation is { Length: > 0 } standing) sb.AppendLine(standing);
        if (v.Tasks.Count > 0)
        {
            sb.AppendLine("The winter work on her:");
            foreach (var task in v.Tasks)
            {
                string price = task.CostNok > 0 ? $", {task.CostNok} NOK" : "";
                sb.AppendLine(task.Done
                    ? $"  Done: {task.Name}."
                    : $"  To do: {task.Name} ({task.Command}; {Duration(task.Minutes)}{price}). Left alone: {task.Risk}");
            }
        }
        return sb.ToString();
    }

    /// <summary>The plan for a trip: route, estimate, forecast, readiness against it, spares, shelter, money.</summary>
    public static string TripPlan(TripPlanView v)
    {
        var sb = new StringBuilder();
        foreach (var b in v.Blockers) sb.AppendLine($"Can't go yet: {b}");
        if (v.DistanceNm <= 0 && string.IsNullOrEmpty(v.To)) return sb.ToString();
        sb.AppendLine($"{v.From} to {v.To}: {v.DistanceNm:0.#} nm on a course of {v.CourseDeg} degrees. Leaving day {v.DepartDay}.");
        sb.AppendLine(v.Estimate);
        if (v.Fuel is not null) sb.AppendLine(v.Fuel);
        foreach (var w in v.Warnings ?? []) sb.AppendLine($"Mind: {w}");
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
        foreach (var line in v.AtDestination ?? []) sb.AppendLine($"At {v.To}: {line}");
        sb.AppendLine($"{MoneyWords.Balance(v.BalanceNok)} If it goes wrong, a tow costs {v.TowNok} NOK.");
        return sb.ToString();
    }

    /// <summary>The upgrades: status first, then what they cost, what they ask of you and what they open.</summary>
    public static string Upgrades(UpgradesView v)
    {
        var sb = new StringBuilder();
        if (v.Items.Count == 0) return "There is nothing to fit to her.\n";
        sb.AppendLine("Upgrades:");
        foreach (var u in v.Items)
        {
            string yard = u.YardNok is { } nok && u.YardDays is { } days
                ? $"the yard {nok} NOK labour and {days} day{(days == 1 ? "" : "s")}"
                : "not a job for the yard";
            sb.AppendLine($"  {u.Name} ({u.Status}): {u.PriceNok} NOK; {Duration(u.InstallMinutes)} of your own work, or {yard}; adds {u.ValueNok} NOK to her value; opens {u.Opens}.");
        }
        return sb.ToString();
    }

    /// <summary>The page the logbook wrote at the autumn lift: the lines as they are, no grade.</summary>
    public static string SeasonPage(SeasonPageView v)
    {
        var sb = new StringBuilder();
        foreach (var line in v.Lines) sb.AppendLine(line);
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

    private static string Position(double east, double north) =>
        $"{Math.Abs(east):0.0} {(east < 0 ? "W" : "E")} {Math.Abs(north):0.0} {(north < 0 ? "S" : "N")}";

    /// <summary>The shelters of the route between two ports, nearest the first port first, whichever way the data writes it.</summary>
    private static IEnumerable<(string Name, double AtNm)> ShelterAlong(ChartView v, string from, string to)
    {
        var direct = v.Routes.FirstOrDefault(r => r.From == from && r.To == to);
        if (direct is not null) return direct.Shelters.Select(s => (s.Name, s.AtNm));
        var back = v.Routes.FirstOrDefault(r => r.From == to && r.To == from);
        return back is null ? [] : back.Shelters.Select(s => (s.Name, back.DistanceNm - s.AtNm)).OrderBy(s => s.Item2);
    }

    /// <summary>
    /// The chart as text (design note 009): where she is, the wind, a line per port with the distance and bearing from where she is, the
    /// shelters on the way, the plan, the track of the passage under way as fixes, and this season's earlier passages. Positions are
    /// nautical miles east and north of the home port.
    /// </summary>
    public static string Chart(ChartView v)
    {
        var sb = new StringBuilder();
        sb.AppendLine(v.Here);
        sb.AppendLine($"She is at {Position(v.Boat.EastNm, v.Boat.NorthNm)}.");
        sb.AppendLine($"Wind: {v.Wind.Text}");
        sb.AppendLine("Ports (distance and bearing from where she is):");
        foreach (var p in v.Ports)
        {
            var marks = new List<string>();
            if (p.Yard) marks.Add("yard");
            if (p.Shop) marks.Add("shop");
            if (p.Fuel) marks.Add("fuel");
            if (p.Anchorage) marks.Add("anchorage");
            string where = p.IsHere ? "you are here" : $"{p.DistanceNm:0.0} nm on {p.BearingDeg:000}";
            string note = p.IsHere || v.Boat.AtSea || p.Reachable ? "" : " (no route from here)";
            sb.AppendLine($"  {p.Name}, {where}, at {Position(p.EastNm, p.NorthNm)}: {(marks.Count == 0 ? "nothing" : string.Join(", ", marks))}.{note}");
        }
        if (v.Boat.AtSea && v.Boat.From is not null && v.Boat.To is not null)
        {
            foreach (var s in ShelterAlong(v, v.Boat.From, v.Boat.To))
                sb.AppendLine($"On the way to {v.Boat.To}: {s.Name} at {s.AtNm:0.#} nm.");
        }
        else if (v.Boat.Port is not null)
            foreach (var r in v.Routes.Where(r => r.From == v.Boat.Port || r.To == v.Boat.Port))
            {
                string to = r.From == v.Boat.Port ? r.To : r.From;
                foreach (var s in ShelterAlong(v, v.Boat.Port, to))
                    sb.AppendLine($"On the way to {to}: {s.Name} at {s.AtNm:0.#} nm.");
            }
        if (v.Plan is { } plan)
        {
            sb.AppendLine($"Planned: {plan.From} to {plan.To}, {plan.DistanceNm:0.0} nm on {plan.CourseDeg:000}. {plan.Estimate}");
            foreach (var s in plan.Shelters) sb.AppendLine($"  Shelter: {s.Name}, {s.AtNm:0.#} nm out, at {Position(s.EastNm, s.NorthNm)}.");
            foreach (var b in plan.Blockers) sb.AppendLine($"  Can't go yet: {b}");
        }
        if (v.Track is { } track)
        {
            sb.AppendLine($"Under way, {track.From} to {track.To}:");
            AppendFixes(sb, track);
        }
        foreach (var t in v.EarlierTracks)
        {
            int hours = t.Fixes.Count == 0 ? 0 : t.Fixes[^1].Hour;
            sb.AppendLine($"Earlier this season: day {t.Day}, {t.From} to {t.To}, {t.Outcome}, {hours} h.");
        }
        return sb.ToString();
    }

    private static void AppendFixes(StringBuilder sb, ChartTrack track)
    {
        var start = track.Fixes.Count > 0 ? track.Fixes[0] : null;
        foreach (var f in track.Fixes)
        {
            double out_ = start is null ? 0 : Math.Sqrt(Math.Pow(f.EastNm - start.EastNm, 2) + Math.Pow(f.NorthNm - start.NorthNm, 2));
            string events = f.Events.Count == 0 ? "" : " " + string.Join(" ", f.Events);
            sb.AppendLine($"  Hour {f.Hour}: {out_:0.0} nm out, {f.Mode}, at {Position(f.EastNm, f.NorthNm)}.{events}");
        }
    }

    /// <summary>Where she is on the passage: the view's own text, plus the numbers when at sea.</summary>
    public static string TripState(TripView v)
    {
        var sb = new StringBuilder();
        sb.AppendLine(v.Text);
        if (v.Destinations is { Count: > 0 }) sb.AppendLine("Routes out: " + string.Join("; ", v.Destinations) + ".");
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
        string lead;
        if (v.SeasonLead is { Length: > 0 } season) lead = season;
        else
        {
            var deadline = v.DaysToDeadline switch
            {
                0 => "that is today",
                1 => "1 day to go",
                -1 => "1 day past",
                > 1 => $"{v.DaysToDeadline} days to go",
                _ => $"{-v.DaysToDeadline} days past",
            };
            lead = $"Launch deadline: day {v.LaunchDeadlineDay} ({deadline}).";
        }
        sb.AppendLine($"Day {v.Day}. {lead} {MoneyWords.Balance(v.BalanceNok)} " +
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
        minutes >= 1440 ? $"{Duration(minutes / 1440 * 1440)} {Duration(minutes % 1440)}" :
        minutes >= 60 && minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";
}
