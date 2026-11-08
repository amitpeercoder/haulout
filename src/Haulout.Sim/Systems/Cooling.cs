namespace Haulout.Sim.Systems;

/// <summary>
/// The cooling side (design note 006, step 4): while the engine runs, a raw-water flow below
/// <see cref="Engine.EngineModel.OverheatBelowFlow"/> adds the step's minutes to the overheat clock. The flow itself is the
/// engine inputs builder's (seacock, impeller, faults); this class only keeps the minutes.
/// </summary>
internal sealed class CoolingSystem
{
    /// <summary>Minutes the engine has run too hot (no cooling water), for the whole game.</summary>
    public double OverheatMinutes { get; private set; }

    public void Step(int minutes, bool running, double rawWaterFlow)
    {
        if (running && minutes > 0 && rawWaterFlow < Engine.EngineModel.OverheatBelowFlow) OverheatMinutes += minutes;
    }

    public void Publish(SystemValues values) => values.Set(SystemValues.OverheatMinutes, OverheatMinutes);
}
