using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Faults;
using Haulout.Sim.Systems;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The passage (design note 007): the at-sea tick, the helm commands, arrival and the tow. The trip adds no clock and no second fault
/// system: <see cref="Tick"/> runs as before, and while a passage is set each step gets <see cref="SeaBefore"/> and <see cref="SeaAfter"/>
/// around the systems step.
/// </summary>
public sealed partial class Game
{
    private const string TillerMessage = "The tiller is in the cockpit.";
    private const string NotAtSeaMessage = "You're not at sea.";
    private const string FuelAlert = "The engine coughs, picks up, coughs again and dies. Worth a look in the fuel filter bowl.";
    private const string OverheatStopAlert = "Steam from the exhaust, and the engine labours and stops by itself. She's too hot to run.";

    /// <summary>A passage that has just ended in arrival; the command being accepted hands the trip card over (like the loss card).</summary>
    private Passage? _arrived;

    private string PortName(string portId) => _boat.Trips?.FindPort(portId)?.Name ?? portId;

    /// <summary>The weather of the day the step that ended <paramref name="back"/> minutes ago belongs to.</summary>
    private int StepDay(int back) => _startDay + (StartMinuteOfDay + ClockMinutes - back) / 1440;

    private int DayAt(int clockMinutes) => _startDay + (StartMinuteOfDay + clockMinutes) / 1440;

    // ------------------------------------------------------------------ the tick

    /// <summary>She moves along the route by <paramref name="nm"/>, never past the goal: what the log reads.</summary>
    private static void Advance(Passage p, double nm)
    {
        double room = Math.Max(0, p.Route.DistanceNm + p.DriftNm - p.PositionNm);
        p.PositionNm += nm;
        p.CoveredNm += Math.Min(nm, room);
    }

    /// <summary>Before the systems step of an hourly step at sea: progress by mode, the load on the impeller, the stirring of the tank, the draw.</summary>
    private void SeaBefore(int step)
    {
        var p = _passage!;
        var rules = _boat.SeaRules;
        double h = step / 60.0;
        if (p.Ferry is not null && !_inCall) ReleaseFerry(p); // time passes without an answer: the close call comes as it would have (design note 012, step 3)
        var w = _weather[StepDay(step)];
        int seaState = Sea.SeaState(rules, w.WindForce);

        if (p.Mode == SeaMode.Motoring && !_running) p.Mode = SeaMode.Drifting;
        if (p.Mode == SeaMode.Sailing && !Sea.Serves(rules, w.WindForce, w.WindFrom, p.Route.CourseDeg)) p.Mode = SeaMode.Drifting;

        // Fog holds her to a creep unless she has the instruments (design note 010); noted for the hour line.
        var (midDay, midHour) = StepMiddle(step);
        double minutesOut = ClockMinutes - step / 2.0 - p.DepartClockMinutes;
        bool underWay = p.Mode is SeaMode.Motoring or SeaMode.Sailing;
        double? fogCap = underWay ? FogCap(midDay, midHour, minutesOut) : null;
        if (underWay && BlindInFog(midDay, midHour, minutesOut)) RollCloseCall(p, "fog", midDay, midHour);
        if (underWay && IsDark(midDay, midHour) && !ComponentWorks(NavLightsId)) RollCloseCall(p, "night", midDay, midHour);
        p.FogCap = fogCap;
        // The skipper's log (design note 012): the hours under way after dark, and the strongest wind met under way (at anchor she is not under way).
        if (p.Mode != SeaMode.Anchored)
        {
            if (IsDark(midDay, midHour)) p.NightMinutes += step;
            p.MaxForce = Math.Max(p.MaxForce, w.WindForce);
        }
        double Capped(double knots) => fogCap is { } cap ? Math.Min(knots, cap) : knots;

        switch (p.Mode)
        {
            case SeaMode.Motoring: Advance(p, Capped(Slowed(rules.MotorKnots, Fouling)) * h); break;
            case SeaMode.Sailing: Advance(p, Capped(Slowed(Sea.SailKnots(rules, w.WindForce), Fouling)) * h); break;
            case SeaMode.HoveTo:
            case SeaMode.Drifting: p.DriftNm += rules.HeaveToDriftKnots * h; break;
            case SeaMode.UnderTow:
                // Towards the nearer port, set when the tow was called: on along the route or back along it.
                double before = p.PositionNm;
                p.PositionNm = Math.Clamp(p.PositionNm + (p.DestinationPortId == p.Route.To ? 1 : -1) * rules.TowKnots * h, 0, p.Route.DistanceNm);
                p.CoveredNm += Math.Abs(p.PositionNm - before);
                break;
        }

        // Under load for hours the impeller sheds vanes; a new one sheds little.
        if (p.Mode == SeaMode.Motoring && _running && _boat.TryGet(EngineInputsBuilder.ImpellerId, out var impeller))
        {
            double vanes = impeller.State.GetValueOrDefault(EngineInputsBuilder.VaneFactorProperty, 1.0);
            impeller.State[EngineInputsBuilder.VaneFactorProperty] =
                Math.Max(0, vanes - rules.ImpellerShedPerLoadHour * (1 - impeller.Condition) * (1 + _boat.Fouling.LoadPerGrowth * Fouling) * h);
        }

        // A running engine in a seaway stirs the tank: water in the fuel reaches the primary filter.
        if (_running && seaState >= rules.StirFromSeaState && WaterInTheFuel()
            && _boat.TryGet(EngineInputsBuilder.PrimaryFilterId, out var filter))
            filter.State[EngineInputsBuilder.WaterMinutesProperty] =
                filter.State.GetValueOrDefault(EngineInputsBuilder.WaterMinutesProperty) + step;

        Electrical.DrawFromEffective(_boat, rules.UnderWayDrawPerHour * h); // instruments, VHF, lights
    }

    private bool WaterInTheFuel() =>
        _boat.Get("fuel-tank").ActiveFaults.Any(f => f.Properties.ContainsKey(DrainProperty));

    /// <summary>After the systems step and the spawn: the engine's own endings (fuel, heat), then arrival.</summary>
    private void SeaAfter(Turn t, int step)
    {
        var p = _passage!;
        var rules = _boat.SeaRules;

        if (_running)
        {
            var inputs = EngineInputsBuilder.Build(_boat);
            if (inputs.Fuel.Restriction >= EngineModel.FiresAndDiesRestriction)
            {
                _running = false;
                p.OverheatMinutes = 0;
                if (FuelEmpty) Raise("fuel-empty", EmptyAlert);
                else Raise("fuel", FuelAlert);
                if (p.Mode == SeaMode.Motoring) p.Mode = SeaMode.Drifting;
            }
            else if (inputs.Cooling.RawWaterFlow < EngineModel.OverheatBelowFlow)
            {
                p.OverheatMinutes += step;
                if (p.OverheatMinutes >= rules.OverheatStopMinutes)
                {
                    _running = false;
                    p.OverheatMinutes = 0;
                    Raise("overheat-stop", OverheatStopAlert);
                    if (p.Mode == SeaMode.Motoring) p.Mode = SeaMode.Drifting;
                }
            }
            else p.OverheatMinutes = 0;
        }
        else p.OverheatMinutes = 0;

        p.EngineNote = EngineNote();
        UpdateFix(p);
        if (p.Mode != SeaMode.UnderTow && p.PositionNm >= p.Route.DistanceNm + p.DriftNm - 1e-9) Arrive(p);
    }

    /// <summary>The engine as she sounds now, for the hour line.</summary>
    private string EngineNote()
    {
        if (!_running) return "the engine is silent";
        var inputs = EngineInputsBuilder.Build(_boat);
        if (inputs.Cooling.RawWaterFlow < EngineModel.OverheatBelowFlow) return "the engine note has gone hard and the temperature alarm whines";
        if (inputs.Fuel.Restriction >= EngineModel.RunsRoughRestriction) return "the engine note hunts and sags";
        return "the engine note is steady";
    }

    private void Arrive(Passage p)
    {
        p.EndClockMinutes = ClockMinutes;
        MarkTrack($"Arrived at {PortName(p.DestinationPortId)}.");
        ArchivePassage(p, p.TurnedBack ? "turned back" : "arrived");
        LogPassage(p, towed: false);
        _running = false;
        _portId = p.DestinationPortId;
        _passage = null;
        _arrived = p;
    }

    /// <summary>Called by Accept once the time has passed: the arrival lines, the logbook entry, and the card.</summary>
    private TripResultView FinishArrival(Turn t, Passage p)
    {
        var port = _boat.Trips!.FindPort(p.DestinationPortId)!;
        t.Narrative.Add("You pick up a mooring and stop the engine.");
        var lines = ArrivalLinesFor(t, port);
        t.Narrative.AddRange(lines);
        TiredArrival(t, p);
        if (p.Onward is { Count: > 0 } onward && !p.TurnedBack)
        {
            double rest = onward.Sum(l => l.DistanceNm);
            t.Narrative.Add($"{port.Name} was the first leg of your way to {PortName(onward[^1].To)}: from here it is "
                + $"{rest.ToString("0.#", CultureInfo.InvariantCulture)} nm on. Depart again when you are ready.");
        }
        CollectWaiting(t);
        DeliverLetters(t, Day); // a letter written before today waits at the harbour office
        _log.Add(new LogEntry(DayAt(p.EndClockMinutes), FormatTime(p.EndClockMinutes), $"Arrived at {port.Name}."));
        CountArrival(p, port);
        JudgeTrip(t, p, port);
        TicketsCheck(t); // the log has this passage now: a ticket that waited for it is held
        return ResultOf(p, towed: false, lines.ToList());
    }

    private TripResultView ResultOf(Passage p, bool towed, IReadOnlyList<string> lines) => new(
        PortName(p.DestinationPortId), towed, p.TurnedBack, p.EndClockMinutes - p.DepartClockMinutes, _money - p.MoneyAtDeparture,
        p.Failures.ToList(), p.Fixes.ToList(), _log.Skip(p.LogStart).ToList(), lines);

    // ------------------------------------------------------------------ refusals

    /// <summary>
    /// Work on the engine and its parts (uncover, measure, clean, replace) at sea needs her stopped and the engine off. Null in port
    /// (there the ashore rules apply, with their own auto-stop).
    /// </summary>
    private string? SeaWorkRefusal()
    {
        if (_passage is not { } p) return null;
        if (p.Mode is not (SeaMode.HoveTo or SeaMode.Anchored)) return "Heave to or anchor first: you can't work on the engine under way.";
        return _running ? "Stop the engine first." : null;
    }

    /// <summary>The refusal every helm command shares: she must be at sea, and you at the tiller.</summary>
    private string? HelmRefusal() => !AtSea ? NotAtSeaMessage : _placeId != StartPlaceId ? TillerMessage : null;

    // ------------------------------------------------------------------ depart

    private CommandResult DoDepart(Turn t, string? destination)
    {
        if (AtSea) return Reject("depart", "You're already at sea.");
        if (_boat.Trips is not { } trips) return Reject("depart", "There's nowhere to go from here.");
        if (_onTheHard) return Reject("depart", "She's still on the hard.");
        if (_placeId != StartPlaceId) return Reject("depart", TillerMessage);
        if (!_finished) return Reject("depart", "The engine hasn't run yet this season.");
        if (!_running) return Reject("depart", "Start the engine first: you'll motor off the mooring.");
        if (Today.WindForce >= _boat.Rates.WeatherGates.WindForce)
            return Reject("depart", $"It's blowing too hard to leave: {WeatherWords.WindPhrase(Today)}.");
        var way = FindWay(trips, destination);
        if (way is null)
            return Reject("depart", $"There's no route from {CurrentPort?.Name} to '{(destination ?? "").Trim()}'.");
        var route = way.First;

        var ci = CultureInfo.InvariantCulture;
        string from = PortName(route.From), to = PortName(route.To);
        int logStart = _log.Count;
        t.Narrative.Add($"You slip the mooring and motor out of {from}, bound for {to}: {route.DistanceNm.ToString("0.#", ci)} nm on {route.CourseDeg:000}.");
        if (!way.Direct) t.Narrative.Add($"First leg: {to}. {RestOfTheWay(way)}");
        return Accept(t, "depart", _boat.SeaRules.DepartMinutes, 0, null, $"Left {from} for {to}.", afterTick: () =>
        {
            _passage = new Passage
            {
                Route = route, OriginPortId = route.From, DestinationPortId = route.To, Mode = SeaMode.Motoring,
                DepartClockMinutes = ClockMinutes, MoneyAtDeparture = _money, LogStart = logStart, Year = new GameClock(Day).Year,
                Onward = way.Direct ? null : way.Legs.Skip(1).ToList(), FriendAboard = FriendAboardNow,
            };
            StartTrack(_passage);
            ChargeDay(t, Day, atSea: true); // casting off on a weekday is the player's day
            return null;
        });
    }

    // ------------------------------------------------------------------ the legs

    /// <summary>Minutes to the end of the leg: an hour, or less when she gets there sooner at the speed she makes now.</summary>
    private int LegMinutes(Passage p)
    {
        var rules = _boat.SeaRules;
        var w = Today;
        double speed = p.Mode switch
        {
            SeaMode.Motoring => _running ? Slowed(rules.MotorKnots, Fouling) : 0,
            SeaMode.Sailing => Sea.Serves(rules, w.WindForce, w.WindFrom, p.Route.CourseDeg) ? Slowed(Sea.SailKnots(rules, w.WindForce), Fouling) : 0,
            _ => 0,
        };
        if (speed > 0 && p.Mode is SeaMode.Motoring or SeaMode.Sailing && FogCap(Day, HourOfDay, ClockMinutes - p.DepartClockMinutes) is { } cap) speed = Math.Min(speed, cap);
        if (speed <= 0) return MaxStepMinutes;
        return Math.Clamp((int)Math.Ceiling(p.RemainingNm / speed * 60 - 1e-9), 1, MaxStepMinutes);
    }

    /// <summary>The leg: the time passes, then the hour line goes into the narrative and the logbook and the observation is a LegView.</summary>
    private CommandResult DoLeg(Turn t, string action)
    {
        var p = _passage!;
        int minutes = LegMinutes(p);
        int pumpBefore = _water.PumpCycles;
        return Accept(t, action, minutes, 0, null, null, afterTick: () =>
        {
            string line = HourLine(p, _water.PumpCycles - pumpBefore);
            t.Narrative.Add(line);
            _log.Add(new LogEntry(Day, TimeOfDay, line));
            TellCloseCalls(t, p);
            var w = _weather[StepDay(1)];
            return new LegView(HourOf(p), p.ModeWord, Math.Round(p.ProgressNm, 1), Math.Round(p.RemainingNm, 1),
                w.WindForce, w.WindFrom, Sea.SeaState(_boat.SeaRules, w.WindForce), line);
        });
    }

    private int HourOf(Passage p) =>
        Math.Max(1, (int)Math.Ceiling(((p.EndClockMinutes > 0 ? p.EndClockMinutes : ClockMinutes) - p.DepartClockMinutes) / 60.0 - 1e-9));

    /// <summary>"Hour 3. Force 4 from the southwest, a short chop on the beam. The engine note is steady; the bilge pump has run once. 4.0 nm to Hestholmen."</summary>
    private string HourLine(Passage p, int pumpCycles)
    {
        var rules = _boat.SeaRules;
        var ci = CultureInfo.InvariantCulture;
        var w = _weather[StepDay(1)];
        string sea = Sea.StateWord(rules, Sea.SeaState(rules, w.WindForce));
        string wind = w.WindForce == 0
            ? $"No wind, {sea}."
            : $"Force {w.WindForce} from the {w.WindFrom}, {sea} {Sea.WindPhrase(rules, Sea.WindOnBoat(w.WindFrom, p.Route.CourseDeg))}.";
        if (p.FogCap is { } fog) wind += $" Fog: you creep along at {fog.ToString("0.#", ci)} knots, listening.";
        foreach (var symptom in InstrumentSymptoms(_boat.Components))
            if (p.SymptomsTold.Add(symptom)) wind += " " + symptom;
        string pump = pumpCycles switch
        {
            0 => "the bilge pump has stayed quiet",
            1 => "the bilge pump has run once",
            2 => "the bilge pump has run twice",
            _ => $"the bilge pump has run {Number(pumpCycles)} times",
        };
        string engine = p.EngineNote.Length > 0 ? p.EngineNote : EngineNote();
        return $"Hour {HourOf(p)}. {wind} {char.ToUpperInvariant(engine[0])}{engine[1..]}; {pump}. {p.RemainingNm.ToString("0.0", ci)} nm to {PortName(p.DestinationPortId)}.";
    }

    private CommandResult DoContinue(Turn t) =>
        HelmRefusal() is { } no ? Reject("continue", no) : DoLeg(t, "continue");

    private CommandResult DoMotor(Turn t)
    {
        if (HelmRefusal() is { } no) return Reject("motor", no);
        if (!_running) return Reject("motor", "The engine isn't running: start it first.");
        var p = _passage!;
        t.Narrative.Add(p.Mode == SeaMode.Anchored ? "You weigh anchor and motor on." : "You put her in gear and motor on.");
        p.Mode = SeaMode.Motoring;
        return DoLeg(t, "motor");
    }

    private string? SailRefusal(Passage p)
    {
        var rules = _boat.SeaRules;
        var w = Today;
        if (w.WindForce < rules.SailMinForce) return $"There's too little wind to sail: {WeatherWords.WindPhrase(w)}.";
        if (w.WindForce > rules.SailMaxForce) return $"It's too rough to sail: {WeatherWords.WindPhrase(w)}.";
        if (Sea.WindOnBoat(w.WindFrom, p.Route.CourseDeg) < rules.NoGoDeg) return $"The wind is on the nose ({WeatherWords.WindPhrase(w)}): she can't sail that close.";
        return null;
    }

    private CommandResult DoSail(Turn t)
    {
        if (HelmRefusal() is { } no) return Reject("sail", no);
        var p = _passage!;
        if (SailRefusal(p) is { } refusal) return Reject("sail", refusal);
        t.Narrative.Add(p.Mode == SeaMode.Anchored ? "You weigh anchor and set the sails." : "You trim the sheets and she heels to the wind.");
        p.Mode = SeaMode.Sailing;
        return DoLeg(t, "sail");
    }

    private CommandResult DoHeaveTo(Turn t)
    {
        if (HelmRefusal() is { } no) return Reject("heave-to", no);
        var p = _passage!;
        if (p.Mode == SeaMode.Anchored) return Reject("heave-to", "You're at anchor: there's nothing to heave to.");
        if (p.Mode == SeaMode.HoveTo) return Reject("heave-to", "You're already hove-to.");
        t.Narrative.Add("You back the jib, lash the tiller to leeward and she lies quietly, drifting slowly.");
        p.Mode = SeaMode.HoveTo;
        return Accept(t, "heave-to", _boat.SeaRules.HeaveToMinutes, 0, null, "Hove-to.");
    }

    // ------------------------------------------------------------------ shelter

    /// <summary>The places she could anchor along this route, with where they are along it: the port she left, the named shelters, the port she is bound for.</summary>
    private List<(string Name, double AtNm)> Anchorages(Passage p) =>
        [(PortName(p.Route.From), 0), .. p.Route.Shelter.Select(s => (s.Name, s.AtNm)), (PortName(p.Route.To), p.Route.DistanceNm)];

    private (string Name, double AtNm, double DistanceNm) NearestAnchorage(Passage p)
    {
        var best = Anchorages(p).OrderBy(a => Math.Abs(a.AtNm - p.ProgressNm)).First();
        return (best.Name, best.AtNm, Math.Abs(best.AtNm - p.ProgressNm));
    }

    private static string AheadOrAstern(double atNm, double progressNm) => atNm >= progressNm ? "ahead" : "astern";

    private CommandResult DoAnchor(Turn t)
    {
        if (HelmRefusal() is { } no) return Reject("anchor", no);
        var p = _passage!;
        if (p.Mode == SeaMode.Anchored) return Reject("anchor", "You're already at anchor.");
        var ci = CultureInfo.InvariantCulture;
        var near = NearestAnchorage(p);
        if (near.DistanceNm > _boat.SeaRules.AnchorWithinNm)
            return Reject("anchor", $"Nowhere to anchor here: {near.Name} is {near.DistanceNm.ToString("0.0", ci)} nm {AheadOrAstern(near.AtNm, p.ProgressNm)}.");
        if (SeizedChain is { } chain)
        {
            // The world shows it: the chain jams, and now you know why.
            int logFrom = t.Newly.Count;
            _seen.Add(chain.ComponentId);
            Reveal(t, chain, PlayerAction.Inspect);
            LogFound(t, logFrom, Day, TimeOfDay);
            t.Narrative.Insert(0, "The chain jams in the hawse after a few metres; the anchor never reaches the bottom. Heave to, or find a mooring.");
            return new CommandResult(false, "anchor", t.Narrative.ToList(), 0, 0, null, t.Newly.ToList(), _running, _finished, TakeUnread());
        }
        t.Narrative.Add($"You let the anchor go in the lee of {near.Name} and she swings to it.");
        p.Mode = SeaMode.Anchored;
        return Accept(t, "anchor", _boat.SeaRules.AnchorMinutes, 0, null, $"Anchored near {near.Name}.");
    }

    // ------------------------------------------------------------------ turn back

    private CommandResult DoTurnBack(Turn t)
    {
        if (HelmRefusal() is { } no) return Reject("turn-back", no);
        var p = _passage!;
        if (p.TurnedBack) return Reject("turn-back", "You've already turned back.");
        string home = PortName(p.OriginPortId);
        double progress = p.ProgressNm;
        p.Route = p.Route.Reversed();
        p.PositionNm = Math.Clamp(p.Route.DistanceNm - progress, 0, p.Route.DistanceNm);
        p.DriftNm = 0;
        p.DestinationPortId = p.Route.To;
        p.TurnedBack = true;
        t.Narrative.Add($"You put the helm over and head back for {home}.");
        return Accept(t, "turn-back", _boat.SeaRules.TurnBackMinutes, 0, null, $"Turned back for {home}.");
    }

    // ------------------------------------------------------------------ tow

    private CommandResult DoCallTow(Turn t)
    {
        if (_passage is not { } p) return Reject("call-tow", NotAtSeaMessage);
        var tow = _boat.Trips!.Tow;
        int towNok = TowPriceNok;
        // Decision 007/2: the towboat takes her to the nearer port along the route (a tie goes back the way she came).
        bool onward = p.ProgressNm * 2 > p.Route.DistanceNm;
        string target = onward ? p.Route.To : p.Route.From;
        double towNm = onward ? p.Route.DistanceNm - p.ProgressNm : p.ProgressNm;
        bool back = target == p.OriginPortId;
        string home = PortName(target);
        string towed = back ? $"back to {home}" : $"on to {home}";

        // The bill is allowed to take the balance below zero: it comes due later.
        Spend(towNok, LedgerBill, "The towboat.");
        _running = false;
        p.Mode = SeaMode.UnderTow;
        p.OverheatMinutes = 0;
        p.DestinationPortId = target;
        t.Narrative.Add((VhfFitted ? "You call the towboat on the VHF" : "You ring the towboat on your phone") + " and give your position. It comes alongside, takes your line, and the engine stays silent.");
        t.Narrative.Add($"{home} is the nearer port, {towNm.ToString("0.0", CultureInfo.InvariantCulture)} nm off; that's where they take her.");
        _log.Add(new LogEntry(Day, TimeOfDay, $"Called the towboat: {Money(towNok)} to be towed {towed}.", towNok));
        CountTow($"Called the towboat: {Money(towNok)} to be towed {towed}.");
        MarkTrack($"Called the towboat: {Money(towNok)} to be towed {towed}.");
        int before = ClockMinutes;

        Tick(t, tow.Hours * 60, aboard: true);
        if (_sunk) return TowSunk(t, before, towNok);

        _portId = target;
        p.EndClockMinutes = ClockMinutes;
        ArchivePassage(p, "towed");
        LogPassage(p, towed: true);
        _passage = null;
        _log.Add(new LogEntry(Day, TimeOfDay, $"The towboat left her on her mooring at {home}."));
        Tick(t, tow.LostHours * 60, aboard: false);
        if (_sunk) return TowSunk(t, before, towNok);

        p.EndClockMinutes = ClockMinutes;
        string[] lines =
        [
            $"The towboat brings her {towed} at a walking pace, the engine silent behind you, and hands you a bill for {Money(towNok)}.",
            "By the time she is on her mooring the day is gone and the evening with it. Whatever was on goes on without you.",
        ];
        t.Narrative.AddRange(lines);
        CheckEvents(t);
        TicketsCheck(t);
        return new CommandResult(true, "call-tow", t.Narrative.ToList(), ClockMinutes - before, towNok,
            ResultOf(p, towed: true, lines), t.Newly.ToList(), _running, _finished, TakeUnread());
    }

    private CommandResult TowSunk(Turn t, int before, int cost)
    {
        t.Narrative.AddRange(_loss!.Lines);
        return new CommandResult(true, "call-tow", t.Narrative.ToList(), ClockMinutes - before, cost, _loss, t.Newly.ToList(), _running, _finished, TakeUnread());
    }

    // ------------------------------------------------------------------ the trip query

    private TripView TripSnapshot()
    {
        var rules = _boat.SeaRules;
        var ci = CultureInfo.InvariantCulture;
        var w = Today;
        if (_passage is not { } p)
        {
            var route = _boat.Trips is { } trips && !_sunk ? trips.RoutesFrom(_portId).FirstOrDefault() : null;
            bool serves = route is not null && Sea.Serves(rules, w.WindForce, w.WindFrom, route.CourseDeg);
            string at = CurrentPort?.Name ?? "";
            var out_ = _boat.Trips is { } all && !_sunk
                ? all.RoutesFrom(_portId).Select(r => $"{PortName(r.To)}, {r.DistanceNm.ToString("0.#", ci)} nm").ToList()
                : [];
            return new TripView(false, CurrentPort?.Name, null, null, 0, 0, null, 0, false, null, serves,
                at.Length > 0 ? $"She lies at {at}." : "She lies at her mooring.", out_, VhfFitted);
        }

        var near = NearestAnchorage(p);
        bool canAnchor = near.DistanceNm <= rules.AnchorWithinNm;
        string shelter = $"{near.Name}, {near.DistanceNm.ToString("0.0", ci)} nm {AheadOrAstern(near.AtNm, p.ProgressNm)}";
        bool serves2 = Sea.Serves(rules, w.WindForce, w.WindFrom, p.Route.CourseDeg);
        string mode = p.ModeWord;
        string text = $"{char.ToUpperInvariant(mode[0])}{mode[1..]}, {p.ProgressNm.ToString("0.0", ci)} nm out of {PortName(p.Route.From)} "
            + $"and {p.RemainingNm.ToString("0.0", ci)} nm to {PortName(p.Route.To)}. "
            + $"{WeatherWords.WindPhrase(w)[..1].ToUpperInvariant()}{WeatherWords.WindPhrase(w)[1..]}, {Sea.StateWord(rules, Sea.SeaState(rules, w.WindForce))}. "
            + (canAnchor ? $"You could anchor: {near.Name} is {near.DistanceNm.ToString("0.0", ci)} nm {AheadOrAstern(near.AtNm, p.ProgressNm)}."
                : $"{near.Name} is {near.DistanceNm.ToString("0.0", ci)} nm {AheadOrAstern(near.AtNm, p.ProgressNm)}.")
            + (p.Ferry is { Told: true } waiting ? " " + Fill(Radio.Ferry.Waiting, ("ferry", waiting.Ferry)) : "");
        return new TripView(true, null, PortName(p.Route.From), PortName(p.Route.To), Math.Round(p.ProgressNm, 1), Math.Round(p.RemainingNm, 1),
            mode, Math.Max(1, (int)Math.Ceiling((ClockMinutes - p.DepartClockMinutes) / 60.0 - 1e-9)), canAnchor, shelter, serves2, text,
            null, VhfFitted, p.Ferry is { Told: true } call ? FerryCallLine(call) : null);
    }
}
