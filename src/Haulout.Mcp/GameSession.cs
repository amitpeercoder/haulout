using System.Text.Json;
using Haulout.Prose;
using Haulout.Sim.Play;

namespace Haulout.Mcp;

/// <summary>One game per process. Serialises calls and appends the optional harness trace.</summary>
public sealed class GameSession
{
    private readonly Game _game;
    private readonly Prose.Prose _prose;
    private readonly string? _tracePath;
    private readonly object _lock = new();
    private int _seq;
    /// <summary>Vacation days used in earlier calendar years of this run (the game's own count starts again every new year), and the last count seen.</summary>
    private int _vacationBefore;
    private int _vacationLast;
    /// <summary>How much of each result the trace keeps: enough for an arrival, tow or sinking card to show.</summary>
    private const int TraceResultChars = 300;

    public GameSession(Game game, string? tracePath)
    {
        _game = game;
        _prose = new Prose.Prose(game.Currency);
        _tracePath = string.IsNullOrWhiteSpace(tracePath) ? null : tracePath;
    }

    public string Do(string tool, Command command, params (string Name, string Value)[] args) =>
        Run(tool, args, () =>
        {
            var result = _game.Execute(command);
            var status = (StatusView)_game.Ask(new Status());
            return (_prose.Result(result, status), result.Accepted);
        });

    public string Ask(string tool, Query query) =>
        Run(tool, [], () =>
        {
            var answer = _game.Ask(query);
            var text = answer is BriefingView b ? _prose.Briefing(b, includeHelpHint: false) : _prose.Query(answer);
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
            // The wants (design note 010): everything here is something a player can count in their own logbook and calendar.
            var calendar = (CalendarView)_game.Ask(new Calendar());
            var season = (SeasonView)_game.Ask(new Season());
            var upgrades = (UpgradesView)_game.Ask(new Upgrades());
            // The club (design note 012): what the player can read on the board, in their tickets and in their own logbook.
            var tickets = (TicketsView)_game.Ask(new Tickets());
            var board = (NoticeboardView)_game.Ask(new Noticeboard());
            var log = (LogView)_game.Ask(new Logbook());
            if (calendar.VacationDaysUsed < _vacationLast) _vacationBefore += _vacationLast;
            _vacationLast = calendar.VacationDaysUsed;
            var line = JsonSerializer.Serialize(new
            {
                seq = ++_seq,
                tool,
                args = argMap,
                accepted,
                result = text.Length <= TraceResultChars ? text : text[..TraceResultChars],
                minutesSpent = status.MinutesSpent,
                moneySpentNok = status.MoneySpentNok,
                balanceNok = status.BalanceNok,
                launched = status.Launched,
                engineRunning = status.EngineRunning,
                finished = status.Finished,
                day = status.Day,
                port = status.Port,
                atSea = status.AtSea,
                sunk = status.Sunk,
                sold = status.Sold,
                vacationDaysUsed = _vacationBefore + calendar.VacationDaysUsed,
                unpaidDays = calendar.UnpaidDays,
                paydays = calendar.PaydaysSeen,
                eventsMade = calendar.EventsMade,
                eventsMissed = calendar.EventsMissed,
                upgradesFitted = upgrades.Items.Count(i => i.Status.StartsWith("fitted", StringComparison.Ordinal)),
                kept = season.Kept,
                ticketsHeld = tickets.Tickets.Where(t => t.Held).Select(t => t.Name).ToArray(),
                vhfFitted = upgrades.Items.Any(i => i.Name.Contains("VHF", StringComparison.Ordinal) && i.Status.StartsWith("fitted", StringComparison.Ordinal)),
                crewingDays = board.Cards.OfType<CrewingCard>().Where(c => c.Status == "done").Sum(c => c.Days),
                courseDays = board.Cards.OfType<CourseCard>().Where(c => c.Status == "done").Sum(c => c.Days),
                examsPassed = log.Entries.Count(e => e.Text.StartsWith("Sat the exam of", StringComparison.Ordinal) && e.Text.EndsWith(", passed.", StringComparison.Ordinal)),
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
