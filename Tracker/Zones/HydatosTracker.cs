using System;
using System.Collections.Generic;
using System.Numerics;
using EurekaAggro.Tracker.Model;

namespace EurekaAggro.Tracker.Zones;

public class HydatosTracker : IEurekaZoneTracker
{
    public ushort ZoneId => 4;
    public string ZoneName => "Hydatos";
    public uint TerritoryId => 827;

    private readonly List<EurekaFate> fates;

    private static readonly (int, EurekaWeather)[] Weathers =
    {
        (12, EurekaWeather.FairSkies),
        (34, EurekaWeather.Showers),
        (56, EurekaWeather.Gloom),
        (78, EurekaWeather.Thunderstorms),
        (100, EurekaWeather.Snow),
    };

    public HydatosTracker()
    {
        fates = new List<EurekaFate>
        {
            new(1412, 55, 827, 515, "I Ink, Therefore I Am", "Khalamari", "Khalamari", new Vector2(11.0f, 25.3f), "Xzomit", new Vector2(11.0f, 25.3f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Water, EurekaElement.Water, false, 50),
            new(1413, 56, 827, 515, "From Tusk till Dawn", "Stegodon", "Stegodon", new Vector2(10.1f, 17.9f), "Hydatos Primelephas", new Vector2(11.1f, 16.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Earth, false, 51),
            new(1414, 57, 827, 515, "Bullheaded Berserker", "Molech", "Molech", new Vector2(7.0f, 21.0f), "Val Nullchu", new Vector2(7.0f, 21.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Ice, EurekaElement.Earth, false, 52),
            new(1415, 58, 827, 515, "Mad, Bad, and Fabulous to Know", "Piasa", "Piasa", new Vector2(7.0f, 14.0f), "Vivid Gastornis", new Vector2(7.0f, 14.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Wind, EurekaElement.Wind, false, 53),
            new(1416, 59, 827, 515, "Fearful Symmetry", "Frostmane", "Frostmane", new Vector2(7.9f, 26.1f), "Northern Tiger", new Vector2(6.4f, 26.5f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Earth, false, 54),
            new(1417, 60, 827, 515, "Crawling Chaos", "Daphne", "Daphne", new Vector2(25.6f, 16.2f), "Dark Void Monk", new Vector2(25.6f, 16.2f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Water, EurekaElement.Water, false, 55),
            new(1418, 61, 827, 515, "Duty-free", "King Goldemar", "Golde", new Vector2(28.9f, 23.6f), "Hydatos Wraith", new Vector2(28.0f, 23.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Lightning, EurekaElement.Lightning, true, 56),
            new(1419, 62, 827, 515, "Leukwarm Reception", "Leuke", "Leuke", new Vector2(37.0f, 26.0f), "Tigerhawk", new Vector2(37.2f, 27.8f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Wind, false, 57),
            new(1420, 63, 827, 515, "Robber Barong", "Barong", "Barong", new Vector2(32.0f, 24.0f), "Laboratory Lion", new Vector2(34.6f, 24.9f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Earth, false, 58),
            new(1421, 64, 827, 515, "Stone-cold Killer", "Ceto", "Ceto", new Vector2(36.4f, 13.4f), "Hydatos Delphyne", new Vector2(36.4f, 13.4f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Water, EurekaElement.Fire, false, 59),
            new(1423, 65, 827, 515, "Crystalline Provenance", "Provenance Watcher", "PW", new Vector2(32.7f, 19.6f), "Crystal Claw", new Vector2(32.5f, 21.6f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Fire, false, 60),
        };
    }

    public List<EurekaFate> GetFates() => fates;

    public (EurekaWeather Weather, TimeSpan Timeleft) GetCurrentWeatherInfo() =>
        EorzeaWeather.GetCurrentWeatherInfo(Weathers);

    public List<(EurekaWeather Weather, TimeSpan Time)> GetAllNextWeatherTime() =>
        EorzeaWeather.GetAllWeathers(Weathers);

    public void SetPopTimes(Dictionary<ushort, long> keyValuePairs)
    {
        foreach (var fate in fates)
        {
            if (keyValuePairs.TryGetValue(fate.TrackerId, out long time))
                fate.SetKill(time);
            else
                fate.ResetKill();
        }
    }
}
