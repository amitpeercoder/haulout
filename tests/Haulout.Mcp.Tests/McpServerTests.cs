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
        foreach (var section in new[] { "compartments", "places", "covers", "positions", "components" })
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
    public async Task Emergency_lift_tool_description_quotes_the_data_numbers()
    {
        var eco = Haulout.Sim.Data.BoatLoader.LoadSloop28().Economy;
        Assert.Equal(32, ExpectedTools.Length);
        await using var client = await Connect();
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "emergency_lift");
        Assert.Contains($"{eco.EmergencyLiftNok} NOK", tool.Description);
        Assert.Contains($"{eco.EmergencyLiftDays} days", tool.Description);
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
                Assert.True(r.GetProperty("result").GetString()!.Length <= 80);
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
        }
        finally { File.Delete(path); }
    }
}
