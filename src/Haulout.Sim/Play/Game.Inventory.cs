using Haulout.Sim.Components;
using Haulout.Sim.Data;

namespace Haulout.Sim.Play;

/// <summary>What is aboard: tools and spares, bought at the chandlery (design note 007, "Inventory").</summary>
public sealed partial class Game
{
    private const string ChandleryAshoreMessage = "The chandlery is ashore; out here you have what's aboard.";

    /// <summary>Chandlery item id to how many are aboard (tools: 0 or 1). Entries at 0 are removed.</summary>
    private readonly Dictionary<string, int> _inventory = [];

    private bool OwnsButton => _inventory.GetValueOrDefault(Shop.RemoteStartButtonId) > 0;

    private List<string> ToolNames() =>
        _boat.Chandlery.Where(i => i.Tool && _inventory.GetValueOrDefault(i.Id) > 0).Select(i => i.Name).ToList();

    private ChandleryItem? ItemById(string id) => _boat.Chandlery.FirstOrDefault(i => i.Id == id);

    /// <summary>Name or alias as the player would say it; "a ", "the " and "spare " in front are ignored.</summary>
    private Resolution<ChandleryItem> FindItem(string name)
    {
        var n = NameResolver.Normalize(name);
        if (n.StartsWith("a ", StringComparison.Ordinal)) n = n[2..].TrimStart();
        if (n.StartsWith("spare ", StringComparison.Ordinal)) n = n[6..].TrimStart();
        return NameResolver.Resolve(_boat.Chandlery, n, i => i.Id, i => i.Name, i => i.Aliases);
    }

    /// <summary>The spare aboard that replaces this component, if any.</summary>
    private ChandleryItem? SpareFor(string componentId) =>
        _boat.Chandlery.FirstOrDefault(i => !i.Tool && i.ForComponent == componentId && _inventory.GetValueOrDefault(i.Id) > 0);

    private void ConsumeSpare(ChandleryItem item)
    {
        int left = _inventory.GetValueOrDefault(item.Id) - 1;
        if (left <= 0) _inventory.Remove(item.Id); else _inventory[item.Id] = left;
    }

    /// <summary>At sea nothing is bought: Replace there needs a spare aboard. Null when she is in port.</summary>
    private string? SpareRefusal(Component c)
    {
        if (!AtSea || SpareFor(c.Id) is not null) return null;
        var fits = _boat.Chandlery.FirstOrDefault(i => !i.Tool && i.ForComponent == c.Id);
        return fits is null
            ? $"There's no spare for the {c.Name.ToLowerInvariant()} aboard."
            : $"You don't have a spare {fits.Name} aboard.";
    }

    /// <summary>The refusals before Replace: at sea the spare, in port the yard rule for work below the waterline.</summary>
    private string? ReplaceGate(Component c) => AtSea ? SeaWorkRefusal() ?? SpareRefusal(c) : AfloatRefusal(c);

    private InventoryView InventorySnapshot()
    {
        var aboard = _boat.Chandlery.Where(i => _inventory.GetValueOrDefault(i.Id) > 0)
            .Select(i => new InventoryLine(i.Name, _inventory[i.Id], i.Tool)).ToList();
        if (AtSea) return new InventoryView(aboard, null, []);
        return new InventoryView(aboard, ShopName, _boat.Chandlery.Select(i => new ShopLine(i.Name, i.PriceNok, i.Minutes)).ToList());
    }

    private string ShopName => CurrentPort?.ShopName ?? "the chandlery";

    private static string WithArticle(string name) =>
        name.Contains('(') ? name : (("aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an " : "a ") + name);

    private CommandResult DoBuy(Turn t, string item)
    {
        if (AtSea) return Reject("buy", ChandleryAshoreMessage);
        var r = FindItem(item);
        if (r.Match is not { } shopItem)
            return r.Kind == ResolutionKind.Ambiguous
                ? RejectResolution("buy", r, item, i => i.Name)
                : Reject("buy", $"The chandlery doesn't have '{item.Trim()}'.");
        if (shopItem.Tool && _inventory.GetValueOrDefault(shopItem.Id) > 0) return Reject("buy", "You already have one.");
        if (MoneyRefusal(shopItem.PriceNok) is { } broke) return Reject("buy", broke);

        _inventory[shopItem.Id] = _inventory.GetValueOrDefault(shopItem.Id) + 1;
        t.Narrative.Add($"You walk over to {ShopName} and buy {WithArticle(shopItem.Name)}.");
        return Accept(t, "buy", shopItem.Minutes, shopItem.PriceNok, null, shopItem.LogText ?? $"Bought {WithArticle(shopItem.Name)}.");
    }
}
