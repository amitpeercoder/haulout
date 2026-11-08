namespace Haulout.Sim.Time;

/// <summary>
/// The one place the simulation clock lives. Work minutes are the player's own actions; idle minutes are calendar time
/// that passes with the player away (waiting, the survey, the yard's days). Only <c>Game.Tick</c> calls <see cref="Advance"/>.
/// </summary>
public sealed class SimTime
{
    public int WorkMinutes { get; private set; }
    public int IdleMinutes { get; private set; }
    public int ClockMinutes => WorkMinutes + IdleMinutes;

    internal void Advance(int minutes, bool work)
    {
        if (minutes < 0) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (work) WorkMinutes += minutes; else IdleMinutes += minutes;
    }
}
