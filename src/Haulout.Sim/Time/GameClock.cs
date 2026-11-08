namespace Haulout.Sim.Time;

/// <summary>
/// Simulation time. Days since the game started; the season is derived.
/// Never use DateTime.Now in the sim.
/// </summary>
public readonly record struct GameClock(int Day)
{
    public const int DaysPerYear = 360;
    public const int DaysPerMonth = 30;
    /// <summary>Day 0 is a Monday (design note 010). 360 is not a multiple of 7, so a day of the year falls on a different weekday each year.</summary>
    public const int DaysPerWeek = 7;

    public int Year => Day / DaysPerYear;
    public int DayOfYear => Day % DaysPerYear;
    public int Month => DayOfYear / DaysPerMonth; // 0 = January

    /// <summary>0 Monday to 6 Sunday.</summary>
    public int Weekday => Day % DaysPerWeek;
    /// <summary>Saturday and Sunday: the boat days.</summary>
    public bool IsWeekend => Weekday >= 5;

    private static readonly string[] WeekdayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    public string WeekdayName => WeekdayNames[Weekday];

    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>"January" to "December" (months are 30 days).</summary>
    public string MonthName => MonthNames[Month];

    public Season Season => Month switch
    {
        >= 11 or <= 1 => Season.Winter,
        >= 2 and <= 4 => Season.Spring,
        >= 5 and <= 7 => Season.Summer,
        _ => Season.Autumn,
    };

    public GameClock AddDays(int days) => new(Day + days);
}

public enum Season { Winter, Spring, Summer, Autumn }
