using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Haulout.Web.Art.CloseUps;

/// <summary>
/// Close-up: Cockpit. Sitting in the well looking forward: mast and shrouds above the cabin top, the bulkhead with the
/// ignition panel, the companionway washboards, a teak seat each side, the tiller. The locker lid on the starboard
/// seat folds back to show the fuel tank, shutoff valve, primary filter, supply line, exhaust hose and anti-siphon loop.
/// </summary>
public static class CockpitCloseUp
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
        // locker lid outline on the starboard seat (flat), hinged along the forward edge
        double[] lid = { 622, 350, 900, 350, 975, 485, 668, 485 };
        double LeftAt(double y) => 622 + 46 * (y - 350) / 135;
        double RightAt(double y) => 900 + 75 * (y - 350) / 135;
        var lidD = Poly(lid);

        var backdrop = new List<DecorPath>();

        // mast, boom end, spreader, shrouds
        backdrop.Add(P("hull-line", Ln(495, 40, 495, 172), Ln(505, 40, 505, 172), Ln(495, 40, 505, 40), Rect(478, 143, 44, 12)));
        backdrop.Add(P("rig-line", Ln(430, 88, 570, 88),
            Open(500, 52, 430, 88, 58, 232), Open(500, 52, 570, 88, 942, 232),
            Open(500, 128, 92, 236), Open(500, 128, 908, 236)));
        backdrop.Add(P("ink-thin", Circle(58, 232, 3), Circle(942, 232, 3), Circle(92, 236, 3), Circle(908, 236, 3)));

        // cabin top with handrails and the sliding hatch
        backdrop.Add(P("paper-fill", Poly(130, 172, 870, 172, 912, 205, 88, 205)));
        backdrop.Add(P("hull-line", Poly(130, 172, 870, 172, 912, 205, 88, 205)));
        backdrop.Add(P("detail", Ln(190, 188, 410, 188), Ln(590, 188, 810, 188), Rect(420, 177, 160, 23)));

        // bulkhead face
        backdrop.Add(P("wood-fill", Rect(88, 205, 824, 130)));
        backdrop.Add(P("hull-line", Ln(88, 205, 88, 335), Ln(912, 205, 912, 335)));
        backdrop.Add(P("ink-thin", Circle(205, 262, 24), Circle(205, 262, 18), Circle(860, 262, 24), Circle(860, 262, 18)));

        // companionway opening with washboards in
        backdrop.Add(P("tone-fill", Rect(415, 215, 170, 210)));
        backdrop.Add(P("paper-fill", Rect(415, 300, 170, 125)));
        backdrop.Add(P("wood-fill", Rect(415, 300, 170, 125)));
        backdrop.Add(P("ink-thin", Rect(415, 215, 170, 210)));
        backdrop.Add(P("wood-line", Ln(415, 300, 585, 300), Ln(415, 340, 585, 340), Ln(415, 382, 585, 382), Ln(440, 320, 470, 320), Ln(440, 361, 470, 361)));

        // ignition panel on the bulkhead
        backdrop.Add(P("paper-fill", RRect(680, 218, 120, 78, 6)));
        backdrop.Add(P("ink", RRect(680, 218, 120, 78, 6)));
        backdrop.Add(P("ink-thin", Circle(708, 244, 12), Ln(708, 236, 708, 244), Circle(772, 244, 8), Circle(708, 280, 4), Circle(740, 280, 4), Circle(772, 280, 4), Circle(740, 244, 5)));

        // seats: teak benches each side, well floor between
        backdrop.Add(P("wood-fill", Poly(90, 335, 400, 335, 345, 500, 5, 500)));
        backdrop.Add(P("hull-line", Poly(90, 335, 400, 335, 345, 500, 5, 500)));
        backdrop.Add(P("wood-line", Ln(167, 335, 90, 500), Ln(245, 335, 175, 500), Ln(322, 335, 260, 500)));
        backdrop.Add(P("wood-fill", Poly(910, 335, 600, 335, 655, 500, 995, 500)));
        backdrop.Add(P("hull-line", Poly(910, 335, 600, 335, 655, 500, 995, 500)));
        backdrop.Add(P("furniture", Poly(400, 335, 345, 500, 342, 562, 400, 425), Poly(600, 335, 655, 500, 658, 562, 600, 425)));
        backdrop.Add(P("wood-fill", Poly(400, 425, 600, 425, 658, 562, 342, 562)));
        backdrop.Add(P("ink-thin", Poly(400, 425, 600, 425, 658, 562, 342, 562)));
        backdrop.Add(P("wood-line", Ln(450, 425, 430, 562), Ln(500, 425, 500, 562), Ln(550, 425, 570, 562)));

        // tiller coming in from aft, knob near the companionway
        backdrop.Add(P("paper-fill", Poly(382, 566, 396, 556, 468, 492, 458, 482)));
        backdrop.Add(P("hull-line", Poly(382, 566, 396, 556, 468, 492, 458, 482), Circle(463, 487, 6)));

        // ---- the locker lid cover ----
        var lidIn = new List<DecorPath>
        {
            P("paper-fill", lidD),
            P("wood-fill", lidD),
            P("ink", lidD),
            P("wood-line", Ln(622 + 278 * .25, 350, 668 + 307 * .25, 485), Ln(622 + 278 * .5, 350, 668 + 307 * .5, 485), Ln(622 + 278 * .75, 350, 668 + 307 * .75, 485)),
            P("ink-thin", Circle(795, 428, 6), Rect(660, 351, 22, 5), Rect(840, 351, 22, 5)),
        };

        var lidOut = new List<DecorPath>
        {
            // the hole: far wall (hatched), floor
            P("paper-fill", lidD),
            P("hatch-fill", Poly(622, 350, 900, 350, RightAt(398), 398, LeftAt(398), 398)),
            P("ink", lidD),
            P("ink-thin", Ln(LeftAt(398), 398, RightAt(398), 398)),
            // fuel tank: top face, front face to the hole edge, fill-sender
            P("paper-fill", Poly(770, 420, 925, 420, 950, 462, 775, 462), Poly(775, 462, 950, 462, 955, 485, 779, 485)),
            P("ink", Poly(770, 420, 925, 420, 950, 462, 775, 462)),
            P("ink-thin", Ln(775, 462, 779, 485), Ln(950, 462, 955, 485), Circle(900, 442, 6), Ln(900, 436, 900, 428)),
            // valve on a stub off the tank, filter canister above it
            P("ink-thin", Ln(722, 440, 773, 440)),
            P("paper-fill", Poly(706, 440, 716, 433, 726, 440, 716, 447)),
            P("ink", Poly(704, 440, 716, 432, 728, 440, 716, 448), Ln(716, 432, 716, 424), Ln(710, 424, 722, 424)),
            P("paper-fill", Rect(706, 362, 26, 10), Rect(708, 372, 22, 24), Rect(711, 396, 16, 8)),
            P("ink", Rect(706, 362, 26, 10), Rect(708, 372, 22, 24)),
            P("ink-thin", Rect(711, 396, 16, 8), Ln(708, 384, 730, 384)),
            P("ink", Ln(716, 424, 716, 404)),
            // supply line out through the bulkhead at the left of the hole
            P("ink", Ln(706, 366, 634, 366), Circle(630, 366, 3)),
            // exhaust hose (two lines) and anti-siphon loop
            P("paper-fill", Poly(772, 380, 922, 380, 922, 392, 772, 392)),
            P("ink", Ln(772, 380, 922, 380), Ln(772, 392, 922, 392), Ln(772, 380, 772, 392)),
            P("ink-thin", Ln(790, 380, 790, 392), Ln(870, 380, 870, 392), Ln(900, 380, 900, 392)),
            P("ink", "M836 380C836 356 856 356 856 380", Circle(846, 361, 3)),
            // the lid folded back against the bulkhead, underside showing
            P("paper-fill", Poly(622, 350, 900, 350, 885, 298, 640, 298)),
            P("ink", Poly(622, 350, 900, 350, 885, 298, 640, 298)),
            P("wood-fill", Poly(622, 350, 900, 350, 885, 298, 640, 298)),
            P("wood-line", Ln(632, 324, 892, 324), Ln(636, 311, 888, 311), Ln(627, 337, 896, 337)),
            P("ink-thin", Rect(660, 345, 22, 5), Rect(840, 345, 22, 5)),
        };

        var covers = ImmutableArray.Create(new CoverShape(
            "Cockpit-locker lid", lidD, A(lidIn.ToArray()), A(lidOut.ToArray()),
            "Cockpit-locker lid", 690, 580));

        var spots = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new Dictionary<string, MarkerSpot>
        {
            ["Ignition panel"] = Sp(740, 258, "Ignition panel", 740, 212, "middle"),
            ["Standing rig"] = MarkerSpot.Right(500, 112, "Standing rig"),
            ["Fuel tank"] = Sp(850, 440, "Fuel tank", 878, 527),
            ["Fuel shutoff valve"] = Sp(716, 440, "Shutoff valve", 745, 527, "end"),
            ["Fuel supply line"] = Sp(668, 366, "Fuel line", 660, 552, "end"),
            ["Primary fuel filter / water separator"] = Sp(719, 384, "Primary filter", 768, 552),
            ["Exhaust hose"] = new MarkerSpot(808, 386, "Exhaust hose", 800, 410),
            ["Anti-siphon loop"] = MarkerSpot.Right(846, 366, "Anti-siphon loop"),
        });

        return new CloseUpScene(
            "Cockpit", "COCKPIT  /  LOOKING FORWARD",
            A(backdrop.ToArray()), covers, ImmutableArray<DecorPath>.Empty, spots,
            ImmutableArray.Create((60.0, 70.0), (60.0, 92.0), (60.0, 114.0), (60.0, 136.0), (700.0, 66.0), (700.0, 88.0), (700.0, 110.0), (700.0, 132.0)));
    }
}
