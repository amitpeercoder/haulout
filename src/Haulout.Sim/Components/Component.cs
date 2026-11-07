namespace Haulout.Sim.Components;

/// <summary>
/// A physical part of the boat with a condition that degrades.
/// Condition is 0..1; 1 is new. What condition means for symptoms is the
/// job of each system's rules, not of this class.
/// </summary>
public sealed class Component
{
    public Component(string id, string name, double condition, WearRate wear)
    {
        if (condition is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(condition));
        Id = id;
        Name = name;
        Condition = condition;
        Wear = wear;
    }

    public string Id { get; }
    public string Name { get; }
    public double Condition { get; private set; }
    public WearRate Wear { get; }

    /// <summary>Advance wear by elapsed calendar days and engine/use hours.</summary>
    public void Age(int days, double useHours)
    {
        var loss = days * Wear.PerDay + useHours * Wear.PerUseHour;
        Condition = Math.Max(0, Condition - loss);
    }

    public void Replace() => Condition = 1;
}

/// <summary>Condition lost per calendar day (UV, salt, time) and per hour of use.</summary>
public readonly record struct WearRate(double PerDay, double PerUseHour)
{
    /// <summary>Lasts roughly <paramref name="years"/> of calendar time if never used.</summary>
    public static WearRate CalendarYears(double years, double perUseHour = 0) =>
        new(1.0 / (years * 360), perUseHour);
}
