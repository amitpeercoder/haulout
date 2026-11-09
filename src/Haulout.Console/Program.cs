using Haulout.Sim.Play;

// `--seed N` picks the seeded run; the default is the game's own. Scripts in tools/playtest rely on the default.
int seed = Game.DefaultSeed;
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] == "--seed" && int.TryParse(args[i + 1], out var s)) seed = s;
var game = Game.NewFirstFault(seed, withHours: true);
var prose = new Haulout.Prose.Prose(game.Currency);

if (game.Ask(new Briefing()) is BriefingView briefing) Console.Write(prose.Briefing(briefing));

while (true)
{
    Console.Write("> ");
    var line = Console.In.ReadLine();
    if (line is null) return 0; // EOF: leave quietly.

    switch (WordParser.Parse(line))
    {
        case null:
            break;
        case Quit:
            return 0;
        case Problem p:
            Console.WriteLine(p.Message);
            break;
        case RunQuery q:
            Console.Write(prose.Query(game.Ask(q.Query)));
            break;
        case RunCommand c:
            var result = game.Execute(c.Command);
            var status = (StatusView)game.Ask(new Status());
            Console.Write(prose.Result(result, status));
            break;
    }
}
