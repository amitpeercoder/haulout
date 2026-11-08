using Haulout.Sim.Faults;

namespace Haulout.Sim.Systems;

/// <summary>The start battery's live charge: self-discharge, cranking, and the engine's charge and draw while it runs.</summary>
public static class Electrical
{
    public const string BatteryId = "start-battery";
    public const string ChargeProperty = "stateOfCharge";
    public const string SelfDischargeProperty = "selfDischargePerDay";
    public const string AlternatorId = "alternator";
    public const string ChargeFactorProperty = "chargeFactor";

    /// <summary>
    /// The charge everything sees: the live value (property effects applied), capped by the legacy low-charge fault.
    /// A boat with no start battery reads as full.
    /// </summary>
    public static double StateOfCharge(Boat boat)
    {
        if (!boat.TryGet(BatteryId, out var battery)) return 1.0;
        double soc = Properties.Effective(boat, BatteryId, ChargeProperty, 1.0);
        foreach (var f in battery.ActiveFaults)
            if (f.Effect.Kind == FaultEffectKind.StateOfCharge) soc = Math.Min(soc, f.Effect.Value);
        return Math.Clamp(soc, 0.0, 1.0);
    }

    /// <summary>Takes charge out of the battery (the live value, not the cap).</summary>
    public static void Draw(Boat boat, double amount)
    {
        if (amount <= 0 || !boat.TryGet(BatteryId, out var battery)) return;
        battery.State[ChargeProperty] = Math.Max(0, battery.State.GetValueOrDefault(ChargeProperty, 1.0) - amount);
    }

    /// <summary>
    /// Takes charge out of what the panel shows: the live value is first brought down to the effective charge (the cap of a battery
    /// that can only hold 60 %), so a load on a capped battery is seen at once. Used by the draw of a passage (design note 007).
    /// </summary>
    public static void DrawFromEffective(Boat boat, double amount)
    {
        if (amount <= 0 || !boat.TryGet(BatteryId, out var battery)) return;
        double live = Math.Min(battery.State.GetValueOrDefault(ChargeProperty, 1.0), StateOfCharge(boat));
        battery.State[ChargeProperty] = Math.Max(0, live - amount);
    }

    /// <summary>Full again: shore-power charging.</summary>
    public static void Fill(Boat boat)
    {
        if (boat.TryGet(BatteryId, out var battery)) battery.State[ChargeProperty] = 1.0;
    }

    /// <summary>Self-discharge over <paramref name="minutes"/>, at the battery's effective rate per day.</summary>
    public static void Step(Boat boat, int minutes)
    {
        double perDay = Properties.Effective(boat, BatteryId, SelfDischargeProperty, boat.Electrical.SelfDischargePerDay);
        Draw(boat, perDay * minutes / 1440.0);
    }

    /// <summary>The alternator's effective charge factor: 1 when healthy, 0 with a slipping belt. A boat with no alternator charges nothing.</summary>
    public static double ChargeFactor(Boat boat) =>
        boat.TryGet(AlternatorId, out _) ? Math.Max(0, Properties.Effective(boat, AlternatorId, ChargeFactorProperty, 1.0)) : 0.0;

    /// <summary>A start attempt that cranked takes its share of the charge.</summary>
    public static void Crank(Boat boat) => Draw(boat, boat.Electrical.CrankDraw);

    /// <summary>
    /// While the engine runs: the alternator puts back <c>chargePerHour</c> times its factor, the running boat takes
    /// <c>runDrawPerHour</c>. Returns the factor that was charging (0 when the engine is off).
    /// </summary>
    public static double Run(Boat boat, int minutes, bool running)
    {
        if (!running || minutes <= 0 || !boat.TryGet(BatteryId, out var battery)) return 0.0;
        var rules = boat.Electrical;
        double factor = ChargeFactor(boat);
        double delta = (rules.ChargePerHour * factor - rules.RunDrawPerHour) * minutes / 60.0;
        battery.State[ChargeProperty] = Math.Clamp(battery.State.GetValueOrDefault(ChargeProperty, 1.0) + delta, 0.0, 1.0);
        return factor;
    }
}
