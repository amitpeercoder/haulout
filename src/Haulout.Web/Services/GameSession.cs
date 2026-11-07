using Haulout.Sim.Play;

namespace Haulout.Web.Services;

/// <summary>
/// The web adapter's one door into the sim. Holds the Game, forwards commands and queries,
/// and remembers what the UI needs between clicks (selection, last result, what the player read).
/// Nothing here knows more than the facade tells a player.
/// </summary>
public sealed class GameSession
{
    private readonly Game _game = Game.NewFirstFault();
    private readonly Dictionary<string, string> _lastInspect = new();
    private readonly Dictionary<string, string> _lastMeter = new();

    /// <summary>Raised after every command and every selection change.</summary>
    public event Action? Changed;

    /// <summary>The player has dismissed the briefing and looked around.</summary>
    public bool IsAboard { get; private set; }

    public CommandResult? LastResult { get; private set; }

    /// <summary>Component id (facade id) of the selected component, if any.</summary>
    public string? SelectedComponentId { get; private set; }

    /// <summary>Compartment id last clicked, if any.</summary>
    public string? SelectedCompartmentId { get; private set; }

    public bool Finished => Status.Finished;

    /// <summary>The narrative of the start attempt that finished the slice; kept so the closing stays readable.</summary>
    public IReadOnlyList<string>? ClosingNarrative { get; private set; }

    // ------------------------------------------------------------- queries

    public T Ask<T>(Query query) where T : QueryResult => (T)_game.Ask(query);

    public BriefingView Briefing => Ask<BriefingView>(new Briefing());
    public StatusView Status => Ask<StatusView>(new Status());
    public LogView Log => Ask<LogView>(new Logbook());
    public AboardView Aboard => Ask<AboardView>(new Aboard());

    public ComponentActionsView ActionsFor(string componentId) =>
        Ask<ComponentActionsView>(new ComponentActions(componentId));

    public bool HasTool(string tool) => Status.Tools.Contains(tool);

    public bool HasRemoteStartButton => HasTool(Shop.RemoteStartButton);

    public int RemoteStartButtonCostNok => Shop.Find(Shop.RemoteStartButton)?.CostNok ?? 0;

    /// <summary>The text of the last Inspect on this component, if the player has inspected it.</summary>
    public string? LastInspectText(string componentId) => _lastInspect.GetValueOrDefault(componentId);

    /// <summary>The text of the last meter reading on this component, if any.</summary>
    public string? LastMeterText(string componentId) => _lastMeter.GetValueOrDefault(componentId);

    // ------------------------------------------------------------ commands

    public CommandResult Execute(Command command)
    {
        var result = _game.Execute(command);
        LastResult = result;
        if (result.Finished && ClosingNarrative is null && command is StartEngine) ClosingNarrative = result.Narrative;
        if (result.Accepted)
        {
            switch (command, result.Observation)
            {
                case (Inspect c, InspectionView v): _lastInspect[c.Component] = v.Text; break;
                case (Measure c, MeterReading m): _lastMeter[c.Component] = m.Text; break;
            }
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Leaves the briefing: one look around, which shows what is in plain view.</summary>
    public void GoAboard()
    {
        if (IsAboard) return;
        IsAboard = true;
        Execute(new Look());
    }

    /// <summary>A click on a compartment: opens it the first time (costs time), only selects it afterwards.</summary>
    public void ClickCompartment(string compartmentId)
    {
        SelectedCompartmentId = compartmentId;
        SelectedComponentId = null;
        var comp = Aboard.Compartments.FirstOrDefault(c => c.Id == compartmentId);
        if (comp is { Opened: false }) Execute(new Open(compartmentId));
        else Changed?.Invoke();
    }

    public void SelectComponent(string? componentId)
    {
        SelectedComponentId = componentId;
        if (componentId is not null)
            SelectedCompartmentId = Aboard.Compartments.FirstOrDefault(c => c.Components.Any(x => x.Id == componentId))?.Id
                                    ?? SelectedCompartmentId;
        Changed?.Invoke();
    }

    /// <summary>Runs a verb from <see cref="ComponentActionsView"/> on a component.</summary>
    public CommandResult RunVerb(string verb, string componentId) => Execute(verb switch
    {
        "inspect" => new Inspect(componentId),
        "measure" => new Measure(componentId),
        "clean" => new Clean(componentId),
        "replace" => new Replace(componentId),
        "charge" => new Charge(componentId),
        "check-filter-bowl" => new CheckFilterBowl(),
        "check-raw-water-flow" => new CheckRawWaterFlow(),
        _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
    });

    public CommandResult TryStart() => Execute(new StartEngine());

    public CommandResult BuyRemoteStartButton() => Execute(new Buy(Shop.RemoteStartButton));
}
