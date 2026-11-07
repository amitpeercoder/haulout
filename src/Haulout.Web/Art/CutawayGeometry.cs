using System.Collections.Immutable;

namespace Haulout.Web.Art;

/// <summary>A fallback point for a found-component marker; its label is drawn to the right of it.</summary>
public sealed record MarkerSlot(double X, double Y);

/// <summary>One clickable compartment region: SVG path, caption anchor, and marker slots (in fill order).</summary>
public sealed record CompartmentShape(
    string Id,
    /// <summary>Short drawing caption; the facade's compartment name is used for the accessible label.</summary>
    string Caption,
    string PathData,
    double LabelX,
    double LabelY,
    ImmutableArray<MarkerSlot> Slots,
    /// <summary>Equipment drawn only once the compartment is open (pointer-events none).</summary>
    ImmutableArray<string> Furniture = default);

/// <summary>A decorative path and its CSS class. Never receives pointer events.</summary>
public sealed record DecorPath(string Class, string D);

/// <summary>
/// Where one component's marker sits and how its label is drawn. The label is abbreviated; the full name stays in the
/// panel and the aria-label. A leader line is drawn from the dot to (LeaderX, LeaderY) when those are set.
/// </summary>
public sealed record MarkerSpot(
    double X, double Y, string Label, double LabelX, double LabelY, string Anchor = "start",
    double? LeaderX = null, double? LeaderY = null)
{
    /// <summary>Fallback for ids not in the map: label to the right of the slot, full name.</summary>
    public static MarkerSpot FromSlot(MarkerSlot slot, string name) => new(slot.X, slot.Y, name, slot.X + 11, slot.Y + 4);
}

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

    // ---- yard: she is on the hard -----------------------------------------------------------------------------------
    public const double GroundY = 562;
    /// <summary>Yard concrete below the ground line (x, y, width, height).</summary>
    public static readonly (double X, double Y, double W, double H) GroundTone = (8, 562, 984, 30);

    /// <summary>Design waterline, a thin dashed reference only.</summary>
    public const double WaterlineY = 395;
    public const double WaterlineX1 = 36;
    public const double WaterlineX2 = 964;
    public const string WaterlineLabel = "DWL";

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

    private static string Pad(double x1, double y1, double x2, double y2, double t) =>
        FormattableString.Invariant($"M{x1},{y1} L{x2},{y2} L{x2},{y2 + t} L{x1},{y1 + t} Z");

    private static string Ladder()
    {
        var sb = new System.Text.StringBuilder("M916,562 L892,430 M934,562 L910,430");
        for (var y = 544.0; y > 440; y -= 18)
        {
            var k = (562 - y) / 132.0;
            sb.Append(FormattableString.Invariant($" M{916 - 24 * k:0.#},{y} L{934 - 24 * k:0.#},{y}"));
        }
        return sb.ToString();
    }

    /// <summary>Drawn behind the hull: ground line, joints, keel block, cradle stands, yard ladder.</summary>
    public static readonly ImmutableArray<DecorPath> Backdrop = ImmutableArray.Create(
        new DecorPath("detail", "M150,562 L136,592 M430,562 L420,592 M760,562 L772,592"),
        new DecorPath("ground", "M16,562 L984,562"),
        // timber block under the keel, with end grain and wedges
        new DecorPath("yard", "M524,548 L716,548 L716,562 L524,562 Z M524,555 L716,555 M560,548 L560,562 M680,548 L680,562 M524,562 L514,562 M716,562 L728,562"),
        // aft stand
        new DecorPath("yard", Pad(178, 466, 224, 474, 8) + " M188,474 L172,562 M214,479 L232,562 M181,520 L222,520 M181,520 L222,545"),
        // mid stand
        new DecorPath("yard", Pad(338, 476, 386, 476, 8) + " M346,484 L330,562 M378,484 L394,562 M338,524 L384,524 M338,524 L384,548"),
        // bow stand, pad following the hull
        new DecorPath("yard", Pad(820, 466, 868, 444, 8) + " M830,469 L806,562 M860,456 L888,562 M817,520 L877,520 M817,520 L877,545"),
        new DecorPath("yard", Ladder()));

    /// <summary>Drawn over the hull and regions, never clickable: steps, sprayhood, boom, mast.</summary>
    public static readonly ImmutableArray<DecorPath> Overlay = ImmutableArray.Create(
        // Companionway steps: two stringers and four treads, crossing the top edge of the engine-front region.
        new DecorPath("detail", "M796,226 L840,282 M812,226 L856,282"
            + " M805.4,238 L821.4,238 M814.9,250 L830.9,250 M824.3,262 L840.3,262 M833.7,274 L849.7,274"),
        // Sprayhood outline over the companionway.
        new DecorPath("rig-line", "M762,227 L774,198 L836,198 L854,225 M793,198 L797,226 M815,198 L819,226"),
        // Boom, outline only.
        new DecorPath("rig-line", "M118,166 L637,166 M118,174 L637,174 M118,166 L118,174 M628,170 L637,170"));

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
            Column(140, 340, 15, 7),
            ImmutableArray.Create("M150,396 L236,396 L236,440 L150,440 Z M214,396 L214,388 L222,388 L222,396")),

        // Low, under the quarter berth, aft of the engine. 6 + 2 spare.
        new CompartmentShape("battery-box", "Battery box",
            "M262,312 L400,312 L400,450 L262,450 Z",
            272, 327,
            Column(282, 344, 14, 8),
            ImmutableArray.Create(
                "M270,420 L384,420 L384,446 L270,446 Z M278,414 L290,414 L290,420 M308,414 L320,414 L320,420",
                "M314,416 L314,392 L400,392",
                "M278,416 L272,416 L272,340")),

        // Aft part of the engine space, behind the quarter-berth panel. 12 + 2 spare, two columns of seven.
        new CompartmentShape("engine-side", "Engine, side",
            "M406,262 L646,262 L646,450 L406,450 Z",
            416, 278,
            Column(424, 304, 20, 7).AddRange(Column(532, 304, 20, 7)),
            ImmutableArray.Create(
                "M426,334 L604,334 L604,440 L426,440 Z M452,292 L592,292 L592,334",
                "M410,396 L426,396 M410,424 L426,424 M410,396 L410,424")),

        // Forward end of the engine space, under the companionway steps. 9 + 2 spare, two columns of six.
        new CompartmentShape("engine-front", "Engine, front",
            "M652,262 L890,262 L890,394 L652,394 Z",
            662, 278,
            Column(668, 300, 16, 6).AddRange(Column(780, 300, 16, 6)),
            ImmutableArray.Create(
                "M662,308 L784,308 L784,392 L662,392 Z",
                "M844,332 a16,16 0 1,0 32,0 a16,16 0 1,0 -32,0 M850,376 a10,10 0 1,0 20,0 a10,10 0 1,0 -20,0 M844,332 L850,376 M876,332 L870,376")),

        // Low, under the engine front. 2 + 2 spare.
        new CompartmentShape("bilge", "Bilge",
            "M652,400 L886,400 L880,428 L862,450 L652,450 Z",
            662, 414,
            Slots((668, 429), (780, 429), (668, 442), (780, 442)),
            ImmutableArray.Create("M780,450 L780,418")),

        // Hull skin below the waterline (inner chamfer to outer curve), keel and rudder, one region. 4 + 2 spare (+2).
        // Slots 0-3 on the skin, 4-5 on the keel, 6-7 on the rudder.
        new CompartmentShape("hull-outside", "Hull, outside",
            "M84,378 Q112,432 170,450 L862,450 L880,428 L898,416 Q880,460 780,476 L240,476 Q104,468 74,370 Z"
            + " M520,476 L720,476 L700,548 L545,548 Z"
            + " M98,416 L136,449 L128,530 L100,530 Z",
            548, 500,
            Slots((300, 463), (420, 463), (540, 463), (660, 463), (600, 515), (600, 535), (111, 490), (111, 510))));

    // ---- marker map: component id -> position and short label --------------------------------------------------------
    private static KeyValuePair<string, MarkerSpot> R(string id, double x, double y, string label) =>
        new(id, new MarkerSpot(x, y, label, x + 11, y + 4));

    private static KeyValuePair<string, MarkerSpot> L(string id, double x, double y, string label) =>
        new(id, new MarkerSpot(x, y, label, x - 11, y + 4, "end"));

    /// <summary>Label placed freely; a leader line runs from the dot to (lx, ly) when leader is true.</summary>
    private static KeyValuePair<string, MarkerSpot> At(string id, double x, double y, string label, double lx, double ly,
        string anchor = "start", double? leadX = null, double? leadY = null) =>
        new(id, new MarkerSpot(x, y, label, lx, ly, anchor, leadX, leadY));

    public static readonly ImmutableDictionary<string, MarkerSpot> Spots = ImmutableDictionary.CreateRange(new[]
    {
        // battery box
        R("start-battery", 290, 433, "Start battery"),
        At("negative-terminal", 284, 408, "Neg. terminal", 286, 393),
        R("positive-terminal", 314, 408, "Pos. terminal"),
        At("main-positive-cable", 384, 392, "Main + cable", 398, 376, "end", 384, 380),
        R("negative-cable", 272, 356, "Neg. cable"),
        R("battery-switch", 272, 340, "Battery switch"),

        // engine side, aft end on the left: starter low, heat exchanger and elbow high; fuel gear along the right
        R("exhaust-elbow", 432, 300, "Exhaust elbow"),
        R("heat-exchanger", 432, 320, "Heat exchanger"),
        R("engine-ground-stud", 432, 366, "Ground stud"),
        R("starter-solenoid", 432, 388, "Solenoid"),
        R("starter-motor", 432, 412, "Starter motor"),
        R("glow-plugs", 540, 300, "Glow plugs"),
        R("injectors", 540, 322, "Injectors"),
        R("injection-pump", 540, 344, "Injection pump"),
        R("lift-pump", 540, 366, "Lift pump"),
        R("secondary-filter", 540, 388, "2nd fuel filter"),
        R("fuel-supply-line", 540, 410, "Fuel supply"),
        R("fuel-return-line", 540, 432, "Fuel return"),

        // engine front: block and lubrication on the left, front-face drive on the right
        R("coolant", 670, 298, "Coolant cap"),
        R("thermostat", 670, 318, "Thermostat"),
        R("engine-block", 670, 338, "Engine block"),
        R("engine-oil", 670, 358, "Engine oil"),
        R("oil-filter", 670, 378, "Oil filter"),
        R("alternator", 796, 300, "Alternator"),
        R("alternator-belt", 796, 322, "Alt. belt"),
        R("raw-water-pump", 796, 344, "Raw-water pump"),
        R("impeller", 796, 366, "Impeller"),

        // cockpit locker
        R("fuel-tank", 170, 426, "Fuel tank"),
        L("fuel-shutoff", 222, 402, "Shutoff"),
        L("primary-filter", 240, 370, "Primary filter"),
        L("exhaust-hose", 240, 352, "Exhaust hose"),
        L("anti-siphon-loop", 240, 334, "Anti-siphon"),

        // bilge
        R("raw-water-seacock", 780, 438, "Seacock"),
        R("raw-water-strainer", 780, 418, "Strainer"),

        // hull outside
        R("hull", 300, 463, "Hull"),
        R("antifouling", 440, 463, "Antifouling"),
        R("keel", 612, 522, "Keel"),
        R("rudder", 114, 495, "Rudder"),

        // deck and cockpit
        R("standing-rig", 641, 247, "Standing rig"),
        L("ignition-panel", 246, 276, "Ignition panel"),
    });

    /// <summary>Lookup by compartment id; null if the drawing has no such region.</summary>
    public static CompartmentShape? Find(string id)
    {
        foreach (var c in Compartments) if (c.Id == id) return c;
        return null;
    }
}
