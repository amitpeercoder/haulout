namespace Haulout.Sim.Data;

/// <summary>
/// How money is written. The in-game currency is one abstract unit (CLAUDE.md); this is the label only, with no exchange rate.
/// Symbol empty: the amount then the code ("6000 NOK"). Symbol set: the symbol before the amount when Before ("€6000"), else after ("6000 kr").
/// </summary>
public sealed record Currency(string Code, string Symbol, bool Before)
{
    /// <summary>What a boat built without an economy file writes.</summary>
    public static Currency Default { get; } = new("NOK", "", false);
}

/// <summary>
/// The world's money and time knobs (design note 010, second pass A): one file, <c>economy.json</c>, shared by every boat. The "Nok" suffix
/// on names in code means "money units". Everything here reaches the rule records through <see cref="BoatLoader"/>.
/// </summary>
public sealed record EconomyData
{
    public Currency Currency { get; init; } = Currency.Default;
    public StartData Start { get; init; } = new();
    public EmergencyLiftData EmergencyLift { get; init; } = new();
    public CalendarData Calendar { get; init; } = new();
    public YardData Yard { get; init; } = new();
    public TowData Tow { get; init; } = new();
    public OrdersData Orders { get; init; } = new();
    public MooringData Mooring { get; init; } = new();
    public FuelData Fuel { get; init; } = new();
    public SurveyFeeData Survey { get; init; } = new();
    public CloseCallsData CloseCalls { get; init; } = new();

    public sealed record StartData
    {
        public int StartingBalanceNok { get; init; }
        public int LaunchDeadlineDay { get; init; }
        public int LaunchMinutes { get; init; }
    }

    public sealed record EmergencyLiftData
    {
        public int Nok { get; init; }
        public int Days { get; init; }
    }

    public sealed record CalendarData
    {
        public int EveningMinutes { get; init; } = int.MaxValue;
        public int VacationDaysPerYear { get; init; } = int.MaxValue;
        public int PayNok { get; init; }
        public int WorkingDaysPerMonth { get; init; } = 21;
        public int UnpaidLeaveRoundTo { get; init; } = 50;
        /// <summary>Optional explicit override of the derived day's pay. While present it wins (design note 010, second pass B deletes it).</summary>
        public int? UnpaidLeaveNok { get; init; }
        public int OverdraftLimitNok { get; init; }
        public double InterestPerMonth { get; init; }
        public IReadOnlyList<BillRule> Bills { get; init; } = [];

        /// <summary>A day of unpaid leave: the override, else pay / working days a month, to the nearest <see cref="UnpaidLeaveRoundTo"/>.</summary>
        public int DerivedUnpaidLeaveNok() =>
            UnpaidLeaveNok ?? (WorkingDaysPerMonth <= 0 || UnpaidLeaveRoundTo <= 0
                ? 0
                : (int)Math.Round((double)PayNok / WorkingDaysPerMonth / UnpaidLeaveRoundTo, MidpointRounding.AwayFromZero) * UnpaidLeaveRoundTo);
    }

    public sealed record YardData
    {
        public int LiftNok { get; init; }
        public int StorageNok { get; init; }
        public int ChangeFeeNok { get; init; }
        public int WashNok { get; init; }
    }

    public sealed record TowData { public int Nok { get; init; } }

    public sealed record OrdersData { public IReadOnlyList<int> LeadDays { get; init; } = [0, 0]; }

    public sealed record MooringData { public int PartedRepairNok { get; init; } }

    public sealed record FuelData { public int DieselNokPerLitre { get; init; } }

    public sealed record SurveyFeeData { public int NokPerFoot { get; init; } }

    public sealed record CloseCallsData { public int LetterNok { get; init; } }
}
