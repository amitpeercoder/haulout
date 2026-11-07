using Haulout.Sim.Components;
using Haulout.Sim.Time;

var clock = new GameClock(90); // April 1st: launch is close.
var impeller = new Component("impeller", "Raw-water impeller", 0.4,
    WearRate.CalendarYears(2, perUseHour: 1.0 / 200));

Console.WriteLine($"Haulout — day {clock.Day}, {clock.Season}");
Console.WriteLine($"{impeller.Name}: condition {impeller.Condition:P0}");
Console.WriteLine("The boat was fine last year.");
