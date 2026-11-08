using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Haulout.Web.Art;

/// <summary>
/// Static art for the paper chart (design note 009): a fictional archipelago drawn by hand-placed control points in
/// nautical miles, turned into smooth closed paths (Catmull-Rom to cubic Beziers). Pure data in viewBox units
/// (1000 x 600, 20 units per nm). Nothing here is traced from a real chart; positions of the ports and the routes
/// between them come from the sim, the land is drawn so those routes cross open water only.
/// </summary>
public static class ChartGeometry
{
    public const string ViewBox = "0 0 1000 600";
    public const double Width = 1000;
    public const double Height = 600;
    public const double UnitsPerNm = 20;

    /// <summary>Chart frame: east -28..22 nm, north -8..22 nm of the home port.</summary>
    public static double X(double eastNm) => (eastNm + 28) * UnitsPerNm;
    public static double Y(double northNm) => (22 - northNm) * UnitsPerNm;

    // ---- records ----------------------------------------------------------------------------------------------------
    /// <summary>A land or shoal shape. Class: "land", "mainland", "skerry", "shoal". D is a closed path.</summary>
    public sealed record Land(string Class, string D);
    /// <summary>A depth figure in metres, drawn centred on (X, Y).</summary>
    public sealed record Sounding(double X, double Y, string Depth);
    /// <summary>Label offset from the port mark (viewBox units) and its text-anchor.</summary>
    public sealed record LabelSpot(double Dx, double Dy, string Anchor);
    /// <summary>A decorative place name (viewBox units, text-anchor middle). OnLand: it sits on the island (draw it light); else on water (draw it dark).</summary>
    public sealed record PlaceName(double X, double Y, string Text, bool OnLand);
    /// <summary>A text at a point.</summary>
    public sealed record TextAt(double X, double Y, string Text);

    // ---- number formatting --------------------------------------------------------------------------------------------
    private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string P(double e, double n) => N(X(e)) + "," + N(Y(n));

    // ---- spline helpers -----------------------------------------------------------------------------------------------
    private readonly record struct Pt(double E, double N);

    /// <summary>Cubic segments (in nm) of a Catmull-Rom spline through the points, closed or open (ends doubled).</summary>
    private static List<(Pt A, Pt C1, Pt C2, Pt B)> Segments(IReadOnlyList<Pt> p, bool closed)
    {
        var segs = new List<(Pt, Pt, Pt, Pt)>();
        int n = p.Count;
        int count = closed ? n : n - 1;
        Pt At(int i) => closed ? p[((i % n) + n) % n] : p[Math.Clamp(i, 0, n - 1)];
        for (int i = 0; i < count; i++)
        {
            Pt p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
            var c1 = new Pt(p1.E + (p2.E - p0.E) / 6, p1.N + (p2.N - p0.N) / 6);
            var c2 = new Pt(p2.E - (p3.E - p1.E) / 6, p2.N - (p3.N - p1.N) / 6);
            segs.Add((p1, c1, c2, p2));
        }
        return segs;
    }

    private static string PathOf(IReadOnlyList<(Pt A, Pt C1, Pt C2, Pt B)> segs, IEnumerable<Pt>? cornersAfter = null)
    {
        var sb = new StringBuilder();
        sb.Append('M').Append(P(segs[0].A.E, segs[0].A.N));
        foreach (var s in segs)
            sb.Append(" C").Append(P(s.C1.E, s.C1.N)).Append(' ').Append(P(s.C2.E, s.C2.N)).Append(' ').Append(P(s.B.E, s.B.N));
        if (cornersAfter is not null)
            foreach (var c in cornersAfter) sb.Append(" L").Append(P(c.E, c.N));
        sb.Append(" Z");
        return sb.ToString();
    }

    /// <summary>One piece of land: the class, its control points, a flattened outline in nm for the clearance check, and its path.</summary>
    private sealed record Piece(string Name, string Class, List<Pt> Pts, List<Pt> Outline, Land Art);

    private static List<Pt> Flatten(IReadOnlyList<(Pt A, Pt C1, Pt C2, Pt B)> segs, IEnumerable<Pt>? corners = null)
    {
        var o = new List<Pt>();
        foreach (var s in segs)
            for (int k = 0; k < 10; k++)
            {
                double t = k / 10.0, u = 1 - t;
                o.Add(new Pt(
                    u * u * u * s.A.E + 3 * u * u * t * s.C1.E + 3 * u * t * t * s.C2.E + t * t * t * s.B.E,
                    u * u * u * s.A.N + 3 * u * u * t * s.C1.N + 3 * u * t * t * s.C2.N + t * t * t * s.B.N));
            }
        if (corners is not null) o.AddRange(corners);
        return o;
    }

    private static Piece Closed(string name, string cls, params double[] en)
    {
        var pts = new List<Pt>();
        for (int i = 0; i < en.Length; i += 2) pts.Add(new Pt(en[i], en[i + 1]));
        var segs = Segments(pts, closed: true);
        return new Piece(name, cls, pts, Flatten(segs), new Land(cls, PathOf(segs)));
    }

    /// <summary>
    /// A rock or islet around (cx, cy): radius r nm times a hand-picked factor at each of the evenly spaced angles,
    /// stretched along the rotated axis by aspect (1 = round, 2 = twice as long as wide).
    /// </summary>
    private static Piece Blob(string name, string cls, double cx, double cy, double r, double rotDeg, double aspect, params double[] factors)
    {
        var en = new double[factors.Length * 2];
        double rot = rotDeg * Math.PI / 180;
        for (int i = 0; i < factors.Length; i++)
        {
            double a = 2 * Math.PI * i / factors.Length;
            double lx = r * aspect * factors[i] * Math.Cos(a), ly = r * factors[i] * Math.Sin(a);
            en[2 * i] = cx + lx * Math.Cos(rot) - ly * Math.Sin(rot);
            en[2 * i + 1] = cy + lx * Math.Sin(rot) + ly * Math.Cos(rot);
        }
        return Closed(name, cls, en);
    }

    /// <summary>The 5 m line: the outline pushed outward from its centroid by an uneven amount per point.</summary>
    private static Piece Shoal(string name, Piece island, double pushNm)
    {
        var pts = island.Pts;
        double cx = pts.Average(p => p.E), cy = pts.Average(p => p.N);
        var pushed = new List<Pt>();
        for (int i = 0; i < pts.Count; i++)
        {
            double dx = pts[i].E - cx, dy = pts[i].N - cy, d = Math.Sqrt(dx * dx + dy * dy);
            double k = pushNm * (0.6 + 0.8 * ((i * 7) % 5) / 4.0);
            pushed.Add(new Pt(pts[i].E + k * dx / d, pts[i].N + k * dy / d));
        }
        var segs = Segments(pushed, closed: true);
        return new Piece(name, "shoal", pushed, Flatten(segs), new Land("shoal", PathOf(segs)));
    }

    // ---- the land ---------------------------------------------------------------------------------------------------
    // Control points are hand-placed in nm (east, north). The mainland is an open spline along the south edge, rising
    // on the west side, with a bay at Skjaervik and three inlets; it is closed off outside the frame by straight corners.
    private static readonly Pt[] MainlandCoast =
    {
        new(-29, 8.0), new(-27.5, 6.5), new(-26.9, 4.3), new(-25.3, 3.0), new(-24.0, 1.0), new(-22.6, 0.4),
        new(-21.4, -1.2), new(-21.8, -3.4), new(-20.6, -4.6), new(-19.2, -3.2), new(-17.6, -1.8), new(-16.4, -3.6),
        new(-14.8, -3.9), new(-14.2, -5.6), new(-13.7, -7.0), new(-12.7, -7.5), new(-12.1, -5.4), new(-11.2, -3.2), new(-9.6, -2.6),
        new(-8.0, -1.0), new(-6.8, 0.6), new(-5.6, 2.0), new(-4.4, 1.6), new(-3.0, 0.6), new(-1.6, -0.3),
        new(0.2, -0.55), new(1.5, -0.2), new(2.4, 0.9), new(3.1, 2.3), new(3.9, 2.6), new(4.6, 1.2), new(5.0, -1.0),
        new(6.3, -3.2), new(7.4, -1.8), new(9.0, -2.2), new(10.0, -4.8), new(10.4, -6.9), new(11.4, -7.3), new(11.9, -4.5),
        new(12.6, -2.4), new(14.2, -2.0), new(15.4, -4.0), new(17.0, -4.2), new(18.4, -2.6), new(19.6, -3.4),
        new(20.6, -1.2), new(20.9, 1.2), new(22.0, 2.6),
    };
    private static readonly Pt[] MainlandCorners = { new(25, 2.8), new(25, -12), new(-32, -12), new(-32, 8.0) };

    private static Piece MainlandPiece()
    {
        var segs = Segments(MainlandCoast, closed: false);
        return new Piece("Mainland", "mainland", MainlandCoast.ToList(), Flatten(segs, MainlandCorners), new Land("mainland", PathOf(segs, MainlandCorners)));
    }

    private static readonly Piece Mainland = MainlandPiece();

    // Hestholmen (Hestoya): long, north-east to south-west, the port off its south-west tip (shore point 9.15, 11.15).
    private static readonly Piece HestIsland = Closed("Hestholmen", "land",
        9.15, 11.15, 10.3, 11.0, 11.8, 11.8, 13.2, 12.9, 14.5, 14.4, 14.9, 15.9, 14.2, 17.0, 13.0, 16.9, 12.3, 16.0,
        11.5, 15.2, 10.4, 14.6, 10.2, 13.6, 9.3, 13.1, 8.7, 12.2);
    // Storholmen: a hooked point curls round the harbour from the south; the port (shore point -22.9, 13.4) lies in its lee.
    private static readonly Piece StorIsland = Closed("Storholmen", "land",
        -22.9, 13.4, -23.0, 14.9, -24.2, 16.3, -26.0, 16.5, -27.4, 15.3, -27.5, 13.6, -26.6, 12.7, -25.8, 12.1,
        -24.6, 11.5, -23.4, 11.1, -22.4, 11.4, -21.9, 12.1, -22.2, 12.45, -22.8, 12.1, -23.5, 12.2, -23.9, 12.75, -23.5, 13.1);
    // Kvitnes (Ulvoya): a crescent with its horns to the west and the anchorage in the notch of the south horn.
    private static readonly Piece KvitIsland = Closed("Kvitnes", "land",
        17.15, 6.3, 18.6, 7.2, 19.4, 8.6, 18.8, 9.9, 17.3, 10.6, 16.9, 11.2, 18.2, 12.0, 20.2, 12.3, 22.2, 11.3, 23.1, 9.3,
        23.2, 7.2, 21.9, 5.4, 20.0, 4.6, 18.4, 4.6, 17.4, 5.1);

    private static readonly Piece Lyngskjaer = Blob("Lyngskjær", "skerry", 5.15, 4.7, 0.28, 20, 1.4, 1.0, 0.7, 1.1, 0.8, 1.0, 0.75);
    private static readonly Piece Maakeskjaer = Blob("Måkeskjær", "skerry", -10.1, 6.55, 0.3, 70, 1.5, 1.1, 0.75, 1.0, 0.7, 1.05, 0.85);

    private static readonly Piece[] Islets =
    {
        // rocks beside the named skerries, on the side away from the route
        Blob("Lyngskjær rock 1", "skerry", 5.75, 4.3, 0.13, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Lyngskjær rock 2", "skerry", 4.85, 4.15, 0.1, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Lyngskjær rock 3", "skerry", 5.55, 5.15, 0.07, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Måkeskjær rock 1", "skerry", -9.5, 7.05, 0.14, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Måkeskjær rock 2", "skerry", -10.7, 7.1, 0.1, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Måkeskjær rock 3", "skerry", -9.2, 6.5, 0.07, 0, 1, 1.0, 0.8, 1.1, 0.85),
        // a long islet and a pair
        Blob("Islet A", "skerry", -6.5, 12.6, 0.4, 25, 2.6, 1.0, 0.8, 1.15, 0.7, 1.0, 0.85),
        Blob("Islet A2", "skerry", -5.7, 13.1, 0.18, 0, 1, 1.0, 0.8, 1.1, 0.85),
        // Laksholmen: a lobed island with two rocks
        Closed("Laksholmen", "land", -17.6, 17.3, -17.0, 18.2, -16.0, 18.4, -15.3, 17.7, -15.7, 16.9, -16.5, 16.6, -16.9, 16.1, -17.5, 16.5),
        Blob("Laksholmen rock 1", "skerry", -14.6, 17.0, 0.12, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Laksholmen rock 2", "skerry", -15.0, 16.2, 0.08, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Islet C", "skerry", 3.0, 15.0, 0.3, 100, 1.8, 0.9, 1.1, 0.7, 1.0, 0.8),
        // a longer, ragged island to the north
        Closed("Islet D", "land", 2.6, 19.4, 3.4, 20.2, 4.6, 20.5, 5.7, 19.9, 5.4, 19.2, 4.4, 19.0, 3.7, 18.6, 3.0, 18.8),
        Blob("Islet E", "skerry", -14.0, 1.4, 0.25, 30, 1.5, 1.0, 0.7, 1.1, 0.8, 0.9),
        Blob("Islet E2", "skerry", -13.4, 1.0, 0.1, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Islet F", "skerry", 13.0, 2.0, 0.3, 60, 1.6, 1.1, 0.8, 0.95, 0.7, 1.0),
        Blob("Islet G", "skerry", 8.0, 5.0, 0.3, 15, 1.3, 1.0, 0.75, 1.1, 0.8),
        Closed("Islet H", "land", -20.2, 5.9, -19.2, 6.0, -18.2, 5.3, -18.6, 4.4, -19.6, 4.6, -20.5, 4.9),
        Blob("Islet H rock", "skerry", -17.5, 5.2, 0.1, 0, 1, 1.0, 0.8, 1.1, 0.85),
        Blob("Islet I", "skerry", -3.5, 19.0, 0.3, 5, 1.7, 1.0, 0.8, 1.1, 0.7),
    };

    private static readonly Piece[] Shoals =
    {
        Shoal("Hest shoal", HestIsland, 0.5),
        Shoal("Stor shoal", StorIsland, 0.5),
        Shoal("Kvit shoal", KvitIsland, 0.45),
        Shoal("Lyngskjær shoal", Lyngskjaer, 0.2),
        Shoal("Måkeskjær shoal", Maakeskjaer, 0.2),
    };

    private static IEnumerable<Piece> LandPieces =>
        new[] { Mainland, HestIsland, StorIsland, KvitIsland, Lyngskjaer, Maakeskjaer }.Concat(Islets);

    /// <summary>Shoal contours first (drawn under), then land in drawing order.</summary>
    public static readonly ImmutableArray<Land> Shallows = Shoals.Select(s => s.Art).ToImmutableArray();
    public static readonly ImmutableArray<Land> Lands = LandPieces.Select(s => s.Art).ToImmutableArray();

    // ---- ports, routes (nm) for the check and for anyone who wants the numbers ------------------------------------------
    public static readonly ImmutableDictionary<string, (double EastNm, double NorthNm)> PortsNm =
        new Dictionary<string, (double, double)>
        {
            ["Skjærvik"] = (0, 0), ["Hestholmen"] = (9.0, 10.7), ["Storholmen"] = (-22.5, 13.0), ["Kvitnes"] = (16.8, 6.2),
        }.ToImmutableDictionary();

    private static readonly (string From, string To)[] RouteEnds =
    {
        ("Skjærvik", "Hestholmen"), ("Skjærvik", "Storholmen"), ("Kvitnes", "Hestholmen"),
    };

    // ---- label anchors ----------------------------------------------------------------------------------------------
    /// <summary>Label offset per port name from the port mark, chosen to keep clear of marks, routes and shores.</summary>
    public static readonly ImmutableDictionary<string, LabelSpot> PortLabels = new Dictionary<string, LabelSpot>
    {
        ["Skjærvik"] = new(-14, -12, "end"),
        ["Hestholmen"] = new(-14, -12, "end"),
        ["Storholmen"] = new(14, -14, "start"),
        ["Kvitnes"] = new(-14, 20, "end"),
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Label offset for a skerry/shelter name, from the skerry centre (viewBox units).</summary>
    public static readonly ImmutableDictionary<string, LabelSpot> ShelterLabels = new Dictionary<string, LabelSpot>
    {
        ["Lyngskjær"] = new(8, 24, "start"),
        ["Måkeskjær"] = new(8, 24, "start"),
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where the skerry that shelters each named place sits (viewBox units), for labels.</summary>
    public static readonly ImmutableDictionary<string, (double X, double Y)> SkerryCentres = new Dictionary<string, (double, double)>
    {
        ["Lyngskjær"] = (X(5.15), Y(4.7)),
        ["Måkeskjær"] = (X(-10.1), Y(6.55)),
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Decorative island names, italic, centred.</summary>
    public static readonly ImmutableArray<PlaceName> IslandNames = ImmutableArray.Create(
        new PlaceName(X(12.4), Y(14.3), "Hestøya", true),
        new PlaceName(X(20.5), Y(8.4), "Ulvøya", true),
        new PlaceName(X(-16.4), Y(15.5), "Laksholmen", false));

    // ---- soundings (metres), shallower near land --------------------------------------------------------------------
    private static Sounding S(double e, double n, string d) => new(X(e), Y(n), d);

    public static readonly ImmutableArray<Sounding> Soundings = ImmutableArray.Create(
        S(4.0, 1.6, "6"), S(-1.2, 2.4, "9"), S(1.6, 4.6, "5"), S(8.4, 2.4, "23"), S(1.0, 7.5, "31"),
        S(-6.0, 3.0, "27"), S(-15.2, 10.4, "36"), S(-20.8, 10.8, "8"), S(8.0, 8.4, "14"), S(13.2, 7.0, "11"),
        S(14.4, 4.0, "19"), S(-9.0, 0.6, "16"), S(-18.0, 14.6, "22"), S(-3.0, 11.5, "38"), S(11.0, 17.6, "26"),
        S(4.4, 12.8, "33"), S(16.0, 9.4, "7"));

    // ---- compass rose -----------------------------------------------------------------------------------------------
    private const double RoseE = 18.7, RoseN = 17.2;
    public static readonly double RoseCx = X(RoseE), RoseCy = Y(RoseN);
    private const double RoseR = 44;

    private static string RoseStar(double cx, double cy, double rLong, double rWaist, double startDeg)
    {
        // eight-vertex star: points at 0/90/180/270 (+start) with waist vertices between.
        var sb = new StringBuilder();
        for (int i = 0; i < 8; i++)
        {
            double r = i % 2 == 0 ? rLong : rWaist;
            double a = (startDeg + 45 * i) * Math.PI / 180;
            sb.Append(i == 0 ? 'M' : 'L').Append(N(cx + r * Math.Sin(a))).Append(',').Append(N(cy - r * Math.Cos(a))).Append(' ');
        }
        return sb.Append('Z').ToString();
    }

    private static string RoseRing(double cx, double cy, double r) =>
        F($"M{N(cx + r)},{N(cy)} A{N(r)},{N(r)} 0 1 0 {N(cx - r)},{N(cy)} A{N(r)},{N(r)} 0 1 0 {N(cx + r)},{N(cy)} Z");

    private static string F(FormattableString s) => FormattableString.Invariant(s);
    private static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);

    /// <summary>Compass rose strokes. Classes: "rose-ring", "rose-star", "rose-star-minor", "rose-north" (the filled north half).</summary>
    public static readonly ImmutableArray<DecorPath> Rose = ImmutableArray.Create(
        new DecorPath("rose-ring", RoseRing(RoseCx, RoseCy, RoseR)),
        new DecorPath("rose-ring", RoseRing(RoseCx, RoseCy, RoseR - 6)),
        new DecorPath("rose-star-minor", RoseStar(RoseCx, RoseCy, RoseR - 8, 6, 45)),
        new DecorPath("rose-star", RoseStar(RoseCx, RoseCy, RoseR + 6, 7, 0)),
        new DecorPath("rose-north",
            F($"M{N(RoseCx)},{N(RoseCy - RoseR - 6)} L{N(RoseCx + 5)},{N(RoseCy - 5)} L{N(RoseCx - 5)},{N(RoseCy - 5)} Z")));

    /// <summary>The "N" label above the rose.</summary>
    public static readonly TextAt RoseNorthLabel = new(RoseCx, RoseCy - RoseR - 12, "N");

    // ---- scale bar (0-5 nm) -----------------------------------------------------------------------------------------
    public const double ScaleX0 = 320, ScaleY = 44, ScaleNm = 5;
    public static readonly ImmutableArray<double> ScaleTickX = Enumerable.Range(0, 6).Select(i => ScaleX0 + i * UnitsPerNm).ToImmutableArray();
    public const double ScaleTickHeight = 8;

    /// <summary>Bar line, ticks and alternating blocks.</summary>
    public static readonly ImmutableArray<DecorPath> ScaleBar = ImmutableArray.Create(
        new DecorPath("scale-line", F($"M{N(ScaleX0)},{N(ScaleY)} L{N(ScaleX0 + ScaleNm * UnitsPerNm)},{N(ScaleY)}")),
        new DecorPath("scale-block", F($"M{N(ScaleX0)},{N(ScaleY - 3)} L{N(ScaleX0 + UnitsPerNm)},{N(ScaleY - 3)} L{N(ScaleX0 + UnitsPerNm)},{N(ScaleY)} L{N(ScaleX0)},{N(ScaleY)} Z")),
        new DecorPath("scale-block", F($"M{N(ScaleX0 + 2 * UnitsPerNm)},{N(ScaleY - 3)} L{N(ScaleX0 + 3 * UnitsPerNm)},{N(ScaleY - 3)} L{N(ScaleX0 + 3 * UnitsPerNm)},{N(ScaleY)} L{N(ScaleX0 + 2 * UnitsPerNm)},{N(ScaleY)} Z")),
        new DecorPath("scale-block", F($"M{N(ScaleX0 + 4 * UnitsPerNm)},{N(ScaleY - 3)} L{N(ScaleX0 + 5 * UnitsPerNm)},{N(ScaleY - 3)} L{N(ScaleX0 + 5 * UnitsPerNm)},{N(ScaleY)} L{N(ScaleX0 + 4 * UnitsPerNm)},{N(ScaleY)} Z")),
        new DecorPath("scale-tick", string.Join(" ", ScaleTickX.Select(x => F($"M{N(x)},{N(ScaleY - ScaleTickHeight)} L{N(x)},{N(ScaleY + 3)}")))));

    /// <summary>Labels under the bar: 0, 1, ... 5, and the unit.</summary>
    public static readonly ImmutableArray<TextAt> ScaleLabels = ScaleTickX
        .Select((x, i) => new TextAt(x, ScaleY + 17, i.ToString(CultureInfo.InvariantCulture)))
        .Append(new TextAt(ScaleX0 + ScaleNm * UnitsPerNm + 10, ScaleY + 17, "nm"))
        .ToImmutableArray();

    // ---- self-check -------------------------------------------------------------------------------------------------
    private static bool Inside(List<Pt> poly, double e, double n)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].N > n) != (poly[j].N > n) &&
                e < (poly[j].E - poly[i].E) * (n - poly[i].N) / (poly[j].N - poly[i].N) + poly[i].E)
                c = !c;
        return c;
    }

    private static double DistToOutline(List<Pt> poly, double e, double n)
    {
        double best = double.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            double ax = poly[j].E, ay = poly[j].N, bx = poly[i].E, by = poly[i].N;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Clamp(((e - ax) * dx + (n - ay) * dy) / len2, 0, 1);
            best = Math.Min(best, Hyp(e - (ax + t * dx), n - (ay + t * dy)));
        }
        return best;
    }

    /// <summary>
    /// Samples every route every 0.05 nm and every port, tests them against all land outlines. Returns one report line
    /// per route and port: the least distance to any shore in nm, and "INSIDE LAND" if a sample falls on land.
    /// </summary>
    public static IReadOnlyList<string> CheckRoutesClearOfLand(double minClearNm = 0.25)
    {
        var report = new List<string>();
        foreach (var (from, to) in RouteEnds)
        {
            var a = PortsNm[from]; var b = PortsNm[to];
            double len = Hyp(b.EastNm - a.EastNm, b.NorthNm - a.NorthNm);
            double min = double.MaxValue; string nearest = "", inside = "";
            for (double d = 0; d <= len + 1e-9; d += 0.05)
            {
                double e = a.EastNm + (b.EastNm - a.EastNm) * d / len, n = a.NorthNm + (b.NorthNm - a.NorthNm) * d / len;
                foreach (var land in LandPieces)
                {
                    if (Inside(land.Outline, e, n)) inside = land.Name;
                    double dist = DistToOutline(land.Outline, e, n);
                    if (dist < min) { min = dist; nearest = land.Name; }
                }
            }
            report.Add($"{from} -> {to}: {(inside != "" ? "INSIDE LAND (" + inside + ")" : min < minClearNm ? "TOO CLOSE" : "clear")}, least {min:0.00} nm from {nearest}");
        }
        foreach (var (name, pos) in PortsNm)
        {
            double min = double.MaxValue; string nearest = ""; string inside = "";
            foreach (var land in LandPieces)
            {
                if (Inside(land.Outline, pos.EastNm, pos.NorthNm)) inside = land.Name;
                double dist = DistToOutline(land.Outline, pos.EastNm, pos.NorthNm);
                if (dist < min) { min = dist; nearest = land.Name; }
            }
            report.Add($"port {name}: {(inside != "" ? "INSIDE LAND (" + inside + ")" : "afloat")}, {min:0.00} nm from the shore of {nearest}");
        }
        foreach (var s in new[] { ("Lyngskjær", 7.0), ("Måkeskjær", 12.0) })
        {
            var (from, to) = s.Item1 == "Lyngskjær" ? ("Skjærvik", "Hestholmen") : ("Skjærvik", "Storholmen");
            var a = PortsNm[from]; var b = PortsNm[to];
            double len = Hyp(b.EastNm - a.EastNm, b.NorthNm - a.NorthNm);
            double e = a.EastNm + (b.EastNm - a.EastNm) * s.Item2 / len, n = a.NorthNm + (b.NorthNm - a.NorthNm) * s.Item2 / len;
            var piece = LandPieces.First(p => p.Name == s.Item1);
            report.Add($"shelter {s.Item1} at {e:0.0},{n:0.0}: {DistToOutline(piece.Outline, e, n):0.00} nm from its skerry");
        }
        return report;
    }
}
