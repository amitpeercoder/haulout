namespace Haulout.Sim;

/// <summary>All randomness in the sim goes through this so runs are replayable.</summary>
public interface IRandom
{
    /// <summary>Uniform in [0, 1).</summary>
    double NextDouble();
}

public sealed class SeededRandom(int seed) : IRandom
{
    private readonly Random _random = new(seed);
    public double NextDouble() => _random.NextDouble();
}
