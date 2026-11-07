using System.Text.Json.Serialization;

namespace Haulout.Sim.Play;

/// <summary>Something the player does. Names are free text; the sim resolves them.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Look), "look")]
[JsonDerivedType(typeof(Open), "open")]
[JsonDerivedType(typeof(Inspect), "inspect")]
[JsonDerivedType(typeof(Measure), "measure")]
[JsonDerivedType(typeof(StartEngine), "start")]
[JsonDerivedType(typeof(Clean), "clean")]
[JsonDerivedType(typeof(Replace), "replace")]
[JsonDerivedType(typeof(Charge), "charge")]
[JsonDerivedType(typeof(Buy), "buy")]
[JsonDerivedType(typeof(CheckFilterBowl), "check-filter-bowl")]
[JsonDerivedType(typeof(CheckRawWaterFlow), "check-raw-water-flow")]
public abstract record Command;

public sealed record Look() : Command;
public sealed record Open(string Compartment) : Command;
public sealed record Inspect(string Component) : Command;
public sealed record Measure(string Component) : Command;
public sealed record StartEngine() : Command;
public sealed record Clean(string Component) : Command;
public sealed record Replace(string Component) : Command;
public sealed record Charge(string Component) : Command;
public sealed record Buy(string Item) : Command;
public sealed record CheckFilterBowl() : Command;
public sealed record CheckRawWaterFlow() : Command;

/// <summary>Something the player asks about. Costs no time.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Briefing), "briefing")]
[JsonDerivedType(typeof(Status), "status")]
[JsonDerivedType(typeof(Logbook), "logbook")]
[JsonDerivedType(typeof(Compartments), "compartments")]
[JsonDerivedType(typeof(Commands), "commands")]
[JsonDerivedType(typeof(Aboard), "aboard")]
[JsonDerivedType(typeof(ComponentActions), "component-actions")]
public abstract record Query;

public sealed record Briefing() : Query;
public sealed record Status() : Query;
public sealed record Logbook() : Query;
public sealed record Compartments() : Query;
public sealed record Commands() : Query;
/// <summary>What the player has opened and found so far, compartment by compartment.</summary>
public sealed record Aboard() : Query;
/// <summary>The verbs that make sense on a component the player has found, with time, cost and why one is unavailable.</summary>
public sealed record ComponentActions(string Component) : Query;
