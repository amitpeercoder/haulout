using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The chart (design note 009): the track of a passage as hourly fixes, the season's earlier passages, and the Chart query.
/// Nothing here draws randomness or changes the tick; every event is a line the logbook already has.
/// </summary>
public sealed partial class Game
{
    /// <summary>One fix of a track: hour 0 is the departure. Events are the logbook lines of that hour.</summary>
    internal sealed class TrackFix
    {
        public int Hour { get; init; }
        public double EastNm { get; set; }
        public double NorthNm { get; set; }
        public string Mode { get; set; } = "";
        public List<string> Events { get; } = [];
    }

    /// <summary>A finished passage, kept for the chart: "this season" is the clock year it left in.</summary>
    internal sealed record PastPassage(string From, string To, int Year, int Day, string Outcome, IReadOnlyList<TrackFix> Track, double Nm = 0);

    private readonly List<PastPassage> _pastPassages = [];

    private static double Tenth(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);

    /// <summary>The first fix of a passage: the origin, hour 0.</summary>
    private void StartTrack(Passage p)
    {
        var at = _boat.Trips!.PointAlong(p.Route, 0);
        var fix = new TrackFix { Hour = 0, EastNm = at.EastNm, NorthNm = at.NorthNm, Mode = p.ModeWord };
        fix.Events.Add($"Left {PortName(p.Route.From)} for {PortName(p.Route.To)}.");
        p.Track.Add(fix);
    }

    /// <summary>The fix for the current hour: found, or made (position and mode as they are now).</summary>
    private TrackFix FixFor(Passage p)
    {
        int hour = HourOf(p);
        var at = _boat.Trips!.PointAlong(p.Route, p.ProgressNm);
        if (p.Track.Count > 0 && p.Track[^1].Hour == hour)
        {
            var last = p.Track[^1];
            last.EastNm = at.EastNm;
            last.NorthNm = at.NorthNm;
            last.Mode = p.ModeWord;
            return last;
        }
        var fix = new TrackFix { Hour = hour, EastNm = at.EastNm, NorthNm = at.NorthNm, Mode = p.ModeWord };
        p.Track.Add(fix);
        return fix;
    }

    /// <summary>After every at-sea step: this hour's fix takes her position and mode now.</summary>
    private void UpdateFix(Passage p)
    {
        if (_boat.Trips is not null) FixFor(p);
    }

    /// <summary>Puts a logbook line on the current hour's fix. No-op when not at sea.</summary>
    private void MarkTrack(string text)
    {
        if (_passage is { } p && _boat.Trips is not null) FixFor(p).Events.Add(text);
    }

    /// <summary>The passage ends: it moves to the season's memory with its outcome.</summary>
    private void ArchivePassage(Passage p, string outcome) =>
        _pastPassages.Add(new PastPassage(PortName(p.OriginPortId), PortName(BoundFor(p)), p.Year, DayAt(p.DepartClockMinutes), outcome, p.Track.ToList(), p.CoveredNm));

    /// <summary>The port the passage was first bound for (a turn back reverses the route, so it is then the route's From).</summary>
    private static string BoundFor(Passage p) => p.TurnedBack ? p.Route.From : p.Route.To;

    private static double DistanceBetween(ChartPoint a, ChartPoint b)
    {
        double dx = b.EastNm - a.EastNm, dy = b.NorthNm - a.NorthNm;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int BearingBetween(ChartPoint a, ChartPoint b)
    {
        double deg = Math.Atan2(b.EastNm - a.EastNm, b.NorthNm - a.NorthNm) * 180 / Math.PI;
        return (((int)Math.Round(deg, MidpointRounding.AwayFromZero) % 360) + 360) % 360;
    }

    private ChartTrack TrackView(string from, string to, int day, string outcome, IEnumerable<TrackFix> fixes) =>
        new(from, to, day, outcome,
            fixes.Select(f => new ChartFix(f.Hour, Tenth(f.EastNm), Tenth(f.NorthNm), f.Mode, f.Events.ToList())).ToList());

    private ChartView ChartSnapshot(string? planned)
    {
        var ci = CultureInfo.InvariantCulture;
        var wd = Today;
        var wind = new ChartWind(wd.WindForce, wd.WindFrom, wd.RainMm > 0, WeatherWords.Describe(wd));
        if (_boat.Trips is not { } trips)
        {
            return new ChartView("She lies at her mooring.", [], [],
                new ChartBoat(false, null, 0, 0, null, null, 0, 0, null, 0, _sunk), null, [], wind, null);
        }

        // Where the boat is, in chart miles.
        var p = _passage;
        var here = p is not null ? trips.PointAlong(p.Route, p.ProgressNm) : CurrentPort?.Chart ?? new ChartPoint(0, 0);

        var reachable = AtSea || _sunk ? [] : trips.RoutesFrom(_portId).Select(r => r.To).ToHashSet();
        var ports = trips.Ports.Where(x => x.Chart is not null).Select(x =>
        {
            IReadOnlyList<Route> legs = AtSea || _sunk || x.Id == _portId || reachable.Contains(x.Id) ? [] : trips.PathBetween(_portId, x.Id);
            var way = legs.Count > 1 ? new Way(legs) : null;
            return new ChartPort(
                x.Name, Tenth(x.Chart!.EastNm), Tenth(x.Chart.NorthNm), x.Yard, x.Shop, x.Fuel, x.Anchorage,
                p is null && x.Id == _portId, reachable.Contains(x.Id),
                Tenth(DistanceBetween(here, x.Chart)), BearingBetween(here, x.Chart),
                way is null ? null : ViaNames(way), way?.TotalNm ?? 0);
        }).ToList();

        var routes = trips.Routes.Select(r => new ChartRoute(PortName(r.From), PortName(r.To), r.DistanceNm, r.CourseDeg, ShelterViews(trips, r))).ToList();

        string hereText;
        ChartBoat boat;
        if (p is not null)
        {
            string mode = p.ModeWord;
            hereText = _sunk
                ? "She lies on the bottom of the sound."
                : $"{char.ToUpperInvariant(mode[0])}{mode[1..]}, {Tenth(p.ProgressNm).ToString("0.0", ci)} nm out of {PortName(p.Route.From)}, "
                  + $"{Tenth(p.RemainingNm).ToString("0.0", ci)} nm to {PortName(p.Route.To)}.";
            boat = new ChartBoat(true, null, Tenth(here.EastNm), Tenth(here.NorthNm), PortName(p.Route.From), PortName(p.Route.To),
                Tenth(p.ProgressNm), Tenth(p.RemainingNm), mode, p.Route.CourseDeg, _sunk);
        }
        else
        {
            string at = CurrentPort?.Name ?? "";
            hereText = _sunk ? "She lies on the bottom of the harbour."
                : at.Length > 0 ? $"She lies at {at}." : "She lies at her mooring.";
            boat = new ChartBoat(false, at.Length > 0 ? at : null, Tenth(here.EastNm), Tenth(here.NorthNm), null, null, 0, 0, null, 0, _sunk);
        }

        // A passage that sank has been put in the season's memory; the current track is only the one still under way.
        ChartTrack? track = p is not null && !_sunk
            ? TrackView(PortName(p.OriginPortId), PortName(BoundFor(p)), DayAt(p.DepartClockMinutes), "under way", p.Track)
            : null;
        int year = new GameClock(Day).Year;
        var earlier = _pastPassages.Where(x => x.Year == year).Select(x => TrackView(x.From, x.To, x.Day, x.Outcome, x.Track)).ToList();

        ChartPlan? plan = null;
        if (!string.IsNullOrWhiteSpace(planned) && !AtSea && !_sunk && FindWay(trips, planned) is { } way)
        {
            var snap = PlanTripSnapshot(planned, null);
            plan = new ChartPlan(snap.From, snap.To, way.TotalNm, way.First.CourseDeg, ShelterViewsAlong(trips, way),
                snap.MotorHours, snap.SailHours, snap.Estimate, snap.Blockers);
        }

        return new ChartView(hereText, ports, routes, boat, track, earlier, wind, plan);
    }

    private static List<ChartShelter> ShelterViews(TripRules trips, Route r) =>
        r.Shelter.Select(s =>
        {
            var at = trips.PointAlong(r, s.AtNm);
            return new ChartShelter(s.Name, s.AtNm, Tenth(at.EastNm), Tenth(at.NorthNm));
        }).ToList();
}
