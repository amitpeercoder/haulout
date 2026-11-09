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
    public RadioMoneyData Radio { get; init; } = new();
    public HoursData Hours { get; init; } = new();
    public SleepData Sleep { get; init; } = new();
    /// <summary>The club's course prices by ticket id (design note 012, step 2). The tickets themselves are in tickets.json.</summary>
    public Dictionary<string, int> Courses { get; init; } = [];
    /// <summary>The exam fee for a re-sit, by ticket id (design note 012, decision 8): paid at enrolling for the re-sit.</summary>
    public Dictionary<string, int> ExamFees { get; init; } = [];

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

    /// <summary>What a call made without the radio ticket costs: the harbourmaster's letter (design note 012, step 3).</summary>
    public sealed record RadioMoneyData { public int UnlicensedLetterNok { get; init; } }

    /// <summary>
    /// The shops' hours and the harbourmaster's night (design note 012). Times are "HH:MM" in the file and minutes of the day in
    /// <see cref="HoursRules"/>. A file with no <c>hours</c> block has no hours: shops are always open.
    /// </summary>
    public sealed record HoursData
    {
        public string? NightFrom { get; init; }
        public string HarbourmasterFirstCall { get; init; } = "08:00";
        public ShopHoursData Shop { get; init; } = new();

        public HoursRules ToRules() => NightFrom is null
            ? HoursRules.Default
            : new HoursRules
            {
                NightFromMinute = Minute(NightFrom),
                HarbourmasterFirstCallMinute = Minute(HarbourmasterFirstCall),
                Weekday = Open(Shop.Weekday),
                Saturday = Open(Shop.Saturday),
                Sunday = Open(Shop.Sunday),
            };

        private static OpeningHours Open(IReadOnlyList<string>? span) =>
            span is { Count: 2 } ? new OpeningHours(Minute(span[0]), Minute(span[1])) : OpeningHours.Shut;

        internal static int Minute(string hhmm)
        {
            var parts = hhmm.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h is < 0 or > 24 || m is < 0 or > 59)
                throw new InvalidOperationException($"economy.json: '{hhmm}' is not a time of day (HH:MM).");
            return h * 60 + m;
        }
    }

    public sealed record ShopHoursData
    {
        public IReadOnlyList<string>? Weekday { get; init; }
        public IReadOnlyList<string>? Saturday { get; init; }
        public IReadOnlyList<string>? Sunday { get; init; }
    }

    /// <summary>Sleep is a need, not a curfew (design note 012, Amit's answers). No block: no tiredness.</summary>
    public sealed record SleepData
    {
        public int TiredAtHours { get; init; }
        public int ExhaustedAtHours { get; init; }
        public double BotchFactorTired { get; init; } = 1;
        public double BotchFactorExhausted { get; init; } = 1;
        public string WakeHour { get; init; } = "08:00";
        public int SleepHours { get; init; }
        public int MinSleepHours { get; init; }
        /// <summary>The sleep command sleeps to the wake hour only when it is no further than this (step 1 follow-up a).</summary>
        public int MaxSleepHours { get; init; }
        /// <summary>How fast the hours awake grow under way at sea (0.5: you doze in the cockpit).</summary>
        public double AtSeaRate { get; init; }
        /// <summary>A passage longer than this makes the arrival a tired one.</summary>
        public int TiredArrivalHours { get; init; }

        public SleepRules ToRules() => TiredAtHours <= 0
            ? SleepRules.Default
            : new SleepRules
            {
                TiredAtMinutes = TiredAtHours * 60, ExhaustedAtMinutes = ExhaustedAtHours * 60,
                BotchFactorTired = BotchFactorTired, BotchFactorExhausted = BotchFactorExhausted,
                WakeMinute = HoursData.Minute(WakeHour), SleepMinutes = SleepHours * 60, MinSleepMinutes = MinSleepHours * 60,
                MaxSleepMinutes = MaxSleepHours > 0 ? MaxSleepHours * 60 : int.MaxValue, AtSeaRate = AtSeaRate, TiredArrivalMinutes = TiredArrivalHours * 60,
            };
    }
}
