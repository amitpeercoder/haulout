using System.Globalization;
using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Faults;

namespace Haulout.Sim.Play;

/// <summary>
/// The player-facing facade. Commands change the world, queries only look.
/// Results carry only what the player could perceive.
/// </summary>
public sealed class Game
{
    private const int StartMinuteOfDay = 8 * 60;

    private readonly Boat _boat;
    private readonly int _startDay;
    private readonly List<LogEntry> _log = [];
    private readonly List<KnownFault> _discovered = [];
    /// <summary>Component ids the player has seen. Commands can only name what the player has found.</summary>
    private readonly HashSet<string> _seen = [];
    /// <summary>Where the player is. Starts in the cockpit.</summary>
    private string _placeId = StartPlaceId;
    private readonly HashSet<string> _visited = [];
    /// <summary>Cover ids that are currently off.</summary>
    private readonly HashSet<string> _removed = [];
    /// <summary>Component ids whose removable part (boot, cap) is pulled back.</summary>
    private readonly HashSet<string> _uncovered = [];
    /// <summary>She is on the hard for the whole slice; the ground beside the hull is only there then.</summary>
    private readonly bool _onTheHard = true;
    private readonly Reach _reach;
    private const string StartPlaceId = "cockpit";
    /// <summary>Where the exhaust outlet (the transom) can be seen from.</summary>
    private static readonly string[] ExhaustOutletPlaces = ["cockpit", "on-deck", "beside-the-hull"];
    private const string NoOutletMessage = "You can't see the exhaust outlet from down here.";
    private const string KeyMessage = "The key is on the panel in the cockpit.";
    private int _minutes;
    private int _money;
    private bool _running;
    private bool _finished;
    private bool _measureNoted;
    private readonly List<string> _tools = [];
    private StartOutcome? _lastStart;

    public Game(Boat boat, int startDay = 75)
    {
        _boat = boat ?? throw new ArgumentNullException(nameof(boat));
        _startDay = startDay;
        _reach = new Reach(boat, _removed);
    }

    public static Game NewFirstFault() => new(BoatLoader.LoadSloop28(), 75);

    // ---------------------------------------------------------------- queries

    public QueryResult Ask(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query switch
        {
            Briefing => new BriefingView(
            [
                "You bought her in March. Launch is in three weeks. The engine won't start.",
                "She stands on the hard in her cradle, grey light and a cold wind off the water.",
                "The yard hose is rigged to the raw-water intake, so the engine can run ashore.",
                "You're standing in the cockpit, the companionway hatch slid open.",
            ]),
            Status => new StatusView(
                Day, TimeOfDay, _minutes, _money, _lastStart?.ToString(), _running, _finished, _discovered.ToList(), _tools.ToList(), CurrentPlace.Name),
            Logbook => new LogView(_log.ToList()),
            Commands => new CommandList(CommandCatalogue),
            Aboard => AboardSnapshot(),
            ComponentActions q => ActionsFor(q.Component),
            _ => throw new ArgumentException($"Unknown query {query.GetType().Name}.", nameof(query)),
        };
    }

    private static readonly IReadOnlyList<CommandInfo> CommandCatalogue =
    [
        new("look", null, "Look around where you are"),
        new("go", "place", "Move to a place next to this one (1 min)"),
        new("remove", "cover", "Take off steps, a panel, a lid or floorboards"),
        new("refit", "cover", "Put a cover back"),
        new("inspect", "component", "Take a closer look at something"),
        new("uncover", "component", "Pull back a boot or cap to see underneath"),
        new("measure", "component", "Put the multimeter on something (cranking needs a remote start button)"),
        new("start", null, "Try to start the engine"),
        new("clean", "component", "Clean and tighten a part"),
        new("replace", "component", "Fit a new part"),
        new("buy", "item", "Buy a tool at the yard chandlery"),
        new("charge", "component", "Charge a battery on shore power (8 h)"),
        new("check filter", null, "Look in the fuel filter bowl"),
        new("check flow", null, "Look at the cooling water at the exhaust (engine running)"),
        new("log", null, "Read the logbook"),
        new("help", null, "List the commands"),
        new("quit", null, "Leave the boat for now"),
    ];

    private AboardView AboardSnapshot()
    {
        var here = CurrentPlace;
        var places = _boat.Places.Select(p => new AboardPlace(
            p.Name, p.Id == _placeId, _visited.Contains(p.Id), IsAvailable(p),
            p.Adjacent.Select(a => _boat.GetPlace(a).Name).ToList())).ToList();
        var covers = _boat.Covers.Select(c => new AboardCover(c.Name, _boat.GetPlace(c.PlaceId).Name, _removed.Contains(c.Id))).ToList();
        var found = _boat.Components.Where(c => _seen.Contains(c.Id))
            .Select(c => new FoundComponent(
                c.Name, _boat.GetPosition(c.PositionId).Label, c.Faults.Any(f => f.Discovered && f.Present),
                _reach.Visible(c, _placeId), _reach.Reachable(c, _placeId)))
            .ToList();
        return new AboardView(here.Name, places, covers, found);
    }

    private ComponentActionsView ActionsFor(string name)
    {
        var seen = _boat.Components.Where(c => _seen.Contains(c.Id));
        var r = NameResolver.Resolve(seen, name, c => c.Id, c => c.Name, c => c.Aliases);
        if (r.Match is not { } c) return new ComponentActionsView(name, false, null, []);

        bool hasButton = _tools.Contains(Shop.RemoteStartButton);
        string? sight = _reach.Refusal(c, _placeId, false);
        string? touch = _reach.Refusal(c, _placeId, true);
        var options = new List<ActionOption>();
        void Add(string verb, string label, string? reason, int minutes, int cost) =>
            options.Add(new(verb, label, reason is null, reason, minutes, cost));

        Add("inspect", "Inspect", sight, 5, 0);

        if (c.RemovablePart is { } part)
            Add("uncover", $"Pull back the {part.Name}",
                touch ?? (_uncovered.Contains(c.Id) ? AlreadyUncovered(part) : null), 2, 0);

        if (c.Id == "start-battery" || c.Id == "glow-plugs")
            Add("measure", "Measure", touch, 10, 0);
        else if (EngineInputsBuilder.LoopOrder.Contains(c.Id))
            Add("measure", "Measure", touch ?? (hasButton ? null : "Needs someone to crank while you hold the probes: a remote start button."), 10, 0);

        if (c.Cleanable) Add("clean", "Clean", touch, 30, 0);
        if (c.ReplaceCostNok > 0) Add("replace", "Replace", touch, c.ReplaceMinutes, c.ReplaceCostNok);
        if (c.Id == "start-battery") Add("charge", "Charge", touch, 480, 0);
        if (c.Id == "primary-filter") Add("check-filter-bowl", "Check filter bowl", touch, 10, 0);
        if (RawWaterParts.Contains(c.Id))
            Add("check-raw-water-flow", "Check flow",
                !ExhaustOutletPlaces.Contains(_placeId) ? NoOutletMessage
                : _running ? null : "Only tells you anything with the engine running.", 5, 0);

        return new ComponentActionsView(name, true, c.Name, options);
    }

    /// <summary>Parts on the raw-water side, where watching the exhaust flow is the natural check.</summary>
    private static readonly string[] RawWaterParts =
        ["raw-water-seacock", "raw-water-strainer", "raw-water-pump", "impeller", "exhaust-elbow", "exhaust-hose"];

    // --------------------------------------------------------------- commands

    public CommandResult Execute(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var turn = new Turn();
        return command switch
        {
            Look => DoLook(turn),
            GoTo c => DoGoTo(turn, c.Place),
            Remove c => DoCover(turn, c.Cover, remove: true),
            Refit c => DoCover(turn, c.Cover, remove: false),
            Inspect c => DoInspect(turn, c.Component),
            Measure c => DoMeasure(turn, c.Component),
            StartEngine => DoStart(turn),
            Clean c => DoClean(turn, c.Component),
            Uncover c => DoUncover(turn, c.Component),
            Replace c => DoReplace(turn, c.Component),
            Charge c => DoCharge(turn, c.Component),
            Buy c => DoBuy(turn, c.Item),
            CheckFilterBowl => DoCheckFilterBowl(turn),
            CheckRawWaterFlow => DoCheckFlow(turn),
            _ => throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command)),
        };
    }

    private sealed class Turn
    {
        public List<string> Narrative { get; } = [];
        public List<KnownFault> Newly { get; } = [];
    }

    private Place CurrentPlace => _boat.GetPlace(_placeId);

    private bool IsAvailable(Place p) => !p.OnlyOnTheHard || _onTheHard;

    private List<Place> Exits(Place p) =>
        p.Adjacent.Select(_boat.GetPlace).Where(IsAvailable).ToList();

    private string ExitsLine(Place p) =>
        "From here you can go " + Words.JoinOr(Exits(p).Select(e => e.Way).ToList()) + ".";

    private string SeeLine(Place p)
    {
        var names = _reach.VisibleAt(p.Id).Select(c => c.Name).ToList();
        return names.Count == 0 ? "Nothing here worth naming." : "You can see: " + string.Join(", ", names) + ".";
    }

    private PlaceView PlaceViewOf(Place p) => new(
        p.Name, p.Description,
        _boat.Covers.Where(c => c.PlaceId == p.Id).Select(c => new CoverState(c.Name, _removed.Contains(c.Id))).ToList(),
        _reach.VisibleAt(p.Id).Select(c => c.Name).ToList(),
        _reach.ReachableAt(p.Id).Select(c => c.Name).ToList(),
        Exits(p).Select(e => e.Name).ToList());

    /// <summary>Whatever is in view from the current place becomes nameable. Never un-sees.</summary>
    private void MarkSeen(Turn t)
    {
        foreach (var c in _reach.VisibleAt(_placeId))
        {
            if (!_seen.Add(c.Id)) continue;
            foreach (var f in c.ActiveFaults.ToList()) Reveal(t, f, PlayerAction.Look);
        }
    }

    private CommandResult DoLook(Turn t)
    {
        var place = CurrentPlace;
        _visited.Add(place.Id);
        MarkSeen(t);
        t.Narrative.Add(place.Description);
        foreach (var cover in _boat.Covers.Where(c => c.PlaceId == place.Id))
            t.Narrative.Add($"The {Words.CoverName(cover)} {Words.Is(cover)} {(_removed.Contains(cover.Id) ? "off" : "in place")}.");
        t.Narrative.Add(SeeLine(place));
        t.Narrative.Add(ExitsLine(place));
        return Accept(t, "look", 2, 0, PlaceViewOf(place), $"Looked around {Words.The(place)}.");
    }

    private CommandResult DoGoTo(Turn t, string name)
    {
        var r = NameResolver.Resolve(_boat.Places, name, p => p.Id, p => p.Name, p => p.Aliases);
        if (r.Match is not { } target)
            return r.Kind == ResolutionKind.Ambiguous
                ? RejectResolution("go", r, name, p => p.Name)
                : Reject("go", $"There's no place aboard called '{name.Trim()}'.");

        if (target.Id == _placeId) return Reject("go", $"You're already {Words.In(target)}.");
        if (!IsAvailable(target)) return Reject("go", $"You can't go {Words.To(target)} while she's afloat.");
        if (!CurrentPlace.Adjacent.Contains(target.Id))
            return Reject("go", $"You can't get {Words.To(target)} from here. {ExitsLine(CurrentPlace)}");

        _placeId = target.Id;
        _visited.Add(target.Id);
        MarkSeen(t);
        t.Narrative.Add(target.Description);
        t.Narrative.Add(SeeLine(target));
        return Accept(t, "go", 1, 0, PlaceViewOf(target), $"Went {target.Way}.");
    }

    private CommandResult DoCover(Turn t, string name, bool remove)
    {
        string action = remove ? "remove" : "refit";
        var r = NameResolver.Resolve(_boat.Covers, name, c => c.Id, c => c.Name, c => c.Aliases);
        if (r.Match is not { } cover)
            return r.Kind == ResolutionKind.Ambiguous
                ? RejectResolution(action, r, name, c => c.Name)
                : Reject(action, $"There's nothing called '{name.Trim()}' to take {(remove ? "off" : "put back")}.");

        var label = Words.CoverName(cover);
        var coverPlace = _boat.GetPlace(cover.PlaceId);
        if (cover.PlaceId != _placeId)
            return Reject(action, $"The {label} {Words.Is(cover)} {Words.In(coverPlace)}.");
        bool off = _removed.Contains(cover.Id);
        if (remove && off) return Reject(action, $"The {label} {Words.Is(cover)} already off.");
        if (!remove && !off) return Reject(action, $"The {label} {Words.Is(cover)} already in place.");

        var before = _reach.VisibleAt(_placeId).Select(c => c.Id).ToHashSet();
        if (remove) _removed.Add(cover.Id); else _removed.Remove(cover.Id);
        var newlyVisible = remove
            ? _reach.VisibleAt(_placeId).Where(c => !before.Contains(c.Id)).Select(c => c.Name).ToList()
            : [];
        MarkSeen(t);

        t.Narrative.Add(remove ? cover.RemoveText : cover.RefitText);
        if (newlyVisible.Count > 0) t.Narrative.Add("Behind it: " + string.Join(", ", newlyVisible) + ".");
        return Accept(t, action, remove ? cover.RemoveMinutes : cover.RefitMinutes, 0,
            new CoverView(cover.Name, coverPlace.Name, remove, newlyVisible),
            remove ? $"Took off the {label}." : $"Put back the {label}.");
    }

    private CommandResult DoInspect(Turn t, string name)
    {
        if (!ResolveComponent("inspect", name, needTouch: false, out var c, out var rejected)) return rejected!;

        bool off = _uncovered.Contains(c.Id);
        t.Narrative.Add(c.Description + " " + WearPhrase(c.Condition));
        if (off && c.RemovablePart is { UncoveredText.Length: > 0 } part) t.Narrative.Add(part.UncoveredText);
        foreach (var f in c.ActiveFaults.ToList())
        {
            Reveal(t, f, PlayerAction.Inspect, uncovered: off);
            if (f.Discovered) t.Narrative.Add($"You know what this is: {f.Name}.");
        }
        var view = new InspectionView(c.Name, string.Join(" ", t.Narrative));
        return Accept(t, "inspect", 5, 0, view, $"Inspected the {c.Name.ToLowerInvariant()}.");
    }

    private CommandResult DoMeasure(Turn t, string name)
    {
        if (!ResolveComponent("measure", name, needTouch: true, out var c, out var rejected)) return rejected!;

        var inputs = EngineInputsBuilder.Build(_boat);
        var reading = EngineModel.Attempt(inputs).Electrical;
        bool hasButton = _tools.Contains(Shop.RemoteStartButton);
        bool needsCranking = c.Id == EngineInputsBuilder.StarterId || reading.DropVolts.ContainsKey(c.Id);
        if (needsCranking && !hasButton) return Reject("measure", NeedButtonMessage);
        var ci = CultureInfo.InvariantCulture;
        MeterReading meter;
        string log;

        if (hasButton && !_measureNoted && (needsCranking || c.Id == "start-battery"))
        {
            t.Narrative.Add("You clip the remote start button onto the solenoid and pull the stop, so she won't fire. Probes on, you press the button.");
            _measureNoted = true;
        }

        if (c.Id == "start-battery" && !hasButton)
        {
            double ocv = inputs.Electrical.BatteryOpenCircuitVolts;
            meter = new MeterReading(c.Name, "volts-resting", ocv, "V", $"Resting at {ocv.ToString("F2", ci)} V.");
            log = $"Measured the start battery: {ocv.ToString("F2", ci)} V resting.";
            t.Narrative.Add(meter.Text);
            t.Narrative.Add(NeedButtonMessage);
            foreach (var f in c.ActiveFaults.ToList()) Reveal(t, f, PlayerAction.Measure);
            return Accept(t, "measure", 10, 0, meter, log);
        }

        if (c.Id == "start-battery")
        {
            double ocv = inputs.Electrical.BatteryOpenCircuitVolts;
            double cranking = reading.BatteryTerminalVolts;
            string text = reading.CircuitClosed
                ? $"Resting at {ocv.ToString("F2", ci)} V; it dips to {cranking.ToString("F2", ci)} V while cranking."
                : $"Resting at {ocv.ToString("F2", ci)} V. Nothing flows when you crank, so it does not dip.";
            meter = new MeterReading(c.Name, "volts-resting", ocv, "V", text, cranking);
            log = $"Measured the start battery: {ocv.ToString("F2", ci)} V resting, {cranking.ToString("F2", ci)} V cranking.";
        }
        else if (c.Id == EngineInputsBuilder.StarterId)
        {
            double v = reading.DropVolts[c.Id];
            meter = new MeterReading(c.Name, "volts-at-starter", v, "V", $"{v.ToString("F2", ci)} V at the starter while cranking.");
            log = $"Measured at the starter while cranking: {v.ToString("F2", ci)} V.";
        }
        else if (reading.DropVolts.TryGetValue(c.Id, out var drop))
        {
            meter = new MeterReading(c.Name, "volts-drop", drop, "V", $"{drop.ToString("F2", ci)} V lost across the {c.Name.ToLowerInvariant()} while cranking.");
            log = $"Measured across the {c.Name.ToLowerInvariant()} while cranking: {drop.ToString("F2", ci)} V.";
        }
        else if (c.Id == "glow-plugs")
        {
            bool dead = c.Condition < 0.15 || c.ActiveFaults.Any(f => f.Effect.Kind == FaultEffectKind.OpenCircuit);
            if (dead)
            {
                meter = new MeterReading(c.Name, "ohms", null, "ohm", "OL: no circuit through the glow plug.");
                log = "Measured a glow plug: OL.";
            }
            else
            {
                double ohms = c.Properties.GetValueOrDefault("healthyOhms");
                meter = new MeterReading(c.Name, "ohms", ohms, "ohm", $"{ohms.ToString("F2", ci)} ohm across a glow plug.");
                log = $"Measured a glow plug: {ohms.ToString("F2", ci)} ohm.";
            }
        }
        else
        {
            t.Narrative.Add("Nothing there a multimeter can tell you.");
            foreach (var f in c.ActiveFaults.ToList()) Reveal(t, f, PlayerAction.Measure);
            return Accept(t, "measure", 2, 0, null, $"Tried the multimeter on the {c.Name.ToLowerInvariant()}: nothing to read.");
        }

        t.Narrative.Add(meter.Text);
        foreach (var f in c.ActiveFaults.ToList()) Reveal(t, f, PlayerAction.Measure);
        return Accept(t, "measure", 10, 0, meter, log);
    }

    private CommandResult DoStart(Turn t)
    {
        if (_placeId != "cockpit") return Reject("start", KeyMessage);
        if (_running) return Reject("start", "The engine is already running.");

        var result = EngineModel.Attempt(EngineInputsBuilder.Build(_boat));
        _lastStart = result.Outcome;
        t.Narrative.Add(result.Description);
        _running = result.Outcome is StartOutcome.RunsRough or StartOutcome.Runs;

        const int minutes = 2;
        if (result.Outcome == StartOutcome.Runs && !result.OverheatWarning && !_finished)
        {
            _finished = true;
            int total = _minutes + minutes;
            t.Narrative.Add("She'll do. Launch is in three weeks.");
            t.Narrative.Add($"Time spent: {total / 60} h {total % 60} min. Money spent: {_money} NOK.");
        }
        return Accept(t, "start", minutes, 0, new StartAttemptView(result.Outcome.ToString(), result.Description, result.LabouredCrank),
            $"Tried to start the engine. Result: {result.Outcome}.");
    }

    private static string AlreadyUncovered(RemovablePart part) => $"The {part.Name} is already pulled back.";

    private CommandResult DoUncover(Turn t, string name)
    {
        if (!ResolveComponent("uncover", name, needTouch: true, out var c, out var rejected)) return rejected!;
        if (c.RemovablePart is not { } part) return Reject("uncover", $"There's nothing to pull back on the {c.Name.ToLowerInvariant()}.");
        if (!_uncovered.Add(c.Id)) return Reject("uncover", AlreadyUncovered(part));

        t.Narrative.Add($"You peel the {part.Name} back off the clamp.");
        if (part.UncoveredText.Length > 0) t.Narrative.Add(part.UncoveredText);
        return Accept(t, "uncover", 2, 0, null, $"Pulled back the {part.Name} on the {c.Name.ToLowerInvariant()}.");
    }

    private CommandResult DoClean(Turn t, string name)
    {
        if (!ResolveComponent("clean", name, needTouch: true, out var c, out var rejected)) return rejected!;
        if (!c.Cleanable) return Reject("clean", "Nothing to clean there.");

        StopEngine(t);
        bool bootWasOff = _uncovered.Remove(c.Id);
        var fixedFaults = c.ActiveFaults.Where(f => f.FixedBy.Contains(PlayerAction.Clean)).ToList();
        string text;
        if (fixedFaults.Count == 0)
        {
            text = "Cleaned and tightened. It looked fine before, too.";
            t.Narrative.Add(text);
        }
        else
        {
            foreach (var f in fixedFaults)
            {
                Reveal(t, f, PlayerAction.Clean, forceDiscover: true);
                f.Clear();
            }
            t.Narrative.Add("Cleaned and tightened. It looks right now.");
            text = string.Join(" ", t.Narrative);
        }
        if (bootWasOff && c.RemovablePart is { } part)
        {
            var refit = $"You push the {part.Name} back over the clamp.";
            t.Narrative.Add(refit);
            text += " " + refit;
        }
        return Accept(t, "clean", 30, 0, new RepairView(c.Name, text), $"Cleaned the {c.Name.ToLowerInvariant()}.");
    }

    private CommandResult DoReplace(Turn t, string name)
    {
        if (!ResolveComponent("replace", name, needTouch: true, out var c, out var rejected)) return rejected!;
        if (c.ReplaceCostNok <= 0) return Reject("replace", $"The {c.Name.ToLowerInvariant()} is not something you can replace in the yard this spring.");

        StopEngine(t);
        c.Replace();
        _uncovered.Remove(c.Id);
        var text = $"You fit a new {c.Name.ToLowerInvariant()}.";
        t.Narrative.Add(text);
        return Accept(t, "replace", c.ReplaceMinutes, c.ReplaceCostNok, new RepairView(c.Name, text),
            $"Replaced the {c.Name.ToLowerInvariant()}.");
    }

    private CommandResult DoCharge(Turn t, string name)
    {
        if (!ResolveComponent("charge", name, needTouch: true, out var c, out var rejected)) return rejected!;
        if (c.Id != "start-battery") return Reject("charge", "Only a battery takes a charge.");

        StopEngine(t);
        foreach (var f in c.ActiveFaults.Where(f => f.FixedBy.Contains(PlayerAction.Charge)).ToList()) f.Clear();
        var text = "You plug in the shore-power charger and wait out eight hours. By the end of it the battery is full.";
        t.Narrative.Add(text);
        return Accept(t, "charge", 480, 0, new RepairView(c.Name, text), "Charged the start battery for eight hours.");
    }

    private const string NeedButtonMessage =
        "You can't crank and hold the probes at once. A remote start button from the chandlery would let you.";

    private CommandResult DoBuy(Turn t, string item)
    {
        var shopItem = Shop.Find(item);
        if (shopItem is null) return Reject("buy", $"The chandlery doesn't have '{item.Trim()}'.");
        if (_tools.Contains(shopItem.Name)) return Reject("buy", "You already have one.");

        _tools.Add(shopItem.Name);
        t.Narrative.Add($"You walk over to the yard chandlery and buy a {shopItem.Name}.");
        return Accept(t, "buy", shopItem.Minutes, shopItem.CostNok, null, shopItem.LogText);
    }

    private CommandResult DoCheckFilterBowl(Turn t)
    {
        var filter = _boat.Get("primary-filter");
        if (!_seen.Contains(filter.Id)) return Reject("check-filter-bowl", "You haven't found a fuel filter yet.");
        if (_reach.Refusal(filter, _placeId, needTouch: true) is { } refusal) return Reject("check-filter-bowl", refusal);
        var before = t.Narrative.Count;
        foreach (var f in filter.ActiveFaults.ToList()) Reveal(t, f, PlayerAction.CheckFilterBowl);
        if (t.Narrative.Count == before) t.Narrative.Add("Clear, pale diesel. No water at the bottom.");
        return Accept(t, "check-filter-bowl", 10, 0, new FilterBowlView(string.Join(" ", t.Narrative)), "Checked the fuel filter bowl.");
    }

    private CommandResult DoCheckFlow(Turn t)
    {
        if (!ExhaustOutletPlaces.Contains(_placeId)) return Reject("check-raw-water-flow", NoOutletMessage);
        if (!_running)
        {
            const string idle = "The engine isn't running. Nothing comes out of the exhaust.";
            t.Narrative.Add(idle);
            return Accept(t, "check-raw-water-flow", 5, 0, new RawWaterFlowView(idle), "Checked the cooling water: engine not running.");
        }

        double flow = EngineInputsBuilder.Build(_boat).Cooling.RawWaterFlow;
        t.Narrative.Add(flow >= 0.8 ? "Water leaves the exhaust in strong, steady spurts."
            : flow >= 0.25 ? "The water at the exhaust is weaker than you'd like."
            : "Almost nothing comes out of the exhaust.");
        foreach (var f in _boat.AllFaults.Where(f => f.Present).ToList()) Reveal(t, f, PlayerAction.CheckRawWaterFlow);
        return Accept(t, "check-raw-water-flow", 5, 0, new RawWaterFlowView(string.Join(" ", t.Narrative)), "Checked the cooling water at the exhaust.");
    }

    // ---------------------------------------------------------------- helpers

    private int Day => _startDay + (StartMinuteOfDay + _minutes) / 1440;
    private string TimeOfDay => FormatTime(_minutes);

    private static string FormatTime(int minutesSpent)
    {
        int m = (StartMinuteOfDay + minutesSpent) % 1440;
        return $"{m / 60:00}:{m % 60:00}";
    }

    private static string WearPhrase(double condition) =>
        condition >= 0.8 ? "In good shape."
        : condition >= 0.5 ? "Worn but serviceable."
        : condition >= 0.3 ? "Tired."
        : "In poor shape.";

    private void StopEngine(Turn t)
    {
        if (!_running) return;
        _running = false;
        t.Narrative.Add("You shut the engine down first.");
    }

    /// <summary>Shows the fault's reveal text for the action and records a discovery if it has one.</summary>
    private void Reveal(Turn t, Fault f, PlayerAction action, bool forceDiscover = false, bool uncovered = false)
    {
        var reveal = f.RevealFor(action, uncovered);
        if (reveal is not null) t.Narrative.Add(reveal.Text);
        if ((reveal?.Discovers ?? false) || forceDiscover) Discover(t, f);
    }

    private void Discover(Turn t, Fault f)
    {
        if (f.Discovered) return;
        f.Discover();
        var known = new KnownFault(f.Id, f.Name, _boat.Get(f.ComponentId).Name);
        _discovered.Add(known);
        t.Newly.Add(known);
    }

    private bool ResolveComponent(string action, string name, bool needTouch, out Component component, out CommandResult? rejected)
    {
        // Only components the player has seen are nameable, so a refusal never confirms an unseen part exists.
        var seen = _boat.Components.Where(c => _seen.Contains(c.Id));
        var r = NameResolver.Resolve(seen, name, c => c.Id, c => c.Name, c => c.Aliases);
        component = r.Match!;
        if (r.Match is null)
        {
            rejected = r.Kind == ResolutionKind.Ambiguous
                ? RejectResolution(action, r, name, c => c.Name)
                : Reject(action, $"You haven't found anything called '{name.Trim()}' yet.");
            return false;
        }
        var refusal = _reach.Refusal(r.Match, _placeId, needTouch);
        rejected = refusal is null ? null : Reject(action, refusal);
        return refusal is null;
    }

    private CommandResult RejectResolution<T>(string action, Resolution<T> r, string name, Func<T, string> label) where T : class =>
        r.Kind == ResolutionKind.Ambiguous
            ? Reject(action, $"'{name.Trim()}' could be: {string.Join(", ", r.Candidates.Select(label))}. Be more specific.")
            : Reject(action, $"You can't find '{name.Trim()}' on this boat.");

    private CommandResult Reject(string action, string message) =>
        new(false, action, [message], 0, 0, null, [], _running, _finished);

    private CommandResult Accept(Turn t, string action, int minutes, int costNok, Observation? obs, string logText)
    {
        var stamp = FormatTime(_minutes);
        int day = Day;
        _log.Add(new LogEntry(day, stamp, logText, costNok));
        foreach (var k in t.Newly) _log.Add(new LogEntry(day, stamp, $"Found: {k.Name}."));
        _minutes += minutes;
        _money += costNok;
        return new CommandResult(true, action, t.Narrative.ToList(), minutes, costNok, obs, t.Newly.ToList(), _running, _finished);
    }
}
