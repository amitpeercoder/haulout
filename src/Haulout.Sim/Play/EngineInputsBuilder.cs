using Haulout.Sim.Components;
using Haulout.Sim.Engine;
using Haulout.Sim.Faults;
using Haulout.Sim.Systems;

namespace Haulout.Sim.Play;

/// <summary>The one place that turns the boat's components and faults into plain numbers for the engine rules.</summary>
internal static class EngineInputsBuilder
{
    public const string StarterId = "starter-motor";
    public const string ImpellerId = "impeller";
    public const string VaneFactorProperty = "vaneFactor";
    public const string PrimaryFilterId = "primary-filter";
    public const string WaterMinutesProperty = "waterMinutes";
    /// <summary>The fuel restriction of water that has reached the filter: past fires-and-dies, so the engine will not stay running.</summary>
    public const double WaterRestriction = 0.9;

    public static readonly string[] LoopOrder =
    [
        "positive-terminal", "main-positive-cable", "battery-switch", "starter-solenoid",
        "starter-motor", "engine-ground-stud", "negative-cable", "negative-terminal",
    ];

    public const string FuelTankId = "fuel-tank";
    public const string FuelLitresProperty = "fuelLitres";
    /// <summary>The restriction of an empty tank: fully restricted, so the engine rules' own words apply (design note 010).</summary>
    public const double EmptyRestriction = 1.0;

    /// <summary>The tank is empty: the boat burns fuel at all (the data says so) and the tank is below the data's empty line.</summary>
    public static bool FuelEmpty(Boat boat) =>
        boat.Fuel.Burns && boat.TryGet(FuelTankId, out var tank)
        && tank.State.TryGetValue(FuelLitresProperty, out var litres) && litres < boat.Fuel.EmptyBelowLitres;

    public static EngineInputs Build(Boat boat)
    {
        var loop = LoopOrder.Select(id =>
        {
            var c = boat.Get(id);
            double ohms = Properties.Effective(boat, id, "seriesOhms", c.SeriesOhms ?? 0.0);
            foreach (var f in c.ActiveFaults)
            {
                if (f.Effect.Kind == FaultEffectKind.OpenCircuit) ohms = double.PositiveInfinity;
                else if (f.Effect.Kind == FaultEffectKind.SeriesOhms) ohms += f.Effect.Value;
            }
            return new SeriesElement(c.Id, c.Name, ohms);
        }).ToList();

        var battery = boat.Get("start-battery");
        double soc = Electrical.StateOfCharge(boat);

        var electrical = new ElectricalInputs(
            SwitchOn: boat.Get("battery-switch").Properties.GetValueOrDefault("on") == 1,
            PanelLive: !boat.Get("ignition-panel").ActiveFaults.Any(f => f.Effect.Kind == FaultEffectKind.OpenCircuit),
            BatteryOpenCircuitVolts: ElectricalChain.OpenCircuitVolts(soc),
            BatteryInternalOhms: Properties.Effective(boat, "start-battery", "internalOhms", battery.Properties.GetValueOrDefault("internalOhms")),
            Loop: loop,
            StarterId: StarterId);

        var activeFaults = boat.AllFaults.Where(f => f.Present).ToList();

        bool fuelAvailable =
            boat.Get("fuel-tank").Properties.GetValueOrDefault("litres") >= 2
            && boat.Get("fuel-shutoff").Properties.GetValueOrDefault("open") == 1
            && boat.Get("lift-pump").Condition > 0.2
            && !activeFaults.Any(f => f.Effect.Kind == FaultEffectKind.NoFuel);

        double restriction = activeFaults
            .Where(f => f.Effect.Kind == FaultEffectKind.FuelRestriction)
            .Select(f => f.Effect.Value).DefaultIfEmpty(0.0).Max();

        if (FuelEmpty(boat)) restriction = Math.Max(restriction, EmptyRestriction);

        // Stirred-up water from the tank has reached the primary filter (design note 007).
        if (boat.TryGet(PrimaryFilterId, out _)
            && Properties.Effective(boat, PrimaryFilterId, WaterMinutesProperty, 0) >= boat.SeaRules.StirHoursToBite * 60)
            restriction = Math.Max(restriction, WaterRestriction);

        var glow = boat.Get("glow-plugs");
        var fuel = new FuelInputs(
            fuelAvailable, restriction,
            GlowPlugsWork: glow.Condition >= 0.15 && !glow.ActiveFaults.Any(f => f.Effect.Kind == FaultEffectKind.OpenCircuit),
            InjectorCondition: boat.Get("injectors").Condition);

        double flow = boat.Get("raw-water-seacock").Properties.GetValueOrDefault("open") == 1 ? 1.0 : 0.0;
        foreach (var f in activeFaults.Where(f => f.Effect.Kind == FaultEffectKind.RawWaterFlowFactor))
            flow *= f.Effect.Value;
        flow *= Properties.Effective(boat, ImpellerId, VaneFactorProperty, 1.0); // vanes shed under load at sea

        return new EngineInputs(electrical, fuel, new CoolingInputs(flow));
    }
}
