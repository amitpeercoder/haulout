using Haulout.Sim.Faults;

namespace Haulout.Sim.Systems;

/// <summary>
/// The water model (design note 006): ingress from every component's effective leak and rain properties, a float-switch
/// bilge pump drawing on the start battery, the level, and the hours spent awash. One call per step. Sinking is the
/// facade's business: this class only says how much water is in her.
/// </summary>
internal sealed class WaterSystem
{
    public const string PumpId = "bilge-pump";
    public const string LeakProperty = "leakLitresPerHour";
    public const string RainProperty = "rainLitresPerMm";
    public const string CyclesState = "cycles";
    public const string RunMinutesState = "runMinutes";

    private readonly Boat _boat;
    /// <summary>Components that can let water in at all: the rest are skipped every step.</summary>
    private List<string> _ingress;

    public WaterSystem(Boat boat)
    {
        _boat = boat;
        Level = boat.Water.StartLitres;
        _ingress = IngressIds();
    }

    /// <summary>An upgrade was fitted (design note 010, step 3): the list of components that can let water in is made again.</summary>
    public void Refresh() => _ingress = IngressIds();

    private List<string> IngressIds() => _boat.Components
        .Where(c => c.State.ContainsKey(LeakProperty) || c.State.ContainsKey(RainProperty)
            || _boat.AllFaults.Any(f => f.Effects.Any(e =>
                e.Kind == FaultEffectKind.Property && e.Property is LeakProperty or RainProperty && (e.Component ?? f.ComponentId) == c.Id)))
        .Select(c => c.Id).ToList();

    /// <summary>Litres in the bilge.</summary>
    public double Level { get; set; }
    /// <summary>The float switch has the pump running.</summary>
    public bool PumpOn { get; private set; }
    /// <summary>Hours spent with the level above the sole.</summary>
    public double AwashHours { get; private set; }
    /// <summary>Pump cycles since the player was last aboard: the arrival alerts (step 5) read it, the next aboard step resets it.</summary>
    public int CyclesSinceAboard { get; set; }
    /// <summary>Litres that came in through the open companionway hatch (rain) since the player was last aboard.</summary>
    public double HatchLitresSinceAboard { get; private set; }
    /// <summary>Litres of rain that came in through components (deck, ports, hoses) since the player was last aboard.</summary>
    public double RainLitresSinceAboard { get; private set; }
    /// <summary>Litres of seawater through leaks (and spray) since the player was last aboard.</summary>
    public double SeaLitresSinceAboard { get; private set; }

    /// <summary>The player is aboard and has read the counter: the next stretch away counts from here.</summary>
    public void ResetSinceAboard()
    {
        CyclesSinceAboard = 0;
        HatchLitresSinceAboard = 0;
        RainLitresSinceAboard = 0;
        SeaLitresSinceAboard = 0;
    }

    /// <summary>The hatch let in more than every other source put together since the player was last aboard.</summary>
    public bool HatchDominant => HatchLitresSinceAboard > RainLitresSinceAboard + SeaLitresSinceAboard;

    public int PumpCycles => _boat.TryGet(PumpId, out var p) ? (int)p.State.GetValueOrDefault(CyclesState) : 0;
    public double PumpRunMinutes => _boat.TryGet(PumpId, out var p) ? p.State.GetValueOrDefault(RunMinutesState) : 0;

    /// <summary>What a step needs to know of the world. RainMm is the day's rain; the step takes its share of 24 hours.</summary>
    public readonly record struct Inputs(int Minutes, bool Afloat, double RainMm, bool HatchOpen, bool FuseIn, int SeaState = 0);

    /// <summary>The start battery is in its box (design note 008: a battery in the yard's battery room powers nothing).</summary>
    public bool BatteryAboard { get; set; } = true;

    /// <summary>The pump has current: its fuse is in and the battery is above the floor.</summary>
    public bool PumpPowered(bool fuseIn) =>
        BatteryAboard && fuseIn && _boat.TryGet(PumpId, out _) && Electrical.StateOfCharge(_boat) > _boat.Electrical.PumpMinStateOfCharge;

    public void Step(Inputs input)
    {
        var rules = _boat.Water;
        int minutes = input.Minutes;
        if (minutes <= 0) return;
        double hours = minutes / 60.0;

        // Ingress, litres per minute over the step.
        double sea = 0, hatchPerMm = input.HatchOpen ? rules.RainThroughOpenHatchLitresPerMm : 0, componentPerMm = 0;
        foreach (var id in _ingress)
        {
            if (input.Afloat) sea += Math.Max(0, Properties.Effective(_boat, id, LeakProperty, 0));
            componentPerMm += Math.Max(0, Properties.Effective(_boat, id, RainProperty, 0));
        }
        double perMm = hatchPerMm + componentPerMm;
        // Under way (design note 007): the sea slops at every leak, and spray finds the open hatch.
        var seaRules = _boat.SeaRules;
        sea *= 1 + seaRules.SlopPerSeaState * input.SeaState;
        if (input.Afloat && input.HatchOpen && input.SeaState >= seaRules.SprayFromSeaState)
            sea += seaRules.SprayLitresPerHourPerSeaState * input.SeaState;
        double rate = (sea * hours + perMm * input.RainMm * hours / 24.0) / minutes;
        HatchLitresSinceAboard += hatchPerMm * input.RainMm * hours / 24.0;
        RainLitresSinceAboard += componentPerMm * input.RainMm * hours / 24.0;
        SeaLitresSinceAboard += sea * hours;

        // The pump, with its float switch: on at onLitres, off at offLitres.
        bool pumpFitted = _boat.TryGet(PumpId, out var pump);
        bool powered = PumpPowered(input.FuseIn);
        double lpm = pumpFitted ? Properties.Effective(_boat, PumpId, "litresPerMinute", 0) : 0;
        double on = pumpFitted ? Properties.Effective(_boat, PumpId, "onLitres", 15) : 0;
        double off = pumpFitted ? Properties.Effective(_boat, PumpId, "offLitres", 5) : 0;
        bool canPump = powered && lpm > 0 && on > off;

        double level = Level, left = minutes, runMinutes = 0;
        int cycles = 0;
        if (!canPump) PumpOn = false;
        for (int guard = 0; left > 1e-9 && guard < 500; guard++)
        {
            if (!PumpOn)
            {
                if (canPump && level >= on) { PumpOn = true; cycles++; }
                else if (!canPump || rate <= 0) { level += rate * left; left = 0; }
                else
                {
                    double toOn = (on - level) / rate;
                    if (toOn >= left) { level += rate * left; left = 0; }
                    else { level = on; left -= toOn; PumpOn = true; cycles++; }
                }
            }
            else
            {
                double net = lpm - rate;
                if (net <= 1e-9) { level = Math.Max(0, level - net * left); runMinutes += left; left = 0; }
                else
                {
                    double toOff = Math.Max(0, (level - off) / net);
                    if (toOff >= left) { level -= net * left; runMinutes += left; left = 0; }
                    else { level = Math.Min(level, off); runMinutes += toOff; left -= toOff; PumpOn = false; }
                }
            }
        }

        Level = input.Afloat ? Math.Max(0, level) : Math.Clamp(level, 0, rules.SinkLitres);
        if (Level > rules.SoleLitres) AwashHours += hours;

        if (pumpFitted && (cycles > 0 || runMinutes > 0))
        {
            pump!.State[CyclesState] = pump.State.GetValueOrDefault(CyclesState) + cycles;
            pump.State[RunMinutesState] = pump.State.GetValueOrDefault(RunMinutesState) + runMinutes;
            CyclesSinceAboard += cycles;
            Electrical.Draw(_boat,
                cycles * Properties.Effective(_boat, PumpId, "chargePerCycle", 0)
                + runMinutes * Properties.Effective(_boat, PumpId, "chargePerRunMinute", 0));
        }
    }

    /// <summary>Publishes the water and battery numbers for the spawn rules.</summary>
    public void Publish(SystemValues values)
    {
        values.Set(SystemValues.BilgeLitres, Level);
        values.Set(SystemValues.PumpCycles, PumpCycles);
        values.Set(SystemValues.PumpRunMinutes, PumpRunMinutes);
        values.Set(SystemValues.AwashHours, AwashHours);
        values.Set(SystemValues.StateOfCharge, Electrical.StateOfCharge(_boat));
    }
}
