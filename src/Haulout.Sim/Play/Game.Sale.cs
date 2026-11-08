using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Time;

namespace Haulout.Sim.Play;

/// <summary>
/// The sale (design note 008, step 3): the broker's valuation letter, the listing, the buyers who come to look, the sale and its closing
/// card, and Keep. The boat is worth what a buyer can see and what their surveyor will find; nothing is hidden and nothing is invented.
/// Every roll is seeded from <see cref="SeasonSeed"/> (salts 5, 6 and 7), never from the world or the player's random.
/// </summary>
public sealed partial class Game
{
    private const string SoldMessage = "She's sold. The logbook is all that's left of her for you.";

    /// <summary>The surveyor of a buyer finds only what the player's own tools could: these are the verbs that can discover a fault.</summary>
    private static readonly PlayerAction[] BuyerSurveyActions =
        [PlayerAction.Look, PlayerAction.Inspect, PlayerAction.Measure, PlayerAction.CheckFilterBowl, PlayerAction.CheckRawWaterFlow];

    /// <summary>The commands that always make a maintenance entry in the logbook (the premium counts them). Clean, Clean hull, the yard wash and Scrub count only when they changed something: they call <c>CountMaintenance</c> themselves.</summary>
    private static readonly HashSet<string> MaintenanceActions =
    [
        "replace", "yard-fix", "winterise-engine", "fill-tank", "battery-ashore", "battery-aboard", "winter-charger",
        "fit-winter-cover",
    ];

    private sealed class Viewing
    {
        public int Number { get; init; }
        public int Day { get; init; }
        public List<string> Findings { get; init; } = [];
        public int ValueNok { get; init; }
        public int OfferNok { get; init; }
        public int HoldsUntilDay { get; init; }
        /// <summary>The listing was withdrawn, or the offer was taken.</summary>
        public bool Closed { get; set; }
    }

    /// <summary>A problem a valuation counts: the line in the letter, the sentence in the buyer's notes, and the price.</summary>
    private sealed record Finding(string Line, string Sentence, int Nok);

    private bool _listed;
    private int _askNok;
    private bool _sold;
    private bool _kept;
    private int _incomeNok;
    private readonly List<Viewing> _viewings = [];

    /// <summary>How many times she has been kept: 0 in the first season. Seasons are what the engine service entries are counted by.</summary>
    private int _seasonIndex;
    private readonly HashSet<int> _serviceSeasons = [];
    private int _maintenanceEntries;

    private int _trips;
    private int _tows;
    private string? _firstAwayLine;
    private string? _firstCauseLine;
    private string? _firstTowLine;

    private void CountMaintenance() => _maintenanceEntries++;

    private bool CountsAsMaintenance(string action) => MaintenanceActions.Contains(action);

    private void CountArrival(Passage p, Port port)
    {
        if (p.TurnedBack) return;
        _trips++;
        if (port.Id != _boat.Trips?.Home)
            _firstAwayLine ??= $"Day {DayAt(p.EndClockMinutes)}: Arrived at {port.Name}.";
    }

    private void CountTow(string text)
    {
        _tows++;
        _firstTowLine ??= $"Day {Day}: {text}";
    }

    // ------------------------------------------------------------------ the valuation

    private int UndocumentedYears() =>
        _boat.Sale.UndocumentedYearsAtPurchase + Enumerable.Range(0, _seasonIndex + 1).Count(k => !_serviceSeasons.Contains(k));

    private static string Basis(Fault f) =>
        f.Yard is not null ? "at the yard" : f.Diy is not null ? "in parts" : "to put right later";

    private static Finding FindingOf(Fault f)
    {
        int nok = f.Yard?.Nok ?? f.Diy?.PartsNok ?? f.CostLaterNok;
        string line = f.Yard is not null ? $"{f.Name}, at the yard's price"
            : f.Diy is not null ? $"{f.Name}, at the price of the parts"
            : $"{f.Name}, at what it costs to leave it";
        return new Finding(line, $"{f.Name}: {nok} NOK {Basis(f)}.", nok);
    }

    private Finding FoulingFinding()
    {
        int wash = _boat.Yard.WashNok;
        return new Finding("Heavy growth on the hull, at the yard's wash", $"Heavy growth under the waterline: {wash} NOK for the yard's wash.", wash);
    }

    /// <summary>The problems the player knows are open, and heavy growth only if they have seen it. Never anything else.</summary>
    private List<Finding> KnownFindings()
    {
        var found = KnownOpenFaults().Select(FindingOf).ToList();
        if (_foulingSeen >= _boat.Sale.FoulingFindFrom) found.Add(FoulingFinding());
        return found;
    }

    /// <summary>The value of the boat for a list of findings, on <paramref name="day"/>, and the lines that add up to it.</summary>
    private (int Value, List<ValuationLine> Lines) Valuate(int day, IReadOnlyList<Finding> findings)
    {
        var rules = _boat.Sale;
        var lines = new List<ValuationLine> { new("Base value of a 28-footer of her kind", rules.BaseNok) };
        foreach (var f in findings) lines.Add(new ValuationLine(f.Line, -f.Nok));

        int years = UndocumentedYears();
        if (years > 0)
            lines.Add(new ValuationLine(
                Capital($"{Counted(years, "year", "years")} of engine hours with no service entry, at {rules.PerUndocumentedYearNok} NOK each"),
                -years * rules.PerUndocumentedYearNok));
        int entries = Math.Min(rules.EntriesCap, _maintenanceEntries);
        if (entries > 0)
            lines.Add(new ValuationLine(
                $"A kept logbook: {Counted(entries, "maintenance entry", "maintenance entries")} this season, at {rules.PerEntryNok} NOK each",
                entries * rules.PerEntryNok));

        int total = lines.Sum(l => l.Nok);
        int month = new GameClock(day).Month;
        if (month is >= 2 and <= 5)
        {
            int bonus = (int)Math.Round(total * rules.SpringBonus, MidpointRounding.AwayFromZero);
            lines.Add(new ValuationLine($"Sold in spring: {Percent(rules.SpringBonus)} on the price", bonus));
            total += bonus;
        }
        else if (month >= 8 || month <= 1)
        {
            int discount = (int)Math.Round(total * rules.AutumnDiscount, MidpointRounding.AwayFromZero);
            lines.Add(new ValuationLine($"Sold in autumn or winter: {Percent(-rules.AutumnDiscount)} on the price", -discount));
            total -= discount;
        }

        int floor = (int)Math.Round(rules.BaseNok * rules.FloorFraction, MidpointRounding.AwayFromZero);
        if (total < floor)
        {
            lines.Add(new ValuationLine("Up to the broker's floor: no buyer pays less than this for a boat of her kind", floor - total));
            total = floor;
        }
        return (total, lines);
    }

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string Percent(double fraction) =>
        (fraction >= 0 ? "+" : "-") + (Math.Abs(fraction) * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " %";

    private ValuationLetter BuildLetter(int day)
    {
        var (value, lines) = Valuate(day, KnownFindings());
        return new ValuationLetter(day, _boat.Sale.Broker, lines, value, _listed ? _askNok : null,
            ["A buyer's surveyor will go over her for themselves."]);
    }

    // ------------------------------------------------------------------ the listing

    private IEnumerable<Viewing> OpenViewings() =>
        _sold ? [] : _viewings.Where(v => !v.Closed && Day <= v.HoldsUntilDay);

    private string ListingSentence()
    {
        int open = OpenViewings().Count();
        string offers = open == 0 ? "" : open == 1 ? ", with an offer open" : $", with {Counted(open, "offer", "offers")} open";
        return $"She is listed for sale at {_askNok} NOK{offers}.";
    }

    private void WithdrawListing()
    {
        _listed = false;
        foreach (var v in _viewings) v.Closed = true;
    }

    private string? ListRefusal(int? askNok)
    {
        if (YardGate() is { } away) return away;
        if (Afloat) return "The broker sells her from the hard.";
        if (askNok <= 0) return "Name a price above zero: list for sale <nok>.";
        return null;
    }

    private CommandResult DoListForSale(Turn t, int askNok)
    {
        const string action = "list-for-sale";
        if (ListRefusal(askNok) is { } refusal) return Reject(action, refusal);

        var rules = _boat.Sale;
        ShutDownEngine(t);
        bool change = _listed;
        _listed = true;
        _askNok = askNok;
        var letter = BuildLetter(Day);
        t.Narrative.Add(change
            ? $"You ring {rules.Broker} and change the asking price to {askNok} NOK."
            : $"You ring {rules.Broker}. Someone comes down to the yard, walks round her with a clipboard and sends you their valuation letter. You list her at {askNok} NOK.");
        return Accept(t, action, rules.ListMinutes, 0, new ListingView(letter),
            change
                ? $"Changed the asking price to {askNok} NOK. The broker values her at {letter.ValueNok} NOK."
                : $"Listed her for sale with {rules.Broker} at {askNok} NOK. The broker values her at {letter.ValueNok} NOK.");
    }

    /// <summary>At launch: she is off the market, and so are the offers. Also clears the "kept" mark: a new stretch afloat has begun.</summary>
    private void LaunchSaleLines(Turn t)
    {
        _kept = false;
        if (!_listed) return;
        WithdrawListing();
        t.Narrative.Add("You tell the broker she's going back in the water: the listing is withdrawn, and the offers with it.");
        _log.Add(new LogEntry(Day, TimeOfDay, "Took her off the market: the listing is withdrawn."));
    }

    // ------------------------------------------------------------------ the buyers

    /// <summary>The midnight's part of the market: offers lapse by themselves (an offer is open to its day), then at most one viewing.</summary>
    private void SaleDaily(Turn t, int day)
    {
        if (!_listed || !_onTheHard || _sold) return;
        var rules = _boat.Sale;
        int letterValue = BuildLetter(day).ValueNok;
        double gap = letterValue <= 0 ? 0 : (double)(letterValue - _askNok) / letterValue;
        double sensitivity = Math.Clamp(1 + rules.AskSensitivity * gap, 0.1, 2.0);
        string season = new GameClock(day).Season.ToString().ToLowerInvariant();
        double seasonal = rules.SeasonViewingFactor.GetValueOrDefault(season, 1.0);
        double chance = Math.Min(1.0, rules.ViewingChancePerDay * sensitivity * seasonal);
        if (SeasonRoll(SaltViewing, day, 0) >= chance) return;

        // The buyer's surveyor goes over her with the player's own tools and finds what they would find.
        const string stamp = "12:00";
        int from = t.Newly.Count;
        var findings = new List<Finding>();
        var faults = _boat.AllFaults.ToList();
        for (int i = 0; i < faults.Count; i++)
        {
            var f = faults[i];
            if (!f.Present || f.Harmless || !f.Reveals.Any(r => r.Discovers && BuyerSurveyActions.Contains(r.Action))) continue;
            if (SeasonRoll(SaltBuyerSurvey, day, i) >= rules.FindChance) continue;
            findings.Add(FindingOf(f));
            Discover(t, f);
        }
        if (Fouling >= rules.FoulingFindFrom)
        {
            findings.Add(FoulingFinding());
            SeeFouling();
        }
        LogFound(t, from, day, stamp);

        var (value, _) = Valuate(day, findings);
        double factor = Between(rules.OfferSpread, SeasonRoll(SaltOffer, day, 0));
        int offer = Math.Min(_askNok, (int)Math.Round(value * factor / 100.0, MidpointRounding.AwayFromZero) * 100);
        var viewing = new Viewing
        {
            Number = _viewings.Count + 1, Day = day, Findings = findings.Select(f => f.Sentence).ToList(),
            ValueNok = value, OfferNok = offer, HoldsUntilDay = day + rules.OfferHoldsDays,
        };
        _viewings.Add(viewing);

        string found = findings.Count == 0 ? "found nothing wrong" : $"found {Counted(findings.Count, "problem", "problems")}";
        _log.Add(new LogEntry(day, stamp,
            $"A buyer looked her over. Their surveyor {found}, and they offer {offer} NOK, open until day {viewing.HoldsUntilDay}.",
            0, $"viewing {viewing.Number}"));
        Raise("viewing",
            $"The broker rang: a buyer looked her over on day {day} and offers {offer} NOK, open until day {viewing.HoldsUntilDay}. Their surveyor's notes are in the logbook ('sale').");
    }

    // ------------------------------------------------------------------ the sale

    private string? AcceptRefusal(int? number)
    {
        if (AtSea) return "You're at sea.";
        if (!_listed) return "She isn't listed: list for sale <nok>.";
        var open = OpenViewings().ToList();
        if (number is { } n)
        {
            var chosen = _viewings.FirstOrDefault(v => v.Number == n);
            if (chosen is null) return $"There's no offer number {n}.";
            return open.Contains(chosen) ? null : $"Offer {n} has lapsed.";
        }
        return open.Count == 0 ? "There's no offer open." : null;
    }

    private CommandResult DoAcceptOffer(Turn t, int? number)
    {
        const string action = "accept-offer";
        if (AcceptRefusal(number) is { } refusal) return Reject(action, refusal);
        var open = OpenViewings().ToList();
        var chosen = number is { } n
            ? _viewings.First(v => v.Number == n)
            : open.OrderByDescending(v => v.OfferNok).ThenByDescending(v => v.Number).First();

        ShutDownEngine(t);
        _incomeNok += chosen.OfferNok;
        _sold = true;
        WithdrawListing();
        var card = ClosingCard(chosen);
        t.Narrative.Add("You shake hands with the buyer on the quay and sign the bill of sale. She's theirs.");
        t.Narrative.AddRange(card.Lines);
        return Accept(t, action, 0, 0, card, $"Sold her for {chosen.OfferNok} NOK.", work: false);
    }

    private ClosingView ClosingCard(Viewing sold)
    {
        int days = Day - _startDay;
        int hours = (int)Math.Round(_time.WorkMinutes / 60.0, MidpointRounding.AwayFromZero);
        var lines = new List<string>
        {
            $"Sold on day {Day} for {sold.OfferNok} NOK, to the buyer who viewed her on day {sold.Day}.",
            $"You spent {_money} NOK on her over {Days(days)}, and {hours} {(hours == 1 ? "hour" : "hours")} of your own work.",
            $"Trips made: {_trips}. Tows: {_tows}.",
        };
        foreach (var earned in new[] { _firstAwayLine, _firstCauseLine, _firstTowLine })
            if (earned is not null) lines.Add(earned);
        return new ClosingView(Day, sold.OfferNok, _money, Balance, days, hours, _trips, _tows, lines);
    }

    // ------------------------------------------------------------------ keeping her

    private string? KeepRefusal()
    {
        if (AtSea) return "You're at sea.";
        if (!_launched) return "You haven't had a season with her yet.";
        if (_kept) return $"You've kept her already: the yard has her down for launch on day {_launchDeadlineDay}.";
        return null;
    }

    private CommandResult DoKeep(Turn t)
    {
        const string action = "keep";
        if (KeepRefusal() is { } refusal) return Reject(action, refusal);

        if (_listed)
        {
            WithdrawListing();
            t.Narrative.Add("You ring the broker and take her off the market.");
        }
        int doy = _boat.Economy.LaunchDeadlineDay % GameClock.DaysPerYear;
        int deadline = Day - Day % GameClock.DaysPerYear + doy;
        if (deadline <= Day) deadline += GameClock.DaysPerYear;
        _launchDeadlineDay = deadline;
        _seasonIndex++;
        _maintenanceEntries = 0;
        _kept = true;
        t.Narrative.Add($"You keep her. The yard has her down for launch on day {deadline}.");
        return Accept(t, action, 0, 0, null, $"Kept her for another season. Launch is set for day {deadline}.", work: false);
    }

    // ------------------------------------------------------------------ the query

    private SaleView SaleSnapshot()
    {
        var viewings = _viewings.Select(v => new ViewingReport(
            v.Number, v.Day, v.Findings, v.ValueNok, v.OfferNok, v.HoldsUntilDay, !_sold && !v.Closed && Day <= v.HoldsUntilDay)).ToList();
        var letter = _sunk || _sold ? null : BuildLetter(Day);
        return new SaleView(_listed, _listed ? _askNok : null, letter, viewings, _sold);
    }
}
