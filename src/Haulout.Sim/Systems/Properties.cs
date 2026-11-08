using Haulout.Sim.Faults;

namespace Haulout.Sim.Systems;

/// <summary>
/// The effect graph's one reading rule (design note 006): a component's property is its live value with the property
/// effects of every present fault that targets it applied, in data order.
/// </summary>
public static class Properties
{
    /// <summary>
    /// The live value of <paramref name="name"/> on the component (its <see cref="Components.Component.State"/>, else
    /// <paramref name="fallback"/>), then each present fault's property effects on that component and property, in the
    /// order the faults appear in the boat's data.
    /// </summary>
    public static double Effective(Boat boat, string componentId, string name, double fallback)
    {
        double value = boat.TryGet(componentId, out var c) && c.State.TryGetValue(name, out var live) ? live : fallback;
        foreach (var fault in boat.AllFaults)
        {
            if (!fault.Present) continue;
            foreach (var e in fault.Effects)
            {
                if (e.Kind != FaultEffectKind.Property || e.Property != name) continue;
                if ((e.Component ?? fault.ComponentId) != componentId) continue;
                value = Apply(e.Op ?? PropertyOp.Set, value, e.Value);
            }
        }
        return value;
    }

    public static double Apply(PropertyOp op, double value, double operand) => op switch
    {
        PropertyOp.Set => operand,
        PropertyOp.Add => value + operand,
        PropertyOp.Multiply => value * operand,
        PropertyOp.Min => Math.Min(value, operand),
        _ => value,
    };
}
