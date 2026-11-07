using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Haulout.Web.Art.CloseUps;

/// <summary>
/// Close-up: Quarter berth. Kneeling on the berth. Upper right: the plywood engine-side panel at your shoulder; off, it
/// shows the engine's fuel side. Lower left: the berth top with the hinged battery-box lid (flat, or folded back to show
/// two batteries with terminals and cables) and the battery switch on the face of the berth.
/// The two covers' clickable regions do not overlap.
/// </summary>
public static class QuarterBerthCloseUp
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
        // geometry shared by several parts
        const double ox = 436, oy = 72, ow = 538, oh = 322;      // the plywood panel (and the opening behind it)
        double[] lid = { 80, 432, 520, 432, 567, 516, 33, 516 }; // battery-box hatch in the berth top
        var lidD = Poly(lid);
        var lidUp = Poly(80, 432, 520, 432, 505, 372, 95, 372);   // the same lid folded back on its far hinge

        // ---- backdrop: cabin sides, the opening frame, the berth ----
        var backdrop = new List<DecorPath>
        {
            P("hull-line", "M30 410Q20 250 66 92", Rect(422, 58, 566, 350)),
                        // berth top and face
            P("wood-fill", Poly(55, 420, 565, 420, 615, 524, 5, 524)),
            P("hull-line", Poly(55, 420, 565, 420, 615, 524, 5, 524), Poly(5, 524, 615, 524, 615, 574, 5, 574)),
            // battery switch on the face of the berth, and the cable leaving it for the engine
            P("paper-fill", Rect(290, 534, 70, 36)),
            P("ink", Rect(290, 534, 70, 36)),
            P("ink-thin", Circle(325, 552, 10), Ln(325, 552, 333, 544), Ln(300, 540, 304, 544), Ln(346, 540, 350, 544), Ln(340, 566, 346, 560)),
            P("ink", "M360 563H600"),
        };

        // ---- battery-box lid ----
        var lidIn = new List<DecorPath>
        {
            P("paper-fill", lidD),
            P("wood-fill", lidD),
            P("ink", lidD),
            P("wood-line",
                Ln(80 + 440 * .2, 432, 33 + 534 * .2, 516), Ln(80 + 440 * .4, 432, 33 + 534 * .4, 516),
                Ln(80 + 440 * .6, 432, 33 + 534 * .6, 516), Ln(80 + 440 * .8, 432, 33 + 534 * .8, 516)),
            P("ink-thin", Circle(300, 478, 6), Rect(130, 433, 22, 5), Rect(450, 433, 22, 5)),
        };
        double LeftAt(double y) => 80 - 47 * (y - 432) / 84;
        double RightAt(double y) => 520 + 47 * (y - 432) / 84;
        var lidOut = new List<DecorPath>
        {
            // the hole: far wall hatched, two batteries with their terminals
            P("paper-fill", lidD),
            P("hatch-fill", Poly(80, 432, 520, 432, RightAt(468), 468, LeftAt(468), 468)),
            P("ink", lidD),
            P("ink-thin", Ln(LeftAt(468), 468, RightAt(468), 468)),
            P("paper-fill", Poly(95, 472, 265, 472, 280, 508, 88, 508), Poly(305, 472, 475, 472, 490, 508, 298, 508)),
            P("ink", Poly(95, 472, 265, 472, 280, 508, 88, 508), Poly(305, 472, 475, 472, 490, 508, 298, 508)),
            P("ink-thin", Ln(88, 508, 84, 516), Ln(280, 508, 283, 516), Ln(298, 508, 296, 516), Ln(490, 508, 493, 516),
                Circle(120, 480, 4), Circle(240, 480, 4), Circle(335, 480, 4), Circle(450, 480, 4)),
            // positive cable: out of the + post, around the left of the box and along the face to the switch
            P("ink", "M120 480H84Q58 480 58 506V556H290"),
            // negative cable: out of the - post, along the back wall, out of the hole and along under the engine
            P("ink", "M240 480V460H505Q525 460 528 438V414H880V408"),
            // the lid folded back, underside showing
            P("paper-fill", lidUp),
            P("wood-fill", lidUp),
            P("ink", lidUp),
            P("wood-line", Ln(88, 402, 512, 402), Ln(92, 387, 508, 387), Ln(84, 417, 516, 417)),
            P("ink-thin", Rect(130, 426, 22, 6), Rect(450, 426, 22, 6)),
        };

        // ---- the plywood panel: local geometry reused for the leaning copy ----
        var screwX = new List<double>(); var screwY = new List<double>();
        for (var i = 0; i < 9; i++) { screwX.Add(ox + 20 + i * 62); screwY.Add(oy + 12); screwX.Add(ox + 20 + i * 62); screwY.Add(oy + oh - 12); }
        for (var j = 1; j < 5; j++) { screwX.Add(ox + 12); screwY.Add(oy + 12 + j * 74); screwX.Add(ox + ow - 12); screwY.Add(oy + 12 + j * 74); }
        var grain = new[]
        {
            $"M{N(ox + 14)} 140Q{N(ox + 250)} 128 {N(ox + ow - 14)} 143",
            $"M{N(ox + 14)} 215Q{N(ox + 290)} 228 {N(ox + ow - 14)} 210",
            $"M{N(ox + 14)} 290Q{N(ox + 230)} 280 {N(ox + ow - 14)} 296",
            $"M{N(ox + 14)} 350Q{N(ox + 300)} 362 {N(ox + ow - 14)} 348",
        };
        var panelRect = Rect(ox, oy, ow, oh);
        var screwsIn = screwX.Select((x, i) => Circle(x, screwY[i], 3.5)).ToArray();
        var slots = screwX.Select((x, i) => Ln(x - 2.4, screwY[i] - 2.4, x + 2.4, screwY[i] + 2.4)).ToArray();
        var panelIn = new List<DecorPath>
        {
            P("paper-fill", panelRect),
            P("wood-fill", panelRect),
            P("ink", panelRect),
            P("wood-line", grain),
            P("ink-thin", screwsIn),
            P("ink-thin", slots),
        };

        // the panel set aside, leaning on the berth bottom-right (same drawing, scaled and tilted)
        const double sx = 0.5, sy = 0.42, rot = -0.06, px = 690, py = 572;
        (double, double) T(double u, double v)
        {
            double a = (u - ox) * sx, b = (v - (oy + oh)) * sy;
            return (px + a * Math.Cos(rot) - b * Math.Sin(rot), py + a * Math.Sin(rot) + b * Math.Cos(rot));
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
        var leaning = TPoly(ox, oy, ox + ow, oy, ox + ow, oy + oh, ox, oy + oh);
        var corners = new[] { (ox + 12, oy + 12), (ox + ow - 12, oy + 12), (ox + 12, oy + oh - 12), (ox + ow - 12, oy + oh - 12) }
            .Select(c => { var (x, y) = T(c.Item1, c.Item2); return Circle(x, y, 1.8); }).ToArray();

        // ---- the engine, fuel side (drawn in the opening when the panel is off) ----
        var e = new List<DecorPath>();
        e.Add(P("paper-fill", panelRect));
        e.Add(P("hatch-fill", panelRect));
        e.Add(P("dash", Rect(ox, oy, ow, oh)));
        e.Add(P("ink-thin", screwX.Select((x, i) => Circle(x, screwY[i], 2.5)).ToArray()));
        // block, sump, flywheel housing, front cover
        e.Add(P("paper-fill", RRect(500, 195, 360, 155, 4), Poly(520, 350, 840, 350, 815, 395, 545, 395), RRect(860, 215, 70, 125, 6), RRect(930, 245, 34, 65, 4), RRect(458, 235, 44, 52, 6), Rect(440, 248, 18, 26)));
        e.Add(P("ink", RRect(500, 195, 360, 155, 4), Poly(520, 350, 840, 350, 815, 395, 545, 395), RRect(860, 215, 70, 125, 6), RRect(930, 245, 34, 65, 4), RRect(458, 235, 44, 52, 6)));
        e.Add(P("ink-thin", Rect(440, 248, 18, 26), Ln(500, 345, 860, 345), Circle(520, 338, 2.5), Circle(840, 338, 2.5), Circle(520, 205, 2.5), Circle(850, 205, 2.5), Circle(895, 228, 2.5), Circle(895, 328, 2.5)));
        // head, rocker cover, injectors
        e.Add(P("paper-fill", RRect(515, 140, 315, 58, 4), RRect(600, 105, 180, 38, 6), RRect(552, 108, 16, 44, 2), RRect(800, 108, 16, 44, 2)));
        e.Add(P("ink", RRect(515, 140, 315, 58, 4), RRect(600, 105, 180, 38, 6), RRect(552, 108, 16, 44, 2), RRect(800, 108, 16, 44, 2)));
        e.Add(P("ink-thin", Rect(549, 134, 22, 7), Rect(797, 134, 22, 7), Ln(620, 124, 760, 124)));
        // glow plugs with their bus bar
        e.Add(P("paper-fill", Rect(574, 166, 12, 24), Rect(764, 166, 12, 24)));
        e.Add(P("ink", Rect(574, 166, 12, 24), Rect(764, 166, 12, 24)));
        e.Add(P("ink-thin", Ln(580, 172, 770, 172)));
        // injection pump, lift pump, secondary filter, oil filter
        e.Add(P("paper-fill", RRect(520, 215, 80, 53, 6), RRect(545, 290, 52, 42, 5), Rect(715, 227, 44, 12), RRect(718, 239, 38, 50, 6), RRect(765, 300, 48, 56, 6)));
        e.Add(P("ink", RRect(520, 215, 80, 53, 6), RRect(545, 290, 52, 42, 5), Rect(715, 227, 44, 12), RRect(718, 239, 38, 50, 6), RRect(765, 300, 48, 56, 6)));
        e.Add(P("ink-thin", Circle(555, 215, 4), Circle(580, 215, 4), Ln(597, 300, 618, 288), Circle(621, 286, 4), Ln(718, 262, 756, 262), Ln(765, 316, 813, 316)));
        // starter motor, solenoid on top, exhaust elbow and hose
        e.Add(P("paper-fill", RRect(838, 302, 118, 40, 8), RRect(862, 280, 66, 24, 4)));
        e.Add(P("ink", RRect(838, 302, 118, 40, 8), RRect(862, 280, 66, 24, 4)));
        e.Add(P("ink-thin", Circle(875, 280, 3), Ln(875, 277, 875, 262), Circle(915, 280, 3)));
        e.Add(P("paper-fill", RRect(830, 150, 40, 46, 6), "M834 150V122Q834 105 852 105H975V133H874Q866 133 866 140V150Z"));
        e.Add(P("ink", RRect(830, 150, 40, 46, 6), "M834 150V122Q834 105 852 105H975V133H874Q866 133 866 140V150Z"));
        e.Add(P("ink-thin", Ln(900, 105, 900, 133), Ln(940, 105, 940, 133)));
        // engine foot and ground stud, with the strap going down to the battery box
        e.Add(P("paper-fill", Rect(850, 350, 60, 12)));
        e.Add(P("ink-thin", Rect(850, 350, 60, 12), Circle(880, 356, 4)));
        e.Add(P("ink", "M880 362V408"));
        // fuel plumbing: return line along the top, supply into the lift pump, lift pump -> filter -> injection pump, pump -> injectors
        e.Add(P("ink", "M432 90H808M560 90V108M808 90V108", "M432 318H545"));
        e.Add(P("ink-thin", "M571 290V276H737V289", "M737 227V218H600", "M555 215V152", "M580 215V208H808V152"));

        var panelOut = new List<DecorPath>(e)
        {
            P("paper-fill", leaning),
            P("wood-fill", leaning),
            P("ink", leaning),
            P("wood-line", TLn(ox + 14, 140, ox + ow - 14, 143), TLn(ox + 14, 215, ox + ow - 14, 210), TLn(ox + 14, 290, ox + ow - 14, 296)),
            P("ink-thin", corners),
        };

        var covers = ImmutableArray.Create(
            new CoverShape("Quarter-berth panel", Rect(ox, oy, ow, 296) + " " + leaning,
                A(panelIn.ToArray()), A(panelOut.ToArray()), "Quarter-berth panel", 690, 421),
            new CoverShape("Battery-box lid", lidD + " " + lidUp,
                A(lidIn.ToArray()), A(lidOut.ToArray()), "Battery-box lid", 120, 566));

        var spots = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new Dictionary<string, MarkerSpot>
        {
            ["Battery switch"] = new MarkerSpot(325, 552, "Battery switch", 372, 556, "start", 368, 552),
            // battery box
            ["Start battery"] = MarkerSpot.Right(150, 494, "Start battery"),
            ["Positive terminal"] = Sp(120, 480, "Positive terminal", 120, 447, "middle"),
            ["Negative terminal"] = Sp(240, 480, "Negative terminal", 245, 447, "middle"),
            ["Negative cable"] = Sp(400, 460, "Negative cable", 400, 447, "middle"),
            ["Main positive cable"] = MarkerSpot.Right(58, 536, "Main positive cable"),
            // engine, left column
            ["Fuel return line"] = Sp(480, 90, "Fuel return line", 396, 94, "end"),
            ["Injectors"] = Sp(560, 130, "Injectors", 396, 134, "end"),
            ["Glow plugs"] = Sp(580, 178, "Glow plugs", 396, 174, "end"),
            ["Injection pump"] = Sp(560, 242, "Injection pump", 396, 214, "end"),
            ["Lift pump"] = Sp(571, 311, "Lift pump", 396, 290, "end"),
            ["Fuel supply line"] = Sp(480, 318, "Fuel supply line", 396, 326, "end"),
            // engine, in the block
            ["Secondary fuel filter"] = MarkerSpot.Right(737, 262, "Secondary filter"),
            ["Engine block"] = MarkerSpot.Right(640, 330, "Engine block"),
            ["Oil filter"] = Sp(789, 330, "Oil filter", 789, 384, "middle"),
            // engine, right
            ["Exhaust elbow"] = MarkerSpot.Right(850, 172, "Exhaust elbow"),
            ["Exhaust hose"] = new MarkerSpot(925, 119, "Exhaust hose", 925, 97, "middle", 925, 101),
            ["Starter solenoid"] = MarkerSpot.Right(897, 292, "Solenoid"),
            ["Starter motor"] = Sp(935, 326, "Starter motor", 905, 372),
            ["Engine ground stud"] = Sp(880, 356, "Ground stud", 905, 394),
        });

        return new CloseUpScene(
            "Quarter berth", "QUARTER BERTH  /  KNEELING ON THE BERTH",
            A(backdrop.ToArray()), covers, ImmutableArray<DecorPath>.Empty, spots,
            ImmutableArray.Create((40.0, 110.0), (40.0, 132.0), (40.0, 154.0), (40.0, 176.0), (40.0, 198.0), (40.0, 220.0), (40.0, 242.0), (40.0, 264.0)));
    }
}
