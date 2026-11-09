using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haulout.Sim.Components;
using Haulout.Sim.Faults;

namespace Haulout.Sim.Data;

public static class BoatLoader
{
    private const string Sloop28Resource = "Haulout.Sim.Data.sloop28.json";
    private const string EconomyResource = "Haulout.Sim.Data.economy.json";
    private const string TicketsResource = "Haulout.Sim.Data.tickets.json";
    private const string BoardResource = "Haulout.Sim.Data.board.json";
    private const string LadderResource = "Haulout.Sim.Data.ladder.json";
    private const string QuestionsResource = "Haulout.Sim.Data.questions.json";

    /// <summary>
    /// The knobs economy.json owns (design note 010, second pass A): a boat file that still holds one is refused, so the two never disagree.
    /// Each entry is a path of object keys; the last key is the knob (or the whole section when it is the only key).
    /// </summary>
    private static readonly string[][] EconomyKnobs =
    [
        ["economy"], ["survey", "nokPerFoot"],
        ["yard", "liftNok"], ["yard", "storageNok"], ["yard", "changeFeeNok"], ["yard", "washNok"],
        ["trips", "tow", "nok"], ["mooring", "partedRepairNok"], ["layup", "dieselNokPerLitre"], ["closeCalls", "letterNok"], ["radio", "unlicensedLetterNok"],
        ["orders", "leadDays"],
        ["calendar", "eveningMinutes"], ["calendar", "vacationDaysPerYear"], ["calendar", "unpaidLeaveNok"], ["calendar", "payNok"],
        ["calendar", "overdraftLimitNok"], ["calendar", "interestPerMonth"], ["calendar", "bills"],
    ];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <param name="withCalendar">
    /// false: the boat as it was before design note 010. No pay, no bills, no vacation days, no overdraft limit, and every part on the
    /// yard's shelf (no orders). For tests whose subject is not the week or the money.
    /// </param>
    /// <param name="withHours">
    /// true: the shops' hours and the skipper's tiredness of economy.json (design note 012). Off by default, as the calendar's knobs
    /// were: a boat written in a test plays with shops always open and no tiredness; the shipped game (<c>Game.NewFirstFault</c>) turns it on.
    /// </param>
    public static Boat LoadSloop28(bool withCalendar = true, bool withHours = false) =>
        Load(ReadResource(Sloop28Resource), withCalendar, null, withHours);

    private static string ReadResource(string name)
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The world's economy, from the embedded economy.json.</summary>
    public static EconomyData LoadEconomy() => ParseEconomy(ReadResource(EconomyResource));

    public static EconomyData ParseEconomy(string json) =>
        JsonSerializer.Deserialize<EconomyData>(json, Options) ?? throw new InvalidOperationException("Economy JSON is empty.");

    /// <param name="economyJson">
    /// The economy file's text. Null: the embedded economy.json, so a boat written inline in a test is played in the world's economy
    /// (the same numbers the knobs held in the boat file before design note 010, second pass A).
    /// </param>
    public static Boat Load(string json, bool withCalendar = true, string? economyJson = null, bool withHours = false, string? boardJson = null)
    {
        RefuseEconomyKnobs(json);
        var eco = economyJson is null ? LoadEconomy() : ParseEconomy(economyJson);
        var dto = JsonSerializer.Deserialize<BoatDto>(json, Options)
            ?? throw new InvalidOperationException("Boat JSON is empty.");

        var places = dto.Places.Select(p => new Place(
            p.Id, p.Name, p.Aliases ?? [], p.Description ?? "", p.Way ?? "", p.Adjacent ?? [], p.OnlyOnTheHard, p.Exposed));
        var covers = dto.Covers.Select(c => new Cover(
            c.Id, c.Name, c.Aliases ?? [], c.Place, c.Plural, c.RemoveMinutes, c.RefitMinutes,
            c.RemoveText ?? "", c.RefitText ?? "", c.StartsOff, c.OffPhrase ?? "", c.StateWords is { Count: 2 } ? c.StateWords : null,
            c.RemoveLog ?? "", c.RefitLog ?? ""));
        var positions = dto.Positions.Select(p => new Position(p.Id, p.Label));

        var chandlery = dto.Chandlery ?? [];
        var kinds = BuildKinds(dto.Kinds);
        Component BuildComponent(ComponentDto c)
        {
            // Decision 007/1: a part the chandlery sells costs what the chandlery charges. The price lives on the shop item;
            // the component's replace price and the parts of a DIY fix by replacing it are taken from it.
            // An upgrade's replace price is the price of the item that fits it (design note 010, step 3).
            var part = chandlery.FirstOrDefault(i => !i.Tool && (i.ForComponent == c.Id || i.Upgrade == c.Id));
            int replaceCostNok = PartPrice(part, c.ReplaceCostNok, $"component '{c.Id}' replaceCostNok");
            var kind = ResolveKind(c, kinds, replaceCostNok);
            var spec = new ComponentSpec(
                c.Subsystem, c.Position,
                (c.Reach ?? []).Select(r => new ReachEntry(r.Place, r.Cover, r.Visible, r.Reachable)).ToList(),
                c.Aliases ?? [], c.Description ?? "",
                c.SeriesOhms, kind, replaceCostNok, c.ReplaceMinutes,
                new Dictionary<string, double>(c.Properties ?? new()),
                c.RemovablePart is { } rp ? new RemovablePart(rp.Name, rp.Aliases ?? [], rp.UncoveredText ?? "", rp.UncoverText, rp.RefitText) : null,
                c.BelowWaterline, c.ReplaceText, c.NeedsDry, c.Stocked, c.NoPoorJob, c.Aloft);
            var wear = WearRate.CalendarYears(c.WearYears > 0 ? c.WearYears : 10, c.WearPerUseHour);
            var faults = (c.Faults ?? []).Select(f => new Fault(
                f.Id, f.Name, c.Id, f.Effect ?? new FaultEffect(FaultEffectKind.None, 0),
                f.Reveals ?? [], f.FixedBy ?? [], f.Present ?? true,
                f.OnHardOnly, f.Harmless, f.Dormant, f.Consequence ?? "", f.CostLaterNok, f.LaterNote,
                f.Diy is { } d
                    ? new DiyRoute(d.How,
                        (f.FixedBy ?? []).Contains(PlayerAction.Replace)
                            ? PartPrice(part, d.PartsNok, $"fault '{f.Id}' diy partsNok")
                            : d.PartsNok ?? 0,
                        d.Minutes)
                    : null,
                f.Yard is { } y ? new YardRoute(y.How, y.Nok, y.LeadDays) : null,
                f.Escalation is { } e ? new Escalation(e.To, e.OnLaunch, e.Text ?? "") : null,
                f.Properties is null ? null : new Dictionary<string, double>(f.Properties),
                f.Progression is { } pr ? new ProgressionSpec(pr.BaseRatePerDay, pr.Factors ?? [], pr.ExposedBy ?? [], pr.RainOnlyWhenOff ?? [], pr.Spread is { Count: 2 } ps ? ps : null) : null,
                f.Effects, ParseSpawn(f.Spawn), f.ArrivalSign?.Text,
                f.AtSea is { } asp ? new AtSeaSpec(asp.Text, asp.Spare) : null,
                f.FailsWhen is { Length: > 0 } fw ? (fw is "rain" ? fw : throw new InvalidOperationException($"Fault '{f.Id}' has failsWhen '{fw}'.")) : null,
                f.Symptom));
            return new Component(c.Id, c.Name, c.Condition, wear, spec, faults);
        }
        var components = dto.Components.Select(BuildComponent).ToList();

        var upgrades = (dto.Upgrades ?? []).Select(u =>
        {
            var comp = BuildComponent(u.Component ?? throw new InvalidOperationException("An upgrade needs a component."));
            if (!chandlery.Any(i => i.Upgrade == comp.Id))
                throw new InvalidOperationException($"Upgrade '{comp.Id}' has no chandlery item that fits it.");
            if (chandlery.Count(i => i.Upgrade == comp.Id) > 1)
                throw new InvalidOperationException($"Upgrade '{comp.Id}' has more than one chandlery item.");
            return new UpgradeRule
            {
                Component = comp, PlaceId = u.Place, InstallMinutes = u.InstallMinutes, YardNok = u.YardNok, YardDays = u.YardDays,
                ValueNok = u.ValueNok, Opens = u.Opens ?? "", InstallText = u.InstallText ?? "", Replaces = u.Replaces ?? [],
                Also = (u.Also ?? []).Select(BuildComponent).ToList(),
            };
        }).ToList();
        // An upgrade may take the place of components the boat has from the start (design note 010, second pass F), each by at most one upgrade.
        var replacedBy = new Dictionary<string, string>();
        foreach (var u in upgrades)
            foreach (var old in u.Replaces)
            {
                if (dto.Components.All(c => c.Id != old))
                    throw new InvalidOperationException($"Upgrade '{u.Component.Id}' replaces '{old}', which is not a component of the boat.");
                if (!replacedBy.TryAdd(old, u.Component.Id))
                    throw new InvalidOperationException($"Component '{old}' is replaced by both '{replacedBy[old]}' and '{u.Component.Id}'.");
            }
        foreach (var item in chandlery.Where(i => i.Upgrade is not null))
            if (upgrades.All(u => u.Component.Id != item.Upgrade))
                throw new InvalidOperationException($"Chandlery item '{item.Id}' fits '{item.Upgrade}', which is not an upgrade.");

        var economy = new Economy(
            eco.Start.StartingBalanceNok, eco.Start.LaunchDeadlineDay, eco.EmergencyLift.Nok, eco.Start.LaunchMinutes, eco.EmergencyLift.Days);
        var survey = dto.Survey is { } sv
            ? new SurveyData(
                eco.Survey.NokPerFoot, sv.LengthFeet, sv.Days, sv.Surveyor, sv.Opening ?? [], sv.Closing ?? [],
                (sv.Items ?? []).Select(i => new SurveyItemData(i.Component, i.Severity, i.Fault, i.Finding, i.Text)).ToList())
            : null;
        var quality = dto.DiyQuality is { } dq
            ? new DiyQuality(
                dq.StartSkill, dq.Threshold, dq.PoorJobChance,
                (dq.PoorJobs ?? new()).ToDictionary(
                    kv => kv.Key,
                    kv => new PoorJobTemplate(
                        kv.Value.Name, kv.Value.Reveal, kv.Value.Consequence, kv.Value.CostLaterNok,
                        new DiyRoute(kv.Value.Diy.How, kv.Value.Diy.PartsNok ?? 0, kv.Value.Diy.Minutes),
                        new YardRoute(kv.Value.Yard.How, kv.Value.Yard.Nok, kv.Value.Yard.LeadDays)))
                    .ToDictionary(kv => Enum.Parse<Subsystem>(kv.Key, ignoreCase: true), kv => kv.Value))
            : null;

        var rates = dto.Rates is { } rt
            ? new Rates(
                rt.Threshold, rt.Spread is { Count: 2 } sp ? sp[0] : 0.6, rt.Spread is { Count: 2 } sp2 ? sp2[1] : 1.6,
                new ExposureRates(rt.Exposure?.RainWeight ?? 0, rt.Exposure?.RainCapMm ?? 1, rt.Exposure?.FrostWeight ?? 0, rt.Exposure?.PerOpenCover ?? 0),
                new UseRates(rt.Use?.PerEngineHour ?? 0),
                new NeglectRates(
                    rt.Neglect?.PerKnownOpenFault ?? 0, rt.Neglect?.BatteryLow ?? 0, rt.Neglect?.PerCoverLeftOff ?? 0,
                    rt.Neglect?.PerDayAwayBeyond3 ?? 0, rt.Neglect?.Covers ?? []),
                new WeatherGates(rt.WeatherGates?.WindForce ?? 7, rt.WeatherGates?.RainMm ?? 0.5))
            : null;

        var water = dto.Water is { } w
            ? new WaterRules(
                w.StartLitres, w.SoleLitres, w.SinkLitres, w.RainThroughOpenHatchLitresPerMm,
                w.ManualPumpLitresPerMinute, w.ManualPumpMinutes, w.SoleText ?? WaterRules.Default.SoleText,
                w.LevelWords is { Count: > 0 } words ? words.Select(x => new LevelWord(x.UnderLitres, x.Text)).ToList() : WaterRules.Default.LevelWords,
                w.HarbourmasterLitres ?? w.SoleLitres, w.HarbourmasterText ?? WaterDefaults.HarbourmasterText)
            : null;
        var electrical = dto.Electrical is { } el ? new ElectricalRules(el.SelfDischargePerDay, el.PumpMinStateOfCharge, el.CrankDraw, el.ChargePerHour, el.RunDrawPerHour) : null;
        var insurance = dto.Insurance is { } ins ? new InsuranceRules(ins.ValueNok, ins.PayoutFraction) : null;

        foreach (var port in dto.Trips?.Ports ?? [])
            foreach (var id in port.Stock ?? [])
                if (!chandlery.Any(i => i.Id == id))
                    throw new InvalidOperationException($"Port '{port.Id}' stocks '{id}', which is not in the chandlery.");

        var trips = dto.Trips is { } tr
            ? new TripRules(
                tr.Home, tr.Ports ?? [], DeriveRoutes(tr.Ports ?? [], tr.Routes ?? []), (tr.Tow ?? new TowRules()) with { Nok = eco.Tow.Nok })
            : null;

        // withCalendar false: no pay, no bills, no vacation limit, no overdraft limit (the calendar's money knobs stay at their off defaults) and no orders.
        var calendar = withCalendar
            ? (dto.Calendar ?? CalendarRules.Default) with
            {
                EveningMinutes = eco.Calendar.EveningMinutes,
                VacationDaysPerYear = eco.Calendar.VacationDaysPerYear,
                UnpaidLeaveNok = eco.Calendar.DerivedUnpaidLeaveNok(),
                PayNok = eco.Calendar.PayNok,
                OverdraftLimitNok = eco.Calendar.OverdraftLimitNok,
                InterestPerMonth = eco.Calendar.InterestPerMonth,
                Bills = eco.Calendar.Bills,
            }
            : null;
        foreach (var e in calendar?.Events ?? [])
        {
            if (trips?.FindPort(e.Port) is null)
                throw new InvalidOperationException($"Event '{e.Id}' is at port '{e.Port}', which is not in the trips.");
            if (e.Weekday is { Length: > 0 } wd && !new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" }.Contains(wd.ToLowerInvariant()))
                throw new InvalidOperationException($"Event '{e.Id}' has the weekday '{wd}'.");
            foreach (var r in e.Requires)
            {
                if (r.Kind is not ("fitted" or "working" or "anyWorking" or "dryBunks" or "warm"))
                    throw new InvalidOperationException($"Event '{e.Id}' has a requirement of the kind '{r.Kind}'.");
                if (r.Kind == "anyWorking")
                {
                    // Alternatives, each one component or several joined by '+' that must all work (design note 010, second pass F).
                    if (r.Components.Count == 0 || r.Id.Length == 0)
                        throw new InvalidOperationException($"Event '{e.Id}' has an anyWorking requirement with no id or no components.");
                    foreach (var id in r.Components.SelectMany(a => a.Split('+')))
                        if (dto.Components.All(c => c.Id != id) && upgrades.All(u => u.Component.Id != id))
                            throw new InvalidOperationException($"Event '{e.Id}' needs '{id}', which is neither a component nor an upgrade.");
                    continue;
                }
                // A requirement names a component the boat has from the start, or an upgrade it can be given (design note 010, step 3).
                // "working" may name several (all must work).
                foreach (var id in r.Components.Prepend(r.Component))
                {
                    if (r.Kind == "working" && dto.Components.All(c => c.Id != id))
                        throw new InvalidOperationException($"Event '{e.Id}' needs '{id}' to work, which is not a component.");
                    if (dto.Components.All(c => c.Id != id) && upgrades.All(u => u.Component.Id != id))
                        throw new InvalidOperationException($"Event '{e.Id}' needs '{id}', which is neither a component nor an upgrade.");
                }
            }
        }
        foreach (var e in calendar?.Events ?? [])
            foreach (var b in e.Beats)
            {
                if (b.Kind is not ("arrival" or "trip" or "night"))
                    throw new InvalidOperationException($"Event '{e.Id}' has a beat '{b.Id}' of the kind '{b.Kind}'.");
                if (b.Hour is < 0 or > 23)
                    throw new InvalidOperationException($"Event '{e.Id}' has a beat '{b.Id}' at hour {b.Hour}.");
                if (b.Kind == "trip" && trips?.FindPort(b.Port) is null)
                    throw new InvalidOperationException($"Event '{e.Id}' has a trip beat to '{b.Port}', which is not in the trips.");
            }
        var orders = withCalendar ? (dto.Orders ?? OrderRules.Default) with { LeadDays = eco.Orders.LeadDays } : null;
        var yard = (dto.Yard ?? YardRules.Default) with
        {
            LiftNok = eco.Yard.LiftNok, StorageNok = eco.Yard.StorageNok, ChangeFeeNok = eco.Yard.ChangeFeeNok, WashNok = eco.Yard.WashNok,
        };
        var mooring = (dto.Mooring ?? MooringRules.Default) with { PartedRepairNok = eco.Mooring.PartedRepairNok };
        var layup = (dto.Layup ?? LayupRules.Default) with { DieselNokPerLitre = eco.Fuel.DieselNokPerLitre };
        var night = dto.Night ?? NightRules.Default;
        var closeCalls = (dto.CloseCalls ?? CloseCallRules.Default) with { LetterNok = eco.CloseCalls.LetterNok };
        // The skipper's tickets and the notice boards ride the same switch as the hours (design note 012, step 2): off for a boat written in a test.
        var tickets = withCalendar && withHours ? TicketLoader.LoadTickets(ReadResource(TicketsResource), eco, ReadResource(QuestionsResource)) : null;
        var board = withCalendar && withHours ? TicketLoader.LoadBoard(boardJson ?? ReadResource(BoardResource), ReadResource(LadderResource)) : null;
        if (tickets is { Enabled: true } && trips?.FindPort(tickets.Club.Port) is null)
            throw new InvalidOperationException($"tickets.json: the club is at '{tickets.Club.Port}', which is not in the trips.");
        if (board is not null)
            foreach (var (portId, port) in board.Ports)
            {
                if (trips?.FindPort(portId) is null) throw new InvalidOperationException($"board.json: a board at '{portId}', which is not in the trips.");
                foreach (var id in port.Events)
                    if (calendar?.Events.All(e => e.Id != id) ?? true) throw new InvalidOperationException($"board.json: '{portId}' posts the event '{id}', which is not in the calendar.");
            }
        if (board is not null) TicketLoader.CheckLadder(board.Ladder, trips, tickets);
        if (board is { Crewing.Enabled: true } crewing) TicketLoader.CheckCrewing(crewing.Crewing, trips, tickets);
        return new Boat(
            dto.Name, places, covers, positions, components, economy, survey, quality, rates, water, electrical, insurance,
            chandlery, trips, dto.Sea, dto.Forecast, yard, dto.Fouling, mooring, layup, dto.Sale, calendar, orders, dto.Fuel, night, upgrades, withCalendar ? dto.Reputation : null,
            eco.Currency, closeCalls, dto.Instruments, withCalendar && withHours ? eco.Hours.ToRules() : null, withCalendar && withHours ? eco.Sleep.ToRules() : null,
            tickets, board, (dto.Radio ?? RadioRules.Default) with { UnlicensedLetterNok = eco.Radio.UnlicensedLetterNok });
    }

    /// <summary>A boat file holding a knob that economy.json owns is a mistake the two files could disagree about, so it is refused.</summary>
    private static void RefuseEconomyKnobs(string boatJson)
    {
        using var doc = JsonDocument.Parse(boatJson, new JsonDocumentOptions { AllowTrailingCommas = true });
        foreach (var path in EconomyKnobs)
        {
            var node = doc.RootElement;
            var found = true;
            foreach (var key in path)
            {
                if (node.ValueKind != JsonValueKind.Object) { found = false; break; }
                var next = node.EnumerateObject().Where(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault();
                if (next is null) { found = false; break; }
                node = next.Value;
            }
            if (found)
                throw new InvalidOperationException(
                    $"The boat file holds '{string.Join('.', path)}', which economy.json owns (design note 010, second pass A). Remove it from the boat file.");
        }
    }

    private static Dictionary<string, ComponentKind> BuildKinds(List<KindDto>? dtos)
    {
        var kinds = new Dictionary<string, ComponentKind>();
        foreach (var k in dtos ?? [])
        {
            if (kinds.ContainsKey(k.Id)) throw new ArgumentException($"Duplicate kind '{k.Id}'.");
            foreach (var v in k.Verbs)
                if (!Verb.All.Contains(v)) throw new ArgumentException($"Kind '{k.Id}' names an unknown verb '{v}'.");
            if (!k.Verbs.Contains(Verb.Inspect)) throw new ArgumentException($"Kind '{k.Id}' must offer inspect.");
            kinds[k.Id] = new ComponentKind(k.Id, k.Verbs.ToHashSet());
        }
        return kinds;
    }

    /// <summary>
    /// A component's kind decides its verbs, never its faults (002 finding 2). The kind must agree with the rest of the component's data:
    /// uncover exactly when it has a removable part, replace exactly when it has a replace price, and every fault a verb fixes
    /// must be one the kind offers. A boat with no kinds table (a test fixture) gives every part inspect alone.
    /// </summary>
    private static ComponentKind? ResolveKind(ComponentDto c, Dictionary<string, ComponentKind> kinds, int replaceCostNok)
    {
        if (c.Kind is null)
        {
            if (kinds.Count > 0) throw new ArgumentException($"Component '{c.Id}' has no kind.");
            return null;
        }
        if (!kinds.TryGetValue(c.Kind, out var kind)) throw new ArgumentException($"Component '{c.Id}' has unknown kind '{c.Kind}'.");
        if (kind.Verbs.Contains(Verb.Uncover) != (c.RemovablePart is not null))
            throw new ArgumentException($"Component '{c.Id}' (kind '{kind.Id}'): uncover is offered exactly when it has a removablePart.");
        if (kind.Verbs.Contains(Verb.Replace) != (replaceCostNok > 0))
            throw new ArgumentException($"Component '{c.Id}' (kind '{kind.Id}'): replace is offered exactly when it has a replace price.");
        foreach (var f in c.Faults ?? [])
        {
            // A non-discovering uncovered inspect reveal stands in for the part's uncoveredText (decision 2026-11-08): it needs a part to stand in for.
            if ((f.Reveals ?? []).Any(r => r.Action == PlayerAction.Inspect && r.NeedsUncovered && !r.Discovers) && c.RemovablePart is null)
                throw new ArgumentException($"Fault '{f.Id}' has a non-discovering uncovered inspect reveal, but component '{c.Id}' has no removablePart to stand in for.");
            foreach (var (action, verb) in new[] { (PlayerAction.Clean, Verb.Clean), (PlayerAction.Charge, Verb.Charge), (PlayerAction.Replace, Verb.Replace) })
                if ((f.FixedBy ?? []).Contains(action) && !kind.Verbs.Contains(verb))
                    throw new ArgumentException($"Fault '{f.Id}' is fixed by {verb}, but component '{c.Id}' is of kind '{kind.Id}', which has no {verb} verb.");
        }
        return kind;
    }

    /// <summary>
    /// Routes as written in the data carry no distance or course: both are derived from the two ports' chart positions (design note 009),
    /// distance to 0.1 nm and course to the whole degree, 0 to 359. A route whose ports lack a position, a leftover distance or course,
    /// two ports at one point, or a shelter outside the route are errors.
    /// </summary>
    private static List<Route> DeriveRoutes(List<Port> ports, List<RouteDto> routes)
    {
        for (int i = 0; i < ports.Count; i++)
            for (int j = i + 1; j < ports.Count; j++)
                if (ports[i].Chart is { } a && ports[j].Chart is { } b && a == b)
                    throw new InvalidOperationException($"Ports '{ports[i].Id}' and '{ports[j].Id}' are at the same point on the chart.");

        var result = new List<Route>();
        foreach (var r in routes)
        {
            if (r.DistanceNm is not null || r.CourseDeg is not null)
                throw new InvalidOperationException($"Route {r.From} → {r.To} carries distanceNm or courseDeg: they are derived from the ports' chart positions.");
            var from = ports.FirstOrDefault(p => p.Id == r.From) ?? throw new InvalidOperationException($"Route {r.From} → {r.To}: no port '{r.From}'.");
            var to = ports.FirstOrDefault(p => p.Id == r.To) ?? throw new InvalidOperationException($"Route {r.From} → {r.To}: no port '{r.To}'.");
            foreach (var end in new[] { from, to })
                if (end.Chart is null)
                    throw new InvalidOperationException($"Route {r.From} → {r.To}: port {end.Id} has no chart position.");
            double dx = to.Chart!.EastNm - from.Chart!.EastNm, dy = to.Chart.NorthNm - from.Chart.NorthNm;
            double distance = Math.Round(Math.Sqrt(dx * dx + dy * dy), 1, MidpointRounding.AwayFromZero);
            int course = (((int)Math.Round(Math.Atan2(dx, dy) * 180 / Math.PI, MidpointRounding.AwayFromZero) % 360) + 360) % 360;
            var shelter = r.Shelter ?? [];
            foreach (var s in shelter)
                if (s.AtNm <= 0 || s.AtNm >= distance)
                    throw new InvalidOperationException($"Route {r.From} → {r.To}: shelter '{s.Name}' at {s.AtNm} nm is not inside the route (0 to {distance} nm).");
            result.Add(new Route { From = r.From, To = r.To, DistanceNm = distance, CourseDeg = course, Shelter = shelter });
        }
        return result;
    }

    /// <summary>
    /// The price of a part the chandlery sells: the shop item's. A number written in the data as well must agree with it,
    /// so two prices for one part cannot drift apart again. No shop item: the data's number (0 when absent).
    /// </summary>
    private static int PartPrice(ChandleryItem? part, int? dataNok, string what)
    {
        if (part is null) return dataNok ?? 0;
        if (dataNok is { } n && n != 0 && n != part.PriceNok)
            throw new InvalidOperationException(
                $"The chandlery sells the {part.Name} at {part.PriceNok} NOK but {what} says {n}. Remove the number: the chandlery price is used.");
        return part.PriceNok;
    }

    /// <summary>"spawn": one rule or a list of rules; any one crossing wakes the fault.</summary>
    private static List<SpawnRule> ParseSpawn(JsonElement? spawn)
    {
        if (spawn is not { } el || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return [];
        var items = el.ValueKind == JsonValueKind.Array ? el.EnumerateArray().ToList() : [el];
        return items.Select(i => new SpawnRule(
            i.GetProperty("when").GetString() ?? throw new InvalidOperationException("A spawn rule needs 'when'."),
            i.GetProperty("atLeast").GetDouble(),
            i.TryGetProperty("spread", out var spread) && spread.ValueKind == JsonValueKind.True,
            i.TryGetProperty("repeats", out var repeats) && repeats.ValueKind == JsonValueKind.True,
            i.TryGetProperty("cause", out var cause) && cause.ValueKind == JsonValueKind.String ? cause.GetString() : null)).ToList();
    }

    private sealed class BoatDto
    {
        public string Name { get; set; } = "";
        public List<PlaceDto> Places { get; set; } = [];
        public List<CoverDto> Covers { get; set; } = [];
        public List<PositionDto> Positions { get; set; } = [];
        public List<KindDto>? Kinds { get; set; }
        public List<ComponentDto> Components { get; set; } = [];
        public SurveyDto? Survey { get; set; }
        public DiyQualityDto? DiyQuality { get; set; }
        public RatesDto? Rates { get; set; }
        public WaterDto? Water { get; set; }
        public ElectricalDto? Electrical { get; set; }
        public InsuranceDto? Insurance { get; set; }
        public List<ChandleryItem>? Chandlery { get; set; }
        public TripsDto? Trips { get; set; }
        public SeaRules? Sea { get; set; }
        public ForecastRules? Forecast { get; set; }
        public YardRules? Yard { get; set; }
        public FoulingRules? Fouling { get; set; }
        public MooringRules? Mooring { get; set; }
        public LayupRules? Layup { get; set; }
        public SaleRules? Sale { get; set; }
        public CalendarRules? Calendar { get; set; }
        public OrderRules? Orders { get; set; }
        public FuelRules? Fuel { get; set; }
        public NightRules? Night { get; set; }
        public CloseCallRules? CloseCalls { get; set; }
        public InstrumentRules? Instruments { get; set; }
        public RadioRules? Radio { get; set; }
        public ReputationRules? Reputation { get; set; }
        public List<UpgradeDto>? Upgrades { get; set; }
    }

    private sealed class UpgradeDto
    {
        public ComponentDto? Component { get; set; }
        public string Place { get; set; } = "";
        public int InstallMinutes { get; set; }
        public int? YardNok { get; set; }
        public int? YardDays { get; set; }
        public int ValueNok { get; set; }
        /// <summary>Parts that come aboard with it and are not upgrades themselves (the radar's scanner cable): full components, fitted together.</summary>
        public List<ComponentDto>? Also { get; set; }
        public string? Opens { get; set; }
        public string? InstallText { get; set; }
        /// <summary>The component(s) aboard that this one takes the place of: one id or a list of ids.</summary>
        [JsonConverter(typeof(StringOrListConverter))]
        public List<string>? Replaces { get; set; }
    }

    /// <summary>"replaces": "old-heads" or "replaces": ["old-heads", "joker-valve"].</summary>
    private sealed class StringOrListConverter : JsonConverter<List<string>>
    {
        public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String
                ? [reader.GetString() ?? ""]
                : JsonSerializer.Deserialize<List<string>>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class RouteDto
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        /// <summary>Present only to be rejected: derived from the ports' positions.</summary>
        public double? DistanceNm { get; set; }
        public int? CourseDeg { get; set; }
        public List<Shelter>? Shelter { get; set; }
    }

    private sealed class TripsDto
    {
        public string Home { get; set; } = "";
        public List<Port>? Ports { get; set; }
        public List<RouteDto>? Routes { get; set; }
        public TowRules? Tow { get; set; }
    }

    private sealed class LevelWordDto
    {
        public double? UnderLitres { get; set; }
        public string Text { get; set; } = "";
    }

    private sealed class WaterDto
    {
        public double StartLitres { get; set; }
        public double SoleLitres { get; set; } = 120;
        public double SinkLitres { get; set; } = 1500;
        public double RainThroughOpenHatchLitresPerMm { get; set; }
        public double ManualPumpLitresPerMinute { get; set; } = 10;
        public int ManualPumpMinutes { get; set; } = 15;
        public string? SoleText { get; set; }
        public double? HarbourmasterLitres { get; set; }
        public string? HarbourmasterText { get; set; }
        public List<LevelWordDto>? LevelWords { get; set; }
    }

    private sealed class ElectricalDto
    {
        public double SelfDischargePerDay { get; set; }
        public double PumpMinStateOfCharge { get; set; } = 0.15;
        public double CrankDraw { get; set; } = 0.01;
        public double ChargePerHour { get; set; } = 0.10;
        public double RunDrawPerHour { get; set; } = 0.03;
    }

    private sealed class InsuranceDto
    {
        public int ValueNok { get; set; }
        public double PayoutFraction { get; set; }
    }

    private sealed class RatesDto
    {
        public double Threshold { get; set; } = 1.0;
        public List<double>? Spread { get; set; }
        public ExposureDto? Exposure { get; set; }
        public UseDto? Use { get; set; }
        public NeglectDto? Neglect { get; set; }
        public WeatherGatesDto? WeatherGates { get; set; }
    }

    private sealed class ExposureDto
    {
        public double RainWeight { get; set; }
        public double RainCapMm { get; set; } = 1;
        public double FrostWeight { get; set; }
        public double PerOpenCover { get; set; }
    }

    private sealed class WeatherGatesDto
    {
        public int WindForce { get; set; } = 7;
        public double RainMm { get; set; } = 0.5;
    }

    private sealed class UseDto
    {
        public double PerEngineHour { get; set; }
    }

    private sealed class NeglectDto
    {
        public double PerKnownOpenFault { get; set; }
        public double BatteryLow { get; set; }
        public double PerCoverLeftOff { get; set; }
        public double PerDayAwayBeyond3 { get; set; }
        public List<string>? Covers { get; set; }
    }

    private sealed class ProgressionDto
    {
        public double BaseRatePerDay { get; set; }
        public List<RateFactor>? Factors { get; set; }
        public List<string>? ExposedBy { get; set; }
        public List<string>? RainOnlyWhenOff { get; set; }
        public List<double>? Spread { get; set; }
    }

    private sealed class SurveyItemDto
    {
        public string Component { get; set; } = "";
        public SurveySeverity Severity { get; set; }
        public string? Fault { get; set; }
        public SurveyFinding Finding { get; set; }
        public string Text { get; set; } = "";
    }

    private sealed class SurveyDto
    {
        public int LengthFeet { get; set; }
        public int Days { get; set; }
        public string Surveyor { get; set; } = "";
        public List<string>? Opening { get; set; }
        public List<string>? Closing { get; set; }
        public List<SurveyItemDto>? Items { get; set; }
    }

    private sealed class DiyDto
    {
        public string How { get; set; } = "";
        /// <summary>Optional when the fix replaces a part the chandlery sells: the chandlery price is used.</summary>
        public int? PartsNok { get; set; }
        public int Minutes { get; set; }
    }

    private sealed class YardDto
    {
        public string How { get; set; } = "";
        public int Nok { get; set; }
        public int LeadDays { get; set; }
    }

    private sealed class EscalationDto
    {
        public string To { get; set; } = "";
        public bool OnLaunch { get; set; }
        public string? Text { get; set; }
    }

    private sealed class PoorJobDto
    {
        public string Name { get; set; } = "";
        public string Reveal { get; set; } = "";
        public string Consequence { get; set; } = "";
        public int CostLaterNok { get; set; }
        public DiyDto Diy { get; set; } = new();
        public YardDto Yard { get; set; } = new();
    }

    private sealed class DiyQualityDto
    {
        public int StartSkill { get; set; }
        public int Threshold { get; set; }
        public double PoorJobChance { get; set; }
        public Dictionary<string, PoorJobDto>? PoorJobs { get; set; }
    }

    private sealed class PlaceDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public string? Description { get; set; }
        public string? Way { get; set; }
        public List<string>? Adjacent { get; set; }
        public bool OnlyOnTheHard { get; set; }
        public bool Exposed { get; set; }
    }

    private sealed class CoverDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public string Place { get; set; } = "";
        public bool Plural { get; set; }
        public int RemoveMinutes { get; set; }
        public int RefitMinutes { get; set; }
        public string? RemoveText { get; set; }
        public string? RefitText { get; set; }
        public bool StartsOff { get; set; }
        public string? OffPhrase { get; set; }
        public List<string>? StateWords { get; set; }
        public string? RemoveLog { get; set; }
        public string? RefitLog { get; set; }
    }

    private sealed class PositionDto
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private sealed class ReachDto
    {
        public string Place { get; set; } = "";
        public string? Cover { get; set; }
        public bool Visible { get; set; }
        public bool Reachable { get; set; }
    }

    private sealed class ComponentDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Subsystem Subsystem { get; set; }
        public string Position { get; set; } = "";
        public List<ReachDto>? Reach { get; set; }
        public List<string>? Aliases { get; set; }
        public string? Description { get; set; }
        public double Condition { get; set; }
        public double WearYears { get; set; }
        public double WearPerUseHour { get; set; }
        public double? SeriesOhms { get; set; }
        /// <summary>Authoring note, ignored by the sim.</summary>
        public string? Note { get; set; }
        public string? Kind { get; set; }
        /// <summary>Omitted when the chandlery sells the part (decision 007/1): the shop item's price is used.</summary>
        public int? ReplaceCostNok { get; set; }
        public int ReplaceMinutes { get; set; }
        public Dictionary<string, double>? Properties { get; set; }
        public List<FaultDto>? Faults { get; set; }
        public RemovablePartDto? RemovablePart { get; set; }
        public bool BelowWaterline { get; set; }
        public string? ReplaceText { get; set; }
        public bool NeedsDry { get; set; }
        public bool Stocked { get; set; } = true;
        /// <summary>Fitting a new one cannot come out as a poor job: the templates are about paint and wiring, and new lines or a shackle are just new.</summary>
        public bool NoPoorJob { get; set; }
        /// <summary>The job is done up the mast: refused in more wind than instruments.aloftMaxForce (design note 012, step 3).</summary>
        public bool Aloft { get; set; }
    }

    private sealed class KindDto
    {
        public string Id { get; set; } = "";
        public List<string> Verbs { get; set; } = [];
    }

    private sealed class RemovablePartDto
    {
        public string Name { get; set; } = "";
        public List<string>? Aliases { get; set; }
        public string? UncoveredText { get; set; }
        public string? UncoverText { get; set; }
        public string? RefitText { get; set; }
    }

    private sealed class FaultDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public FaultEffect? Effect { get; set; }
        public List<FaultEffect>? Effects { get; set; }
        public JsonElement? Spawn { get; set; }
        public List<FaultReveal>? Reveals { get; set; }
        public List<PlayerAction>? FixedBy { get; set; }
        public bool? Present { get; set; }
        public bool OnHardOnly { get; set; }
        public bool Harmless { get; set; }
        public bool Dormant { get; set; }
        public string? Consequence { get; set; }
        public int CostLaterNok { get; set; }
        public string? LaterNote { get; set; }
        public DiyDto? Diy { get; set; }
        public YardDto? Yard { get; set; }
        public EscalationDto? Escalation { get; set; }
        public Dictionary<string, double>? Properties { get; set; }
        public ProgressionDto? Progression { get; set; }
        /// <summary>Free text for the data's readers (for example "UNVERIFIED"); ignored by the loader.</summary>
        public string? Note { get; set; }
        public ArrivalSignDto? ArrivalSign { get; set; }
        public AtSeaDto? AtSea { get; set; }
        /// <summary>"rain": the fault stops the instrument only on a day of rain (design note 010, second pass G).</summary>
        public string? FailsWhen { get; set; }
        /// <summary>What shows of it while it stops the instrument.</summary>
        public string? Symptom { get; set; }
    }

    private sealed class AtSeaDto
    {
        public string Text { get; set; } = "";
        public string? Spare { get; set; }
    }

    private sealed class ArrivalSignDto
    {
        public string Text { get; set; } = "";
    }
}
