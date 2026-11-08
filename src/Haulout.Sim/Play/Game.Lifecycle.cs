using Haulout.Sim.Components;
using Haulout.Sim.Faults;

namespace Haulout.Sim.Play;

/// <summary>Fault lifecycle: deferral, the yard, waiting, escalation, launch, money, and the quality of the player's own work.</summary>
public sealed partial class Game
{
    /// <summary>The longest single wait the facade accepts.</summary>
    private const int MaxWaitDays = 60;

    private bool _launched;
    private readonly Dictionary<Subsystem, int> _skill = [];
    private int _poorJobs;
    /// <summary>Per fault id: the distinct days on which the player drained the bowl while the fault was present.</summary>
    private readonly Dictionary<string, HashSet<int>> _drains = [];

    private void InitSkills()
    {
        int start = _boat.DiyQuality?.StartSkill ?? 0;
        foreach (var s in Enum.GetValues<Subsystem>()) _skill[s] = start;
    }

    // ------------------------------------------------------------------ money and haul state

    private int Balance => _boat.Economy.StartingBalanceNok - _money;

    private string? MoneyRefusal(int costNok) =>
        costNok > Balance ? $"That's {costNok} NOK and you have {Balance} NOK left." : null;

    private bool Afloat => !_onTheHard;

    private string EmergencyLiftLine =>
        $"She's afloat: that's below the waterline. It needs her lifted out: an emergency lift, about {_boat.Economy.EmergencyLiftNok} NOK and a yard slot.";

    /// <summary>Do-it-yourself work on a part that sits below the waterline, or on a known problem that needs her out of the water.</summary>
    private string? AfloatRefusal(Component c) =>
        Afloat && (c.BelowWaterline || c.ActiveFaults.Any(f => f.OnHardOnly && f.Discovered)) ? EmergencyLiftLine : null;

    private string? AfloatRefusal(Fault f, Component c) =>
        Afloat && (f.OnHardOnly || c.BelowWaterline) ? EmergencyLiftLine : null;

    private static string Days(int n) => n == 1 ? "1 day" : $"{n} days";

    private string LaunchIn()
    {
        int deadline = _boat.Economy.LaunchDeadlineDay;
        int n = deadline - Day;
        return n > 0 ? $"Launch is set for day {deadline}, {Days(n)} from now."
            : n == 0 ? $"Launch is set for day {deadline}, today."
            : $"Launch was set for day {deadline}, {Days(-n)} ago.";
    }

    // ------------------------------------------------------------------ problems the player knows

    /// <summary>Discovered problems still present: the only ones a player can name.</summary>
    private IEnumerable<Fault> OpenFaults() => _boat.AllFaults.Where(f => f.Present && f.Discovered);

    private bool ResolveFault(string action, string name, out Fault fault, out CommandResult? rejected)
    {
        var open = OpenFaults().ToList();
        var r = NameResolver.Resolve(open, name, f => f.Id, f => f.Name, _ => []);
        if (r.Kind == ResolutionKind.Unknown)
            r = NameResolver.Resolve(open, name, f => f.Id, f => f.Name,
                f => _boat.Get(f.ComponentId) is var c ? c.Aliases.Append(c.Name).ToList() : []);
        fault = r.Match!;
        if (r.Match is not null) { rejected = null; return true; }
        rejected = r.Kind == ResolutionKind.Ambiguous
            ? RejectResolution(action, r, name, f => f.Name)
            : Reject(action, $"You don't know of a problem called '{name.Trim()}'.");
        return false;
    }

    private CommandResult DoDefer(Turn t, string name)
    {
        if (!ResolveFault("defer", name, out var f, out var rejected)) return rejected!;
        if (f.State == FaultState.Deferred) return Reject("defer", $"{f.Name} is already deferred.");
        f.Defer(Day);
        t.Narrative.Add($"You put it off for now: {f.Name}. It stays on the list, and it keeps ageing.");
        return Accept(t, "defer", 0, 0, null, $"Deferred: {f.Name}.");
    }

    private CommandResult DoUndefer(Turn t, string name)
    {
        if (!ResolveFault("undefer", name, out var f, out var rejected)) return rejected!;
        if (f.State != FaultState.Deferred) return Reject("undefer", $"{f.Name} isn't deferred.");
        f.Undefer();
        t.Narrative.Add($"You take it back onto the list: {f.Name}.");
        return Accept(t, "undefer", 0, 0, null, $"Back on the list: {f.Name}.");
    }

    private string? YardRefusal(Fault f)
    {
        if (f.Yard is not { } yard) return "The yard can't take that on.";
        return AfloatRefusal(f, _boat.Get(f.ComponentId)) ?? MoneyRefusal(yard.Nok);
    }

    private CommandResult DoYardFix(Turn t, string name)
    {
        if (!ResolveFault("yard-fix", name, out var f, out var rejected)) return rejected!;
        if (YardRefusal(f) is { } refusal) return Reject("yard-fix", refusal);
        var yard = f.Yard!;
        var comp = _boat.Get(f.ComponentId);

        StopEngine(t);
        f.Clear();
        var text = $"The yard takes it on: {yard.How}. {Days(yard.LeadDays)} later it's done.";
        t.Narrative.Add(text);
        return Accept(t, "yard-fix", yard.LeadDays * 1440, yard.Nok, new RepairView(comp.Name, text), $"The yard fixed: {f.Name}.", work: false);
    }

    private CommandResult DoWait(Turn t, int days)
    {
        if (days is < 1 or > MaxWaitDays) return Reject("wait", $"You can wait between 1 and {MaxWaitDays} days at a time.");
        t.Narrative.Add($"You leave her for {Days(days)}.");
        return Accept(t, "wait", days * 1440, 0, null, $"Waited {Days(days)}.", work: false);
    }

    // ------------------------------------------------------------------ escalation

    private void RunClockEscalations(Turn t)
    {
        int today = Day;
        var due = _boat.AllFaults.Where(f =>
            f is { State: FaultState.Deferred, Present: true, DeferredDay: { } since }
            && f.Escalation is { AfterDeferredDays: { } n } && today - since >= n).ToList();
        foreach (var f in due) Escalate(t, f);
    }

    private void Escalate(Turn t, Fault f)
    {
        var esc = f.Escalation;
        if (esc is null) return;
        var next = _boat.Get(f.ComponentId).Faults.FirstOrDefault(x => x.Id == esc.To);
        if (next is null) return;
        bool known = f.State is FaultState.Discovered or FaultState.Deferred;
        f.MarkEscalated();
        next.Wake();
        if (!known) return; // hidden stays hidden
        if (esc.Text.Length > 0) t.Narrative.Add(esc.Text);
        Discover(t, next);
    }

    private void LogFound(Turn t, int from, int day, string stamp)
    {
        for (int i = from; i < t.Newly.Count; i++)
        {
            var k = t.Newly[i];
            _log.Add(new LogEntry(day, stamp, t.RuledOut.Contains(k.Id) ? $"Ruled out: {k.Name}." : $"Found: {k.Name}."));
        }
    }

    // ------------------------------------------------------------------ launch

    private CommandResult DoLaunch(Turn t)
    {
        if (_launched) return Reject("launch", "She's already in the water.");

        _onTheHard = false;
        _launched = true;
        _placeId = StartPlaceId;
        _visited.Add(_placeId);
        MarkSeen(t);
        int day = Day;

        t.Narrative.Add("The travel hoist lifts her out of the cradle and lowers her into the water.");
        t.Narrative.Add("She's in the water.");
        foreach (var f in _boat.AllFaults.Where(f => f.Present && f.Escalation is { OnLaunch: true }).ToList()) Escalate(t, f);

        int deadline = _boat.Economy.LaunchDeadlineDay;
        int early = deadline - day;
        string when = early > 0 ? $"{Days(early)} before the deadline" : early == 0 ? "on the deadline" : $"{Days(-early)} after the deadline";
        var open = OpenFaults().ToList();
        int deferred = open.Count(f => f.State == FaultState.Deferred);
        int others = open.Count - deferred;
        var summary = $"Launched on day {day}, {when}, with {deferred} deferred {(deferred == 1 ? "item" : "items")}";
        if (others > 0) summary += $", and {others} other known {(others == 1 ? "problem" : "problems")} still open";
        t.Narrative.Add(summary + ".");

        var view = new LaunchView(day, deadline, deferred, open.Count, t.Narrative.ToList());
        return Accept(t, "launch", _boat.Economy.LaunchMinutes, 0, view, "Launched.");
    }

    // ------------------------------------------------------------------ the player's own work

    private bool CanRedoByClean(Component c) =>
        c.ActiveFaults.Any(f => f.Discovered && f.FixedBy.Contains(PlayerAction.Clean));

    /// <summary>Early jobs in a system can come out poor. Nothing is said: it surfaces later, on inspection.</summary>
    private void MaybePoorJob(Component c)
    {
        var quality = _boat.DiyQuality;
        if (quality is null) return;
        int skill = _skill[c.Subsystem];
        if (skill < quality.Threshold
            && _random.NextDouble() < quality.PoorJobChance
            && !c.ActiveFaults.Any(f => f.Id.StartsWith(PoorJobPrefix, StringComparison.Ordinal))
            && quality.PoorJobs.TryGetValue(c.Subsystem, out var template))
        {
            c.AddFault(new Fault(
                $"{PoorJobPrefix}{c.Id}-{++_poorJobs}", template.Name, c.Id, new FaultEffect(FaultEffectKind.None, 0),
                [new FaultReveal(PlayerAction.Inspect, template.Reveal, true)],
                [PlayerAction.Clean, PlayerAction.Replace],
                onHardOnly: c.BelowWaterline, consequence: template.Consequence, costLaterNok: template.CostLaterNok,
                diy: template.Diy, yard: template.Yard));
        }
        _skill[c.Subsystem] = skill + 1;
    }

    private const string PoorJobPrefix = "poor-job-";

    // ------------------------------------------------------------------ water in the fuel

    private const string DrainProperty = "diyDrainDays";

    private void BowlCheck(Turn t, Fault f)
    {
        if (!f.Properties.TryGetValue(DrainProperty, out var need))
        {
            Reveal(t, f, PlayerAction.CheckFilterBowl);
            return;
        }
        if (!_drains.TryGetValue(f.Id, out var days)) _drains[f.Id] = days = [];
        if (days.Count >= need)
        {
            t.Narrative.Add("Clear diesel at the bottom now.");
            return;
        }
        days.Add(Day);
        if (days.Count >= need)
        {
            t.Narrative.Add("Only a trace of water this time.");
            Discover(t, f);
        }
        else Reveal(t, f, PlayerAction.CheckFilterBowl);
    }

    /// <summary>A new primary filter element after the bowl has been drained enough days finishes the job.</summary>
    private string? FixDrainedWater()
    {
        string? line = null;
        foreach (var f in _boat.AllFaults.Where(f => f.Present && f.Properties.ContainsKey(DrainProperty)).ToList())
        {
            if (!_drains.TryGetValue(f.Id, out var days) || days.Count < f.Properties[DrainProperty]) continue;
            f.Clear();
            line = "With the water drained and a new element in, the fuel runs clean.";
        }
        return line;
    }

    // ------------------------------------------------------------------ options on a component

    private void AddProblemOptions(Component c, List<ActionOption> options)
    {
        foreach (var f in c.ActiveFaults.Where(f => f.Discovered))
        {
            bool deferred = f.State == FaultState.Deferred;
            options.Add(deferred
                ? new ActionOption("undefer", $"Undefer: {f.Name}", true, null, 0, 0, f.Name)
                : new ActionOption("defer", $"Defer: {f.Name}", true, null, 0, 0, f.Name));
            if (f.Yard is { } yard)
            {
                var reason = YardRefusal(f);
                options.Add(new ActionOption(
                    "yard-fix", $"Yard fix ({Days(yard.LeadDays)})", reason is null, reason, yard.LeadDays * 1440, yard.Nok, f.Name));
            }
        }
    }
}
