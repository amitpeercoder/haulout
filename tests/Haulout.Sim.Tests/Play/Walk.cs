using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Play;

namespace Haulout.Sim.Tests.Play;

/// <summary>
/// Test helper: takes the player (through the facade alone) to a place where a named component can be touched,
/// breadth-first over the places' adjacency, taking off whatever cover is needed on the way.
/// The static reference boat is read for layout only and is never played.
/// </summary>
internal static class Walk
{
    private static readonly Boat Layout = BoatLoader.LoadSloop28();

    private static Component Find(string nameOrId) =>
        Layout.Components.FirstOrDefault(c =>
            c.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)
            || c.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)
            || c.Aliases.Any(a => a.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)))
        ?? throw new ArgumentException($"No component '{nameOrId}' in the layout.");

    public static string PlaceId(Game g)
    {
        var name = ((AboardView)g.Ask(new Aboard())).Place;
        return Layout.Places.Single(p => p.Name == name).Id;
    }

    private static bool IsOff(Game g, Cover cover) =>
        ((AboardView)g.Ask(new Aboard())).Covers.Single(c => c.Name == cover.Name).Removed;

    /// <summary>Walk to a place by its id, one accepted move at a time.</summary>
    public static void ToPlace(Game g, string placeId)
    {
        var from = PlaceId(g);
        if (from == placeId) return;
        var prev = new Dictionary<string, string> { [from] = from };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in Layout.GetPlace(at).Adjacent)
                if (prev.TryAdd(next, at)) queue.Enqueue(next);
        }
        var path = new List<string>();
        for (var at = placeId; at != from; at = prev[at]) path.Add(at);
        path.Reverse();
        foreach (var step in path)
        {
            var r = g.Execute(new GoTo(Layout.GetPlace(step).Name));
            Assert.True(r.Accepted, string.Join(" ", r.Narrative));
        }
    }

    /// <summary>Make sure the cover is off, going to it first if need be.</summary>
    public static void Uncover(Game g, string coverId)
    {
        var cover = Layout.GetCover(coverId);
        if (IsOff(g, cover)) return;
        ToPlace(g, cover.PlaceId);
        var r = g.Execute(new Remove(cover.Name));
        Assert.True(r.Accepted, string.Join(" ", r.Narrative));
    }

    /// <summary>Go where the component can be touched, with its cover off. Prefers a spot that is already open.</summary>
    public static void Near(Game g, string nameOrId)
    {
        var c = Find(nameOrId);
        var spots = c.Reach.Where(e => e.Reachable).ToList();
        var spot = spots.FirstOrDefault(e => e.CoverId is null || IsOff(g, Layout.GetCover(e.CoverId))) ?? spots[0];
        if (spot.CoverId is not null) Uncover(g, spot.CoverId);
        ToPlace(g, spot.PlaceId);
    }

    /// <summary>Go near the component, then run the command built from its name.</summary>
    public static CommandResult Do(Game g, string name, Func<string, Command> make)
    {
        Near(g, name);
        return g.Execute(make(name));
    }

    /// <summary>Start the engine from where the key is.</summary>
    public static CommandResult Start(Game g)
    {
        ToPlace(g, "cockpit");
        return g.Execute(new StartEngine());
    }
}
