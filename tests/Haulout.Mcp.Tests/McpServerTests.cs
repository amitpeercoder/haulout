using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Haulout.Mcp.Tests;

public class McpServerTests
{
    private static readonly string[] ExpectedTools =
    [
        "look", "open", "inspect", "measure", "clean", "replace", "charge", "buy",
        "check_filter", "check_flow", "start_engine",
        "status", "logbook", "aboard", "help", "briefing",
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

    private static IEnumerable<string> HyphenatedIds()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Haulout.Sim", "Data", "sloop28.json")));
        var ids = new List<string>();
        foreach (var comp in doc.RootElement.GetProperty("compartments").EnumerateArray())
            ids.Add(comp.GetProperty("id").GetString()!);
        foreach (var c in doc.RootElement.GetProperty("components").EnumerateArray())
        {
            ids.Add(c.GetProperty("id").GetString()!);
            if (c.TryGetProperty("faults", out var faults))
                foreach (var f in faults.EnumerateArray())
                    if (f.TryGetProperty("id", out var fid)) ids.Add(fid.GetString()!);
        }
        return ids.Where(i => i.Contains('-')).Distinct();
    }

    [Fact]
    public async Task Lists_all_sixteen_tools()
    {
        await using var client = await Connect();
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        foreach (var n in ExpectedTools) Assert.Contains(n, names);
        Assert.Equal(ExpectedTools.Length, names.Count);
    }

    [Fact]
    public async Task Tool_results_are_prose_without_ids_or_structured_content()
    {
        var ids = HyphenatedIds().ToList();
        Assert.NotEmpty(ids);
        await using var client = await Connect();

        var calls = new (string Tool, Dictionary<string, object?> Args)[]
        {
            ("briefing", []),
            ("help", []),
            ("start_engine", []),
            ("open", new() { ["compartment"] = "battery box" }),
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
            foreach (var id in ids) Assert.DoesNotContain(id, text, StringComparison.OrdinalIgnoreCase);
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
                await Call(client, "open", new() { ["compartment"] = "battery box" });
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
            var open = JsonDocument.Parse(lines[1]).RootElement;
            Assert.Equal("open", open.GetProperty("tool").GetString());
            Assert.Equal("battery box", open.GetProperty("args").GetProperty("compartment").GetString());
            Assert.Equal(4, open.GetProperty("minutesSpent").GetInt32());
            Assert.False(JsonDocument.Parse(lines[3]).RootElement.GetProperty("accepted").GetBoolean());
            Assert.True(JsonDocument.Parse(lines[2]).RootElement.GetProperty("accepted").GetBoolean());
        }
        finally { File.Delete(path); }
    }
}
