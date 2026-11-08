namespace Haulout.Sim.Weather;

/// <summary>One day of weather. WindForce is Beaufort 0-9; WindFrom an 8-point direction word.</summary>
public sealed record WeatherDay(int Day, int WindForce, string WindFrom, double RainMm, double TempC, double NightC);
