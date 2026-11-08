namespace Haulout.Sim.Weather;

/// <summary>
/// One day of weather. WindForce is Beaufort 0-9; WindFrom an 8-point direction word. DaylightHours is the length of the day (24: never dark),
/// FogUntilHour the hour of the day the morning fog lifts (0: no fog, 24: fog all day). Both are design note 010; a hand-made series keeps the defaults.
/// </summary>
public sealed record WeatherDay(
    int Day, int WindForce, string WindFrom, double RainMm, double TempC, double NightC,
    double DaylightHours = 24, int FogUntilHour = 0);
