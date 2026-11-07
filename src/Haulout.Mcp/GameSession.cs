using System.Text.Json;
using Haulout.Prose;
using Haulout.Sim.Play;

namespace Haulout.Mcp;

/// <summary>One game per process. Serialises calls and appends the optional harness trace.</summary>
public sealed class GameSession
{
    private readonly Game _game;
    private readonly string? _tracePath;
    private readonly object _lock = new();
    private int _seq;

    public GameSession(Game game, string? tracePath)
    {
        _game = game;
        _tracePath = string.IsNullOrWhiteSpace(tracePath) ? null : tracePath;
    }

    public string Do(string tool, Command command, params (string Name, string Value)[] args) =>
        Run(tool, args, () =>
        {
            var result = _game.Execute(command);
            var status = (StatusView)_game.Ask(new Status());
            return (Prose.Prose.Result(result, status), result.Accepted);
        });

    public string Ask(string tool, Query query) =>
        Run(tool, [], () =>
        {
            var answer = _game.Ask(query);
            var text = answer is BriefingView b ? Prose.Prose.Briefing(b, includeHelpHint: false) : Prose.Prose.Query(answer);
            return (text, true);
        });

    private string Run(string tool, (string Name, string Value)[] args, Func<(string Text, bool Accepted)> action)
    {
        lock (_lock)
        {
            var (text, accepted) = action();
            WriteTrace(tool, args, text, accepted);
            return text;
        }
    }

    private void WriteTrace(string tool, (string Name, string Value)[] args, string text, bool accepted)
    {
        if (_tracePath is null) return;
        try
        {
            var status = (StatusView)_game.Ask(new Status());
            var argMap = args.ToDictionary(a => a.Name, a => a.Value);
            var line = JsonSerializer.Serialize(new
            {
                seq = ++_seq,
                tool,
                args = argMap,
                accepted,
                result = text.Length <= 80 ? text : text[..80],
                minutesSpent = status.MinutesSpent,
                moneySpentNok = status.MoneySpentNok,
                engineRunning = status.EngineRunning,
                finished = status.Finished,
            });
            using var w = new StreamWriter(_tracePath, append: true);
            w.WriteLine(line);
            w.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"trace write failed: {ex.Message}");
        }
    }
}
