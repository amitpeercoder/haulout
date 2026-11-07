using System.Collections.Immutable;

namespace Haulout.Web.Art;

/// <summary>A point where a found-component marker goes; its label is drawn to the right of it.</summary>
public sealed record MarkerSlot(double X, double Y);

/// <summary>One clickable compartment region: SVG path, caption anchor, and marker slots (in fill order).</summary>
public sealed record CompartmentShape(
    string Id,
    /// <summary>Short drawing caption; the facade's compartment name is used for the accessible label.</summary>
    string Caption,
    string PathData,
    double LabelX,
    double LabelY,
    ImmutableArray<MarkerSlot> Slots);

/// <summary>
/// All SVG geometry for the side-section cutaway: a 28 ft sloop, bow to the right, stern and cockpit to the left.
/// Pure data in viewBox units (1000 x 600). The art pass edits only this file.
/// </summary>
public static class CutawayGeometry
{
    public const string ViewBox = "0 0 1000 600";
    public const double Width = 1000;
    public const double Height = 600;

    /// <summary>Drawing-sheet border.</summary>
    public const string SheetFrame = "M8,8 L992,8 L992,592 L8,592 Z";

    public const string Title = "HAULOUT  /  28 FT SLOOP  /  SECTION, LOOKING TO PORT";
    public const double TitleX = 24;
    public const double TitleY = 584;

    // ---- water -----------------------------------------------------------------------------------------------------
    public const double WaterlineY = 395;
    public const double WaterlineX1 = 24;
    public const double WaterlineX2 = 976;
    /// <summary>Faint water tone below the waterline (x, y, width, height).</summary>
    public static readonly (double X, double Y, double W, double H) WaterTone = (16, 395, 968, 185);

    // ---- hull and rig ------------------------------------------------------------------------------------------------
    /// <summary>Outer hull outline, closed; filled with paper so the water tone stays outside the boat.</summary>
    public const string HullOutline =
        "M60,240 L74,370 Q104,468 240,476 L780,476 Q880,460 915,390 Q955,300 940,224 Z";

    /// <summary>Cabin-top outline (also part of the deck region).</summary>
    public const string CabinTop = "M276,236 L296,196 L520,196 L548,231";

    /// <summary>Sheer (deck) line, stern to bow.</summary>
    public const string SheerLine = "M60,240 L76,240 M256,240 L262,236 L276,236 M548,231 L940,224";

    /// <summary>Mast stub above the deck, a little forward of mid-length, and its foot.</summary>
    public const string Mast = "M637,229 L637,92 L645,92 L645,229 M631,229 L631,262 M651,229 L651,262 M637,92 L645,92";

    /// <summary>Thin decorative detail lines (portlights, cockpit seat, berth).</summary>
    public static readonly ImmutableArray<string> Details = ImmutableArray.Create(
        "M330,206 L390,206 L390,224 L330,224 Z",
        "M420,206 L480,206 L480,224 L420,224 Z",
        "M80,296 L256,296",
        "M262,268 L400,268 L400,306 L262,306 Z");

    /// <summary>Quiet caption for the unclickable quarter berth above the battery box.</summary>
    public const string QuarterBerthCaption = "quarter berth";
    public const double QuarterBerthX = 272;
    public const double QuarterBerthY = 290;

    // ---- compartments ------------------------------------------------------------------------------------------------
    private static ImmutableArray<MarkerSlot> Column(double x, double y0, double step, int count)
    {
        var b = ImmutableArray.CreateBuilder<MarkerSlot>(count);
        for (var i = 0; i < count; i++) b.Add(new MarkerSlot(x, y0 + step * i));
        return b.ToImmutable();
    }

    private static ImmutableArray<MarkerSlot> Slots(params (double X, double Y)[] points) =>
        points.Select(p => new MarkerSlot(p.X, p.Y)).ToImmutableArray();

    /// <summary>Drawing order. Regions do not overlap: clicks are unambiguous.</summary>
    public static readonly ImmutableArray<CompartmentShape> Compartments = ImmutableArray.Create(
        // Deck strip plus cabin top (cabin top is the second sub-shape). 1 component + 2 spare.
        new CompartmentShape("deck", "Deck",
            "M262,262 L262,236 L276,236 L296,196 L520,196 L548,231 L940,224 L928,262 Z",
            270, 254,
            Slots((668, 246), (780, 243), (860, 241))),

        // Open well aft, above the locker. 1 + 2 spare.
        new CompartmentShape("cockpit", "Cockpit",
            "M76,240 L256,240 L256,306 L80,306 Z",
            88, 258,
            Slots((104, 272), (104, 285), (104, 298))),

        // Deep locker under the seat. 5 + 2 spare.
        new CompartmentShape("cockpit-locker", "Cockpit locker",
            "M80,306 L256,306 L256,450 L170,450 Q112,432 84,378 Z",
            92, 322,
            Column(140, 340, 15, 7)),

        // Low, under the quarter berth, aft of the engine. 6 + 2 spare.
        new CompartmentShape("battery-box", "Battery box",
            "M262,312 L400,312 L400,450 L262,450 Z",
            272, 327,
            Column(282, 344, 14, 8)),

        // Aft part of the engine space, behind the quarter-berth panel. 12 + 2 spare, two columns of seven.
        new CompartmentShape("engine-side", "Engine, side",
            "M406,262 L646,262 L646,450 L406,450 Z",
            416, 278,
            Column(424, 304, 20, 7).AddRange(Column(532, 304, 20, 7))),

        // Forward end of the engine space, under the companionway steps. 9 + 2 spare, two columns of six.
        new CompartmentShape("engine-front", "Engine, front",
            "M652,262 L890,262 L890,394 L652,394 Z",
            662, 278,
            Column(668, 300, 16, 6).AddRange(Column(780, 300, 16, 6))),

        // Low, under the engine front. 2 + 2 spare.
        new CompartmentShape("bilge", "Bilge",
            "M652,400 L886,400 L880,428 L862,450 L652,450 Z",
            662, 414,
            Slots((668, 429), (780, 429), (668, 442), (780, 442))),

        // Hull skin below the waterline (inner chamfer to outer curve), keel and rudder, one region. 4 + 2 spare (+2).
        // Slots 0-3 on the skin, 4-5 on the keel, 6-7 on the rudder.
        new CompartmentShape("hull-outside", "Hull, outside",
            "M84,378 Q112,432 170,450 L862,450 L880,428 L898,416 Q880,460 780,476 L240,476 Q104,468 74,370 Z"
            + " M520,476 L720,476 L692,570 L540,570 Z"
            + " M98,416 L136,449 L128,545 L100,545 Z",
            548, 500,
            Slots((300, 463), (420, 463), (540, 463), (660, 463), (560, 520), (560, 540), (111, 500), (111, 520))));

    /// <summary>Lookup by compartment id; null if the drawing has no such region.</summary>
    public static CompartmentShape? Find(string id)
    {
        foreach (var c in Compartments) if (c.Id == id) return c;
        return null;
    }
}
