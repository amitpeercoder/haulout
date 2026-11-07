namespace Haulout.Sim.Engine;

public sealed record FuelInputs(bool FuelAvailable, double Restriction, bool GlowPlugsWork, double InjectorCondition);

/// <summary>RawWaterFlow is a 0..1 fraction of healthy flow.</summary>
public sealed record CoolingInputs(double RawWaterFlow);

public sealed record EngineInputs(ElectricalInputs Electrical, FuelInputs Fuel, CoolingInputs Cooling);

public enum StartOutcome { Nothing, Click, SlowCrank, CranksNoFire, FiresAndDies, RunsRough, Runs }

/// <summary>Rule is the 1-based row of the rule table that matched.</summary>
public sealed record StartResult(
    StartOutcome Outcome,
    int Rule,
    ElectricalReading Electrical,
    bool OverheatWarning,
    string Description);

public static class EngineModel
{
    public const double DeadBatteryVolts = 10.5;
    public const double ClickBelowStarterVolts = 6.0;
    public const double SlowCrankBelowStarterVolts = 8.0;
    public const double FiresAndDiesRestriction = 0.85;
    public const double RunsRoughRestriction = 0.6;
    public const double RoughInjectorCondition = 0.3;
    public const double OverheatBelowFlow = 0.25;

    public static StartResult Attempt(EngineInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var reading = ElectricalChain.Crank(inputs.Electrical);
        var fuel = inputs.Fuel;

        (StartOutcome outcome, int rule) =
            !reading.CircuitClosed || inputs.Electrical.BatteryOpenCircuitVolts < DeadBatteryVolts ? (StartOutcome.Nothing, 1)
            : reading.StarterVolts < ClickBelowStarterVolts ? (StartOutcome.Click, 2)
            : reading.StarterVolts < SlowCrankBelowStarterVolts ? (StartOutcome.SlowCrank, 3)
            : !fuel.FuelAvailable || !fuel.GlowPlugsWork ? (StartOutcome.CranksNoFire, 4)
            : fuel.Restriction >= FiresAndDiesRestriction ? (StartOutcome.FiresAndDies, 5)
            : fuel.Restriction >= RunsRoughRestriction || fuel.InjectorCondition < RoughInjectorCondition ? (StartOutcome.RunsRough, 6)
            : (StartOutcome.Runs, 7);

        // A dead battery means nothing flows, whatever the loop looks like.
        if (outcome == StartOutcome.Nothing && reading.CircuitClosed)
        {
            reading = new ElectricalReading(false, 0.0, 0.0, inputs.Electrical.BatteryOpenCircuitVolts,
                reading.DropVolts.ToDictionary(kv => kv.Key, _ => 0.0));
        }

        bool running = outcome is StartOutcome.RunsRough or StartOutcome.Runs;
        bool overheat = running && inputs.Cooling.RawWaterFlow < OverheatBelowFlow;

        return new StartResult(outcome, rule, reading, overheat, Describe(outcome, overheat));
    }

    private static string Describe(StartOutcome outcome, bool overheat)
    {
        string text = outcome switch
        {
            StartOutcome.Nothing => "You turn the key. Nothing happens. The cockpit stays quiet and the cold sits in your hands.",
            StartOutcome.Click => "A single heavy click from the engine bay, and the panel lights dim and sag. The engine does not turn.",
            StartOutcome.SlowCrank => "The starter turns the engine over, but it is laboured and dragging, each stroke slower than the last.",
            StartOutcome.CranksNoFire => "The engine cranks briskly and evenly, round after round, and never catches. The smell of cold diesel stays out of the exhaust.",
            StartOutcome.FiresAndDies => "It catches, coughs twice, shudders, and dies. The silence afterwards is longer than it should be.",
            StartOutcome.RunsRough => "The engine starts and settles into a lumpy, hunting idle. The note rises and sags, and the cockpit shakes with it.",
            StartOutcome.Runs => "The engine catches and settles to a steady note. The cockpit hums quietly under the grey March light.",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        return overheat
            ? text + " No water spitting from the exhaust. The temperature alarm starts to whine."
            : text;
    }
}
