using Haulout.Sim.Data;
using Haulout.Sim.Components;
using Haulout.Sim.Faults;
using Haulout.Sim.Weather;

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

    private int Balance => _boat.Economy.StartingBalanceNok - _money + _incomeNok;

    private bool Afloat => !_onTheHard;

    private string EmergencyLiftLine =>
        $"She's afloat: that's below the waterline. It needs her lifted out: an 'emergency lift', {Money(_boat.Economy.EmergencyLiftNok)} and {Days(_boat.Economy.EmergencyLiftDays)}.";

    /// <summary>Do-it-yourself work on a part that sits below the waterline, or on a known problem that needs her out of the water.</summary>
    private string? AfloatRefusal(Component c) =>
        Afloat && (c.BelowWaterline || c.ActiveFaults.Any(f => f.OnHardOnly && f.Discovered)) ? EmergencyLiftLine : null;

    private string? AfloatRefusal(Fault f, Component c) =>
        Afloat && (f.OnHardOnly || c.BelowWaterline) ? EmergencyLiftLine : null;

    /// <summary>For tests only (the player never sees this): the fault has turned into its successor.</summary>
    internal bool HasEscalated(string faultId) => FaultById(faultId) is { Escalated: true };

    /// <summary>For tests only: where the fault really stands.</summary>
    internal FaultState StateOf(string faultId) => FaultById(faultId)!.State;

    private static string Days(int n) => n == 1 ? "1 day" : $"{n} days";

    /// <summary>How money is written for this boat (display only; one abstract unit). Adapters read the label here, never from the JSON.</summary>
    public Currency Currency => _boat.Currency;

    /// <summary>An amount with the currency label, the one way the sim writes money.</summary>
    private string Money(int units) => MoneyWords.Amount(units, _boat.Currency);

    private string LaunchIn()
    {
        int deadline = _launchDeadlineDay;
        int n = deadline - Day;
        return n > 0 ? $"Launch is set for day {deadline}, {Days(n)} from now."
            : n == 0 ? $"Launch is set for day {deadline}, today."
            : $"Launch was set for day {deadline}, {Days(-n)} ago.";
    }

    // ------------------------------------------------------------------ problems the player knows

    /// <summary>Discovered problems still present: the only ones a player can name.</summary>
    private IEnumerable<Fault> OpenFaults() => _boat.AllFaults.Where(f => f.Present && f.Discovered);

    /// <summary>The faults an escalated fault turned into, in order, as far as the chain goes.</summary>
    private IEnumerable<Fault> Successors(Fault f)
    {
        var seen = new List<Fault>();
        var cur = f;
        while (cur.Escalation?.To is { } to && FaultById(to) is { } next && !seen.Contains(next))
        {
            seen.Add(next);
            cur = next;
        }
        return seen;
    }

    /// <summary>
    /// A known fault that escalated while the player was away and whose successors are still unfound: to the player it has not
    /// changed. They keep seeing it (readiness, the known list, the component marker) until they find what it became.
    /// </summary>
    private bool Superseded(Fault f) => f is { Escalated: true, Discovered: true } && Successors(f).All(s => !s.Discovered);

    /// <summary>Superseded faults the player still believes are open (the thing they would be fixing has not been found fixed).</summary>
    private IEnumerable<Fault> SupersededOpen() =>
        _boat.AllFaults.Where(f => Superseded(f) && f.State != FaultState.Fixed && Successors(f).Last().State != FaultState.Fixed);

    /// <summary>Superseded faults whose successor was put right before it was ever found: to the player, those are fixed.</summary>
    private IEnumerable<Fault> SupersededFixed() =>
        _boat.AllFaults.Where(f => Superseded(f) && f.State != FaultState.Fixed && Successors(f).Last().State == FaultState.Fixed);

    /// <summary>What the player knows is open: the discovered problems still present, and the ones that have since turned into something unfound.</summary>
    private IEnumerable<Fault> KnownOpenFaults() => OpenFaults().Concat(SupersededOpen());

    /// <summary>
    /// The status list: every problem the player has found, the ones put right marked as fixed (they stay as history, as in the readiness
    /// list's "Done"). Empty once she is sold: the status says so instead.
    /// </summary>
    private List<KnownFault> KnownStatus()
    {
        if (_sold) return [];
        var superseded = SupersededFixed().ToHashSet();
        return _discovered
            .Where(k => FaultById(k.Id) is not { Escalated: true } || Superseded(FaultById(k.Id)!))
            .Select(k => FaultById(k.Id) is { } f ? k with { Fixed = f.State == FaultState.Fixed || superseded.Contains(f) } : k)
            .ToList();
    }

    private bool ResolveFault(string action, string name, out Fault fault, out CommandResult? rejected)
    {
        var open = KnownOpenFaults().ToList();
        var r = NameResolver.Resolve(open, name, f => f.Id, f => f.Name, _ => []);
        if (r.Kind == ResolutionKind.Unknown)
            r = NameResolver.Resolve(open, name, f => f.Id, f => f.Name,
                f => _boat.Get(f.ComponentId) is var c ? c.Aliases.Append(c.Name).ToList() : []);
        fault = r.Match!;
        if (r.Match is { ComponentId: Systems.Electrical.BatteryId } && IsBatteryAshore)
        {
            rejected = Reject(action, BatteryAshoreMessage);
            return false;
        }
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
        return YardGate() ?? AfloatRefusal(f, _boat.Get(f.ComponentId)) ?? OfficeOpenRefusal() ?? MoneyRefusal(yard.Nok);
    }

    private CommandResult DoYardFix(Turn t, string name)
    {
        if (!ResolveFault("yard-fix", name, out var f, out var rejected)) return rejected!;
        if (YardRefusal(f) is { } refusal) return Reject("yard-fix", refusal);
        var yard = f.Yard!;
        var comp = _boat.Get(f.ComponentId);

        ShutDownEngine(t);
        // A fault the player believes open that has since become something else: the yard puts right what is there.
        (f.Escalated ? Successors(f).Last() : f).Clear();
        var text = $"The yard takes it on: {yard.How}. {Days(yard.LeadDays)} later it's done.";
        t.Narrative.Add(text);
        return Accept(t, "yard-fix", yard.LeadDays * 1440, yard.Nok, new RepairView(comp.Name, text), $"The yard fixed: {f.Name}.", work: false);
    }

    private CommandResult DoWait(Turn t, int days)
    {
        if (AtSea) return Reject("wait", "You're at sea: continue, heave to or anchor.");
        if (days is < 1 or > MaxWaitDays) return Reject("wait", $"You can wait between 1 and {MaxWaitDays} days at a time.");
        ShutDownEngine(t);
        _weekEvents.Clear();
        int minutes = StopAtCalendar(t, days * 1440);
        days = minutes / 1440;
        // A wait cut at the hour of a beat of the friend's week is not whole days: it ends at the hour.
        if (Afloat) return DoWaitMoored(t, "wait", minutes, $"leave her for {Days(Math.Max(1, (minutes + 1439) / 1440))}",
            minutes % 1440 == 0 ? $"You leave her for {Days(days)}." : null, null);
        // A wait cut on a morning that is not a whole day on (a crewing place's first morning, design note 012, step 4) ends at that hour.
        bool whole = minutes % 1440 == 0;
        if (!whole) days = DayAt(ClockMinutes + minutes) - Day;
        t.Narrative.Add(whole ? $"You leave her for {Days(days)}." : $"You leave her until {FormatTime(ClockMinutes + minutes)} on day {DayAt(ClockMinutes + minutes)}.");
        // The days left behind: today up to the day before she is next seen (the arrival day is not counted).
        var left = Enumerable.Range(Day, days).Select(d => _weather[d]).ToList();
        if (left.Count > 0) t.Narrative.Add(WeatherWords.Summarise(left));
        return Accept(t, "wait", whole ? days * 1440 : minutes, 0, null, whole ? $"Waited {Days(days)}." : $"Waited until {FormatTime(ClockMinutes + minutes)} on day {DayAt(ClockMinutes + minutes)}.", work: false, afterTick: () =>
        {
            AddWeekLine(t);
            return null;
        });
    }

    // ------------------------------------------------------------------ escalation

    /// <param name="present">
    /// The player is aboard as it happens (an aboard tick crossing midnight, or the launch): a known predecessor then tells its
    /// escalation text and the successor is Discovered on the spot, because the player is there to see it. Away, nothing is
    /// seen: the successor wakes Latent, its cause line waits for the moment it is found, and it may show an arrival sign.
    /// </param>
    /// <param name="withCause">From the daily progression: a known fault also gets its cause line in the logbook.</param>
    private void Escalate(Turn t, Fault f, bool present, bool withCause = false)
    {
        var esc = f.Escalation;
        if (esc is null) return;
        var next = FaultById(esc.To); // usually on the same component; the frozen shackle's successor is the chain's
        if (next is null) return;
        bool known = f.State is FaultState.Discovered or FaultState.Deferred;
        f.MarkEscalated();
        next.Wake(Day);
        DrawSpread(next);
        if (!present)
        {
            _pendingCause[next.Id] = CauseLine(f, next);
            _wokenAway.Add(next);
            return;
        }
        if (!known) return; // hidden stays hidden
        int from = t.Newly.Count;
        if (esc.Text.Length > 0) t.Narrative.Add(esc.Text);
        Discover(t, next);
        if (!withCause) return;
        _log.Add(new LogEntry(Day, TimeOfDay, CauseLine(f, next)));
        _firstCauseLine ??= $"Day {Day}: {CauseLine(f, next)}";
        LogFound(t, from, Day, TimeOfDay);
    }

    private void LogFound(Turn t, int from, int day, string stamp)
    {
        for (int i = from; i < t.Newly.Count; i++)
        {
            var k = t.Newly[i];
            if (t.Causes.TryGetValue(k.Id, out var cause))
            {
                _log.Add(new LogEntry(day, stamp, cause));
                _firstCauseLine ??= $"Day {day}: {cause}";
            }
            _log.Add(new LogEntry(day, stamp, t.RuledOut.Contains(k.Id) ? $"Ruled out: {k.Name}." : $"Found: {k.Name}."));
        }
    }

    // ------------------------------------------------------------------ launch

    private CommandResult DoLaunch(Turn t)
    {
        if (YardGate() is { } away) return Reject("launch", away);
        if (Afloat) return Reject("launch", "She's already in the water.");
        if (IsBatteryAshore) return Reject("launch", "The battery is in the yard's battery room: fit it first (battery aboard).");
        if (OfficeOpenRefusal() is { } shut) return Reject("launch", shut);
        if (Today.WindForce >= _boat.Rates.WeatherGates.WindForce)
            return Reject("launch", $"The yard won't launch her in this: {WeatherWords.WindPhrase(Today)} (force {Today.WindForce}). Try another day.");

        _onTheHard = false;
        _launched = true;
        _laidUp = false;
        _foulingSpread = Between(_boat.Fouling.Spread, SeasonRoll(SaltFouling, Day, 0));
        _placeId = StartPlaceId;
        _visited.Add(_placeId);
        MarkSeen(t);
        int day = Day;

        t.Narrative.Add("The travel hoist lifts her out of the cradle and lowers her into the water.");
        t.Narrative.Add("She's in the water.");
        LaunchLayupLines(t);
        LaunchSaleLines(t);
        foreach (var f in _boat.AllFaults.Where(f => f.Present && f.Escalation is { OnLaunch: true }).ToList()) Escalate(t, f, present: true);
        RecordLaunchCover();

        int deadline = _launchDeadlineDay;
        int early = deadline - day;
        string when = early > 0 ? $"{Days(early)} before the deadline" : early == 0 ? "on the deadline" : $"{Days(-early)} after the deadline";
        var open = KnownOpenFaults().ToList();
        int deferred = open.Count(f => f.State == FaultState.Deferred);
        int others = open.Count - deferred;
        var summary = $"Launched on day {day}, {when}, with {deferred} deferred {(deferred == 1 ? "item" : "items")}";
        if (others > 0) summary += $", and {others} other known {(others == 1 ? "problem" : "problems")} still open";
        t.Narrative.Add(summary + ".");

        var view = new LaunchView(day, deadline, deferred, open.Count, t.Narrative.ToList());
        return Accept(t, "launch", _boat.Economy.LaunchMinutes, 0, view, "Launched.");
    }

    // ------------------------------------------------------------------ emergency lift

    private CommandResult DoEmergencyLift(Turn t)
    {
        if (YardGate() is { } away) return Reject("emergency-lift", away);
        if (!Afloat) return Reject("emergency-lift", "She's already ashore.");
        var economy = _boat.Economy;
        if (MoneyRefusal(economy.EmergencyLiftNok) is { } broke) return Reject("emergency-lift", broke);

        ShutDownEngine(t);
        _onTheHard = true;
        t.Narrative.Add($"The yard crane lifts her out and sets her back in the cradle. {Days(economy.EmergencyLiftDays)} later she's standing on the hard again.");
        // On or after the autumn day this is the lift for the winter: storage is billed, she is laid up, any booking lapses.
        bool autumn = IsAutumnLift();
        int storage = autumn ? _boat.Yard.StorageNok : 0;
        HauloutView? card = null;
        if (autumn)
        {
            _laidUp = true;
            _laidUpSinceDay = Day;
            if (_bookedDay is not null)
            {
                _bookedDay = null;
                t.Narrative.Add("Your haulout booking lapses: she is out already.");
            }
            card = LiftCard(t, economy.EmergencyLiftNok, storage);
        }
        int liftDay = Day;
        return Accept(t, "emergency-lift", economy.EmergencyLiftDays * 1440, economy.EmergencyLiftNok + storage, card,
            autumn ? $"Emergency lift for the winter: back on the hard, storage {Money(storage)}." : "Emergency lift: back on the hard.", work: false,
            afterTick: autumn && card is not null ? () => { WriteSeasonPage(t, liftDay); return card with { Lines = t.Narrative.ToList() }; } : null);
    }

    // ------------------------------------------------------------------ the player's own work

    /// <summary>Early jobs in a system can come out poor. Nothing is said: it surfaces later, on inspection.</summary>
    private void MaybePoorJob(Turn t, Component c)
    {
        var quality = _boat.DiyQuality;
        if (quality is null) return;
        int skill = _skill[c.Subsystem];
        // Tiredness is a named factor on the same roll (design note 012): nothing moves unless the skipper is tired.
        double factor = BotchFactor;
        if (factor > 1 && skill < quality.Threshold && quality.PoorJobs.ContainsKey(c.Subsystem)
            && !c.ActiveFaults.Any(f => f.Id.StartsWith(PoorJobPrefix, StringComparison.Ordinal)))
            t.Narrative.Add(TirednessNow == Tiredness.Exhausted ? ExhaustedJobLine : TiredJobLine);
        if (skill < quality.Threshold
            && _random.NextDouble() < quality.PoorJobChance * factor
            && !c.ActiveFaults.Any(f => f.Id.StartsWith(PoorJobPrefix, StringComparison.Ordinal))
            && quality.PoorJobs.TryGetValue(c.Subsystem, out var template))
        {
            var poor = new Fault(
                $"{PoorJobPrefix}{c.Id}-{++_poorJobs}", template.Name, c.Id, new FaultEffect(FaultEffectKind.None, 0),
                [new FaultReveal(PlayerAction.Inspect, template.Reveal, true)],
                c.HasVerb(Verb.Clean) ? [PlayerAction.Clean, PlayerAction.Replace] : [PlayerAction.Replace], // only a verb the kind offers can mend it
                onHardOnly: c.BelowWaterline, consequence: template.Consequence, costLaterNok: template.CostLaterNok,
                diy: template.Diy, yard: template.Yard);
            poor.ActivateOn(Day);
            DrawSpread(poor);
            c.AddFault(poor);
            // A botch made tired is diagnosable: its cause line, told when the player finds it, says so.
            if (factor > 1) _pendingCause[poor.Id] = $"It was tired work: you did that job {HoursAwake} hours into the day.";
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
        foreach (var f in c.ActiveFaults.Where(f => f.Discovered).Concat(SupersededOpen().Where(f => f.ComponentId == c.Id)))
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
