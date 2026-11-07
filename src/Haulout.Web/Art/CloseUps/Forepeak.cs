using System.Collections.Immutable;

namespace Haulout.Web.Art.CloseUps;

/// <summary>Close-up: Forepeak, looking forward from the saloon door into the V-berth. Mostly empty in the slice.</summary>
public static class ForepeakCloseUp
{
    private static readonly Tunnel T = new(30, 60, 970, 575, 410, 170, 590, 300);
    private const double CushionTop = 0.62, Shelf = 0.7;

    private static (double X, double Y) P(double u, double v, double t) => T.P(u, v, t);
    private static double Inner(double t) => 0.30 + 0.17 * t;
    /// <summary>How big things look at depth t, relative to the near end.</summary>
    private static double Scale(double t) => (1 - t) + t * 0.196;

    public static readonly CloseUpScene Scene = Build();

    private static CloseUpScene Build()
    {
        var back = new List<DecorPath>();

        // overhead: deck beams and the deck hatch
        foreach (var t in new[] { 0.22, 0.5, 0.78 })
            back.Add(ArtPen.D("furniture", ArtPen.Poly(false, P(0, 0.09, t), P(0, 0, t), P(1, 0, t), P(1, 0.09, t))));
        var hatch = ArtPen.Poly(true, P(0.34, 0, 0.36), P(0.66, 0, 0.36), P(0.66, 0, 0.64), P(0.34, 0, 0.64));
        back.Add(ArtPen.D("paper-fill", hatch));
        back.Add(ArtPen.D("ink", hatch));
        back.Add(ArtPen.D("ink-thin", ArtPen.Poly(true, P(0.37, 0, 0.4), P(0.63, 0, 0.4), P(0.63, 0, 0.6), P(0.37, 0, 0.6))));
        back.Add(ArtPen.D("detail", T.L(0.4, 0, 0.5, 0.45, 0, 0.42), T.L(0.47, 0, 0.58, 0.58, 0, 0.42)));

        // hull: deck edge, topsides to the shelf, the turn of the bilge, floor
        foreach (var right in new[] { false, true })
        {
            double U(double u) => right ? 1 - u : u;
            (double X, double Y) Q(double u, double v, double t) => P(U(u), v, t);
            string Ln(double u0, double v0, double t0, double u1, double v1, double t1)
            {
                var a = Q(u0, v0, t0); var b = Q(u1, v1, t1);
                return ArtPen.Line(a.X, a.Y, b.X, b.Y);
            }
            string Qd(params (double u, double v, double t)[] pts) => ArtPen.Poly(true, pts.Select(p => Q(p.u, p.v, p.t)).ToArray());

            back.Add(ArtPen.D("hull-line", Ln(0, 0, 0, 0, 0, 1), Ln(0, Shelf, 0, 0, Shelf, 1), Ln(0.2, 1, 0, 0.2, 1, 1), Ln(0, Shelf, 1, 0.2, 1, 1)));
            foreach (var t in new[] { 0.3, 0.6, 0.85 })
                back.Add(ArtPen.D("furniture", Ln(0, 0.08, t, 0, 0, t), Ln(0, Shelf, t, 0.2, 1, t)));

            // locker fronts under the cushion
            back.Add(ArtPen.D("wood-fill", Qd((Inner(0.03), Shelf, 0.03), (Inner(0.97), Shelf, 0.97), (Inner(0.97), 1, 0.97), (Inner(0.03), 1, 0.03))));
            back.Add(ArtPen.D("hull-line", Qd((Inner(0.03), Shelf, 0.03), (Inner(0.97), Shelf, 0.97), (Inner(0.97), 1, 0.97), (Inner(0.03), 1, 0.03))));
            foreach (var (a, b) in new[] { (0.1, 0.45), (0.55, 0.9) })
            {
                back.Add(ArtPen.D("detail", Qd((Inner(a), 0.78, a), (Inner(b), 0.78, b), (Inner(b), 0.95, b), (Inner(a), 0.95, a))));
                var m = (a + b) / 2;
                back.Add(ArtPen.D("detail", Ln(Inner(m), 0.84, m, Inner(m + 0.04), 0.84, m + 0.04)));
            }

            // cushion: top face, thickness, a seam
            var top = Qd((0, CushionTop, 0.03), (Inner(0.03), CushionTop, 0.03), (Inner(0.97), CushionTop, 0.97), (0, CushionTop, 0.97));
            back.Add(ArtPen.D("paper-fill", top));
            back.Add(ArtPen.D("ink", top));
            back.Add(ArtPen.D("ink-thin", Ln(Inner(0.03), CushionTop, 0.03, Inner(0.03), Shelf, 0.03), Ln(Inner(0.97), CushionTop, 0.97, Inner(0.97), Shelf, 0.97),
                Ln(Inner(0.03), Shelf, 0.03, Inner(0.97), Shelf, 0.97)));
            back.Add(ArtPen.D("furniture", Ln(0, CushionTop, 0.45, Inner(0.45), CushionTop, 0.45)));
        }

        // bulkhead at the forward end: planks, the chain locker door
        const string bulkhead = "M410,170 L590,170 L590,300 L410,300 Z";
        back.Add(ArtPen.D("paper-fill", bulkhead));
        back.Add(ArtPen.D("hull-line", bulkhead));
        back.Add(ArtPen.D("furniture", T.L(0.16, 0, 1, 0.16, 1, 1), T.L(0.3, 0, 1, 0.3, 1, 1), T.L(0.7, 0, 1, 0.7, 1, 1), T.L(0.84, 0, 1, 0.84, 1, 1)));
        var door = ArtPen.Poly(true, P(0.36, 0.38, 1), P(0.64, 0.38, 1), P(0.64, 0.9, 1), P(0.36, 0.9, 1));
        back.Add(ArtPen.D("tone-fill", door));
        back.Add(ArtPen.D("ink", door));
        back.Add(ArtPen.D("ink-thin", ArtPen.Poly(true, P(0.4, 0.44, 1), P(0.6, 0.44, 1), P(0.6, 0.84, 1), P(0.4, 0.84, 1))));
        var knob = P(0.55, 0.64, 1);
        back.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(knob.X, knob.Y, 2.5, 2.5)));
        back.Add(ArtPen.D("detail", T.L(0.36, 0.48, 1, 0.34, 0.48, 1), T.L(0.36, 0.8, 1, 0.34, 0.8, 1), T.L(0.64, 0.48, 1, 0.66, 0.48, 1), T.L(0.64, 0.8, 1, 0.66, 0.8, 1)));

        // floor between the berths
        back.Add(ArtPen.D("furniture", T.L(0.2, 1, 0.12, 0.8, 1, 0.12), T.L(0.2, 1, 0.4, 0.8, 1, 0.4), T.L(0.2, 1, 0.7, 0.8, 1, 0.7), T.L(0.5, 1, 0.05, 0.5, 1, 1)));

        // sail bags on the starboard cushion
        foreach (var (u, t, w, h, deg) in new[] { (0.62, 0.16, 210.0, 76.0, -5.0), (0.76, 0.52, 190.0, 66.0, 4.0) })
        {
            var s = Scale(t); var c = P(u, CushionTop, t);
            var bw = w * s; var bh = h * s; var cy = c.Y - bh * 0.4;
            back.Add(ArtPen.D("paper-fill", ArtPen.Blob(c.X, cy, bw, bh, deg)));
            back.Add(ArtPen.D("ink", ArtPen.Blob(c.X, cy, bw, bh, deg)));
            back.Add(ArtPen.D("detail", ArtPen.Line(c.X - bw * 0.3, cy - bh * 0.3, c.X - bw * 0.3, cy + bh * 0.3), ArtPen.Line(c.X + bw * 0.12, cy - bh * 0.35, c.X + bw * 0.12, cy + bh * 0.35)));
            back.Add(ArtPen.D("ink-thin", ArtPen.Line(c.X + bw * 0.38, cy, c.X + bw * 0.48, cy + bh * 0.45)));
        }
        // a folded blanket on the port cushion
        var bl = P(0.1, CushionTop, 0.4); var bs = Scale(0.4);
        back.Add(ArtPen.D("paper-fill", ArtPen.Blob(bl.X, bl.Y - 12 * bs, 150 * bs, 34 * bs, 2, 4)));
        back.Add(ArtPen.D("ink-thin", ArtPen.Blob(bl.X, bl.Y - 12 * bs, 150 * bs, 34 * bs, 2, 4), ArtPen.Line(bl.X - 60 * bs, bl.Y - 12 * bs, bl.X + 60 * bs, bl.Y - 10 * bs)));

        // coil of old anchor warp on the floor, tail trailing aft
        var cc = P(0.5, 1, 0.45); var cs = Scale(0.45);
        for (var i = 0; i < 5; i++)
            back.Add(ArtPen.D(i == 0 ? "ink" : "ink-thin", ArtPen.Ellipse(cc.X, cc.Y - (14 + i * 3) * cs, (110 - i * 13) * cs, (30 - i * 3.5) * cs)));
        back.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(cc.X, cc.Y - 6 * cs, 112 * cs, 31 * cs)));
        back.Add(ArtPen.D("ink",
            $"M{ArtPen.N(cc.X - 110 * cs)},{ArtPen.N(cc.Y - 14 * cs)} C{ArtPen.N(cc.X - 150 * cs)},{ArtPen.N(cc.Y - 4 * cs)} {ArtPen.N(cc.X - 190 * cs)},{ArtPen.N(cc.Y + 12 * cs)} {ArtPen.N(cc.X - 240 * cs)},{ArtPen.N(cc.Y + 8 * cs)}"));
        back.Add(ArtPen.D("ink-thin", ArtPen.Line(cc.X - 240 * cs, cc.Y + 5 * cs, cc.X - 252 * cs, cc.Y + 5 * cs)));

        var fallback = ImmutableArray.Create<(double X, double Y)>(
            (130, 250), (130, 350), (870, 250), (870, 350), (300, 200), (700, 200), (300, 520), (700, 520));

        return new CloseUpScene("Forepeak", "FOREPEAK  /  LOOKING FORWARD", back.ToImmutableArray(),
            ImmutableArray<CoverShape>.Empty, ImmutableArray<DecorPath>.Empty, ImmutableDictionary<string, MarkerSpot>.Empty, fallback);
    }
}
