using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The harbourmaster (design note 008, finding 3 in 002): a plain wait while she lies afloat at a port is not blind. It runs a day at a
/// time, like the wait for spring, and stops after the day on which the bilge passed the harbourmaster's level, the lines parted or
/// something else that wants hands came up. Nothing here is a new rule of the boat: it only lets the player hear what a marina would say.
/// </summary>
public sealed partial class Game
{
    private const string HarbourmasterKind = "harbourmaster";

    /// <summary>
    /// The alert kinds that bring the player back from a wait at a mooring: a new fault's sign, the lines parting, the chafe warning, water
    /// over the sole, a slot moved or missed, a viewing offer, and the harbourmaster. Not the voltmeter, not the pump counter (either): those
    /// are things the player sees on coming aboard, and nobody rings about them.
    /// </summary>
    private static bool MooringStops(string kind) =>
        kind.StartsWith("sign-", StringComparison.Ordinal) || kind is
            HarbourmasterKind or "mooring-parted" or "chafe" or "sole" or "viewing" or "slot-moved" or "slot-missed";

    /// <summary>The ones that arrive as a phone call.</summary>
    private static bool IsPhoneCall(string kind) =>
        kind is HarbourmasterKind or "mooring-parted" or "viewing" or "slot-moved" or "slot-missed";

    /// <summary>Waits <paramref name="days"/> days (already cut at a booked slot) afloat at a port, one day at a time, and stops when she needs her owner.</summary>
    private CommandResult DoWaitMoored(Turn t, int days)
    {
        const string action = "wait";
        int startDay = Day;
        string stamp = TimeOfDay;
        int before = ClockMinutes;
        int logAt = _log.Count; // the "Waited" line goes where it always went: before the alerts of the days
        int waited = 0;
        var stoppedBy = new List<string>();

        _holdPumpCounter = true;
        _holdMorningReadings = true;
        _watchingBilge = true;
        try
        {
            while (waited < days)
            {
                var raisedBefore = new Dictionary<string, int>(_alerts.LastRaised);
                _bilgePassedToday = false;
                Tick(t, 1440, aboard: false);
                if (_sunk) break;
                waited++;
                if (_bilgePassedToday) Raise(HarbourmasterKind, _boat.Water.HarbourmasterText);
                if (FreshAlert(raisedBefore, MooringStops))
                {
                    stoppedBy = _alerts.LastRaised.Where(kv => MooringStops(kv.Key) && kv.Value == Day).Select(kv => kv.Key).ToList();
                    if (stoppedBy.Count == 0) stoppedBy = _alerts.LastRaised.Keys.Where(MooringStops).ToList();
                    break;
                }
            }
        }
        finally
        {
            _holdPumpCounter = false;
            _holdMorningReadings = false;
            _watchingBilge = false;
        }
        if (!_sunk)
        {
            PumpCounterAlert(); // whatever the counter built up while she sat, read once
            BilgeSmell(OverSole);
            Voltmeter();
        }

        int elapsed = Day - startDay;
        string logText = $"Waited {Days(elapsed)}.";
        _log.Insert(logAt, new LogEntry(startDay, stamp, logText));

        Observation? obs = null;
        if (_sunk)
        {
            obs = _loss;
            t.Narrative.AddRange(_loss!.Lines);
        }
        else if (waited < days)
        {
            bool phone = stoppedBy.Any(IsPhoneCall);
            t.Narrative.Add(phone
                ? $"You meant to leave her for {Days(days)}; the phone rang on day {waited}."
                : $"You meant to leave her for {Days(days)}; you came back on day {waited}: something on her wants your attention.");
        }
        else t.Narrative.Add($"You leave her for {Days(days)}.");

        if (!_sunk || elapsed > 0)
        {
            var left = Enumerable.Range(startDay, Math.Max(elapsed, 0)).Select(d => _weather[d]).ToList();
            if (left.Count > 0) t.Narrative.Insert(t.Narrative.Count - (_sunk ? _loss!.Lines.Count : 0), WeatherWords.Summarise(left));
        }

        // A condition that went on for days (water over the sole, a falling voltmeter) raised the same alert every morning: said once.
        var told = TakeUnread().DistinctBy(a => a.Text).ToList();
        return new CommandResult(true, action, t.Narrative.ToList(), ClockMinutes - before, 0, obs, t.Newly.ToList(), _running, _finished, told);
    }
}
