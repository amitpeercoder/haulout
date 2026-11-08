using System.Collections.Immutable;
using System.Globalization;

namespace Haulout.Web.Art;

/// <summary>
/// All SVG geometry for the overview cutaway (the map): a side section of a 28 ft fin-keel sloop on the hard, bow to the
/// right, stern and cockpit to the left. Pure data in viewBox units (1000 x 600). Cutaway.razor only renders it.
/// Places, covers and component positions follow docs/design/004.
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
    public const double TitleY = 586;

    // ---- scale -------------------------------------------------------------------------------------------------------
    // LOA 8.5 m runs from the heel of the transom (x 46) to the stem head (x 937): 891 units, so 105 units per metre.
    // Draft 1.5 m below the DWL puts the keel tip at y 539.5; the rudder blade goes 1.2 m below the DWL (y 508).
    public const double UnitsPerMetre = 105;

    // ---- yard: she is on the hard -----------------------------------------------------------------------------------
    /// <summary>Yard ground, just under the keel block that carries the keel tip.</summary>
    public const double GroundY = 554;
    /// <summary>Yard concrete below the ground line (x, y, width, height).</summary>
    public static readonly (double X, double Y, double W, double H) GroundTone = (8, GroundY, 984, 592 - GroundY);

    /// <summary>Design waterline, a thin dashed reference only: where the canoe body would meet the water.</summary>
    public const double WaterlineY = 382;
    public const double WaterlineX1 = 36;
    public const double WaterlineX2 = 964;
    public const string WaterlineLabel = "DWL";

    // ---- number formatting --------------------------------------------------------------------------------------------
    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string F(FormattableString s) => FormattableString.Invariant(s);

    // ---- curves ------------------------------------------------------------------------------------------------------
    private readonly record struct Cubic(double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3)
    {
        public (double X, double Y) At(double t)
        {
            var u = 1 - t;
            return (u * u * u * X0 + 3 * u * u * t * X1 + 3 * u * t * t * X2 + t * t * t * X3,
                    u * u * u * Y0 + 3 * u * u * t * Y1 + 3 * u * t * t * Y2 + t * t * t * Y3);
        }

        /// <summary>The part of the curve between parameters t0 and t1 (de Casteljau).</summary>
        public Cubic Sub(double t0, double t1)
        {
            var a = Split(this, t1).Left;
            return t0 <= 0 ? a : Split(a, t0 / t1).Right;
        }

        public Cubic Reverse() => new(X3, Y3, X2, Y2, X1, Y1, X0, Y0);

        public Cubic Shift(double dy) => Shift(0, dy);

        public Cubic Shift(double dx, double dy) => new(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy, X2 + dx, Y2 + dy, X3 + dx, Y3 + dy);

        public string C() => F($" C{N(X1)},{N(Y1)} {N(X2)},{N(Y2)} {N(X3)},{N(Y3)}");

        private static (Cubic Left, Cubic Right) Split(Cubic c, double t)
        {
            static (double, double) L(double ax, double ay, double bx, double by, double t) => (ax + (bx - ax) * t, ay + (by - ay) * t);
            var p01 = L(c.X0, c.Y0, c.X1, c.Y1, t); var p12 = L(c.X1, c.Y1, c.X2, c.Y2, t); var p23 = L(c.X2, c.Y2, c.X3, c.Y3, t);
            var a = L(p01.Item1, p01.Item2, p12.Item1, p12.Item2, t); var b = L(p12.Item1, p12.Item2, p23.Item1, p23.Item2, t);
            var m = L(a.Item1, a.Item2, b.Item1, b.Item2, t);
            return (new Cubic(c.X0, c.Y0, p01.Item1, p01.Item2, a.Item1, a.Item2, m.Item1, m.Item2),
                    new Cubic(m.Item1, m.Item2, b.Item1, b.Item2, p23.Item1, p23.Item2, c.X3, c.Y3));
        }
    }

    /// <summary>A chain of cubics that is a function of x (strictly increasing x), with y lookup and sub-path data.</summary>
    private sealed class Profile(params Cubic[] segs)
    {
        private (int Seg, double T) FindX(double x)
        {
            for (var i = 0; i < segs.Length; i++)
            {
                var s = segs[i];
                if (x < s.X0 - 1e-9 || x > s.X3 + 1e-9) continue;
                double lo = 0, hi = 1;
                for (var k = 0; k < 50; k++)
                {
                    var mid = (lo + hi) / 2;
                    if (s.At(mid).X < x) lo = mid; else hi = mid;
                }
                return (i, (lo + hi) / 2);
            }
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        public double Y(double x) { var (i, t) = FindX(x); return segs[i].At(t).Y; }

        private List<Cubic> Curves(double xa, double xb, double dy)
        {
            var (i, ta) = FindX(xa); var (j, tb) = FindX(xb);
            var list = new List<Cubic>();
            if (i == j) list.Add(segs[i].Sub(ta, tb));
            else
            {
                list.Add(segs[i].Sub(ta, 1));
                for (var k = i + 1; k < j; k++) list.Add(segs[k]);
                list.Add(segs[j].Sub(0, tb));
            }
            return list.Select(c => c.Shift(dy)).ToList();
        }

        /// <summary>Curve from xa to xb (either direction) as path data continuing from the current point, offset by dy.</summary>
        public string D(double xa, double xb, double dy = 0)
        {
            if (xa <= xb) return string.Concat(Curves(xa, xb, dy).Select(c => c.C()));
            var list = Curves(xb, xa, dy);
            list.Reverse();
            return string.Concat(list.Select(c => c.Reverse().C()));
        }
    }

    // ---- the hull: canoe body, stem, sheer ---------------------------------------------------------------------------
    // Canoe body in side section, stern to bow: the heel of a raked reverse transom (x 46, y 366), a short counter and a run
    // that crosses the DWL at about x 95 (so a little stern overhang), the lowest point (y 434, horizontal tangent) at x 500,
    // then up through a cut-away forefoot to x 874. Two cubics joined with the same tangent: one fair curve, no flat run.
    private static readonly Profile Skin = new(
        new Cubic(46, 366, 112, 396, 300, 434, 500, 434),
        new Cubic(500, 434, 640, 434, 790, 424, 874, 398));

    private const double SkinStartX = 46, SkinEndX = 874, StemHeadX = 937, StemHeadY = 232;

    // Stem: leaves the forefoot with the skin's tangent and rakes about 17 degrees to the head; about 0.5 m of overhang at
    // the DWL, a little more above.
    private static readonly Cubic Stem = new(874, 398, 895, 391.5, 910, 320, StemHeadX, StemHeadY);
    // Inner stem line: starts on the inner skin at x 866 with the same tangent, ends near the deck underside.
    private static readonly Cubic StemIn = new(866, 393.4, 886, 387.5, 901, 316, 928, 225);

    // Sheer: concave, lowest (y 294) a little aft of amidships at x 460, rising about 54 units (0.5 m) to the transom top, where it meets the cockpit coaming in one corner
    // and about 62 (0.6 m) to the stem head. The coachroof stands on it; the cockpit coaming rises above it aft.
    private const double TransomTopX = 73, TransomTopY = 240;
    private static readonly Profile Sheer = new(
        new Cubic(TransomTopX, TransomTopY, 190, 268, 330, 294, 460, 294),
        new Cubic(460, 294, 600, 294, 800, 270, StemHeadX, StemHeadY));

    /// <summary>Hull skin y at a given x (transom heel to forefoot).</summary>
    public static double HullY(double x) => Skin.Y(x);

    private static double SheerY(double x) => Sheer.Y(x);

    private static string SkinD(double xa, double xb, double dy = 0) => Skin.D(xa, xb, dy);

    private static string SheerD(double xa, double xb, double dy = 0) => Sheer.D(xa, xb, dy);

    private static string Pt(double x, double dy = 0) => F($"{N(x)},{N(HullY(x) + dy)}");

    // ---- fin keel and skeg-hung rudder ----------------------------------------------------------------------------
    // Keel: root chord 189 units (1.8 m) from x 500 to 689, tip chord 131 (1.25 m) from 508 to 639; leading edge swept back
    // about 24 degrees, trailing edge near vertical, tip squared with small rounded corners. Tip at DWL + 1.5 m.
    public const double KeelRootAft = 500, KeelRootFwd = 689, KeelTipAft = 508, KeelTipFwd = 639;
    public const double KeelBottomY = WaterlineY + 1.5 * UnitsPerMetre;   // 539.5

    // Rudder: a raked blade hung behind a skeg, well aft. Hull attach from x 96 (trailing edge) to the stock at x 136; the
    // blade goes down to DWL + 1.2 m (y 508), above the keel tip. The skeg is a short fixed fin ahead of the blade.
    public const double RudderTrailingX = 96, RudderStockX = 136, RudderBottomY = WaterlineY + 1.2 * UnitsPerMetre;   // 508
    private const double SkegForwardX = 200, SkegBottomY = 498;

    private static string KeelOutline()
    {
        var yr = HullY(KeelRootAft); var yf = HullY(KeelRootFwd);
        return F($"M{N(KeelRootAft)},{N(yr)} L{N(KeelTipAft - 0.6)},{N(KeelBottomY - 8)} Q{N(KeelTipAft)},{N(KeelBottomY)} {N(KeelTipAft + 8)},{N(KeelBottomY)}")
             + F($" L{N(KeelTipFwd - 8)},{N(KeelBottomY)} Q{N(KeelTipFwd)},{N(KeelBottomY)} {N(KeelTipFwd + 3.3)},{N(KeelBottomY - 7.3)} L{N(KeelRootFwd)},{N(yf)}");
    }

    /// <summary>Hull, keel, rudder and stem as one open path from the transom top, round the bottom, to the stem head.</summary>
    private static string HullRun() =>
        F($"M{N(TransomTopX)},{N(TransomTopY)} L{N(SkinStartX)},366")
        + SkinD(SkinStartX, RudderTrailingX)
        // rudder: trailing edge, rounded tip, then up its fore edge to the skeg's heel
        + F($" L99,{N(RudderBottomY - 8)} Q99.6,{N(RudderBottomY)} 108,{N(RudderBottomY)} L124,{N(RudderBottomY)} L128,{N(SkegBottomY + 2)}")
        // skeg: bottom, then its leading edge up and forward to the hull
        + F($" L156,{N(SkegBottomY)} L{N(SkegForwardX)},{N(HullY(SkegForwardX))}")
        + SkinD(SkegForwardX, KeelRootAft)
        + KeelOutline().Replace("M", "L")   // the keel starts where the skin reaches the root: continue the path
        + SkinD(KeelRootFwd, SkinEndX)
        + Stem.C();

    /// <summary>Hull outline incl. keel and rudder, closed along the full sheer; filled with paper.</summary>
    public static readonly string HullFill = HullRun() + SheerD(StemHeadX, TransomTopX) + " Z";

    /// <summary>Hull outline, open, ending with the foredeck sheer back to the foot of the coachroof (heavy ink).</summary>
    public static readonly string HullStroke = HullRun() + SheerD(StemHeadX, CoachroofFootX);

    /// <summary>Keel fin, for hatching (the lead).</summary>
    public static readonly string KeelHatch = KeelOutline() + " Z";

    /// <summary>The hinge line between skeg and rudder, from the stock down to the lower pintle.</summary>
    private static string RudderHinge() => F($"M{N(RudderStockX)},{N(HullY(RudderStockX))} L127,{N(RudderBottomY - 2)}");

    /// <summary>Inner skin, a thin line seven units inside the outer one, up the inside of the stem.</summary>
    public const double SkinThickness = 7;
    private static readonly string InnerSkin =
        F($"M{Pt(84, -SkinThickness)}") + SkinD(84, 866, -SkinThickness) + F($" L{N(StemIn.X0)},{N(StemIn.Y0)}") + StemIn.Sub(0, 0.9).C();

    /// <summary>Hull skin between outer and inner line, for light hatching.</summary>
    private static readonly string SkinBand =
        F($"M{Pt(84)}") + SkinD(84, 866) + F($" L{Pt(866, -SkinThickness)}") + SkinD(866, 84, -SkinThickness) + " Z";

    // ---- yard --------------------------------------------------------------------------------------------------------
    /// <summary>Cradle pad whose top edge is the hull curve between x1 and x2, t thick.</summary>
    private static string Pad(double x1, double x2, double t) =>
        F($"M{Pt(x1)}") + SkinD(x1, x2) + F($" L{Pt(x2, t)}") + SkinD(x2, x1, t) + " Z";

    /// <summary>Cradle stand: pad under the hull, two splayed legs to the ground, a crossbar and a brace.</summary>
    private static string Stand(double x1, double x2, double splay)
    {
        const double t = 8;
        var xa = x1 + 8; var xb = x2 - 8;
        var ya = HullY(xa) + t; var yb = HullY(xb) + t;
        var fa = xa - splay; var fb = xb + splay;
        double LegX(double top, double foot, double ytop, double y) => top + (foot - top) * (y - ytop) / (GroundY - ytop);
        var yc = GroundY - 42; var yd = GroundY - 17;
        return Pad(x1, x2, t)
            + F($" M{N(xa)},{N(ya)} L{N(fa)},{GroundY} M{N(xb)},{N(yb)} L{N(fb)},{GroundY}")
            + F($" M{N(LegX(xa, fa, ya, yc))},{N(yc)} L{N(LegX(xb, fb, yb, yc))},{N(yc)}")
            + F($" M{N(LegX(xa, fa, ya, yc))},{N(yc)} L{N(LegX(xb, fb, yb, yd))},{N(yd)}");
    }

    /// <summary>Yard ladder leaning on the bow, from the ground to the foredeck.</summary>
    private static string Ladder()
    {
        const double fl = 962, fr = 984, tl = 948, tr = 966, top = 236;
        var sb = new System.Text.StringBuilder(F($"M{fl},{GroundY} L{tl},{top} M{fr},{GroundY} L{tr},{top}"));
        for (var y = GroundY - 22.0; y > top + 10; y -= 22)
        {
            var k = (GroundY - y) / (GroundY - top);
            sb.Append(F($" M{N(fl + (tl - fl) * k)},{N(y)} L{N(fr + (tr - fr) * k)},{N(y)}"));
        }
        return sb.ToString();
    }

    // The keel stands on a timber block a little wider than the tip.
    private const double BlockX1 = 494, BlockX2 = 654;

    /// <summary>Drawn behind the hull: ground line, joints, keel block, cradle stands, yard ladder.</summary>
    public static readonly ImmutableArray<DecorPath> Backdrop = ImmutableArray.Create(
        new DecorPath("detail", F($"M150,{GroundY} L136,592 M420,{GroundY} L412,592 M780,{GroundY} L792,592")),
        new DecorPath("ground", F($"M16,{GroundY} L984,{GroundY}")),
        // timber block under the keel tip, with end grain and wedges
        new DecorPath("yard", F(
            $"M{BlockX1},{N(KeelBottomY)} L{BlockX2},{N(KeelBottomY)} L{BlockX2},{GroundY} L{BlockX1},{GroundY} Z M550,{N(KeelBottomY)} L550,{GroundY} M598,{N(KeelBottomY)} L598,{GroundY} M{BlockX1},{GroundY} L{BlockX1 - 10},{GroundY} M{BlockX2},{GroundY} L{BlockX2 + 10},{GroundY}")),
        new DecorPath("yard", Stand(236, 282, 14)),   // aft stand
        new DecorPath("yard", Stand(340, 386, 14)),   // mid stand
        new DecorPath("yard", Stand(790, 832, 22)),   // bow stand
        new DecorPath("yard", Ladder()));

    // ---- topsides, rig ----------------------------------------------------------------------------------------------
    // The coachroof stands on the sheer; its forward face rakes back from the roof edge (650, 196) to the foot at x 688.
    private const double CoachroofFootX = 688;
    private static readonly double CoachroofFootY = SheerY(CoachroofFootX);

    /// <summary>
    /// Heavy ink above the hull: the stern seat and cockpit coaming, the bridge deck, the coachroof. The aft face of the
    /// coachroof is the open companionway (dashed). The far side of the sheer is drawn thin where nothing else is.
    /// </summary>
    public static readonly ImmutableArray<DecorPath> Topsides = ImmutableArray.Create(
        new DecorPath("hull-line", F($"M{N(TransomTopX)},{N(TransomTopY)} L236,240 L270,238 M292,196 L650,196 L{N(CoachroofFootX)},{N(CoachroofFootY)}")),
        new DecorPath("dash", "M270,238 L292,196"),
        // the sheer on the far side, seen through the saloon; hidden by the cockpit seat, steps and berth
        new DecorPath("detail", F($"M392,{N(SheerY(392))}") + SheerD(392, CoachroofFootX)),
        // headliner and deck undersides
        new DecorPath("detail", F($"M298,204 L644,204 L683,{N(SheerY(683) + 6)}") + SheerD(683, 924, 6) + " M238,248 L272,246"));

    /// <summary>Drawn over the hull and regions, never clickable: mast, boom, sprayhood, hatches, pulpit, rudder hinge.</summary>
    public static readonly ImmutableArray<DecorPath> Overlay = ImmutableArray.Create(
        // Mast stub through the coachroof, with the compression post below it.
        new DecorPath("rig-line", "M590,92 L598,92 L598,196 M590,92 L590,196"),
        new DecorPath("detail", "M590,204 L590,424 M598,204 L598,424"),
        // Boom, outline only.
        new DecorPath("rig-line", "M104,142 L590,142 M104,150 L590,150 M104,142 L104,150 M578,146 L590,146"),
        // Sprayhood over the companionway.
        new DecorPath("sprayhood", "M300,196 L312,170 L346,170 L360,196 M322,170 L320,196 M338,170 L340,196"),
        // Foredeck hatch and bow pulpit, both standing on the sheer.
        new DecorPath("ink-thin", F($"M772,{N(SheerY(772))} L778,{N(SheerY(778) - 8)} L806,{N(SheerY(806) - 8)} L812,{N(SheerY(812))} M896,{N(SheerY(896))} L896,{N(SheerY(896) - 28)} L{N(StemHeadX - 3)},{N(SheerY(StemHeadX - 3) - 28)} L{N(StemHeadX - 3)},{N(SheerY(StemHeadX - 3))} M896,{N(SheerY(896) - 14)} L{N(StemHeadX - 3)},{N(SheerY(StemHeadX - 3) - 14)}")),
        // Rudder: the hinge between skeg and blade
        new DecorPath("ink-thin", RudderHinge()));


    // ---- interior: thin ink, decor only -----------------------------------------------------------------------------
    // Cockpit sole y292, cabin sole y392, bulkheads at x236 and x722, panel at x326.
    // Aft to forward: cockpit (locker and fuel tank under it, rudder stock beside) | quarter berth with battery box |
    // engine under the steps | saloon | forepeak.
    public static readonly ImmutableArray<DecorPath> Interior = ImmutableArray.Create(
        new DecorPath("hatch-fill", SkinBand),
        new DecorPath("hatch-fill", KeelHatch),
        new DecorPath("ink-thin", InnerSkin),

        // cockpit well: aft wall, sole (the locker lid is a cover), seat line beyond the cut
        new DecorPath("ink-thin", "M75,240 L75,292 L150,292 M220,292 L236,292"),
        new DecorPath("furniture", "M80,264 L234,264 M80,264 L80,292"),
        // rudder stock up into the cockpit
        new DecorPath("detail", F($"M136,292 L136,{N(HullY(136) - SkinThickness)} M131,292 L141,292")),
        // cockpit / aft cabin bulkhead, and the forepeak bulkhead
        new DecorPath("ink-thin", F($"M236,240 L236,{N(HullY(236) - SkinThickness)} M722,{N(SheerY(722) + 6)} L722,{N(HullY(722) - SkinThickness)}")),

        // fuel tank in the cockpit locker, and the exhaust hose with its anti-siphon loop
        new DecorPath("tone-fill", "M150,326 L222,326 L222,374 L150,374 Z"),
        new DecorPath("ink-thin", "M150,326 L222,326 L222,374 L150,374 Z M186,326 L186,374 M150,340 L222,340"),
        new DecorPath("detail", "M236,312 L126,312 Q114,312 114,303 Q114,296 104,296 Q94,296 92,306 L58,314 M222,360 L236,360"),

        // quarter berth: cushion, pillow, battery box with its battery
        new DecorPath("wood-fill", "M240,316 L324,316 L324,324 L240,324 Z"),
        new DecorPath("furniture", "M240,316 L324,316 L324,324 L240,324 Z M242,316 Q244,306 256,306 L262,306 Q266,310 264,316"),
        new DecorPath("ink-thin", "M266,358 L266,408 L320,408 L320,358"),
        new DecorPath("tone-fill", "M272,376 L314,376 L314,406 L272,406 Z"),
        new DecorPath("detail", "M272,376 L314,376 L314,406 L272,406 Z M278,372 L278,376 M308,372 L308,376"),

        // a small diesel: block with tapered sump, rocker cover, pulley on the forward face, starter low aft, oil filter, beds
        new DecorPath("tone-fill", "M340,320 L438,320 L438,392 L428,408 L352,408 L340,392 Z"),
        new DecorPath("ink-thin", "M340,320 L438,320 L438,392 L428,408 L352,408 L340,392 Z M384,320 L384,308 L432,308 L432,320 M438,344 L446,344 L446,362 L438,362"),
        new DecorPath("ink-thin", "M330,378 L352,378 L352,396 L330,396 Z M396,392 L396,412 L410,412 L410,392"),
        new DecorPath("detail", "M340,350 L438,350 M336,414 L444,414 M390,320 L390,350 M410,320 L410,350"),

        // saloon: settees, table, bilge details
        new DecorPath("wood-fill", "M470,304 L476,304 L476,346 L522,346 L522,392 L470,392 Z"),
        new DecorPath("wood-fill", "M716,304 L710,304 L710,346 L656,346 L656,392 L716,392 Z"),
        new DecorPath("furniture", "M470,304 L476,304 L476,346 L522,346 L522,392 L470,392 Z M716,304 L710,304 L710,346 L656,346 L656,392 L716,392 Z"),
        new DecorPath("furniture", "M528,328 L584,328 L584,332 L528,332 Z M552,332 L552,390 M542,392 L562,392"),
        new DecorPath("detail", F($"M576,416 L576,{N(HullY(580))} M584,416 L584,{N(HullY(580))} M520,396 L520,{N(HullY(520) - SkinThickness)} M660,396 L660,{N(HullY(660) - SkinThickness)}")),

        // forepeak: V-berth
        new DecorPath("wood-fill", "M728,352 L884,346 L884,354 L728,360 Z"),
        new DecorPath("furniture", "M728,352 L884,346 L884,354 L728,360 Z M732,352 Q734,342 746,342 L752,342 Q756,346 754,352"));

    // ---- places: clickable regions that never overlap ---------------------------------------------------------------
    private const double StairTopY = 262, StairFootX = 372, StairFootY = 336;

    private static double StairUnderY(double x) => StairTopY + (x - 276) * (StairFootY - StairTopY) / (StairFootX - 276);

    public static readonly ImmutableArray<PlaceShape> Places = ImmutableArray.Create(
        // Well plus locker, down to the skin.
        new PlaceShape("Cockpit",
            F($"M76,240 L236,240 L236,{N(HullY(236))}") + SkinD(236, 62) + " Z",
            "Cockpit", 150, 258, 200, 292),

        // The steps and the air above them, in the hatch.
        new PlaceShape("Companionway",
            F($"M268,238 L300,204 L340,204 L392,320 L{StairFootX},{StairFootY} L276,{StairTopY} Z"),
            "Companionway", 352, 224, 305, 261),

        // Aft cabin: berth, battery box, up to the panel and under the stairs.
        new PlaceShape("Quarter berth",
            F($"M238,248 L271.3,248 L276,{StairTopY} L326,{N(StairUnderY(326))} L326,{N(HullY(326))}") + SkinD(326, 238) + " Z",
            "Quarter berth", 240, 268, 262, 316),

        // Saloon and the bilge below its sole, bounded by the engine's front, the companionway edge and the bulkhead.
        new PlaceShape("Saloon",
            F($"M340,204 L644,204 L683,{N(SheerY(683) + 6)} L722,{N(SheerY(722) + 6)} L722,{N(HullY(722))}") + SkinD(722, 456) + " L456,320 L392,320 Z",
            "Saloon", 646, 290, 536, 392),

        // V-berth.
        new PlaceShape("Forepeak",
            F($"M722,{N(SheerY(722) + 6)}") + SheerD(722, 916, 6) + F($" L{N(StemIn.At(0.82).X)},{N(StemIn.At(0.82).Y)}")
                + StemIn.Sub(0, 0.82).Reverse().C() + F($" L{Pt(866)}") + SkinD(866, 722) + " Z",
            "Forepeak", 790, 330, 810, 352),

        // A strip along the coachroof and foredeck.
        new PlaceShape("On deck",
            F($"M236,240 L270,238 L292,196 L650,196 L{N(CoachroofFootX)},{N(CoachroofFootY)}") + SheerD(CoachroofFootX, 930)
                + F($" L930,{N(SheerY(930) - 34)}") + SheerD(930, CoachroofFootX, -34) + " L650,166 L292,166 L270,206 L236,210 Z",
            "On deck", 430, 186, 470, 196),

        // The ground around and under the hull, outside it. The ladder is at the bow.
        new PlaceShape("Beside the hull",
            F($"M14,300 L61.3,300 L{SkinStartX},366") + SkinD(SkinStartX, SkinEndX) + F($" L985,398 L985,{GroundY} L14,{GroundY} Z"),
            "Beside the hull", 20, 570, 936, GroundY));

    // ---- covers, drawn in state --------------------------------------------------------------------------------------
    private const string Stairs = "M276,246 h19 v14.8 h19 v14.8 h19 v14.8 h19 v14.8 h19 v14.8";

    // Locker lid hinged at (150,292), 70 long, open 60 degrees: tip (185,231.4).
    // Battery lid hinged at (266,358), 52 long, open 38 degrees: tip (307,326).
    // Floorboard hinged at (646,392), 40 long, lifted 35 degrees: tip (613.2,369.1).
    public static readonly ImmutableArray<CoverShape> Covers = ImmutableArray.Create(
        new CoverShape("Companionway steps", "M276,246 L371,320 L371,336 L276,262 Z",
            ImmutableArray.Create(new DecorPath("ink-thin", Stairs)),
            ImmutableArray.Create(new DecorPath("dash", Stairs)),
            "Steps", 290, 236),

        new CoverShape("Quarter-berth panel", "M320,304 L332,304 L332,418 L320,418 Z",
            ImmutableArray.Create(new DecorPath("ink", "M326,303 L326,416"), new DecorPath("detail", "M323,330 h6 M323,390 h6")),
            ImmutableArray.Create(new DecorPath("dash", "M326,303 L326,416"),
                new DecorPath("ink-thin", "M254,316 L258,316 L244,270 L240,270 Z")),
            "Panel", 296, 330),

        new CoverShape("Battery-box lid", "M262,350 L324,350 L324,362 L262,362 Z",
            ImmutableArray.Create(new DecorPath("ink-thin", "M266,354 L320,354 L320,358 L266,358 Z")),
            ImmutableArray.Create(new DecorPath("dash", "M266,354 L320,354 L320,358 L266,358 Z"),
                new DecorPath("ink-thin", "M266,358 L307,326 L304.5,322.8 L263.5,354.8 Z")),
            "Lid", 270, 350),

        new CoverShape("Cockpit-locker lid", "M146,284 L224,284 L224,296 L146,296 Z",
            ImmutableArray.Create(new DecorPath("ink-thin", "M150,288 L220,288 L220,292 L150,292 Z")),
            ImmutableArray.Create(new DecorPath("dash", "M150,288 L220,288 L220,292 L150,292 Z"),
                new DecorPath("ink-thin", "M150,292 L185,231.4 L181.5,229.4 L146.5,290 Z")),
            "Lid", 168, 282),

        new CoverShape("Saloon floorboards", "M456,388 L722,388 L722,400 L456,400 Z",
            ImmutableArray.Create(
                new DecorPath("ink-thin", "M456,392 L722,392 M456,396 L722,396 M520,392 L520,396 M580,392 L580,396 M606,392 L606,396 M646,392 L646,396")),
            ImmutableArray.Create(
                new DecorPath("ink-thin", "M456,392 L606,392 M456,396 L606,396 M646,392 L722,392 M646,396 L722,396 M520,392 L520,396 M580,392 L580,396 M606,392 L606,396 M646,392 L646,396"),
                new DecorPath("dash", "M606,392 L646,392"),
                new DecorPath("ink-thin", "M646,392 L613.2,369.1 L615.5,365.8 L648.3,388.7 Z")),
            "Floorboards", 560, 410));

    // ---- components ------------------------------------------------------------------------------------------------
    /// <summary>Where each of the eight true positions sits on the drawing (the facade's position labels).</summary>
    public static readonly ImmutableDictionary<string, (double X, double Y)> PositionAnchors =
        new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Engine bay, front (under the steps)"] = (445, 364),
            ["Engine bay, quarter-berth side"] = (360, 350),
            ["Under the quarter berth"] = (293, 370),
            ["Under the cockpit locker"] = (170, 350),
            ["Bilge under the saloon"] = (600, 410),
            ["Cockpit"] = (150, 275),
            ["Deck"] = (594, 190),
            ["Hull, outside"] = (400, 480),
            // The positions of the parts fitted later or added with the boat as sold (design note 010): no per-name table entry, so the
            // fallback fan-out from these anchors places them. Coordinates are estimates from the place shapes, not checked by eye.
            ["Heads, forward"] = (800, 372),
            ["Galley, by the companionway"] = (410, 300),
            ["At the bow"] = (838, 236),
            ["Saloon berths"] = (640, 345),
            ["On the mast"] = (594, 120),
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private const string EngFront = "Engine bay, front (under the steps)";
    private const string EngSide = "Engine bay, quarter-berth side";
    private const string QBerth = "Under the quarter berth";
    private const string Locker = "Under the cockpit locker";
    private const string Bilge = "Bilge under the saloon";

    /// <summary>
    /// One component's marker, in drawing coordinates. Side: r, l (label beside the dot), u, d (above, below), or custom
    /// when LabelX/LabelY are set (then a leader line runs from the dot to the label). Dense markers show their label
    /// only on hover, selection or a known fault. Slot: assign the label to the next free slot in its cluster.
    /// </summary>
    private sealed record Entry(string Pos, string Name, string Label, double X, double Y, char Side = 'r',
        double LabelX = double.NaN, double LabelY = double.NaN, string Anchor = "start", bool Dense = false, bool Slot = false);

    private static Entry Eng(string pos, string name, string label, double x, double y) => new(pos, name, label, x, y, Dense: true, Slot: true);

    private static readonly Entry[] Raw =
    {
        // engine, fuel side: top edge, aft face, starter low aft, oil filter underneath
        Eng(EngSide, "Exhaust elbow", "Exhaust elbow", 392, 300),
        Eng(EngSide, "Glow plugs", "Glow plugs", 410, 300),
        Eng(EngSide, "Injection pump", "Inj. pump", 331, 326),
        Eng(EngSide, "Lift pump", "Lift pump", 331, 338),
        Eng(EngSide, "Secondary fuel filter", "2nd filter", 331, 350),
        Eng(EngSide, "Fuel supply line", "Fuel supply", 331, 362),
        Eng(EngSide, "Fuel return line", "Fuel return", 331, 374),
        Eng(EngSide, "Starter solenoid", "Solenoid", 352, 370),
        Eng(EngSide, "Starter motor", "Starter", 341, 387),
        Eng(EngSide, "Engine ground stud", "Ground stud", 334, 404),
        Eng(EngSide, "Oil filter", "Oil filter", 403, 416),
        // engine, front: injectors on top, the rest down the forward face, the block in its middle
        Eng(EngFront, "Injectors", "Injectors", 428, 300),
        Eng(EngFront, "Engine block", "Engine block", 388, 358),
        Eng(EngFront, "Alternator", "Alternator", 451, 322),
        Eng(EngFront, "Alternator belt", "Alt. belt", 451, 334),
        Eng(EngFront, "Raw-water pump", "RW pump", 451, 346),
        Eng(EngFront, "Raw-water impeller", "Impeller", 451, 358),
        Eng(EngFront, "Heat exchanger", "Heat exch.", 451, 370),
        Eng(EngFront, "Thermostat", "Thermostat", 451, 382),
        Eng(EngFront, "Coolant", "Coolant cap", 451, 394),
        Eng(EngFront, "Engine oil", "Engine oil", 451, 406),

        // battery box and the cables above its lid
        new(QBerth, "Negative cable", "Neg. cable", 272, 340, Dense: true, Slot: true),
        new(QBerth, "Battery switch", "Battery switch", 296, 340, Dense: true, Slot: true),
        new(QBerth, "Main positive cable", "+ cable", 318, 340, Dense: true, Slot: true),
        new(QBerth, "Negative terminal", "Neg. terminal", 276, 370, Dense: true, Slot: true),
        new(QBerth, "Positive terminal", "Pos. terminal", 310, 370, Dense: true, Slot: true),
        new(QBerth, "Start battery", "Start battery", 293, 392, Dense: true, Slot: true),

        // cockpit locker: tank, valve, filter on the bulkhead, hose with its loop
        new(Locker, "Fuel tank", "Fuel tank", 168, 352),
        new(Locker, "Fuel shutoff valve", "Shutoff", 222, 334, 'l'),
        new(Locker, "Primary fuel filter / water separator", "Primary filter", 228, 368, 'l'),
        new(Locker, "Exhaust hose", "Exhaust hose", 208, 312, 'u'),
        new(Locker, "Anti-siphon loop", "Anti-siphon", 104, 296, 'r'),

        // bilge
        new(Bilge, "Raw-water seacock", "Seacock", 580, 414),
        new(Bilge, "Raw-water strainer", "Strainer", 628, 404),

        // cockpit, deck
        new("Cockpit", "Ignition panel", "Ignition panel", 228, 276, 'l'),
        new("Deck", "Standing rig", "Standing rig", 610, 188, 'r'),

        // outside the hull: skin, keel, rudder, bottom paint
        new("Hull, outside", "Hull", "Hull", 307, HullY(307), 'd'),
        new("Hull, outside", "Antifouling", "Antifouling", 440, HullY(440), 'd'),
        new("Hull, outside", "Keel", "Keel", 560, 500, 'r'),
        new("Hull, outside", "Rudder", "Rudder", 116, 440, 'l'),
    };

    private sealed record Resolved(string Pos, double Dx, double Dy, string Label, char Side, double Lx, double Ly, string Anchor, bool Dense);

    // Hover labels for dense clusters sit in free air: engine callouts in the saloon headroom, battery callouts above the berth.
    private static (double X, double Y) EngineSlot(int i) => (new[] { 398.0, 488, 606 }[i / 7], 236 + 11.0 * (i % 7));
    private static (double X, double Y) BatterySlot(int i) => (244, 258 + 10.0 * i);

    private static readonly ImmutableDictionary<string, Resolved> Table = BuildTable();

    private static ImmutableDictionary<string, Resolved> BuildTable()
    {
        var abs = new Dictionary<string, (Entry E, double Lx, double Ly, string Anchor)>(StringComparer.OrdinalIgnoreCase);
        void Assign(IEnumerable<Entry> items, Func<int, (double X, double Y)> slot)
        {
            var i = 0;
            foreach (var e in items.OrderBy(e => e.X).ThenBy(e => e.Y))
            {
                var (sx, sy) = slot(i++);
                abs[e.Name] = (e, sx, sy, "start");
            }
        }
        Assign(Raw.Where(e => e.Slot && e.Pos is EngFront or EngSide), EngineSlot);
        Assign(Raw.Where(e => e.Slot && e.Pos == QBerth), BatterySlot);
        foreach (var e in Raw.Where(e => !e.Slot)) abs[e.Name] = (e, e.LabelX, e.LabelY, e.Anchor);

        var b = ImmutableDictionary.CreateBuilder<string, Resolved>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, v) in abs)
        {
            var (e, lx, ly, anchor) = v;
            var a = PositionAnchors[e.Pos];
            // For non-slot entries Lx/Ly are NaN unless given: the Side picks the label spot.
            b[name] = new Resolved(e.Pos, e.X - a.X, e.Y - a.Y, e.Label, e.Side,
                double.IsNaN(lx) ? double.NaN : lx - e.X, double.IsNaN(ly) ? double.NaN : ly - e.Y, anchor, e.Dense);
        }
        return b.ToImmutable();
    }

    /// <summary>True for components in a crowded cluster (engine, battery): their label shows on hover, selection or a known fault.</summary>
    public static bool IsDense(string componentName) => Table.TryGetValue(componentName, out var r) && r.Dense;

    /// <summary>
    /// Marker spot for a component: its position label picks the anchor, the per-name table the offset and the short label.
    /// Unknown names fall back to the anchor plus an index-based offset.
    /// </summary>
    public static MarkerSpot SpotFor(string componentName, string positionLabel, int indexAtPosition)
    {
        if (Table.TryGetValue(componentName, out var r))
        {
            var a = PositionAnchors.TryGetValue(positionLabel, out var pa) ? pa : PositionAnchors[r.Pos];
            double x = a.X + r.Dx, y = a.Y + r.Dy;
            if (!double.IsNaN(r.Lx))
            {
                double lx = x + r.Lx, ly = y + r.Ly;
                var (leadX, leadY) = r.Anchor == "end" ? (lx + 3, ly - 3) : (lx - 3, ly - 3);
                return new MarkerSpot(x, y, r.Label, lx, ly, r.Anchor, leadX, leadY);
            }
            return r.Side switch
            {
                'l' => MarkerSpot.Left(x, y, r.Label),
                'u' => new MarkerSpot(x, y, r.Label, x, y - 11, "middle"),
                'd' => new MarkerSpot(x, y, r.Label, x, y + 20, "middle"),
                _ => MarkerSpot.Right(x, y, r.Label),
            };
        }

        var (ax, ay) = PositionAnchors.TryGetValue(positionLabel, out var fa) ? fa : (500.0, 300.0);
        var fx = ax + 16 * (indexAtPosition % 5 - 2);
        var fy = ay + 14 + 16 * (indexAtPosition / 5);
        return MarkerSpot.Right(fx, fy, componentName.Length > 14 ? componentName[..14] : componentName);
    }
}
