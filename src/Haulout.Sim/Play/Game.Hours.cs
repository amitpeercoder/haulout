using Haulout.Sim.Data;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// The shops' hours and sleep (design note 012, Amit's answers). Shops and the yard office keep hours (the fuel pump does not); the
/// harbourmaster does not ring in the night. Nothing stops at any hour: the skipper has hours awake, a rate, and tiredness makes own work
/// likelier to come out poor and the subtle signs easier to miss. Nothing here draws a random number; every number is in <c>economy.json</c>.
/// </summary>
public sealed partial class Game
{
    private HoursRules DayHours => _boat.Hours;
    private SleepRules SleepRule => _boat.Sleep;

    private int MinuteOfDay => (StartMinuteOfDay + ClockMinutes) % 1440;

    // ------------------------------------------------------------------ the harbourmaster's night

    /// <summary>After the night begins and before the harbourmaster's first call: she does not ring now.</summary>
    private bool InNight(int minuteOfDay) =>
        DayHours.Enabled && (minuteOfDay > DayHours.NightFromMinute || minuteOfDay < DayHours.HarbourmasterFirstCallMinute);

    // ------------------------------------------------------------------ shop and office hours

    private static string HourWords(int minuteOfDay) =>
        minuteOfDay % 60 == 0 ? Number(minuteOfDay / 60) : $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

    /// <summary>Why a shop or the yard office is shut now, in words that name the hour it opens; null when it is open. Free, like every refusal.</summary>
    private string? OpenRefusal(string place)
    {
        var h = DayHours;
        if (!h.Enabled) return null;
        int day = Day, now = MinuteOfDay;
        var today = h.ShopOn(new GameClock(day).Weekday);
        if (!today.IsShut && now >= today.OpenMinute && now < today.CloseMinute) return null;
        string name = Capital(place);
        if (!today.IsShut && now < today.OpenMinute) return $"{name} opens at {HourWords(today.OpenMinute)}.";
        string? next = null;
        for (int d = day + 1; d <= day + 7 && next is null; d++)
        {
            var open = h.ShopOn(new GameClock(d).Weekday);
            if (open.IsShut) continue;
            next = d == day + 1 ? $"at {HourWords(open.OpenMinute)} tomorrow" : $"at {HourWords(open.OpenMinute)} on {new GameClock(d).WeekdayName}";
        }
        if (next is null) return $"{name} is shut.";
        return today.IsShut
            ? $"{name} is shut on {new GameClock(day).WeekdayName}s; it opens {next}."
            : $"{name} has closed for the day; it opens {next}.";
    }

    private string? ShopOpenRefusal() => OpenRefusal(ShopName);

    private string? OfficeOpenRefusal() => OpenRefusal("the yard office");

    /// <summary>Replacing in port without a spare buys the part at the shop: the shop's hours. Null when a spare is aboard or nothing is bought.</summary>
    private string? ReplaceBuyHoursRefusal(Components.Component c)
    {
        if (!DayHours.Enabled || SpareFor(c.Id) is not null) return null;
        var (cost, refusal) = ReplacePartHere(c);
        return refusal is null && cost > 0 ? ShopOpenRefusal() : null;
    }

    // ------------------------------------------------------------------ hours awake

    private enum Tiredness { Fresh, Tired, Exhausted }

    /// <summary>Minutes since the skipper last slept. Runs on own work ashore and at anchor; not under way at sea (the watches take turns).</summary>
    private int _awakeMinutes;

    private Tiredness TirednessNow =>
        !SleepRule.Enabled ? Tiredness.Fresh
        : _awakeMinutes >= SleepRule.ExhaustedAtMinutes ? Tiredness.Exhausted
        : _awakeMinutes >= SleepRule.TiredAtMinutes ? Tiredness.Tired
        : Tiredness.Fresh;

    private double BotchFactor => TirednessNow switch
    {
        Tiredness.Exhausted => SleepRule.BotchFactorExhausted,
        Tiredness.Tired => SleepRule.BotchFactorTired,
        _ => 1.0,
    };

    /// <summary>For tests only (the player never sees a number): minutes since the skipper slept, and the factor on the botch chance.</summary>
    internal int AwakeMinutesForTests => _awakeMinutes;
    internal double BotchFactorForTests => BotchFactor;

    private int HoursAwake => _awakeMinutes / 60;

    /// <summary>"You've been up 17 hours: tired." in words, no score; null when fresh.</summary>
    private string? TiredWords() => TirednessNow switch
    {
        Tiredness.Exhausted => $"You've been up {HoursAwake} hours: exhausted, your eyes are not to be trusted.",
        Tiredness.Tired => $"You've been up {HoursAwake} hours: tired.",
        _ => null,
    };

    private const string TiredJobLine = "You're tired; this is the kind of job that gets done twice.";
    private const string ExhaustedJobLine = "You're exhausted; this job will almost certainly have to be done twice.";
    private const string EyesLine = "Your eyes are not to be trusted tonight.";

    /// <summary>Called after time has passed: the awake count runs on, or an away stretch of sleeping length resets it.</summary>
    private void CountAwake(int minutes, bool aboard)
    {
        if (!SleepRule.Enabled) return;
        if (!aboard && minutes >= SleepRule.MinSleepMinutes) _awakeMinutes = 0;
        else if (NotUnderWay) _awakeMinutes += minutes;
    }

    private CommandResult DoSleep(Turn t)
    {
        const string action = "sleep";
        if (!SleepRule.Enabled) return Reject(action, "There's no need to turn in.");
        if (!NotUnderWay) return Reject(action, "You're under way: heave to or anchor first.");
        int toWake = (SleepRule.WakeMinute - MinuteOfDay + 1440) % 1440;
        int minutes = toWake >= SleepRule.MinSleepMinutes ? toWake : SleepRule.SleepMinutes;
        int wakeDay = DayAt(ClockMinutes + minutes);
        string up = FormatTime(ClockMinutes + minutes);
        t.Narrative.Add($"You turn in. {new GameClock(wakeDay).WeekdayName}, day {wakeDay}: up at {up}.");
        return Accept(t, action, minutes, 0, null, $"Slept until {up}.", work: false);
    }
}
