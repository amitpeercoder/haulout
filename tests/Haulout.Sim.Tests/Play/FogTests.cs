using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Haulout.Sim.Data;
using Haulout.Sim.Time;
using Haulout.Sim.Weather;

namespace Haulout.Sim.Tests.Play;

/// <summary>Fog is emergent from the weather (design note 010, second pass D): humidity, dew point, sea temperature, the two fogs and their words.</summary>
public class FogTests
{
    private static readonly FogRules Rules = Climate.Default.FogRule;

    /// <summary>A day for the fog rule: 14 degrees by day, 8 at night, calm, dry, the dew point and the sea as given.</summary>
    private static WeatherDay Day(double dew, double sea, int wind = 2, double rain = 0, double temp = 14, double night = 8) =>
        new(0, wind, "west", rain, temp, night, DewPointC: dew, SeaTempC: sea, Humidity: 80);

    // ------------------------------------------------------------------ the dew point and the sea

    [Fact]
    public void The_dew_point_is_the_Magnus_value()
    {
        Assert.Equal(9.3, WeatherSeries.DewPoint(20, 50), 1);   // the usual worked example: 20 C at 50 percent
        Assert.Equal(20, WeatherSeries.DewPoint(20, 100), 6);   // saturated air: the dew point is the air temperature
        Assert.Equal(0, WeatherSeries.DewPoint(0, 100), 6);
        Assert.True(WeatherSeries.DewPoint(15, 60) < WeatherSeries.DewPoint(15, 80));
    }

    [Fact]
    public void The_sea_follows_the_month_curve_lags_the_air_and_is_clamped_to_the_curve_plus_or_minus_the_clamp()
    {
        var rule = Climate.Default.SeaRule;
        Assert.Equal(0, WeatherSeries.SeaAnomaly(0, rule));
        Assert.Equal(rule.AnomalyGain * 1.0, WeatherSeries.SeaAnomaly(1.0, rule), 9);
        Assert.Equal(rule.AnomalyClampC, WeatherSeries.SeaAnomaly(100, rule));
        Assert.Equal(-rule.AnomalyClampC, WeatherSeries.SeaAnomaly(-100, rule));

        var months = Climate.Default.Months;
        int coldest = months.Select((m, i) => (m.SeaTempC, i)).MinBy(x => x.SeaTempC).i;
        int warmest = months.Select((m, i) => (m.SeaTempC, i)).MaxBy(x => x.SeaTempC).i;
        Assert.Equal(2, coldest);   // March: the sea is coldest well after the air is
        Assert.Equal(7, warmest);   // August: and warmest well after it
        int airColdest = months.Select((m, i) => (m.TempC[0] + m.TempC[1], i)).MinBy(x => x.Item1).i;
        Assert.True(coldest > airColdest);

        for (int seed = 1; seed <= 20; seed++)
        {
            var s = WeatherSeries.Generate(seed);
            for (int d = 0; d <= 720; d++)
            {
                var curve = CurveSea(d);
                Assert.InRange(s[d].SeaTempC, curve - rule.AnomalyClampC - 0.06, curve + rule.AnomalyClampC + 0.06);
            }
        }
    }

    /// <summary>The month curve of the sea at a day, interpolated between month centres (the same rule as the other curves).</summary>
    private static double CurveSea(int day)
    {
        var clock = new GameClock(day);
        var pos = (clock.DayOfYear + 0.5) / GameClock.DaysPerMonth - 0.5;
        int i = (int)Math.Floor(pos);
        double f = pos - i;
        var m = Climate.Default.Months;
        return m[(i + 12) % 12].SeaTempC + (m[(i + 13) % 12].SeaTempC - m[(i + 12) % 12].SeaTempC) * f;
    }

    [Fact]
    public void The_day_carries_its_humidity_in_the_months_band_raised_when_it_rains_and_the_dew_point_is_Magnus_of_the_mean_air()
    {
        var wetAdd = Climate.Default.HumidityRule.WetAddPercent;
        for (int seed = 1; seed <= 5; seed++)
        {
            var s = WeatherSeries.Generate(seed);
            for (int d = 0; d <= 720; d++)
            {
                var w = s[d];
                var m = Climate.Default.Months[new GameClock(d).Month];
                var near = Climate.Default.Months.Select(x => x.Humidity!).ToList();
                double lo = near.Min(b => b[0]), hi = near.Max(b => b[1]);
                Assert.InRange(w.Humidity, lo - 0.05, Math.Min(100, hi + wetAdd) + 0.05);
                Assert.Equal(WeatherSeries.DewPoint((w.TempC + w.NightC) / 2, w.Humidity), w.DewPointC, 1);
                Assert.True(w.DewPointC < (w.TempC + w.NightC) / 2 + 0.06);
            }
        }
        // A wet day is more humid than the dry days around it, on average.
        double wet = 0, dry = 0; int nw = 0, nd = 0;
        var series = WeatherSeries.Generate(3);
        for (int d = 0; d <= 720; d++) { if (series[d].RainMm > 0) { wet += series[d].Humidity; nw++; } else { dry += series[d].Humidity; nd++; } }
        Assert.True(wet / nw > dry / nd + 3);
    }

    [Fact]
    public void The_air_runs_from_the_night_minimum_at_five_to_the_days_temperature_at_three_in_the_afternoon_and_back()
    {
        var d = Day(10, 10, temp: 16, night: 6);
        Assert.Equal(6, d.AirTempAt(5), 9);
        Assert.Equal(16, d.AirTempAt(15), 9);
        Assert.Equal(11, d.AirTempAt(10), 9);
        for (int h = 5; h < 15; h++) Assert.True(d.AirTempAt(h) < d.AirTempAt(h + 1));
        for (int h = 15; h < 29; h++) Assert.True(d.AirTempAt(h) > d.AirTempAt(h + 1));
        Assert.Equal(d.AirTempAt(29), d.AirTempAt(5), 9);
        Assert.Equal(d.AirTempAt(2), d.AirTempAt(26), 9);   // the small hours belong to yesterday's fall
    }

    // ------------------------------------------------------------------ sea fog

    [Fact]
    public void Sea_fog_forms_when_the_dew_point_is_within_the_margin_of_the_sea_and_not_a_hair_below()
    {
        double margin = Rules.MarginC;
        var at = WeatherSeries.ApplyFog(Day(10 - margin, 10), Rules);
        Assert.Equal(FogKind.Sea, at.Kind);
        Assert.Equal(0, at.FogStartHour);
        Assert.True(at.FogUntilHour > 0);
        Assert.Equal(FogKind.None, WeatherSeries.ApplyFog(Day(10 - margin - 0.1, 10, night: 12, temp: 16), Rules).Kind);
        Assert.Equal(FogKind.Sea, WeatherSeries.ApplyFog(Day(12, 10), Rules).Kind);   // dew above the sea: fog all the more
    }

    [Fact]
    public void Sea_fog_needs_a_wind_of_at_most_force_three()
    {
        Assert.Equal(FogKind.Sea, WeatherSeries.ApplyFog(Day(10, 10, wind: Rules.MaxWindForce), Rules).Kind);
        Assert.Equal(FogKind.None, WeatherSeries.ApplyFog(Day(10, 10, wind: Rules.MaxWindForce + 1), Rules).Kind);
    }

    [Fact]
    public void Sea_fog_lifts_at_the_first_hour_the_air_is_more_than_two_degrees_above_the_dew_point()
    {
        var d = WeatherSeries.ApplyFog(Day(10, 10, temp: 14, night: 8), Rules);   // 12 degrees is the line
        Assert.Equal(12, d.FogUntilHour);
        Assert.True(d.AirTempAt(11) <= 12 && d.AirTempAt(12) > 12);
        // A warmer day lifts it sooner; a cooler one later.
        var warm = WeatherSeries.ApplyFog(Day(10, 10, temp: 20, night: 8), Rules);
        Assert.True(warm.FogUntilHour < d.FogUntilHour);
        var cool = WeatherSeries.ApplyFog(Day(10, 10, temp: 13, night: 8), Rules);
        Assert.True(cool.FogUntilHour > d.FogUntilHour);
    }

    [Fact]
    public void Sea_fog_lasts_all_day_when_the_air_never_gets_that_warm_and_never_forms_when_the_night_is_already_that_warm()
    {
        var all = WeatherSeries.ApplyFog(Day(10, 10, temp: 11.5, night: 8), Rules);
        Assert.Equal((FogKind.Sea, 24), (all.Kind, all.FogUntilHour));
        Assert.Equal(FogKind.None, WeatherSeries.ApplyFog(Day(10, 10, temp: 20, night: 13), Rules).Kind);
    }

    // ------------------------------------------------------------------ harbour fog

    [Fact]
    public void Radiation_fog_lies_in_the_harbour_on_a_calm_dry_night_that_falls_to_the_dew_point_and_clears_by_mid_morning()
    {
        var d = WeatherSeries.ApplyFog(Day(5, 12, wind: 1, night: 4.5), Rules);   // the sea is far too warm for sea fog
        Assert.Equal((FogKind.Harbour, 0, Rules.RadiationClearByHour), (d.Kind, d.FogStartHour, d.FogUntilHour));
        Assert.Equal(FogKind.None, WeatherSeries.ApplyFog(Day(5, 12, wind: 1, night: 5.5), Rules).Kind);              // the night stays above the dew point
        Assert.Equal(FogKind.None, WeatherSeries.ApplyFog(Day(5, 12, wind: Rules.RadiationMaxWindForce + 1, night: 4.5), Rules).Kind);
        Assert.Equal(FogKind.None, WeatherSeries.ApplyFog(Day(5, 12, wind: 1, night: 4.5, rain: 2), Rules).Kind);
    }

    [Fact]
    public void Sea_fog_wins_when_both_would_form()
    {
        var d = WeatherSeries.ApplyFog(Day(10, 10, wind: 1, night: 8), Rules);
        Assert.Equal(FogKind.Sea, d.Kind);
    }

    [Fact]
    public void Harbour_fog_counts_in_port_and_in_the_first_hour_of_a_passage_and_sea_fog_counts_all_the_way()
    {
        var harbour = WeatherSeries.ApplyFog(Day(5, 12, wind: 1, night: 4.5), Rules);
        Assert.True(WeatherWords.IsFoggy(harbour, 7));
        Assert.True(WeatherWords.IsFoggyUnderway(harbour, 7, 30));
        Assert.False(WeatherWords.IsFoggyUnderway(harbour, 8, 90));
        Assert.False(WeatherWords.IsFoggy(harbour, 11));
        var sea = WeatherSeries.ApplyFog(Day(10, 10), Rules);
        Assert.True(WeatherWords.IsFoggyUnderway(sea, 8, 300));
    }

    // ------------------------------------------------------------------ the series

    [Fact]
    public void The_same_seed_gives_the_same_days_and_the_new_fields_are_on_their_own_stream()
    {
        var a = WeatherSeries.Generate(4);
        var b = WeatherSeries.Generate(4);
        for (int d = 0; d <= 1080; d++) Assert.Equal(a[d], b[d]);
        Assert.Contains(Enumerable.Range(0, 1081), d => a[d].Kind == FogKind.Sea);
        Assert.Contains(Enumerable.Range(0, 1081), d => a[d].Kind == FogKind.Harbour);
        // Another humidity does not move the main stream (the golden hash of SeasonTests pins that for the shipped data too).
        var drier = Climate.Load(System.Text.RegularExpressions.Regex.Replace(ShippedWeatherJson(), "\"humidity\": \\[[0-9]+, [0-9]+\\]", "\"humidity\": [30, 40]"));
        var dry = WeatherSeries.Generate(4, drier);
        for (int d = 0; d <= 720; d++)
            Assert.Equal((a[d].WindForce, a[d].WindFrom, a[d].RainMm, a[d].TempC, a[d].NightC), (dry[d].WindForce, dry[d].WindFrom, dry[d].RainMm, dry[d].TempC, dry[d].NightC));
        Assert.DoesNotContain(Enumerable.Range(0, 721), d => dry[d].Kind == FogKind.Sea);
    }

    private static string ShippedWeatherJson()
    {
        using var stream = typeof(Climate).Assembly.GetManifestResourceStream("Haulout.Sim.Data.weather.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Fog_days_are_early_summer_things_a_few_a_month_and_each_one_follows_the_rule()
    {
        var sea = new int[12];
        int seeds = 20;
        var rule = Climate.Default.FogRule;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var s = WeatherSeries.Generate(seed);
            for (int d = 0; d < 360; d++)
            {
                var w = s[d];
                if (w.Kind == FogKind.Sea)
                {
                    sea[new GameClock(d).Month]++;
                    Assert.True(w.WindForce <= rule.MaxWindForce && w.DewPointC >= w.SeaTempC - rule.MarginC);
                    Assert.Equal(0, w.FogStartHour);
                    Assert.True(w.FogUntilHour is > 5 and <= 24);
                }
                else if (w.Kind == FogKind.Harbour)
                {
                    Assert.True(w.RainMm <= 0 && w.WindForce <= rule.RadiationMaxWindForce && w.NightC <= w.DewPointC);
                    Assert.Equal(rule.RadiationClearByHour, w.FogUntilHour);
                }
                else Assert.Equal(0, w.FogUntilHour);
            }
        }
        double PerSeed(int m) => sea[m] / (double)seeds;
        Assert.InRange(PerSeed(5), 2.0, 7.0);   // June
        Assert.InRange(PerSeed(4), 1.0, 6.0);   // May
        Assert.True(PerSeed(5) > PerSeed(1) && PerSeed(5) > PerSeed(10), "sea fog is a summer thing, not a winter one");
    }

    [Fact]
    public void The_weather_series_new_fields_are_pinned_by_a_second_golden_hash()
    {
        // Taken once, when the second pass of design note 010 (D) made fog emergent from the weather. The first golden (SeasonTests) pins the
        // wind, rain and temperature streams and has not moved; this one pins the humidity stream, the dew point, the sea temperature and the fog.
        var all = new StringBuilder();
        for (int seed = 1; seed <= 20; seed++)
        {
            var s = WeatherSeries.Generate(seed);
            for (int d = 0; d <= 720; d++)
            {
                var w = s[d];
                all.Append(string.Create(CultureInfo.InvariantCulture, $"{seed},{d},{w.Humidity:R},{w.DewPointC:R},{w.SeaTempC:R},{w.FogStartHour},{w.FogUntilHour},{w.Kind};"));
            }
        }
        Assert.Equal("A4BA12AEE2080DB0FE9ECC7E82DDDB3C757566E5B9447273AD1F9112550E3374", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString()))));
    }

    // ------------------------------------------------------------------ the words

    [Fact]
    public void The_report_names_the_dew_point_and_the_sea_the_way_a_sailor_would()
    {
        Assert.Equal("Dew point 11, sea 10: fog likely in the morning.", WeatherWords.SkyLine(Day(11, 10)));
        Assert.Equal("Dew point 6, sea 10: no fog in that.", WeatherWords.SkyLine(Day(6, 10)));
        Assert.Equal("Dew point 8, sea 10: fog unlikely.", WeatherWords.SkyLine(Day(8, 10)));
        Assert.Equal("Dew point 11, sea 10: fog would form, but the wind will keep it off.", WeatherWords.SkyLine(Day(11, 10, wind: 5)));
        var harbour = WeatherSeries.ApplyFog(Day(5, 12, wind: 1, night: 4.5), Rules);
        Assert.Equal("Dew point 5, sea 12: no sea fog, but air near its dew point on a still night: mist in the harbour at dawn.", WeatherWords.SkyLine(harbour));
        Assert.Equal("", WeatherWords.SkyLine(new WeatherDay(0, 3, "west", 0, 8, 3)));   // a hand-made day has nothing to read
    }

    [Fact]
    public void The_bands_of_the_words_begin_where_the_fog_rule_does()
    {
        var bands = Rules.Words.Bands;
        string likely = Band.Pick(bands, -Rules.MarginC);
        Assert.Equal("fog likely in the morning", likely);
        Assert.NotEqual(likely, Band.Pick(bands, -Rules.MarginC - 0.01));
    }

    [Fact]
    public void The_description_adds_the_sky_when_asked_tells_fog_in_progress_as_fog_and_after_it_lifts_gives_the_numbers_alone()
    {
        var d = WeatherSeries.ApplyFog(Day(10, 10, temp: 14, night: 8), Rules);   // lifts at 12
        Assert.DoesNotContain("Dew point", WeatherWords.Describe(d));
        Assert.EndsWith("Dew point 10, sea 10: fog likely in the morning.", WeatherWords.Describe(d, null, withSky: true));
        var morning = WeatherWords.Describe(d, 9, withSky: true);
        Assert.EndsWith("Dew point 10, sea 10. Fog over the water.", morning);
        Assert.EndsWith("Dew point 10, sea 10.", WeatherWords.Describe(d, 14, withSky: true));
        var harbour = WeatherSeries.ApplyFog(Day(5, 12, wind: 1, night: 4.5), Rules);
        Assert.EndsWith("Mist lies over the harbour.", WeatherWords.Describe(harbour, 7, withSky: true));
        // Under way: told in the first hour out, never after.
        Assert.EndsWith("Mist lies over the harbour.", WeatherWords.Describe(harbour, 7, withSky: true, minutesOut: 30));
        var later = WeatherWords.Describe(harbour, 8, withSky: true, minutesOut: 90);
        Assert.DoesNotContain("Mist lies", later);
        Assert.EndsWith("Dew point 5, sea 12.", later);
        // Sea fog is told at sea all the way.
        Assert.EndsWith("Fog over the water.", WeatherWords.Describe(d, 9, withSky: true, minutesOut: 300));
    }

    [Fact]
    public void The_forecast_names_fog_only_as_a_morning_fog_all_day_or_harbour_mist_never_the_hour_it_lifts()
    {
        var d = WeatherSeries.ApplyFog(Day(10, 10), Rules);
        Assert.Equal("morning fog", WeatherWords.FogWord(d));
        Assert.Equal("fog all day", WeatherWords.FogWord(d with { FogUntilHour = 24 }));
        Assert.Equal("harbour mist", WeatherWords.FogWord(WeatherSeries.ApplyFog(Day(5, 12, wind: 1, night: 4.5), Rules)));
        Assert.Equal("", WeatherWords.FogWord(Day(5, 12)));
    }

    // ------------------------------------------------------------------ the data

    [Fact]
    public void The_boat_carries_the_fog_close_calls_and_a_ferry_report_in_its_reputation()
    {
        var boat = BoatLoader.LoadSloop28();
        var fog = boat.CloseCalls.Causes["fog"];
        Assert.Equal((0.1, 0.3), (fog.ChancePerHour, fog.ReportChance));
        Assert.True(fog.Narrations.Count >= 3);
        Assert.Contains(fog.Narrations, n => n.Ferry);
        Assert.Contains(fog.Narrations, n => !n.Ferry);
        Assert.NotEmpty(fog.ReportLine);
        Assert.Equal(-1, boat.Reputation.FerryReport);
    }

    [Fact]
    public void The_old_fog_knobs_are_gone_from_the_weather_file()
    {
        var json = ShippedWeatherJson();
        Assert.DoesNotContain("fogChance", json);
        Assert.DoesNotContain("fogAllDayFraction", json);
        Assert.DoesNotContain("fogLiftsHour", json);
    }
}
