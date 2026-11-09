using Haulout.Sim.Play;

namespace Haulout.Web.Services;

/// <summary>The two UI levels of docs/design/004: the map, and the close-up of the current place.</summary>
public enum ViewLevel { Map, CloseUp }

/// <summary>The tabs of the context panel: the part and place, the launch picture, the passage.</summary>
public enum PanelTab { Here, Readiness, Trip, Season, Calendar }

/// <summary>Above the stage: the boat (cutaway, with its Map and Close-up) or the chart (docs/design/009).</summary>
public enum StageView { Boat, Chart }

/// <summary>
/// The web adapter's one door into the sim. Holds the Game, forwards commands and queries,
/// and remembers what the UI needs between clicks (selection, last result, what the player read).
/// Nothing here knows more than the facade tells a player.
/// </summary>
public sealed class GameSession
{
    private readonly Game _game = Game.NewFirstFault(withHours: true);

    /// <summary>How money is written (a display label from the game; the web never reads the data files).</summary>
    public Haulout.Sim.Data.Currency Currency => _game.Currency;
    /// <summary>An amount with the currency label.</summary>
    public string Money(int units) => MoneyWords.Amount(units, _game.Currency);
    /// <summary>As <see cref="Money"/>, with a plus on a positive amount.</summary>
    public string Signed(int units) => MoneyWords.Signed(units, _game.Currency);
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

    /// <summary>Boat (the cutaway) or Chart (the paper chart).</summary>
    public StageView View { get; private set; } = StageView.Boat;

    /// <summary>The port the player has chosen on the chart to plan a trip to; null: the sim's default route.</summary>
    public string? PlannedTo { get; private set; }

    public void ShowView(StageView view)
    {
        View = view;
        Changed?.Invoke();
    }

    /// <summary>A click on a reachable port on the chart: plan the trip there and open the Trip tab.</summary>
    public void PlanTo(string portName)
    {
        PlannedTo = portName;
        Tab = PanelTab.Trip;
        Changed?.Invoke();
    }

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

    /// <summary>The season page the player chose to read in the logbook (web-local); null: the latest.</summary>
    public int? ShownPage { get; private set; }

    /// <summary>Opens the Season tab with the page of the given season showing.</summary>
    public void ShowSeasonPage(int season)
    {
        ShownPage = season;
        Tab = PanelTab.Season;
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

    /// <summary>The closing card when she has been sold, kept so it stays readable.</summary>
    public ClosingView? Closing { get; private set; }

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

    public TripPlanView PlanTrip() => Ask<TripPlanView>(new PlanTrip(PlannedTo));
    public ChartView Chart => Ask<ChartView>(new Chart(PlannedTo));
    public InventoryView Inventory => Ask<InventoryView>(new Inventory());
    public TripView Trip => Ask<TripView>(new Trip());
    public SeasonView Season => Ask<SeasonView>(new Season());
    public CalendarView Calendar => Ask<CalendarView>(new Calendar());
    public SeasonPageView SeasonPage => Ask<SeasonPageView>(new SeasonPage(ShownPage));
    public SaleView Sale => Ask<SaleView>(new Sale());

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
        // The plan is spent once she leaves; and cleared again when a passage ends.
        if (result.Accepted && (command is Depart || result.Observation is TripResultView)) PlannedTo = null;
        if (result.Finished && ClosingNarrative is null && command is StartEngine) ClosingNarrative = result.Narrative;
        if (result.Accepted)
        {
            if (command is Launch && result.Observation is LaunchView lv) LaunchNarrative = lv.Lines;
            if (result.Observation is LossView loss) Loss = loss;
            if (result.Observation is ClosingView closing) Closing = closing;
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
        var result = Execute(new Depart(PlannedTo));
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

    // ------------------------------------------------------------ the season, the yard and the sale (docs/design/008)

    public CommandResult BookHaulout(int day) => Execute(new BookHaulout(day));
    public CommandResult HaulOut() => Execute(new HaulOut());
    public CommandResult CleanHull() => Execute(new Clean("hull"));
    public CommandResult WashHull() => Execute(new WashHull());
    public CommandResult Scrub() => Execute(new Scrub());
    public CommandResult WinteriseEngine() => Execute(new WinteriseEngine());
    public CommandResult ChangeOil() => Execute(new Replace("engine oil"));
    public CommandResult FillTank() => Execute(new FillTank());
    public CommandResult BatteryAshore() => Execute(new BatteryAshore());
    public CommandResult BatteryAboard() => Execute(new BatteryAboard());
    public CommandResult WinterCharger() => Execute(new WinterCharger());
    public CommandResult FitWinterCover() => Execute(new FitWinterCover());
    public CommandResult WaitUntilSpring() => Execute(new WaitUntilSpring());
    public CommandResult ListForSale(int askNok) => Execute(new ListForSale(askNok));
    public CommandResult AcceptOffer(int offer) => Execute(new AcceptOffer(offer));
    public CommandResult Keep() => Execute(new Keep());

    // ------------------------------------------------------------ the week, the money and the wants (docs/design/010)

    public CommandResult NextWeekend() => Execute(new NextWeekend());
    public CommandResult WaitWeek() => Execute(new WaitWeek());
    public CommandResult Order(string item) => Execute(new Order(item));
    public CommandResult InviteFriend(int day) => Execute(new InviteFriend(day));
    /// <summary>Marks an event as going (a wait stops on its morning) or takes the mark off.</summary>
    public CommandResult SetGoing(string eventName, bool going) => Execute(going ? new PlanEvent(eventName) : new UnplanEvent(eventName));
    public CommandResult Install(string item) => Execute(new Install(item));
    public CommandResult YardInstall(string item) => Execute(new YardInstall(item));

    /// <summary>Runs a winter task by the console words the sim names it with.</summary>
    public CommandResult RunLayupTask(string command) => command switch
    {
        "winterise" => WinteriseEngine(),
        "replace engine oil" => ChangeOil(),
        "fill tank" => FillTank(),
        "battery ashore" => BatteryAshore(),
        "fit cover" => FitWinterCover(),
        _ => throw new ArgumentException($"Unknown winter task '{command}'.", nameof(command)),
    };
}
