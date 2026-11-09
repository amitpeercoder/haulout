using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;

namespace Haulout.Sim.Play;

/// <summary>
/// The VHF, the radio ticket and Call (design note 012, step 3, decisions 4 and 8). A set fitted at the chart table lets the player call the harbour,
/// the coast radio and a ferry. A fog or night close call that a ferry would have reported becomes the ferry's call on channel 16: answer it
/// (<see cref="Call"/> "ferry") and she passes astern, with nothing reported; move on without answering and the close call happens as it would
/// have. A set with a corroded antenna connector receives and cannot be heard. A call made without the radio ticket is reported by the coast radio
/// (the harbourmaster's letter with the next morning's post, once a day at most). Nothing here draws a random number: the close call's own rolls
/// (salt 11) decide whether a ferry calls, and every outcome after that is the player's.
/// </summary>
public sealed partial class Game
{
    /// <summary>A ferry's call on channel 16, waiting to be answered: the close call it stands in for (told, with its report and letter, if the player moves on).</summary>
    internal sealed class PendingFerry
    {
        public string Ferry { get; init; } = "";
        public string Where { get; init; } = "";
        public string Narration { get; init; } = "";
        public string? Report { get; init; }
        public CloseCallCause Cause { get; init; } = null!;
        public int Day { get; init; }
        public int Hour { get; init; }
        public double FerryPick { get; init; }
        /// <summary>The call has been put into the narrative (the hour that rolled it).</summary>
        public bool Told { get; set; }
    }

    private RadioRules Radio => _boat.Radio;

    /// <summary>A set is fitted (sound or not).</summary>
    private bool VhfFitted => Radio.Enabled && _boat.IsFitted(VhfId);

    /// <summary>The truth: the set is fitted and the connector is sound.</summary>
    private bool VhfSound => VhfFitted && UpgradeWorks(VhfId);

    /// <summary>What a plan may assume: a set is fitted and the player knows of no fault on it.</summary>
    private bool VhfKnownFine(int day) => VhfFitted && UpgradeWorks(VhfId, day, knownOnly: true);

    /// <summary>The radio ticket is held, or the boat has no tickets (a boat written in a test): then nobody asks.</summary>
    private bool RadioLicensed => TicketData.Find("radio") is not { } ticket || Holds(ticket);

    /// <summary>True while a Call is being made: the ferry's pending call is not given up by the minutes the call itself takes.</summary>
    private bool _inCall;

    private int _radioReports;
    private readonly HashSet<int> _radioReportDays = [];

    // ------------------------------------------------------------------ the ferry's call

    /// <summary>The ferry's call, in words (told in the hour that rolled it, and in the trip query while it waits).</summary>
    private string FerryCallLine(PendingFerry f) => Fill(Radio.Ferry.Call, ("where", f.Where), ("ferry", f.Ferry));

    /// <summary>The player did not answer (or could not be heard): the close call happens as it would have without a set, with its report and its letter.</summary>
    private void ReleaseFerry(Passage p)
    {
        if (p.Ferry is not { } f) return;
        p.Ferry = null;
        p.CloseCalls.Add((f.Narration, f.Report));
        _ferryReports++;
        if (f.Cause.Letter) QueueLetter(f.Cause, f.Day, f.Hour, f.FerryPick);
    }

    /// <summary>Called by the roll: with a set aboard a ferry's close call is her call first. Returns true when the call took the close call's place.</summary>
    private bool OfferFerryCall(Passage p, CloseCallCause cause, CloseCallNarration narration, int day, int whole, double ferryPick)
    {
        if (!VhfFitted || p.Ferry is not null) return false;
        var rules = _boat.CloseCalls;
        string ferry = rules.Ferries.Count == 0 ? "a ferry" : rules.Ferries[Math.Min(rules.Ferries.Count - 1, (int)(ferryPick * rules.Ferries.Count))];
        p.Ferry = new PendingFerry
        {
            Ferry = ferry, Where = "near " + NearestAnchorage(p).Name, Narration = narration.Text, Report = cause.ReportLine,
            Cause = cause, Day = day, Hour = whole, FerryPick = ferryPick,
        };
        return true;
    }

    /// <summary>Tells a call that has not been told yet (after the hour line).</summary>
    private void TellFerryCall(Turn t, Passage p)
    {
        if (p.Ferry is not { Told: false } f) return;
        f.Told = true;
        string line = FerryCallLine(f);
        t.Narrative.Add(line);
        _log.Add(new LogEntry(Day, TimeOfDay, line));
        MarkTrack(line);
    }

    // ------------------------------------------------------------------ Call

    private enum CallTarget { None, Harbour, Coast, Ferry }

    private static CallTarget ResolveTarget(string to)
    {
        string s = (to ?? "").Trim().ToLowerInvariant();
        if (s.Contains("ferry", StringComparison.Ordinal)) return CallTarget.Ferry;
        if (s.Contains("coast", StringComparison.Ordinal) || s.Contains("check", StringComparison.Ordinal)) return CallTarget.Coast;
        if (s.Contains("harbour", StringComparison.Ordinal) || s.Contains("harbor", StringComparison.Ordinal) || s.Contains("port", StringComparison.Ordinal)
            || s.Contains("marina", StringComparison.Ordinal) || s.Contains("quay", StringComparison.Ordinal)) return CallTarget.Harbour;
        return CallTarget.None;
    }

    /// <summary>The harbour the call reaches: the one she lies at, or at sea the nearer end of the leg.</summary>
    private Port? HarbourCalled()
    {
        if (_passage is { } p)
        {
            bool onward = p.ProgressNm * 2 > p.Route.DistanceNm;
            return _boat.Trips?.FindPort(onward ? p.Route.To : p.Route.From);
        }
        return CurrentPort;
    }

    private string Identity(Turn t)
    {
        string id = Fill(Radio.Identity, ("boat", Radio.BoatName));
        if (RadioLicensed) return id + Fill(Radio.WithSign, ("callSign", Radio.CallSign));
        t.Narrative.Add(Radio.NoSign);
        return id;
    }

    private CommandResult DoCall(Turn t, string to)
    {
        const string action = "call";
        if (!VhfFitted) return Reject(action, Radio.Enabled ? Radio.NoRadio : "You have no radio aboard.");
        if (!Afloat) return Reject(action, "She is out of the water: there is nobody to call from the hard.");
        var target = ResolveTarget(to);
        if (target == CallTarget.None) return Reject(action, "Call whom? The harbour, a ferry, or the coast radio.");
        var pending = _passage?.Ferry;
        if (target == CallTarget.Ferry && pending is null) return Reject(action, Radio.Ferry.Nobody);

        bool heard = VhfSound;
        int minutes = Radio.CallMinutes;
        string log;
        string station;
        switch (target)
        {
            case CallTarget.Ferry:
                station = "the ferry " + pending!.Ferry;
                break;
            case CallTarget.Coast:
                station = "coast radio";
                break;
            default:
                station = (HarbourCalled()?.Name ?? "the harbour") + " harbour";
                break;
        }
        string working = Radio.HarbourChannel.ToString(CultureInfo.InvariantCulture);
        string channel = Radio.Channel.ToString(CultureInfo.InvariantCulture);

        // The call and what comes back. A set that cannot be heard still hears: the coast radio's forecast comes through.
        string Say(string template, params (string Key, string Value)[] more) =>
            Fill(template, [("station", station), ("boat", Radio.BoatName), ("channel", channel), ("working", working), ("ferry", pending?.Ferry ?? ""), .. more]);
        if (!heard)
        {
            if (target == CallTarget.Coast) t.Narrative.Add(Radio.Unheard + " " + Say(Radio.Coast.Forecast, ("forecast", ForecastWords())));
            else t.Narrative.Add(Radio.Unheard);
            log = target switch
            {
                CallTarget.Ferry => $"Called the ferry {pending!.Ferry} on channel 16: no answer.",
                CallTarget.Coast => "Called the coast radio: no answer.",
                _ => $"Called {station}: no answer.",
            };
            foreach (var f in _boat.TryGetUpgrade(VhfId, out var up) ? up.Also.SelectMany(c => c.ActiveFaults) : []) f.MarkSymptomatic();
        }
        else
        {
            string id = Identity(t);
            switch (target)
            {
                case CallTarget.Ferry:
                    t.Narrative.Add(Say(Radio.Ferry.Answer, ("identity", id)));
                    log = Say(Radio.Ferry.Log);
                    _passage!.Ferry = null;
                    break;
                case CallTarget.Coast:
                    t.Narrative.Add(Say(Radio.Coast.Call, ("identity", id)));
                    t.Narrative.Add(Say(Radio.Coast.Answer));
                    t.Narrative.Add(Say(Radio.Coast.Forecast, ("forecast", ForecastWords())));
                    log = "Called the coast radio: loud and clear; heard the forecast.";
                    break;
                default:
                    t.Narrative.Add(Say(Radio.Harbour.Call, ("identity", id)));
                    if (HarbourCalled() is { Anchorage: true } anchorage)
                    {
                        t.Narrative.Add(Say(Radio.Harbour.NoOffice, ("port", anchorage.Name)));
                        log = $"Called {station}: nobody to answer.";
                    }
                    else
                    {
                        string line = Radio.Harbour.Lines.Count == 0 ? "" : Radio.Harbour.Lines[Day % Radio.Harbour.Lines.Count];
                        t.Narrative.Add(Say(Radio.Harbour.Answer, ("line", line)));
                        log = $"Called {station}: they answered.";
                    }
                    break;
            }
            // Heard without the ticket: the coast radio logs it. One report a day at most.
            if (!RadioLicensed && ReportUnlicensedCall(t)) t.Narrative.Add(Radio.ReportedNote);
        }

        _inCall = true;
        try
        {
            return Accept(t, action, minutes, 0, null, log, work: false);
        }
        finally { _inCall = false; }
    }

    /// <summary>The forecast as the coast radio reads it: today's and tomorrow's, as the player's own forecast has them.</summary>
    private string ForecastWords()
    {
        var today = ForecastFor(Day, []);
        var tomorrow = ForecastFor(Day + 1, []);
        return $"Today: {today.Text} Tomorrow: {tomorrow.Text}";
    }

    // ------------------------------------------------------------------ the report

    /// <summary>
    /// A call that was heard, made without the radio ticket: the coast radio reports it, the harbourmaster's letter comes with the next morning's post (or
    /// on arrival, at sea), billed from economy.json, with a mark on the reputation. Deterministic. Once a day: a second call the same day is the same report.
    /// Returns true when this call was the day's report (so the note is told once).
    /// </summary>
    private bool ReportUnlicensedCall(Turn t)
    {
        if (!_radioReportDays.Add(Day)) return false;
        _radioReports++;
        var rules = Radio;
        string time = TimeOfDay;
        string Fill2(string template) => Fill(template, ("day", Day.ToString(CultureInfo.InvariantCulture)), ("time", time),
            ("channel", rules.Channel.ToString(CultureInfo.InvariantCulture)), ("nok", Money(rules.UnlicensedLetterNok)));
        _letters.Add(new PendingLetter(++_lettersWritten, Day, Fill2(rules.Letter.Text), Fill2(rules.Letter.Alert), "", rules.UnlicensedLetterNok, rules.Letter.BillText));
        return true;
    }

    // ------------------------------------------------------------------ what the plan and the weather say

    /// <summary>What a plan adds to its fog and night lines when a set is fitted and no fault on it is known; empty otherwise.</summary>
    private string VhfPlanSentence(int day)
    {
        if (!VhfKnownFine(day) || Radio.Plan.WithSet.Length == 0) return "";
        return " " + Radio.Plan.WithSet + (RadioLicensed ? Radio.Plan.NoTicketNone : Radio.Plan.NoTicket);
    }

    /// <summary>The coast radio's line for the weather report: with a set aboard and no known fault, the forecast it reads (the player's own forecast, tomorrow's).</summary>
    private string? CoastRadioLine()
    {
        if (!VhfKnownFine(Day) || Radio.Weather.Length == 0) return null;
        return Fill(Radio.Weather, ("channel", Radio.ForecastChannel.ToString(CultureInfo.InvariantCulture)), ("hour", Radio.ForecastHour),
            ("forecast", "tomorrow: " + ForecastFor(Day + 1, []).Text));
    }
}
