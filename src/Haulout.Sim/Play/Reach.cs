using Haulout.Sim.Components;

namespace Haulout.Sim.Play;

/// <summary>
/// What the player can see and touch from a place, given which covers are off.
/// A reach entry applies when its cover is null or removed.
/// </summary>
internal sealed class Reach
{
    private readonly Boat _boat;
    private readonly IReadOnlySet<string> _removed;

    public Reach(Boat boat, IReadOnlySet<string> removedCovers)
    {
        _boat = boat;
        _removed = removedCovers;
    }

    private bool Applies(ReachEntry e) => e.CoverId is null || _removed.Contains(e.CoverId);

    public bool Visible(Component c, string placeId) =>
        c.Reach.Any(e => e.PlaceId == placeId && e.Visible && Applies(e));

    public bool Reachable(Component c, string placeId) =>
        c.Reach.Any(e => e.PlaceId == placeId && e.Reachable && Applies(e));

    public List<Component> VisibleAt(string placeId) =>
        _boat.Components.Where(c => Visible(c, placeId)).ToList();

    public List<Component> ReachableAt(string placeId) =>
        _boat.Components.Where(c => Reachable(c, placeId)).ToList();

    /// <summary>
    /// Why the player cannot see (or, with <paramref name="needTouch"/>, touch) a component they have found,
    /// from this place. Null when they can.
    /// </summary>
    public string? Refusal(Component c, string placeId, bool needTouch)
    {
        var name = c.Name.ToLowerInvariant();
        if (!Visible(c, placeId)) return $"You can't see the {name} from here.";
        if (!needTouch || Reachable(c, placeId)) return null;

        var here = $"You can see the {name} from here but can't get at it";
        var reachable = c.Reach.Where(e => e.Reachable).ToList();

        // A reachable spot at this very place, with its cover still on.
        if (reachable.FirstOrDefault(e => e.PlaceId == placeId && e.CoverId is not null) is { } gated)
            return $"{here}; {InTheWay(gated.CoverId!)}.";

        // Somewhere else: prefer a spot that is open now, otherwise name the cover in the way.
        var open = reachable.FirstOrDefault(Applies);
        if (open is not null)
            return $"{here}; you'd have to get at it from {Words.The(_boat.GetPlace(open.PlaceId))}.";
        if (reachable.FirstOrDefault() is { CoverId: not null } closed)
            return $"{here}; {InTheWay(closed.CoverId)}.";
        return $"{here}.";
    }

    private string InTheWay(string coverId)
    {
        var cover = _boat.GetCover(coverId);
        return $"the {cover.Name.ToLowerInvariant()} {(cover.Plural ? "are" : "is")} in the way";
    }
}

/// <summary>Small phrases for places and covers, shared by the facade.</summary>
internal static class Words
{
    private static bool Bare(Place p) =>
        p.Name.StartsWith("On ", StringComparison.Ordinal) || p.Name.StartsWith("Beside ", StringComparison.Ordinal);

    /// <summary>"the quarter berth", "on deck".</summary>
    public static string The(Place p) => Bare(p) ? p.Name.ToLowerInvariant() : "the " + p.Name.ToLowerInvariant();

    /// <summary>"in the cockpit", "on deck".</summary>
    public static string In(Place p) => Bare(p) ? p.Name.ToLowerInvariant() : "in the " + p.Name.ToLowerInvariant();

    /// <summary>"to the forepeak", "on deck".</summary>
    public static string To(Place p) => Bare(p) ? p.Name.ToLowerInvariant() : "to the " + p.Name.ToLowerInvariant();

    public static string Is(Cover c) => c.Plural ? "are" : "is";

    public static string CoverName(Cover c) => c.Name.ToLowerInvariant();

    /// <summary>"off" / "in place", or the cover's own words ("open" / "shut").</summary>
    public static string State(Cover c, bool off) =>
        c.StateWords is { Count: 2 } w ? (off ? w[0] : w[1]) : off ? "off" : "in place";

    public static string JoinOr(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Join("", items) : string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1];
}
