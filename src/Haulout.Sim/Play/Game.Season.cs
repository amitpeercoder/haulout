using System.Globalization;
using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// The season (design note 008, step 1): the yard's calendar and the lift, the fouling of the hull, the chafe of the mooring lines,
/// the season line and the Season query. Nothing here draws from the world or the player's random: every new roll is seeded from
/// <see cref="SeasonSeed"/> so the same inputs give the same answer whatever else the player did.
/// </summary>
public sealed partial class Game
{
    private const string HullId = "hull";
    private const string AntifoulingId = "antifouling";
    private const string MooringLinesId = "mooring-lines";
    private const string FoulingProperty = "fouling";
    private const string ChafeProperty = "chafe";
    private const string OnDeckId = "on-deck";

    // Salts of SeasonSeed, one per purpose.
    private const int SaltYardDrift = 1;
    private const int SaltFouling = 2;
    private const int SaltChafe = 3;
    private const int SaltSpawnSpread = 4;
    private const int SaltViewing = 5;
    private const int SaltBuyerSurvey = 6;
    private const int SaltOffer = 7;
    private const int SaltLayupDraw = 8;
    // 9 is the order lead (Game.Orders.cs). 10 was the fog stream of the first 010 round, now gone. 11: close calls (Game.Passage.cs).
    private const int SaltCloseCall = 11;
    /// <summary>The own spread of a fault whose progression names its range (design note 010, second pass F).</summary>
    private const int SaltFaultSpread = 12;

    /// <summary>The day the yard has her down for launch. Starts at the data's deadline; a later round moves it.</summary>
    private int _launchDeadlineDay;
    /// <summary>The absolute day of the haulout slot the player holds; null: none.</summary>
    private int? _bookedDay;
    /// <summary>She was lifted out for the winter (the autumn lift) and has not been launched since.</summary>
    private bool _laidUp;
    /// <summary>The luck of this stretch afloat: scales the growth. Drawn at every launch.</summary>
    private double _foulingSpread = 1.0;
    /// <summary>The growth as the player last saw it (0 when there was nothing to see): all that PlanTrip may use.</summary>
    private double _foulingSeen;
    /// <summary>The growth at the last look at the waterline (0: never looked, or nothing to see): the next look says if it changed.</summary>
    private double _foulingLooked;
    /// <summary>Days afloat, and the warmest water, since the hull was last clean: the cause line of the lift.</summary>
    private int _foulingDays;
    private double _foulingWaterMaxC;
    /// <summary>The chafe the player knew of the last time they were aboard.</summary>
    private double _chafeSeen;

    /// <summary>The seeded roll of one purpose. Same world seed, salt and arguments, same answer.</summary>
    internal static int SeasonSeed(int worldSeed, int salt, int a, int b) =>
        unchecked((int)(worldSeed * 73856093 ^ salt * 19349663 ^ a * 83492791 ^ b * 2654435761L));

    private double SeasonRoll(int salt, int a, int b) => new SeededRandom(SeasonSeed(_worldSeed, salt, a, b)).NextDouble();

    private static double Between(IReadOnlyList<double> range, double roll) =>
        range.Count >= 2 ? range[0] + roll * (range[1] - range[0]) : 1.0;

    // ------------------------------------------------------------------ small state helpers

    private double Fouling => _boat.TryGet(HullId, out var hull) ? hull.State.GetValueOrDefault(FoulingProperty) : 0;

    private void SetFouling(double value)
    {
        if (_boat.TryGet(HullId, out var hull)) hull.State[FoulingProperty] = Math.Clamp(value, 0, 1);
    }

    /// <summary>For tests only (a player reads the hull, not a number): the true growth on the hull, 0 to 1.</summary>
    internal double FoulingGrowth
    {
        get => Fouling;
        set => SetFouling(value);
    }

    /// <summary>For tests only: the true chafe on the mooring lines. Setting it also sets what the player last knew.</summary>
    internal double MooringChafe
    {
        get => Chafe;
        set
        {
            if (_boat.TryGet(MooringLinesId, out var lines)) lines.State[ChafeProperty] = value;
            _chafeSeen = value;
        }
    }

    private double Chafe => _boat.TryGet(MooringLinesId, out var lines) ? lines.State.GetValueOrDefault(ChafeProperty) : 0;

    /// <summary>The player has now seen the growth: what they saw is all PlanTrip may use. Below the threshold there was nothing to see.</summary>
    /// <param name="look">The player looked at the waterline (not just scrubbed it): the next look compares with this one.</param>
    private void SeeFouling(bool look = true)
    {
        _foulingSeen = Fouling >= _boat.Fouling.ShowFrom ? Fouling : 0;
        if (look) _foulingLooked = _foulingSeen;
    }

    private void ResetFoulingCause()
    {
        _foulingDays = 0;
        _foulingWaterMaxC = 0;
    }

    /// <summary>Not under way: in port, or at anchor at sea.</summary>
    private bool NotUnderWay => _passage is null || _passage.Mode == SeaMode.Anchored;

    /// <summary>The speed after the growth on the hull has taken its share (never below half a knot while she moves at all).</summary>
    private double Slowed(double knots, double growth) =>
        knots <= 0 ? knots : Math.Max(0.5, knots - _boat.Fouling.MaxKnotsLost * growth);

    // ------------------------------------------------------------------ the calendar

    /// <summary>This year's lifting window in absolute days.</summary>
    private (int From, int To) Window(int day)
    {
        int year = new GameClock(day).Year * GameClock.DaysPerYear;
        return (year + _boat.Yard.WindowFromDayOfYear, year + _boat.Yard.WindowToDayOfYear);
    }

    /// <summary>
    /// The first slot the yard can give someone asking on <paramref name="today"/>. Before the drift starts it is the window's first day
    /// (at least minLeadDays ahead); after it, every full week of waiting pushes it later by a seeded number of days. Null: the yard is
    /// full for the autumn, or has finished lifting for the year.
    /// </summary>
    private int? FirstFreeDay(int today)
    {
        var yard = _boat.Yard;
        var (from, to) = Window(today);
        if (today > to) return null;
        var clock = new GameClock(today);
        int drift = 0;
        if (clock.DayOfYear >= yard.DriftFromDayOfYear)
        {
            int weeks = (clock.DayOfYear - yard.DriftFromDayOfYear) / 7;
            for (int w = 1; w <= weeks; w++)
                drift += (int)(SeasonRoll(SaltYardDrift, clock.Year, w) * (yard.DriftMaxDaysPerWeek + 1));
        }
        int earliest = Math.Max(from, today + yard.MinLeadDays);
        // Reputation (design note 010, step 4): a yard that knows you keeps a slot nearer, one that doesn't pushes it later; never before the earliest.
        int first = Math.Max(earliest, earliest + drift - _boat.Reputation.SlotDaysPerPoint * ReputationScore);
        return first > to ? null : first;
    }

    /// <summary>Why the yard will not take a booking (for the day, or for any day when it is null); null: it would.</summary>
    private string? BookRefusal(int? day)
    {
        if (AtSea) return "You're at sea: the yard is for when you lie at a port.";
        var (_, to) = Window(Day);
        if (Day > to) return "The yard has finished lifting for this year.";
        if (FirstFreeDay(Day) is not { } free) return "The yard is full for the autumn: the emergency lift is all that's left.";
        if (day is { } d)
        {
            if (d < Day) return $"Day {d} has already gone by.";
            if (d > to) return $"The yard stops lifting on day {to}.";
            if (d < free) return $"The yard's first free slot is day {free}.";
            if (_bookedDay == d) return $"You're already booked for day {d}.";
        }
        int fee = _bookedDay is null ? 0 : _boat.Yard.ChangeFeeNok;
        return fee > 0 ? MoneyRefusal(fee) : null;
    }

    private CommandResult DoBookHaulout(Turn t, int day)
    {
        const string action = "book-haulout";
        if (BookRefusal(day) is { } refusal) return Reject(action, refusal);
        var yard = _boat.Yard;
        int fee = _bookedDay is null ? 0 : yard.ChangeFeeNok;

        int old = _bookedDay ?? 0;
        bool change = _bookedDay is not null;
        _bookedDay = day;
        t.Narrative.Add(change
            ? $"You ring the yard and move your slot from day {old} to day {day}. The change costs {Money(fee)}."
            : $"You ring the yard and book the crane for day {day}.");
        return Accept(t, action, 0, fee, null,
            change ? $"Moved the haulout slot from day {old} to day {day}." : $"Booked a haulout slot for day {day}.");
    }

    // ------------------------------------------------------------------ the lift

    private bool IsAutumnLift() => new GameClock(Day).DayOfYear >= _boat.Yard.AutumnFromDayOfYear;

    private string? HaulOutRefusal()
    {
        if (AtSea) return "You're at sea.";
        if (YardGate() is { } away) return away;
        if (_bookedDay is not { } booked) return "You haven't booked a slot: book haulout <day>.";
        if (booked != Day) return $"Your slot is day {booked}.";
        if (_onTheHard) return "She's already ashore.";
        if (_placeId != StartPlaceId) return "You motor her round to the crane from the helm: go to the cockpit.";
        if (!_running) return "Start the engine: you motor her round to the crane.";
        if (Today.WindForce >= _boat.Rates.WeatherGates.WindForce)
            return $"The yard won't lift in this: {WeatherWords.WindPhrase(Today)} (force {Today.WindForce}). If it blows all day, your slot moves a day.";
        return null;
    }

    private CommandResult DoHaulOut(Turn t)
    {
        const string action = "haul-out";
        if (HaulOutRefusal() is { } refusal) return Reject(action, refusal);

        var yard = _boat.Yard;
        bool autumn = IsAutumnLift();
        int storage = autumn ? yard.StorageNok : 0;
        ShutDownEngine(t);
        _onTheHard = true;
        _bookedDay = null;
        _slotsKept++;
        _laidUp = autumn;
        if (autumn) _laidUpSinceDay = Day;
        t.Narrative.Add("You motor her round to the yard crane and the yard hands take your lines.");
        t.Narrative.Add("The slings go under her, and the crane lifts her out in a rush of water and sets her down in the cradle.");
        var card = LiftCard(t, yard.LiftNok, storage);
        int liftDay = Day;
        return Accept(t, action, yard.HauloutMinutes, yard.LiftNok + storage, card,
            autumn ? $"Hauled out for the winter: lift {Money(yard.LiftNok)}, storage {Money(storage)}." : $"Hauled out: lift {Money(yard.LiftNok)}.",
            // The autumn lift writes the season's page once the bill is booked: the card carries it.
            afterTick: autumn ? () => { WriteSeasonPage(t, liftDay); return card with { Lines = t.Narrative.ToList() }; } : null);
    }

    /// <summary>What the hull looks like out of the water, told in the card; the player has seen it now.</summary>
    private HauloutView LiftCard(Turn t, int liftNok, int storageNok)
    {
        SeeFouling();
        string hull = HullText();
        if (hull.Length > 0) t.Narrative.Add(hull);
        if (_laidUp) t.Narrative.Add("She is laid up on the hard for the winter.");
        return new HauloutView(Day, liftNok, storageNok, hull, t.Narrative.ToList());
    }

    /// <summary>The hull below the waterline in words, with its cause once there is growth to speak of.</summary>
    private string HullText()
    {
        var rules = _boat.Fouling;
        double growth = Fouling;
        string words = Band.Pick(rules.HullWords, growth);
        if (words.Length == 0) return "";
        string text = $"Under the waterline: {words}.";
        if (growth < rules.ShowFrom || _foulingDays <= 0) return text;
        bool thin = _boat.TryGet(AntifoulingId, out var paint) && paint.Condition < rules.PaintThinBelow;
        string temp = Math.Round(_foulingWaterMaxC, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        return $"{text} It grew over {Counted(_foulingDays, "day", "days")} afloat on {(thin ? "thin paint" : "fresh paint")}, in water that reached {temp} °C.";
    }

    // ------------------------------------------------------------------ the hull

    private CommandResult DoCleanHull(Turn t, Component hull)
    {
        var rules = _boat.Fouling;
        ShutDownEngine(t);
        bool growth = Fouling >= rules.ShowFrom;
        string text = growth
            ? "You scrape and scrub the hull from the ground, the growth coming away in green sheets and the barnacles chipped off one by one. By the end of it she is clean to the paint."
            : "You go over the hull with a brush and a bucket. There is little to take off.";
        t.Narrative.Add(text);
        if (growth) CountMaintenance();
        SetFouling(0);
        ResetFoulingCause();
        _foulingSeen = 0;
        _foulingLooked = 0;
        return Accept(t, "clean", rules.CleanMinutes, 0, new RepairView(hull.Name, text), "Cleaned the hull.");
    }

    private string? WashRefusal()
    {
        if (AtSea) return "You're at sea.";
        if (Afloat) return "She's in the water: the yard washes her on the hard.";
        if (YardGate() is { } away) return away;
        return MoneyRefusal(_boat.Yard.WashNok);
    }

    private CommandResult DoWashHull(Turn t)
    {
        const string action = "wash-hull";
        if (WashRefusal() is { } refusal) return Reject(action, refusal);
        var yard = _boat.Yard;

        ShutDownEngine(t);
        string text = Fouling >= _boat.Fouling.ShowFrom
            ? "The yard hands go over the hull with a pressure washer, and the growth comes off in a grey-green slurry. By the end she is clean to the paint."
            : "The yard hands go over the hull with a pressure washer. There was little on it to wash off.";
        t.Narrative.Add(text);
        if (Fouling >= _boat.Fouling.ShowFrom) CountMaintenance();
        SetFouling(0);
        ResetFoulingCause();
        _foulingSeen = 0;
        _foulingLooked = 0;
        return Accept(t, action, yard.WashMinutes, yard.WashNok, new RepairView(_boat.TryGet(HullId, out var hull) ? hull.Name : "Hull", text),
            "The yard washed the hull.", work: false);
    }

    private string? ScrubRefusal()
    {
        if (_onTheHard) return "Clean the hull properly from the ground.";
        if (!NotUnderWay) return "Anchor first.";
        if (_placeId is not (StartPlaceId or OnDeckId)) return "You scrub over the side: go up to the cockpit or on deck.";
        return null;
    }

    private CommandResult DoScrub(Turn t)
    {
        const string action = "scrub";
        if (ScrubRefusal() is { } refusal) return Reject(action, refusal);

        var rules = _boat.Fouling;
        ShutDownEngine(t);
        bool growth = Fouling >= rules.ShowFrom;
        t.Narrative.Add(growth
            ? "You lean over the side with a long-handled brush and scrub what you can reach at the waterline. The worst of the green comes off, but you can't get under her."
            : "You lean over the side with a long-handled brush. There is little to take off.");
        if (growth) CountMaintenance();
        SetFouling(Fouling * (1 - rules.ScrubFraction));
        SeeFouling(look: false); // she is cleaner for it, and the next look says so against the last one
        var name = _boat.TryGet(HullId, out var hull) ? hull.Name : "Hull";
        return Accept(t, action, rules.ScrubMinutes, 0, new RepairView(name, string.Join(" ", t.Narrative)), "Scrubbed the hull from the rail.");
    }

    /// <summary>Said in a look from the cockpit or on deck, afloat and not under way: the waterline as it is.</summary>
    private string? OverTheSideLine(Place place)
    {
        if (!Afloat || !NotUnderWay || place.Id is not (StartPlaceId or OnDeckId)) return null;
        var rules = _boat.Fouling;
        double before = _foulingLooked;
        SeeFouling();
        if (_foulingSeen <= 0) return null;
        var words = Band.Pick(rules.WaterlineWords, Fouling);
        if (words.Length == 0) return null;
        // The same words as last time, but the growth has moved: say which way, against the last look.
        if (before > 0 && Math.Abs(Fouling - before) > rules.CompareStep && Band.Pick(rules.WaterlineWords, before) == words)
            return $"Over the side: {words}, {(Fouling < before ? "less" : "more")} than when you last looked.";
        return $"Over the side: {words}.";
    }

    /// <summary>The antifouling cannot be painted over growth: the hull is cleaned first.</summary>
    private string? AntifoulingRefusal(Component c) =>
        c.Id == AntifoulingId && Fouling >= _boat.Fouling.PaintNeedsCleanBelow
            ? "Scrape the growth off first: clean the hull."
            : null;

    // ------------------------------------------------------------------ the day

    /// <summary>The season's part of the midnight: the growth, the chafe, the booked slot.</summary>
    private void SeasonDaily(Turn t, int day, bool present)
    {
        FoulingDaily(day);
        MooringDaily(day, present);
        LayupDaily(day);
        SlotDaily(day);
        NightsDaily();
        SaleDaily(t, day);
    }

    private void FoulingDaily(int day)
    {
        if (!Afloat) return; // on the hard nothing grows (and nothing falls off)
        var rules = _boat.Fouling;
        int lag = Math.Max(1, rules.WaterLagDays);
        double sum = 0;
        for (int d = day - lag + 1; d <= day; d++) sum += _weather[d].TempC;
        double waterC = sum / lag;
        double span = Math.Max(1e-9, rules.FullGrowthC - rules.GrowthFromC);
        double tempFactor = Math.Clamp((waterC - rules.GrowthFromC) / span, 0, 1);
        double paint = _boat.TryGet(AntifoulingId, out var antifouling) ? antifouling.Condition : 0;
        double paintFactor = 1 - rules.PaintProtection * paint;
        SetFouling(Fouling + rules.BaseGrowthPerDay * _foulingSpread * tempFactor * paintFactor);
        _foulingDays++;
        _foulingWaterMaxC = Math.Max(_foulingWaterMaxC, waterC);
    }

    private void MooringDaily(int day, bool present)
    {
        if (Afloat && !AtSea && _boat.TryGet(MooringLinesId, out var lines))
        {
            var rules = _boat.Mooring;
            var weather = _weather[day];
            if (weather.WindForce >= rules.FromForce && rules.ChafeByForce.Count > 0)
            {
                int index = Math.Min(weather.WindForce - rules.FromForce, rules.ChafeByForce.Count - 1);
                double spread = Between(rules.Spread, SeasonRoll(SaltChafe, day, 0));
                double chafe = lines.State.GetValueOrDefault(ChafeProperty) + rules.ChafeByForce[index] * spread;
                lines.State[ChafeProperty] = chafe;
                if (chafe >= rules.PartAt) PartMooring(lines);
            }
        }
        if (present) _chafeSeen = Chafe;
    }

    /// <summary>The lines let go in the night: a tow, the repair and new lines are billed (below zero allowed); nothing is lost.</summary>
    private void PartMooring(Component lines)
    {
        int tow = _boat.Trips is null ? 0 : TowPriceNok;
        _partings++;
        int repair = _boat.Mooring.PartedRepairNok;
        int newLines = ItemById(MooringLinesId)?.PriceNok ?? 0;
        int total = tow + repair + newLines;
        Spend(total, LedgerBill, "Her mooring parted: the tow, the repair and new lines.");
        lines.Replace();
        _log.Add(new LogEntry(Day, TimeOfDay,
            $"Her mooring parted in the gale. The towboat brought her back and she is on new lines: {Money(tow)} for the tow, {Money(repair)} for the repair, {Money(newLines)} for the lines.",
            total));
        Raise("mooring-parted",
            "The harbourmaster rang: she broke her mooring in the night's gale and fetched up against the next pontoon. The towboat brought her back and she's on new lines; the bill is in the logbook.");
    }

    /// <summary>At the end of the booked day: a gale moves the slot to the next day; otherwise it is forfeited.</summary>
    private void SlotDaily(int day)
    {
        if (_bookedDay != day) return;
        if (_weather[day].WindForce >= _boat.Rates.WeatherGates.WindForce)
        {
            _bookedDay = day + 1;
            Raise("slot-moved", $"The yard couldn't lift in the gale. Your slot moves to day {day + 1}.");
            return;
        }
        _bookedDay = null;
        _slotsMissed++;
        var next = FirstFreeDay(Day);
        Raise("slot-missed",
            $"You missed your haulout slot on day {day}; the yard has given it to the next boat. Book again: "
            + (next is { } n ? $"the first free slot is day {n}." : "the yard is full for the autumn."));
    }

    /// <summary>On coming back aboard after a while away: the lines have been sawing at the fairleads.</summary>
    private void MooringArrivalAlert()
    {
        if (!Afloat || AtSea) return;
        double chafe = Chafe;
        if (chafe >= _boat.Mooring.WarnFrom && chafe > _chafeSeen)
            Raise("chafe", "After the blow the bow lines creak and saw at the fairleads. Worth a look on deck.");
        _chafeSeen = chafe;
    }

    // ------------------------------------------------------------------ the season line and the query

    private string SeasonWord => new GameClock(Day).Season.ToString().ToLowerInvariant();

    private string YardSentence()
    {
        var (from, to) = Window(Day);
        var clock = new GameClock(Day);
        if (_bookedDay is { } booked)
            return booked == Day ? $"Haulout booked for today, day {booked}."
                : $"Haulout booked for day {booked}, {Days(booked - Day)} away.";
        if (FirstFreeDay(Day) is not { } free)
            return Day > to ? "The yard has finished lifting for this year."
                : "The yard is full for the autumn: the emergency lift is all that's left.";
        return clock.DayOfYear < _boat.Yard.WindowFromDayOfYear
            ? $"No haulout booked. The yard lifts boats from day {from} to day {to}; the first free slot is day {free}."
            : $"No haulout booked. The first free slot is day {free}; the yard lifts until day {to}.";
    }

    /// <summary>
    /// What the readiness list leads with once she has been launched: the booked slot, or the first free one; the winter list when laid up;
    /// nothing after a Keep (the deadline is next) or before the first launch (the deadline is all there is).
    /// </summary>
    private string? ReadinessSeasonLead()
    {
        if (_sold) return "She's sold.";
        if (!_launched || _sunk) return null;
        if (_kept) return null;
        if (_laidUp)
        {
            var tasks = LayupTasks();
            int done = tasks.Count(x => x.Done);
            var left = tasks.Where(x => !x.Done).Select(x => x.Name).ToList();
            return left.Count == 0
                ? $"Winter: all {tasks.Count} tasks done."
                : $"Winter: {done} of {tasks.Count} tasks done. Left: {string.Join("; ", left)}.";
        }
        if (_bookedDay is { } booked) return $"Haulout booked for day {booked}.";
        var (from, to) = Window(Day);
        if (FirstFreeDay(Day) is { } free)
            return $"No slot booked: the yard lifts from day {from} to day {to}; the first free slot is day {free}.";
        return Day > to ? "No slot booked: the yard has finished lifting for this year."
            : "No slot booked: the yard is full for the autumn; the emergency lift is all that's left.";
    }

    private string SeasonLine()
    {
        string season = SeasonWord;
        string head = $"{char.ToUpperInvariant(season[0])}{season[1..]}, year {new GameClock(Day).Year + 1}";
        string body;
        if (_sold) return $"{head}. She's sold.";
        if (_sunk) body = "She is on the bottom.";
        else if (_laidUp) body = "She is laid up on the hard for the winter.";
        else if (_onTheHard && !_launched)
            body = $"She stands on the hard. Launch is set for day {_launchDeadlineDay}." + (_bookedDay is null ? "" : " " + YardSentence());
        else if (_onTheHard) body = "She is out of the water, on the hard. " + YardSentence();
        else body = YardSentence();
        if (_listed) body += " " + ListingSentence();
        return $"{head}. {body}";
    }

    private SeasonView SeasonSnapshot()
    {
        var clock = new GameClock(Day);
        var yard = _boat.Yard;
        var (from, to) = Window(Day);
        return new SeasonView(
            Day, clock.Year + 1, clock.DayOfYear, SeasonWord, SeasonLine(), Afloat, _laidUp, _launchDeadlineDay,
            _bookedDay, _sunk ? null : FirstFreeDay(Day), from, to, yard.LiftNok, yard.StorageNok, yard.ChangeFeeNok,
            _boat.Economy.EmergencyLiftNok, _launched && _onTheHard, LayupTasks(), _listed, _listed ? _askNok : null, OpenViewings().Count(), _kept,
            SeasonActions(), UpgradeViews(), ReputationWords);
    }

    // ------------------------------------------------------------------ what the facade would accept now

    private string? CleanHullRefusal(string name = "hull")
    {
        if (!_seen.Contains(HullId)) return $"You haven't found anything called '{name}' yet.";
        var hull = _boat.Get(HullId);
        return SeaWorkRefusal() ?? AfloatRefusal(hull) ?? _reach.Refusal(hull, _placeId, true) ?? WetRefusal(hull);
    }

    /// <summary>
    /// The season's verbs with the refusal each would give now, from the same helpers the commands use. Once she is sold or sunk, every one
    /// says what every command says.
    /// </summary>
    private IReadOnlyList<SeasonAction> SeasonActions()
    {
        var rules = _boat.Layup;
        var yard = _boat.Yard;
        string? over = _sunk ? SunkMessage : _sold ? SoldMessage : null;
        SeasonAction A(string verb, string label, string? reason, int minutes, int nok) =>
            new(verb, label, (over ?? reason) is null, over ?? reason, minutes, nok);
        int fill = TankCapacity > 0 ? (int)Math.Ceiling(Math.Max(0, TankCapacity - FuelLitres) * rules.DieselNokPerLitre) : 0;
        return
        [
            A("wait-until-spring", "Wait until spring", WaitUntilSpringRefusal(), 0, 0),
            A("keep", "Keep", KeepRefusal(), 0, 0),
            A("book-haulout", "Book haulout", BookRefusal(null), 0, _bookedDay is null ? 0 : yard.ChangeFeeNok),
            A("haul-out", "Haul out", HaulOutRefusal(), yard.HauloutMinutes, yard.LiftNok + (IsAutumnLift() ? yard.StorageNok : 0)),
            A("scrub", "Scrub", ScrubRefusal(), _boat.Fouling.ScrubMinutes, 0),
            A("wash-hull", "Yard wash", WashRefusal(), yard.WashMinutes, yard.WashNok),
            A("clean-hull", "Clean hull", CleanHullRefusal(), _boat.Fouling.CleanMinutes, 0),
            A("winterise-engine", "Winterise the engine", WinteriseRefusal(), rules.WinteriseMinutes, rules.AntifreezeNok),
            A("fill-tank", "Fill the tank", FillRefusal(), rules.FillMinutes, fill),
            A("battery-ashore", "Battery ashore", BatteryAshoreRefusal(), rules.BatteryMinutes, rules.BatteryRoomNok),
            A("battery-aboard", "Battery aboard", BatteryAboardRefusal(), rules.BatteryMinutes, 0),
            A("winter-charger", "Charger", ChargerRefusal(), rules.ChargerMinutes, rules.ChargerNok),
            A("fit-winter-cover", "Fit the winter cover", CoverRefusal(), rules.CoverMinutes, _coverOwned ? 0 : rules.CoverNok),
            A("list-for-sale", "List for sale", ListRefusal(null), _boat.Sale.ListMinutes, 0),
            A("accept-offer", "Accept offer", AcceptRefusal(null), 0, 0),
        ];
    }
}
