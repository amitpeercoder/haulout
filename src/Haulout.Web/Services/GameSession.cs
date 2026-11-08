using Haulout.Sim.Play;

namespace Haulout.Web.Services;

/// <summary>The two UI levels of docs/design/004: the map, and the close-up of the current place.</summary>
public enum ViewLevel { Map, CloseUp }

/// <summary>The tabs of the context panel: the part and place, the launch picture, the passage.</summary>
public enum PanelTab { Here, Readiness, Trip }

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

    /// <summary>Name of the selected component, if any (the facade's display name).</summary>
    public string? SelectedComponent { get; private set; }

    /// <summary>Map (overview cutaway) or the close-up of the place the player is in.</summary>
    public ViewLevel Level { get; private set; } = ViewLevel.Map;

    /// <summary>Which tab of the context panel is showing.</summary>
    public PanelTab Tab { get; private set; } = PanelTab.Here;

    /// <summary>The survey letter is open in the Readiness tab.</summary>
    public bool SurveyOpen { get; private set; }

    /// <summary>How many alerts the logbook held when the Readiness tab was last shown (web-local).</summary>
    private int _alertsSeen;

    /// <summary>Alerts since the player last opened the Readiness tab (zero while it is open).</summary>
    public int UnseenAlerts
    {
        get
        {
            int total = Log.Entries.Count(e => e.Alert);
            if (Tab == PanelTab.Readiness) _alertsSeen = total;
            return Math.Max(0, total - _alertsSeen);
        }
    }

    public void ShowTab(PanelTab tab)
    {
        Tab = tab;
        Changed?.Invoke();
    }

    public void ToggleSurvey()
    {
        SurveyOpen = !SurveyOpen;
        Changed?.Invoke();
    }

    /// <summary>Opens the Readiness tab with the survey letter showing.</summary>
    public void ShowSurvey()
    {
        Tab = PanelTab.Readiness;
        SurveyOpen = true;
        Changed?.Invoke();
    }

    public bool Finished => Status.Finished;

    /// <summary>The narrative of the start attempt that finished the slice; kept so the closing stays readable.</summary>
    public IReadOnlyList<string>? ClosingNarrative { get; private set; }

    /// <summary>The lines of the launch card, kept so the closing stays readable.</summary>
    public IReadOnlyList<string>? LaunchNarrative { get; private set; }

    /// <summary>The closing card when she has sunk, kept so it stays readable.</summary>
    public LossView? Loss { get; private set; }

    /// <summary>The card of the passage that just ended (arrival, turned back, tow); kept until the next command.</summary>
    public TripResultView? TripResult { get; private set; }

    // ------------------------------------------------------------- queries

    public T Ask<T>(Query query) where T : QueryResult => (T)_game.Ask(query);

    public BriefingView Briefing => Ask<BriefingView>(new Briefing());
    public StatusView Status => Ask<StatusView>(new Status());
    public LogView Log => Ask<LogView>(new Logbook());
    public ReadinessView Readiness => Ask<ReadinessView>(new Readiness());
    public SurveyReportView Survey => Ask<SurveyReportView>(new SurveyReport());
    public AboardView Aboard => Ask<AboardView>(new Aboard());

    public ComponentActionsView ActionsFor(string componentName) =>
        Ask<ComponentActionsView>(new ComponentActions(componentName));

    public TripPlanView PlanTrip() => Ask<TripPlanView>(new PlanTrip());
    public InventoryView Inventory => Ask<InventoryView>(new Inventory());
    public TripView Trip => Ask<TripView>(new Trip());

    public bool HasTool(string tool) => Status.Tools.Contains(tool);

    public bool HasRemoteStartButton => HasTool(Shop.RemoteStartButton);

    public int RemoteStartButtonCostNok =>
        Ask<InventoryView>(new Inventory()).ForSale.FirstOrDefault(l => l.Name == Shop.RemoteStartButton)?.PriceNok ?? 0;

    /// <summary>The text of the last Inspect on this component, if the player has inspected it.</summary>
    public string? LastInspectText(string componentName) => _lastInspect.GetValueOrDefault(componentName);

    /// <summary>The text of the last meter reading on this component, if any.</summary>
    public string? LastMeterText(string componentName) => _lastMeter.GetValueOrDefault(componentName);

    // ------------------------------------------------------------ commands

    public CommandResult Execute(Command command)
    {
        var result = Run(command);
        Changed?.Invoke();
        return result;
    }

    /// <summary>Executes without raising Changed, so a caller can update view state first.</summary>
    private CommandResult Run(Command command)
    {
        var result = _game.Execute(command);
        LastResult = result;
        TripResult = result.Accepted ? result.Observation as TripResultView : null;
        if (result.Finished && ClosingNarrative is null && command is StartEngine) ClosingNarrative = result.Narrative;
        if (result.Accepted)
        {
            if (command is Launch && result.Observation is LaunchView lv) LaunchNarrative = lv.Lines;
            if (result.Observation is LossView loss) Loss = loss;
            switch (command, result.Observation)
            {
                case (Inspect c, InspectionView v): _lastInspect[c.Component] = v.Text; break;
                case (Measure c, MeterReading m): _lastMeter[c.Component] = m.Text; break;
            }
        }
        return result;
    }

    /// <summary>Leaves the briefing: one look around, which shows what is in plain view.</summary>
    public void GoAboard()
    {
        if (IsAboard) return;
        IsAboard = true;
        Execute(new Look());
    }

    // ------------------------------------------------------------ the two levels (docs/design/004)

    /// <summary>A click on a place on the map. Here already: zoom in. Otherwise ask the sim to go there; zoom in only if it agrees.
    /// A refusal ("You can't get to the forepeak from here.") stays as the last result for the panel.</summary>
    public void ClickPlace(string placeName)
    {
        if (!IsAboard) return;
        if (Same(Aboard.Place, placeName))
        {
            Level = ViewLevel.CloseUp;
            Changed?.Invoke();
            return;
        }
        var result = Run(new GoTo(placeName));
        if (result.Accepted)
        {
            Level = ViewLevel.CloseUp;
            SelectedComponent = null;
        }
        Changed?.Invoke();
    }

    /// <summary>A click on a marker on the map: select it, and zoom in when it can be seen from where the player is.</summary>
    public void ClickMapMarker(string componentName)
    {
        SelectedComponent = componentName;
        if (Aboard.Components.FirstOrDefault(c => Same(c.Name, componentName)) is { VisibleHere: true }) Level = ViewLevel.CloseUp;
        Changed?.Invoke();
    }

    /// <summary>A click on a cover in a close-up: take it off if it is on, put it back if it is off. The sim decides.</summary>
    public void ClickCover(string coverName)
    {
        var cover = Aboard.Covers.FirstOrDefault(c => Same(c.Name, coverName));
        Run(cover is { Removed: true } ? new Refit(coverName) : new Remove(coverName));
        Changed?.Invoke();
    }

    public void ZoomOut()
    {
        Level = ViewLevel.Map;
        Changed?.Invoke();
    }

    public void SelectComponent(string? componentName)
    {
        SelectedComponent = componentName;
        Changed?.Invoke();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs a verb from <see cref="ComponentActionsView"/> on a component.</summary>
    public CommandResult RunVerb(string verb, string componentName, string? target = null) => Execute(verb switch
    {
        "inspect" => new Inspect(componentName),
        "measure" => new Measure(componentName),
        "uncover" => new Uncover(componentName),
        "clean" => new Clean(componentName),
        "replace" => new Replace(componentName),
        "charge" => new Charge(componentName),
        "check-filter-bowl" => new CheckFilterBowl(),
        "check-raw-water-flow" => new CheckRawWaterFlow(),
        "defer" => new Defer(target ?? componentName),
        "undefer" => new Undefer(target ?? componentName),
        "yard-fix" => new FixByYard(target ?? componentName),
        _ => throw new ArgumentException($"Unknown verb '{verb}'.", nameof(verb)),
    });

    public CommandResult OrderSurvey() => Execute(new OrderSurvey());

    public CommandResult Launch() => Execute(new Launch());

    public CommandResult EmergencyLift()
    {
        var result = Execute(new EmergencyLift());
        if (result.Accepted) LaunchNarrative = null;
        return result;
    }

    public CommandResult PumpBilge() => Execute(new PumpBilge());

    public CommandResult Wait(int days) => Execute(new WaitDays(days));

    public CommandResult Defer(string problem) => Execute(new Defer(problem));

    public CommandResult Undefer(string problem) => Execute(new Undefer(problem));

    public CommandResult TryStart() => Execute(new StartEngine());

    public CommandResult StopEngine() => Execute(new StopEngine());

    public CommandResult BuyRemoteStartButton() => Execute(new Buy(Shop.RemoteStartButton));

    public CommandResult Buy(string item) => Execute(new Buy(item));

    public CommandResult Depart()
    {
        var result = Execute(new Depart());
        if (result.Accepted) { Tab = PanelTab.Trip; Changed?.Invoke(); }
        return result;
    }

    public CommandResult Continue() => Execute(new Continue());
    public CommandResult Motor() => Execute(new Motor());
    public CommandResult Sail() => Execute(new Sail());
    public CommandResult HeaveTo() => Execute(new HeaveTo());
    public CommandResult Anchor() => Execute(new Anchor());
    public CommandResult TurnBack() => Execute(new TurnBack());
    public CommandResult CallTow() => Execute(new CallTow());
}
