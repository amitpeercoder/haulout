namespace Haulout.Sim.Systems;

/// <summary>
/// A name to number map the systems write every step and the spawn rules read ("water.bilgeLitres", "water.pumpCycles",
/// "water.pumpRunMinutes", "water.awashHours", "electrical.stateOfCharge", "electrical.charging", "electrical.dragStarts", "cooling.overheatMinutes"). A name nobody wrote reads as 0.
/// </summary>
public sealed class SystemValues
{
    public const string BilgeLitres = "water.bilgeLitres";
    public const string PumpCycles = "water.pumpCycles";
    public const string PumpRunMinutes = "water.pumpRunMinutes";
    public const string AwashHours = "water.awashHours";
    public const string StateOfCharge = "electrical.stateOfCharge";
    public const string Charging = "electrical.charging";
    public const string DragStarts = "electrical.dragStarts";
    public const string OverheatMinutes = "cooling.overheatMinutes";

    private readonly Dictionary<string, double> _values = new();

    public double Get(string name) => _values.GetValueOrDefault(name);

    public void Set(string name, double value) => _values[name] = value;
}
