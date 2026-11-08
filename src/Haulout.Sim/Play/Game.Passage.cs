using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Faults;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// Fuel, the dark, fog and the anchor (design note 010, step 2). The tank burns fuel and the gauge says how much is left; a passage that runs
/// after dark with a dead bow light meets the harbour patrol; fog holds a boat in harbour and slows her at sea unless she has a plotter or
/// radar (upgrades: step 3 fits them, until then none is fitted); the anchor chain can be seized. Nothing here draws from the world or the
/// player's random: the fog is in the weather series, on its own stream.
/// </summary>
public sealed partial class Game
{
    private const string NavLightsId = "nav-lights";
    private const string PlotterId = "chartplotter";
    private const string RadarId = "radar";
    private const string AnchorId = "anchor-gear";
    private const string EmptyAlert = "The engine coughs and dies. The fuel gauge needle lies on empty.";

    private int _patrolFines;

    /// <summary>The hour of the day it is now, 0 to 24.</summary>
    private double HourOfDay => ((StartMinuteOfDay + ClockMinutes) % 1440) / 60.0;

    /// <summary>The day and hour of the middle of the step that ended now and lasted <paramref name="step"/> minutes (a step never crosses midnight).</summary>
    private (int Day, double Hour) StepMiddle(int step)
    {
        double m = StartMinuteOfDay + ClockMinutes - step / 2.0;
        return (_startDay + (int)Math.Floor(m / 1440), m % 1440 / 60.0);
    }

    /// <summary>The component has no present fault (an anchor, a pump, the lights). A boat without it is taken to be fine.</summary>
    private bool ComponentWorks(string id) => !_boat.TryGet(id, out var c) || !c.ActiveFaults.Any();

    /// <summary>
    /// An upgrade is fitted and has no present fault (the plotter, the radar, the heads, the stove, the cushions, the heater). A fitted
    /// upgrade is an ordinary component (step 3); one that is not fitted is not among the components, so this is false for it.
    /// </summary>
    private bool UpgradeWorks(string id) => _boat.TryGet(id, out var c) && !_gone.Contains(id) && !c.ActiveFaults.Any();

    private bool IsDark(int day, double hour) => WeatherWords.IsDark(_weather[day], hour, _boat.Night.SolarNoonHour);

    /// <summary>"22:30": the hour when the light goes (or comes back), to five minutes.</summary>
    private static string ClockHour(double hour)
    {
        int minutes = (int)Math.Round(hour * 60 / 5.0) * 5;
        minutes = (minutes % 1440 + 1440) % 1440;
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private string LightGoes(int day) => ClockHour(_boat.Night.SolarNoonHour + _weather[day].DaylightHours / 2);

    private string LightComes(int day) => ClockHour(_boat.Night.SolarNoonHour - _weather[day].DaylightHours / 2);

    // ------------------------------------------------------------------ fuel

    private bool FuelEmpty => EngineInputsBuilder.FuelEmpty(_boat);

    /// <summary>The engine drinks while it runs: the motoring rate under way under engine, the idle rate in every other case.</summary>
    private void BurnFuel(int minutes)
    {
        var rules = _boat.Fuel;
        if (!_running || !rules.Burns || !_boat.TryGet(FuelTankId, out var tank) || !tank.State.ContainsKey(FuelLitresProperty)) return;
        double rate = _passage is { Mode: SeaMode.Motoring } ? rules.MotorLitresPerHour : rules.IdleLitresPerHour;
        tank.State[FuelLitresProperty] = Math.Max(0, tank.State[FuelLitresProperty] - rate * minutes / 60.0);
        if (FuelEmpty && !AtSea)
        {
            _running = false;
            Raise("fuel-empty", EmptyAlert);
        }
    }

    /// <summary>What the gauge says, in words and to the gauge's step; null when the boat's fuel is not counted.</summary>
    private string? GaugeWords()
    {
        var rules = _boat.Fuel;
        if (!rules.Burns || !_boat.TryGet(FuelTankId, out var tank) || !tank.State.ContainsKey(FuelLitresProperty) || TankCapacity <= 0) return null;
        if (FuelEmpty) return "the needle lies on empty";
        double litres = FuelLitres;
        int step = Math.Max(1, rules.GaugeStepLitres);
        int rounded = (int)Math.Round(litres / step, MidpointRounding.AwayFromZero) * step;
        if (rounded <= 0) return "almost nothing, the needle just off empty";
        return $"about {rounded} litres ({Band.Pick(rules.GaugeWords, rounded / TankCapacity)})"; // the words go with the figure the gauge shows
    }

    private string? FuelStatusLine() => GaugeWords() is { } words ? $"Fuel: {words}." : null;

    /// <summary>Litres the gauge shows (the player's figure, not the tank's): null when fuel is not counted.</summary>
    private int? GaugeLitres()
    {
        if (!_boat.Fuel.Burns || !_boat.TryGet(FuelTankId, out _)) return null;
        int step = Math.Max(1, _boat.Fuel.GaugeStepLitres);
        return (int)Math.Round(FuelLitres / step, MidpointRounding.AwayFromZero) * step;
    }

    // ------------------------------------------------------------------ fog

    /// <summary>The knots she is held to in fog at this day and hour: null when the fog does not slow her (none, none that matters, or radar).</summary>
    private double? FogCap(int day, double hour)
    {
        var night = _boat.Night;
        if (!night.FogMatters || !WeatherWords.IsFoggy(_weather[day], hour) || UpgradeWorks(RadarId)) return null;
        return UpgradeWorks(PlotterId) ? night.FogKnotsWithPlotter : night.FogKnots;
    }

    private const string FogHarbourMessage =
        "Fog lies over the harbour, and the harbourmaster won't let a boat out without a plotter or radar. It often lifts by late morning.";

    /// <summary>Fog lies on the water now and she has neither a working plotter nor a working radar: the harbourmaster holds her.</summary>
    private bool FogHoldsHarbour =>
        _boat.Night.FogMatters && WeatherWords.IsFoggy(Today, HourOfDay) && !UpgradeWorks(PlotterId) && !UpgradeWorks(RadarId);

    // ------------------------------------------------------------------ night

    /// <summary>
    /// A step under way (not at anchor, not under tow) that is dark, with the bow light out: once a passage the harbour patrol comes
    /// alongside, fines her, and the fault behind the dark light is found (the patrol told you).
    /// </summary>
    private void CheckPatrol(Turn t, Passage p, int step)
    {
        var night = _boat.Night;
        if (night.NoLightsFineNok <= 0 || p.PatrolFined || p.Mode is SeaMode.Anchored or SeaMode.UnderTow || ComponentWorks(NavLightsId)) return;
        var (day, hour) = StepMiddle(step);
        if (!IsDark(day, hour)) return;
        p.PatrolFined = true;
        _patrolFines++;
        Spend(night.NoLightsFineNok, LedgerBill, "The harbour patrol's fine for running without lights.");
        Raise("patrol",
            $"A patrol boat's searchlight finds you: your bow light is out. They take your name and a fine of {night.NoLightsFineNok} NOK, and tell you to get it seen to.",
            night.NoLightsFineNok);
        foreach (var f in _boat.Get(NavLightsId).ActiveFaults.ToList()) DiscoverNow(t, f);
    }

    /// <summary>Finds a present fault because the world showed it (a patrol, an event), and writes the logbook's Found line.</summary>
    private void DiscoverNow(Turn t, Fault f)
    {
        if (!f.Present || f.Discovered) return;
        int from = t.Newly.Count;
        _seen.Add(f.ComponentId);
        Discover(t, f);
        LogFound(t, from, Day, TimeOfDay);
    }

    // ------------------------------------------------------------------ the anchor

    /// <summary>The anchor chain is seized: the anchor will not go down, at sea or at Kvitnes.</summary>
    private bool AnchorSeized => !ComponentWorks(AnchorId);

    private Fault? SeizedChain => _boat.TryGet(AnchorId, out var c) ? c.ActiveFaults.FirstOrDefault() : null;

    /// <summary>Arriving at an anchorage with a seized chain: the jam line instead of the anchoring line, and the fault found.</summary>
    private IReadOnlyList<string> ArrivalLinesFor(Turn t, Port port)
    {
        if (!port.Anchorage || !AnchorSeized || port.ArrivalLinesAnchorJammed is not { Count: > 0 } jammed) return port.ArrivalLines;
        if (SeizedChain is { } chain) DiscoverNow(t, chain);
        return jammed;
    }
}
