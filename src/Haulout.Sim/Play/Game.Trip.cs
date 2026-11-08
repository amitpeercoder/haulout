using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// Where she lies, and the plan for a trip (design note 007). The at-sea tick and commands build on <see cref="Passage"/>.
/// </summary>
public sealed partial class Game
{
    private readonly int _worldSeed;

    /// <summary>The port she lies at when not at sea. Starts at the data's home port.</summary>
    private string _portId = "";

    /// <summary>Non-null while at sea. Created by Depart, ended by arrival or the tow (see Game.Sea.cs).</summary>
    private Passage? _passage = null;

    /// <summary>True while a passage is under way.</summary>
    internal bool AtSea => _passage is not null;

    /// <summary>How she is moving through the water (design note 007). The words are in <see cref="Passage.ModeWord"/>.</summary>
    internal enum SeaMode { Motoring, Sailing, HoveTo, Anchored, Drifting, UnderTow }

    /// <summary>State of a passage under way (design note 007).</summary>
    internal sealed class Passage
    {
        /// <summary>The route as she sails it now: reversed by a turn back.</summary>
        public Route Route { get; set; } = null!;
        /// <summary>The port she cast off from, and the one she is bound for now (the origin again after a turn back).</summary>
        public string OriginPortId { get; init; } = "";
        public string DestinationPortId { get; set; } = "";
        /// <summary>Nautical miles made through the water along the route. Drift is how far the sea has set her back; the goal is Distance + Drift.</summary>
        public double PositionNm { get; set; }
        public double DriftNm { get; set; }
        /// <summary>Nautical miles the boat itself (or the tow) carried her along the route: the log of the passage. Drift is not counted (design note 010, step 4).</summary>
        public double CoveredNm { get; set; }
        public SeaMode Mode { get; set; } = SeaMode.Motoring;
        public bool TurnedBack { get; set; }
        /// <summary>Overheat minutes in a row; cleared when the engine stops or cools.</summary>
        public int OverheatMinutes { get; set; }
        /// <summary>How the engine sounded at the end of the last step, for the hour line.</summary>
        public string EngineNote { get; set; } = "";
        public int DepartClockMinutes { get; init; }
        /// <summary>Set when the passage ends (arrival, or the end of the tow).</summary>
        public int EndClockMinutes { get; set; }
        public int MoneyAtDeparture { get; init; }
        public int LogStart { get; init; }
        /// <summary>The knots fog holds her to this step (null: none), for the hour line. The patrol fined her already this passage (design note 010).</summary>
        public double? FogCap { get; set; }
        public bool PatrolFined { get; set; }
        public List<string> Failures { get; } = [];
        public List<string> Fixes { get; } = [];
        /// <summary>The clock year she left in, and the hourly fixes of the passage (design note 009).</summary>
        public int Year { get; init; }
        public List<TrackFix> Track { get; } = [];

        /// <summary>Net distance along the route from its start, drift taken off (never below 0).</summary>
        public double ProgressNm => Math.Max(0, PositionNm - DriftNm);
        public double RemainingNm => Math.Max(0, Route.DistanceNm + DriftNm - PositionNm);

        public string ModeWord => Mode switch
        {
            SeaMode.Motoring => "under engine",
            SeaMode.Sailing => "under sail",
            SeaMode.HoveTo => "hove-to",
            SeaMode.Anchored => "at anchor",
            SeaMode.Drifting => "drifting",
            _ => "under tow",
        };
    }

    private Port? CurrentPort => _boat.Trips?.FindPort(_portId);

    /// <summary>Refusal for work only the yard does (fixes, survey, launch, lift, shore power) when she is not at the yard. Null: fine.</summary>
    private string? YardGate()
    {
        if (AtSea) return "You're at sea.";
        if (_boat.Trips is not { } trips || CurrentPort is { Yard: true }) return null;
        var yard = trips.Ports.FirstOrDefault(p => p.Yard);
        return yard is null ? "There's no yard here." : $"The yard is at {yard.Name}.";
    }

    // ---------------------------------------------------------------- the plan

    private static int ForecastSeed(int worldSeed, int day, int today) =>
        unchecked(worldSeed * 73856093 ^ day * 19349663 ^ today * 83492791);

    /// <summary>
    /// What the forecast says for a day. Lead 0 is the real day. Further out, with the data's chances, the force is off by one
    /// (clamped 0 to 9) and the rain is flipped. Uses its own random stream, never the world's or the player's.
    /// </summary>
    private ForecastDay ForecastFor(int day, Route route)
    {
        var rules = _boat.ForecastRules;
        int lead = Math.Max(0, day - Day);
        if (lead > rules.MaxLeadDays) return new ForecastDay(day, false, "No forecast that far out.", 0, "", false, false);

        var real = _weather[day];
        var noisy = real;
        if (lead > 0)
        {
            var rng = new SeededRandom(ForecastSeed(_worldSeed, day, Day));
            double forceRoll = rng.NextDouble(), signRoll = rng.NextDouble(), rainRoll = rng.NextDouble();
            int i = Math.Min(lead, Math.Min(rules.ForceErrorChance.Count, rules.RainFlipChance.Count) - 1);
            if (i >= 0)
            {
                if (forceRoll < rules.ForceErrorChance[i])
                    noisy = noisy with { WindForce = Math.Clamp(real.WindForce + (signRoll < 0.5 ? -1 : 1), 0, 9) };
                if (rainRoll < rules.RainFlipChance[i])
                    noisy = noisy with { RainMm = real.RainMm > 0 ? 0 : 1.0 };
            }
        }
        // The fog is a morning you see coming: the forecast's noise does not touch it.
        string fog = WeatherWords.FogWord(real);
        string text = WeatherWords.Describe(noisy) + (fog.Length > 0 ? $" {char.ToUpperInvariant(fog[0])}{fog[1..]}." : "");
        return new ForecastDay(day, true, text, noisy.WindForce, noisy.WindFrom,
            noisy.RainMm > 0, Sea.Serves(_boat.SeaRules, noisy.WindForce, noisy.WindFrom, route.CourseDeg), fog.Length > 0);
    }

    /// <summary>The route out of the current port to the named destination (null: the one route out). Null: there is none.</summary>
    private Route? FindRoute(TripRules trips, string? destination)
    {
        var routes = trips.RoutesFrom(_portId).ToList();
        if (string.IsNullOrWhiteSpace(destination)) return routes.FirstOrDefault();
        var ports = routes.Select(r => trips.FindPort(r.To)).OfType<Port>().ToList();
        var res = NameResolver.Resolve(ports, destination, p => p.Id, p => p.Name, p => p.Aliases);
        return res.Match is { } target ? routes.First(r => r.To == target.Id) : null;
    }

    private List<string> TripBlockers(int departDay)
    {
        var b = new List<string>();
        if (AtSea) b.Add("You're already at sea.");
        else if (_sunk) b.Add("She has sunk.");
        else
        {
            if (_onTheHard) b.Add("She's still on the hard.");
            if (!_finished) b.Add("The engine hasn't run yet this season.");
            if (departDay == Day && Today.WindForce >= _boat.Rates.WeatherGates.WindForce)
                b.Add($"It's blowing too hard to leave today: {WeatherWords.WindPhrase(Today)}.");
            if (departDay == Day && FogHoldsHarbour) b.Add(FogHarbourMessage);
        }
        return b;
    }

    private TripPlanView PlanTripSnapshot(string? destination, int? departDay)
    {
        int depart = Math.Max(Day, departDay ?? Day);
        var blockers = TripBlockers(depart);
        var sea = _boat.SeaRules;
        var from = CurrentPort;
        string fromName = from?.Name ?? "";
        int towNok = _boat.Trips is null ? 0 : TowPriceNok;

        TripPlanView NoRoute(string to, string reason)
        {
            blockers.Add(reason);
            return new TripPlanView(fromName, to, 0, 0, depart, 0, null, "", [], [], SparesAboardNames(), [], [], Balance, towNok, blockers);
        }

        if (_boat.Trips is not { } trips || AtSea)
            return NoRoute(destination?.Trim() ?? "", AtSea ? "You can't plan a trip from the water." : "There's nowhere to go from here.");

        var route = FindRoute(trips, destination);
        if (route is null)
            return NoRoute(destination?.Trim() ?? "", $"There's no route from {fromName} to '{(destination ?? "").Trim()}'.");

        var toName = trips.FindPort(route.To)?.Name ?? route.To;
        var days = new[] { depart, depart + 1 }.Select(d => ForecastFor(d, route)).ToList();
        var ci = CultureInfo.InvariantCulture;

        // What the player has seen of the growth on her hull is all the plan may know of it.
        double motorKnots = Slowed(sea.MotorKnots, _foulingSeen);
        double motorHours = Math.Round(route.DistanceNm / motorKnots, 1);
        double? sailHours = null;
        string estimate = $"About {motorHours.ToString("0.0", ci)} hours under engine at {motorKnots.ToString("0.#", ci)} knots";
        if (_foulingSeen > 0) estimate += $" (the growth you saw on her hull costs her about {(sea.MotorKnots - motorKnots).ToString("0.#", ci)} knots)";
        var first = days[0];
        if (!first.Known) estimate += "; no forecast that far out, so no sailing estimate.";
        else if (first.WindServes)
        {
            sailHours = Math.Round(route.DistanceNm / Slowed(Sea.SailKnots(sea, first.WindForce), _foulingSeen), 1);
            estimate += $"; about {sailHours.Value.ToString("0.0", ci)} under sail if the forecast wind holds.";
        }
        else estimate += "; the forecast wind doesn't serve for sailing.";

        var risks = new List<TripRisk>();
        foreach (var f in KnownOpenFaults().Distinct())
        {
            if (f.AtSea is not { } at) continue;
            var spare = at.Spare is null ? null : ItemById(at.Spare);
            risks.Add(new TripRisk(f.Name, _boat.Get(f.ComponentId).Name, at.Text, spare?.Name,
                spare is not null && _inventory.GetValueOrDefault(spare.Id) > 0));
        }
        var missing = risks.Where(r => r.Spare is not null && !r.SpareAboard).Select(r => r.Spare!).Distinct().ToList();
        var shelter = route.Shelter.Select(s => $"{s.Name}, {s.AtNm.ToString("0.#", ci)} nm out").ToList();

        var (fuelLine, warnings) = PassageWarnings(route, depart, motorHours, days, out int vacationDays);
        var atDestination = EventsAt(route.To, ArrivalDay(depart, motorHours));
        if (trips.FindPort(route.To) is { Anchorage: true }) atDestination.Add("An anchorage: no shop, no fuel.");

        return new TripPlanView(
            fromName, toName, route.DistanceNm, route.CourseDeg, depart, motorHours, sailHours, estimate, days, risks,
            SparesAboardNames(), missing, shelter, Balance, towNok, blockers,
            fuelLine, vacationDays, warnings, atDestination.Count == 0 ? null : atDestination);
    }

    /// <summary>The hour the plan assumes she casts off: now, today; 09:00 on a later day.</summary>
    private double PlanDepartHour(int depart) => depart == Day ? HourOfDay : 9.0;

    /// <summary>The day she would arrive, leaving at the planned hour and motoring for the estimated hours.</summary>
    private int ArrivalDay(int depart, double motorHours) => depart + (int)Math.Floor((PlanDepartHour(depart) + motorHours) / 24);

    /// <summary>
    /// The plan's risks that are not walls: the fuel, the dark, fog, and the vacation days. Only what the player knows: the fuel gauge, the
    /// forecast, the problems they have found. Never the true state of the lights or the tank.
    /// </summary>
    private (string? Fuel, List<string> Warnings) PassageWarnings(Route route, int depart, double motorHours, List<ForecastDay> days, out int vacationDays)
    {
        var warnings = new List<string>();
        string? fuelLine = null;
        var ci = CultureInfo.InvariantCulture;

        if (GaugeLitres() is { } aboard && _boat.Fuel.MotorLitresPerHour > 0)
        {
            double burn = _boat.Fuel.MotorLitresPerHour;
            int needs = (int)Math.Round(motorHours * burn, MidpointRounding.AwayFromZero);
            int hours = (int)Math.Round(aboard / burn, MidpointRounding.AwayFromZero);
            fuelLine = $"About {aboard} litres aboard: some {hours} hours of motoring. This passage needs about {needs} litres.";
            if (motorHours * burn > aboard) warnings.Add("The fuel aboard won't motor her all the way: fill the tank or count on the wind.");
        }

        // Daylight: leave now (today) or at nine on a later day, motoring all the way.
        double leave = PlanDepartHour(depart), arrive = leave + motorHours;
        int arrivalDay = depart + (int)Math.Floor(arrive / 24);
        double arriveHour = arrive % 24;
        {
            bool arrivesDark = IsDark(arrivalDay, arriveHour);
            bool partDark = false;
            for (double h = leave; h <= arrive && !partDark; h += 0.25)
                partDark = IsDark(depart + (int)Math.Floor(h / 24), h % 24);
            if (arrivesDark || partDark)
            {
                string text = arrivesDark
                    ? $"She'll arrive after dark (the light goes at about {LightGoes(arrivalDay)})."
                    : $"Part of the passage is in the dark (the light comes at about {LightComes(depart)}).";
                if (KnownOpenFaults().Any(f => f.ComponentId == NavLightsId))
                    text = text[..^1] + ", and you know the navigation lights have a problem: the patrol fines boats running without lights.";
                warnings.Add(text);
            }
        }

        // Fog: a wall today in the harbour (a blocker), a warning for a later day.
        if (_boat.Night.FogMatters && days.Count > 0 && days[0].Fog && depart != Day
            && !UpgradeWorks(PlotterId) && !UpgradeWorks(RadarId))
            warnings.Add($"The forecast says fog on day {depart}: without a plotter or radar the harbourmaster holds you in until it lifts, and at sea you would creep at "
                + $"{_boat.Night.FogKnots.ToString("0.#", ci)} knots.");

        // The weekdays the passage touches, none counted twice, less one already charged.
        vacationDays = 0;
        if (_boat.Calendar.ChargesDays)
        {
            var charged = new List<string>();
            bool anyWeekday = false;
            for (int d = depart; d <= arrivalDay; d++)
            {
                var clock = new GameClock(d);
                if (clock.IsWeekend) continue;
                anyWeekday = true;
                if (DayCharged(d)) continue;
                charged.Add(clock.WeekdayName);
            }
            vacationDays = charged.Count;
            if (charged.Count > 0)
                warnings.Add($"This passage costs {charged.Count} vacation day{(charged.Count == 1 ? "" : "s")} ({JoinAnd(charged)}). "
                    + (_vacationLeft >= charged.Count ? $"You have {_vacationLeft} left." : $"You have {(_vacationLeft == 0 ? "none" : _vacationLeft.ToString(ci))} left: the rest is unpaid leave."));
            else if (!anyWeekday) warnings.Add("This passage falls on a weekend: no vacation days.");
        }
        return (fuelLine, warnings);
    }

    private List<string> SparesAboardNames() =>
        _boat.Chandlery.Where(i => !i.Tool && _inventory.GetValueOrDefault(i.Id) > 0).Select(i => i.Name).Concat(PartsAboardNames()).ToList();
}
