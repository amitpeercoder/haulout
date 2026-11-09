using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Haulout.Mcp.Tests;

public class McpServerTests
{
    private static readonly string[] ExpectedTools =
    [
        "look", "go", "remove", "refit", "inspect", "uncover", "measure", "clean", "replace", "charge", "buy",
        "check_filter", "check_flow", "start_engine", "stop_engine",
        "order_survey", "survey", "readiness", "defer", "undefer", "yard_fix", "wait", "launch", "emergency_lift", "pump_bilge",
        "status", "logbook", "aboard", "weather", "notices", "help", "briefing",
        "plan_trip", "inventory", "trip", "chart", "depart", "continue", "motor", "sail", "heave_to", "anchor", "turn_back", "call_tow",
        "season", "book_haulout", "haul_out", "scrub", "yard_wash",
        "winterise_engine", "fill_tank", "battery_ashore", "battery_aboard", "winter_charger", "fit_winter_cover", "wait_until_spring",
        "list_for_sale", "accept_offer", "keep", "sale", "sleep",
        "calendar", "next_weekend", "wait_week", "order", "invite_friend", "plan_event",
        "install", "yard_install", "upgrades", "season_page",
        "skipper", "tickets", "noticeboard", "enrol", "take_crewing_place", "read", "exam_question", "answer", "call",
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Haulout.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    private static string ServerDll()
    {
        // Same configuration/TFM as this test assembly: bin/<config>/<tfm>/ of the Mcp project.
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var tfm = baseDir.Name;
        var config = baseDir.Parent!.Name;
        var dll = Path.Combine(RepoRoot(), "src", "Haulout.Mcp", "bin", config, tfm, "Haulout.Mcp.dll");
        Assert.True(File.Exists(dll), $"Server not built: {dll}");
        return dll;
    }

    private static string DotnetHost()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(host)) return host;
        var self = Environment.ProcessPath;
        return self is not null && Path.GetFileNameWithoutExtension(self) == "dotnet" ? self : "dotnet";
    }

    private static async Task<McpClient> Connect(Dictionary<string, string?>? env = null) =>
        await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "haulout-test",
            Command = DotnetHost(),
            Arguments = [ServerDll()],
            EnvironmentVariables = env ?? [],
        }));

    private static Task<CallToolResult> Call(McpClient c, string tool, Dictionary<string, object?> args) =>
        c.CallToolAsync(tool, args).AsTask();

    private static string TextOf(CallToolResult r)
    {
        Assert.NotEqual(true, r.IsError);
        var block = Assert.Single(r.Content);
        return Assert.IsType<TextContentBlock>(block).Text;
    }

    private static JsonDocument SloopData() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoRoot(), "src", "Haulout.Sim", "Data", "sloop28.json")));

    /// <summary>Ids of places, covers, positions, components and faults. Tolerant of the old (compartments) and new shape.</summary>
    private static IEnumerable<string> HyphenatedIds()
    {
        using var doc = SloopData();
        var ids = new List<string>();
        foreach (var section in new[] { "compartments", "places", "covers", "positions", "components", "upgrades" })
        {
            if (!doc.RootElement.TryGetProperty(section, out var el)) continue;
            if (el.ValueKind == JsonValueKind.Object)
                foreach (var p in el.EnumerateObject()) ids.Add(p.Name);
            else if (el.ValueKind == JsonValueKind.Array)
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) ids.Add(item.GetString()!);
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (item.TryGetProperty("id", out var id)) ids.Add(id.GetString()!);
                    // An upgrade carries its component (design note 010, step 3).
                    if (item.TryGetProperty("component", out var up) && up.ValueKind == JsonValueKind.Object && up.TryGetProperty("id", out var uid)) ids.Add(uid.GetString()!);
                    if (item.TryGetProperty("faults", out var faults))
                        foreach (var f in faults.EnumerateArray())
                            if (f.TryGetProperty("id", out var fid)) ids.Add(fid.GetString()!);
                }
        }
        return ids.Where(i => i.Contains('-')).Distinct();
    }

    /// <summary>Display names of places, covers and components; some contain an id as a substring ("Quarter-berth panel").</summary>
    private static IEnumerable<string> DisplayNames()
    {
        using var doc = SloopData();
        var names = new List<string>();
        foreach (var section in new[] { "compartments", "places", "covers", "components" })
            if (doc.RootElement.TryGetProperty(section, out var el) && el.ValueKind == JsonValueKind.Array)
                foreach (var item in el.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var n) && n.GetString() is { } name)
                        names.Add(name);
        return names.OrderByDescending(n => n.Length);
    }

    [Fact]
    public async Task Lists_all_tools()
    {
        await using var client = await Connect();
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        foreach (var n in ExpectedTools) Assert.Contains(n, names);
        Assert.Equal(ExpectedTools.Length, names.Count);
    }

    [Fact]
    public async Task No_tool_or_parameter_description_carries_an_economy_figure_or_the_currency_code()
    {
        Assert.Equal(80, ExpectedTools.Length);
        await using var client = await Connect();
        var tools = await client.ListToolsAsync();
        // Rule 5: the price is a query result, never a literal in the interface. A description may say "costs the tow"; it may not say how much.
        var figures = new System.Text.RegularExpressions.Regex(@"\d+\s*(NOK|kr|nok)|NOK|\b\d{4,}\b");
        foreach (var t in tools)
        {
            Assert.DoesNotMatch(figures, t.Description ?? "");
            Assert.DoesNotMatch(figures, t.JsonSchema.GetRawText());
        }
    }

    [Fact]
    public async Task Tool_results_are_prose_without_ids_or_structured_content()
    {
        var ids = HyphenatedIds().ToList();
        Assert.NotEmpty(ids);
        var names = DisplayNames().ToList();
        await using var client = await Connect();

        var calls = new (string Tool, Dictionary<string, object?> Args)[]
        {
            ("briefing", []),
            ("help", []),
            ("start_engine", []),
            ("go", new() { ["place"] = "companionway" }),
            ("go", new() { ["place"] = "quarter berth" }),
            ("remove", new() { ["cover"] = "battery-box lid" }),
            ("inspect", new() { ["component"] = "negative terminal" }),
            ("uncover", new() { ["component"] = "negative terminal" }),
            ("inspect", new() { ["component"] = "negative terminal" }),
            ("aboard", []),
            ("status", []),
            ("logbook", []),
            ("plan_trip", []),
            ("plan_trip", new() { ["destination"] = "Hestholmen", ["depart_day"] = 150 }),
            ("inventory", []),
            ("buy", new() { ["item"] = "impeller" }),
            ("chart", []),
            ("chart", new() { ["destination"] = "Hestholmen" }),
            ("trip", []),
            ("calendar", []),
            ("order", new() { ["item"] = "starter motor" }),
            ("order", new() { ["item"] = "marine heads" }),
            ("upgrades", []),
            ("season_page", []),
            ("install", new() { ["item"] = "marine heads" }),
            ("yard_install", new() { ["item"] = "bunk cushions" }),
            ("next_weekend", []),
            ("wait_week", []),
            ("invite_friend", new() { ["day"] = 190 }),
            ("plan_event", new() { ["event"] = "regatta" }),
            ("plan_event", new() { ["event"] = "regatta", ["going"] = false }),
            ("plan_trip", new() { ["destination"] = "Kvitnes" }),
            ("calendar", []),
            ("depart", []),
            ("continue", []),
            ("call_tow", []),
            ("winterise_engine", []),
            ("fill_tank", []),
            ("battery_ashore", []),
            ("battery_aboard", []),
            ("winter_charger", []),
            ("fit_winter_cover", []),
            ("wait_until_spring", []),
            ("sale", []),
            ("list_for_sale", new() { ["ask_nok"] = 150000 }),
            ("sale", []),
            ("accept_offer", []),
            ("season_page", new() { ["season"] = 1 }),
        };
        foreach (var (tool, args) in calls)
        {
            var result = await Call(client, tool, args);
            var text = TextOf(result);
            Assert.False(string.IsNullOrWhiteSpace(text), tool);
            Assert.False(text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['), $"{tool}: {text}");
            var bare = names.Aggregate(text, (t, n) => t.Replace(n, " ", StringComparison.OrdinalIgnoreCase));
            foreach (var id in ids) Assert.DoesNotContain(id, bare, StringComparison.OrdinalIgnoreCase);
            if (result.StructuredContent is { } sc) AssertNoIdProperties(sc, tool);
        }
    }

    /// <summary>The game starts at eight on a Saturday: thirty looks of two minutes each bring it to nine, when the chandlery and the yard office open.</summary>
    private static async Task UntilNine(McpClient client)
    {
        for (int i = 0; i < 30; i++) await Call(client, "look", []);
    }

    [Fact]
    public async Task Trip_tools_plan_buy_and_refuse_in_plain_words()
    {
        await using var client = await Connect();
        var plan = TextOf(await Call(client, "plan_trip", []));
        Assert.Contains("Hestholmen", plan);
        Assert.Contains("Readiness for this trip:", plan);
        Assert.Contains("tow costs", plan);
        Assert.Contains("Day 75", plan);

        var inv = TextOf(await Call(client, "inventory", []));
        Assert.Contains("impeller", inv);
        Assert.Contains("NOK", inv);

        // The chandlery opens at nine on a Saturday (design note 012): at eight it says so, and the refusal costs nothing.
        Assert.Contains("opens at nine", TextOf(await Call(client, "buy", new() { ["item"] = "impeller" })));
        await UntilNine(client);
        var buy = TextOf(await Call(client, "buy", new() { ["item"] = "impeller" }));
        Assert.Contains("impeller", buy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("impeller x1", TextOf(await Call(client, "inventory", [])));

        Assert.Contains("hard", TextOf(await Call(client, "depart", [])), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not at sea", TextOf(await Call(client, "continue", [])), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_course_book_and_the_exam_tools_read_ask_and_refuse_in_plain_words()
    {
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains("course book", tools["read"]);
        Assert.Contains("never the right letter", tools["answer"]);
        Assert.Contains("read <document> = read", TextOf(await Call(client, "help", [])));
        Assert.Contains("exam = exam_question", TextOf(await Call(client, "help", [])));

        Assert.Contains("You have no document called 'radio course book'.", TextOf(await Call(client, "read", new() { ["document"] = "radio course book" })));
        Assert.Contains("There is no exam to sit.", TextOf(await Call(client, "exam_question", [])));
        Assert.Contains("There is no exam to sit.", TextOf(await Call(client, "answer", new() { ["letter"] = "a" })));

        await UntilNine(client);
        Assert.Contains("The club secretary hands you the radio course book.", TextOf(await Call(client, "enrol", new() { ["course"] = "radio" })));
        var book = TextOf(await Call(client, "read", new() { ["document"] = "radio course book" }));
        Assert.StartsWith("The radio course book", book);
        Assert.Contains("Page 1. Channel 16 and channel 70", book);
        Assert.Contains("Page 7. The DSC alert, and keeping silent", book);
        Assert.Contains("(course book:", TextOf(await Call(client, "logbook", [])));
    }

    [Fact]
    public async Task The_club_tools_skipper_tickets_and_noticeboard_answer_in_plain_words_with_no_ids_and_their_descriptions_name_what_they_show()
    {
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains("crewing places", tools["noticeboard"]);
        Assert.Contains("broker's window", tools["noticeboard"]);
        Assert.Contains("the season is posted", tools["noticeboard"]);
        Assert.Contains("board = noticeboard", TextOf(await Call(client, "help", [])));

        var skipper = TextOf(await Call(client, "skipper", []));
        Assert.Contains("No miles under way yet.", skipper);
        Assert.Contains("No tows.", skipper);
        var tickets = TextOf(await Call(client, "tickets", []));
        Assert.Contains("Radio ticket: Not held.", tickets);
        Assert.Contains("[ ] 300 miles: you have 0.", tickets);
        var board = TextOf(await Call(client, "noticeboard", []));
        Assert.Contains("The club's board by the slipway, pinned by the committee:", board);
        Assert.Contains("The radio course, Saturday, day 138", board);
        Assert.Contains("Your log: the radio ticket: not yet.", board);
        Assert.Contains("The broker's window, by the club:", board);
        Assert.Contains("Her insurer wants the coastal skipper ticket: you don't.", board);
        foreach (var text in new[] { skipper, tickets, board })
            foreach (var id in HyphenatedIds()) Assert.DoesNotContain(id, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sleep_and_call_refuse_and_answer_in_plain_words_and_the_help_names_them()
    {
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains("Turn in", tools["sleep"]);
        Assert.Contains("VHF", tools["call"]);
        Assert.Contains("turn in = sleep", TextOf(await Call(client, "help", [])));

        Assert.Contains("You have no radio aboard.", TextOf(await Call(client, "call", new() { ["to"] = "harbour" })));
        var slept = TextOf(await Call(client, "sleep", []));
        Assert.Contains("You turn in.", slept);
        Assert.Contains("up at", slept);
        Assert.Contains("Slept until", TextOf(await Call(client, "logbook", [])));
    }

    [Fact]
    public async Task Take_crewing_place_rings_the_owner_refuses_in_words_and_the_board_and_help_say_so()
    {
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains("owner", tools["take_crewing_place"]);
        Assert.Contains("crew = take_crewing_place", TextOf(await Call(client, "help", [])));
        Assert.Contains("Crew wanted: a 36 ft cutter", TextOf(await Call(client, "noticeboard", [])));

        Assert.Contains("Mari Heiane wants the coastal skipper ticket or a season's log", TextOf(await Call(client, "take_crewing_place", new() { ["card"] = "north" })));
        var taken = TextOf(await Call(client, "take_crewing_place", new() { ["card"] = "delivery" }));
        Assert.Contains("You ring Per Aune and say you will come", taken);
        Assert.Contains("[taken]", TextOf(await Call(client, "noticeboard", [])));
    }

    [Fact]
    public async Task Plan_event_marks_and_unmarks_an_event_and_the_calendar_and_the_plan_say_so()
    {
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains("stops on the morning", tools["plan_event"]);
        Assert.Contains("plan_event", tools["wait_week"]);
        Assert.Contains("calendar plan = plan_event", TextOf(await Call(client, "help", [])));

        Assert.Contains("not marked: a wait goes past it", TextOf(await Call(client, "calendar", [])));
        var marked = TextOf(await Call(client, "plan_event", new() { ["event"] = "regatta" }));
        Assert.Contains("You mark it on the calendar: The regatta party at Hestholmen, Saturday, day 152.", marked);
        Assert.Contains("going: a wait stops on its morning", TextOf(await Call(client, "calendar", [])));
        Assert.Contains("is marked already", TextOf(await Call(client, "plan_event", new() { ["event"] = "regatta" })));
        var off = TextOf(await Call(client, "plan_event", new() { ["event"] = "the regatta party", ["going"] = false }));
        Assert.Contains("You take the mark off", off);
        Assert.DoesNotContain("going: a wait stops", TextOf(await Call(client, "calendar", [])));
        Assert.Contains("There's nothing on the calendar called 'sailing'.", TextOf(await Call(client, "plan_event", new() { ["event"] = "sailing" })));
    }

    [Fact]
    public async Task The_friends_week_over_the_wire_asks_the_friend_first_then_shows_three_beats()
    {
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains("three beats", tools["invite_friend"]);
        Assert.Contains("cost vacation days as usual", tools["invite_friend"]);

        var before = TextOf(await Call(client, "calendar", []));
        Assert.Contains("ask your friend first (invite friend <day>, days 180 to 203)", before);
        Assert.DoesNotContain("A day trip to Hestholmen", before);

        var invited = TextOf(await Call(client, "invite_friend", new() { ["day"] = 190 }));
        Assert.Contains("Weekdays you spend with your friend cost vacation days as usual.", invited);
        var after = TextOf(await Call(client, "calendar", []));
        Assert.Contains("Your friend comes aboard, Tuesday, day 190, about 17:00: ahead", after);
        Assert.Contains("A day trip to Hestholmen, any day of the week", after);
        Assert.Contains("The last night aboard, Monday, day 196, about 22:00: ahead", after);
        Assert.DoesNotContain("ask your friend first", after);
    }

    [Fact]
    public async Task The_trip_tools_route_a_port_with_no_direct_route_by_the_port_in_between()
    {
        await using var client = await Connect();
        var plan = TextOf(await Call(client, "plan_trip", new() { ["destination"] = "Kvitnes" }));
        Assert.Contains("Skjærvik to Kvitnes by Hestholmen: 23 nm.", plan);
        Assert.Contains("Skjærvik to Hestholmen, 14 nm on 040; then Hestholmen to Kvitnes, 9 nm on 120.", plan);
        Assert.Contains("you depart again from Hestholmen", plan);
        Assert.DoesNotContain("There's no route", plan);
        var chart = TextOf(await Call(client, "chart", []));
        Assert.Contains("(by Hestholmen: 23 nm along the routes)", chart);
        Assert.DoesNotContain("(no route from here)", chart);
    }

    [Fact]
    public async Task Season_tools_book_refuse_and_answer_in_plain_words()
    {
        await using var client = await Connect();
        var season = TextOf(await Call(client, "season", []));
        Assert.Contains("The yard lifts boats from day 255 to day 300.", season);
        Assert.Contains("winter storage", season);

        await UntilNine(client); // the yard office opens at nine on a Saturday (design note 012)
        var booked = TextOf(await Call(client, "book_haulout", new() { ["day"] = 270 }));
        Assert.Contains("book the crane for day 270", booked);
        Assert.Contains("Haulout booked for day 270", TextOf(await Call(client, "season", [])));
        Assert.Contains("500 NOK", TextOf(await Call(client, "book_haulout", new() { ["day"] = 271 })));

        Assert.Contains("Your slot is day 271.", TextOf(await Call(client, "haul_out", [])));
        Assert.Contains("Clean the hull properly from the ground.", TextOf(await Call(client, "scrub", [])));
        Assert.Contains("Routes out: Hestholmen, 14 nm; Storholmen, 26 nm.", TextOf(await Call(client, "trip", [])));
        Assert.Contains("1200 NOK", TextOf(await Call(client, "yard_wash", [])));
    }

    [Fact]
    public async Task The_season_page_tool_says_there_is_none_yet_and_the_help_and_keep_descriptions_name_it()
    {
        await using var client = await Connect();
        Assert.Contains("No season page yet", TextOf(await Call(client, "season_page", [])));
        Assert.Contains("There is no page for season 2.", TextOf(await Call(client, "season_page", new() { ["season"] = 2 })));
        Assert.Contains("season page = season_page", TextOf(await Call(client, "help", [])));
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains("calendar repeats", tools["keep"]);
        Assert.Contains("autumn haulout", tools["season_page"]);
        var briefing = TextOf(await Call(client, "briefing", []));
        Assert.Contains("sell her or keep her for another year", briefing);
    }

    [Fact]
    public async Task Layup_tools_refuse_in_plain_words_off_the_hard_and_their_descriptions_quote_the_data()
    {
        var rules = Haulout.Sim.Data.BoatLoader.LoadSloop28().Layup;
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains($"day {rules.SpringDayOfYear}", tools["wait_until_spring"]);

        Assert.Contains("steps off", TextOf(await Call(client, "winterise_engine", [])));
        Assert.Contains("The battery is aboard already.", TextOf(await Call(client, "battery_aboard", [])));
        Assert.Contains("from on deck", TextOf(await Call(client, "fit_winter_cover", [])));
        Assert.Contains("battery", TextOf(await Call(client, "battery_ashore", [])), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("396 NOK", TextOf(await Call(client, "fill_tank", []))); // 18 litres of the 45 litre tank at 22 NOK (design note 010)
        Assert.Contains("The tank is full.", TextOf(await Call(client, "fill_tank", [])));
        var help = TextOf(await Call(client, "help", []));
        Assert.Contains("wait until spring = wait_until_spring", help);
        Assert.Contains("winterise = winterise_engine", help);
    }

    [Fact]
    public async Task Sale_tools_list_refuse_and_answer_in_plain_words_and_their_descriptions_quote_the_data()
    {
        var rules = Haulout.Sim.Data.BoatLoader.LoadSloop28().Sale;
        await using var client = await Connect();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description ?? "");
        Assert.Contains($"{rules.ListMinutes} minutes", tools["list_for_sale"]);
        Assert.Contains($"{rules.OfferHoldsDays} days", tools["list_for_sale"]);
        Assert.Contains("day 96", tools["keep"]);

        Assert.Contains("Not listed for sale", TextOf(await Call(client, "sale", [])));
        Assert.Contains("She isn't listed", TextOf(await Call(client, "accept_offer", [])));
        Assert.Contains("Name a price above zero", TextOf(await Call(client, "list_for_sale", new() { ["ask_nok"] = 0 })));
        Assert.Contains("opens at nine", TextOf(await Call(client, "list_for_sale", new() { ["ask_nok"] = 150000 }))); // the broker keeps the yard office's hours, on the hard too
        await UntilNine(client);
        var listed = TextOf(await Call(client, "list_for_sale", new() { ["ask_nok"] = 150000 }));
        Assert.Contains("Holm & Datter, yacht brokers", listed);
        Assert.Contains("Base value of a 28-footer of her kind: +180000 NOK", listed);
        Assert.Contains("The broker values her at", listed);
        Assert.Contains("You are asking 150000 NOK", listed);
        Assert.Contains("A buyer's surveyor will go over her for themselves.", listed);
        var sale = TextOf(await Call(client, "sale", []));
        Assert.Contains("Listed for sale at 150000 NOK.", sale);
        Assert.Contains("There's no offer open.", TextOf(await Call(client, "accept_offer", [])));
        Assert.Contains("There's no offer number 4.", TextOf(await Call(client, "accept_offer", new() { ["offer"] = 4 })));

        // Before the first launch there is no season to keep (the keep itself, and "kept her already", are tested in the Sim tests).
        Assert.Contains("You haven't had a season with her yet.", TextOf(await Call(client, "keep", [])));
        var help = TextOf(await Call(client, "help", []));
        Assert.Contains("list for sale = list_for_sale", help);
        Assert.Contains("accept = accept_offer", help);
    }

    [Fact]
    public async Task A_sale_ends_the_game_over_the_wire_and_the_queries_still_answer()
    {
        await using var client = await Connect();
        await UntilNine(client);
        await Call(client, "list_for_sale", new() { ["ask_nok"] = 100000 });
        for (int i = 0; i < 40 && !TextOf(await Call(client, "sale", [])).Contains("open until day", StringComparison.Ordinal); i++)
            await Call(client, "wait", new() { ["days"] = 1 });
        Assert.Contains("open until day", TextOf(await Call(client, "sale", [])));
        var card = TextOf(await Call(client, "accept_offer", []));
        Assert.Contains("Sold on day", card);
        Assert.Contains("Trips made: 0. Tows: 0.", card);
        Assert.Contains("The game is over", card);
        Assert.Contains("She's sold. The logbook is all that's left of her for you.", TextOf(await Call(client, "look", [])));
        Assert.Contains("She's sold.", TextOf(await Call(client, "status", [])));
        Assert.Contains("Viewing 1", TextOf(await Call(client, "sale", [])));
        Assert.Contains("(sale)", TextOf(await Call(client, "logbook", [])));
    }

    private static void AssertNoIdProperties(JsonElement el, string tool)
    {
        if (el.ValueKind == JsonValueKind.Object)
            foreach (var p in el.EnumerateObject())
            {
                var n = p.Name.ToLowerInvariant();
                Assert.False(n == "id" || n.EndsWith("id") || n.Contains("fault"), $"{tool}: property {p.Name}");
                AssertNoIdProperties(p.Value, tool);
            }
        else if (el.ValueKind == JsonValueKind.Array)
            foreach (var i in el.EnumerateArray()) AssertNoIdProperties(i, tool);
    }

    [Fact]
    public async Task Trace_file_records_one_line_per_call()
    {
        var path = Path.Combine(Path.GetTempPath(), $"haulout-trace-{Guid.NewGuid():N}.jsonl");
        try
        {
            await using (var client = await Connect(new() { ["HAULOUT_TRACE"] = path }))
            {
                await Call(client, "look", []);
                await Call(client, "go", new() { ["place"] = "companionway" });
                await Call(client, "status", []);
                await Call(client, "inspect", new() { ["component"] = "nothing like this" });
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(4, lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                var r = JsonDocument.Parse(lines[i]).RootElement;
                Assert.Equal(i + 1, r.GetProperty("seq").GetInt32());
                Assert.Equal(JsonValueKind.String, r.GetProperty("tool").ValueKind);
                Assert.Equal(JsonValueKind.Object, r.GetProperty("args").ValueKind);
                Assert.True(r.GetProperty("accepted").ValueKind is JsonValueKind.True or JsonValueKind.False);
                Assert.True(r.GetProperty("result").GetString()!.Length <= 300);
                Assert.True(r.GetProperty("atSea").ValueKind is JsonValueKind.True or JsonValueKind.False);
                Assert.True(r.GetProperty("sunk").ValueKind is JsonValueKind.True or JsonValueKind.False);
                Assert.Equal("Skjærvik", r.GetProperty("port").GetString());
                Assert.True(r.GetProperty("minutesSpent").GetInt32() >= 0);
                Assert.True(r.GetProperty("moneySpentNok").GetInt32() >= 0);
                Assert.True(r.GetProperty("engineRunning").ValueKind is JsonValueKind.True or JsonValueKind.False);
                Assert.True(r.GetProperty("finished").ValueKind is JsonValueKind.True or JsonValueKind.False);
            }
            var look = JsonDocument.Parse(lines[0]).RootElement;
            Assert.Equal(2, look.GetProperty("minutesSpent").GetInt32());
            var go = JsonDocument.Parse(lines[1]).RootElement;
            Assert.Equal("go", go.GetProperty("tool").GetString());
            Assert.Equal("companionway", go.GetProperty("args").GetProperty("place").GetString());
            // minutesSpent is the running total: look 2 + go 1.
            Assert.Equal(3, go.GetProperty("minutesSpent").GetInt32());
            Assert.False(JsonDocument.Parse(lines[3]).RootElement.GetProperty("accepted").GetBoolean());
            Assert.True(JsonDocument.Parse(lines[2]).RootElement.GetProperty("accepted").GetBoolean());

            // The wants (design note 010, step 4a): what score.py reads, all of it countable in the player's own logbook.
            var last = JsonDocument.Parse(lines[3]).RootElement;
            foreach (var number in new[] { "vacationDaysUsed", "unpaidDays", "paydays", "eventsMade", "eventsMissed", "upgradesFitted" })
                Assert.Equal(0, last.GetProperty(number).GetInt32());
            Assert.Equal(JsonValueKind.False, last.GetProperty("kept").ValueKind);

            // The club (design note 012, step 6): the tickets held (the licence from the start), the set, the days away crewing and on courses, the exams passed.
            Assert.Equal(["Boat licence"], last.GetProperty("ticketsHeld").EnumerateArray().Select(e => e.GetString()).ToArray());
            Assert.Equal(JsonValueKind.False, last.GetProperty("vhfFitted").ValueKind);
            foreach (var number in new[] { "crewingDays", "courseDays", "examsPassed" })
                Assert.Equal(0, last.GetProperty(number).GetInt32());
        }
        finally { File.Delete(path); }
    }
}
