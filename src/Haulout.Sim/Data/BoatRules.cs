using Haulout.Sim.Faults;

namespace Haulout.Sim.Data;

/// <summary>The money and the calendar of the spring. All placeholders from the boat's data.</summary>
public sealed record Economy(int StartingBalanceNok, int LaunchDeadlineDay, int EmergencyLiftNok, int LaunchMinutes);

public enum SurveySeverity { Note, Attention, BeforeLaunch }

/// <summary>Cause: the surveyor names the fault. Investigate: the surveyor only points at the part. None: nothing wrong to find.</summary>
public enum SurveyFinding { Cause, Investigate, None }

/// <summary>Component and Fault are ids. Fault null: no fault behind the line.</summary>
public sealed record SurveyItemData(string Component, SurveySeverity Severity, string? Fault, SurveyFinding Finding, string Text);

public sealed record SurveyData(
    int NokPerFoot, int LengthFeet, int Days, string Surveyor,
    IReadOnlyList<string> Opening, IReadOnlyList<string> Closing, IReadOnlyList<SurveyItemData> Items)
{
    public int FeeNok => NokPerFoot * LengthFeet;
}

/// <summary>What poor DIY work turns into, per subsystem.</summary>
public sealed record PoorJobTemplate(
    string Name, string Reveal, string Consequence, int CostLaterNok, DiyRoute Diy, YardRoute Yard);

public sealed record DiyQuality(
    int StartSkill, int Threshold, double PoorJobChance, IReadOnlyDictionary<Components.Subsystem, PoorJobTemplate> PoorJobs);
