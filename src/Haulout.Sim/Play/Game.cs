using System.Globalization;
using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Faults;
using Haulout.Sim.Systems;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The player-facing facade. Commands change the world, queries only look.
/// Results carry only what the player could perceive.
/// </summary>
public sealed partial class Game
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
    private bool _onTheHard = true;
    private readonly Reach _reach;
    /// <summary>Components that are not aboard: the start battery in the yard's battery room.</summary>
    private readonly HashSet<string> _gone = [];
    private const string StartPlaceId = "cockpit";
    private const string BelowPlaceId = "companionway";
    private const string HatchId = "companionway-hatch";
    /// <summary>Where the exhaust outlet (the transom) can be seen from.</summary>
    private static readonly string[] ExhaustOutletPlaces = ["cockpit", "on-deck", "beside-the-hull"];
    private const string NoOutletMessage = "You can't see the exhaust outlet from down here.";
    private const string KeyMessage = "The key is on the panel in the cockpit.";
    /// <summary>The one clock. Written only by <see cref="Tick"/>.</summary>
    private readonly SimTime _time = new();
    private int _money;
    private bool _running;
    private bool _finished;
    private bool _measureNoted;
    /// <summary>The cranking readings the player was shown on <see cref="_meterDay"/>: drops by component id, and the starter's voltage (002 finding 4).</summary>
    private readonly Dictionary<string, double> _shownDrops = [];
    private double? _shownStarterVolts;
    private double? _shownBatteryCrankingVolts;
    private int _meterDay = -1;
    /// <summary>A shown reading older than this against the current chain is stale (a part was cleaned or replaced since).</summary>
    private const double StaleReadingVolts = 0.03;
    private StartOutcome? _lastStart;

    /// <summary>The player-luck stream (poor jobs).</summary>
    private readonly IRandom _random;
    /// <summary>The world stream: draws each fault's spread when it becomes active.</summary>
    private readonly IRandom _world;
    /// <summary>The daily weather, absolute game days 0 to 1080 (three years).</summary>
    private readonly WeatherSeries _weather;

    public Game(Boat boat, int startDay = 75, IRandom? random = null, int worldSeed = DefaultSeed, WeatherSeries? weather = null)
    {
        _weather = weather ?? WeatherSeries.Generate(worldSeed);
        _worldSeed = worldSeed;
        _portId = boat?.Trips?.Home ?? "";
        _boat = boat ?? throw new ArgumentNullException(nameof(boat));
        _launchDeadlineDay = boat.Economy.LaunchDeadlineDay;
        _startDay = startDay;
        _random = random ?? new SeededRandom(DefaultSeed);
        _world = new SeededRandom(worldSeed * 7919 + 17);
        _reach = new Reach(boat, _removed, _gone);
        _lastAboardDay = startDay;
        _water = new WaterSystem(boat);
        foreach (var cover in boat.Covers.Where(c => c.StartsOff)) _removed.Add(cover.Id);
        InitSkills();
        if (boat.TryGet(MooringLinesId, out var mooringLines))
            _chafeSeen = mooringLines.State[ChafeProperty] = boat.Mooring.StartChafe;
        foreach (var f in boat.AllFaults)
        {
            f.ActivateOn(startDay);
            DrawSpread(f);
        }
        _water.Publish(_values);
    }

    /// <summary>The seed of the shipped first game: it decides the poor-job rolls.</summary>
    public const int DefaultSeed = 4;

    public static Game NewFirstFault(int seed = DefaultSeed) => new(BoatLoader.LoadSloop28(), 75, new SeededRandom(seed), seed);

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
                $"The yard has her down for launch on day {_launchDeadlineDay}. You have {_boat.Economy.StartingBalanceNok} NOK for the spring.",
                "The season runs on past the launch, to the autumn haulout at the yard, the winter on the hard and a sale.",
            ]),
            Status => new StatusView(
                Day, TimeOfDay, _time.WorkMinutes, _money, _lastStart?.ToString(), _running, _finished, KnownStatus(), ToolNames(), CurrentPlace.Name,
                Balance, _launchDeadlineDay, _launched, Afloat, WeatherWords.Describe(Today), _sunk,
                UnreadAlerts(), AtSea ? null : CurrentPort?.Name, AtSea, SeasonLine(), _sold, ReadinessSeasonLead()),
            Logbook => new LogView(_log.ToList(), WeatherWords.Describe(Today), Day),
            Weather => TodayView(),
            Notices => new NoticesView(TakeUnread()),
            Commands => new CommandList(CommandCatalogue),
            Aboard => AboardSnapshot(),
            ComponentActions q => ActionsFor(q.Component),
            SurveyReport => SurveyReportSnapshot(),
            Readiness => ReadinessSnapshot(),
            Inventory => InventorySnapshot(),
            PlanTrip q => PlanTripSnapshot(q.Destination, q.DepartDay),
            Trip => TripSnapshot(),
            Season => SeasonSnapshot(),
            Sale => SaleSnapshot(),
            Chart q => ChartSnapshot(q.Planned),
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
        new("stop", null, "Pull the stop and shut the engine down (1 min)"),
        new("clean", "component", "Clean and tighten a part"),
        new("replace", "component", "Fit a new part"),
        new("buy", "item", "Buy a tool or a spare at the chandlery"),
        new("inventory", null, "See what is aboard and what the chandlery sells"),
        new("plan trip", "destination", "Plan a trip: distance, hours, the forecast, what could go wrong and what you carry"),
        new("depart", "destination", "Cast off for another port (engine running, from the cockpit)"),
        new("continue", null, "At sea: another hour as you are"),
        new("motor", null, "At sea: under engine, then an hour"),
        new("sail", null, "At sea: under sail if the wind serves, then an hour"),
        new("heave to", null, "At sea: stop her where she is so you can work on the boat"),
        new("anchor", null, "At sea, near shelter: let the anchor go"),
        new("turn back", null, "At sea: give up the passage and head for the port you left"),
        new("call tow", null, "At sea: call the towboat (costs money and the best part of a day)"),
        new("trip", null, "Where you are on the passage"),
        new("chart", "destination", "The chart: the ports with distance and bearing from here, the shelters, and the track of the passage under way"),
        new("season", null, "The yard calendar: your slot, the first free slot and what lifting costs"),
        new("book haulout", "day", "Ring the yard and book a slot to be lifted out (free; changing the booking costs a fee)"),
        new("haul out", null, "At the yard, afloat, on your booked day, engine running: be lifted out for the winter (costs the lift and the storage)"),
        new("scrub", null, "Afloat, from the cockpit or on deck: scrub what you can reach at the waterline (takes part of the growth off)"),
        new("yard wash", null, "On the hard at the yard: the yard pressure-washes the hull (costs money)"),
        new("winterise", null, "On the hard, from the companionway with the steps off: drain the engine's raw-water side and run antifreeze through (1.5 h, a little money)"),
        new("fill tank", null, "At a port with fuel: top the tank up with diesel (half an hour, the diesel)"),
        new("battery ashore", null, "On the hard, from the quarter berth with the battery box lid off: carry the battery to the yard's battery room for the winter (costs money)"),
        new("battery aboard", null, "On the hard, from the quarter berth: fit the battery again"),
        new("charger", null, "On the hard: clip the yard's charger on the battery aboard for the winter (costs money; off again at launch)"),
        new("fit cover", null, "On the hard, on deck: rig the winter cover over the deck (the cover costs money the first time; off again at launch)"),
        new("wait until spring", null, "Leave her for the winter, day by day, coming back early if she has something to tell you or your slot comes"),
        new("list for sale", "nok", "On the hard at the yard: list her with the broker at your asking price and get the broker's valuation letter (half an hour)"),
        new("accept offer", "offer", "Sell her to the best open offer, or to the numbered one: the game ends with the closing card"),
        new("keep", null, "Keep her for another season: the launch deadline moves to next spring and the logbook starts again"),
        new("sale", null, "The broker's letter, your listing, the viewings and the offers"),
        new("charge", "component", "Charge a battery on shore power (8 h)"),
        new("order survey", null, "Hire the surveyor to go over her (costs money and a day)"),
        new("read survey", null, "Read the surveyor's report"),
        new("notices", null, "What the boat has told you since you last asked"),
        new("readiness", null, "See the open problems, their price now and later, the deadline and the money"),
        new("defer", "problem", "Leave a known problem for later, on purpose"),
        new("undefer", "problem", "Take a deferred problem back onto the list"),
        new("yard fix", "problem", "Pay the yard to fix a known problem"),
        new("weather", null, "Look at the sky and the wind today"),
        new("wait", "days", "Let some days pass"),
        new("launch", null, "Put her in the water"),
        new("pump bilge", null, "Work the manual bilge pump from the cockpit (15 min, up to 150 litres)"),
        new("emergency lift", null, "Pay the yard to lift her out again, afloat only (costs money and days)"),
        new("check filter", null, "Look in the fuel filter bowl"),
        new("check flow", null, "Look at the cooling water at the exhaust (engine running)"),
        new("log", null, "Read the logbook"),
        new("help", null, "List the commands"),
        new("quit", null, "Leave the boat for now"),
    ];

    private WeatherDay Today => _weather[Day];

    private WeatherView TodayView()
    {
        var w = Today;
        return new WeatherView(Day, WeatherWords.Describe(w), w.WindForce, w.WindFrom, w.RainMm, w.TempC);
    }

    /// <summary>A job that needs a dry day is refused when it is raining today.</summary>
    private string? WetRefusal(Component c)
    {
        var w = Today;
        if (!c.Spec.NeedsDry || w.RainMm < _boat.Rates.WeatherGates.RainMm) return null;
        return $"It's raining ({WeatherWords.RainWord(w)}, {WeatherWords.Temperature(w)}): the paint won't take on a wet hull. Wait for a dry day.";
    }

    private AboardView AboardSnapshot()
    {
        var here = CurrentPlace;
        var places = _boat.Places.Select(p => new AboardPlace(
            p.Name, p.Id == _placeId, _visited.Contains(p.Id), IsAvailable(p),
            p.Adjacent.Select(a => _boat.GetPlace(a).Name).ToList())).ToList();
        var covers = _boat.Covers.Select(c => new AboardCover(c.Name, _boat.GetPlace(c.PlaceId).Name, _removed.Contains(c.Id))).ToList();
        var found = _boat.Components.Where(c => _seen.Contains(c.Id))
            .Select(c => new FoundComponent(
                c.Name, _boat.GetPosition(c.PositionId).Label, c.Faults.Any(f => f.Discovered && (f.Present || SupersededOpen().Contains(f))),
                _reach.Visible(c, _placeId), _reach.Reachable(c, _placeId), IsFlagged(c)))
            .ToList();
        return new AboardView(here.Name, places, covers, found);
    }

    private ComponentActionsView ActionsFor(string name)
    {
        var seen = _boat.Components.Where(c => _seen.Contains(c.Id));
        var r = NameResolver.Resolve(seen, name, c => c.Id, c => c.Name, c => c.Aliases);
        if (r.Match is not { } c) return new ComponentActionsView(name, false, null, []);

        bool hasButton = OwnsButton;
        bool ashore = c.Id == Electrical.BatteryId && IsBatteryAshore;
        string? sight = ashore ? BatteryAshoreMessage : _reach.Refusal(c, _placeId, false);
        string? touch = ashore ? BatteryAshoreMessage : _reach.Refusal(c, _placeId, true);
        var options = new List<ActionOption>();
        void Add(string verb, string label, string? reason, int minutes, int cost) =>
            options.Add(new(verb, label, reason is null, reason, minutes, cost));

        Add("inspect", "Inspect", sight, 5, 0);

        // Every verb below comes from the component's kind, never from its faults or its id (002 finding 2).
        if (c.HasVerb(Verb.Uncover) && c.RemovablePart is { } part)
            Add("uncover", $"Pull back the {part.Name}",
                SeaWorkRefusal() ?? touch ?? (_uncovered.Contains(c.Id) ? AlreadyUncovered(part) : null), 2, 0);

        if (c.HasVerb(Verb.Measure))
        {
            // Parts in the crank loop are only read while she cranks, which needs the remote start button.
            bool crankLoop = EngineInputsBuilder.LoopOrder.Contains(c.Id);
            Add("measure", "Measure",
                SeaWorkRefusal() ?? touch ?? (crankLoop && !hasButton ? "Needs someone to crank while you hold the probes: a remote start button." : null), 10, 0);
        }

        if (c.HasVerb(Verb.Clean))
            Add("clean", "Clean", SeaWorkRefusal() ?? AfloatRefusal(c) ?? touch ?? WetRefusal(c), c.Id == HullId ? _boat.Fouling.CleanMinutes : 30, 0);
        if (c.HasVerb(Verb.Replace))
        {
            bool spare = SpareFor(c.Id) is not null;
            var (partNok, notStocked) = spare ? (0, null) : ReplacePartHere(c);
            Add("replace", "Replace",
                ReplaceGate(c) ?? touch ?? WetRefusal(c) ?? notStocked ?? (spare ? null : MoneyRefusal(partNok)),
                c.ReplaceMinutes, partNok);
        }
        if (c.HasVerb(Verb.Charge)) Add("charge", "Charge", YardGate() ?? touch, 480, 0);
        if (c.HasVerb(Verb.CheckFilterBowl)) Add("check-filter-bowl", "Check filter bowl", touch, 10, 0);
        if (c.HasVerb(Verb.CheckRawWaterFlow))
            Add("check-raw-water-flow", "Check flow",
                !ExhaustOutletPlaces.Contains(_placeId) ? NoOutletMessage
                : _running ? null : "Only tells you anything with the engine running.", 5, 0);

        AddProblemOptions(c, options);
        return new ComponentActionsView(name, true, c.Name, options);
    }

    // --------------------------------------------------------------- commands

    public CommandResult Execute(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_sunk) return Reject(command.GetType().Name.ToLowerInvariant(), SunkMessage);
        if (_sold) return Reject(command.GetType().Name.ToLowerInvariant(), SoldMessage);
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
            Defer c => DoDefer(turn, c.Fault),
            Undefer c => DoUndefer(turn, c.Fault),
            FixByYard c => DoYardFix(turn, c.Fault),
            OrderSurvey => DoOrderSurvey(turn),
            WaitDays c => DoWait(turn, c.Days),
            Launch => DoLaunch(turn),
            EmergencyLift => DoEmergencyLift(turn),
            PumpBilge => DoPumpBilge(turn),
            StopEngine => DoStopEngine(turn),
            Depart c => DoDepart(turn, c.Destination),
            Continue => DoContinue(turn),
            Motor => DoMotor(turn),
            Sail => DoSail(turn),
            HeaveTo => DoHeaveTo(turn),
            Anchor => DoAnchor(turn),
            TurnBack => DoTurnBack(turn),
            CallTow => DoCallTow(turn),
            BookHaulout c => DoBookHaulout(turn, c.Day),
            HaulOut => DoHaulOut(turn),
            Scrub => DoScrub(turn),
            WashHull => DoWashHull(turn),
            WinteriseEngine => DoWinteriseEngine(turn),
            FillTank => DoFillTank(turn),
            BatteryAshore => DoBatteryAshore(turn),
            BatteryAboard => DoBatteryAboard(turn),
            WinterCharger => DoWinterCharger(turn),
            FitWinterCover => DoFitWinterCover(turn),
            WaitUntilSpring => DoWaitUntilSpring(turn),
            ListForSale c => DoListForSale(turn, c.AskNok),
            AcceptOffer c => DoAcceptOffer(turn, c.Offer),
            Keep => DoKeep(turn),
            _ => throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command)),
        };
    }

    private sealed class Turn
    {
        public List<string> Narrative { get; } = [];
        public List<KnownFault> Newly { get; } = [];
        /// <summary>Ids of faults found and ruled out in the same breath (red herrings).</summary>
        public HashSet<string> RuledOut { get; } = [];
        /// <summary>Cause lines told at this turn's discoveries, by fault id; the logbook writes each just before its Found line.</summary>
        public Dictionary<string, string> Causes { get; } = [];
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
        RevealSpawnedByLook(t);
        t.Narrative.Add(place.Description);
        if (SoleLine(place) is { } soleLook) t.Narrative.Add(soleLook);
        if (OverTheSideLine(place) is { } overTheSide) t.Narrative.Add(overTheSide);
        foreach (var cover in _boat.Covers.Where(c => c.PlaceId == place.Id))
            t.Narrative.Add($"The {Words.CoverName(cover)} {Words.Is(cover)} {Words.State(cover, _removed.Contains(cover.Id))}.");
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
        if (target.Exposed && Today.WindForce >= _boat.Rates.WeatherGates.WindForce)
            return Reject("go", $"It's blowing {WeatherWords.WindPhrase(Today)} (force {Today.WindForce}): not a day to be up on deck.");

        // Going below with the hatch shut: you slide it open on the way, for free.
        if (_placeId == StartPlaceId && target.Id == BelowPlaceId && _boat.Covers.Any(c => c.Id == HatchId) && !_removed.Contains(HatchId))
        {
            _removed.Add(HatchId);
            t.Narrative.Add("You slide the hatch open to go below.");
        }
        _placeId = target.Id;
        _visited.Add(target.Id);
        MarkSeen(t);
        t.Narrative.Add(target.Description);
        if (SoleLine(target) is { } soleGo) t.Narrative.Add(soleGo);
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
        if (remove && off) return Reject(action, $"The {label} {Words.Is(cover)} already {Words.State(cover, true)}.");
        if (!remove && !off) return Reject(action, $"The {label} {Words.Is(cover)} already {Words.State(cover, false)}.");

        var before = _reach.VisibleAt(_placeId).Select(c => c.Id).ToHashSet();
        if (remove) _removed.Add(cover.Id); else _removed.Remove(cover.Id);
        var newlyVisible = remove
            ? _reach.VisibleAt(_placeId).Where(c => !before.Contains(c.Id)).Select(c => c.Name).ToList()
            : [];

        t.Narrative.Add(remove ? cover.RemoveText : cover.RefitText);
        if (newlyVisible.Count > 0) t.Narrative.Add("Behind it: " + string.Join(", ", newlyVisible) + ".");
        MarkSeen(t);
        return Accept(t, action, remove ? cover.RemoveMinutes : cover.RefitMinutes, 0,
            new CoverView(cover.Name, coverPlace.Name, remove, newlyVisible),
            remove
                ? cover.RemoveLog.Length > 0 ? cover.RemoveLog : $"Took off the {label}."
                : cover.RefitLog.Length > 0 ? cover.RefitLog : $"Put back the {label}.");
    }

    private CommandResult DoInspect(Turn t, string name)
    {
        if (!ResolveComponent("inspect", name, needTouch: false, out var c, out var rejected)) return rejected!;

        bool off = _uncovered.Contains(c.Id);
        t.Narrative.Add(c.Description + " " + WearPhrase(c.Condition));
        if (c.Id == BilgeId) t.Narrative.Add($"In the bilge: {_boat.Water.Words(_water.Level)}.");
        if (c.Id == HullId && Band.Pick(_boat.Fouling.HullWords, Fouling) is { Length: > 0 } below)
        {
            t.Narrative.Add($"Under the waterline: {below}.");
            SeeFouling();
        }
        if (c.Id == MooringLinesId && Band.Pick(_boat.Mooring.ChafeWords, Chafe) is { Length: > 0 } chafed) t.Narrative.Add(chafed);
        if (off && c.RemovablePart is { UncoveredText.Length: > 0 } part) t.Narrative.Add(part.UncoveredText);
        foreach (var f in c.ActiveFaults.ToList())
        {
            Reveal(t, f, PlayerAction.Inspect, uncovered: off);
            if (f.Discovered && f.Present) t.Narrative.Add($"You know what this is: {f.Name}.");
        }
        var view = new InspectionView(c.Name, string.Join(" ", t.Narrative));
        return Accept(t, "inspect", 5, 0, view, $"Inspected the {c.Name.ToLowerInvariant()}.");
    }

    private CommandResult DoMeasure(Turn t, string name)
    {
        if (!ResolveComponent("measure", name, needTouch: true, out var c, out var rejected, pre: _ => SeaWorkRefusal())) return rejected!;
        if (!c.HasVerb(Verb.Measure)) return Reject("measure", $"There's nothing on the {c.Name.ToLowerInvariant()} to put a multimeter to.");

        var inputs = EngineInputsBuilder.Build(_boat);
        var reading = EngineModel.Attempt(inputs).Electrical;
        bool hasButton = OwnsButton;
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
            if (reading.CircuitClosed) meter = meter with { Gap = NoteCrankingReading(reading, c.Id, cranking) };
        }
        else if (c.Id == EngineInputsBuilder.StarterId)
        {
            double v = reading.DropVolts[c.Id];
            meter = new MeterReading(c.Name, "volts-at-starter", v, "V", $"{v.ToString("F2", ci)} V at the starter while cranking.");
            log = $"Measured at the starter while cranking: {v.ToString("F2", ci)} V.";
            meter = meter with { Gap = NoteCrankingReading(reading, c.Id, v) };
        }
        else if (reading.DropVolts.TryGetValue(c.Id, out var drop))
        {
            meter = new MeterReading(c.Name, "volts-drop", drop, "V", $"{drop.ToString("F2", ci)} V lost across the {c.Name.ToLowerInvariant()} while cranking.");
            log = $"Measured across the {c.Name.ToLowerInvariant()} while cranking: {drop.ToString("F2", ci)} V.";
            meter = meter with { Gap = NoteCrankingReading(reading, c.Id, drop) };
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

    /// <summary>
    /// Records what the meter showed and, once the starter has been read today, returns the gap the player can add up.
    /// Readings from an earlier day are forgotten; a reading that no longer matches the current chain (the part was
    /// cleaned or replaced since) is dropped, and the gap is always taken from the current chain.
    /// </summary>
    private CircuitGap? NoteCrankingReading(ElectricalReading now, string id, double volts)
    {
        if (Day != _meterDay)
        {
            _shownDrops.Clear();
            _shownStarterVolts = null;
            _shownBatteryCrankingVolts = null;
            _meterDay = Day;
        }
        double shown = Math.Round(volts, 2);
        if (id == EngineInputsBuilder.StarterId) _shownStarterVolts = shown;
        else if (id == "start-battery") _shownBatteryCrankingVolts = shown;
        else _shownDrops[id] = shown;

        foreach (var stale in _shownDrops.Where(kv => Math.Abs(kv.Value - now.DropVolts.GetValueOrDefault(kv.Key)) > StaleReadingVolts).Select(kv => kv.Key).ToList())
            _shownDrops.Remove(stale);
        if (_shownStarterVolts is { } sv && Math.Abs(sv - now.StarterVolts) > StaleReadingVolts) _shownStarterVolts = null;

        if (_shownBatteryCrankingVolts is { } bv && Math.Abs(bv - now.BatteryTerminalVolts) > StaleReadingVolts) _shownBatteryCrankingVolts = null;

        // The gap needs both ends as the player has seen them; the figure itself is the current chain's.
        if (_shownStarterVolts is null || _shownBatteryCrankingVolts is null || !now.CircuitClosed) return null;
        return new CircuitGap(now.BatteryTerminalVolts - now.StarterVolts, _shownDrops.Values.Sum());
    }

    private CommandResult DoStart(Turn t)
    {
        if (_placeId != "cockpit") return Reject("start", KeyMessage);
        if (_running) return Reject("start", "The engine is already running.");
        if (IsBatteryAshore) return Reject("start", BatteryAshoreStartMessage);

        var result = EngineModel.Attempt(EngineInputsBuilder.Build(_boat), new GameClock(Day).MonthName);
        _lastStart = result.Outcome;
        t.Narrative.Add(result.Description);
        if (result.Outcome != StartOutcome.Nothing) Electrical.Crank(_boat); // a start that cranks takes its share of the charge
        if (result.Outcome == StartOutcome.SlowCrank || result.LabouredCrank) _values.Set(SystemValues.DragStarts, _values.Get(SystemValues.DragStarts) + 1);
        _running = result.Outcome is StartOutcome.RunsRough or StartOutcome.Runs;

        const int minutes = 2;
        if (result.Outcome == StartOutcome.Runs && !result.OverheatWarning && !_finished)
        {
            _finished = true;
            int total = _time.WorkMinutes + minutes;
            t.Narrative.Add($"She'll do. {LaunchIn()}");
            t.Narrative.Add($"Time spent: {total / 60} h {total % 60} min. Money spent: {_money} NOK.");
        }
        return Accept(t, "start", minutes, 0, new StartAttemptView(result.Outcome.ToString(), result.Description, result.LabouredCrank),
            $"Tried to start the engine. Result: {result.Outcome}.");
    }

    private static string AlreadyUncovered(RemovablePart part) => $"The {part.Name} is already pulled back.";

    private CommandResult DoUncover(Turn t, string name)
    {
        if (!ResolveComponent("uncover", name, needTouch: true, out var c, out var rejected, pre: _ => SeaWorkRefusal())) return rejected!;
        if (!c.HasVerb(Verb.Uncover) || c.RemovablePart is not { } part) return Reject("uncover", $"There's nothing to pull back on the {c.Name.ToLowerInvariant()}.");
        if (!_uncovered.Add(c.Id)) return Reject("uncover", AlreadyUncovered(part));

        // Pulling the boot back is the same act on every part of a kind; what is under it is seen on Inspect.
        // Narrating it here leaked the fault: "clean and bright" on one terminal, silence on the other (002 finding 2).
        t.Narrative.Add(part.UncoverText ?? $"You peel the {part.Name} back off the clamp. What's under it is bare to look at now.");
        return Accept(t, "uncover", 2, 0, null, $"Pulled back the {part.Name} on the {c.Name.ToLowerInvariant()}.");
    }

    private CommandResult DoClean(Turn t, string name)
    {
        if (!ResolveComponent("clean", name, needTouch: true, out var c, out var rejected,
                pre: x => SeaWorkRefusal() ?? (x.HasVerb(Verb.Clean) ? AfloatRefusal(x) : null))) return rejected!;
        if (c.Id == HullId) return DoCleanHull(t, c);
        if (!c.HasVerb(Verb.Clean)) return Reject("clean", "Nothing to clean there.");
        if (WetRefusal(c) is { } wet) return Reject("clean", wet);

        ShutDownEngine(t);
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
            CountMaintenance();
        }
        if (bootWasOff && c.RemovablePart is { } part)
        {
            var refit = part.RefitText ?? $"You push the {part.Name} back over the clamp.";
            t.Narrative.Add(refit);
            text += " " + refit;
        }
        MaybePoorJob(c);
        return Accept(t, "clean", 30, 0, new RepairView(c.Name, text), $"Cleaned the {c.Name.ToLowerInvariant()}.");
    }

    private CommandResult DoReplace(Turn t, string name)
    {
        if (!ResolveComponent("replace", name, needTouch: true, out var c, out var rejected,
                pre: x => x.HasVerb(Verb.Replace) ? ReplaceGate(x) : null)) return rejected!;
        if (!c.HasVerb(Verb.Replace)) return Reject("replace", $"The {c.Name.ToLowerInvariant()} is not something you can replace in the yard this spring.");
        if (WetRefusal(c) is { } wet) return Reject("replace", wet);
        var spare = SpareFor(c.Id);
        var (cost, notStocked) = spare is null ? ReplacePartHere(c) : (0, null);
        if (notStocked is not null) return Reject("replace", notStocked);
        if (spare is null && MoneyRefusal(cost) is { } broke) return Reject("replace", broke);

        ShutDownEngine(t);
        c.Replace();
        _uncovered.Remove(c.Id);
        if (spare is not null)
        {
            ConsumeSpare(spare);
            t.Narrative.Add($"You take the spare {spare.Name} from the spares aboard.");
        }
        var text = c.ReplaceText ?? $"You fit a new {c.Name.ToLowerInvariant()}.";
        t.Narrative.Add(text);
        if (c.Id == "primary-filter" && FixDrainedWater() is { } runsClean)
        {
            t.Narrative.Add(runsClean);
            text += " " + runsClean;
        }
        if (c.Id == EngineOilId) _oilChangedDay = Day;
        if (_boat.Sale.ServiceComponents.Contains(c.Id)) _serviceSeasons.Add(_seasonIndex);
        if (c.Id != MooringLinesId) MaybePoorJob(c); // the poor-job templates are about paint; new lines are just new lines
        return Accept(t, "replace", c.ReplaceMinutes, cost, new RepairView(c.Name, text),
            spare is null ? $"Replaced the {c.Name.ToLowerInvariant()}." : $"Replaced the {c.Name.ToLowerInvariant()} from the spares aboard.");
    }

    private CommandResult DoCharge(Turn t, string name)
    {
        if (YardGate() is { } away) return Reject("charge", away);
        if (!ResolveComponent("charge", name, needTouch: true, out var c, out var rejected)) return rejected!;
        if (!c.HasVerb(Verb.Charge)) return Reject("charge", "Only a battery takes a charge.");

        ShutDownEngine(t);
        foreach (var f in c.ActiveFaults.Where(f => f.FixedBy.Contains(PlayerAction.Charge)).ToList()) f.Clear();
        Electrical.Fill(_boat);
        var text = "You plug in the shore-power charger and wait out eight hours. By the end of it the battery is full.";
        t.Narrative.Add(text);
        return Accept(t, "charge", 480, 0, new RepairView(c.Name, text), "Charged the start battery for eight hours.");
    }

    private const string NeedButtonMessage =
        "You can't crank and hold the probes at once. A remote start button from the chandlery would let you.";

    private CommandResult DoCheckFilterBowl(Turn t)
    {
        var filter = _boat.Components.First(x => x.HasVerb(Verb.CheckFilterBowl));
        if (!_seen.Contains(filter.Id)) return Reject("check-filter-bowl", "You haven't found a fuel filter yet.");
        if (_reach.Refusal(filter, _placeId, needTouch: true) is { } refusal) return Reject("check-filter-bowl", refusal);
        var before = t.Narrative.Count;
        // Water in the fuel lives on the tank, but it is the filter bowl that shows it.
        foreach (var f in filter.ActiveFaults.Concat(_boat.Get("fuel-tank").ActiveFaults).ToList()) BowlCheck(t, f);
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

    private int ClockMinutes => _time.ClockMinutes;
    private int Day => _startDay + (StartMinuteOfDay + ClockMinutes) / 1440;
    private string TimeOfDay => FormatTime(ClockMinutes);

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

    private CommandResult DoStopEngine(Turn t)
    {
        if (!_running) return Reject("stop-engine", "The engine isn't running.");
        _running = false;
        t.Narrative.Add("You pull the stop and the engine dies.");
        return Accept(t, "stop-engine", 1, 0, null, "Stopped the engine.");
    }

    private void ShutDownEngine(Turn t)
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
        if (reveal is { Discovers: false }) f.MarkSymptomatic();
        if ((reveal?.Discovers ?? false) || forceDiscover) Discover(t, f);
    }

    private void Discover(Turn t, Fault f)
    {
        if (f.Discovered || f.Dormant) return;
        f.Discover();
        var known = new KnownFault(f.Id, f.Name, _boat.Get(f.ComponentId).Name);
        _discovered.Add(known);
        t.Newly.Add(known);
        if (_pendingCause.Remove(f.Id, out var cause))
        {
            t.Narrative.Add(cause);
            t.Causes[f.Id] = cause;
        }
        if (!f.Harmless) return;
        f.Clear();
        t.RuledOut.Add(f.Id);
        t.Narrative.Add($"Ruled out: {f.Name}.");
    }

    private bool ResolveComponent(
        string action, string name, bool needTouch, out Component component, out CommandResult? rejected,
        Func<Component, string?>? pre = null)
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
        if (r.Match.Id == Electrical.BatteryId && IsBatteryAshore)
        {
            rejected = Reject(action, BatteryAshoreMessage);
            return false;
        }
        var refusal = pre?.Invoke(r.Match) ?? _reach.Refusal(r.Match, _placeId, needTouch);
        rejected = refusal is null ? null : Reject(action, refusal);
        return refusal is null;
    }

    private CommandResult RejectResolution<T>(string action, Resolution<T> r, string name, Func<T, string> label) where T : class =>
        r.Kind == ResolutionKind.Ambiguous
            ? Reject(action, $"'{name.Trim()}' could be: {string.Join(", ", r.Candidates.Select(label))}. Be more specific.")
            : Reject(action, $"You can't find '{name.Trim()}' on this boat.");

    private CommandResult Reject(string action, string message) =>
        new(false, action, [message], 0, 0, null, [], _running, _finished, []);

    /// <summary>
    /// Logs the command (a null <paramref name="logText"/> logs nothing: the leg commands write their own line), passes the time, and builds the result.
    /// <paramref name="afterTick"/> runs once the time has passed and may add to the narrative and the logbook; its observation is used
    /// unless she sank (the loss card) or the passage ended (the trip card).
    /// </summary>
    private CommandResult Accept(Turn t, string action, int minutes, int costNok, Observation? obs, string? logText, bool work = true,
        Func<Observation?>? afterTick = null)
    {
        var stamp = TimeOfDay;
        int day = Day;
        // What goes on the track once the time has passed: the hour she is in then is the hour it happened in (design note 009).
        string? trackEvent = _passage is not null && logText is not null && action is "clean" or "replace" or "heave-to" or "anchor" or "turn-back"
            ? logText : null;
        var seaPassage = _passage;
        if (CountsAsMaintenance(action)) _maintenanceEntries++;
        if (logText is not null)
        {
            _log.Add(new LogEntry(day, stamp, logText, costNok));
            if (_passage is not null && action is "clean" or "replace") _passage?.Fixes.Add(logText);
        }
        LogFound(t, 0, day, stamp);
        _money += costNok;
        int before = ClockMinutes;
        Tick(t, minutes, work);
        if (afterTick is not null) obs = afterTick();
        if (trackEvent is not null && !_sunk && _passage is not null && ReferenceEquals(_passage, seaPassage)) MarkTrack(trackEvent);
        if (_sunk)
        {
            // She went down while the time passed: the card is the result, and the rest of the time does not pass.
            minutes = ClockMinutes - before;
            obs = _loss;
            t.Narrative.AddRange(_loss!.Lines);
        }
        else if (_arrived is { } arrived)
        {
            _arrived = null;
            obs = FinishArrival(t, arrived);
        }
        return new CommandResult(true, action, t.Narrative.ToList(), minutes, costNok, obs, t.Newly.ToList(), _running, _finished, TakeUnread());
    }
}
