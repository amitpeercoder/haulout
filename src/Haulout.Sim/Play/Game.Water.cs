using System.Globalization;
using Haulout.Sim.Data;
using Haulout.Sim.Faults;
using Haulout.Sim.Systems;

namespace Haulout.Sim.Play;

/// <summary>The systems step, spawning, the manual pump, and the way she sinks (design note 006, step 3).</summary>
public sealed partial class Game
{
    private const string SaloonId = "saloon";
    private const string BilgeId = "bilge";
    private const string PumpFuseId = "bilge-pump-fuse";
    private const string SunkMessage = "She's on the bottom of the harbour. The logbook is all that's left.";
    private const string PumpHandleMessage = "The bilge pump handle is in the cockpit.";

    private readonly WaterSystem _water;
    private readonly CoolingSystem _cooling = new();
    private readonly SystemValues _values = new();
    private bool _sunk;
    private LossView? _loss;

    /// <summary>For tests only (not on the facade: a player cannot read the bilge in litres).</summary>
    internal double BilgeLitres
    {
        get => _water.Level;
        set => _water.Level = value;
    }

    internal SystemValues Values => _values;
    internal int PumpCyclesSinceAboard => _water.CyclesSinceAboard;

    /// <summary>The systems, in order, over one step of the clock (already advanced). Electrical (self-discharge, then the engine's charge), cooling, then water.</summary>
    private void SystemsStep(int minutes)
    {
        // The step ends at the clock; a step that ends at midnight belongs to the day before.
        int day = _startDay + (StartMinuteOfDay + ClockMinutes - minutes) / 1440;
        Electrical.Step(_boat, minutes);
        _values.Set(SystemValues.Charging, Electrical.Run(_boat, minutes, _running));
        _cooling.Step(minutes, _running, _running ? EngineInputsBuilder.Build(_boat).Cooling.RawWaterFlow : 1.0);
        _cooling.Publish(_values);
        _water.Step(new WaterSystem.Inputs(minutes, Afloat, _weather[day].RainMm, _removed.Contains(HatchId), !_removed.Contains(PumpFuseId),
            AtSea ? Sea.SeaState(_boat.SeaRules, _weather[day].WindForce) : 0));
        _water.Publish(_values);
        if (Afloat && _water.Level >= _boat.Water.SinkLitres) Sink(day);
    }

    /// <summary>A dormant fault whose spawn value has crossed wakes once, Latent and silent, with its spread drawn.</summary>
    private void Spawn(bool present)
    {
        foreach (var f in _boat.AllFaults)
        {
            if (!f.Dormant || f.Spawned || f.Spawn.Count == 0) continue;
            if (!f.Spawn.Any(r => _values.Get(r.When) >= r.AtLeast)) continue;
            f.SpawnOn(Day);
            DrawSpread(f);
            if (!present) _wokenAway.Add(f);
        }
    }

    /// <summary>Said in the saloon (look, go) when the water is over the sole, even with the floorboards in.</summary>
    private string? SoleLine(Place p) =>
        p.Id == SaloonId && _water.Level > _boat.Water.SoleLitres ? _boat.Water.SoleText : null;

    // ------------------------------------------------------------------ the manual pump

    private CommandResult DoPumpBilge(Turn t)
    {
        if (_placeId != StartPlaceId) return Reject("pump-bilge", PumpHandleMessage);
        var rules = _boat.Water;
        double capacity = rules.ManualPumpLitresPerMinute * rules.ManualPumpMinutes;
        double before = _water.Level;
        double removed = Math.Min(before, capacity);
        _water.Level = before - removed;

        string how = rules.ManualPumpMinutes == 15 ? "a quarter of an hour" : $"{rules.ManualPumpMinutes} minutes";
        if (before < 0.5) t.Narrative.Add("You take a few strokes on the handle; the pump sucks air at once. There is nothing to move.");
        else if (before - removed > 1) t.Narrative.Add($"You work the handle for {how}; the pump keeps gurgling, and the water still comes.");
        else t.Narrative.Add($"You work the handle for {how}; the pump gurgles and then sucks air.");
        return Accept(t, "pump-bilge", rules.ManualPumpMinutes, 0, null, "Worked the bilge pump by hand.");
    }

    // ------------------------------------------------------------------ sinking

    private void Sink(int day)
    {
        _sunk = true;
        _running = false;
        var insurance = _boat.Insurance;
        string? voidReason = VoidReason();
        bool voided = voidReason is not null;
        int payout = voided ? 0 : insurance.PayoutNok;

        var lines = new List<string>
        {
            (_passage is { } sea
                ? $"She went down {sea.ProgressNm.ToString("0.0", CultureInfo.InvariantCulture)} nm out from {PortName(sea.Route.From)} on day {day}, a little after {TimeOfDay}."
                : $"She went down at her mooring on day {day}, a little after {TimeOfDay}.")
                + " The water came up over the sole, then the berths, and the stern settled first.",
            PumpLine(),
            voided
                ? $"The insurer's man comes down to the quay with a camera and a folder, and pays nothing: {voidReason}."
                : $"The insurer's man comes down to the quay with a camera and a folder. They put her at {insurance.ValueNok} NOK and pay {payout} NOK.",
            "The yard has another tired boat for sale.",
        };
        _log.Add(new LogEntry(day, TimeOfDay, voided ? "She sank. The insurer pays nothing." : $"She sank. The insurer pays {payout} NOK."));
        _loss = new LossView(day, lines, insurance.ValueNok, payout, voided, voidReason);
    }

    /// <summary>What the pump did, in one sentence.</summary>
    private string PumpLine()
    {
        if (!_boat.TryGet(WaterSystem.PumpId, out _)) return "There was no bilge pump to speak of.";
        if (_removed.Contains(PumpFuseId)) return "The bilge pump fuse was out of its holder, so the pump never ran.";
        if (Electrical.StateOfCharge(_boat) <= _boat.Electrical.PumpMinStateOfCharge)
            return "The battery was too flat to turn the bilge pump over.";
        if (Properties.Effective(_boat, WaterSystem.PumpId, "litresPerMinute", 0) <= 0)
            return "The bilge pump had burnt out; it hummed and moved nothing.";
        int cycles = _water.PumpCycles;
        return cycles > 0
            ? $"The bilge pump cycled {cycles} times and could not keep up."
            : "The bilge pump never came on.";
    }

    /// <summary>The words for the insurer's refusal, fixed at the last launch; null when the cover held at launch.</summary>
    private string? _launchVoidReason;

    /// <summary>
    /// Called at launch: a survey item marked Before launch whose fault (or what it escalated into) is still present, whether the player
    /// knew of it or not, voids the cover. It reads "put off" if the player ever deferred it, else "never put right". A deferral is named
    /// before an item merely left open. Overwritten at every launch, so a boat lifted, put right and relaunched is covered.
    /// </summary>
    private void RecordLaunchCover()
    {
        string? deferred = null, open = null;
        foreach (var item in _surveyKept.Where(i => i.Severity == SurveySeverity.BeforeLaunch && i.Fault is not null))
        {
            var chain = EscalationChain(item.Fault!);
            if (!chain.Any(f => f.Present)) continue;
            string name = _boat.Get(item.Component).Name.ToLowerInvariant();
            if (chain.Any(f => f.EverDeferred)) deferred ??= name;
            else open ??= name;
        }
        _launchVoidReason = deferred is not null
            ? $"the surveyor had marked the {deferred} for attention before launch, and it was put off"
            : open is not null
                ? $"the surveyor had marked the {open} for attention before launch, and it was never put right"
                : null;
    }

    /// <summary>What voids the cover, recorded at the last launch; null when it holds.</summary>
    private string? VoidReason() => _launchVoidReason;

    /// <summary>The fault and every fault it escalated into, on its component.</summary>
    private List<Fault> EscalationChain(string faultId)
    {
        var chain = new List<Fault>();
        var f = FaultById(faultId);
        while (f is not null && !chain.Contains(f))
        {
            chain.Add(f);
            var to = f.Escalation?.To;
            f = to is null ? null : _boat.Get(f.ComponentId).Faults.FirstOrDefault(x => x.Id == to);
        }
        return chain;
    }
}
