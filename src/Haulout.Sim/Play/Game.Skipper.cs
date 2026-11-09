using System.Globalization;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// The skipper's log (design note 012, decision 2): the record the tickets read. <see cref="SkipperLog"/> is plain data; this file is the one place
/// that feeds it, from the same events as the boat's log and the season page: the end of a passage, the midnight, the tow, the autumn page.
/// What a passage is: from casting off to arriving at a port (a passage is always one leg). A turn back is not a passage; its miles, night hours and
/// wind are logged all the same. A towed arrival counts its miles but is not a passage over any length as skipper. A passage ran through a night
/// when it crossed a midnight under way. Night hours are the hours under way (not at anchor) after dark by the daylight table the nav-lights close
/// calls use. A day aboard is a day the player worked aboard or was at sea. Nothing here draws a random number.
/// </summary>
public sealed partial class Game
{
    private SkipperLog _skipper = new();
    /// <summary>The tows already taken when this season began: a season is "without a tow" when none came since.</summary>
    private int _skipperTowsAtSeasonStart;

    /// <summary>For tests only: the log as the game keeps it (a player reads the Skipper query).</summary>
    internal SkipperLog SkipperLogForTests => _skipper;

    /// <summary>For tests only: write the log a scenario needs (300 miles takes a summer to sail); the game feeds it as usual from there.</summary>
    internal void SetSkipperLogForTests(SkipperLog log) => _skipper = log;

    // ------------------------------------------------------------------ feeding

    /// <summary>A passage has ended (arrived, turned back or towed): its miles, its night hours, the wind, and, if it counts, the passage.</summary>
    private void LogPassage(Passage p, bool towed)
    {
        double nm = p.CoveredNm;
        double nightHours = p.NightMinutes / 60.0;
        var log = _skipper with
        {
            MilesUnderWay = _skipper.MilesUnderWay + nm,
            NightHoursUnderWay = _skipper.NightHoursUnderWay + nightHours,
            StrongestForce = Math.Max(_skipper.StrongestForce, p.MaxForce),
        };
        if (!p.TurnedBack && !towed)
        {
            double hours = (p.EndClockMinutes - p.DepartClockMinutes) / 60.0;
            bool overnight = DayAt(p.EndClockMinutes) > DayAt(p.DepartClockMinutes);
            log = log.WithPassage(new LoggedPassage(nm, hours, nightHours, overnight, PassageRole.Skipper));
        }
        _skipper = log;
    }

    /// <summary>A midnight on which she lay away from home, or at sea.</summary>
    private void SkipperNight() => _skipper = _skipper with { NightsAway = _skipper.NightsAway + 1 };

    /// <summary>The midnight that ends a day on which the player was aboard (worked aboard or was at sea).</summary>
    private void SkipperDay(bool aboard)
    {
        if (aboard) _skipper = _skipper with { DaysAboard = _skipper.DaysAboard + 1 };
    }

    /// <summary>The autumn page is written: the season counts as one without a tow when some miles were sailed and no tow was called.</summary>
    private void SkipperSeasonEnds(double seasonMiles)
    {
        if (seasonMiles > 0 && _skipper.Tows == _skipperTowsAtSeasonStart)
            _skipper = _skipper with { SeasonsWithoutTow = _skipper.SeasonsWithoutTow + 1 };
    }

    // ------------------------------------------------------------------ in words

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private string SkipperWord(string key) => _boat.Tickets.Skipper.TryGetValue(key, out var w) ? w : "";

    private static string Fill(string template, params (string Key, string Value)[] tokens)
    {
        foreach (var (key, value) in tokens) template = template.Replace("{" + key + "}", value, StringComparison.Ordinal);
        return template;
    }

    /// <summary>The log as words, a line per fact, no score. The lines for a passage length are named for the lengths in tickets.json.</summary>
    private List<string> SkipperLines(bool shortBlock = false)
    {
        var ci = CultureInfo.InvariantCulture;
        var log = _skipper;
        var lines = new List<string>();
        int miles = (int)Math.Floor(log.MilesUnderWay);
        lines.Add(miles <= 0 ? SkipperWord("milesNone") : Fill(SkipperWord("miles"), ("n", miles.ToString(ci))));
        lines.Add(log.NightsAway == 0 ? SkipperWord("nightsNone")
            : Fill(SkipperWord("nights"), ("n", log.NightsAway.ToString(ci)), ("nights", log.NightsAway == 1 ? "night" : "nights")));
        int nightHours = (int)Math.Floor(log.NightHoursUnderWay);
        lines.Add(nightHours <= 0 ? SkipperWord("nightHoursNone")
            : Fill(SkipperWord("nightHours"), ("n", nightHours.ToString(ci)), ("hours", nightHours == 1 ? "hour" : "hours")));
        foreach (int nm in _boat.Tickets.Club.PassageNm)
        {
            int all = log.PassagesOver(nm), asSkipper = log.PassagesOver(nm, true), night = log.PassagesOverThroughANight(nm);
            if (all == 0)
            {
                if (!shortBlock || nm == _boat.Tickets.Club.PassageNm.FirstOrDefault())
                    lines.Add(Fill(SkipperWord("passagesNone"), ("nm", nm.ToString(ci))));
                continue;
            }
            int crew = log.PassagesOverAsCrew(nm);
            if (crew > 0)
            {
                // Passages as crew on another boat are told apart from the ones made as skipper (design note 012, step 4).
                foreach (var (count, role, word) in new[] { (asSkipper, PassageRole.Skipper, "asSkipper"), (crew, PassageRole.Crew, "asCrew") })
                {
                    if (count == 0) continue;
                    int k = log.PassagesOverThroughANight(nm, role);
                    lines.Add(Fill(SkipperWord(k > 0 ? "passagesNight" : "passages"), ("n", count.ToString(ci)), ("passages", count == 1 ? "passage" : "passages"),
                        ("nm", nm.ToString(ci)), ("skipper", SkipperWord(word)), ("k", k.ToString(ci))));
                }
                continue;
            }
            string skipper = asSkipper == all ? SkipperWord("asSkipper") : "";
            string key = night > 0 ? "passagesNight" : "passages";
            lines.Add(Fill(SkipperWord(key), ("n", all.ToString(ci)), ("passages", all == 1 ? "passage" : "passages"), ("nm", nm.ToString(ci)),
                ("skipper", skipper), ("k", night.ToString(ci))));
        }
        lines.Add(log.StrongestForce <= 0 ? SkipperWord("windNone") : Fill(SkipperWord("wind"), ("n", log.StrongestForce.ToString(ci))));
        if (shortBlock) return lines;
        lines.Add(log.DaysAboard == 0 ? SkipperWord("daysNone")
            : Fill(SkipperWord("days"), ("n", log.DaysAboard.ToString(ci)), ("days", log.DaysAboard == 1 ? "day" : "days")));
        lines.Add(log.Tows == 0 ? SkipperWord("towsNone")
            : Fill(SkipperWord("tows"), ("n", log.Tows.ToString(ci)), ("tows", log.Tows == 1 ? "tow" : "tows")));
        lines.Add(log.SeasonsWithoutTow == 0 ? SkipperWord("seasonsNone")
            : Fill(SkipperWord("seasons"), ("n", log.SeasonsWithoutTow.ToString(ci)), ("seasons", log.SeasonsWithoutTow == 1 ? "season" : "seasons")));
        lines.Add(log.FirstAidHeld ? SkipperWord("firstAid") : SkipperWord("firstAidNone"));
        return lines;
    }

    private SkipperView SkipperSnapshot() => new(_boat.Tickets.Enabled ? SkipperLines() : ["You keep no log of your own yet."]);
}
