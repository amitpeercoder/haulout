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
    /// <summary>Finding 8: how much of the raw-water impeller's vanes have been shed (1 minus its vane factor); a replaced impeller reads 0 again.</summary>
    public const string VanesShed = "cooling.vanesShed";
    /// <summary>Design note 008, the winter layup: frost hours with raw water left in the engine, damp cold nights in a part-empty tank, frosty nights with a flat battery aboard, snow days with no cover.</summary>
    public const string FrostHoursWithWater = "cooling.frostHoursWithWater";
    public const string CondensationDays = "fuel.condensationDays";
    public const string FrozenNights = "electrical.frozenNights";
    public const string SnowDays = "deck.snowDays";
    /// <summary>Design note 010, second pass G: how wet the plotter's plug has been since the plotter was fitted, and how chafed the scanner cable since the radar was.</summary>
    public const string PlugWetness = "deck.plugWetness";
    public const string ScannerChafe = "mast.scannerChafe";
    /// <summary>Design note 012, step 3: how wet the VHF antenna connector has been since the set was fitted.</summary>
    public const string ConnectorWetness = "mast.connectorWetness";

    private readonly Dictionary<string, double> _values = new();

    public double Get(string name) => _values.GetValueOrDefault(name);

    public void Set(string name, double value) => _values[name] = value;
}
