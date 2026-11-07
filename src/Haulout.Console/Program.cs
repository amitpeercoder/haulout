using Haulout.Sim.Play;

var game = Game.NewFirstFault();

if (game.Ask(new Briefing()) is BriefingView briefing) Console.Write(Haulout.Prose.Prose.Briefing(briefing));

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
            Console.Write(Haulout.Prose.Prose.Query(game.Ask(q.Query)));
            break;
        case RunCommand c:
            var result = game.Execute(c.Command);
            var status = (StatusView)game.Ask(new Status());
            Console.Write(Haulout.Prose.Prose.Result(result, status));
            if (result.Finished) return 0;
            break;
    }
}
