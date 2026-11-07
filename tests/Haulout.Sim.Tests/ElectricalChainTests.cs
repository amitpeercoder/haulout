using Haulout.Sim.Engine;

namespace Haulout.Sim.Tests;

public class ElectricalChainTests
{
    internal static IReadOnlyList<SeriesElement> Loop(double negTerminal = 0.0003) => new[]
    {
        new SeriesElement("pos-terminal", "Positive terminal", 0.0003),
        new SeriesElement("pos-cable", "Positive cable", 0.0015),
        new SeriesElement("switch", "Battery switch", 0.0005),
        new SeriesElement("solenoid", "Solenoid contacts", 0.001),
        new SeriesElement("starter", "Starter", 0.045),
        new SeriesElement("ground", "Ground stud", 0.0005),
        new SeriesElement("neg-cable", "Negative cable", 0.0015),
        new SeriesElement("neg-terminal", "Negative terminal", negTerminal),
    };

    internal static ElectricalInputs Inputs(double soc = 1.0, double negTerminal = 0.0003,
        bool switchOn = true, bool panelLive = true) =>
        new(switchOn, panelLive, ElectricalChain.OpenCircuitVolts(soc), 0.008, Loop(negTerminal), "starter");

    [Theory]
    [InlineData(1.0, 12.6)]
    [InlineData(0.5, 12.2)]
    [InlineData(0.0, 11.8)]
    [InlineData(2.0, 12.6)]
    [InlineData(-1.0, 11.8)]
    public void OpenCircuitVolts_FollowsStateOfCharge(double soc, double expected)
        => Assert.Equal(expected, ElectricalChain.OpenCircuitVolts(soc), 9);

    [Fact]
    public void HealthyLoop_FullBattery_DrawsAbout215AmpsAndGivesStarterAbout9_67Volts()
    {
        var r = ElectricalChain.Crank(Inputs());
        Assert.True(r.CircuitClosed);
        Assert.InRange(r.CurrentAmps, 213, 217);
        Assert.InRange(r.StarterVolts, 9.6, 9.75);
    }

    [Fact]
    public void CorrodedNegativeTerminal_FullBattery_HalvesCurrent()
    {
        var r = ElectricalChain.Crank(Inputs(negTerminal: 0.0603));
        Assert.InRange(r.CurrentAmps, 104, 108);
        Assert.InRange(r.StarterVolts, 4.6, 4.9);
    }

    [Fact]
    public void CorrodedNegativeTerminal_At60PercentCharge_DropsAbout6_2VoltsAcrossTerminal()
    {
        var r = ElectricalChain.Crank(Inputs(soc: 0.6, negTerminal: 0.0603));
        Assert.InRange(r.CurrentAmps, 102, 106);
        Assert.InRange(r.DropVolts["neg-terminal"], 6.1, 6.3);
    }

    [Fact]
    public void HealthyLoop_At60PercentCharge_GivesStarterAbout9_4Volts()
    {
        var r = ElectricalChain.Crank(Inputs(soc: 0.6));
        Assert.InRange(r.StarterVolts, 9.3, 9.5);
    }

    [Theory]
    [InlineData(1.0, 0.0003)]
    [InlineData(0.6, 0.0603)]
    [InlineData(0.2, 0.01)]
    public void Chain_IsConsistent(double soc, double negTerminal)
    {
        var inp = Inputs(soc, negTerminal);
        var r = ElectricalChain.Crank(inp);

        double internalDrop = r.CurrentAmps * inp.BatteryInternalOhms;
        Assert.Equal(inp.BatteryOpenCircuitVolts, r.DropVolts.Values.Sum() + internalDrop, 9);
        Assert.Equal(inp.BatteryOpenCircuitVolts - internalDrop, r.BatteryTerminalVolts, 9);
        foreach (var e in inp.Loop)
        {
            Assert.Equal(r.CurrentAmps * e.Ohms, r.DropVolts[e.Id], 9);
        }
        Assert.Equal(r.DropVolts["starter"], r.StarterVolts, 9);
    }

    [Fact]
    public void SwitchOff_NothingFlows_TerminalIsOpenCircuit()
    {
        var inp = Inputs(switchOn: false);
        var r = ElectricalChain.Crank(inp);
        Assert.False(r.CircuitClosed);
        Assert.Equal(0, r.CurrentAmps);
        Assert.Equal(0, r.StarterVolts);
        Assert.Equal(inp.BatteryOpenCircuitVolts, r.BatteryTerminalVolts);
        Assert.All(r.DropVolts.Values, v => Assert.Equal(0, v));
        Assert.Equal(inp.Loop.Count, r.DropVolts.Count);
    }

    [Fact]
    public void PanelDead_NothingFlows()
        => Assert.False(ElectricalChain.Crank(Inputs(panelLive: false)).CircuitClosed);

    [Fact]
    public void OpenElement_NothingFlows()
    {
        var r = ElectricalChain.Crank(Inputs(negTerminal: double.PositiveInfinity));
        Assert.False(r.CircuitClosed);
        Assert.Equal(0, r.CurrentAmps);
        Assert.All(r.DropVolts.Values, v => Assert.Equal(0, v));
    }
}
