using Haulout.Sim.Components;
using Haulout.Sim.Data;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Play;

/// <summary>
/// Upgrades are components (design note 010, step 3). A heater, a heads, a stove, a plotter, a radar, cushions and varnish are chandlery
/// items that are ordered like any part the yard does not keep, and fitted by the player (Install: their own work, in the right place) or
/// by the yard (YardInstall: labour and days). Once fitted an upgrade is an ordinary component at the end of the list; nothing that ran
/// before the fit has seen it, and nothing here draws from the world or the player's random.
/// </summary>
public sealed partial class Game
{
    /// <summary>The day each upgrade was fitted, and the season (the number of times she has been kept) it was fitted in.</summary>
    private readonly Dictionary<string, (int Day, int Season)> _fitted = [];

    private ChandleryItem? ItemForUpgrade(string upgradeId) => _boat.Chandlery.FirstOrDefault(i => i.Upgrade == upgradeId);

    private int AboardCount(ChandleryItem item) => _inventory.GetValueOrDefault(item.Id);

    private PartOrder? OutstandingForItem(string itemId) => _orders.FirstOrDefault(o => !o.Aboard && o.ItemId == itemId);

    /// <summary>The upgrade the player named (an item of the chandlery that fits one), or the refusal.</summary>
    private bool ResolveUpgrade(string action, string name, out ChandleryItem item, out UpgradeRule rule, out CommandResult? rejected)
    {
        item = null!;
        rule = null!;
        rejected = null;
        var r = FindItem(name);
        if (r.Kind == ResolutionKind.Ambiguous) { rejected = RejectResolution(action, r, name, i => i.Name); return false; }
        if (r.Match is not { } found)
        {
            rejected = Reject(action, $"The chandlery doesn't have '{name.Trim()}'.");
            return false;
        }
        if (found.Upgrade is null || !_boat.TryGetUpgrade(found.Upgrade, out var u))
        {
            rejected = Reject(action, found.Tool || found.ForComponent is null
                ? $"The {found.Name} is not something to fit to the boat."
                : $"The {found.Name} is a spare to replace a worn part with: replace it.");
            return false;
        }
        item = found;
        rule = u;
        return true;
    }

    private string HowToGet(ChandleryItem item) =>
        _boat.Orders.Enabled ? $"Order one: order {item.Name}." : $"Buy one: buy {item.Name}.";

    /// <summary>Why the player cannot fit the item yet; null when they can. Free and unlogged.</summary>
    private string? InstallRefusal(ChandleryItem item, UpgradeRule rule)
    {
        if (AtSea) return "You're at sea: fit it in harbour.";
        string name = item.Name;
        if (_boat.IsFitted(rule.Component.Id)) return $"Already fitted: the {name}.";
        if (AboardCount(item) == 0)
        {
            if (OutstandingForItem(item.Id) is { } order)
                return order.Arrived
                    ? $"Waiting for you at the yard: the {name}."
                    : $"On order: the {name}, due on day {order.DueDay}.";
            return $"You don't have the {name} aboard. {HowToGet(item)}";
        }
        var place = _boat.GetPlace(rule.PlaceId);
        if (_placeId != rule.PlaceId) return $"Fitting the {name} is done {Words.In(place)}: go there first.";
        if (rule.Component.Spec.NeedsDry && Today.RainMm >= _boat.Rates.WeatherGates.RainMm)
            return $"It's raining ({WeatherWords.RainWord(Today)}, {WeatherWords.Temperature(Today)}): the {name} won't take in the wet. Wait for a dry day.";
        return null;
    }

    private void FitUpgrade(UpgradeRule rule, int day)
    {
        // What it replaces goes off the boat with its faults: they are put right (the part is gone), so the known list shows them as done.
        foreach (var old in rule.Replaces)
            if (_boat.TryGet(old, out var oldComponent))
                foreach (var f in oldComponent.Faults.Where(f => f.Present).ToList()) f.Clear();
        _boat.Fit(rule.Component.Id);
        _seen.Add(rule.Component.Id);
        foreach (var part in rule.Also) _seen.Add(part.Id); // the cable you taped down the mast is a part you have seen
        _fitted[rule.Component.Id] = (day, _seasonIndex);
        // A fault an upgrade may carry starts its clock now. Its spread is not drawn (the world's stream is not touched by a fit): it ages at 1.0.
        foreach (var f in rule.Component.Faults) f.ActivateOn(day);
        _water.Refresh();
    }

    private CommandResult DoInstall(Turn t, string name)
    {
        const string action = "install";
        if (!ResolveUpgrade(action, name, out var item, out var rule, out var rejected)) return rejected!;
        if (InstallRefusal(item, rule) is { } refusal) return Reject(action, refusal);

        ShutDownEngine(t);
        _inventory[item.Id] = AboardCount(item) - 1;
        if (_inventory[item.Id] <= 0) _inventory.Remove(item.Id);
        string text = rule.InstallText.Length > 0 ? rule.InstallText : $"You fit the {item.Name}.";
        t.Narrative.Add(text);
        string logName = rule.Component.Name.ToLowerInvariant();
        return Accept(t, action, rule.InstallMinutes, 0, new RepairView(rule.Component.Name, text), $"Fitted the {logName}.",
            afterTick: () =>
            {
                FitUpgrade(rule, Day);
                return new RepairView(rule.Component.Name, text);
            });
    }

    // ------------------------------------------------------------------ the yard

    /// <summary>What the yard bills to fit it: the labour. The part must be aboard (money does not skip the order's wait).</summary>
    private static int YardInstallNok(UpgradeRule rule) => rule.YardNok ?? 0;

    private string? YardInstallRefusal(ChandleryItem item, UpgradeRule rule)
    {
        if (YardGate() is { } away) return away;
        if (rule.YardNok is null || rule.YardDays is null) return "That's a job for you, not the yard.";
        if (_boat.IsFitted(rule.Component.Id)) return $"Already fitted: the {item.Name}.";
        if (AboardCount(item) == 0)
        {
            if (OutstandingForItem(item.Id) is { Arrived: false } order)
                return $"On order already: the {item.Name}, due on day {order.DueDay}. Ask the yard again once it has come in.";
            return _boat.Orders.Enabled
                ? $"The yard needs the part first: order it (order {item.Name}), and they'll fit it when it's here."
                : $"The yard needs the part first: buy it (buy {item.Name}), and they'll fit it.";
        }
        return MoneyRefusal(YardInstallNok(rule));
    }

    private CommandResult DoYardInstall(Turn t, string name)
    {
        const string action = "yard-install";
        if (!ResolveUpgrade(action, name, out var item, out var rule, out var rejected)) return rejected!;
        if (YardInstallRefusal(item, rule) is { } refusal) return Reject(action, refusal);

        ShutDownEngine(t);
        int days = rule.YardDays!.Value;
        int nok = YardInstallNok(rule);
        _inventory[item.Id] = AboardCount(item) - 1;
        if (_inventory[item.Id] <= 0) _inventory.Remove(item.Id);
        string text = $"The yard fits the {item.Name} you brought: {Money(nok)} of labour. {Days(days)} later it's done.";
        t.Narrative.Add(text);
        return Accept(t, action, days * 1440, nok, new RepairView(rule.Component.Name, text),
            $"The yard fitted the {rule.Component.Name.ToLowerInvariant()}.", work: false,
            afterTick: () =>
            {
                FitUpgrade(rule, Day);
                return new RepairView(rule.Component.Name, text);
            });
    }

    // ------------------------------------------------------------------ the query

    private string UpgradeStatus(ChandleryItem item, UpgradeRule rule)
    {
        if (_boat.IsFitted(rule.Component.Id) && _fitted.TryGetValue(rule.Component.Id, out var f)) return $"fitted on day {f.Day}";
        if (AboardCount(item) > 0) return "aboard, not fitted";
        if (OutstandingForItem(item.Id) is { } order) return order.Arrived ? "waiting at the yard" : $"on order, due day {order.DueDay}";
        return "not aboard";
    }

    private List<UpgradeView> UpgradeViews() =>
        _boat.Upgrades
            .Select(u => (Rule: u, Item: ItemForUpgrade(u.Component.Id)))
            .Where(x => x.Item is not null)
            .Select(x => new UpgradeView(
                x.Item!.Name, UpgradeStatus(x.Item, x.Rule), x.Item.PriceNok, x.Rule.InstallMinutes, x.Rule.YardNok, x.Rule.YardDays,
                x.Rule.Opens, x.Rule.ValueNok, ItemOrderRefusal(x.Item), InstallRefusal(x.Item, x.Rule),
                x.Rule.YardNok is null || x.Rule.YardDays is null ? null : YardInstallRefusal(x.Item, x.Rule)))
            .ToList();

    private UpgradesView UpgradesSnapshot() => new(UpgradeViews());

    /// <summary>The broker's lines for the upgrades fitted: what each is worth at its condition, to 100 units.</summary>
    private IEnumerable<ValuationLine> UpgradeLines()
    {
        foreach (var c in _boat.Components)
        {
            if (!_boat.TryGetUpgrade(c.Id, out var u) || !_fitted.TryGetValue(c.Id, out var f)) continue;
            int nok = (int)Math.Round(u.ValueNok * c.Condition / 100.0, MidpointRounding.AwayFromZero) * 100;
            yield return new ValuationLine($"{c.Name}, fitted {(f.Season == _seasonIndex ? "this season" : "earlier")}", nok);
        }
    }
}
