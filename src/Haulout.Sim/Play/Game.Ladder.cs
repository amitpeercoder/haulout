using System.Globalization;
using Haulout.Sim.Data;

namespace Haulout.Sim.Play;

/// <summary>
/// The broker's window (design note 012, step 5; design note 011, decisions 1 and 2): the player's own boat and four listings from ladder.json, at the bottom of
/// the club's board. The yard's quotes are the yard's, not the sim's truth; the shortfall is money and tickets in words. None is buyable, so there is no command.
/// </summary>
public sealed partial class Game
{
    private LadderRules Ladder => _boat.Board.Ladder;

    private string LW(string key) => Ladder.Word(key);

    private IReadOnlyList<LadderCard>? LadderCards()
    {
        var rules = Ladder;
        if (!rules.Enabled || _sold) return null;
        var ci = CultureInfo.InvariantCulture;
        var cards = new List<LadderCard>();

        // Her own bills, as the player pays them: known. Her figure, as the broker would put it in a letter today: only what the player knows.
        int figure = BuildLetter(Day).ValueNok;
        var own = new List<LadderCost>();
        var bills = _boat.Calendar?.Bills ?? [];
        if (bills.FirstOrDefault(b => b.Id == "berth") is { } berth) own.Add(new LadderCost(LW("labelBerth"), berth.Nok * berth.DaysOfYear.Count));
        if (bills.FirstOrDefault(b => b.Id == "insurance") is { } insurance) own.Add(new LadderCost(LW("labelInsurance"), insurance.Nok * insurance.DaysOfYear.Count));
        int lift = _boat.Yard.LiftNok + _boat.Yard.StorageNok;
        if (lift > 0) own.Add(new LadderCost(LW("labelLift"), lift));
        var held = _boat.Tickets.Find(rules.Yours.Ticket);
        string ownTicket = held is null ? "" : Fill(LW("ownTicket"), ("ticket", held.AskName));
        string ownLine = Fill(LW("ownLine"), ("type", rules.Yours.Type), ("value", Money(figure)));
        string ownCosts = own.Count == 0 ? "" : Fill(LW("ownCosts"), ("costs", CostsWords(own)));
        cards.Add(new LadderCard(rules.Yours.Type, true, rules.Yours.LengthFt, rules.Yours.LengthM, rules.Yours.Type, 0, figure, "",
            string.Join(" ", new[] { ownLine, ownCosts, ownTicket }.Where(s => s.Length > 0)), own, own.Sum(c => c.Nok), ownTicket, []));

        foreach (var b in rules.Boats)
        {
            var costs = b.Costs.Select(c => new LadderCost(c.Label, c.Nok)).ToList();
            int total = costs.Sum(c => c.Nok);
            var ticket = _boat.Tickets.Find(b.Ticket);
            string ticketName = ticket?.AskName ?? b.Ticket;
            int short_ = b.AskingNok - (Balance + figure);
            var shortfall = new List<string>
            {
                short_ > 0
                    ? Fill(LW("moneyShort"), ("short", Money(short_)), ("price", Money(b.AskingNok)), ("balance", Money(Balance)), ("value", Money(figure)))
                    : LW("moneyEnough"),
                Fill(ticket is not null && Holds(ticket) ? LW("ticketHave") : LW("ticketLack"), ("ticket", ticketName)),
            };
            string line = Fill(LW("line"), ("name", b.Name), ("yard", b.Yard), ("lengthFt", b.LengthFt.ToString(ci)), ("lengthM", b.LengthM.ToString("0.0", ci)),
                ("type", b.Type), ("year", b.Year.ToString(ci)), ("lies", b.Lies), ("price", Money(b.AskingNok)), ("pro", b.Pro), ("con", b.Con));
            line += " " + Fill(LW("costs"), ("costs", CostsWords(costs)), ("total", Money(total)));
            cards.Add(new LadderCard(b.Name, false, b.LengthFt, b.LengthM, b.Type, b.Year, b.AskingNok, b.Lies, line, costs, total, ticketName, shortfall));
        }
        return cards;
    }

    private string CostsWords(IEnumerable<LadderCost> costs) =>
        string.Join(", ", costs.Select(c => Fill(LW("costItem"), ("label", c.Label), ("nok", Money(c.Nok)))));
}
