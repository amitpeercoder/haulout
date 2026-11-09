using System.Text.Json;
using System.Text.Json.Nodes;
using Haulout.Sim.Data;
using Haulout.Sim.Play;
using Haulout.Sim.Systems;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>The VHF, the radio ticket and Call (design note 012, step 3, decisions 4 and 8).</summary>
public class RadioTests
{
    private static CommandResult Ok(CommandResult r) => ClubGame.Ok(r);
    private static string Text(CommandResult r) => ClubGame.Text(r);
    private static StatusView Status(Game g) => ClubGame.Status(g);

    private static CommandResult No(CommandResult r)
    {
        Assert.False(r.Accepted, Text(r));
        Assert.Equal(0, r.MinutesTaken);
        Assert.Equal(0, r.CostNok);
        return r;
    }

    private static Boat MakeBoat(Action<JsonNode>? edit = null)
    {
        var node = JsonNode.Parse(ClubGame.Sloop28Json())!;
        node["water"]!["sinkLitres"] = 1e9;
        edit?.Invoke(node);
        return BoatLoader.Load(node.ToJsonString(), true, ClubGame.OpenShops(), withHours: true);
    }

    private static Action<JsonNode> Ferries(double fogChance, double fogReport) => n =>
    {
        var c = n["closeCalls"]!["causes"]!;
        c["fog"]!["chancePerHour"] = fogChance;
        c["fog"]!["reportChance"] = fogReport;
        c["night"]!["chancePerHour"] = 0;
    };

    private static Action<JsonNode> ConnectorPresent => n =>
    {
        var up = n["upgrades"]!.AsArray().Single(u => u!["component"]!["id"]!.GetValue<string>() == "vhf-radio")!;
        up["also"]![0]!["faults"]![0]!["dormant"] = false;
    };

    private static WeatherSeries Calm() => ClubGame.Calm();
    private static WeatherSeries Fog() =>
        WeatherSeries.Constant(new WeatherDay(0, 3, "southwest", 0, 12, 8, FogUntilHour: 24, Kind: FogKind.Sea));

    private static Game Afloat(Boat boat, WeatherSeries? weather = null, int day = 100, int seed = Game.DefaultSeed)
    {
        boat.Get("anchor-gear").Replace();
        boat.Get("anchor-shackle").Replace();
        var g = new Game(boat, day, new ClubGame.FixedRandom(0.99), seed, weather ?? Calm());
        Ok(Walk.Do(g, "negative terminal", c => new Clean(c)));
        Ok(Walk.Do(g, "seacock", c => new Replace(c)));
        Ok(Walk.Do(g, "impeller", c => new Replace(c)));
        Ok(Walk.Do(g, "cockpit drains", c => new Replace(c)));
        Ok(Walk.Do(g, "navigation lights", c => new Replace(c)));
        Ok(Walk.Start(g));
        Ok(g.Execute(new Launch()));
        if (!Status(g).EngineRunning) Ok(Walk.Start(g));
        Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Refit("companionway hatch")));
        return g;
    }

    private static void Wait(Game g, int days)
    {
        if (Status(g).EngineRunning) Ok(g.Execute(new StopEngine()));   // an engine left running at the mooring overheats and runs the battery down
        for (int i = 0; i < days; i++) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "cockpit");
    }

    private static void Engine(Game g)
    {
        string last = "";
        if (!Status(g).EngineRunning) { Walk.Start(g); }
        if (!Status(g).EngineRunning) Ok(Walk.Do(g, "start battery", c => new Charge(c)));   // a week at the mooring runs her battery down
        for (int i = 0; i < 3 && !Status(g).EngineRunning; i++) last = Text(Ok(Walk.Start(g)));
        Assert.True(Status(g).EngineRunning, "the engine would not start: " + last);
    }

    private static UpgradeView Up(Game g) => ((UpgradesView)g.Ask(new Upgrades())).Items.Single(i => i.Name == "VHF radio");

    /// <summary>Orders the set, waits for it, fits it at the chart table, and is back at the tiller with the engine going.</summary>
    private static void FitVhf(Game g)
    {
        Ok(g.Execute(new Buy("VHF radio")));
        for (int i = 0; i < 15 && !Up(g).Status.StartsWith("aboard", StringComparison.Ordinal); i++) Wait(g, 1);
        Walk.ToPlace(g, "saloon");
        Ok(g.Execute(new Install("VHF radio")));
        Walk.ToPlace(g, "cockpit");
        Engine(g);
    }

    private static Game WithVhf(Action<JsonNode>? edit = null, WeatherSeries? weather = null, bool ticket = true, int day = 100, int seed = Game.DefaultSeed)
    {
        var g = Afloat(MakeBoat(edit), weather, day, seed);
        FitVhf(g);
        if (ticket) g.GrantTicketForTests("radio");
        return g;
    }

    private static List<CommandResult> LegsUntilFerry(Game g)
    {
        Ok(g.Execute(new Depart("hestholmen")));
        var rs = new List<CommandResult> { Ok(g.Execute(new Motor())) };
        for (int i = 0; i < 30 && !rs[^1].Narrative.Any(l => l.StartsWith("Channel 16:", StringComparison.Ordinal)) && rs[^1].Observation is not TripResultView; i++)
            rs.Add(Ok(g.Execute(new Continue())));
        return rs;
    }

    /// <summary>Answers every ferry that is calling (the five minutes of an answer can meet the next one in a fog hour every hour).</summary>
    private static void AnswerAll(Game g)
    {
        for (int i = 0; i < 6 && g.Ask(new Trip()) is TripView { AtSea: true } tv && tv.Text.Contains("is calling you on channel 16", StringComparison.Ordinal); i++)
            Ok(g.Execute(new Call("ferry")));
    }

    private static List<LedgerEntry> Bills(Game g) => ((CalendarView)g.Ask(new Calendar())).Money.Recent.Where(e => e.Kind == "bill").ToList();
    private static bool HasLetter(Game g) => ClubGame.Log(g).Entries.Any(e => e.Document is { } d && d.StartsWith("letter", StringComparison.Ordinal));

    // ------------------------------------------------------------------ the data and the fit

    [Fact]
    public void The_VHF_loads_as_an_upgrade_with_its_connector_and_is_not_a_component_until_fitted()
    {
        var boat = BoatLoader.LoadSloop28();
        var vhf = boat.Upgrades.Single(u => u.Component.Id == "vhf-radio");
        Assert.False(boat.IsFitted("vhf-radio"));
        Assert.DoesNotContain(boat.Components, c => c.Id is "vhf-radio" or "vhf-antenna-connector");
        Assert.Equal(("chart-table", "saloon", 120, 1500, 1), (vhf.Component.PositionId, vhf.PlaceId, vhf.InstallMinutes, vhf.ValueNok, vhf.YardDays));
        Assert.Contains("channel 16", vhf.Opens);
        var also = Assert.Single(vhf.Also);
        Assert.Equal("vhf-antenna-connector", also.Id);
        Assert.True(also.Spec.Aloft);
        var item = boat.Chandlery.Single(i => i.Upgrade == "vhf-radio");
        Assert.Equal((3500, false), (item.PriceNok, item.Stocked));
        var spare = boat.Chandlery.Single(i => i.ForComponent == "vhf-antenna-connector");
        Assert.Equal((300, true), (spare.PriceNok, spare.Stocked));
        Assert.Equal(1500, boat.Radio.UnlicensedLetterNok);
        Assert.Equal(-1, boat.Reputation.UnlicensedRadio);
        Assert.Equal(3, boat.Instruments.AloftMaxForce);
        var raw = ClubGame.Sloop28Json();
        Assert.Contains("PLACEHOLDER (design note 012, step 3)", raw);
        Assert.Contains("UNVERIFIED (design note 012, step 3)", raw);
    }

    [Fact]
    public void The_VHF_is_ordered_then_fitted_at_the_chart_table_and_only_then_a_component()
    {
        var g = Afloat(MakeBoat());
        Assert.Equal("not aboard", Up(g).Status);
        Ok(g.Execute(new Buy("VHF radio")));
        Assert.StartsWith("on order", Up(g).Status);
        Assert.NotNull(Up(g).InstallRefusal);
        for (int i = 0; i < 15 && !Up(g).Status.StartsWith("aboard", StringComparison.Ordinal); i++) Wait(g, 1);
        Assert.Equal("aboard, not fitted", Up(g).Status);
        Assert.Contains("saloon: go there first", Up(g).InstallRefusal ?? "");
        Walk.ToPlace(g, "saloon");
        var r = Ok(g.Execute(new Install("VHF radio")));
        Assert.Equal(120, r.MinutesTaken);
        Assert.StartsWith("fitted on day", Up(g).Status);
        Assert.Equal(8, ((UpgradesView)g.Ask(new Upgrades())).Items.Count);
    }

    // ------------------------------------------------------------------ Call

    [Fact]
    public void Call_is_refused_without_a_set_and_costs_nothing()
    {
        var g = Afloat(MakeBoat());
        Assert.Equal("You have no radio aboard.", Text(No(g.Execute(new Call("harbour")))));
    }

    [Fact]
    public void Call_harbour_coast_radio_and_ferry_answer_in_the_procedure_the_course_book_teaches()
    {
        var g = WithVhf();
        var h = Ok(g.Execute(new Call("harbour")));
        Assert.Equal(5, h.MinutesTaken);
        var ht = Text(h);
        Assert.Contains("Skjærvik harbour, Skjærvik harbour, Skjærvik harbour, this is sailing yacht Marit, Marit, Marit, call sign Lima Golf Mike Romeo. Over.", ht);
        Assert.Contains("Go to channel 12", ht);
        Assert.Contains("Out.", ht);
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text.StartsWith("Called Skjærvik harbour", StringComparison.Ordinal));

        var c = Ok(g.Execute(new Call("coast radio")));
        var ct = Text(c);
        Assert.Contains("Loud and clear", ct);
        Assert.Contains("Today:", ct);
        Assert.Contains("Tomorrow:", ct);
        Assert.Contains("Over.", ct);
        Ok(g.Execute(new Call("coastguard")));   // the same station by another name

        Assert.Equal("Nobody is calling you; the ferries are working their own channels.", Text(No(g.Execute(new Call("ferry")))));
        Assert.Contains("Call whom", Text(No(g.Execute(new Call("the moon")))));
        Assert.False(HasLetter(g));   // with the ticket nobody reports it
    }

    [Fact]
    public void Call_works_at_sea_and_the_harbour_is_the_nearer_end_of_the_leg()
    {
        var g = WithVhf();
        Ok(g.Execute(new Depart("hestholmen")));
        Ok(g.Execute(new Motor()));
        var r = Ok(g.Execute(new Call("harbour")));
        Assert.Contains("Skjærvik harbour", Text(r));   // an hour out of a nine-mile leg: still nearer home
    }

    [Fact]
    public void A_connector_with_the_fault_receives_but_cannot_be_heard_and_the_coast_radios_forecast_still_comes()
    {
        var g = WithVhf(ConnectorPresent, ticket: false);
        var h = Ok(g.Execute(new Call("harbour")));
        Assert.Contains("You call three times. The set hisses with other boats' traffic: you can hear them; nobody hears you.", Text(h));
        Assert.DoesNotContain("answers", Text(h));
        var c = Ok(g.Execute(new Call("coast radio")));
        Assert.Contains("nobody hears you", Text(c));
        Assert.Contains("Today:", Text(c));
        Assert.DoesNotContain("Loud and clear", Text(c));
        Assert.False(HasLetter(g));   // nobody heard it, so nobody reported it
        Wait(g, 1);
        Assert.False(HasLetter(g));
    }

    // ------------------------------------------------------------------ the connector

    private static WeatherSeries Rain(int wind = 3) => WeatherSeries.Constant(new WeatherDay(0, wind, "southwest", 12, 12, 8));

    [Fact]
    public void The_connector_wakes_by_exposure_from_the_day_the_set_is_fitted_with_the_seeded_spread()
    {
        var boat = MakeBoat();
        var g = Afloat(boat, Rain());
        Wait(g, 20);                                  // weeks of rain with no set: no wetness is counted
        Assert.Equal(0, g.Values.Get(SystemValues.ConnectorWetness));
        FitVhf(g);
        double atFit = g.Values.Get(SystemValues.ConnectorWetness);
        Assert.True(atFit < 5);
        int fitted = Status(g).Day;
        Assert.False(boat.Get("vhf-antenna-connector").ActiveFaults.Any());
        int? woke = null;
        for (int i = 0; i < 120 && woke is null; i++)
        {
            Wait(g, 1);
            if (boat.Get("vhf-antenna-connector").ActiveFaults.Any()) woke = Status(g).Day;
        }
        Assert.NotNull(woke);
        Assert.True(woke > fitted + 7);

        int Wake(int seed)
        {
            var b = MakeBoat();
            var game = Afloat(b, Rain(), 100, seed);
            FitVhf(game);
            for (int i = 0; i < 200; i++)
            {
                Wait(game, 1);
                if (b.Get("vhf-antenna-connector").ActiveFaults.Any()) return Status(game).Day;
            }
            return -1;
        }
        Assert.Equal(Wake(3), Wake(3));
        Assert.True(Enumerable.Range(1, 6).Select(Wake).Distinct().Count() > 1);
    }

    [Fact]
    public void Inspect_shows_the_green_a_radio_check_is_the_first_hint_and_the_fix_needs_a_calm_day()
    {
        var wind = 5;
        var weather = WeatherSeries.From(d => new WeatherDay(0, d >= 130 ? 3 : wind, "southwest", 0, 12, 8));
        var g = WithVhf(ConnectorPresent, weather);
        // the fault is present but nothing has told the player
        Assert.DoesNotContain(((StatusView)g.Ask(new Status())).DiscoveredFaults, f => f.Name.Contains("connector", StringComparison.OrdinalIgnoreCase));
        Ok(g.Execute(new Call("coast radio")));      // unanswered: the hint
        Walk.ToPlace(g, "on-deck");
        var ins = Ok(g.Execute(new Inspect("antenna connector")));
        Assert.Contains("furred with green corrosion", Text(ins));
        Assert.Contains(Status(g).DiscoveredFaults, f => f.Name.Contains("connector", StringComparison.OrdinalIgnoreCase));
        Walk.ToPlace(g, "companionway"); Walk.ToPlace(g, "cockpit");
        Ok(g.Execute(new Buy("VHF antenna connector")));
        Walk.ToPlace(g, "on-deck");
        Assert.Equal("Too much wind to go up the mast: wait for a calm day.", Text(No(g.Execute(new Replace("antenna connector")))));
        while (Status(g).Day < 130) Ok(g.Execute(new WaitDays(1)));
        Walk.ToPlace(g, "cockpit"); Walk.ToPlace(g, "on-deck");
        var fix = Ok(g.Execute(new Replace("antenna connector")));
        Assert.Equal(60, fix.MinutesTaken);
        Walk.ToPlace(g, "cockpit");
        Assert.Contains("Loud and clear", Text(Ok(g.Execute(new Call("coast radio")))));
    }

    // ------------------------------------------------------------------ the close call becomes a call

    [Fact]
    public void With_a_set_and_the_ticket_a_ferrys_call_stops_the_passage_and_answering_it_reports_nothing()
    {
        var g = WithVhf(Ferries(1, 1), Fog());
        int rep = g.ReputationPoints, balance = Status(g).BalanceNok;
        var rs = LegsUntilFerry(g);
        var told = Text(rs[^1]);
        Assert.Contains("Channel 16: 'Sailing yacht near ", told);
        Assert.Contains("Do you read? Over.'", told);
        Assert.DoesNotContain("A ferry's horn", told);
        Assert.DoesNotContain("A wall of white steel", told);
        Assert.Contains("is calling you on channel 16", ((TripView)g.Ask(new Trip())).Text);
        // the trip query carries the call itself and the set, so an adapter can show it without reading prose (step 6)
        var waiting = (TripView)g.Ask(new Trip());
        Assert.True(waiting.RadioFitted);
        Assert.StartsWith("Channel 16: 'Sailing yacht near ", waiting.FerryCall);
        var answer = Ok(g.Execute(new Call("ferry")));
        Assert.Equal(5, answer.MinutesTaken);
        Assert.Contains("passes astern", Text(answer));
        Assert.Contains(ClubGame.Log(g).Entries, e => e.Text.StartsWith("Spoke to the ferry ", StringComparison.Ordinal) && e.Text.EndsWith("on channel 16; she passed astern.", StringComparison.Ordinal));
        // (a fog hour every hour: the five minutes of the call may meet the next ferry, which is answered in the loop below)
        Assert.Equal(rep, g.ReputationPoints);
        Assert.Equal(balance, Status(g).BalanceNok);
        Assert.False(HasLetter(g));
        // the next hour is another fog hour: another call, and the rest of the passage answered the same way is never reported
        AnswerAll(g);
        for (int i = 0; i < 12; i++)
        {
            var leg = Ok(g.Execute(new Continue()));
            if (leg.Observation is TripResultView)
            {
                // a ferry that rang in the last hour had no time to be answered: she is in harbour, and the old close call stands
                if (Text(leg).Contains("her master will have written you", StringComparison.Ordinal)) rep--;
                break;
            }
            Assert.DoesNotContain("her master will have written you", Text(leg));
            AnswerAll(g);
        }
        Assert.Equal(rep, g.ReputationPoints);
    }

    [Fact]
    public void Moving_on_without_answering_gives_the_old_close_call_with_its_report_mark_and_letter()
    {
        var g = WithVhf(Ferries(1, 1), Fog());
        int rep = g.ReputationPoints;
        var rs = LegsUntilFerry(g);
        Assert.Contains("Channel 16:", Text(rs[^1]));
        Assert.Equal(rep, g.ReputationPoints);        // nothing yet: she has not moved on
        var next = Ok(g.Execute(new Continue()));
        var t = Text(next);
        Assert.True(t.Contains("A ferry's horn", StringComparison.Ordinal) || t.Contains("A wall of white steel", StringComparison.Ordinal), t);
        Assert.Contains("her master will have written you", t);
        Assert.Equal(rep - 1, g.ReputationPoints);
    }

    [Fact]
    public void With_the_connector_fault_the_call_is_heard_but_the_answer_is_not_and_the_close_call_happens()
    {
        var g = WithVhf(n => { Ferries(1, 1)(n); ConnectorPresent(n); }, Fog());
        int rep = g.ReputationPoints;
        var rs = LegsUntilFerry(g);
        Assert.Contains("Channel 16:", Text(rs[^1]));
        var no = Ok(g.Execute(new Call("ferry")));
        Assert.Contains("nobody hears you", Text(no));
        Assert.Contains("is calling you on channel 16", ((TripView)g.Ask(new Trip())).Text);
        var next = Ok(g.Execute(new Continue()));
        Assert.Contains("her master will have written you", Text(next));
        Assert.True(g.ReputationPoints <= rep - 1);
    }

    [Fact]
    public void The_close_calls_are_the_same_with_a_set_that_is_never_answered_as_with_a_boat_that_has_no_radio_rules()
    {
        string Run(bool radioData)
        {
            var g = WithVhf(n =>
            {
                Ferries(0.5, 0.5)(n);
                if (!radioData) n.AsObject().Remove("radio");
            }, Fog());
            Ok(g.Execute(new Depart("hestholmen")));
            var rs = new List<CommandResult> { Ok(g.Execute(new Motor())) };
            for (int i = 0; i < 80 && rs[^1].Observation is not TripResultView; i++) rs.Add(Ok(g.Execute(new Continue())));
            var told = rs.SelectMany(r => r.Narrative).Where(l => l.StartsWith("A ferry's horn", StringComparison.Ordinal) || l.StartsWith("A wall of white steel", StringComparison.Ordinal)
                || l.StartsWith("A rock you never saw", StringComparison.Ordinal) || l.StartsWith("A moored fishing float", StringComparison.Ordinal)).ToList();
            return string.Join("|", told) + "#" + g.ReputationPoints;
        }
        var without = Run(radioData: false);
        Assert.Contains("#", without);
        Assert.Equal(without, Run(radioData: true));
    }

    // ------------------------------------------------------------------ without the ticket

    [Fact]
    public void Without_the_ticket_an_answered_call_works_but_the_coast_radio_reports_it_with_the_next_mornings_post_once_a_day()
    {
        var g = WithVhf(Ferries(1, 1), Fog(), ticket: false);
        int rep = g.ReputationPoints, balance = Status(g).BalanceNok;
        LegsUntilFerry(g);
        var answer = Ok(g.Execute(new Call("ferry")));
        Assert.Contains("You give the boat's name; there is no call sign to give.", Text(answer));
        Assert.Contains("passes astern", Text(answer));
        Assert.Contains("will be reported", Text(answer));
        Assert.DoesNotContain("call sign Lima", Text(answer));
        Ok(g.Execute(new Call("coast radio")));       // the same day: the same report
        AnswerAll(g);
        for (int i = 0; i < 60 && g.Ask(new Trip()) is TripView { AtSea: true }; i++)
        {
            var leg = Ok(g.Execute(new Continue()));
            AnswerAll(g);
        }
        Ok(g.Execute(new WaitDays(2)));
        int letters = ClubGame.Log(g).Entries.Count(e => e.Document is { } d && d.StartsWith("letter", StringComparison.Ordinal) && e.Text.Contains("no call sign"));
        Assert.InRange(letters, 1, 3);                // one report a day: the passage may cross a midnight
        Assert.Equal(letters, Bills(g).Count(b => b.Nok == -1500));
        Assert.Equal(rep - letters, g.ReputationPoints);
        Assert.True(Status(g).BalanceNok <= balance - 1500 * letters);
    }

    [Fact]
    public void A_call_in_harbour_without_the_ticket_brings_the_letter_next_morning_and_not_before()
    {
        var g = WithVhf(ticket: false);
        int balance = Status(g).BalanceNok;
        var r = Ok(g.Execute(new Call("harbour")));
        Assert.Contains("no call sign to give", Text(r));
        Assert.Equal(balance, Status(g).BalanceNok);
        Assert.False(HasLetter(g));
        Ok(g.Execute(new Call("harbour")));           // same day: no second report
        Wait(g, 1);
        Assert.True(HasLetter(g));
        Assert.Equal(1, Bills(g).Count(b => b.Nok == -1500));
        Assert.Contains(((NoticesView)g.Ask(new Notices())).Alerts.Concat(ClubGame.Log(g).Entries.Where(e => e.Alert).Select(e => new Alert(e.Day, e.TimeOfDay, e.Text))),
            a => a.Text.StartsWith("A letter from the harbourmaster's office: the coast radio logged a call", StringComparison.Ordinal));
        // another day, another report
        Ok(g.Execute(new Call("harbour")));
        Wait(g, 1);
        Assert.Equal(2, Bills(g).Count(b => b.Nok == -1500));
    }

    // ------------------------------------------------------------------ what the plan and the weather say

    [Fact]
    public void The_plan_and_the_weather_mention_the_set_only_with_a_set_and_no_known_fault()
    {
        string FogLines(Game g) => string.Join(" ", ((TripPlanView)g.Ask(new PlanTrip("hestholmen"))).Warnings ?? []);
        var none = Afloat(MakeBoat(), Fog());
        Assert.DoesNotContain("With the VHF", FogLines(none));
        Assert.Null(((WeatherView)none.Ask(new Haulout.Sim.Play.Weather())).CoastRadio);

        var licensed = WithVhf(null, Fog());
        Assert.Contains("With the VHF a ferry will call you before she is close; answer on 16.", FogLines(licensed));
        Assert.DoesNotContain("no radio ticket", FogLines(licensed));
        var coast = ((WeatherView)licensed.Ask(new Haulout.Sim.Play.Weather())).CoastRadio;
        Assert.StartsWith("The coast radio reads the forecast on channel 24 at 08:00: tomorrow: ", coast);

        var unlicensed = WithVhf(null, Fog(), ticket: false);
        Assert.Contains("answer on 16, but you hold no radio ticket: a call would be reported.", FogLines(unlicensed));

        // a fault the player has found takes the lines away; a fault they have not found does not
        var faulty = WithVhf(ConnectorPresent, Fog());
        Assert.Contains("With the VHF", FogLines(faulty));
        Assert.NotNull(((WeatherView)faulty.Ask(new Haulout.Sim.Play.Weather())).CoastRadio);
        Ok(g2(faulty));
        Assert.DoesNotContain("With the VHF", FogLines(faulty));
        Assert.Null(((WeatherView)faulty.Ask(new Haulout.Sim.Play.Weather())).CoastRadio);

        static CommandResult g2(Game g)
        {
            Walk.ToPlace(g, "on-deck");
            return g.Execute(new Inspect("antenna connector"));
        }
    }

    // ------------------------------------------------------------------ the adapters, the leaks, the questions

    [Fact]
    public void Call_round_trips_through_json_and_no_id_reaches_a_new_view()
    {
        Assert.Contains("\"type\":\"call\"", JsonSerializer.Serialize<Command>(new Call("ferry")));
        Assert.Equal(new Call("ferry"), JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize<Command>(new Call("ferry"))));
        var g = WithVhf(Ferries(1, 1), Fog(), ticket: false);
        var all = new List<string>
        {
            Text(Ok(g.Execute(new Call("harbour")))), Text(Ok(g.Execute(new Call("coast radio")))),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Haulout.Sim.Play.Weather())),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new PlanTrip("hestholmen"))),
            JsonSerializer.Serialize<QueryResult>(g.Ask(new Upgrades())),
        };
        var rs = LegsUntilFerry(g);
        all.Add(Text(rs[^1]));
        all.Add(Text(Ok(g.Execute(new Call("ferry")))));
        all.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Trip())));
        Ok(g.Execute(new Continue()));
        all.Add(JsonSerializer.Serialize<QueryResult>(g.Ask(new Logbook())));
        foreach (var text in all)
            foreach (var id in new[] { "vhf-radio", "vhf-antenna-connector", "vhf-connector-corroded", "connectorWetness", "chart-table" })
                Assert.DoesNotContain(id, text);
    }

    [Fact]
    public void No_question_or_course_book_page_confuses_the_games_own_ticket_names_and_the_radio_question_names_the_radio_ticket()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.questions.json")!;
        using var reader = new StreamReader(stream);
        var doc = JsonNode.Parse(reader.ReadToEnd())!;
        var radio17 = doc["questions"]!.AsArray().Single(q => q!["id"]!.GetValue<string>() == "radio-17")!;
        var options = radio17["options"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
        Assert.Equal("The radio ticket, and a call sign for the boat", options[radio17["answer"]!.GetValue<string>()[0] - 'a']);
        var everything = doc["questions"]!.AsArray().SelectMany(q => q!["options"]!.AsArray().Select(o => o!.GetValue<string>()).Append(q["text"]!.GetValue<string>()))
            .Concat(doc["books"]!.AsArray().SelectMany(b => b!["pages"]!.AsArray().Select(p => p!["text"]!.GetValue<string>()))).ToList();
        Assert.DoesNotContain(everything, s => s.Contains("boat licence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_tow_is_called_on_the_VHF_only_when_a_set_is_aboard_and_otherwise_by_phone()
    {
        var withSet = WithVhf();
        Ok(withSet.Execute(new Depart("hestholmen")));
        Assert.Contains("You call the towboat on the VHF", Text(Ok(withSet.Execute(new CallTow()))));

        var noSet = Afloat(MakeBoat());
        Ok(noSet.Execute(new Depart("hestholmen")));
        var phone = Text(Ok(noSet.Execute(new CallTow())));
        Assert.Contains("You ring the towboat on your phone and give your position.", phone);
        Assert.DoesNotContain("VHF", phone);
    }

    [Fact]
    public void The_trip_query_says_whether_a_set_is_aboard_in_port_and_at_sea_and_carries_no_call_without_one()
    {
        var noSet = Afloat(MakeBoat());
        Assert.False(((TripView)noSet.Ask(new Trip())).RadioFitted);
        Ok(noSet.Execute(new Depart("hestholmen")));
        var atSea = (TripView)noSet.Ask(new Trip());
        Assert.False(atSea.RadioFitted);
        Assert.Null(atSea.FerryCall);

        var withSet = WithVhf();
        Assert.True(((TripView)withSet.Ask(new Trip())).RadioFitted);
        Ok(withSet.Execute(new Depart("hestholmen")));
        var calm = (TripView)withSet.Ask(new Trip());
        Assert.True(calm.RadioFitted);
        Assert.Null(calm.FerryCall);
    }
}
