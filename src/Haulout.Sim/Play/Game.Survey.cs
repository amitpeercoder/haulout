using Haulout.Sim.Data;
using Haulout.Sim.Faults;

namespace Haulout.Sim.Play;

/// <summary>The surveyor's report and the readiness picture.</summary>
public sealed partial class Game
{
    private SurveyReportView? _report;
    /// <summary>The survey lines that went into the report, in order.</summary>
    private readonly List<SurveyItemData> _surveyKept = [];

    private Fault? FaultById(string id) => _boat.AllFaults.FirstOrDefault(f => f.Id == id);

    private CommandResult DoOrderSurvey(Turn t)
    {
        var data = _boat.Survey;
        if (YardGate() is { } away) return Reject("order-survey", away);
        if (data is null) return Reject("order-survey", "There's no surveyor to be had.");
        if (_report is not null) return Reject("order-survey", "You've already had her surveyed.");
        if (Afloat) return Reject("order-survey", "The surveyor wants her ashore.");
        if (MoneyRefusal(data.FeeNok) is { } broke) return Reject("order-survey", broke);

        foreach (var item in data.Items)
        {
            var fault = item.Fault is null ? null : FaultById(item.Fault);
            if (item.Fault is not null && fault is not { Present: true }) continue;
            _surveyKept.Add(item);
            _seen.Add(item.Component);
            if (fault is null) continue;
            if (item.Finding == SurveyFinding.Cause) Discover(t, fault);
            else if (item.Finding == SurveyFinding.Investigate) fault.MarkSymptomatic();
        }

        ShutDownEngine(t);
        int reportDay = _startDay + (StartMinuteOfDay + ClockMinutes + data.Days * 1440) / 1440;
        _report = new SurveyReportView(
            true, reportDay, data.Surveyor, _boat.Name, data.FeeNok, data.Opening,
            _surveyKept.Select((i, n) => new SurveyItemView(n + 1, _boat.Get(i.Component).Name, SeverityWords(i.Severity), i.Text)).ToList(),
            data.Closing);

        t.Narrative.Add(data.Days == 1 ? "The surveyor spends the day aboard." : $"The surveyor spends {data.Days} days aboard.");
        t.Narrative.Add("The report is in the logbook ('read survey').");
        var result = Accept(t, "order-survey", data.Days * 1440, data.FeeNok, null, "Ordered a survey.", work: false);
        _log.Add(new LogEntry(Day, TimeOfDay,
            $"Survey report received from {data.Surveyor}: {_surveyKept.Count} items.", 0, "survey report"));
        return result;
    }

    private static string SeverityWords(SurveySeverity s) => s switch
    {
        SurveySeverity.Note => "Note",
        SurveySeverity.Attention => "Attention",
        _ => "Before launch",
    };

    /// <summary>A surveyor's "recommend investigation" points at this part and the cause has not been found.</summary>
    private bool IsFlagged(Components.Component c) =>
        _surveyKept.Any(i => i.Component == c.Id && PointsAtUnfoundFault(i));

    private bool PointsAtUnfoundFault(SurveyItemData i) =>
        i is { Finding: SurveyFinding.Investigate, Fault: not null }
        && FaultById(i.Fault) is { Present: true, Discovered: false };

    private SurveyReportView SurveyReportSnapshot() =>
        _report ?? new SurveyReportView(false, 0, "", "", 0, [], [], []);

    private ReadinessView ReadinessSnapshot()
    {
        var open = KnownOpenFaults().Select(f =>
        {
            var comp = _boat.Get(f.ComponentId);
            var routes = new List<FixRoute>();
            if (f.Diy is { } diy)
            {
                var diyReason = AfloatRefusal(f, comp) ?? WetRefusal(comp) ?? MoneyRefusal(diy.PartsNok);
                routes.Add(new FixRoute("DIY", diy.How, diy.PartsNok, diy.Minutes, 0, diyReason is null, diyReason));
            }
            if (f.Yard is { } yard)
            {
                var reason = YardRefusal(f);
                routes.Add(new FixRoute("Yard", yard.How, yard.Nok, 0, yard.LeadDays, reason is null, reason));
            }
            int now = f.Diy?.PartsNok ?? f.Yard?.Nok ?? 0;
            var esc = f.Escalation;
            bool known = f.State is FaultState.Discovered or FaultState.Deferred;
            var (likelyMin, likelyMax) = known && !f.Escalated ? LikelyDays(f) : (null, null);
            bool onLaunch = known && esc is { OnLaunch: true } && !_launched;
            return new ReadinessItem(
                f.Name, comp.Name, f.State.ToString(), now, f.CostLaterNok, f.LaterNote,
                f.OnHardOnly || comp.BelowWaterline, f.Consequence, routes, likelyMin, likelyMax, onLaunch);
        }).ToList();

        var investigate = _surveyKept.Where(PointsAtUnfoundFault)
            .Select(i => new InvestigateItem(_boat.Get(i.Component).Name, i.Text)).ToList();

        var fixedNames = _discovered
            .Select(k => FaultById(k.Id))
            .Where(f => f is { State: FaultState.Fixed, Escalated: false })
            .Select(f => f!.Name).Concat(SupersededFixed().Select(f => f.Name)).ToList();

        int deadline = _launchDeadlineDay;
        return new ReadinessView(Day, deadline, deadline - Day, Balance, _launched, open, investigate, fixedNames, Afloat, _boat.Economy.EmergencyLiftNok, ReadinessSeasonLead());
    }
}
