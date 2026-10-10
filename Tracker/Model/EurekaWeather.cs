using System;
using System.Collections.Generic;
using System.Linq;

namespace EurekaSuite.Tracker.Model;

/// <summary>
/// Weather conditions across all Eureka expedition zones.
/// </summary>
public enum EurekaWeather
{
    Gales,
    Showers,
    FairSkies,
    Snow,
    HeatWaves,
    Thunder,
    Blizzards,
    Fog,
    UmbralWind,
    Thunderstorms,
    Gloom,
    None,
}

/// <summary>
/// Elemental affinities for Eureka monsters and Notorious Monsters.
/// </summary>
public enum EurekaElement
{
    Fire,
    Ice,
    Wind,
    Earth,
    Lightning,
    Water,
    None,
}

public static class EurekaWeatherExtensions
{
    public static string ToFriendlyString(this EurekaWeather weather) => weather switch
    {
        EurekaWeather.Gales => "Gales",
        EurekaWeather.Showers => "Showers",
        EurekaWeather.FairSkies => "Fair Skies",
        EurekaWeather.Snow => "Snow",
        EurekaWeather.HeatWaves => "Heat Waves",
        EurekaWeather.Thunder => "Thunder",
        EurekaWeather.Blizzards => "Blizzards",
        EurekaWeather.Fog => "Fog",
        EurekaWeather.UmbralWind => "Umbral Wind",
        EurekaWeather.Thunderstorms => "Thunderstorms",
        EurekaWeather.Gloom => "Gloom",
        _ => "None",
    };

    public static string ToFriendlyString(this EurekaElement element) => element switch
    {
        EurekaElement.Fire => "Fire",
        EurekaElement.Ice => "Ice",
        EurekaElement.Wind => "Wind",
        EurekaElement.Earth => "Earth",
        EurekaElement.Lightning => "Lightning",
        EurekaElement.Water => "Water",
        _ => "None",
    };
}

/// <summary>
/// Calculates official FFXIV weather forecasts for Eureka zones based on Eorzea timestamps.
/// </summary>
public static class EorzeaWeather
{
    public static int CalculateTarget(DateTime dateTime)
    {
        var unix = (int)(dateTime - EorzeaTime.Zero).TotalSeconds;
        var bell = unix / 175;
        var increment = ((uint)(bell + 8 - (bell % 8))) % 24;

        var totalDays = (uint)(unix / 4200);
        var calcBase = (totalDays * 0x64) + increment;

        var step1 = (calcBase << 0xB) ^ calcBase;
        var step2 = (step1 >> 8) ^ step1;

        return (int)(step2 % 0x64);
    }

    public static EurekaWeather Forecast((int, EurekaWeather)[] weathers, int chance)
    {
        return weathers.Where(w => chance < w.Item1).Select(w => w.Item2).FirstOrDefault();
    }

    public static (EurekaWeather Weather, TimeSpan Timeleft) GetCurrentWeatherInfo((int, EurekaWeather)[] weathers)
    {
        int chance = CalculateTarget(DateTime.Now.ToUniversalTime());
        EurekaWeather weather = Forecast(weathers, chance);

        var timeNow = EorzeaTime.GetNearestEarthInterval(DateTime.Now);
        timeNow += TimeSpan.FromMilliseconds(EorzeaTime.EightHours);

        return (weather, timeNow.ToLocalTime() - DateTime.Now);
    }

    public static List<(EurekaWeather Weather, TimeSpan Time)> GetAllWeathers((int, EurekaWeather)[] weathers)
    {
        List<(EurekaWeather, TimeSpan)> results = new();
        foreach (var weather in weathers)
        {
            var nextInterval = EorzeaTime.GetNearestEarthInterval(DateTime.Now) + TimeSpan.FromMilliseconds(EorzeaTime.EightHours);
            while (true)
            {
                if (Forecast(weathers, CalculateTarget(nextInterval)) == weather.Item2)
                {
                    results.Add(new(weather.Item2, nextInterval.ToLocalTime() - DateTime.Now));
                    break;
                }

                nextInterval += TimeSpan.FromMilliseconds(EorzeaTime.EightHours);
            }
        }

        return results;
    }
}
