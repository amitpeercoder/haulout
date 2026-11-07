using Haulout.Sim.Components;
using Haulout.Sim.Faults;

namespace Haulout.Sim;

public sealed record Compartment(string Id, string Name, IReadOnlyList<string> Aliases, string Description);

public sealed class Boat
{
    private readonly Dictionary<string, Component> _byId = new();

    public Boat(string name, IEnumerable<Compartment> compartments, IEnumerable<Component> components)
    {
        Name = name;
        Compartments = compartments.ToList();
        Components = components.ToList();

        var compartmentIds = new HashSet<string>();
        foreach (var c in Compartments)
            if (!compartmentIds.Add(c.Id))
                throw new ArgumentException($"Duplicate compartment id '{c.Id}'.");

        foreach (var c in Components)
        {
            if (!_byId.TryAdd(c.Id, c))
                throw new ArgumentException($"Duplicate component id '{c.Id}'.");
            if (!compartmentIds.Contains(c.Location))
                throw new ArgumentException($"Component '{c.Id}' is in unknown compartment '{c.Location}'.");
            foreach (var f in c.Faults)
                if (f.ComponentId != c.Id)
                    throw new ArgumentException($"Fault '{f.Id}' names component '{f.ComponentId}' but is owned by '{c.Id}'.");
        }
    }

    public string Name { get; }
    public IReadOnlyList<Component> Components { get; }
    public IReadOnlyList<Compartment> Compartments { get; }

    public Component Get(string id) =>
        _byId.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"No component '{id}'.");

    public bool TryGet(string id, out Component c) => _byId.TryGetValue(id, out c!);

    public IEnumerable<Component> In(string compartmentId) =>
        Components.Where(c => c.Location == compartmentId);

    public IEnumerable<Fault> AllFaults => Components.SelectMany(c => c.Faults);
}
