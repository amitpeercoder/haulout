using System.Globalization;
using Haulout.Sim.Faults;
using Haulout.Sim.Systems;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The time driver (design note 006). Every minute the game spends passes through <see cref="Tick"/>, the only caller of the clock's
/// Advance. Hourly steps run the systems; each midnight runs <see cref="Daily"/>: wear, fault progression, escalation.
/// </summary>
public sealed partial class Game
{
    private const int MaxStepMinutes = 60;

    /// <summary>
    /// The cause line of a fault that woke while the player was away, by the id of the fault that woke. It is told at the moment the
    /// player finds the fault (design note 006, away-escalation fix), never at the midnight it happened.
    /// </summary>
    private readonly Dictionary<string, string> _pendingCause = [];
    /// <summary>Faults that woke while the player was away and have not yet shown their arrival sign.</summary>
    private readonly List<Fault> _wokenAway = [];

    /// <summary>Work minutes ticked since the last midnight; more than zero means the player was aboard that day.</summary>
    private int _workMinutesToday;
    /// <summary>Minutes the engine ran today, while the player was aboard.</summary>
    private int _engineMinutesToday;
    /// <summary>The last day on which the player was aboard (the start day counts).</summary>
    private int _lastAboardDay;

    /// <summary>
    /// Pass <paramref name="minutes"/> of game time. Aboard: the player's own work. Not aboard: the player is away.
    /// Steps stop at every midnight so the day's bookkeeping lands on the right day.
    /// </summary>
    private void Tick(Turn t, int minutes, bool aboard)
    {
        int left = minutes;
        while (left > 0)
        {
            int stepDay = Day;
            int toMidnight = 1440 - (StartMinuteOfDay + ClockMinutes) % 1440;
            int step = Math.Min(left, Math.Min(MaxStepMinutes, toMidnight));
            _time.Advance(step, aboard);
            if (aboard)
            {
                _workMinutesToday += step;
                if (_running) _engineMinutesToday += step;
            }
            bool atSea = AtSea;
            // A weekday at sea is always the player's day; aboard in port, more than an evening's work makes it one.
            if (atSea) ChargeDay(t, stepDay, atSea: true);
            else if (aboard && _workMinutesToday > _boat.Calendar.EveningMinutes) ChargeDay(t, stepDay, atSea: false);
            if (atSea) SeaBefore(step);
            SystemsStep(step);
            if (_sunk) return; // she is on the bottom: the rest of the time does not pass
            Spawn(aboard);
            if (atSea) SeaAfter(t, step);
            if (aboard) PresentAlerts(step, atSea);
            if (aboard)
            {
                _water.ResetSinceAboard();
                RaiseArrivalSigns(); // she is here to be seen now
            }
            WeekStep(t); // the friend's arrival and last night come at their hour, wherever the player is
            left -= step;
            if ((StartMinuteOfDay + ClockMinutes) % 1440 == 0) Daily(t, Day - 1, aboard);
        }
        CountAwake(minutes, aboard);
        if (!aboard && minutes >= 1440) ArrivalAlerts(); // "on coming back aboard"
    }

    /// <summary>Everything that happens once the day <paramref name="day"/> is over.</summary>
    /// <param name="present">The player is aboard in the tick that crossed this midnight (not merely earlier that day).</param>
    private void Daily(Turn t, int day, bool present)
    {
        bool aboard = _workMinutesToday > 0;
        if (aboard) _lastAboardDay = day;
        double engineHours = _engineMinutesToday / 60.0;

        foreach (var c in _boat.Components) c.Age(1, engineHours);

        var state = DayState(day, aboard, engineHours);
        foreach (var f in _boat.AllFaults.Where(f => f.Present && f.Progression is not null).ToList())
        {
            if (!f.Present) continue;
            var (product, causes) = Factors(f, state);
            f.History.Record(causes, product);
            f.Progress += f.Progression!.BaseRatePerDay * (f.Spread ?? 1.0) * product;
            if (f.Escalation is not null && f.Progress >= _boat.Rates.Threshold) Escalate(t, f, present, withCause: true);
        }

        CalendarDaily(day);
        DeliverLetters(t, day + 1);
        SeasonDaily(t, day, present);
        EventsDaily(t, day);

        _workMinutesToday = 0;
        _engineMinutesToday = 0;
    }

    // ------------------------------------------------------------------ spread

    /// <summary>A fault became active: draw its spread, once, from the world random. Faults with no progression draw nothing.</summary>
    private void DrawSpread(Fault f)
    {
        if (f.Progression is null || f.ActiveSinceDay is null || f.Spread is not null) return;
        var r = _boat.Rates;
        // A progression with its own range rolls it on its own salt: no draw of the world stream, so no other fault's spread moves.
        if (f.Progression.Spread is { Count: 2 } own)
        {
            f.Spread = own[0] + SeasonRoll(SaltFaultSpread, StableHash(f.Id), 0) * (own[1] - own[0]);
            return;
        }
        f.Spread = r.SpreadLo + _world.NextDouble() * (r.SpreadHi - r.SpreadLo);
    }

    // ------------------------------------------------------------------ factors

    /// <summary>What the day looked like, for the factors.</summary>
    private readonly record struct DayStateValues(
        int Day, bool Aboard, double EngineHours, int DaysAway, int DaysAwayBeyondThree, bool BatteryLow, int KnownOpen, bool Rain, bool Frost, double RainTerm, double FrostTerm);

    private DayStateValues DayState(int day, bool aboard, double engineHours)
    {
        int away = aboard ? 0 : Math.Max(0, day - _lastAboardDay);
        return new DayStateValues(day, aboard, engineHours, away, Math.Max(0, away - 3), BatteryLow(), OpenFaults().Count(),
            RainDay(day), FrostNight(day), RainTerm(day), FrostTerm(day));
    }

    /// <summary>The rain weighting for the day from the day's weather.</summary>
    private double RainTerm(int day)
    {
        if (UnderWinterCover) return 0; // the cover keeps the deck dry
        var e = _boat.Rates.Exposure;
        return e.RainWeight * Math.Min(_weather[day].RainMm, e.RainCapMm) / e.RainCapMm;
    }

    /// <summary>The frost weighting for the day.</summary>
    private double FrostTerm(int day) => _weather[day].NightC < 0 ? _boat.Rates.Exposure.FrostWeight : 0;

    /// <summary>Rain counts for this fault only when its progression asks for no cover, or one of the named covers is off now.</summary>
    private bool RainCounts(ProgressionSpec spec) => spec.RainOnlyWhenOff is not { Count: > 0 } c || c.Any(_removed.Contains);

    private bool RainDay(int day) => !UnderWinterCover && WeatherWords.IsRaining(_weather[day]);
    private bool FrostNight(int day) => _weather[day].NightC < 0;
    /// <summary>The start battery below half charge.</summary>
    private bool BatteryLow() => !IsBatteryAshore && Electrical.StateOfCharge(_boat) < 0.5;

    /// <summary>The product of the fault's declared factors for a day, and the causes that applied.</summary>
    private (double Product, DailyCauses Causes) Factors(Fault f, DayStateValues s)
    {
        var spec = f.Progression!;
        var r = _boat.Rates;
        double product = 1.0;
        bool rain = false, frost = false, away = false, low = false, known = false;
        double hours = 0;
        var coversOff = new List<string>();

        if (spec.Factors.Contains(RateFactor.Exposure))
        {
            coversOff.AddRange(spec.ExposedBy.Where(_removed.Contains));
            bool rainCounts = RainCounts(spec);
            product *= (1 + (rainCounts ? s.RainTerm : 0) + s.FrostTerm) * (1 + r.Exposure.PerOpenCover * coversOff.Count);
            rain = s.Rain && rainCounts;
            frost = s.Frost;
        }
        if (spec.Factors.Contains(RateFactor.Use))
        {
            product *= 1 + r.Use.PerEngineHour * s.EngineHours;
            hours = s.EngineHours;
        }
        if (spec.Factors.Contains(RateFactor.Neglect))
        {
            int leftOff = s.Aboard ? 0 : r.Neglect.Covers.Count(_removed.Contains);
            // Known problems left open: the others, not this one (the fault itself is what the player is neglecting).
            int othersOpen = Math.Max(0, s.KnownOpen - (f is { Present: true, Discovered: true } ? 1 : 0));
            double neglect = r.Neglect.PerKnownOpenFault * othersOpen
                + (s.BatteryLow ? r.Neglect.BatteryLow : 0)
                + r.Neglect.PerCoverLeftOff * leftOff
                + r.Neglect.PerDayAwayBeyond3 * s.DaysAwayBeyondThree;
            product *= 1 + Math.Min(1, neglect);
            away = s.DaysAwayBeyondThree > 0;
            low = s.BatteryLow;
            known = othersOpen > 0;
        }
        return (product, new DailyCauses(rain, frost, coversOff, away, low, known, hours, s.DaysAway));
    }

    // ------------------------------------------------------------------ readiness range

    /// <summary>
    /// How soon a known problem will probably get worse. The ceiling is the trailing mean product (else today's) over the slow end of the spread.
    /// The floor is the worst case of leaving her as she is: every day full rain and frost, the covers as they are now, neglect computed as if
    /// the player were away from today (current covers counted as left off, the days-away term growing day by day), the fast end of the spread.
    /// Only the fault's declared factors count. Null when it never gets worse.
    /// </summary>
    private (int? Min, int? Max) LikelyDays(Fault f)
    {
        if (f.Progression is not { } spec || f.Escalation is null) return (null, null);
        var r = _boat.Rates;
        double m = f.History.MeanProduct()
            ?? Factors(f, DayState(Day, _workMinutesToday > 0, _engineMinutesToday / 60.0)).Product;
        double rate = spec.BaseRatePerDay * m;
        if (rate <= 0) return (null, null);
        double remaining = Math.Max(0, r.Threshold - f.Progress);
        int max = (int)Math.Ceiling(remaining / (rate * (spec.Spread is { Count: 2 } own ? own[0] : r.SpreadLo)));

        // Worst case, day by day. Day k from now is the one whose midnight run adds that day's progress.
        int lastAboard = _workMinutesToday > 0 ? Day : _lastAboardDay;
        double exposure = 1.0, neglectBase = 0;
        if (spec.Factors.Contains(RateFactor.Exposure))
            exposure = (1 + (RainCounts(spec) ? r.Exposure.RainWeight : 0) + r.Exposure.FrostWeight)
                * (1 + r.Exposure.PerOpenCover * spec.ExposedBy.Count(_removed.Contains));
        bool neglects = spec.Factors.Contains(RateFactor.Neglect);
        if (neglects)
        {
            int othersOpen = Math.Max(0, OpenFaults().Count() - (f is { Present: true, Discovered: true } ? 1 : 0));
            neglectBase = r.Neglect.PerKnownOpenFault * othersOpen
                + (BatteryLow() ? r.Neglect.BatteryLow : 0)
                + r.Neglect.PerCoverLeftOff * r.Neglect.Covers.Count(_removed.Contains);
        }
        double progress = f.Progress;
        int min = 0;
        while (progress < r.Threshold && min < 2000)
        {
            double neglect = neglects
                ? 1 + Math.Min(1, neglectBase + r.Neglect.PerDayAwayBeyond3 * Math.Max(0, Day + min - lastAboard - 3))
                : 1.0;
            progress += spec.BaseRatePerDay * (spec.Spread is { Count: 2 } own2 ? own2[1] : r.SpreadHi) * exposure * neglect;
            min++;
        }
        return (Math.Min(min, max), Math.Max(max, Math.Min(min, max)));
    }

    // ------------------------------------------------------------------ cause line

    private static readonly string[] SmallNumbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    private static string Number(int n) => n is >= 0 and <= 20 ? SmallNumbers[n] : n.ToString(CultureInfo.InvariantCulture);

    private static string Counted(int n, string one, string many) => $"{Number(n)} {(n == 1 ? one : many)}";

    private static string JoinAnd(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>"The raw-water seacock has seized. It came on after twelve days of rain, with the floorboards up and the hatch open."</summary>
    private string CauseLine(Fault from, Fault to)
    {
        var h = from.History;
        // A cause counts when it applied on at least a third of the days, and on at least two.
        bool Applies(int days) => days >= 2 && days * 3 >= h.Days;

        var after = new List<string>();
        if (Applies(h.RainDays)) after.Add(Counted(h.RainDays, "day of rain", "days of rain"));
        if (Applies(h.FrostNights)) after.Add(Counted(h.FrostNights, "night of frost", "nights of frost"));
        if (Applies(h.EngineDays))
        {
            int hours = Math.Max(1, (int)Math.Round(h.EngineHours));
            after.Add(Counted(hours, "hour of running", "hours of running"));
        }

        var with = new List<string>();
        foreach (var id in from.Progression?.ExposedBy ?? [])
            if (h.CoverOffDays.TryGetValue(id, out var days) && Applies(days) && _boat.Covers.FirstOrDefault(c => c.Id == id) is { } cover)
                with.Add(cover.OffPhrase.Length > 0 ? cover.OffPhrase : $"the {Words.CoverName(cover)} off");
        if (Applies(h.AwayDays)) with.Add($"no one aboard for {Counted(h.LongestAway, "day", "days")}");
        if (Applies(h.BatteryLowDays)) with.Add("the battery low");
        if (Applies(h.KnownOpenDays)) with.Add("known problems left open");

        var parts = new List<string>();
        if (after.Count > 0) parts.Add("after " + JoinAnd(after));
        if (with.Count > 0) parts.Add("with " + JoinAnd(with));
        string came = parts.Count == 0 ? "It came on with time alone." : "It came on " + string.Join(", ", parts) + ".";
        return $"{LeadSentence(from, to)} {came}";
    }

    /// <summary>The first clause of the escalation text ("The raw-water seacock has seized"), or the new fault's name.</summary>
    private static string LeadSentence(Fault from, Fault to)
    {
        var text = from.Escalation?.Text ?? "";
        int cut = text.IndexOfAny([':', '.']);
        var lead = (cut > 0 ? text[..cut] : text).Trim();
        return lead.Length > 0 ? lead + "." : $"{to.Name}.";
    }
}
