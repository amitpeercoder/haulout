using System.Globalization;
using Haulout.Sim.Components;
using Haulout.Sim.Faults;
using Haulout.Sim.Systems;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The winter layup (design note 008, step 2): the five tasks on the hard, the four winter accumulators they feed (published as system
/// values for the spawn rules), the battery in the yard's battery room, and <see cref="DoWaitUntilSpring"/>. Nothing here fires on a fixed
/// day: a skipped task only lets an accumulator grow, and the faults wake from it with their own spread.
/// </summary>
public sealed partial class Game
{
    private const string EngineOilId = "engine-oil";
    private const string RawWaterPumpId = "raw-water-pump";
    private const string FuelTankId = "fuel-tank";
    private const string WaterHeldProperty = "waterHeld";
    private const string FuelLitresProperty = "fuelLitres";
    private const string TankCapacityProperty = "litres";
    private const string BatteryAshoreMessage = "The battery is in the yard's battery room.";
    private const string BatteryAshoreStartMessage = "The battery is in the yard's battery room: fit it first (battery aboard).";

    /// <summary>The yard's charger is clipped on the battery aboard. Off again at launch.</summary>
    private bool _chargerOn;
    /// <summary>The winter cover is rigged over the deck. Off again at launch.</summary>
    private bool _coverOn;
    /// <summary>The cover is bought once and kept for later winters.</summary>
    private bool _coverOwned;
    /// <summary>The day of the autumn lift she is laid up since.</summary>
    private int _laidUpSinceDay;
    /// <summary>The last day the player changed the engine oil.</summary>
    private int? _oilChangedDay;

    // The winter accumulators. Only a boat laid up for the winter counts them; the published values only ever grow.
    private double _frostHours;
    private double _condensationDays;
    private double _frozenNights;
    private double _snowDays;

    /// <summary>
    /// The luck of this winter's battery draw: a factor in the usual spread, rolled once per layup from the day of the autumn lift (design
    /// note 008, salt 8), so the day the charge falls under the freeze floor is not the same for every boat.
    /// </summary>
    private double LayupDrawSpread() => Between([_boat.Rates.SpreadLo, _boat.Rates.SpreadHi], SeasonRoll(SaltLayupDraw, _laidUpSinceDay, 0));

    private bool IsBatteryAshore => _gone.Contains(Electrical.BatteryId);

    /// <summary>The winter cover is on and she is out of the water: rain and snow stay off the deck.</summary>
    private bool UnderWinterCover => _coverOn && _onTheHard;

    private double WaterHeld =>
        _boat.TryGet(RawWaterPumpId, out var pump) ? pump.State.GetValueOrDefault(WaterHeldProperty) : 0;

    private double FuelLitres =>
        _boat.TryGet(FuelTankId, out var tank) ? tank.State.GetValueOrDefault(FuelLitresProperty) : 0;

    private double TankCapacity =>
        _boat.TryGet(FuelTankId, out var tank) ? tank.State.GetValueOrDefault(TankCapacityProperty) : 0;

    // ------------------------------------------------------------------ the step

    /// <summary>
    /// The electrical side of one step: self-discharge and the engine's charge, unless the battery is in the battery room; the winter
    /// charger holds a battery aboard full on the hard. Running the engine fills the raw-water side again.
    /// </summary>
    private void ElectricalStep(int minutes)
    {
        if (IsBatteryAshore) _values.Set(SystemValues.Charging, 0);
        else
        {
            Electrical.Step(_boat, minutes);
            _values.Set(SystemValues.Charging, Electrical.Run(_boat, minutes, _running));
            if (_chargerOn && _onTheHard) HoldBatteryFull();
        }
        if (_running && _boat.TryGet(RawWaterPumpId, out var pump) && pump.State.ContainsKey(WaterHeldProperty))
            pump.State[WaterHeldProperty] = 1;
    }

    /// <summary>The battery is full, and anything a charge puts right is put right.</summary>
    private void HoldBatteryFull()
    {
        Electrical.Fill(_boat);
        if (!_boat.TryGet(Electrical.BatteryId, out var battery)) return;
        foreach (var f in battery.ActiveFaults.Where(f => f.FixedBy.Contains(PlayerAction.Charge)).ToList()) f.Clear();
    }

    /// <summary>
    /// A fault that woke after the player first saw its part still shows its look hint, on the next look at the place (the first sight
    /// was long ago and had nothing to show). Only faults woken by a spawn rule do this.
    /// </summary>
    private void RevealSpawnedByLook(Turn t)
    {
        foreach (var c in _reach.VisibleAt(_placeId))
            foreach (var f in c.ActiveFaults.Where(f => f is { Spawned: true, State: FaultState.Latent }).ToList())
                Reveal(t, f, PlayerAction.Look);
    }

    // ------------------------------------------------------------------ the day

    /// <summary>
    /// The winter's part of the midnight, for a boat laid up on the hard: the battery aboard runs down, and four counts grow under
    /// the conditions of the task that was skipped. The counts are published for the spawn rules.
    /// </summary>
    private void LayupDaily(int day)
    {
        if (_laidUp && _onTheHard)
        {
            var rules = _boat.Layup;
            var weather = _weather[day];
            double night = weather.NightC;

            if (!IsBatteryAshore && !_chargerOn) Electrical.Draw(_boat, rules.DrawPerDay * LayupDrawSpread());

            if (WaterHeld > 0 && night < 0)
                _frostHours += Math.Min(rules.MaxFrostHoursPerNight, -night * rules.FrostHoursPerDegree);

            double capacity = TankCapacity;
            if (capacity > 0 && FuelLitres < rules.FullAt * capacity && night < rules.CondensationBelowNightC)
                _condensationDays += 1 - FuelLitres / capacity;

            if (!IsBatteryAshore && !_chargerOn && night < 0 && Electrical.StateOfCharge(_boat) < rules.FreezeBelowCharge)
                _frozenNights += 1;

            if (!_coverOn && weather.RainMm >= _boat.Rates.WeatherGates.RainMm && weather.TempC < rules.SnowBelowC)
                _snowDays += 1;
        }
        _values.Set(SystemValues.FrostHoursWithWater, _frostHours);
        _values.Set(SystemValues.CondensationDays, _condensationDays);
        _values.Set(SystemValues.FrozenNights, _frozenNights);
        _values.Set(SystemValues.SnowDays, _snowDays);
    }

    // ------------------------------------------------------------------ the tasks

    private string? WinteriseRefusal()
    {
        if (YardGate() is { } away) return away;
        if (Afloat) return "She's in the water: while she's afloat the raw-water side stays full.";
        if (!_boat.TryGet(RawWaterPumpId, out var pump) || !pump.State.ContainsKey(WaterHeldProperty)) return "There's nothing to drain on this engine.";
        if (!_reach.Reachable(pump, _placeId)) return "The engine is reached from the companionway, with the steps off.";
        if (WaterHeld <= 0) return "The raw-water side is drained and full of antifreeze already.";
        return ShopOpenRefusal() ?? MoneyRefusal(_boat.Layup.AntifreezeNok);
    }

    private CommandResult DoWinteriseEngine(Turn t)
    {
        const string action = "winterise-engine";
        var rules = _boat.Layup;
        if (WinteriseRefusal() is { } refusal) return Reject(action, refusal);
        var pump = _boat.Get(RawWaterPumpId);

        ShutDownEngine(t);
        pump.State[WaterHeldProperty] = 0;
        _serviceSeasons.Add(_seasonIndex);
        var text = "You pull the yard hose off the intake, drain the raw-water pump and the heat exchanger into a bucket, and run antifreeze through the raw-water side until pink comes out of the exhaust. Nothing is left in there for the frost.";
        t.Narrative.Add(text);
        return Accept(t, action, rules.WinteriseMinutes, rules.AntifreezeNok, new RepairView("Engine", text),
            "Winterised the engine: drained the raw-water side and ran antifreeze through it.");
    }

    private string? FillRefusal()
    {
        if (AtSea) return "You're at sea: there's no fuel out here.";
        if (CurrentPort is { Fuel: false } dry) return $"There's no fuel at {dry.Name}.";
        if (!_boat.TryGet(FuelTankId, out var tank) || TankCapacity <= 0 || !tank.State.ContainsKey(FuelLitresProperty)) return "There's no tank to fill.";
        double litres = TankCapacity - FuelLitres;
        if (litres < 0.5) return "The tank is full.";
        return FriendPays ? null : MoneyRefusal((int)Math.Ceiling(litres * _boat.Layup.DieselNokPerLitre));
    }

    private CommandResult DoFillTank(Turn t)
    {
        const string action = "fill-tank";
        var rules = _boat.Layup;
        if (FillRefusal() is { } refusal) return Reject(action, refusal);
        var tank = _boat.Get(FuelTankId);
        double capacity = TankCapacity;
        double litres = capacity - FuelLitres;
        bool friend = FriendPays;
        int nok = friend ? 0 : (int)Math.Ceiling(litres * rules.DieselNokPerLitre);

        ShutDownEngine(t);
        tank.State[FuelLitresProperty] = capacity;
        int rounded = (int)Math.Round(litres, MidpointRounding.AwayFromZero);
        var text = friend
            ? $"You fill the tank to the brim from the fuel pump: {rounded} litres of diesel, and your friend pays."
            : $"You fill the tank to the brim from the fuel pump: {rounded} litres of diesel, {Money(nok)}.";
        t.Narrative.Add(text);
        if (friend) _ledger.Add(new LedgerEntry(Day, LedgerSpending, "Diesel, paid by your friend.", 0));
        return Accept(t, action, rules.FillMinutes, nok, new RepairView(tank.Name, text),
            friend ? $"Filled the tank: {rounded} litres of diesel, paid by your friend." : $"Filled the tank: {rounded} litres of diesel for {Money(nok)}.");
    }

    private string? BatteryAshoreRefusal()
    {
        if (YardGate() is { } away) return away;
        if (Afloat) return "She's in the water: the battery stays aboard while she's afloat.";
        if (IsBatteryAshore) return "The battery is in the yard's battery room already.";
        if (!_boat.TryGet(Electrical.BatteryId, out var battery)) return "There's no battery to take off.";
        if (!_reach.ReachableWhenFitted(battery, _placeId))
            return "The battery is under the quarter berth: get at it from there with the battery box lid off.";
        return OfficeOpenRefusal() ?? MoneyRefusal(_boat.Layup.BatteryRoomNok);
    }

    private CommandResult DoBatteryAshore(Turn t)
    {
        const string action = "battery-ashore";
        var rules = _boat.Layup;
        if (BatteryAshoreRefusal() is { } refusal) return Reject(action, refusal);
        var battery = _boat.Get(Electrical.BatteryId);

        ShutDownEngine(t);
        _gone.Add(Electrical.BatteryId);
        _chargerOn = false;
        var text = "You unclamp the terminals, lift the battery out of its box and carry it to the yard's battery room, where it goes on a charger shelf for the winter.";
        t.Narrative.Add(text);
        return Accept(t, action, rules.BatteryMinutes, rules.BatteryRoomNok, new RepairView(battery.Name, text),
            "Took the battery ashore to the yard's battery room.");
    }

    private string? BatteryAboardRefusal()
    {
        if (!IsBatteryAshore) return "The battery is aboard already.";
        if (YardGate() is { } away) return away;
        if (!_reach.ReachableWhenFitted(_boat.Get(Electrical.BatteryId), _placeId))
            return "The battery goes back under the quarter berth: get at its box from there with the lid off.";
        return null;
    }

    private CommandResult DoBatteryAboard(Turn t)
    {
        const string action = "battery-aboard";
        var rules = _boat.Layup;
        if (BatteryAboardRefusal() is { } refusal) return Reject(action, refusal);
        var battery = _boat.Get(Electrical.BatteryId);

        _gone.Remove(Electrical.BatteryId);
        HoldBatteryFull(); // it spent the winter on their charger
        var text = "You fetch the battery from the yard's battery room, set it in its box and clamp the terminals. It has been on the charger all winter and is full.";
        t.Narrative.Add(text);
        return Accept(t, action, rules.BatteryMinutes, 0, new RepairView(battery.Name, text), "Fitted the battery again.");
    }

    private string? ChargerRefusal()
    {
        if (YardGate() is { } away) return away;
        if (Afloat) return "She's in the water: shore power is for the hard.";
        if (IsBatteryAshore) return "The battery is in the yard's battery room, on their charger already.";
        if (_chargerOn) return "The charger is on the battery already.";
        return OfficeOpenRefusal() ?? MoneyRefusal(_boat.Layup.ChargerNok);
    }

    private CommandResult DoWinterCharger(Turn t)
    {
        const string action = "winter-charger";
        var rules = _boat.Layup;
        if (ChargerRefusal() is { } refusal) return Reject(action, refusal);

        _chargerOn = true;
        HoldBatteryFull();
        var text = "You run the yard's cable down to the boat and clip the charger on the battery. It will hold it full all winter.";
        t.Narrative.Add(text);
        return Accept(t, action, rules.ChargerMinutes, rules.ChargerNok,
            new RepairView(_boat.TryGet(Electrical.BatteryId, out var battery) ? battery.Name : "Battery", text),
            "Clipped the yard's charger on the battery.");
    }

    private string? CoverRefusal()
    {
        if (YardGate() is { } away) return away;
        if (Afloat) return "She's in the water: the winter cover is for the hard.";
        if (_placeId != OnDeckId) return "The cover goes over the deck from on deck: go up there.";
        if (_coverOn) return "The winter cover is on already.";
        int nok = _coverOwned ? 0 : _boat.Layup.CoverNok;
        return nok > 0 ? ShopOpenRefusal() ?? MoneyRefusal(nok) : null;
    }

    private CommandResult DoFitWinterCover(Turn t)
    {
        const string action = "fit-winter-cover";
        var rules = _boat.Layup;
        if (CoverRefusal() is { } refusal) return Reject(action, refusal);
        int nok = _coverOwned ? 0 : rules.CoverNok;

        ShutDownEngine(t);
        _coverOn = true;
        _coverOwned = true;
        var text = nok > 0
            ? "You buy a winter cover and rig it over the deck on its frame, lacing it down at the toe rail. Rain and snow will slide off it."
            : "You rig last winter's cover over the deck on its frame and lace it down at the toe rail. Rain and snow will slide off it.";
        t.Narrative.Add(text);
        return Accept(t, action, rules.CoverMinutes, nok, new RepairView("Winter cover", text), "Fitted the winter cover.");
    }

    /// <summary>At launch: the cover comes off and is stowed, the charger is unplugged.</summary>
    private void LaunchLayupLines(Turn t)
    {
        if (_coverOn)
        {
            _coverOn = false;
            t.Narrative.Add("You take the winter cover off, fold it and stow it in the locker.");
        }
        if (_chargerOn)
        {
            _chargerOn = false;
            t.Narrative.Add("You unplug the yard's charger and coil the cable.");
        }
    }

    // ------------------------------------------------------------------ the task list

    /// <summary>The five jobs of the layup, while she is laid up; empty otherwise. What a job costs is what it costs now.</summary>
    private IReadOnlyList<LayupTask> LayupTasks()
    {
        if (!_laidUp) return [];
        var rules = _boat.Layup;
        double capacity = TankCapacity;
        int oilMinutes = _boat.TryGet(EngineOilId, out var oil) ? oil.ReplaceMinutes : 0;
        int oilNok = ItemById(EngineOilId)?.PriceNok ?? 0;
        int fillNok = capacity > 0 ? (int)Math.Ceiling(Math.Max(0, capacity - FuelLitres) * rules.DieselNokPerLitre) : 0;
        return
        [
            new LayupTask("Winterise the engine", WaterHeld <= 0, rules.WinteriseMinutes, rules.AntifreezeNok, "winterise",
                "Raw water left in the engine can freeze in the pump or the heat exchanger and crack it."),
            new LayupTask("Change the oil", _oilChangedDay is { } d && d >= _laidUpSinceDay, oilMinutes, oilNok, "replace engine oil",
                "Old oil sits in the sump all winter with whatever it has collected."),
            new LayupTask("Fill the tank", capacity <= 0 || FuelLitres >= rules.FullAt * capacity, rules.FillMinutes, fillNok, "fill tank",
                "A part-empty tank breathes damp air on the cold nights, and water settles in the diesel."),
            new LayupTask("Battery ashore or on a charger", IsBatteryAshore || _chargerOn, rules.BatteryMinutes, rules.BatteryRoomNok, "battery ashore",
                "A battery left aboard runs down in the cold, and a flat one freezes and splits."),
            new LayupTask("Fit the winter cover", _coverOn, rules.CoverMinutes, _coverOwned ? 0 : rules.CoverNok, "fit cover",
                "Rain wets the deck all winter, and snow lies on the sprayhood."),
        ];
    }

    // ------------------------------------------------------------------ waiting for spring

    private string? WaitUntilSpringRefusal()
    {
        if (AtSea) return "You're at sea: continue, heave to or anchor.";
        int doy = new GameClock(Day).DayOfYear;
        if (!_laidUp && doy >= _boat.Layup.SeasonFromDayOfYear && doy < _boat.Yard.AutumnFromDayOfYear) return "It's already the season.";
        return null;
    }

    private CommandResult DoWaitUntilSpring(Turn t)
    {
        const string action = "wait-until-spring";
        if (WaitUntilSpringRefusal() is { } refusal) return Reject(action, refusal);

        ShutDownEngine(t);
        int spring = _boat.Layup.SpringDayOfYear;
        int target = Day - Day % GameClock.DaysPerYear + spring;
        if (target <= Day) target += GameClock.DaysPerYear;
        int startDay = Day;
        int before = ClockMinutes;
        t.Narrative.Add("You leave her for the winter.");
        if (_removed.Contains(HatchId))
            t.Narrative.Add("The companionway hatch is open: slide it shut before you go if you want her dry below.");
        _log.Add(new LogEntry(Day, TimeOfDay, "Left her for the winter."));

        _weekEvents.Clear();
        _holdPumpCounter = true;
        string? why;
        try { why = WaitDaysUntilSpring(t, target); }
        finally { _holdPumpCounter = false; }
        if (_sunk) return WaitSunk(t, before);

        int days = Day - startDay;
        t.Narrative.Add($"{Days(days)} later it is day {Day}.");
        t.Narrative.Add(WinterSummary(startDay, days));
        AddWeekLine(t);
        t.Narrative.Add(why ?? "It is spring.");
        _log.Add(new LogEntry(Day, TimeOfDay, $"Came back on day {Day} after {Days(days)}."));
        // A condition that went on all winter (a flat battery, water over the sole) raised the same alert every morning: said once.
        var told = TakeUnread().DistinctBy(a => a.Text).ToList();
        return new CommandResult(true, action, t.Narrative.ToList(), ClockMinutes - before, 0, null, t.Newly.ToList(), _running, _finished, told);
    }

    /// <summary>The days of the winter, one at a time, until spring, a booked slot or something that wants the player. Null: it is spring (or she sank).</summary>
    private string? WaitDaysUntilSpring(Turn t, int target)
    {
        string? why = null;
        while (Day < target)
        {
            var raisedBefore = new Dictionary<string, int>(_alerts.LastRaised);
            var arrivedBefore = _orders.Where(o => o.Arrived).ToHashSet();
            Tick(t, 1440, aboard: false);
            if (_sunk) return null;
            // Water the floorboards would explain wants the player's hands now; rain through the open hatch is only told at the end.
            if (_water.CyclesSinceAboard > 0 && !_water.HatchDominant) PumpCounterAlert();
            if (CourseStartsTodayWords() is { } courseStarts)
            {
                why = courseStarts;
                break;
            }
            if (_bookedDay is { } booked && Day == booked)
            {
                why = $"You come back on day {booked} for your haulout slot.";
                break;
            }
            if (_orders.FirstOrDefault(o => o.Arrived && !arrivedBefore.Contains(o)) is { } delivered)
            {
                why = $"You come back on day {Day}: the {delivered.Name} is due at the yard.";
                break;
            }
            if (FreshAlert(raisedBefore))
            {
                why = "You come back before spring: something on her wants your attention.";
                break;
            }
        }
        PumpCounterAlert(); // whatever the counter built up while she sat
        return why;
    }

    /// <summary>
    /// An alert of a kind that was not already being raised the day before: a condition that goes on (a flat battery) stops the wait once,
    /// not on every day after.
    /// </summary>
    private static readonly Func<string, bool> NeedsHandsPredicate = NeedsHands;

    private bool FreshAlert(Dictionary<string, int> before, Func<string, bool>? stops = null) =>
        _alerts.LastRaised.Any(kv => (stops ?? NeedsHandsPredicate)(kv.Key) &&
            (!before.TryGetValue(kv.Key, out var was) || (was != kv.Value && was < kv.Value - 1)));

    /// <summary>
    /// The alert kinds that bring the player back from the winter because something wants their hands: a new fault's sign, the lines parting,
    /// the chafe warning, water over the sole, the bilge smell, the voltmeter, a pump counter with no known cause, a viewing offer and the
    /// slot moved or missed. The pump counter after rain through the open hatch is not one: it only tells the player what they did.
    /// </summary>
    private static bool NeedsHands(string kind) =>
        kind.StartsWith("sign-", StringComparison.Ordinal) || kind is
            "mooring-parted" or "chafe" or "sole" or "bilge-smell" or "battery" or "pump-counter" or "viewing" or "slot-moved" or "slot-missed";

    private CommandResult WaitSunk(Turn t, int before)
    {
        t.Narrative.AddRange(_loss!.Lines);
        return new CommandResult(true, "wait-until-spring", t.Narrative.ToList(), ClockMinutes - before, 0, _loss, t.Newly.ToList(), _running, _finished, TakeUnread());
    }

    /// <summary>The weather she sat through: the usual summary for a short wait, a count of the bad days for a long one.</summary>
    private string WinterSummary(int startDay, int days)
    {
        var left = Enumerable.Range(startDay, days).Select(d => _weather[d]).ToList();
        if (days <= 7) return WeatherWords.Summarise(left);
        int gales = left.Count(d => d.WindForce >= _boat.Rates.WeatherGates.WindForce);
        int rain = left.Count(WeatherWords.IsRaining);
        int frost = left.Count(d => d.NightC < 0);
        return $"In that time: {Counted(rain, "day", "days")} of rain or snow, {Counted(gales, "day", "days")} of gale and {Counted(frost, "night", "nights")} of frost.";
    }
}
