namespace Haulout.Sim.Faults;

/// <summary>The named factors a fault's progression rate can be multiplied by (design note 006).</summary>
public enum RateFactor { Exposure, Use, Neglect }

/// <summary>
/// How an active fault creeps toward its escalation. Per day: BaseRatePerDay * spread * product of the declared factors.
/// ExposedBy: ids of covers whose being off exposes this fault.
/// RainOnlyWhenOff: when not empty, rain counts toward the exposure factor (and the cause line) only on days one of these covers was off
/// at the end of the day; frost is unaffected.
/// Spread (design note 010, second pass F): [low, high], this fault's own range for its seeded spread, drawn from its own roll (salt 12) and
/// not from the world stream, so adding such a fault moves no other draw. Null: the world stream and the boat's range, as before.
/// </summary>
public sealed record ProgressionSpec(
    double BaseRatePerDay, IReadOnlyList<RateFactor> Factors, IReadOnlyList<string> ExposedBy, IReadOnlyList<string>? RainOnlyWhenOff = null,
    IReadOnlyList<double>? Spread = null);

/// <summary>What applied on one day to one fault. Recorded so the logbook can name the causes.</summary>
public sealed record DailyCauses(
    bool Rain, bool Frost, IReadOnlyList<string> CoversOff, bool AwayBeyondThree, bool BatteryLow, bool KnownOpen, double EngineHours, int DaysAway = 0);

/// <summary>A small per-fault memory: how many days each cause applied, and the last seven daily factor products.</summary>
public sealed class FaultHistory
{
    private const int Recent = 7;
    private readonly Queue<double> _products = new();
    private readonly Dictionary<string, int> _coverOffDays = new();

    public int Days { get; private set; }
    public int RainDays { get; private set; }
    public int FrostNights { get; private set; }
    public int AwayDays { get; private set; }
    /// <summary>The longest run of days away seen so far (days since the player was last aboard, at its longest).</summary>
    public int LongestAway { get; private set; }
    public int BatteryLowDays { get; private set; }
    public int KnownOpenDays { get; private set; }
    public int EngineDays { get; private set; }
    public double EngineHours { get; private set; }
    public IReadOnlyDictionary<string, int> CoverOffDays => _coverOffDays;
    public IReadOnlyCollection<double> LastProducts => _products;

    internal void Record(DailyCauses c, double product)
    {
        Days++;
        if (c.Rain) RainDays++;
        if (c.Frost) FrostNights++;
        if (c.AwayBeyondThree) AwayDays++;
        LongestAway = Math.Max(LongestAway, c.DaysAway);
        if (c.BatteryLow) BatteryLowDays++;
        if (c.KnownOpen) KnownOpenDays++;
        if (c.EngineHours > 0) { EngineDays++; EngineHours += c.EngineHours; }
        foreach (var id in c.CoversOff) _coverOffDays[id] = _coverOffDays.GetValueOrDefault(id) + 1;
        _products.Enqueue(product);
        while (_products.Count > Recent) _products.Dequeue();
    }

    /// <summary>Mean of the last seven daily products; null before the first day.</summary>
    public double? MeanProduct() => _products.Count == 0 ? null : _products.Average();
}
