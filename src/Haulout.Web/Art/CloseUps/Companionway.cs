using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Haulout.Web.Art.CloseUps;

/// <summary>
/// Close-up: Companionway. Standing in the hatch looking down at the front of the engine. With the step unit in, a
/// stack of treads and risers hides the engine; with it out it leans aside and the engine front is revealed: belt,
/// alternator, raw-water pump, heat exchanger, thermostat, injectors, lift pump, secondary filter, oil filter, and the battery switch on the bulkhead above the engine.
/// </summary>
public static class CompanionwayCloseUp
{
    public static readonly CloseUpScene Scene = Build();

    // ---- helpers for building path strings -------------------------------------------------------------------
    static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static string Poly(params double[] p)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 1 < p.Length; i += 2) sb.Append(i == 0 ? "M" : "L").Append(N(p[i])).Append(' ').Append(N(p[i + 1])).Append(' ');
        return sb.Append('Z').ToString();
    }

    static string Open(params double[] p) => Poly(p).TrimEnd('Z', ' ');

    static string Ln(double x1, double y1, double x2, double y2) => $"M{N(x1)} {N(y1)}L{N(x2)} {N(y2)}";

    static string Rect(double x, double y, double w, double h) => Poly(x, y, x + w, y, x + w, y + h, x, y + h);

    static string RRect(double x, double y, double w, double h, double r)
    {
        string a(double dx, double dy) => $"A{N(r)} {N(r)} 0 0 1 {N(dx)} {N(dy)}";
        return $"M{N(x + r)} {N(y)}H{N(x + w - r)}{a(x + w, y + r)}V{N(y + h - r)}{a(x + w - r, y + h)}H{N(x + r)}{a(x, y + h - r)}V{N(y + r)}{a(x + r, y)}Z";
    }

    static string Circle(double cx, double cy, double r) =>
        $"M{N(cx - r)} {N(cy)}A{N(r)} {N(r)} 0 1 0 {N(cx + r)} {N(cy)}A{N(r)} {N(r)} 0 1 0 {N(cx - r)} {N(cy)}Z";

    static DecorPath P(string cls, params string[] d) => new(cls, string.Join(" ", d));

    static ImmutableArray<DecorPath> A(params DecorPath[] p) => ImmutableArray.Create(p);

    static MarkerSpot Sp(double x, double y, string label, double lx, double ly, string anchor = "start")
    {
        var (ex, ey) = anchor switch
        {
            "end" => (lx + 3, ly - 4),
            "middle" => (lx, ly < y ? ly + 4 : ly - 14),
            _ => (lx - 3, ly - 4),
        };
        return new MarkerSpot(x, y, label, lx, ly, anchor, ex, ey);
    }

    // ---- the drawing -------------------------------------------------------------------------------------------
    static CloseUpScene Build()
    {
        // ---- backdrop: the engine bay seen from the companionway -------------------------------------------------
        var backdrop = new List<DecorPath>
        {
            P("hull-line", Rect(180, 45, 720, 25), Ln(200, 70, 200, 566), Ln(880, 70, 880, 566), Ln(200, 566, 880, 566)),
            P("hatch-fill", Rect(201, 71, 24, 494), Rect(856, 71, 23, 494)),
            P("detail", Ln(225, 71, 225, 565), Ln(856, 71, 856, 565)),
        };

        // ---- the step unit, standing: five treads with solid risers, hiding the engine ----------------------------
        var stepsIn = new List<DecorPath>
        {
            P("paper-fill", Rect(212, 72, 656, 490)),
            P("wood-fill", Rect(218, 80, 34, 472), Rect(828, 80, 34, 472)),
            P("hull-line", Rect(218, 80, 34, 472), Rect(828, 80, 34, 472)),
        };
        for (var i = 0; i < 5; i++)
        {
            double y0 = 90 + i * 92;
            stepsIn.Add(P("paper-fill", Rect(240, y0, 600, 24)));
            stepsIn.Add(P("wood-fill", Rect(240, y0, 600, 24), Rect(254, y0 + 24, 572, 68)));
            stepsIn.Add(P("ink", Rect(240, y0, 600, 24)));
            stepsIn.Add(P("ink-thin", Rect(254, y0 + 24, 572, 68)));
            stepsIn.Add(P("wood-line", Ln(270, y0 + 8, 810, y0 + 8), Ln(270, y0 + 16, 810, y0 + 16)));
            stepsIn.Add(P("detail", Ln(300, y0 + 58, 780, y0 + 58)));
        }

        // ---- the step unit, leaning aside in the bottom-left corner (same drawing, scaled and tilted) --------------
        const double sx = 0.26, sy = 0.34, rot = -0.12, ox = 38, oy = 566;
        (double, double) T(double u, double v)
        {
            double a = (u - 212) * sx, b = (v - 562) * sy;
            return (ox + a * Math.Cos(rot) - b * Math.Sin(rot), oy + a * Math.Sin(rot) + b * Math.Cos(rot));
        }
        string TPoly(params double[] p)
        {
            var q = new double[p.Length];
            for (var i = 0; i < p.Length; i += 2) (q[i], q[i + 1]) = T(p[i], p[i + 1]);
            return Poly(q);
        }
        string TLn(double u1, double v1, double u2, double v2)
        {
            var (x1, y1) = T(u1, v1); var (x2, y2) = T(u2, v2);
            return Ln(x1, y1, x2, y2);
        }
        var unit = TPoly(212, 72, 868, 72, 868, 562, 212, 562);
        var stepsOut = new List<DecorPath>
        {
            P("paper-fill", unit),
            P("wood-fill", unit),
            P("hull-line", unit),
        };
        for (var i = 0; i < 5; i++)
        {
            double y0 = 90 + i * 92;
            stepsOut.Add(P("ink", TLn(240, y0, 840, y0), TLn(240, y0 + 24, 840, y0 + 24)));
            stepsOut.Add(P("ink-thin", TLn(254, y0 + 58, 826, y0 + 58)));
        }
        // a short prop line where it rests on the bay wall
        stepsOut.Add(P("detail", TLn(212, 72, 212, 40)));

        // ---- what the steps hid: the engine seen from the front ---------------------------------------------------
        var (b1x, b1y, b2x, b2y) = BeltTangent(310, 235, 18, 560, 495, 28);
        var e = new List<DecorPath>();
        // starter and its solenoid sit low on the far side, partly behind the block
        e.Add(P("paper-fill", RRect(335, 408, 150, 44, 8), RRect(385, 386, 80, 22, 4), Rect(312, 420, 23, 20)));
        e.Add(P("ink", RRect(335, 408, 150, 44, 8), RRect(385, 386, 80, 22, 4)));
        e.Add(P("ink-thin", Rect(312, 420, 23, 20), Circle(398, 386, 3), Ln(398, 383, 398, 372)));
        // block, front gear cover, sump, feet
        e.Add(P("paper-fill", RRect(430, 256, 260, 215, 4)));
        e.Add(P("ink", RRect(430, 256, 260, 215, 4)));
        e.Add(P("ink-thin", RRect(448, 274, 224, 180, 6), Circle(460, 286, 2.5), Circle(660, 286, 2.5), Circle(460, 442, 2.5), Circle(660, 442, 2.5), Circle(560, 282, 2.5)));
        e.Add(P("paper-fill", Poly(455, 471, 665, 471, 645, 520, 475, 520)));
        e.Add(P("ink", Poly(455, 471, 665, 471, 645, 520, 475, 520)));
        e.Add(P("ink-thin", Rect(425, 510, 52, 12), Rect(643, 510, 52, 12)));
        // head, rocker cover, filler cap, injectors
        e.Add(P("paper-fill", RRect(430, 176, 260, 80, 4), RRect(480, 125, 160, 50, 6)));
        e.Add(P("ink", RRect(430, 176, 260, 80, 4), RRect(480, 125, 160, 50, 6)));
        e.Add(P("ink-thin", Ln(446, 216, 674, 216), Circle(560, 150, 12), Ln(552, 150, 568, 150), Ln(560, 142, 560, 158)));
        e.Add(P("paper-fill", RRect(448, 130, 16, 46, 2), RRect(656, 130, 16, 46, 2)));
        e.Add(P("ink", RRect(448, 130, 16, 46, 2), RRect(656, 130, 16, 46, 2)));
        e.Add(P("ink-thin", Rect(445, 160, 22, 8), Rect(653, 160, 22, 8), "M456 130V108Q456 98 470 98", "M664 130V108Q664 98 650 98"));
        // thermostat housing and heat exchanger with its coolant cap
        e.Add(P("paper-fill", RRect(690, 196, 36, 38, 3), RRect(742, 160, 126, 82, 20), RRect(790, 142, 22, 20, 3)));
        e.Add(P("ink", RRect(690, 196, 36, 38, 3), RRect(742, 160, 126, 82, 20), RRect(790, 142, 22, 20, 3)));
        e.Add(P("ink-thin", Ln(702, 196, 702, 234), Ln(768, 162, 768, 240), Ln(842, 162, 842, 240), Ln(726, 208, 742, 208), Ln(726, 224, 742, 224)));
        // raw-water pump with its impeller cover, hoses
        e.Add(P("water-line", $"M755 419H794V242", $"M755 431H806V242", "M706 463Q706 510 770 548", "M724 463Q724 520 790 548"));
        e.Add(P("paper-fill", Circle(715, 425, 40)));
        e.Add(P("ink", Circle(715, 425, 40), Circle(715, 425, 24)));
        var screws = new List<string> { Circle(715, 425, 5) };
        for (var k = 0; k < 6; k++)
            screws.Add(Circle(715 + 17 * Math.Cos(k * Math.PI / 3), 425 + 17 * Math.Sin(k * Math.PI / 3), 2));
        e.Add(P("ink-thin", screws.ToArray()));
        // crank pulley, alternator and the belt between them
        e.Add(P("paper-fill", Circle(560, 495, 28), Circle(310, 235, 42)));
        e.Add(P("ink", Circle(560, 495, 28), Circle(310, 235, 42), Circle(310, 235, 18)));
        e.Add(P("ink-thin", Circle(560, 495, 12), Circle(560, 495, 4), Circle(310, 235, 34), Circle(310, 235, 6),
            Ln(340, 262, 430, 300), "M310 193Q380 168 432 204"));
        e.Add(P("ink", Ln(b1x.Item1, b1x.Item2, b2x.Item1, b2x.Item2), Ln(b1y.Item1, b1y.Item2, b2y.Item1, b2y.Item2)));

        var covers = ImmutableArray.Create(new CoverShape(
            "Companionway steps",
            Rect(212, 72, 656, 490) + " " + TPoly(212, 72, 868, 72, 868, 562, 212, 562),
            A(stepsIn.ToArray()), A(stepsOut.Concat(e).ToArray()),
            "Companionway steps", 232, 556));

        var beltSpot = (b1y.Item1 + 0.5 * (b2y.Item1 - b1y.Item1), b1y.Item2 + 0.5 * (b2y.Item2 - b1y.Item2));
        var spots = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new Dictionary<string, MarkerSpot>
        {
            ["Engine block"] = Sp(640, 330, "Engine block", 893, 296),
            ["Injectors"] = MarkerSpot.Right(664, 152, "Injectors"),
            ["Engine oil"] = Sp(560, 150, "Engine oil", 560, 100, "middle"),
            ["Coolant"] = Sp(801, 152, "Coolant", 893, 128),
            ["Heat exchanger"] = Sp(850, 205, "Heat exchanger", 893, 205),
            ["Thermostat"] = Sp(708, 215, "Thermostat", 700, 264),
            ["Raw-water pump"] = Sp(715, 393, "Raw-water pump", 893, 360),
            ["Raw-water impeller"] = Sp(715, 425, "Impeller", 893, 395),
            ["Alternator"] = Sp(310, 207, "Alternator", 205, 232, "end"),
            ["Alternator belt"] = Sp(beltSpot.Item1, beltSpot.Item2, "Alternator belt", 205, 300, "end"),
            // labels only, placed for Amit's layout (2026-11-07); the drawing is redone with the web round
            ["Lift pump"] = Sp(412, 397, "Lift pump", 205, 345, "end"),
            ["Secondary fuel filter"] = Sp(460, 300, "Secondary filter", 205, 380, "end"),
            ["Oil filter"] = Sp(470, 450, "Oil filter", 205, 415, "end"),
            ["Battery switch"] = Sp(540, 95, "Battery switch", 205, 110, "end"),
        });

        return new CloseUpScene(
            "Companionway", "COMPANIONWAY  /  LOOKING DOWN AT THE ENGINE",
            A(backdrop.ToArray()), covers, ImmutableArray<DecorPath>.Empty, spots,
            ImmutableArray.Create((30.0, 70.0), (30.0, 92.0), (30.0, 114.0), (30.0, 136.0), (700.0, 520.0), (700.0, 542.0), (840.0, 500.0), (840.0, 522.0)));
    }

    /// <summary>The two outer tangent segments of a belt around circles 1 and 2 (start/end of each).</summary>
    static ((double, double), (double, double), (double, double), (double, double)) BeltTangent(double x1, double y1, double r1, double x2, double y2, double r2)
    {
        var th = Math.Atan2(y2 - y1, x2 - x1);
        var al = Math.Acos((r1 - r2) / Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)));
        (double, double) P1(double f) => (x1 + r1 * Math.Cos(f), y1 + r1 * Math.Sin(f));
        (double, double) P2(double f) => (x2 + r2 * Math.Cos(f), y2 + r2 * Math.Sin(f));
        return (P1(th + al), P1(th - al), P2(th + al), P2(th - al));
    }
}
