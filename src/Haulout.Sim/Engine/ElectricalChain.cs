namespace Haulout.Sim.Engine;

/// <summary>One resistance in the starter loop. <c>double.PositiveInfinity</c> is an open circuit.</summary>
public sealed record SeriesElement(string Id, string Name, double Ohms);

/// <summary>Loop excludes the battery internal resistance and includes the starter.</summary>
public sealed record ElectricalInputs(
    bool SwitchOn,
    bool PanelLive,
    double BatteryOpenCircuitVolts,
    double BatteryInternalOhms,
    IReadOnlyList<SeriesElement> Loop,
    string StarterId);

/// <summary>DropVolts is keyed by element id and equals I * R.</summary>
public sealed record ElectricalReading(
    bool CircuitClosed,
    double CurrentAmps,
    double StarterVolts,
    double BatteryTerminalVolts,
    IReadOnlyDictionary<string, double> DropVolts);

public static class ElectricalChain
{
    public static double OpenCircuitVolts(double stateOfCharge)
        => 11.8 + 0.8 * Math.Clamp(stateOfCharge, 0.0, 1.0);

    public static ElectricalReading Crank(ElectricalInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var drops = new Dictionary<string, double>();
        foreach (var e in inputs.Loop)
        {
            drops[e.Id] = 0.0;
        }

        bool open = inputs.Loop.Any(e => double.IsPositiveInfinity(e.Ohms));
        bool closed = inputs.SwitchOn && inputs.PanelLive && !open;
        if (!closed)
        {
            return new ElectricalReading(false, 0.0, 0.0, inputs.BatteryOpenCircuitVolts, drops);
        }

        double total = inputs.BatteryInternalOhms + inputs.Loop.Sum(e => e.Ohms);
        double current = total > 0 ? inputs.BatteryOpenCircuitVolts / total : 0.0;

        foreach (var e in inputs.Loop)
        {
            drops[e.Id] = current * e.Ohms;
        }

        double starterVolts = inputs.Loop
            .Where(e => e.Id == inputs.StarterId)
            .Sum(e => current * e.Ohms);

        return new ElectricalReading(
            true,
            current,
            starterVolts,
            inputs.BatteryOpenCircuitVolts - current * inputs.BatteryInternalOhms,
            drops);
    }
}
