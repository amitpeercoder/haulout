namespace Haulout.Sim.Weather;

/// <summary>Which fog the day has (design note 010, second pass D): none, sea fog (advection: it lies on the water and in the harbour), or radiation fog in the harbour only.</summary>
public enum FogKind { None, Sea, Harbour }

/// <summary>
/// One day of weather. WindForce is Beaufort 0-9; WindFrom an 8-point direction word. DaylightHours is the length of the day (24: never dark).
/// Fog lies from FogStartHour to FogUntilHour (the hour it lifts; 0: no fog, 24: fog all day). Humidity (percent), DewPointC and SeaTempC are the
/// day's sky and the sea under it (NaN in a hand-made day: nothing to read); Kind says which fog it is (a hand-made day with a lifting hour and no
/// kind is taken as sea fog). A hand-made series keeps the defaults.
/// </summary>
public sealed record WeatherDay(
    int Day, int WindForce, string WindFrom, double RainMm, double TempC, double NightC,
    double DaylightHours = 24, int FogUntilHour = 0,
    double Humidity = double.NaN, double DewPointC = double.NaN, double SeaTempC = double.NaN,
    int FogStartHour = 0, FogKind Kind = FogKind.None)
{
    /// <summary>The day has a dew point and a sea temperature to read.</summary>
    public bool HasSky => !double.IsNaN(DewPointC) && !double.IsNaN(SeaTempC);

    /// <summary>The fog lies in the harbour only (radiation fog): it does not count at sea after the first hour of a passage.</summary>
    public bool HarbourOnly => Kind == FogKind.Harbour;

    /// <summary>
    /// The air temperature at an hour of the day (0 to 24, fractional): the night minimum at 05:00 rising by a half cosine to the day's
    /// TempC at 15:00, then falling by a half cosine back to the minimum at 05:00 the next morning.
    /// </summary>
    public double AirTempAt(double hour)
    {
        double h = ((hour % 24) + 24) % 24;
        if (h < 5) h += 24;
        double range = TempC - NightC;
        if (h <= 15) return NightC + range * (1 - Math.Cos(Math.PI * (h - 5) / 10)) / 2;
        return TempC - range * (1 - Math.Cos(Math.PI * (h - 15) / 14)) / 2;
    }
}
