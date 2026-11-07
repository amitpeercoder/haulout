using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Haulout.Web.Art.CloseUps;

/// <summary>Small path-building helpers shared by the Saloon, Forepeak, On deck and Beside the hull drawings. Pure data, invariant culture.</summary>
internal static class ArtPen
{
    public static string N(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

    public static string Line(double x1, double y1, double x2, double y2) => $"M{N(x1)},{N(y1)} L{N(x2)},{N(y2)}";

    public static string Poly(bool close, params (double X, double Y)[] p)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < p.Length; i++) sb.Append(i == 0 ? 'M' : 'L').Append(N(p[i].X)).Append(',').Append(N(p[i].Y)).Append(' ');
        if (close) sb.Append('Z');
        return sb.ToString().TrimEnd();
    }

    public static string Rect(double x, double y, double w, double h) => Poly(true, (x, y), (x + w, y), (x + w, y + h), (x, y + h));

    public static string Ellipse(double cx, double cy, double rx, double ry) =>
        $"M{N(cx - rx)},{N(cy)} a{N(rx)},{N(ry)} 0 1,0 {N(2 * rx)},0 a{N(rx)},{N(ry)} 0 1,0 {N(-2 * rx)},0 Z";

    /// <summary>A squarish rounded blob (superellipse, exponent n) rotated by deg degrees: sail bags, cushions.</summary>
    public static string Blob(double cx, double cy, double w, double h, double deg, double n = 3.5)
    {
        var pts = new List<(double, double)>();
        var rot = deg * Math.PI / 180;
        for (var i = 0; i < 40; i++)
        {
            var a = 2 * Math.PI * i / 40;
            var c = Math.Cos(a); var s = Math.Sin(a);
            var x = w / 2 * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n);
            var y = h / 2 * Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / n);
            pts.Add((cx + x * Math.Cos(rot) - y * Math.Sin(rot), cy + x * Math.Sin(rot) + y * Math.Cos(rot)));
        }
        return Poly(true, pts.ToArray());
    }

    public static DecorPath D(string cls, params string[] d) => new(cls, string.Join(" ", d));
}

/// <summary>
/// A box seen down its length: near rectangle at depth t = 0, far rectangle at t = 1, linear in between.
/// u runs left to right and v top to bottom across the section.
/// </summary>
internal sealed class Tunnel(double nx0, double ny0, double nx1, double ny1, double fx0, double fy0, double fx1, double fy1)
{
    public (double X, double Y) P(double u, double v, double t)
    {
        var x0 = nx0 + (fx0 - nx0) * t; var x1 = nx1 + (fx1 - nx1) * t;
        var y0 = ny0 + (fy0 - ny0) * t; var y1 = ny1 + (fy1 - ny1) * t;
        return (x0 + u * (x1 - x0), y0 + v * (y1 - y0));
    }

    public string L(double u0, double v0, double t0, double u1, double v1, double t1)
    {
        var a = P(u0, v0, t0); var b = P(u1, v1, t1);
        return ArtPen.Line(a.X, a.Y, b.X, b.Y);
    }

    public string Quad(double u0, double v0, double t0, double u1, double v1, double t1, double u2, double v2, double t2, double u3, double v3, double t3) =>
        ArtPen.Poly(true, P(u0, v0, t0), P(u1, v1, t1), P(u2, v2, t2), P(u3, v3, t3));

    /// <summary>A rectangle in a plane of constant u (a side wall): t and v ranges.</summary>
    public string WallRect(double u, double t0, double t1, double v0, double v1) => Quad(u, v0, t0, u, v0, t1, u, v1, t1, u, v1, t0);
}

/// <summary>Close-up: Saloon, looking forward down the cabin. Sole with floorboards, a settee each side, folded table, portlights.</summary>
public static class SaloonCloseUp
{
    private static readonly Tunnel T = new(40, 60, 960, 575, 400, 190, 600, 330);
    private const double B0 = 0.094, B1 = 0.747, BMid = 0.42; // floorboard opening spans these depths, u 0.2 to 0.8
    private const double SeatV = 0.62, SettU = 0.17;

    private static (double X, double Y) P(double u, double v, double t) => T.P(u, v, t);

    private static string OvalPoly(bool right, double tc, double tr, double vc, double vr)
    {
        var pts = new List<(double, double)>();
        for (var i = 0; i < 24; i++)
        {
            var a = 2 * Math.PI * i / 24;
            pts.Add(P(right ? 1 : 0, vc + vr * Math.Sin(a), tc + tr * Math.Cos(a)));
        }
        return ArtPen.Poly(true, pts.ToArray());
    }

    public static readonly CloseUpScene Scene = Build();

    private static CloseUpScene Build()
    {
        var back = new List<DecorPath>();

        // cabin shell: corner edges, far bulkhead, beams
        back.Add(ArtPen.D("hull-line",
            T.L(0, 0, 0, 0, 0, 1), T.L(1, 0, 0, 1, 0, 1), 
            ArtPen.Poly(true, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1))));
        foreach (var t in new[] { 0.28, 0.58 })
            back.Add(ArtPen.D("furniture", ArtPen.Poly(false, P(0, 0.1, t), P(0, 0, t), P(1, 0, t), P(1, 0.1, t))));
        back.Add(ArtPen.D("detail", T.L(0.2, 0, 0.04, 0.2, 0, 1), T.L(0.8, 0, 0.04, 0.8, 0, 1))); // ceiling handrails

        // bulkhead door to the forepeak, V-berth hinted beyond
        back.Add(ArtPen.D("tone-fill", ArtPen.Poly(true, P(0.37, 0.24, 1), P(0.63, 0.24, 1), P(0.63, 1, 1), P(0.37, 1, 1))));
        back.Add(ArtPen.D("hull-line", ArtPen.Poly(true, P(0.36, 0.2, 1), P(0.64, 0.2, 1), P(0.64, 1, 1), P(0.36, 1, 1))));
        back.Add(ArtPen.D("dash", ArtPen.Poly(false, P(0.37, 0.7, 1), P(0.45, 0.62, 1), P(0.55, 0.62, 1), P(0.63, 0.7, 1))));
        back.Add(ArtPen.D("furniture", T.L(0.07, 0.1, 1, 0.07, 1, 1), T.L(0.93, 0.1, 1, 0.93, 1, 1), T.L(0.2, 0.1, 1, 0.2, 1, 1), T.L(0.8, 0.1, 1, 0.8, 1, 1)));

        // sole: fixed strips and forward boards
        back.Add(ArtPen.D("furniture",
            T.L(SettU, 1, 0, SettU, 1, 1), T.L(1 - SettU, 1, 0, 1 - SettU, 1, 1),
            T.L(SettU, 1, B0, 1 - SettU, 1, B0), T.L(SettU, 1, B1, 1 - SettU, 1, B1),
            T.L(SettU, 1, 0.87, 1 - SettU, 1, 0.87), T.L(SettU, 1, 0.95, 1 - SettU, 1, 0.95),
            T.L(0.5, 1, B1, 0.5, 1, 1)));

        // settees, one each side (m maps a left-side u to the right side)
        foreach (var right in new[] { false, true })
        {
            double U(double u) => right ? 1 - u : u;
            (double X, double Y) Q(double u, double v, double t) => P(U(u), v, t);
            string Ln(double u0, double v0, double t0, double u1, double v1, double t1)
            {
                var a = Q(u0, v0, t0); var b = Q(u1, v1, t1);
                return ArtPen.Line(a.X, a.Y, b.X, b.Y);
            }
            string Qd(double u0, double v0, double t0, double u1, double v1, double t1, double u2, double v2, double t2, double u3, double v3, double t3) =>
                ArtPen.Poly(true, Q(u0, v0, t0), Q(u1, v1, t1), Q(u2, v2, t2), Q(u3, v3, t3));

            back.Add(ArtPen.D("wood-fill", Qd(SettU, SeatV, 0, SettU, SeatV, 1, SettU, 1, 1, SettU, 1, 0)));
            back.Add(ArtPen.D("hull-line",
                Ln(0, SeatV, 0, 0, SeatV, 1), Ln(SettU, SeatV, 0, SettU, SeatV, 1),
                Ln(0.05, 0.42, 0, 0.05, 0.42, 1), Ln(0.05, SeatV, 0, 0.05, SeatV, 1)));
            foreach (var t in new[] { 0.34, 0.67 })
                back.Add(ArtPen.D("furniture", Ln(0.05, 0.42, t, 0.05, SeatV, t), Ln(0.05, SeatV, t, SettU, SeatV, t)));
            // locker fronts under the seat
            foreach (var (a, b) in new[] { (0.1, 0.44), (0.5, 0.84) })
            {
                back.Add(ArtPen.D("detail", Qd(SettU, 0.69, a, SettU, 0.69, b, SettU, 0.93, b, SettU, 0.93, a)));
                back.Add(ArtPen.D("detail", Ln(SettU, 0.76, (a + b) / 2 - 0.02, SettU, 0.76, (a + b) / 2 + 0.02)));
            }
            // portlights in the hull side, two each
            foreach (var (a, b) in right ? new[] { (0.18, 0.32), (0.72, 0.84) } : new[] { (0.2, 0.34), (0.55, 0.67) })
            {
                string Oval(double k) => OvalPoly(right, (a + b) / 2, (b - a) / 2 * k, 0.285, 0.085 * k);
                back.Add(ArtPen.D("paper-fill", Oval(1)));
                back.Add(ArtPen.D("hull-line", Oval(1)));
                back.Add(ArtPen.D("detail", Oval(0.72)));
                var g0 = Q(0, 0.24, a + (b - a) * 0.3); var g1 = Q(0, 0.29, a + (b - a) * 0.45);
                back.Add(ArtPen.D("detail", ArtPen.Line(g0.X, g0.Y, g1.X, g1.Y)));
            }
        }

        // folded table, hung against the starboard hull side above the settee
        {
            Func<double, double, double, (double X, double Y)> R = (u, v, t) => P(1 - u, v, t);
            back.Add(ArtPen.D("wood-fill", ArtPen.Poly(true, R(0, 0.12, 0.4), R(0, 0.12, 0.62), R(0, 0.39, 0.62), R(0, 0.39, 0.4))));
            back.Add(ArtPen.D("hull-line", ArtPen.Poly(true, R(0, 0.12, 0.4), R(0, 0.12, 0.62), R(0, 0.39, 0.62), R(0, 0.39, 0.4))));
            var a = R(0, 0.16, 0.4); var b = R(0, 0.16, 0.62);
            back.Add(ArtPen.D("ink-thin", ArtPen.Line(a.X, a.Y, b.X, b.Y)));
            var g1 = R(0, 0.26, 0.4); var g2 = R(0, 0.26, 0.62);
            back.Add(ArtPen.D("detail", ArtPen.Line(g1.X, g1.Y, g2.X, g2.Y)));
            var h = R(0, 0.34, 0.51);
            back.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(h.X, h.Y, 3, 3)));
        }

        // cover: the floorboards
        string Hole() => ArtPen.Poly(true, P(0.2, 1, B0), P(0.8, 1, B0), P(0.8, 1, B1), P(0.2, 1, B1));
        var fingerHole = P(0.5, 1, 0.2);
        var onList = ImmutableArray.CreateBuilder<DecorPath>();
        onList.Add(ArtPen.D("wood-fill", Hole()));
        onList.Add(ArtPen.D("ink", Hole()));
        onList.Add(ArtPen.D("ink-thin", T.L(0.2, 1, BMid, 0.8, 1, BMid)));
        onList.Add(ArtPen.D("detail",
            T.L(0.33, 1, B0 + 0.02, 0.33, 1, BMid - 0.02), T.L(0.5, 1, B0 + 0.1, 0.5, 1, BMid - 0.02), T.L(0.67, 1, B0 + 0.02, 0.67, 1, BMid - 0.02),
            T.L(0.27, 1, BMid + 0.02, 0.27, 1, B1 - 0.02), T.L(0.42, 1, BMid + 0.02, 0.42, 1, B1 - 0.02), T.L(0.58, 1, BMid + 0.02, 0.58, 1, B1 - 0.02), T.L(0.73, 1, BMid + 0.02, 0.73, 1, B1 - 0.02)));
        onList.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(fingerHole.X, fingerHole.Y, 24, 5.5)));
        onList.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(fingerHole.X, fingerHole.Y + 1.5, 20, 3.5)));

        var off = ImmutableArray.CreateBuilder<DecorPath>();
        off.Add(ArtPen.D("paper-fill", Hole()));
        // pit: far wall, sloping hull sides, floor
        const string farWall = "M385,392 L615,392 L545,455 L455,455 Z";
        const string leftWall = "M244,552 L385,392 L455,455 L433,552 Z";
        const string rightWall = "M756,552 L615,392 L545,455 L567,552 Z";
        const string pitFloor = "M455,455 L545,455 L567,552 L433,552 Z";
        off.Add(ArtPen.D("tone-fill", farWall, leftWall, rightWall));
        off.Add(ArtPen.D("ink-thin", farWall, leftWall, rightWall, pitFloor));
        off.Add(ArtPen.D("ink", Hole()));
        // frames across the hull sides
        foreach (var s in new[] { 0.3, 0.62 })
        {
            off.Add(ArtPen.D("detail",
                ArtPen.Line(244 + 141 * s, 552 - 160 * s, 433 + 22 * s, 552 - 97 * s),
                ArtPen.Line(756 - 141 * s, 552 - 160 * s, 567 - 22 * s, 552 - 97 * s)));
        }
        // limber hole in the far wall
        off.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(500, 429, 20, 8)));
        // keelson and keel bolts
        off.Add(ArtPen.D("ink-thin", ArtPen.Line(492, 455, 488, 552), ArtPen.Line(508, 455, 512, 552)));
        foreach (var (y, r) in new[] { (470.0, 5.5), (498.0, 7.5), (532.0, 10.0) })
        {
            off.Add(ArtPen.D("paper-fill", ArtPen.Ellipse(500, y, r, r * 0.6)));
            off.Add(ArtPen.D("ink", ArtPen.Ellipse(500, y, r, r * 0.6)));
            off.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(500, y, r * 0.5, r * 0.3)));
        }
        // a little water in the low part of the bilge
        off.Add(ArtPen.D("water-line", ArtPen.Ellipse(462, 522, 30, 8), ArtPen.Line(444, 522, 470, 522), ArtPen.Line(450, 527, 478, 527), ArtPen.Line(470, 517, 484, 517)));
        off.Add(ArtPen.D("water-line", ArtPen.Ellipse(533, 488, 12, 3.5)));

        // seacock on a through-hull in the hull side, hose up to the strainer, hose on aft
        off.Add(ArtPen.D("paper-fill", ArtPen.Ellipse(700, 524, 30, 14)));
        off.Add(ArtPen.D("ink", ArtPen.Ellipse(700, 524, 30, 14)));
        off.Add(ArtPen.D("ink-thin", ArtPen.Ellipse(700, 524, 18, 8), ArtPen.Ellipse(700, 524, 6, 2.8)));
        off.Add(ArtPen.D("paper-fill", "M674,505 L694,505 L694,522 L674,522 Z"));
        off.Add(ArtPen.D("ink", "M674,505 L674,521 M694,505 L694,521"));                         // the through-hull barrel
        off.Add(ArtPen.D("paper-fill", ArtPen.Blob(684, 498, 30, 26, 0, 4)));
        off.Add(ArtPen.D("ink", ArtPen.Blob(684, 498, 30, 26, 0, 4), ArtPen.Ellipse(684, 498, 5, 5)));  // valve body
        off.Add(ArtPen.D("ink", "M684,485 L684,474 L722,458"));                                  // stem and lever
        off.Add(ArtPen.D("paper-fill", ArtPen.Ellipse(727, 456, 8, 4)));
        off.Add(ArtPen.D("ink", ArtPen.Ellipse(727, 456, 8, 4)));                                // lever grip
        off.Add(ArtPen.D("ink", "M669,494 C648,490 636,474 636,452", "M669,506 C640,502 622,482 622,452")); // hose, valve to strainer
        off.Add(ArtPen.D("ink-thin", "M650,490 L650,502", "M630,468 L642,470"));                 // clamps
        // strainer: clear bowl over a body, basket inside, barbs
        off.Add(ArtPen.D("paper-fill", "M606,430 L640,430 L640,452 L606,452 Z", "M608,430 C606,406 640,406 638,430 Z"));
        off.Add(ArtPen.D("ink", "M606,430 L640,430 L640,452 L606,452 Z", "M608,430 C606,406 640,406 638,430", "M612,404 L634,404 L634,410 L612,410 Z"));
        off.Add(ArtPen.D("water-line", "M611,421 L636,421"));
        off.Add(ArtPen.D("detail", "M614,414 L614,430", "M620,412 L620,430", "M626,412 L626,430", "M632,414 L632,430"));
        off.Add(ArtPen.D("ink", "M640,438 L648,438", "M606,440 L598,440"));                    // barbs
        off.Add(ArtPen.D("ink", "M598,436 C586,436 580,452 578,478", "M598,446 C592,446 588,458 588,478")); // hose aft
        off.Add(ArtPen.D("dash", "M583,478 C583,500 576,520 570,545"));

        var covers = ImmutableArray.Create(new CoverShape(
            "Saloon floorboards", Hole(), onList.ToImmutable(), off.ToImmutable(), "Floorboards", 250, 569));

        var spots = ImmutableDictionary<string, MarkerSpot>.Empty
            .Add("Raw-water seacock", new MarkerSpot(684, 498, "Seacock", 774, 505, "start", 764, 503))
            .Add("Raw-water strainer", new MarkerSpot(623, 438, "Strainer", 662, 420, "start", 656, 418))
            .Add("Battery switch", new MarkerSpot(60, 120, "Battery switch (aft, below the hatch)", 80, 124, "start", 76, 120)); // label only

        var fallback = ImmutableArray.Create<(double X, double Y)>(
            (120, 330), (120, 470), (880, 330), (880, 470), (300, 290), (700, 290), (300, 520), (720, 330));

        return new CloseUpScene("Saloon", "SALOON  /  LOOKING FORWARD", back.ToImmutableArray(), covers, ImmutableArray<DecorPath>.Empty, spots, fallback);
    }
}
