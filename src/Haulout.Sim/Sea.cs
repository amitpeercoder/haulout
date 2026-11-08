using Haulout.Sim.Data;

namespace Haulout.Sim;

/// <summary>
/// Pure functions over <see cref="SeaRules"/>: wind force to sea state, compass words to degrees, and whether the wind serves
/// for sailing a course. Used by the trip plan and by the at-sea tick (design note 007).
/// </summary>
public static class Sea
{
    private static readonly string[] Points =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    /// <summary>A compass word ("southwest") to degrees, north 0, clockwise in 45 steps. Unknown words are 0.</summary>
    public static int Degrees(string? word)
    {
        int i = Array.FindIndex(Points, p => p.Equals((word ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        return i < 0 ? 0 : i * 45;
    }

    /// <summary>The sea state for a wind force (clamped into the table).</summary>
    public static int SeaState(SeaRules rules, int force) =>
        rules.StateByForce.Count == 0 ? 0 : rules.StateByForce[Math.Clamp(force, 0, rules.StateByForce.Count - 1)];

    /// <summary>The sea state in words ("a short chop").</summary>
    public static string StateWord(SeaRules rules, int seaState) =>
        rules.StateWords.Count == 0 ? "" : rules.StateWords[Math.Clamp(seaState, 0, rules.StateWords.Count - 1)];

    /// <summary>The angle between the wind's direction (where it blows from) and the course, 0 to 180: 0 is on the nose.</summary>
    public static int WindOnBoat(int windFromDeg, int courseDeg)
    {
        int d = Math.Abs(((windFromDeg - courseDeg) % 360 + 360) % 360);
        return d > 180 ? 360 - d : d;
    }

    public static int WindOnBoat(string windFrom, int courseDeg) => WindOnBoat(Degrees(windFrom), courseDeg);

    /// <summary>"on the nose", "forward of the beam", "on the beam", "on the quarter" or "from astern".</summary>
    public static string WindPhrase(SeaRules rules, int windOnBoat) =>
        windOnBoat < rules.NoGoDeg ? "on the nose"
        : windOnBoat < 80 ? "forward of the beam"
        : windOnBoat < 110 ? "on the beam"
        : windOnBoat < 150 ? "on the quarter"
        : "from astern";

    /// <summary>Boat speed under sail in knots for this force (0 outside the sailing range).</summary>
    public static double SailKnots(SeaRules rules, int force) =>
        force >= rules.SailMinForce && force <= rules.SailMaxForce && force < rules.SailKnots.Count ? rules.SailKnots[force] : 0;

    /// <summary>The wind serves when the force is in the sailing range and the course is not closer than the no-go angle to the wind.</summary>
    public static bool Serves(SeaRules rules, int force, string windFrom, int courseDeg) =>
        SailKnots(rules, force) > 0 && WindOnBoat(windFrom, courseDeg) >= rules.NoGoDeg;
}
