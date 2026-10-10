using System;
using System.Collections.Generic;
using System.Numerics;
using EurekaAggro.Tracker.Model;

namespace EurekaAggro.Tracker.Zones;

public class PyrosTracker : IEurekaZoneTracker
{
    public ushort ZoneId => 3;
    public string ZoneName => "Pyros";
    public uint TerritoryId => 795;

    private readonly List<EurekaFate> fates;

    private static readonly (int, EurekaWeather)[] Weathers =
    {
        (10, EurekaWeather.FairSkies),
        (28, EurekaWeather.HeatWaves),
        (46, EurekaWeather.Thunder),
        (64, EurekaWeather.Blizzards),
        (82, EurekaWeather.UmbralWind),
        (100, EurekaWeather.Snow),
    };

    public PyrosTracker()
    {
        fates = new List<EurekaFate>
        {
            new(1388, 38, 795, 484, "Medias Res", "Leucosia", "Leucosia", new Vector2(27.0f, 26.0f), "Pyros Bhoot", new Vector2(27.0f, 26.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Water, EurekaElement.Ice, true, 35),
            new(1389, 39, 795, 484, "High Voltage", "Flauros", "Flauros", new Vector2(29.0f, 29.0f), "Thunderstorm Sprite", new Vector2(29.0f, 29.0f), EurekaWeather.None, EurekaWeather.Thunder, EurekaElement.Lightning, EurekaElement.Lightning, false, 36),
            new(1390, 40, 795, 484, "On the Nonexistent", "The Sophist", "Sophist", new Vector2(32.1f, 31.5f), "Pyros Apanda", new Vector2(32.1f, 31.5f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Wind, EurekaElement.Earth, false, 37),
            new(1391, 41, 795, 484, "Creepy Doll", "Graffiacane", "Doll", new Vector2(23.0f, 37.0f), "Valking", new Vector2(23.0f, 37.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Ice, EurekaElement.Lightning, false, 38),
            new(1392, 42, 795, 484, "Quiet, Please", "Askalaphos", "Owl", new Vector2(19.2f, 29.2f), "Overdue Tome", new Vector2(19.2f, 29.2f), EurekaWeather.UmbralWind, EurekaWeather.None, EurekaElement.Wind, EurekaElement.Earth, false, 39),
            new(1393, 43, 795, 484, "Up and Batym", "Grand Duke Batym", "Batym", new Vector2(17.8f, 14.1f), "Dark Troubadour", new Vector2(17.8f, 14.1f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Earth, true, 40),
            new(1394, 44, 795, 484, "Rondo Aetolus", "Aetolus", "Aetolus", new Vector2(10.1f, 14.2f), "Islandhander", new Vector2(10.1f, 14.2f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Lightning, EurekaElement.Wind, false, 41),
            new(1395, 45, 795, 484, "Scorchpion King", "Lesath", "Lesath", new Vector2(12.6f, 11.1f), "Bird Eater", new Vector2(12.6f, 11.1f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Wind, false, 42),
            new(1396, 46, 795, 484, "Burning Hunger", "Eldthurs", "Eldthurs", new Vector2(15.2f, 6.4f), "Pyros Crab", new Vector2(15.2f, 6.4f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Fire, false, 43),
            new(1397, 47, 795, 484, "Dry Iris", "Iris", "Iris", new Vector2(21.3f, 12.2f), "Northern Swallow", new Vector2(21.3f, 12.2f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Water, EurekaElement.Water, false, 44),
            new(1398, 48, 795, 484, "Thirty Whacks", "Lamebrix Strikebocks", "Lamebrix", new Vector2(21.8f, 8.4f), "Illuminati Escapee", new Vector2(21.8f, 8.4f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Lightning, false, 45),
            new(1399, 49, 795, 484, "Put Up Your Dux", "Dux", "Dux", new Vector2(27.4f, 8.9f), "Matanga Castaway", new Vector2(27.4f, 8.9f), EurekaWeather.Thunder, EurekaWeather.None, EurekaElement.Lightning, EurekaElement.Fire, false, 46),
            new(1400, 50, 795, 484, "You Do Know Jack", "Lumber Jack", "Jack", new Vector2(30.2f, 11.4f), "Pyros Treant", new Vector2(30.2f, 11.4f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Lightning, false, 47),
            new(1401, 51, 795, 484, "Mister Bright-eyes", "Glaukopis", "Glaukopis", new Vector2(32.0f, 15.2f), "Val Skatene", new Vector2(32.0f, 15.2f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Wind, false, 48),
            new(1402, 52, 795, 484, "Haunter of the Dark", "Ying-Yang", "YY", new Vector2(11.5f, 34.3f), "Pyros Hecteyes", new Vector2(11.5f, 34.3f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Water, EurekaElement.Water, false, 49),
            new(1403, 53, 795, 484, "Heavens' Warg", "Skoll", "Skoll", new Vector2(24.0f, 30.0f), "Pyros Shuck", new Vector2(16.0f, 36.8f), EurekaWeather.Blizzards, EurekaWeather.None, EurekaElement.Ice, EurekaElement.Earth, false, 50),
            new(1404, 54, 795, 484, "Lost Epic", "Penthesilea", "Penny", new Vector2(35.9f, 5.9f), "Val Bloodglider", new Vector2(33.6f, 8.2f), EurekaWeather.HeatWaves, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Fire, false, 50),
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
