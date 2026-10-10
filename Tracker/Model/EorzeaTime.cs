using System;

namespace EurekaAggro.Tracker.Model;

/// <summary>
/// Calculations and conversion utilities for Eorzea Time (ET) and Earth Time (LT).
/// In FFXIV, 1 Eorzea day equals 70 Earth minutes (multiplier: 144 / 7).
/// </summary>
public class EorzeaTime
{
    public const long EightHours = 8 * 175 * 1000;
    public const double Multiplier = 144D / 7D;
    public static readonly DateTime Zero = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public DateTime EorzeaDateTime { get; set; }

    public EorzeaTime(DateTime dateTime)
    {
        EorzeaDateTime = dateTime;
    }

    public static EorzeaTime Now => ToEorzeaTime(DateTime.Now);

    public static EorzeaTime ToEorzeaTime(DateTime dateTime)
    {
        long epochTicks = dateTime.ToUniversalTime().Ticks - Zero.Ticks;
        long eorzeaTicks = (long)Math.Round(epochTicks * Multiplier);
        return new EorzeaTime(new DateTime(eorzeaTicks));
    }

    public DateTime ToEarthTime()
    {
        var epochTicks = (long)Math.Round(EorzeaDateTime.Ticks / Multiplier);
        var earthTicks = epochTicks + Zero.Ticks;
        return new DateTime(earthTicks, DateTimeKind.Utc);
    }

    public DateTime ToLocalEarthTime() => ToEarthTime().ToLocalTime();

    public static DateTime GetNearestEarthInterval(DateTime dateTime)
    {
        long epochTicks = new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
        var result = epochTicks - (epochTicks % EightHours);
        return Zero + TimeSpan.FromSeconds(result / 1000);
    }

    public TimeSpan TimeUntilDay()
    {
        DateTime nextDay;
        if (EorzeaDateTime.Hour < 6)
            nextDay = EorzeaDateTime.Date + new TimeSpan(6, 0, 0);
        else
            nextDay = EorzeaDateTime.Date + new TimeSpan(1, 6, 0, 0);

        return TimeSpan.FromTicks(Convert.ToInt64((nextDay - EorzeaDateTime).Ticks * 7D / 144D));
    }

    public TimeSpan TimeUntilNight()
    {
        DateTime nextNight;
        if (EorzeaDateTime.Hour < 19)
            nextNight = EorzeaDateTime.Date + new TimeSpan(19, 0, 0);
        else
            nextNight = EorzeaDateTime.Date + new TimeSpan(1, 19, 0, 0);

        return TimeSpan.FromTicks(Convert.ToInt64((nextNight - EorzeaDateTime).Ticks * 7D / 144D));
    }
}
