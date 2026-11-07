using System.Collections.Immutable;

namespace Haulout.Web.Art.CloseUps;

/// <summary>
/// Close-up: On deck. Standing on the starboard side deck forward of the mast, looking aft along the boat. Mast, shrouds,
/// boom and sprayhood above; guardwires; the toerail, and the yard far below on the hard.
/// </summary>
public static class OnDeckCloseUp
{
    // Camera: 0.9 m to starboard of the centreline, eye 1.7 m above the side deck. World X right, Y up from the deck, Z aft.
    private const double Focal = 520, Cx = 480, Horizon = 270, CamX = 0.9, Eye = 1.7, Ground = -2.0;

    private static (double X, double Y) C(double x, double y, double z) => (Cx + Focal * (x - CamX) / z, Horizon + Focal * (Eye - y) / z);

    private static string Seg(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var a = C(x0, y0, z0); var b = C(x1, y1, z1);
        return ArtPen.Line(a.X, a.Y, b.X, b.Y);
    }

    private static string Pl(bool close, params (double X, double Y, double Z)[] p) =>
        ArtPen.Poly(close, p.Select(q => C(q.X, q.Y, q.Z)).ToArray());

    /// <summary>A line from a screen point towards another, cut where it leaves the top of the sheet.</summary>
    private static string ToTop((double X, double Y) from, (double X, double Y) to)
    {
        if (to.Y >= 0) return ArtPen.Line(from.X, from.Y, to.X, to.Y);
        var s = (from.Y - 0) / (from.Y - to.Y);
        return ArtPen.Line(from.X, from.Y, from.X + (to.X - from.X) * s, 0);
    }

    private static string Gravel()
    {
        var sb = new System.Text.StringBuilder();
        uint seed = 7;
        double Next() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / (double)(1 << 24); }
        for (var i = 0; i < 90; i++)
        {
            var y = 345 + Math.Pow(Next(), 0.8) * 225;
            var x = 520 + Next() * 480;
            if (x < 500 + (y - 345) * 0.05) continue;
            var len = 3 + (y - 330) / 18 * (0.5 + Next());
            sb.Append(ArtPen.Line(x, y, x + len, y + (Next() - 0.5) * 1.5)).Append(' ');
        }
        for (var i = 0; i < 24; i++)
        {
            var y = 400 + Next() * 170; var x = 20 + Next() * 60;
            sb.Append(ArtPen.Line(x, y, x + 3 + Next() * 8, y)).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    private static string Spruce(double x, double baseY, double h) =>
        $"M{ArtPen.N(x - h * 0.18)},{ArtPen.N(baseY)} L{ArtPen.N(x)},{ArtPen.N(baseY - h)} L{ArtPen.N(x + h * 0.18)},{ArtPen.N(baseY)}";

    public static readonly CloseUpScene Scene = Build();

    private static CloseUpScene Build()
    {
        var back = new List<DecorPath>();

        // far yard: treeline, a boat shed, fence, neighbour boat on stands
        var trees = new System.Text.StringBuilder();
        for (var x = -10.0; x < 1010; x += 16 + (x * 7 % 9)) trees.Append(Spruce(x, 322, 18 + (x * 13 % 16))).Append(' ');
        back.Add(ArtPen.D("furniture", trees.ToString().TrimEnd(), "M0,322 L1000,322"));
        back.Add(ArtPen.D("yard", ArtPen.Poly(true, (700, 330), (700, 300), (760, 286), (830, 300), (830, 330)), "M700,300 L830,300", "M728,330 L728,310 L760,310 L760,330"));
        back.Add(ArtPen.D("yard", "M0,334 L1000,334"));
        back.Add(ArtPen.D("furniture", "M20,334 L20,326 M60,334 L60,326 M100,334 L100,326 M20,328 L100,328 M850,334 L850,326 M890,334 L890,326 M930,334 L930,326 M850,328 L930,328"));
        var n1 = C(7, 0, 12.5); var n2 = C(7, 0, 21); var n3 = C(7, Ground + 0.4, 21); var n4 = C(7, Ground + 0.4, 12.5);
        back.Add(ArtPen.D("furniture", ArtPen.Poly(true, n1, n2, n3, n4), ArtPen.Line(n1.X, n1.Y + 14, n2.X, n2.Y + 6)));
        var nm = C(7, 0, 16.5);
        back.Add(ArtPen.D("furniture", ArtPen.Line(nm.X, nm.Y, nm.X, 18), ArtPen.Line(nm.X, 60, nm.X - 12, nm.Y), ArtPen.Line(nm.X, 60, nm.X + 12, nm.Y)));

        // ground below, with a tall jack stand and a ladder
        back.Add(ArtPen.D("ground", "M520,346 L1000,346"));
        back.Add(ArtPen.D("detail", Gravel()));
        back.Add(ArtPen.D("yard", Seg(2.6, Ground, 8.2, 2.6, -0.9, 8.2), Seg(2.2, Ground, 8.2, 2.6, -1.1, 8.2), Seg(3.0, Ground, 8.2, 2.6, -1.1, 8.2),
            Seg(2.35, -1.0, 8.2, 2.85, -1.0, 8.2), Seg(2.45, -1.55, 8.2, 2.77, -1.55, 8.2)));
        back.Add(ArtPen.D("yard", Seg(2.5, Ground, 11, 2.5, -0.9, 11), Seg(2.1, Ground, 11, 2.5, -1.1, 11), Seg(2.9, Ground, 11, 2.5, -1.1, 11)));

        // the boat hides the ground behind her
        back.Add(ArtPen.D("paper-fill", Pl(true, (-1.32, 0, 2.9), (-1.32, 0, 9.9), (1.32, 0, 9.9), (1.32, 0, 2.9))));

        back.Add(ArtPen.D("hatch-fill", Pl(true, (0.85, 0, 3.0), (1.3, 0, 3.0), (1.3, 0, 9.9), (0.85, 0, 7.2)), Pl(true, (-1.3, 0, 3.0), (-0.85, 0, 3.0), (-0.85, 0, 7.2), (-1.3, 0, 9.9))));

        // toerails, deck edge
        foreach (var x in new[] { -1.3, 1.3 })
            back.Add(ArtPen.D("hull-line", Seg(x, 0, 2.9, x, 0, 9.9), Seg(x, 0.09, 2.9, x, 0.09, 9.9)));
        back.Add(ArtPen.D("hull-line", Seg(-1.3, 0, 9.9, 1.3, 0, 9.9)));

        // coachroof: front face with two portlights, top, grab rails
        back.Add(ArtPen.D("hull-line", Pl(true, (-0.85, 0, 3.0), (0.85, 0, 3.0), (0.85, 0.55, 3.0), (-0.85, 0.55, 3.0)),
            Pl(false, (-0.85, 0.55, 3.0), (-0.85, 0.55, 7.2), (0.85, 0.55, 7.2), (0.85, 0.55, 3.0)),
            Seg(-0.85, 0, 7.2, -0.85, 0.55, 7.2), Seg(-0.85, 0, 3.0, -0.85, 0, 7.2)));
        var p1 = C(-0.42, 0.28, 3.0); var p2 = C(0.42, 0.28, 3.0);
        back.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(p1.X, p1.Y, 30, 17), ArtPen.Ellipse(p2.X, p2.Y, 26, 17)));
        back.Add(ArtPen.D("detail", ArtPen.Ellipse(p1.X, p1.Y, 24, 12), ArtPen.Ellipse(p2.X, p2.Y, 20, 12)));
        back.Add(ArtPen.D("furniture", Seg(-0.55, 0.6, 3.3, -0.55, 0.6, 6.4), Seg(0.55, 0.6, 3.3, 0.55, 0.6, 6.4),
            Seg(-0.55, 0.55, 3.3, -0.55, 0.6, 3.3), Seg(0.55, 0.55, 3.3, 0.55, 0.6, 3.3), Seg(-0.55, 0.55, 6.4, -0.55, 0.6, 6.4), Seg(0.55, 0.55, 6.4, 0.55, 0.6, 6.4)));

        // mast: two lines up and out of the sheet, step on the cabin top, partners
        var mb = C(0, 0.55, 3.9);
        var mw = Focal * 0.12 / 3.9;
        back.Add(ArtPen.D("rig-line", ArtPen.Line(mb.X - mw, mb.Y, mb.X - mw, 0), ArtPen.Line(mb.X + mw, mb.Y, mb.X + mw, 0)));
        back.Add(ArtPen.D("ink", ArtPen.Ellipse(mb.X, mb.Y, mw + 7, 5)));
        back.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(mb.X, mb.Y - 2, mw + 3, 3), ArtPen.Line(mb.X - mw - 7, mb.Y, mb.X - mw - 7, mb.Y + 5), ArtPen.Line(mb.X + mw + 7, mb.Y, mb.X + mw + 7, mb.Y + 5)));
        // halyards down the mast to cleats, tails coiled
        back.Add(ArtPen.D("detail",
            ArtPen.Line(mb.X - mw + 3, 0, mb.X - mw + 3, mb.Y - 52), ArtPen.Line(mb.X + mw - 3, 0, mb.X + mw - 3, mb.Y - 46),
            ArtPen.Line(mb.X - mw - 5, mb.Y - 52, mb.X - mw + 3, mb.Y - 52), ArtPen.Line(mb.X + mw - 3, mb.Y - 46, mb.X + mw + 5, mb.Y - 46)));
        back.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(mb.X - mw - 8, mb.Y - 40, 7, 9), ArtPen.Ellipse(mb.X + mw + 8, mb.Y - 34, 7, 9),
            ArtPen.Line(mb.X - mw - 8, mb.Y - 52, mb.X - mw - 8, mb.Y - 49), ArtPen.Line(mb.X + mw + 8, mb.Y - 46, mb.X + mw + 8, mb.Y - 43)));
        back.Add(ArtPen.D("ink-thin", ArtPen.Rect(mb.X - mw - 8, mb.Y - 55, 11, 3), ArtPen.Rect(mb.X + mw - 3, mb.Y - 49, 11, 3)));

        // boom swung out to starboard, sail furled on it
        var g = C(0, 1.95, 4.2); var e = C(1.1, 1.97, 7.6);
        back.Add(ArtPen.D("rig-line", ArtPen.Line(g.X, g.Y, e.X, e.Y), ArtPen.Line(g.X, g.Y + 7, e.X, e.Y + 4)));
        back.Add(ArtPen.D("ink-thin", ArtPen.Line(g.X, g.Y - 3, e.X, e.Y - 2), ArtPen.Line(e.X, e.Y - 2, e.X, e.Y + 4)));
        foreach (var f in new[] { 0.2, 0.42, 0.64, 0.84 })
            back.Add(ArtPen.D("detail", ArtPen.Line(g.X + (e.X - g.X) * f, g.Y + (e.Y - g.Y) * f - 4, g.X + (e.X - g.X) * f, g.Y + (e.Y - g.Y) * f + 6)));

        // shrouds to chainplates, both sides
        var topCap = C(0, 7.2, 3.9); var topLow = C(0, 3.2, 3.9);
        foreach (var (cxw, cz, lowZ) in new[] { (1.25, 3.7, 3.55), (-1.25, 3.7, 3.55) })
        {
            var chain = C(cxw, 0.05, cz); var chainL = C(cxw * 1.02, 0.05, lowZ + 0.5);
            back.Add(ArtPen.D("rig-line", ToTop(chain, topCap), ToTop(chainL, topLow)));
            foreach (var (pt, to) in new[] { (chain, topCap), (chainL, topLow) })
            {
                var dx = to.X - pt.X; var dy = to.Y - pt.Y; var len = Math.Sqrt(dx * dx + dy * dy);
                var ux = dx / len; var uy = dy / len;
                var tb = (pt.X + ux * 22, pt.Y + uy * 22); var tb2 = (pt.X + ux * 46, pt.Y + uy * 46);
                back.Add(ArtPen.D("ink-thin", ArtPen.Poly(false, tb, tb2), ArtPen.Ellipse(tb.Item1, tb.Item2, 3, 3), ArtPen.Ellipse(tb2.Item1, tb2.Item2, 3, 3)));
                back.Add(ArtPen.D("ink", ArtPen.Rect(pt.X - 7, pt.Y - 3, 14, 5)));
                back.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(pt.X, pt.Y - 0.5, 1.6, 1.6)));
            }
        }

        // stanchions and guardwires
        foreach (var x in new[] { -1.25, 1.25 })
        {
            var zs = new[] { 3.1, 4.5, 5.9, 7.3, 8.7, 9.6 };
            back.Add(ArtPen.D("ink-thin", zs.Select(z => Seg(x, 0, z, x, 0.6, z)).ToArray()));
            back.Add(ArtPen.D("detail",
                ArtPen.Poly(false, zs.Select(z => C(x, 0.6, z)).ToArray()),
                ArtPen.Poly(false, zs.Select(z => C(x, 0.3, z)).ToArray())));
        }
        back.Add(ArtPen.D("ink", Pl(false, (-1.25, 0.6, 9.6), (-0.9, 0.6, 10.1), (0.9, 0.6, 10.1), (1.25, 0.6, 9.6))));

        // cockpit beyond the sprayhood
        back.Add(ArtPen.D("furniture", Pl(false, (-1.0, 0.3, 7.9), (-1.0, 0.3, 9.5), (1.0, 0.3, 9.5), (1.0, 0.3, 7.9)),
            Seg(-0.85, 0.3, 7.9, 0.85, 0.3, 7.9), Seg(-0.5, 0.05, 9.4, 0.5, 0.05, 9.4)));
        back.Add(ArtPen.D("ink-thin", Seg(0.0, 0.2, 9.4, 0.2, 1.0, 8.1)));

        // sprayhood at the after end of the cabin top
        back.Add(ArtPen.D("paper-fill", Pl(true, (-0.75, 0.55, 6.6), (0.75, 0.55, 6.6), (0.75, 1.25, 6.6), (-0.75, 1.25, 6.6))));
        back.Add(ArtPen.D("sprayhood",
            Pl(true, (-0.75, 0.55, 6.6), (0.75, 0.55, 6.6), (0.75, 1.25, 6.6), (-0.75, 1.25, 6.6)),
            Pl(false, (0.75, 1.25, 6.6), (0.7, 1.2, 7.5), (0.7, 0.55, 7.5)),
            Pl(false, (-0.75, 1.25, 6.6), (-0.7, 1.2, 7.5), (-0.7, 0.55, 7.5)),
            Pl(false, (-0.7, 1.2, 7.5), (0.7, 1.2, 7.5))));
        back.Add(ArtPen.D("detail",
            Pl(true, (-0.62, 0.78, 6.6), (0.62, 0.78, 6.6), (0.62, 1.17, 6.6), (-0.62, 1.17, 6.6)),
            Seg(-0.2, 0.78, 6.6, -0.2, 1.17, 6.6), Seg(0.2, 0.78, 6.6, 0.2, 1.17, 6.6)));

        // a ladder hooked over the rail at the gate, running down to the yard
        var r1t = C(1.3, 0.5, 4.0); var r2t = C(1.3, 0.5, 4.5);
        var r1b = C(2.9, Ground, 4.0); var r2b = C(2.9, Ground, 4.5);
        var cut = (570 - r1t.Y) / (r1b.Y - r1t.Y); var cut2 = (570 - r2t.Y) / (r2b.Y - r2t.Y);
        r1b = (r1t.X + (r1b.X - r1t.X) * cut, 570); r2b = (r2t.X + (r2b.X - r2t.X) * cut2, 570);
        back.Add(ArtPen.D("ink", ArtPen.Line(r1t.X, r1t.Y, r1b.X, r1b.Y), ArtPen.Line(r2t.X, r2t.Y, r2b.X, r2b.Y)));
        for (var i = 1; i <= 8; i++)
        {
            var f = i / 9.0;
            back.Add(ArtPen.D("ink-thin", ArtPen.Line(r1t.X + (r1b.X - r1t.X) * f, r1t.Y + (r1b.Y - r1t.Y) * f, r2t.X + (r2b.X - r2t.X) * f, r2t.Y + (r2b.Y - r2t.Y) * f)));
        }

        // the "Standing rig" spot: on the starboard cap shroud, a hand's reach above the turnbuckle
        var ch = C(1.25, 0.05, 3.7);
        var s = 0.42;
        var spot = (ch.X + (topCap.X - ch.X) * s, ch.Y + (topCap.Y - ch.Y) * s);
        var spots = ImmutableDictionary<string, MarkerSpot>.Empty
            .Add("Standing rig", new MarkerSpot(spot.Item1, spot.Item2, "Shrouds", spot.Item1 + 12, spot.Item2 + 4));

        var fallback = ImmutableArray.Create<(double X, double Y)>(
            (80, 120), (80, 200), (900, 120), (900, 200), (700, 450), (200, 330), (620, 130), (900, 330));

        return new CloseUpScene("On deck", "ON DECK  /  LOOKING AFT", back.ToImmutableArray(),
            ImmutableArray<CoverShape>.Empty, ImmutableArray<DecorPath>.Empty, spots, fallback);
    }
}
