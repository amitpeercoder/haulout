using System.Collections.Immutable;

namespace Haulout.Web.Art;

// Shared drawing contract for the map (CutawayGeometry) and the close-ups (Art/CloseUps). Pure data in viewBox units.
// Names (places, covers, components, position labels) are the facade's display names, matched case-insensitively.

/// <summary>A decorative path and its CSS class ("hull-line", "detail", "furniture", "yard", "rig-line", ...). Never receives pointer events.</summary>
public sealed record DecorPath(string Class, string D);

/// <summary>
/// Where one component's marker sits and how its label is drawn. The label is abbreviated; the full name stays in the
/// panel and the aria-label. A leader line is drawn from the dot to (LeaderX, LeaderY) when those are set.
/// </summary>
public sealed record MarkerSpot(
    double X, double Y, string Label, double LabelX, double LabelY, string Anchor = "start",
    double? LeaderX = null, double? LeaderY = null)
{
    /// <summary>Label to the right of the dot.</summary>
    public static MarkerSpot Right(double x, double y, string label) => new(x, y, label, x + 11, y + 4);

    /// <summary>Label to the left of the dot.</summary>
    public static MarkerSpot Left(double x, double y, string label) => new(x, y, label, x - 11, y + 4, "end");
}

/// <summary>
/// A place on the map: a clickable region (closed path), a caption, and where the player figure stands when there.
/// Place is the facade's place name ("Quarter berth").
/// </summary>
public sealed record PlaceShape(string Place, string D, string Caption, double LabelX, double LabelY, double FigureX, double FigureY);

/// <summary>
/// A cover drawn in state. Cover is the facade's cover name ("Companionway steps"). InPlace is drawn while it is on,
/// Removed while it is off (the removed drawing may include what the cover revealed). HitD is the clickable area in a
/// close-up (closed path; unused on the map).
/// </summary>
public sealed record CoverShape(string Cover, string HitD, ImmutableArray<DecorPath> InPlace, ImmutableArray<DecorPath> Removed,
    string Caption = "", double LabelX = 0, double LabelY = 0);

/// <summary>
/// What you see from one place, larger. Same viewBox as the map (1000 x 600) so the two layers crossfade in one frame.
/// Backdrop is drawn first, then covers in state, then Overlay, then markers for known components visible from here
/// (positions from Spots, keyed by component name; a component without a spot falls back to Fallback slots in order).
/// </summary>
public sealed record CloseUpScene(
    string Place,
    string Title,
    ImmutableArray<DecorPath> Backdrop,
    ImmutableArray<CoverShape> Covers,
    ImmutableArray<DecorPath> Overlay,
    ImmutableDictionary<string, MarkerSpot> Spots,
    ImmutableArray<(double X, double Y)> Fallback)
{
    public const string ViewBox = "0 0 1000 600";
    public const double Width = 1000, Height = 600;

    public MarkerSpot SpotFor(string componentName, int fallbackIndex)
    {
        foreach (var kv in Spots)
            if (string.Equals(kv.Key, componentName, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        var (x, y) = Fallback.IsDefaultOrEmpty ? (40.0, 40.0 + 18 * fallbackIndex) : Fallback[Math.Min(fallbackIndex, Fallback.Length - 1)];
        return MarkerSpot.Right(x, y, componentName);
    }
}
