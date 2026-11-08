using System.Globalization;
using Haulout.Sim.Engine;
using Haulout.Sim.Systems;

namespace Haulout.Sim.Play;

/// <summary>
/// The boat talks (design note 006, step 5): perceptions only (a lamp, an alarm, a smell, a counter, a voltmeter), each
/// naming what to check next and never a fault the player has not found. Each kind is raised at most once a day. Alerts are
/// logbook lines too; the next accepted command (or the Notices query) hands the unread ones over.
/// </summary>
public sealed partial class Game
{
    /// <summary>The counter alert when the open hatch let in most of the water: the player's fix is one slide of the hatch, nothing to investigate.</summary>
    private const string PumpCounterHatchKind = "pump-counter-hatch";
    private const string FloorboardsId = "saloon-floorboards";
    private const double BatteryAlertBelow = 0.5;
    private const double BilgeDeepLitres = 30;

    private sealed class AlertState
    {
        public readonly List<(Alert Alert, bool Read)> Items = [];
        /// <summary>Kind to the last day it was raised.</summary>
        public readonly Dictionary<string, int> LastRaised = new();
        public bool WasOverSole;
        public int LastMorningCheckDay = int.MinValue;
    }

    private readonly AlertState _alerts = new();

    /// <summary>Raises an alert unless its kind was already raised today.</summary>
    private void Raise(string kind, string text)
    {
        int day = Day;
        if (_alerts.LastRaised.TryGetValue(kind, out var last) && last == day) return;
        _alerts.LastRaised[kind] = day;
        var alert = new Alert(day, TimeOfDay, text);
        _alerts.Items.Add((alert, false));
        _log.Add(new LogEntry(day, alert.TimeOfDay, text, Alert: true));
        _passage?.Failures.Add(text);
    }

    /// <summary>All unread alerts, now marked read.</summary>
    private List<Alert> TakeUnread()
    {
        var unread = new List<Alert>();
        for (int i = 0; i < _alerts.Items.Count; i++)
        {
            if (_alerts.Items[i].Read) continue;
            unread.Add(_alerts.Items[i].Alert);
            _alerts.Items[i] = (_alerts.Items[i].Alert, true);
        }
        return unread;
    }

    /// <summary>Unread alerts, left unread; null when there are none.</summary>
    private List<Alert>? UnreadAlerts()
    {
        var unread = _alerts.Items.Where(i => !i.Read).Select(i => i.Alert).ToList();
        return unread.Count == 0 ? null : unread;
    }

    private bool OverSole => _water.Level > _boat.Water.SoleLitres;

    /// <summary>Per aboard step: what the player can see, hear and smell right now.</summary>
    private void PresentAlerts(int minutes, bool atSea = false)
    {
        if (_running && _values.Get(SystemValues.Charging) <= 0.01)
            Raise("alternator",
                "The alternator lamp on the panel stays lit with the engine running. Something isn't driving the alternator — worth a look at the front of the engine.");

        if (_running && EngineInputsBuilder.Build(_boat).Cooling.RawWaterFlow < Engine.EngineModel.OverheatBelowFlow)
            Raise("overheat",
                "The temperature alarm whines and there's a smell of hot rubber from under the cockpit. Shut her down and check the cooling water at the exhaust.");

        bool over = OverSole;
        if (over && !_alerts.WasOverSole) Raise("sole", SoleAlert);
        _alerts.WasOverSole = over;

        // At sea the voltmeter is watched every step, not only at the morning check (kinds are still raised once a day).
        if (atSea)
        {
            if (!IsBatteryAshore && Electrical.StateOfCharge(_boat) < BatteryAlertBelow) Raise("battery", BatteryAlert());
            return;
        }

        int day = Day;
        if (_alerts.LastMorningCheckDay != day)
        {
            _alerts.LastMorningCheckDay = day;
            if (!IsBatteryAshore && Electrical.StateOfCharge(_boat) < BatteryAlertBelow) Raise("battery", BatteryAlert());
        }
    }

    private const string SoleAlert = "Water over the saloon sole. Pump by hand from the cockpit and find where it's coming in.";

    private string BatteryAlert()
    {
        double volts = ElectricalChain.OpenCircuitVolts(Electrical.StateOfCharge(_boat));
        return $"The panel voltmeter reads {volts.ToString("F1", CultureInfo.InvariantCulture)} V {(AtSea ? "and falling" : "this morning")}. The battery isn't holding its charge; worth a meter on it.";
    }

    /// <summary>
    /// A long wait (the winter) reads the pump counter once, when it ends or when something needs the player's hands, not every day: the
    /// counter then says what has built up since the player was last aboard, not since the midnight before.
    /// </summary>
    private bool _holdPumpCounter;

    /// <summary>
    /// The bilge pump counter, read on coming back and reset. If the open hatch let in most of the water, it says so: that is what the
    /// player did, and the fix is a slide of the hatch, not a look under the floorboards.
    /// </summary>
    private void PumpCounterAlert()
    {
        int cycles = _water.CyclesSinceAboard;
        if (cycles > 0)
        {
            if (_water.HatchDominant)
                Raise(PumpCounterHatchKind,
                    $"The bilge pump counter has gone up by {Number(cycles)} since you were last aboard. You left the companionway hatch open; rain has been finding its way below.");
            else
                Raise("pump-counter",
                    $"The bilge pump counter has gone up by {Number(cycles)} since you were last aboard. Worth lifting the saloon floorboards to see where the water comes from.");
        }
        // The counter is read; the next stretch away counts from here.
        _water.ResetSinceAboard();
    }

    /// <summary>
    /// A wait at a mooring runs day by day but reads the smell and the voltmeter only when it ends, as one long stretch away would:
    /// nobody rings about them, so they are not worth a line every morning in the logbook.
    /// </summary>
    private bool _holdMorningReadings;

    /// <summary>On coming back aboard after an away period of a day or more: what built up while she sat.</summary>
    private void ArrivalAlerts()
    {
        if (!_holdPumpCounter) PumpCounterAlert();

        bool over = OverSole;
        if (!_holdMorningReadings) BilgeSmell(over);
        if (over) Raise("sole", SoleAlert);
        _alerts.WasOverSole = over;

        if (!_holdMorningReadings) Voltmeter();
        _alerts.LastMorningCheckDay = Day;
        MooringArrivalAlert();
        RaiseArrivalSigns();
    }

    private void BilgeSmell(bool over)
    {
        if (!over && _water.Level >= BilgeDeepLitres && !_removed.Contains(FloorboardsId))
            Raise("bilge-smell", "A damp, sour smell from under the saloon sole. The bilge wants looking at.");
    }

    private void Voltmeter()
    {
        if (!IsBatteryAshore && Electrical.StateOfCharge(_boat) < BatteryAlertBelow) Raise("battery", BatteryAlert());
    }

    /// <summary>
    /// Faults that woke while the player was away and have a sign to perceive (a hose seen through a gap): the sign is an alert, and
    /// the fault turns Symptomatic. Never a "Found:" line; finding it is still the player's inspection. Faults with no sign stay silent.
    /// </summary>
    private void RaiseArrivalSigns()
    {
        foreach (var f in _wokenAway)
        {
            if (f.ArrivalSign is not { Length: > 0 } sign || !f.Present || f.Discovered) continue;
            Raise($"sign-{f.Id}", sign);
            f.MarkSymptomatic();
        }
        _wokenAway.Clear();
    }
}
