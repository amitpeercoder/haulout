using System.Collections.Immutable;

namespace Haulout.Web.Art.CloseUps;

/// <summary>
/// Close-up: Beside the hull. On the yard ground, the port side of the hull above, bow to the right as on the map.
/// Fin keel on its block, spade rudder aft, boot-top, bottom paint, cradle stands, the ladder up to the deck.
/// </summary>
public static class BesideTheHullCloseUp
{
    private const double GroundY = 545;

    // sheer: quadratic from the stern corner to the bow, x is linear in t
    private static double SheerY(double x)
    {
        var t = (x - 110) / 780.0;
        return (1 - t) * (1 - t) * 215 + 2 * t * (1 - t) * 255 + t * t * 190;
    }

    private static string Stand(double x, double padY)
    {
        var mid = padY + 62;
        return $"M{ArtPen.N(x - 26)},{ArtPen.N(padY)} L{ArtPen.N(x + 26)},{ArtPen.N(padY)} M{ArtPen.N(x - 22)},{ArtPen.N(padY)} L{ArtPen.N(x - 22)},{ArtPen.N(padY - 5)} M{ArtPen.N(x + 22)},{ArtPen.N(padY)} L{ArtPen.N(x + 22)},{ArtPen.N(padY - 5)}"
            + $" M{ArtPen.N(x)},{ArtPen.N(padY)} L{ArtPen.N(x)},{ArtPen.N(mid)}"
            + $" M{ArtPen.N(x - 8)},{ArtPen.N(mid)} L{ArtPen.N(x + 8)},{ArtPen.N(mid)}"
            + $" M{ArtPen.N(x - 5)},{ArtPen.N(mid)} L{ArtPen.N(x - 40)},{GroundY} M{ArtPen.N(x + 5)},{ArtPen.N(mid)} L{ArtPen.N(x + 40)},{GroundY}"
            + $" M{ArtPen.N(x - 52)},{GroundY} L{ArtPen.N(x - 28)},{GroundY} M{ArtPen.N(x + 28)},{GroundY} L{ArtPen.N(x + 52)},{GroundY}"
            + $" M{ArtPen.N(x - 24)},{ArtPen.N(mid + 80)} L{ArtPen.N(x + 24)},{ArtPen.N(mid + 80)}";
    }

    public static readonly CloseUpScene Scene = Build();

    private static CloseUpScene Build()
    {
        const string hull =
            "M110,215 Q500,255 900,190 L868,318 Q845,372 790,382 L612,388 L576,526 L530,526 L490,388 C380,386 250,372 178,356 L129,338 Z";
        const string paintBelow =
            "M128.4,334 L860,334 Q834,368 790,382 L612,388 L576,526 L530,526 L490,388 C380,386 250,372 178,356 L129,338 Z";
        const string rudder = "M152,346 L204,359 L194,476 L158,476 Z";

        var back = new List<DecorPath>();

        // ground, gravel, a bucket and a trestle, spruce beyond the yard
        back.Add(ArtPen.D("ground", "M0,545 L1000,545"));
        back.Add(ArtPen.D("detail", "M30,556 L52,556 M90,563 L120,563 M180,552 L196,552 M260,560 L290,560 M360,554 L378,554 M430,566 L470,566 M640,556 L662,556 M720,563 L756,563 M830,552 L846,552 M900,559 L930,559 M980,554 L996,554"));
        back.Add(ArtPen.D("furniture", "M958,545 L964,512 L992,512 L998,545 M960,520 L996,520", "M964,512 Q978,500 992,512",
            "M30,545 L50,498 M62,545 L42,498 M36,516 L60,516 M10,545 L10,545"));
        var trees = new System.Text.StringBuilder();
        foreach (var x in new[] { 22.0, 52, 86, 940, 972, 1000 })
            trees.Append($"M{ArtPen.N(x - 11)},470 L{ArtPen.N(x)},{ArtPen.N(400 + x % 17)} L{ArtPen.N(x + 11)},470 M{ArtPen.N(x)},470 L{ArtPen.N(x)},545 ");
        back.Add(ArtPen.D("furniture", trees.ToString().TrimEnd()));

        // keel block under the fin, cradle stands
        back.Add(ArtPen.D("yard", ArtPen.Rect(505, 526, 96, 12), ArtPen.Rect(497, 538, 112, 7), "M530,526 L530,538 M576,526 L576,538", "M480,545 L497,545 M609,545 L626,545 L608,538"));
        back.Add(ArtPen.D("yard", Stand(300, 377), Stand(790, 385)));

        // hull: bottom paint (hatched), then the outline
        back.Add(ArtPen.D("hatch-fill", paintBelow, rudder));
        back.Add(ArtPen.D("hull-line", hull));
        back.Add(ArtPen.D("hull-line", rudder));
        back.Add(ArtPen.D("ink-thin", "M150,346 L206,359 L206,352 L150,340"));  // rudder head, where it meets the hull
        back.Add(ArtPen.D("ink-thin", "M600,388 L572,520", "M502,388 L534,520"));  // keel foil edges
        // boot-top stripe
        back.Add(ArtPen.D("ink", "M126.5,322 L866,322", "M128.4,334 L860,334"));
        back.Add(ArtPen.D("furniture", "M120,278 Q500,308 888,242"));  // hull-deck joint / rubbing strake
        back.Add(ArtPen.D("detail", "M150,330 L150,334 M300,334 L300,338"));

        // keel bolts (the heads inside): none shown; flat of the fin has a small inspection mark
        // deck edge, toerail, stanchions, wires, pulpits
        var toerail = "M112,210 Q500,250 898,185";
        back.Add(ArtPen.D("ink-thin", toerail));
        var xs = new[] { 140.0, 225, 310, 395, 480, 565, 650, 780, 850 };
        var stan = new System.Text.StringBuilder();
        var top = new List<(double, double)>(); var mid = new List<(double, double)>();
        foreach (var x in xs)
        {
            var y = SheerY(x) - 4;
            stan.Append(ArtPen.Line(x, y, x, y - 62)).Append(' ').Append(ArtPen.Line(x - 5, y, x + 5, y)).Append(' ');
            top.Add((x, y - 62)); mid.Add((x, y - 31));
        }
        back.Add(ArtPen.D("ink-thin", stan.ToString().TrimEnd()));
        // wires: stern pushpit, the stanchions up to the gate, the stanchions from the gate to the bow pulpit
        back.Add(ArtPen.D("detail",
            ArtPen.Poly(false, new[] { (118.0, SheerY(118) - 62) }.Concat(top.Take(8).Take(7)).ToArray()),
            ArtPen.Poly(false, new[] { (118.0, SheerY(118) - 31) }.Concat(mid.Take(7)).ToArray()),
            ArtPen.Poly(false, top.Skip(7).Append((892, SheerY(892) - 62)).ToArray()),
            ArtPen.Poly(false, mid.Skip(7).Append((892, SheerY(892) - 31)).ToArray())));
        back.Add(ArtPen.D("ink", $"M118,{ArtPen.N(SheerY(118) - 4)} L118,{ArtPen.N(SheerY(118) - 62)} Q118,{ArtPen.N(SheerY(118) - 66)} 130,{ArtPen.N(SheerY(130) - 64)}",
            $"M892,{ArtPen.N(SheerY(892) - 4)} L892,{ArtPen.N(SheerY(892) - 62)} Q892,{ArtPen.N(SheerY(892) - 66)} 880,{ArtPen.N(SheerY(880) - 64)}"));

        // coachroof, windows, sprayhood
        back.Add(ArtPen.D("paper-fill", "M310,230 L326,176 L580,170 L618,228 Z"));
        back.Add(ArtPen.D("hull-line", "M310,231 L326,176 Q460,166 580,170 L618,228"));
        back.Add(ArtPen.D("ink-thin", "M345,190 L430,188 L432,208 L342,210 Z", "M450,188 L535,187 L538,207 L452,208 Z", "M552,187 L582,187 L596,206 L556,207 Z"));
        back.Add(ArtPen.D("sprayhood", "M296,230 L304,184 Q330,152 372,158 L384,186 L380,230", "M312,186 L368,182", "M308,206 L376,204"));
        back.Add(ArtPen.D("ink-thin", "M386,176 L398,176"));
        // mast, boom, shrouds, stays
        back.Add(ArtPen.D("rig-line", "M464,174 L464,0", "M476,174 L476,0", "M456,177 L484,177"));
        back.Add(ArtPen.D("rig-line", "M476,132 L262,126", "M476,138 L262,131", "M262,126 L262,131"));
        back.Add(ArtPen.D("ink-thin", "M300,127 L300,134 M340,128 L340,134 M380,129 L380,135 M420,130 L420,136"));
        back.Add(ArtPen.D("rig-line",
            "M452,238 L470,0",       // cap shroud
            "M426,238 L470,96",      // aft lower
            "M512,238 L470,98",      // forward lower
            "M470,6 L902,182",       // forestay
            "M470,6 L108,208"));     // backstay
        back.Add(ArtPen.D("ink", "M446,236 L458,236 L458,242 L446,242 Z", "M420,236 L432,236 L432,242 L420,242 Z", "M506,236 L518,236 L518,242 L506,242 Z"));

        // the ladder, leaning on the hull, top hooked over the rail at the gate
        back.Add(ArtPen.D("ink", "M693,545 L683,188", "M737,545 L727,188", "M683,188 L673,192", "M727,188 L717,192"));
        for (var i = 1; i <= 9; i++)
        {
            var y = 545 - i * (545 - 188) / 10.0;
            var xl = 693 - (545 - y) * 10 / 357.0;
            back.Add(ArtPen.D("ink-thin", ArtPen.Line(xl, y, xl + 44, y)));
        }

        var spots = ImmutableDictionary<string, MarkerSpot>.Empty
            .Add("Hull", MarkerSpot.Right(540, 276, "Hull"))
            .Add("Keel", new MarkerSpot(558, 462, "Keel", 612, 466, "start", 602, 464))
            .Add("Rudder", MarkerSpot.Right(176, 420, "Rudder"))
            .Add("Antifouling", MarkerSpot.Right(372, 358, "Antifouling"))
            .Add("Standing rig", new MarkerSpot(688, 95, "Standing rig", 677, 122, "end"));
        // the Standing rig dot sits on the forestay: y = 6 + 176 * (x - 470) / 432

        var fallback = ImmutableArray.Create<(double X, double Y)>(
            (180, 285), (260, 292), (420, 290), (640, 282), (800, 262), (480, 360), (880, 330), (610, 100));

        return new CloseUpScene("Beside the hull", "BESIDE THE HULL  /  PORT SIDE", back.ToImmutableArray(),
            ImmutableArray<CoverShape>.Empty, ImmutableArray<DecorPath>.Empty, spots, fallback);
    }
}
