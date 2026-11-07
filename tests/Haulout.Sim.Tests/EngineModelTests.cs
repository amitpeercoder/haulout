using Haulout.Sim.Engine;

namespace Haulout.Sim.Tests;

public class EngineModelTests
{
    private static readonly FuelInputs GoodFuel = new(true, 0.0, true, 1.0);
    private static readonly CoolingInputs GoodCooling = new(1.0);

    private static EngineInputs Make(ElectricalInputs? e = null, FuelInputs? f = null, CoolingInputs? c = null)
        => new(e ?? ElectricalChainTests.Inputs(), f ?? GoodFuel, c ?? GoodCooling);

    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { StartOutcome.Nothing, 1, Make(ElectricalChainTests.Inputs(switchOn: false)) };
        yield return new object[] { StartOutcome.Click, 2, Make(ElectricalChainTests.Inputs(negTerminal: 0.0603)) };
        yield return new object[] { StartOutcome.SlowCrank, 3, Make(ElectricalChainTests.Inputs(negTerminal: 0.02)) };
        yield return new object[] { StartOutcome.CranksNoFire, 4, Make(f: GoodFuel with { FuelAvailable = false }) };
        yield return new object[] { StartOutcome.FiresAndDies, 5, Make(f: GoodFuel with { Restriction = 0.9 }) };
        yield return new object[] { StartOutcome.RunsRough, 6, Make(f: GoodFuel with { Restriction = 0.7 }) };
        yield return new object[] { StartOutcome.Runs, 7, Make() };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachOutcomeIsReachable(StartOutcome outcome, int rule, EngineInputs inputs)
    {
        var r = EngineModel.Attempt(inputs);
        Assert.Equal(outcome, r.Outcome);
        Assert.Equal(rule, r.Rule);
        Assert.False(string.IsNullOrWhiteSpace(r.Description));
        Assert.DoesNotContain("!", r.Description);
    }

    [Fact]
    public void SlowCrankNegTerminalFixtureIsActuallyBetween6And8Volts()
    {
        var r = EngineModel.Attempt(Make(ElectricalChainTests.Inputs(negTerminal: 0.02)));
        Assert.InRange(r.Electrical.StarterVolts, 6.0, 8.0);
    }

    [Fact]
    public void DeadBattery_IsNothing()
    {
        var r = EngineModel.Attempt(Make(ElectricalChainTests.Inputs(soc: 0.0) with { BatteryOpenCircuitVolts = 10.4 }));
        Assert.Equal(StartOutcome.Nothing, r.Outcome);
        Assert.Equal(0, r.Electrical.CurrentAmps);
        Assert.Equal(10.4, r.Electrical.BatteryTerminalVolts);
        Assert.Equal(0, r.Electrical.StarterVolts);
    }

    [Fact]
    public void PanelDeadAndOpenCircuit_AreNothing()
    {
        Assert.Equal(StartOutcome.Nothing, EngineModel.Attempt(Make(ElectricalChainTests.Inputs(panelLive: false))).Outcome);
        Assert.Equal(StartOutcome.Nothing,
            EngineModel.Attempt(Make(ElectricalChainTests.Inputs(negTerminal: double.PositiveInfinity))).Outcome);
    }

    [Fact]
    public void LowVoltageBeatsNoFuel()
    {
        var r = EngineModel.Attempt(Make(ElectricalChainTests.Inputs(negTerminal: 0.0603),
            GoodFuel with { FuelAvailable = false, GlowPlugsWork = false }));
        Assert.Equal(StartOutcome.Click, r.Outcome);
        Assert.Equal(2, r.Rule);
    }

    [Fact]
    public void SwitchOffBeatsEverything()
    {
        var r = EngineModel.Attempt(Make(ElectricalChainTests.Inputs(switchOn: false, negTerminal: 0.0603),
            GoodFuel with { FuelAvailable = false }));
        Assert.Equal(1, r.Rule);
    }

    [Fact]
    public void FuelRulesRespectPrecedence()
    {
        // No glow plugs beats a blocked filter.
        Assert.Equal(4, EngineModel.Attempt(Make(f: GoodFuel with { GlowPlugsWork = false, Restriction = 0.95 })).Rule);
        // Severe restriction beats bad injectors.
        Assert.Equal(5, EngineModel.Attempt(Make(f: GoodFuel with { Restriction = 0.85, InjectorCondition = 0.1 })).Rule);
        Assert.Equal(6, EngineModel.Attempt(Make(f: GoodFuel with { Restriction = 0.6 })).Rule);
        Assert.Equal(6, EngineModel.Attempt(Make(f: GoodFuel with { InjectorCondition = 0.29 })).Rule);
        Assert.Equal(7, EngineModel.Attempt(Make(f: GoodFuel with { Restriction = 0.59, InjectorCondition = 0.3 })).Rule);
    }

    [Fact]
    public void FixingOnlyTheTerminal_WithDecoyBattery_Runs()
    {
        var r = EngineModel.Attempt(Make(ElectricalChainTests.Inputs(soc: 0.6)));
        Assert.Equal(StartOutcome.Runs, r.Outcome);
    }

    [Theory]
    [InlineData(0.1, true)]
    [InlineData(0.24, true)]
    [InlineData(0.25, false)]
    [InlineData(1.0, false)]
    public void OverheatWarning_OnRunningEngine(double flow, bool expected)
    {
        Assert.Equal(expected, EngineModel.Attempt(Make(c: new CoolingInputs(flow))).OverheatWarning);
        Assert.Equal(expected,
            EngineModel.Attempt(Make(f: GoodFuel with { Restriction = 0.7 }, c: new CoolingInputs(flow))).OverheatWarning);
    }

    [Fact]
    public void OverheatWarning_NeverOnNonRunningOutcomes()
    {
        var dry = new CoolingInputs(0.0);
        foreach (var o in new[]
        {
            Make(ElectricalChainTests.Inputs(switchOn: false), c: dry),
            Make(ElectricalChainTests.Inputs(negTerminal: 0.0603), c: dry),
            Make(ElectricalChainTests.Inputs(negTerminal: 0.02), c: dry),
            Make(f: GoodFuel with { FuelAvailable = false }, c: dry),
            Make(f: GoodFuel with { Restriction = 0.9 }, c: dry),
        })
        {
            var r = EngineModel.Attempt(o);
            Assert.False(r.OverheatWarning);
            Assert.DoesNotContain("temperature alarm", r.Description);
        }
    }

    [Fact]
    public void Description_AppendsOverheatSentence_AndIsDeterministic()
    {
        var a = EngineModel.Attempt(Make(c: new CoolingInputs(0.0)));
        var b = EngineModel.Attempt(Make(c: new CoolingInputs(0.0)));
        Assert.EndsWith("No water spitting from the exhaust. The temperature alarm starts to whine.", a.Description);
        Assert.Equal(a.Description, b.Description);
    }

    [Fact]
    public void ClickDescription_MentionsClickAndDimmingPanel()
    {
        var d = EngineModel.Attempt(Make(ElectricalChainTests.Inputs(negTerminal: 0.0603))).Description;
        Assert.Contains("click", d);
        Assert.Contains("dim", d);
    }
}
