using System;
using System.Collections.Generic;
using System.Numerics;
using EurekaAggro.Tracker.Model;

namespace EurekaAggro.Tracker.Zones;

public class PagosTracker : IEurekaZoneTracker
{
    public ushort ZoneId => 2;
    public string ZoneName => "Pagos";
    public uint TerritoryId => 763;

    private readonly List<EurekaFate> fates;

    private static readonly (int, EurekaWeather)[] Weathers =
    {
        (10, EurekaWeather.FairSkies),
        (28, EurekaWeather.Fog),
        (46, EurekaWeather.HeatWaves),
        (64, EurekaWeather.Snow),
        (82, EurekaWeather.Thunder),
        (100, EurekaWeather.Blizzards),
    };

    public PagosTracker()
    {
        fates = new List<EurekaFate>
        {
            new(1351, 21, 763, 467, "Eternity", "The Snow Queen", "Queen", new Vector2(21.7f, 26.3f), "Yukinko", new Vector2(22.0f, 27.2f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Ice, EurekaElement.Ice, false, 20),
            new(1369, 22, 763, 467, "Cairn Blight 451", "Taxim", "Taxim", new Vector2(25.0f, 28.0f), "Demon of the Incunable", new Vector2(25.0f, 28.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Wind, true, 21),
            new(1353, 23, 763, 467, "Ash the Magic Dragon", "Ash Dragon", "Dragon", new Vector2(30.1f, 29.8f), "Blood Demon", new Vector2(30.1f, 29.8f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Fire, false, 22),
            new(1354, 24, 763, 467, "Conqueror Worm", "Glavoid", "Glavoid", new Vector2(32.8f, 27.3f), "Val Worm", new Vector2(32.8f, 27.3f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Earth, false, 23),
            new(1355, 25, 763, 467, "Melting Point", "Anapos", "Anapos", new Vector2(32.9f, 21.5f), "Snowmelt Sprite", new Vector2(32.9f, 21.5f), EurekaWeather.None, EurekaWeather.Fog, EurekaElement.Water, EurekaElement.Water, false, 24),
            new(1366, 26, 763, 467, "The Wobbler in Darkness", "Hakutaku", "Haku", new Vector2(28.9f, 22.3f), "Blubber Eyes", new Vector2(28.9f, 22.3f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Fire, false, 25),
            new(1357, 27, 763, 467, "Does It Have to Be a Snowman", "King Igloo", "Igloo", new Vector2(17.2f, 16.2f), "Huwasi", new Vector2(17.2f, 16.2f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Ice, EurekaElement.Ice, false, 26),
            new(1356, 28, 763, 467, "Disorder in the Court", "Asag", "Asag", new Vector2(10.5f, 11.0f), "Wandering Opken", new Vector2(10.5f, 11.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Lightning, EurekaElement.Lightning, false, 27),
            new(1352, 29, 763, 467, "Cows for Concern", "Surabhi", "Surabhi", new Vector2(10.0f, 20.0f), "Pagos Billygoat", new Vector2(10.0f, 20.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Earth, false, 28),
            new(1360, 30, 763, 467, "Morte Arthro", "King Arthro", "Arthro", new Vector2(8.7f, 15.4f), "Val Snipper", new Vector2(8.7f, 15.4f), EurekaWeather.Fog, EurekaWeather.None, EurekaElement.Water, EurekaElement.Water, false, 29),
            new(1358, 31, 763, 467, "Brothers", "Mindertaur/Eldertaur", "Brothers", new Vector2(13.9f, 18.7f), "Lab Minotaur", new Vector2(13.9f, 18.7f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Earth, EurekaElement.Wind, false, 30),
            new(1361, 32, 763, 467, "Apocalypse Cow", "Holy Cow", "Holy Cow", new Vector2(26.5f, 16.9f), "Elder Buffalo", new Vector2(26.5f, 16.9f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Water, EurekaElement.Wind, false, 31),
            new(1362, 33, 763, 467, "Third Impact", "Hadhayosh", "Behe", new Vector2(31.0f, 18.5f), "Lesser Void Dragon", new Vector2(31.0f, 18.5f), EurekaWeather.Thunder, EurekaWeather.None, EurekaElement.Lightning, EurekaElement.Lightning, false, 32),
            new(1359, 34, 763, 467, "Eye of Horus", "Horus", "Horus", new Vector2(26.0f, 20.2f), "Void Vouivre", new Vector2(26.0f, 20.2f), EurekaWeather.HeatWaves, EurekaWeather.None, EurekaElement.Fire, EurekaElement.Fire, false, 33),
            new(1363, 35, 763, 467, "Eye Scream for Ice Cream", "Arch Angra Mainyu", "Mainyu", new Vector2(24.0f, 25.0f), "Gawper", new Vector2(24.0f, 25.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Wind, EurekaElement.Wind, false, 34),
            new(1365, 36, 763, 467, "Cassie and the Copycats", "Copycat Cassie", "Cassie", new Vector2(22.3f, 14.3f), "Ameretat", new Vector2(21.0f, 14.5f), EurekaWeather.Blizzards, EurekaWeather.None, EurekaElement.Ice, EurekaElement.Ice, false, 35),
            new(1364, 37, 763, 467, "Louhi on Ice", "Louhi", "Louhi", new Vector2(36.0f, 19.0f), "Val Corpse", new Vector2(36.0f, 19.0f), EurekaWeather.None, EurekaWeather.None, EurekaElement.Ice, EurekaElement.Ice, true, 35),
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
