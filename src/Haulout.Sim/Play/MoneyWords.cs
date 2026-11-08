namespace Haulout.Sim.Play;

/// <summary>The balance in words, the same for every adapter. Below zero it is debt and says so (decision 007/5).</summary>
public static class MoneyWords
{
    /// <summary>"Money left: 9000 NOK." or "You owe 2400 NOK."</summary>
    public static string Balance(int balanceNok) =>
        balanceNok < 0 ? $"You owe {-balanceNok} NOK." : $"Money left: {balanceNok} NOK.";

    /// <summary>Lower case, for the middle of a line: "9000 NOK left" or "you owe 2400 NOK".</summary>
    public static string BalanceShort(int balanceNok) =>
        balanceNok < 0 ? $"you owe {-balanceNok} NOK" : $"{balanceNok} NOK left";
}
