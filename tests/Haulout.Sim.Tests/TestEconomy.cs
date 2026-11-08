using System.Text.Json.Nodes;
using Haulout.Sim.Data;

namespace Haulout.Sim.Tests;

/// <summary>
/// The embedded economy.json as text, for tests that change a money or time knob. The boat file no longer holds those knobs (design note
/// 010, second pass A), so the edit goes into the economy text and the boat is loaded with it: <c>BoatLoader.Load(boatJson, true, TestEconomy.Edited(...))</c>.
/// </summary>
internal static class TestEconomy
{
    public static string Text()
    {
        using var stream = typeof(BoatLoader).Assembly.GetManifestResourceStream("Haulout.Sim.Data.economy.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The economy text with one exact substring replaced; the substring must be there (a silent no-op would test nothing).</summary>
    public static string Replace(string from, string to)
    {
        var text = Text();
        if (!text.Contains(from, StringComparison.Ordinal)) throw new InvalidOperationException($"economy.json has no '{from}'.");
        return text.Replace(from, to);
    }

    /// <summary>The economy as a JSON tree, edited, as text.</summary>
    public static string Edited(Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(Text())!;
        edit(node);
        return node.ToJsonString();
    }
}
