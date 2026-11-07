namespace Haulout.Sim.Play;

/// <summary>One thing the yard chandlery sells. Data, not logic.</summary>
public sealed record ShopItem(string Name, string[] Aliases, int CostNok, int Minutes, string LogText);

/// <summary>The yard chandlery's catalogue. Placeholder prices and walking time (design note 001, decision 7).</summary>
public static class Shop
{
    public const string RemoteStartButton = "remote start button";

    public static readonly IReadOnlyList<ShopItem> Catalogue =
    [
        new(RemoteStartButton, ["remote start", "remote", "button"], 400, 30, "Bought a remote start button."),
    ];

    public static ShopItem? Find(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n.StartsWith("a ")) n = n[2..];
        else if (n.StartsWith("the ")) n = n[4..];
        return Catalogue.FirstOrDefault(i =>
            i.Name.Equals(n, StringComparison.OrdinalIgnoreCase)
            || i.Aliases.Any(a => a.Equals(n, StringComparison.OrdinalIgnoreCase)));
    }
}
