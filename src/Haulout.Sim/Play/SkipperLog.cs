namespace Haulout.Sim.Play;

/// <summary>The part the skipper played in a passage. A crewing place (design note 012, step 4) writes <see cref="Crew"/>: miles made on another skipper's boat.</summary>
public enum PassageRole { Skipper, Crew }

/// <summary>
/// One passage in the skipper's log: from casting off to arriving at a port. A turn back is not one and a towed arrival is not one
/// (their miles are in the log all the same). Overnight: it crossed a midnight under way.
/// </summary>
public sealed record LoggedPassage(double Nm, double Hours, double NightHours, bool Overnight, PassageRole Role);

/// <summary>
/// The skipper's own record (design note 012, decision 2): the CV the tickets read. It is plain data with no reference to a game or a boat, so
/// that design note 011 can carry it to another boat. Nothing in it is a score. The game feeds it from the same events as the boat's log: the
/// passage's end, the midnight, the tow and the autumn page.
/// </summary>
public sealed record SkipperLog
{
    /// <summary>Nautical miles made under way (the passage's log: drift is not counted; a tow's miles are).</summary>
    public double MilesUnderWay { get; init; }
    /// <summary>Nights aboard away from the home port (a midnight afloat at another port, or at sea).</summary>
    public int NightsAway { get; init; }
    /// <summary>Hours under way (not at anchor) after dark by the daylight table.</summary>
    public double NightHoursUnderWay { get; init; }
    /// <summary>Passages that count, in order. Lengths over any distance and the nights they ran through are read from here.</summary>
    public IReadOnlyList<LoggedPassage> Passages { get; init; } = [];
    /// <summary>The strongest wind force under way, 0 when none was logged.</summary>
    public int StrongestForce { get; init; }
    /// <summary>Days on which the player worked aboard or was at sea.</summary>
    public int DaysAboard { get; init; }
    public int Tows { get; init; }
    /// <summary>Seasons completed (the autumn page) with some miles sailed and no tow.</summary>
    public int SeasonsWithoutTow { get; init; }
    /// <summary>A first-aid day held. Never true this round: nothing gives one yet.</summary>
    public bool FirstAidHeld { get; init; }

    /// <summary>Passages longer than <paramref name="nm"/>, optionally only those made as skipper.</summary>
    public int PassagesOver(double nm, bool asSkipper = false) =>
        Passages.Count(p => p.Nm > nm && (!asSkipper || p.Role == PassageRole.Skipper));

    /// <summary>Passages longer than <paramref name="nm"/> made as crew on another skipper's boat.</summary>
    public int PassagesOverAsCrew(double nm) => Passages.Count(p => p.Nm > nm && p.Role == PassageRole.Crew);

    /// <summary>Passages longer than <paramref name="nm"/> that ran through a night, optionally only those made in one role.</summary>
    public int PassagesOverThroughANight(double nm, PassageRole? role = null) =>
        Passages.Count(p => p.Nm > nm && p.Overnight && (role is null || p.Role == role));

    /// <summary>The log with one more passage, its miles and its night hours.</summary>
    public SkipperLog WithPassage(LoggedPassage passage) =>
        this with { Passages = [.. Passages, passage] };
}
