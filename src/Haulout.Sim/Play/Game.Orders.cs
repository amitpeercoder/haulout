using Haulout.Sim.Components;
using Haulout.Sim.Data;

namespace Haulout.Sim.Play;

/// <summary>
/// Parts take time (design note 010, step 1). A part the yard chandlery does not keep on the shelf is ordered by phone and arrives in a
/// seeded number of days at the yard. Replace on such a part is refused with the order offered: a refusal never spends.
/// </summary>
public sealed partial class Game
{
    private const int SaltOrderLead = 9;
    private const string PartPrefix = "part:";

    private sealed class PartOrder
    {
        public int Number { get; init; }
        /// <summary>"starter motor": how the player names it.</summary>
        public string Name { get; init; } = "";
        public string? ComponentId { get; init; }
        public string? ItemId { get; init; }
        public int OrderedDay { get; init; }
        public int DueDay { get; init; }
        /// <summary>It came in (at 00:00 of its due day).</summary>
        public bool Arrived { get; set; }
        /// <summary>It is aboard in the spares.</summary>
        public bool Aboard { get; set; }
    }

    private readonly List<PartOrder> _orders = [];
    private int _orderCount;
    /// <summary>New parts aboard in their boxes, by component id.</summary>
    private readonly Dictionary<string, int> _partsAboard = [];

    /// <summary>A part the yard does not keep on the shelf (and orders exist at all).</summary>
    private bool Unstocked(Component c) => _boat.Orders.Enabled && !c.Spec.Stocked;

    private PartOrder? OutstandingFor(string componentId) =>
        _orders.FirstOrDefault(o => !o.Aboard && o.ComponentId == componentId);

    private ChandleryItem PartItem(string componentId) => new()
    {
        Id = PartPrefix + componentId,
        Name = _boat.Get(componentId).Name.ToLowerInvariant(),
        ForComponent = componentId,
    };

    private List<string> PartsAboardNames() =>
        _partsAboard.Where(kv => kv.Value > 0).Select(kv => _boat.Get(kv.Key).Name.ToLowerInvariant()).ToList();

    private string LeadWords => $"about {_boat.Orders.LeadLo} to {_boat.Orders.LeadHi} days";

    /// <summary>The refusal Replace gives for a part that is not on the shelf and not aboard: the order offered, or where the order stands.</summary>
    private string? UnstockedRefusal(Component c)
    {
        if (!Unstocked(c)) return null;
        string name = c.Name.ToLowerInvariant();
        if (OutstandingFor(c.Id) is { } order)
            return order.Arrived
                ? $"The new {name} is waiting for you at the yard."
                : $"The {name} is on order: due on day {order.DueDay}.";
        return $"The yard chandlery doesn't keep {WithArticle(name)} on the shelf. Order one (order {name}): {LeadWords}.";
    }

    /// <summary>
    /// Where the part for a DIY fix stands (design note 010, second pass I): a part the yard does not keep on the shelf is ordered first, and the
    /// readiness says so, with the lead from the rules. Null for a part off the shelf.
    /// </summary>
    private string? PartNote(Component c)
    {
        if (!Unstocked(c)) return null;
        if (_partsAboard.GetValueOrDefault(c.Id) > 0) return "the new part is aboard";
        if (OutstandingFor(c.Id) is { } order)
            return order.Arrived ? "waiting for you at the yard" : $"on order, due day {order.DueDay}";
        return $"not on the yard's shelf: order it first, {_boat.Orders.LeadLo} to {_boat.Orders.LeadHi} days";
    }

    // ------------------------------------------------------------------ the command

    private CommandResult DoOrder(Turn t, string item)
    {
        const string action = "order";
        if (AtSea) return Reject(action, "You're at sea: ring the yard from port.");
        if (!_boat.Orders.Enabled) return Reject(action, "The yard chandlery has everything on the shelf: buy it.");

        var r = FindItem(item);
        if (r.Kind == ResolutionKind.Ambiguous) return RejectResolution(action, r, item, i => i.Name);
        if (r.Match is { } shopItem)
        {
            if (shopItem.Stocked) return Reject(action, $"The yard chandlery keeps the {shopItem.Name} on the shelf: buy it.");
            return PlaceOrder(t, action, shopItem.Name, shopItem.PriceNok, null, shopItem.Id);
        }

        var seen = _boat.Components.Where(c => _seen.Contains(c.Id));
        var rc = NameResolver.Resolve(seen, item, c => c.Id, c => c.Name, c => c.Aliases);
        if (rc.Match is not { } comp)
            return rc.Kind == ResolutionKind.Ambiguous
                ? RejectResolution(action, rc, item, c => c.Name)
                : Reject(action, $"The yard chandlery doesn't have '{item.Trim()}', and you haven't found a part called that.");
        if (!comp.Spec.Stocked)
            return PlaceOrder(t, action, comp.Name.ToLowerInvariant(), comp.ReplaceCostNok, comp.Id, null);
        return Reject(action, $"The yard chandlery keeps the {comp.Name.ToLowerInvariant()} on the shelf: replace it.");
    }

    /// <summary>Why the order cannot be placed now (the command's own refusal, free and unlogged); null when it can.</summary>
    private string? OrderRefusal(string name, int price, string? componentId, string? itemId)
    {
        var existing = _orders.FirstOrDefault(o => !o.Aboard && (componentId is not null ? o.ComponentId == componentId : o.ItemId == itemId));
        if (existing is not null)
            return existing.Arrived
                ? $"The {name} is waiting for you at the yard already."
                : $"The {name} is on order already: due on day {existing.DueDay}.";
        if (componentId is not null && _partsAboard.GetValueOrDefault(componentId) > 0)
            return $"You have a new {name} aboard already.";
        if (itemId is not null && ItemById(itemId) is { Upgrade: not null } upgrade && _boat.TryGetUpgrade(upgrade.Upgrade, out var rule))
        {
            if (_boat.IsFitted(rule.Component.Id)) return $"Already fitted: the {name}.";
            if (_inventory.GetValueOrDefault(itemId) > 0) return $"You have the {name} aboard already.";
        }
        return MoneyRefusal(price);
    }

    /// <summary>What Order says to a chandlery item right now: at sea, with nothing to order, on the shelf, or the refusals of <see cref="OrderRefusal"/>.</summary>
    private string? ItemOrderRefusal(ChandleryItem item)
    {
        if (AtSea) return "You're at sea: ring the yard from port.";
        if (!_boat.Orders.Enabled) return "The yard chandlery has everything on the shelf: buy it.";
        if (item.Stocked) return $"The yard chandlery keeps the {item.Name} on the shelf: buy it.";
        return OrderRefusal(item.Name, item.PriceNok, null, item.Id);
    }

    private CommandResult PlaceOrder(Turn t, string action, string name, int price, string? componentId, string? itemId)
    {
        if (OrderRefusal(name, price, componentId, itemId) is { } refusal) return Reject(action, refusal);

        var rules = _boat.Orders;
        int number = _orderCount + 1;
        int span = rules.LeadHi - rules.LeadLo + 1;
        int lead = Math.Min(rules.LeadHi, rules.LeadLo + (int)(SeasonRoll(SaltOrderLead, number, Day) * span));
        int due = Day + lead;
        _orderCount = number;
        _orders.Add(new PartOrder { Number = number, Name = name, ComponentId = componentId, ItemId = itemId, OrderedDay = Day, DueDay = due });

        // An upgrade's name may be a plural ("marine heads", "bunk cushions"): no article to get wrong.
        string what = itemId is not null && ItemById(itemId)?.Upgrade is not null ? "the " + name : WithArticle(name);
        t.Narrative.Add(action == "buy"
            ? $"The yard chandlery doesn't keep {what} on the shelf: ordered, due on day {due}."
            : $"You ring the yard chandlery and order {what}: {Money(price)}, due on day {due}.");
        return Accept(t, action, rules.PhoneMinutes, price, null, $"Ordered {what} from the yard chandlery, due on day {due}.", work: false);
    }

    // ------------------------------------------------------------------ the delivery

    /// <summary>At 00:00 of the due day the part comes in: aboard if she is at the yard, else it waits for her there.</summary>
    private void DeliverOrders()
    {
        foreach (var o in _orders.Where(o => !o.Arrived && o.DueDay <= Day).ToList())
        {
            o.Arrived = true;
            _weekEvents.Add($"the {o.Name} came in");
            bool here = AtYard;
            if (here) PutAboard(o);
            RaiseCalendar($"delivery-{o.Number}",
                $"The yard chandlery rang: your {o.Name} has come in. " + (here ? "It's aboard." : "It's waiting for you at the yard."), 0);
        }
    }

    private bool AtYard => !AtSea && _boat.Orders.DeliverTo.Length > 0 && _portId == _boat.Orders.DeliverTo;

    private void PutAboard(PartOrder o)
    {
        o.Aboard = true;
        if (o.ComponentId is { } id) _partsAboard[id] = _partsAboard.GetValueOrDefault(id) + 1;
        else if (o.ItemId is { } item) _inventory[item] = _inventory.GetValueOrDefault(item) + 1;
    }

    /// <summary>A part that came in while she was elsewhere goes aboard on the next arrival at, or command at, the yard.</summary>
    private void CollectWaiting(Turn? t)
    {
        if (_orders.Count == 0 || !AtYard) return;
        foreach (var o in _orders.Where(o => o.Arrived && !o.Aboard).ToList())
        {
            PutAboard(o);
            string line = $"At the yard: the {o.Name} that came in is put aboard.";
            t?.Narrative.Add(line);
            _log.Add(new LogEntry(Day, TimeOfDay, line));
        }
    }
}
