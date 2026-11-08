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

    /// <summary>
    /// Waits <paramref name="minutes"/> (already cut at a booked slot or a delivery) afloat at a port, a day at a time, and stops when she needs
    /// her owner. <paramref name="meant"/> finishes "You meant to ..." when it is cut short; <paramref name="complete"/> is said when it is not
    /// (null: nothing, the wait was cut by the calendar and said so); <paramref name="logComplete"/> is the logbook line then (null: "Waited N days.").
    /// </summary>
    private CommandResult DoWaitMoored(Turn t, string action, int minutes, string meant, string? complete, string? logComplete)
    {
        int startDay = Day;
        string stamp = TimeOfDay;
        int before = ClockMinutes;
        int logAt = _log.Count; // the "Waited" line goes where it always went: before the alerts of the days
        int waited = 0;
        int left = minutes;
        var stoppedBy = new List<string>();
        FriendWalks(t);

        _holdPumpCounter = true;
        _holdMorningReadings = true;
        _watchingBilge = true;
        try
        {
            while (left > 0)
            {
                var raisedBefore = new Dictionary<string, int>(_alerts.LastRaised);
                _bilgePassedToday = false;
                int chunk = Math.Min(1440, left);
                Tick(t, chunk, aboard: false);
                left -= chunk;
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
        bool stopped = left > 0 && !_sunk;
        string logText = !stopped && logComplete is not null ? logComplete : elapsed == 0 ? $"Waited until {TimeOfDay}." : $"Waited {Days(elapsed)}.";
        _log.Insert(logAt, new LogEntry(startDay, stamp, logText));

        Observation? obs = null;
        if (_sunk)
        {
            obs = _loss;
            t.Narrative.AddRange(_loss!.Lines);
        }
        else if (stopped)
        {
            bool phone = stoppedBy.Any(IsPhoneCall);
            t.Narrative.Add(phone
                ? $"You meant to {meant}; the phone rang on day {waited}."
                : $"You meant to {meant}; you came back after {waited} {(waited == 1 ? "day" : "days")}: something on her wants your attention.");
        }
        else if (complete is not null) t.Narrative.Add(complete);

        if (!_sunk || elapsed > 0)
        {
            var passed = Enumerable.Range(startDay, Math.Max(elapsed, 0)).Select(d => _weather[d]).ToList();
            if (passed.Count > 0) t.Narrative.Insert(t.Narrative.Count - (_sunk ? _loss!.Lines.Count : 0), WeatherWords.Summarise(passed));
        }
        if (!_sunk)
        {
            CheckEvents(t); // she lies at a port, afloat, and the player is back aboard: an event of today is judged
            AddWeekLine(t);
        }

        // A condition that went on for days (water over the sole, a falling voltmeter) raised the same alert every morning: said once.
        var told = TakeUnread().DistinctBy(a => a.Text).ToList();
        return new CommandResult(true, action, t.Narrative.ToList(), ClockMinutes - before, 0, obs, t.Newly.ToList(), _running, _finished, told);
    }
}
