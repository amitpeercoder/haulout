using System.Globalization;
using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Engine;
using Haulout.Sim.Faults;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// Fuel, the dark, fog and the anchor (design note 010, step 2). The tank burns fuel and the gauge says how much is left; a passage that runs
/// after dark without her navigation lights rolls close calls, and a ferry's report brings the harbourmaster's letter; fog holds a boat in harbour and slows her at sea unless she has a plotter or
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

    /// <summary>Close calls a ferry reported (reputation, design note 010, second pass D).</summary>
    private int _ferryReports;

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
    /// Design note 010, second pass G: a fault that stops an instrument only in rain (<see cref="Fault.FailsWhen"/>) stops it on a day of rain and not
    /// on a dry one; the parts that came aboard with an upgrade (the radar's scanner cable) count as part of it. <paramref name="day"/>: the day the
    /// question is about (default today). <paramref name="knownOnly"/>: only faults the player has found count, which is what a plan may assume.
    /// </summary>
    private bool UpgradeWorks(string id, int? day = null, bool knownOnly = false)
    {
        if (!_boat.TryGet(id, out var c) || _gone.Contains(id)) return false;
        int on = day ?? Day;
        var parts = _boat.TryGetUpgrade(id, out var upgrade) ? upgrade.Also.Prepend(c) : [c];
        return !parts.SelectMany(p => p.ActiveFaults).Any(f => (!knownOnly || f.Discovered) && FailsOn(f, on));
    }

    /// <summary>The fault is stopping its part on this day: always, or (failsWhen "rain") only on a day of rain.</summary>
    private bool FailsOn(Fault f, int day) => f.FailsWhen is not "rain" || RainDay(day);

    /// <summary>What the player sees of the instruments that are stopping right now, for the components in <paramref name="among"/>; each fault's symptom once.</summary>
    private List<string> InstrumentSymptoms(IEnumerable<Component> among)
    {
        var lines = new List<string>();
        if (!Afloat) return lines;
        foreach (var f in among.SelectMany(c => c.ActiveFaults).Where(f => f.Symptom is { Length: > 0 } && FailsOn(f, Day)).ToList())
        {
            lines.Add(f.Symptom!);
            f.MarkSymptomatic();
        }
        return lines;
    }

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
        double rate = TrueBurn().LitresPerHour;
        tank.State[FuelLitresProperty] = Math.Max(0, tank.State[FuelLitresProperty] - rate * minutes / 60.0);
        if (FuelEmpty && !AtSea)
        {
            _running = false;
            Raise("fuel-empty", EmptyAlert);
        }
    }

    /// <summary>
    /// What the engine drinks now, with every multiplier that is true (design note 010, second pass C). Under way under engine the load is
    /// the throttle's: the clean motoring speed (held to the fog cap), not the speed the fouled hull makes, because the engine holds its revs
    /// against the drag and the drag shows as the fouling multiplier. Otherwise the idle load.
    /// </summary>
    private FuelBurn TrueBurn()
    {
        var rules = _boat.Fuel;
        double? speed = _passage is { Mode: SeaMode.Motoring } p ? Math.Min(_boat.SeaRules.MotorKnots, p.FogCap ?? double.MaxValue) : null;
        var adds = new List<FuelAdd>();
        if (Fouling > 0 && rules.FoulingMaxAdd > 0) adds.Add(new FuelAdd(Band.Pick(rules.FoulingWords, Fouling), rules.FoulingMaxAdd * Fouling));
        foreach (var f in _boat.AllFaults.Where(f => f.Present))
            if (rules.FaultAdds.TryGetValue(f.Id, out double add)) adds.Add(new FuelAdd(CauseWords(f.Id), add));
        if (_running && rules.ChargingAdd > 0 && Systems.Electrical.ChargeFactor(_boat) > 0 && Systems.Electrical.StateOfCharge(_boat) < rules.ChargingBelowStateOfCharge)
            adds.Add(new FuelAdd(CauseWords("charging"), rules.ChargingAdd));
        return FuelBurn.Of(rules, speed, adds);
    }

    /// <summary>What the player can know of the burn when planning: the clean motoring speed and only the multipliers they have seen (the growth they last saw on the hull, the faults they have found).</summary>
    private FuelBurn PlannedBurn()
    {
        var rules = _boat.Fuel;
        var adds = new List<FuelAdd>();
        if (_foulingSeen > 0 && rules.FoulingMaxAdd > 0) adds.Add(new FuelAdd(Band.Pick(rules.FoulingWords, _foulingSeen), rules.FoulingMaxAdd * _foulingSeen));
        foreach (var f in KnownOpenFaults().Distinct())
            if (rules.FaultAdds.TryGetValue(f.Id, out double add)) adds.Add(new FuelAdd(CauseWords(f.Id), add));
        return FuelBurn.Of(rules, _boat.SeaRules.MotorKnots, adds);
    }

    private string CauseWords(string id) => _boat.Fuel.CauseWords.TryGetValue(id, out var w) ? w : id;

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

    /// <summary>
    /// The fog she is in at this day and hour (null minutes: in port). Sea fog counts at sea; harbour fog counts in port and in the first hour of
    /// a passage out of one.
    /// </summary>
    private bool InFog(int day, double hour, double minutesOut) =>
        _boat.Night.FogMatters && WeatherWords.IsFoggyUnderway(_weather[day], hour, minutesOut);

    /// <summary>The knots she is held to in fog at this day and hour: null when the fog does not slow her (none, none that matters, or radar).</summary>
    private double? FogCap(int day, double hour, double minutesOut)
    {
        var night = _boat.Night;
        if (!InFog(day, hour, minutesOut) || UpgradeWorks(RadarId, day)) return null;
        return UpgradeWorks(PlotterId, day) ? night.FogKnotsWithPlotter : night.FogKnots;
    }

    /// <summary>Fog without a plotter or radar: the instruments that would keep her off what she cannot see are not working.</summary>
    private bool BlindInFog(int day, double hour, double minutesOut) =>
        InFog(day, hour, minutesOut) && !UpgradeWorks(RadarId, day) && !UpgradeWorks(PlotterId, day);

    // ------------------------------------------------------------------ close calls

    /// <summary>
    /// A seeded close call for one whole hour under way and one cause (design note 010, second pass D; the night of step E uses the same).
    /// Seeded from <see cref="SeasonSeed"/> with salt <see cref="SaltCloseCall"/>, keyed on the absolute day and the hour (and the cause), so
    /// the same hour of the same world gives the same answer whatever else was done. Three draws: whether it happens, whether a ferry
    /// reports her (the narration is then a ferry's), which narration. Only the first time an hour is met in a passage.
    /// </summary>
    private void RollCloseCall(Passage p, string cause, int day, double hour)
    {
        if (!_boat.CloseCalls.Causes.TryGetValue(cause, out var rules) || rules.ChancePerHour <= 0 || rules.Narrations.Count == 0) return;
        int whole = (int)Math.Floor(hour);
        if (!p.CloseCallHoursRolled.Add((cause, day * 24 + whole))) return;
        var rng = new SeededRandom(SeasonSeed(_worldSeed, SaltCloseCall, day, whole + 24 * StableHash(cause)));
        double happens = rng.NextDouble(), reportRoll = rng.NextDouble(), pick = rng.NextDouble();
        double ferryPick = rng.NextDouble(); // a fourth draw, used only for the name of the ferry in a letter: the first three do not move
        if (happens >= rules.ChancePerHour) return;
        bool reported = reportRoll < rules.ReportChance;
        var pool = rules.Narrations.Where(n => n.Ferry == reported).ToList();
        if (pool.Count == 0) pool = rules.Narrations.ToList();
        var narration = pool[Math.Min(pool.Count - 1, (int)(pick * pool.Count))];
        bool ferry = reported && narration.Ferry;
        p.CloseCalls.Add((narration.Text, ferry ? rules.ReportLine : null));
        if (ferry) _ferryReports++;
        if (ferry && rules.Letter) QueueLetter(rules, day, whole, ferryPick);
    }

    /// <summary>Tells the close calls of the step in the narrative and the logbook (after the hour line).</summary>
    private void TellCloseCalls(Turn t, Passage p)
    {
        foreach (var (text, report) in p.CloseCalls)
        {
            string line = report is null ? text : $"{text} {report}";
            t.Narrative.Add(line);
            _log.Add(new LogEntry(Day, TimeOfDay, line));
            MarkTrack(text);
        }
        p.CloseCalls.Clear();
    }

    // ------------------------------------------------------------------ the harbourmaster's letter

    /// <summary>A reported close call whose cause brings a letter, waiting for the morning post.</summary>
    private sealed record PendingLetter(int Number, int PostedDay, string Text, string Alert, string Discovers);

    private readonly List<PendingLetter> _letters = [];
    private int _lettersWritten;

    /// <summary>
    /// A ferry reported a close call of a cause that brings a letter (design note 010, second pass E: a night under way without lights). The
    /// letter is written now and comes with the morning post: at the first midnight after, if she is in port, else on arrival.
    /// </summary>
    private void QueueLetter(CloseCallCause cause, int day, int hour, double ferryPick)
    {
        var rules = _boat.CloseCalls;
        if (rules.LetterNok <= 0 || cause.LetterText.Length == 0) return;
        string ferry = rules.Ferries.Count == 0 ? "a ferry" : rules.Ferries[Math.Min(rules.Ferries.Count - 1, (int)(ferryPick * rules.Ferries.Count))];
        string Fill(string template) => template
            .Replace("{ferry}", ferry, StringComparison.Ordinal)
            .Replace("{day}", day.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{hour}", $"{hour:00}:00", StringComparison.Ordinal)
            .Replace("{nok}", Money(rules.LetterNok), StringComparison.Ordinal);
        _letters.Add(new PendingLetter(++_lettersWritten, day, Fill(cause.LetterText), Fill(rules.LetterAlert), cause.LetterDiscovers));
    }

    /// <summary>
    /// The morning post: every letter written before today arrives, is billed through the ledger as a bill (never refused, it may pass the
    /// overdraft), logged as a document, and told as an alert. A boat at sea gets none until she arrives.
    /// </summary>
    private void DeliverLetters(Turn t, int today)
    {
        if (_passage is not null) return;
        foreach (var letter in _letters.Where(l => l.PostedDay < today).ToList())
        {
            _letters.Remove(letter);
            int nok = _boat.CloseCalls.LetterNok;
            Spend(nok, LedgerBill, _boat.CloseCalls.LetterBillText);
            _weekEvents.Add($"the harbourmaster's letter {Money(nok)}");
            Raise($"letter-{letter.Number}", letter.Alert, nok, onPassage: false);
            _log.Add(new LogEntry(Day, TimeOfDay, letter.Text, 0, $"letter {letter.Number}"));
            if (letter.Discovers.Length > 0 && _boat.TryGet(letter.Discovers, out var part))
                foreach (var f in part.ActiveFaults.ToList()) DiscoverNow(t, f); // the letter says she was unlit: the owner knows now
        }
    }

    /// <summary>Finds a present fault because the world showed it (an event, a letter, an anchorage), and writes the logbook's Found line.</summary>
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
