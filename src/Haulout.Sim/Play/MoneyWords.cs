using Haulout.Sim.Data;

namespace Haulout.Sim.Play;

/// <summary>
/// Money in words, the same for every adapter and the one place the currency label is written. The in-game currency is one abstract unit;
/// <see cref="Currency"/> is only how it is spelt.
/// </summary>
public static class MoneyWords
{
    /// <summary>
    /// "6000 NOK" for code NOK with no symbol; "€6000" for symbol "€" before; "6000 kr" for symbol "kr" after. A minus sign comes first
    /// ("-€500", "-500 NOK").
    /// </summary>
    public static string Amount(int units, Currency currency)
    {
        string sign = units < 0 ? "-" : "";
        long abs = Math.Abs((long)units);
        return currency.Symbol.Length == 0 ? $"{sign}{abs} {currency.Code}"
            : currency.Before ? $"{sign}{currency.Symbol}{abs}"
            : $"{sign}{abs} {currency.Symbol}";
    }

    /// <summary>As <see cref="Amount"/>, with a plus sign on a positive amount: "+600 NOK", "-€600". Zero has no sign.</summary>
    public static string Signed(int units, Currency currency) =>
        units > 0 ? "+" + Amount(units, currency) : Amount(units, currency);

    /// <summary>"Money left: 9000 NOK." or "You owe 2400 NOK." Below zero it is debt and says so (decision 007/5).</summary>
    public static string Balance(int balanceNok, Currency currency) =>
        balanceNok < 0 ? $"You owe {Amount(-balanceNok, currency)}." : $"Money left: {Amount(balanceNok, currency)}.";

    /// <summary>Lower case, for the middle of a line: "9000 NOK left" or "you owe 2400 NOK".</summary>
    public static string BalanceShort(int balanceNok, Currency currency) =>
        balanceNok < 0 ? $"you owe {Amount(-balanceNok, currency)}" : $"{Amount(balanceNok, currency)} left";
}
